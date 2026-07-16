using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public interface IStockVideoService
{
    Task<IReadOnlyList<DownloadedVideoClip>> DownloadVideosAsync(
        IReadOnlyList<VoiceSegment> segments,
        string outputDirectory,
        ShortGeneratorOptions options,
        IProgress<ShortGenerationProgress>? progress = null,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default);
}

public sealed class StockVideoService : IStockVideoService
{
    private const int MaxTransientAttempts = 3;
    private const double MinimumSemanticFitScore = 0.18;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public StockVideoService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<DownloadedVideoClip>> DownloadVideosAsync(
        IReadOnlyList<VoiceSegment> segments,
        string outputDirectory,
        ShortGeneratorOptions options,
        IProgress<ShortGenerationProgress>? progress = null,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var pexelsApiKey = ResolvePexelsApiKey(options);
        var pixabayApiKey = ResolvePixabayApiKey(options);
        if (string.IsNullOrWhiteSpace(pexelsApiKey) && string.IsNullOrWhiteSpace(pixabayApiKey))
        {
            throw new InvalidOperationException("Brakuje klucza Pexels API albo Pixabay API. Wpisz jeden z nich w oknie programu albo ustaw zmienna PEXELS_API_KEY lub PIXABAY_API_KEY.");
        }

        Directory.CreateDirectory(outputDirectory);
        logger?.Info($"Downloading stock videos. Segments={segments.Count}; OutputDirectory={outputDirectory}; PexelsConfigured={!string.IsNullOrWhiteSpace(pexelsApiKey)}; PixabayConfigured={!string.IsNullOrWhiteSpace(pixabayApiKey)}");

        var clips = new List<DownloadedVideoClip>();
        var usedVideoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            progress?.Report(new ShortGenerationProgress(
                45 + i * 15 / Math.Max(segments.Count, 1),
                $"Pobieram klip stock: {segment.SearchPhrase}"));

            var selection = await SearchBestVideoAsync(
                pexelsApiKey,
                pixabayApiKey,
                segment,
                $"pexels-search-{segment.Index:00}.json",
                usedVideoUrls,
                logger,
                cancellationToken);
            logger?.Info($"Selected {selection.Provider} video segment={segment.Index} rank={selection.Rank} phrase={segment.SearchPhrase} url={selection.SourceUrl} file={selection.DownloadUrl} reason={selection.Reason}");

            var filePath = Path.Combine(
                outputDirectory,
                $"{segment.Index:00}_{FileNameSanitizer.ForStockVideoFile(segment.SearchPhrase)}.mp4");

            await DownloadFileAsync(
                selection.Provider,
                selection.Provider.Equals("Pexels", StringComparison.OrdinalIgnoreCase) ? pexelsApiKey : null,
                selection.DownloadUrl,
                filePath,
                logger,
                cancellationToken);
            logger?.Info($"Downloaded {selection.Provider} clip segment={segment.Index} path={filePath}");
            usedVideoUrls.Add(selection.SourceUrl);

            clips.Add(new DownloadedVideoClip
            {
                SegmentIndex = segment.Index,
                SearchPhrase = segment.SearchPhrase,
                SearchPhrases = segment.SearchPhrases.Count == 0 ? [segment.SearchPhrase] : segment.SearchPhrases,
                AvoidVisuals = segment.AvoidVisuals,
                VisualDescription = segment.VisualDescription,
                FilePath = filePath,
                PexelsUrl = selection.SourceUrl,
                ThumbnailUrl = selection.ThumbnailUrl,
                PexelsRank = selection.Rank,
                CandidateCount = selection.CandidateCount,
                ContentScore = selection.Score,
                SelectionReason = selection.Reason,
                AuthorName = selection.AuthorName,
                AuthorUrl = selection.AuthorUrl
            });
        }

