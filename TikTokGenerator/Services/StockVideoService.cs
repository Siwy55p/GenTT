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

            await DownloadFileAsync(apiKey, selection.VideoFile.Link, filePath, cancellationToken);
            logger?.Info($"Downloaded Pexels clip segment={segment.Index} path={filePath}");
            usedVideoUrls.Add(selection.Video.Url);

            clips.Add(new DownloadedVideoClip
            {
                SegmentIndex = segment.Index,
                SearchPhrase = segment.SearchPhrase,
                VisualDescription = segment.VisualDescription,
                FilePath = filePath,
                PexelsUrl = selection.Video.Url,
                PexelsRank = selection.Rank,
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
        var query = segment.SearchPhrase;
        var url =
            "https://api.pexels.com/v1/videos/search" +
            $"?query={Uri.EscapeDataString(query)}" +
            "&orientation=portrait" +
            "&per_page=10" +
            "&size=medium";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
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

        var candidates = searchResponse.Videos
            .Select((video, index) => new PexelsVideoCandidate(
                Rank: index + 1,
                Video: video,
                VideoFile: SelectBestVideoFile(video),
                IsDuplicate: usedVideoUrls.Contains(video.Url)))
            .Where(candidate => candidate.VideoFile is not null)
            .ToList();

        if (logger is not null)
        {
            var selectionDebugFileName = debugFileName.Replace("pexels-search", "pexels-selection", StringComparison.OrdinalIgnoreCase);
            await logger.SaveJsonAsync(
                selectionDebugFileName,
                new
                {
                    segmentIndex = segment.Index,
                    segmentName = segment.Name,
                    query,
                    segment.VisualDescription,
                    candidates = candidates.Select(candidate => new
                    {
                        candidate.Rank,
                        candidate.IsDuplicate,
                        candidate.Video.Url,
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

        var selected = candidates
            .OrderBy(candidate => candidate.IsDuplicate)
            .ThenBy(candidate => candidate.Rank)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Pexels nie znalazl pionowego klipu MP4 dla frazy: {query}");

        var reason = selected.IsDuplicate
            ? "All returned Pexels URLs were already used, so the highest ranked reusable candidate was selected."
            : "Selected the highest ranked Pexels candidate with a usable MP4 file, preserving search relevance before duration.";

        return new PexelsVideoSelection(selected.Video, selected.VideoFile!, selected.Rank, reason);
    }

    private async Task DownloadFileAsync(
        string apiKey,
        string url,
        string filePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Nie udalo sie pobrac klipu Pexels HTTP {(int)response.StatusCode}: {responseBody}");
        }

        await using var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(filePath);
        await remoteStream.CopyToAsync(fileStream, cancellationToken);
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
        int Rank,
        PexelsVideo Video,
        PexelsVideoFile? VideoFile,
        bool IsDuplicate);

    private sealed record PexelsVideoSelection(
        PexelsVideo Video,
        PexelsVideoFile VideoFile,
        int Rank,
        string Reason);

    private sealed class PexelsVideo
    {
        public int Width { get; set; }

        public int Height { get; set; }

        public int Duration { get; set; }

        public string Url { get; set; } = string.Empty;

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
