using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class StockVideoService
{
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
        var apiKey = ResolvePexelsApiKey(options);
        Directory.CreateDirectory(outputDirectory);
        logger?.Info($"Downloading Pexels videos. Segments={segments.Count}; OutputDirectory={outputDirectory}");

        var clips = new List<DownloadedVideoClip>();
        var usedVideoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            progress?.Report(new ShortGenerationProgress(
                45 + i * 15 / Math.Max(segments.Count, 1),
                $"Pobieram klip Pexels: {segment.SearchPhrase}"));

            var selection = await SearchVideoAsync(
                apiKey,
                segment,
                $"pexels-search-{segment.Index:00}.json",
                usedVideoUrls,
                logger,
                cancellationToken);
            logger?.Info($"Selected Pexels video segment={segment.Index} rank={selection.Rank} phrase={segment.SearchPhrase} url={selection.Video.Url} file={selection.VideoFile.Link} reason={selection.Reason}");

            var filePath = Path.Combine(
                outputDirectory,
                $"{segment.Index:00}_{SanitizeFileName(segment.SearchPhrase)}.mp4");

            await DownloadFileAsync(apiKey, selection.VideoFile.Link, filePath, logger, cancellationToken);
            logger?.Info($"Downloaded Pexels clip segment={segment.Index} path={filePath}");
            usedVideoUrls.Add(selection.Video.Url);

            clips.Add(new DownloadedVideoClip
            {
                SegmentIndex = segment.Index,
                SearchPhrase = segment.SearchPhrase,
                SearchPhrases = segment.SearchPhrases.Count == 0 ? [segment.SearchPhrase] : segment.SearchPhrases,
                AvoidVisuals = segment.AvoidVisuals,
                VisualDescription = segment.VisualDescription,
                FilePath = filePath,
                PexelsUrl = selection.Video.Url,
                ThumbnailUrl = selection.Video.Image,
                PexelsRank = selection.Rank,
                CandidateCount = selection.CandidateCount,
                ContentScore = selection.Score,
                SelectionReason = selection.Reason,
                AuthorName = selection.Video.User?.Name ?? "Pexels",
                AuthorUrl = selection.Video.User?.Url ?? "https://www.pexels.com"
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

        throw new InvalidOperationException("Brakuje klucza Pexels API. Wpisz go w oknie programu albo ustaw zmienna PEXELS_API_KEY.");
    }

    private async Task<PexelsVideoSelection> SearchVideoAsync(
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
                        candidate.AvoidPenalty,
                        candidate.IsDuplicate,
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
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.IsDuplicate)
            .ThenBy(candidate => candidate.QueryIndex)
            .ThenBy(candidate => candidate.Rank)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Pexels nie znalazl pionowego klipu MP4 dla fraz: {string.Join(", ", queries)}");

        var reason = selected.IsDuplicate
            ? $"Selected reusable candidate after scoring multiple queries. query=\"{selected.Query}\" score={selected.Score:0.#} avoidPenalty={selected.AvoidPenalty:0.#}."
            : $"Selected best scored candidate across multiple queries. query=\"{selected.Query}\" score={selected.Score:0.#} avoidPenalty={selected.AvoidPenalty:0.#}.";

        return new PexelsVideoSelection(selected.Video, selected.VideoFile!, selected.Rank, allCandidates.Count, selected.Score, reason);
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

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();
        logger?.Info($"Pexels search response query=\"{query}\" status={(int)response.StatusCode}; elapsedMs={stopwatch.ElapsedMilliseconds}; bodyChars={responseBody.Length}; rateLimit={ReadHeader(response, "X-Ratelimit-Limit")}; remaining={ReadHeader(response, "X-Ratelimit-Remaining")}; reset={ReadHeader(response, "X-Ratelimit-Reset")}");
        if (logger is not null)
        {
            await logger.SaveTextAsync(debugFileName, responseBody, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Pexels zwrocil blad HTTP {(int)response.StatusCode}: {responseBody}");
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
                var score = CalculateCandidateScore(video, query, queryIndex, index + 1, segment, duplicate, avoidPenalty);
                return new PexelsVideoCandidate(
                    Query: query,
                    QueryIndex: queryIndex,
                    Rank: index + 1,
                    Video: video,
                    VideoFile: videoFile,
                    IsDuplicate: duplicate,
                    AvoidPenalty: avoidPenalty,
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
            .Take(4)
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
        double avoidPenalty)
    {
        var score = 100d;
        score -= queryIndex * 6;
        score -= Math.Max(rank - 1, 0) * 3;
        score -= duplicate ? 35 : 0;
        score -= avoidPenalty;

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

    private static double CalculateAvoidPenalty(PexelsVideo video, string avoidVisuals)
    {
        var avoidTerms = ExtractTerms(avoidVisuals).Take(12).ToList();
        if (avoidTerms.Count == 0)
        {
            return 0;
        }

        var searchable = $"{video.Url} {video.Image}".ToLowerInvariant();
        return avoidTerms.Count(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase)) * 12;
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
        string apiKey,
        string url,
        string filePath,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        stopwatch.Stop();
        logger?.Info($"Pexels download response status={(int)response.StatusCode}; elapsedMs={stopwatch.ElapsedMilliseconds}; url={url}; contentLength={response.Content.Headers.ContentLength?.ToString() ?? string.Empty}");
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Nie udalo sie pobrac klipu Pexels HTTP {(int)response.StatusCode}: {responseBody}");
        }

        await using var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(filePath);
        await remoteStream.CopyToAsync(fileStream, cancellationToken);
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

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        sanitized = sanitized.Replace(' ', '_');
        return sanitized.Length > 48 ? sanitized[..48] : sanitized;
    }

    private sealed class PexelsSearchResponse
    {
        public List<PexelsVideo> Videos { get; set; } = [];
    }

    private sealed record PexelsVideoCandidate(
        string Query,
        int QueryIndex,
        int Rank,
        PexelsVideo Video,
        PexelsVideoFile? VideoFile,
        bool IsDuplicate,
        double AvoidPenalty,
        double Score);

    private sealed record PexelsVideoSelection(
        PexelsVideo Video,
        PexelsVideoFile VideoFile,
        int Rank,
        int CandidateCount,
        double Score,
        string Reason);

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
}