        return clips;
    }

    private static string ResolvePexelsApiKey(ShortGeneratorOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PexelsApiKey))
        {
            return options.PexelsApiKey.Trim();
        }

        var envKey = Environment.GetEnvironmentVariable("PEXELS_API_KEY")
            ?? Environment.GetEnvironmentVariable("PEXELS_API_KEY", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("PEXELS_API_KEY", EnvironmentVariableTarget.Machine);
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            return envKey.Trim();
        }

        return string.Empty;
    }

    private static string ResolvePixabayApiKey(ShortGeneratorOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PixabayApiKey))
        {
            return options.PixabayApiKey.Trim();
        }

        var envKey = Environment.GetEnvironmentVariable("PIXABAY_API_KEY")
            ?? Environment.GetEnvironmentVariable("PIXABAY_API_KEY", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("PIXABAY_API_KEY", EnvironmentVariableTarget.Machine);
        return string.IsNullOrWhiteSpace(envKey) ? string.Empty : envKey.Trim();
    }

    private async Task<StockVideoSelection> SearchBestVideoAsync(
        string pexelsApiKey,
        string pixabayApiKey,
        VoiceSegment segment,
        string debugFileName,
        HashSet<string> usedVideoUrls,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        Exception? pexelsException = null;
        if (!string.IsNullOrWhiteSpace(pexelsApiKey))
        {
            try
            {
                return await SearchPexelsVideoAsync(
                    pexelsApiKey,
                    segment,
                    debugFileName,
                    usedVideoUrls,
                    logger,
                    cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                pexelsException = ex;
                logger?.Warning($"Pexels did not provide a usable clip for segment={segment.Index}. Trying Pixabay fallback. Error={ex.Message}");
            }
        }
        else
        {
            logger?.Warning($"Pexels API key is not configured. Trying Pixabay fallback for segment={segment.Index}.");
        }

        if (!string.IsNullOrWhiteSpace(pixabayApiKey))
        {
            try
            {
                return await SearchPixabayVideoAsync(
                    pixabayApiKey,
                    segment,
                    $"pixabay-search-{segment.Index:00}.json",
                    usedVideoUrls,
                    logger,
                    cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && pexelsException is not null)
            {
                throw new InvalidOperationException(
                    $"Nie udalo sie pobrac klipu ani z Pexels, ani z Pixabay. Pexels: {pexelsException.Message} Pixabay: {ex.Message}",
                    ex);
            }
        }

        if (pexelsException is not null)
        {
            throw new InvalidOperationException(
                $"{pexelsException.Message}{Environment.NewLine}Pixabay fallback nie jest skonfigurowany. Wpisz klucz Pixabay API albo ustaw zmienna PIXABAY_API_KEY.",
                pexelsException);
        }

        throw new InvalidOperationException("Pixabay fallback nie jest skonfigurowany. Wpisz klucz Pixabay API albo ustaw zmienna PIXABAY_API_KEY.");
    }

    private async Task<StockVideoSelection> SearchPexelsVideoAsync(
        string apiKey,
        VoiceSegment segment,
        string debugFileName,
        HashSet<string> usedVideoUrls,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var queries = ResolveSearchQueries(segment);
        var allCandidates = new List<PexelsVideoCandidate>();

        for (var queryIndex = 0; queryIndex < queries.Count; queryIndex++)
        {
            var query = queries[queryIndex];
            var candidates = await SearchVideoCandidatesAsync(
                apiKey,
                query,
                queryIndex,
                segment,
                debugFileName.Replace(".json", $"-q{queryIndex + 1:00}.json", StringComparison.OrdinalIgnoreCase),
                usedVideoUrls,
                logger,
                cancellationToken);
            allCandidates.AddRange(candidates);
        }

        if (logger is not null)
        {
            var selectionDebugFileName = debugFileName.Replace("pexels-search", "pexels-selection", StringComparison.OrdinalIgnoreCase);
            await logger.SaveJsonAsync(
                selectionDebugFileName,
                new
                {
                    segmentIndex = segment.Index,
                    segmentName = segment.Name,
                    queries,
                    segment.VisualDescription,
                    segment.AvoidVisuals,
                    candidates = allCandidates.Select(candidate => new
                    {
                        candidate.Query,
                        candidate.QueryIndex,
                        candidate.Rank,
                        candidate.Score,
                        candidate.SemanticFitScore,
                        candidate.AvoidPenalty,
                        candidate.IsDuplicate,
                        candidate.Rejected,
                        candidate.RejectionReason,
                        candidate.Video.Url,
                        thumbnail = candidate.Video.Image,
                        candidate.Video.Duration,
                        videoSize = $"{candidate.Video.Width}x{candidate.Video.Height}",
                        author = candidate.Video.User?.Name ?? "Pexels",
                        selectedFile = candidate.VideoFile?.Link,
                        selectedFileSize = candidate.VideoFile is null ? string.Empty : $"{candidate.VideoFile.Width}x{candidate.VideoFile.Height}",
                        selectedFileQuality = candidate.VideoFile?.Quality ?? string.Empty
                    })
                },
                cancellationToken);
        }

        var selected = allCandidates
            .Where(candidate => !candidate.Rejected)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.IsDuplicate)
            .ThenBy(candidate => candidate.QueryIndex)
            .ThenBy(candidate => candidate.Rank)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Pexels nie znalazl pionowego klipu MP4 dla fraz: {string.Join(", ", queries)}");

        var reason = selected.IsDuplicate
            ? $"Selected reusable candidate after scoring multiple queries. query=\"{selected.Query}\" score={selected.Score:0.#} semanticFit={selected.SemanticFitScore:0.##} avoidPenalty={selected.AvoidPenalty:0.#}."
            : $"Selected best scored candidate across multiple queries. query=\"{selected.Query}\" score={selected.Score:0.#} semanticFit={selected.SemanticFitScore:0.##} avoidPenalty={selected.AvoidPenalty:0.#}.";

        return new StockVideoSelection(
            Provider: "Pexels",
            SourceUrl: selected.Video.Url,
            DownloadUrl: selected.VideoFile!.Link,
            ThumbnailUrl: selected.Video.Image,
            Rank: selected.Rank,
            CandidateCount: allCandidates.Count,
            Score: selected.Score,
            Reason: $"provider=Pexels. {reason}",
            AuthorName: selected.Video.User?.Name ?? "Pexels",
            AuthorUrl: selected.Video.User?.Url ?? "https://www.pexels.com");
    }

    private async Task<List<PexelsVideoCandidate>> SearchVideoCandidatesAsync(
        string apiKey,
        string query,
        int queryIndex,
        VoiceSegment segment,
        string debugFileName,
        HashSet<string> usedVideoUrls,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var url =
            "https://api.pexels.com/v1/videos/search" +
            $"?query={Uri.EscapeDataString(query)}" +
            "&orientation=portrait" +
            "&per_page=12" +
            "&size=medium";

        var result = await SendWithTransientRetryAsync(
            () =>
            {
                var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
                retryRequest.Headers.TryAddWithoutValidation("Authorization", apiKey);
                return retryRequest;
            },
            "Pexels search",
            logger,
            cancellationToken);
        var responseBody = result.ResponseBody;
        logger?.Info($"Pexels search response query=\"{query}\" status={result.StatusCode}; elapsedMs={result.ElapsedMilliseconds}; attempts={result.Attempts}; bodyChars={responseBody.Length}; rateLimit={result.RateLimit}; remaining={result.RateLimitRemaining}; reset={result.RateLimitReset}");
        if (logger is not null)
        {
            await logger.SaveTextAsync(debugFileName, responseBody, cancellationToken);
        }

        if (result.StatusCode < 200 || result.StatusCode > 299)
        {
            throw new InvalidOperationException($"Pexels zwrocil blad HTTP {result.StatusCode}: {responseBody}");
        }

        var searchResponse = JsonSerializer.Deserialize<PexelsSearchResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Pexels zwrocil pusta odpowiedz.");
        logger?.Info($"Pexels parsed query=\"{query}\" videos={searchResponse.Videos.Count}; segment={segment.Index}; segmentName={segment.Name}");

        return searchResponse.Videos
            .Select((video, index) =>
            {
                var videoFile = SelectBestVideoFile(video);
                var duplicate = usedVideoUrls.Contains(video.Url);
                var avoidPenalty = CalculateAvoidPenalty(video, segment.AvoidVisuals);
                var semanticFitScore = CalculateSemanticFitScore(video, query, segment);
                var rejected = ShouldRejectLowSemanticFit(video, semanticFitScore);
                var score = CalculateCandidateScore(video, query, queryIndex, index + 1, segment, duplicate, avoidPenalty, semanticFitScore);
                return new PexelsVideoCandidate(
                    Query: query,
                    QueryIndex: queryIndex,
                    Rank: index + 1,
                    Video: video,
                    VideoFile: videoFile,
                    IsDuplicate: duplicate,
                    AvoidPenalty: avoidPenalty,
                    SemanticFitScore: semanticFitScore,
                    Rejected: rejected,
                    RejectionReason: rejected ? $"semanticFit below {MinimumSemanticFitScore:0.##}" : string.Empty,
                    Score: score);
            })
            .Where(candidate => candidate.VideoFile is not null)
            .ToList();
    }

    private async Task<StockVideoSelection> SearchPixabayVideoAsync(
        string apiKey,
        VoiceSegment segment,
        string debugFileName,
        HashSet<string> usedVideoUrls,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var queries = ResolveSearchQueries(segment);
        var allCandidates = new List<PixabayVideoCandidate>();

        for (var queryIndex = 0; queryIndex < queries.Count; queryIndex++)
        {
            var query = queries[queryIndex];
            var candidates = await SearchPixabayCandidatesAsync(
                apiKey,
                query,
                queryIndex,
                segment,
                debugFileName.Replace(".json", $"-q{queryIndex + 1:00}.json", StringComparison.OrdinalIgnoreCase),
                usedVideoUrls,
                logger,
                cancellationToken);
            allCandidates.AddRange(candidates);
        }

        if (logger is not null)
        {
            var selectionDebugFileName = debugFileName.Replace("pixabay-search", "pixabay-selection", StringComparison.OrdinalIgnoreCase);
            await logger.SaveJsonAsync(
                selectionDebugFileName,
                new
                {
                    segmentIndex = segment.Index,
                    segmentName = segment.Name,
                    queries,
                    segment.VisualDescription,
                    segment.AvoidVisuals,
                    candidates = allCandidates.Select(candidate => new
                    {
                        candidate.Query,
                        candidate.QueryIndex,
                        candidate.Rank,
                        candidate.Score,
                        candidate.SemanticFitScore,
                        candidate.AvoidPenalty,
                        candidate.IsDuplicate,
                        candidate.Rejected,
                        candidate.RejectionReason,
                        candidate.Video.PageUrl,
                        candidate.Video.Tags,
                        thumbnail = candidate.VideoFile?.Thumbnail ?? string.Empty,
                        candidate.Video.Duration,
                        author = candidate.Video.User,
                        selectedFile = candidate.VideoFile?.Url,
                        selectedFileSize = candidate.VideoFile is null ? string.Empty : $"{candidate.VideoFile.Width}x{candidate.VideoFile.Height}"
                    })
                },
                cancellationToken);
        }

        var selected = allCandidates
            .Where(candidate => !candidate.Rejected)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.IsDuplicate)
            .ThenBy(candidate => candidate.QueryIndex)
            .ThenBy(candidate => candidate.Rank)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Pixabay nie znalazl klipu MP4 dla fraz: {string.Join(", ", queries)}");

        var reason = selected.IsDuplicate
            ? $"provider=Pixabay. Selected reusable candidate after Pexels fallback. query=\"{selected.Query}\" score={selected.Score:0.#} semanticFit={selected.SemanticFitScore:0.##} avoidPenalty={selected.AvoidPenalty:0.#}."
            : $"provider=Pixabay. Selected best scored candidate after Pexels fallback. query=\"{selected.Query}\" score={selected.Score:0.#} semanticFit={selected.SemanticFitScore:0.##} avoidPenalty={selected.AvoidPenalty:0.#}.";
        var authorName = string.IsNullOrWhiteSpace(selected.Video.User)
            ? "Pixabay"
            : selected.Video.User;

        return new StockVideoSelection(
            Provider: "Pixabay",
            SourceUrl: selected.Video.PageUrl,
            DownloadUrl: selected.VideoFile!.Url,
            ThumbnailUrl: selected.VideoFile.Thumbnail,
            Rank: selected.Rank,
            CandidateCount: allCandidates.Count,
            Score: selected.Score,
            Reason: reason,
            AuthorName: authorName,
            AuthorUrl: "https://pixabay.com");
    }

    private async Task<List<PixabayVideoCandidate>> SearchPixabayCandidatesAsync(
        string apiKey,
        string query,
        int queryIndex,
        VoiceSegment segment,
        string debugFileName,
        HashSet<string> usedVideoUrls,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var url =
            "https://pixabay.com/api/videos/" +
            $"?key={Uri.EscapeDataString(apiKey)}" +
            $"&q={Uri.EscapeDataString(query)}" +
            "&lang=en" +
            "&video_type=film" +
            "&per_page=20" +
            "&safesearch=true";

        var result = await SendWithTransientRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url),
            "Pixabay search",
            logger,
            cancellationToken);
        var responseBody = result.ResponseBody;
        logger?.Info($"Pixabay search response query=\"{query}\" status={result.StatusCode}; elapsedMs={result.ElapsedMilliseconds}; attempts={result.Attempts}; bodyChars={responseBody.Length}; rateLimit={result.RateLimit}; remaining={result.RateLimitRemaining}; reset={result.RateLimitReset}");
        if (logger is not null)
        {
            await logger.SaveTextAsync(debugFileName, responseBody, cancellationToken);
        }

        if (result.StatusCode < 200 || result.StatusCode > 299)
        {
            throw new InvalidOperationException($"Pixabay zwrocil blad HTTP {result.StatusCode}: {responseBody}");
        }

        var searchResponse = JsonSerializer.Deserialize<PixabaySearchResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Pixabay zwrocil pusta odpowiedz.");
        logger?.Info($"Pixabay parsed query=\"{query}\" videos={searchResponse.Hits.Count}; segment={segment.Index}; segmentName={segment.Name}");

        return searchResponse.Hits
            .Select((video, index) =>
            {
                var videoFile = SelectBestPixabayVideoFile(video);
                var sourceUrl = string.IsNullOrWhiteSpace(video.PageUrl)
                    ? $"https://pixabay.com/videos/{video.Id}/"
                    : video.PageUrl;
                var duplicate = usedVideoUrls.Contains(sourceUrl);
                var avoidPenalty = CalculatePixabayAvoidPenalty(video, segment.AvoidVisuals);
                var semanticFitScore = CalculatePixabaySemanticFitScore(video, query, segment);
                var rejected = ShouldRejectLowPixabaySemanticFit(video, semanticFitScore);
                var score = CalculatePixabayCandidateScore(video, videoFile, query, queryIndex, index + 1, segment, duplicate, avoidPenalty, semanticFitScore);
                return new PixabayVideoCandidate(
                    Query: query,
                    QueryIndex: queryIndex,
                    Rank: index + 1,
                    Video: video with { PageUrl = sourceUrl },
                    VideoFile: videoFile,
                    IsDuplicate: duplicate,
                    AvoidPenalty: avoidPenalty,
                    SemanticFitScore: semanticFitScore,
                    Rejected: rejected,
                    RejectionReason: rejected ? $"semanticFit below {MinimumSemanticFitScore:0.##}" : string.Empty,
                    Score: score);
            })
            .Where(candidate => candidate.VideoFile is not null)
            .ToList();
    }

    private static List<string> ResolveSearchQueries(VoiceSegment segment)
    {
        var queries = segment.SearchPhrases
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Append(segment.SearchPhrase)
            .Select(phrase => phrase.Trim())
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return queries.Count == 0 ? ["person planning task at desk"] : queries;
    }

    private static double CalculateCandidateScore(
        PexelsVideo video,
        string query,
        int queryIndex,
        int rank,
        VoiceSegment segment,
        bool duplicate,
        double avoidPenalty,
        double semanticFitScore)
    {
        var score = 100d;
        score -= queryIndex * 6;
        score -= Math.Max(rank - 1, 0) * 3;
        score -= duplicate ? 35 : 0;
        score -= avoidPenalty;
        score += semanticFitScore * 18;

        if (video.Height >= video.Width)
        {
            score += 10;
        }

        if (video.Duration >= segment.Duration.TotalSeconds)
        {
            score += 4;
        }

        score += CalculateTextOverlapScore(query, segment.VisualDescription) * 3;
        return Math.Round(score, 3);
    }

    private static double CalculatePixabayCandidateScore(
        PixabayVideo video,
        PixabayVideoFile? videoFile,
        string query,
        int queryIndex,
        int rank,
        VoiceSegment segment,
        bool duplicate,
        double avoidPenalty,
        double semanticFitScore)
    {
        var score = 96d;
        score -= queryIndex * 6;
        score -= Math.Max(rank - 1, 0) * 3;
        score -= duplicate ? 35 : 0;
        score -= avoidPenalty;
        score += semanticFitScore * 18;

        if (videoFile is not null && videoFile.Height >= videoFile.Width)
        {
            score += 10;
        }

        if (video.Duration >= segment.Duration.TotalSeconds)
        {
            score += 4;
        }

        score += CalculateTextOverlapScore(query, segment.VisualDescription) * 3;
        return Math.Round(score, 3);
    }

    private static double CalculateSemanticFitScore(
        PexelsVideo video,
        string query,
        VoiceSegment segment)
    {
        var metadata = BuildCandidateMetadata(video);
        var queryToMetadata = CalculateTextOverlapScore(query, metadata);
        var visualToMetadata = CalculateTextOverlapScore(segment.VisualDescription, metadata);
        return Math.Round(Math.Max(queryToMetadata, visualToMetadata * 0.7), 3);
    }

    private static double CalculatePixabaySemanticFitScore(
        PixabayVideo video,
        string query,
        VoiceSegment segment)
    {
        var metadata = BuildPixabayCandidateMetadata(video);
        var queryToMetadata = CalculateTextOverlapScore(query, metadata);
        var visualToMetadata = CalculateTextOverlapScore(segment.VisualDescription, metadata);
        return Math.Round(Math.Max(queryToMetadata, visualToMetadata * 0.7), 3);
    }

    private static bool ShouldRejectLowSemanticFit(PexelsVideo video, double semanticFitScore)
    {
        if (string.IsNullOrWhiteSpace(video.Image))
        {
            return false;
        }

        var metadataTerms = ExtractTerms(BuildCandidateMetadata(video)).Take(8).Count();
        return metadataTerms >= 4 && semanticFitScore < MinimumSemanticFitScore;
    }

    private static bool ShouldRejectLowPixabaySemanticFit(PixabayVideo video, double semanticFitScore)
    {
        var metadataTerms = ExtractTerms(BuildPixabayCandidateMetadata(video)).Take(8).Count();
        return metadataTerms >= 4 && semanticFitScore < MinimumSemanticFitScore;
    }

    private static double CalculateAvoidPenalty(PexelsVideo video, string avoidVisuals)
    {
        var avoidTerms = ExtractTerms(avoidVisuals).Take(12).ToList();
        if (avoidTerms.Count == 0)
        {
            return 0;
        }

        var searchable = BuildCandidateMetadata(video).ToLowerInvariant();
        return avoidTerms.Count(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase)) * 12;
    }

    private static double CalculatePixabayAvoidPenalty(PixabayVideo video, string avoidVisuals)
    {
        var avoidTerms = ExtractTerms(avoidVisuals).Take(12).ToList();
        if (avoidTerms.Count == 0)
        {
            return 0;
        }

        var searchable = BuildPixabayCandidateMetadata(video).ToLowerInvariant();
        return avoidTerms.Count(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase)) * 12;
    }

    private static string BuildCandidateMetadata(PexelsVideo video)
    {
        return $"{video.Url} {video.Image} {video.User?.Name ?? string.Empty}";
    }

    private static string BuildPixabayCandidateMetadata(PixabayVideo video)
    {
        return $"{video.PageUrl} {video.Tags} {video.User}";
    }

    private static double CalculateTextOverlapScore(string query, string visualDescription)
    {
        var queryTerms = ExtractTerms(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (queryTerms.Count == 0)
        {
            return 0;
        }

        var visualTerms = ExtractTerms(visualDescription).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return queryTerms.Count(visualTerms.Contains) / (double)queryTerms.Count;
    }

    private static IEnumerable<string> ExtractTerms(string value)
    {
        var normalized = new string(value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray());
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 4)
            .Where(term => term is not ("person" or "video" or "with" or "from" or "that" or "this" or "close" or "shot"));
    }

    private async Task DownloadFileAsync(
        string provider,
        string? apiKey,
        string url,
        string filePath,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        using var response = await SendStreamWithTransientRetryAsync(
            () =>
            {
                var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    retryRequest.Headers.TryAddWithoutValidation("Authorization", apiKey);
                }

                return retryRequest;
            },
            $"{provider} download",
            logger,
            cancellationToken);
        logger?.Info($"{provider} download response status={(int)response.StatusCode}; url={url}; contentLength={response.Content.Headers.ContentLength?.ToString() ?? string.Empty}");
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Nie udalo sie pobrac klipu {provider} HTTP {(int)response.StatusCode}: {responseBody}");
        }

        var partPath = $"{filePath}.part";
        if (File.Exists(partPath))
        {
            File.Delete(partPath);
        }

        await using var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var fileStream = File.Create(partPath))
        {
            await remoteStream.CopyToAsync(fileStream, cancellationToken);
        }

        var fileInfo = new FileInfo(partPath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            if (File.Exists(partPath))
            {
                File.Delete(partPath);
            }

            throw new InvalidOperationException($"Pobrany klip {provider} jest pusty: {url}");
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        File.Move(partPath, filePath);
    }

    private async Task<HttpTextResult> SendWithTransientRetryAsync(
        Func<HttpRequestMessage> createRequest,
        string operation,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var totalElapsed = Stopwatch.StartNew();
        var attempts = 0;
        while (true)
        {
            attempts++;
            using var response = await SendSingleAttemptAsync(createRequest, operation, attempts, logger, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!IsTransientStatusCode((int)response.StatusCode) || attempts >= MaxTransientAttempts)
            {
                totalElapsed.Stop();
                return new HttpTextResult(
                    (int)response.StatusCode,
                    responseBody,
                    totalElapsed.ElapsedMilliseconds,
                    attempts,
                    ReadHeader(response, "X-Ratelimit-Limit"),
                    ReadHeader(response, "X-Ratelimit-Remaining"),
                    ReadHeader(response, "X-Ratelimit-Reset"));
            }

            logger?.Warning($"{operation} transient HTTP {(int)response.StatusCode}; retry={attempts}/{MaxTransientAttempts}.");
            await Task.Delay(CreateRetryDelay(attempts), cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendStreamWithTransientRetryAsync(
        Func<HttpRequestMessage> createRequest,
        string operation,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (true)
        {
            attempts++;
            var response = await SendSingleAttemptAsync(
                createRequest,
                operation,
                attempts,
                logger,
                cancellationToken,
                HttpCompletionOption.ResponseHeadersRead);
            if (!IsTransientStatusCode((int)response.StatusCode) || attempts >= MaxTransientAttempts)
            {
                return response;
            }

            logger?.Warning($"{operation} transient HTTP {(int)response.StatusCode}; retry={attempts}/{MaxTransientAttempts}.");
            response.Dispose();
            await Task.Delay(CreateRetryDelay(attempts), cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendSingleAttemptAsync(
        Func<HttpRequestMessage> createRequest,
        string operation,
        int attempt,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        try
        {
            using var request = createRequest();
            return await _httpClient.SendAsync(request, completionOption, cancellationToken);
        }
        catch (HttpRequestException) when (attempt < MaxTransientAttempts)
        {
            logger?.Warning($"{operation} transient network error; retry={attempt}/{MaxTransientAttempts}.");
            await Task.Delay(CreateRetryDelay(attempt), cancellationToken);
            return await SendSingleAttemptAsync(createRequest, operation, attempt + 1, logger, cancellationToken, completionOption);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxTransientAttempts)
        {
            logger?.Warning($"{operation} transient timeout; retry={attempt}/{MaxTransientAttempts}.");
            await Task.Delay(CreateRetryDelay(attempt), cancellationToken);
            return await SendSingleAttemptAsync(createRequest, operation, attempt + 1, logger, cancellationToken, completionOption);
        }
    }

    private static bool IsTransientStatusCode(int statusCode)
    {
        return statusCode == 408 || statusCode == 429 || statusCode >= 500;
    }

    private static TimeSpan CreateRetryDelay(int attempt)
    {
        var jitterMs = Random.Shared.Next(40, 140);
        var baseMs = Math.Min(250 * (1 << Math.Max(attempt - 1, 0)), 2000);
        return TimeSpan.FromMilliseconds(baseMs + jitterMs);
    }

    private static string ReadHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return values.FirstOrDefault() ?? string.Empty;
        }

        return string.Empty;
    }

    private static PexelsVideoFile? SelectBestVideoFile(PexelsVideo video)
    {
        return (video.VideoFiles ?? [])
            .Where(file => !string.IsNullOrWhiteSpace(file.Link))
            .Where(file => !string.IsNullOrWhiteSpace(file.FileType))
            .Where(file => file.FileType.Contains("mp4", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.Height >= file.Width)
            .ThenBy(file => Math.Abs(file.Width - 1080) + Math.Abs(file.Height - 1920))
            .ThenBy(file => string.Equals(file.Quality, "hd", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
    }

    private static PixabayVideoFile? SelectBestPixabayVideoFile(PixabayVideo video)
    {
        var files = new[]
        {
            video.Videos?.Large,
            video.Videos?.Medium,
            video.Videos?.Small,
            video.Videos?.Tiny
        };

        return files
            .Where(file => file is not null)
            .Select(file => file!)
            .Where(file => !string.IsNullOrWhiteSpace(file.Url))
            .OrderByDescending(file => file.Height >= file.Width)
            .ThenBy(file => Math.Abs(file.Width - 1080) + Math.Abs(file.Height - 1920))
            .ThenByDescending(file => file.Width * file.Height)
            .FirstOrDefault();
    }

    private sealed class PexelsSearchResponse
    {
        public List<PexelsVideo> Videos { get; set; } = [];
    }

    private sealed class PixabaySearchResponse
    {
        public List<PixabayVideo> Hits { get; set; } = [];
    }

    private sealed record PexelsVideoCandidate(
        string Query,
        int QueryIndex,
        int Rank,
        PexelsVideo Video,
        PexelsVideoFile? VideoFile,
        bool IsDuplicate,
        double AvoidPenalty,
        double SemanticFitScore,
        bool Rejected,
        string RejectionReason,
        double Score);

    private sealed record PixabayVideoCandidate(
        string Query,
        int QueryIndex,
        int Rank,
        PixabayVideo Video,
        PixabayVideoFile? VideoFile,
        bool IsDuplicate,
        double AvoidPenalty,
        double SemanticFitScore,
        bool Rejected,
        string RejectionReason,
        double Score);

    private sealed record HttpTextResult(
        int StatusCode,
        string ResponseBody,
        long ElapsedMilliseconds,
        int Attempts,
        string RateLimit,
        string RateLimitRemaining,
        string RateLimitReset);

    private sealed record StockVideoSelection(
        string Provider,
        string SourceUrl,
        string DownloadUrl,
        string ThumbnailUrl,
        int Rank,
        int CandidateCount,
        double Score,
        string Reason,
        string AuthorName,
        string AuthorUrl);

    private sealed class PexelsVideo
    {
        public int Width { get; set; }

        public int Height { get; set; }

        public int Duration { get; set; }

        public string Url { get; set; } = string.Empty;

        public string Image { get; set; } = string.Empty;

        public PexelsUser? User { get; set; }

        [JsonPropertyName("video_files")]
        public List<PexelsVideoFile> VideoFiles { get; set; } = [];
    }

    private sealed class PexelsUser
    {
        public string Name { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;
    }

    private sealed class PexelsVideoFile
    {
        public string Quality { get; set; } = string.Empty;

        [JsonPropertyName("file_type")]
        public string FileType { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public string Link { get; set; } = string.Empty;
    }

    private sealed record PixabayVideo
    {
        public int Id { get; init; }

        [JsonPropertyName("pageURL")]
        public string PageUrl { get; init; } = string.Empty;

        public string Tags { get; init; } = string.Empty;

        public int Duration { get; init; }

        public string User { get; init; } = string.Empty;

        public PixabayVideoRenditions? Videos { get; init; }
    }

    private sealed class PixabayVideoRenditions
    {
        public PixabayVideoFile? Large { get; set; }

        public PixabayVideoFile? Medium { get; set; }

        public PixabayVideoFile? Small { get; set; }

        public PixabayVideoFile? Tiny { get; set; }
    }

    private sealed class PixabayVideoFile
    {
        public string Url { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public string Thumbnail { get; set; } = string.Empty;
    }
}
