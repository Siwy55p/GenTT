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
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolvePexelsApiKey(options);
        Directory.CreateDirectory(outputDirectory);

        var clips = new List<DownloadedVideoClip>();

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            progress?.Report(new ShortGenerationProgress(
                45 + i * 15 / Math.Max(segments.Count, 1),
                $"Pobieram klip Pexels: {segment.SearchPhrase}"));

            var video = await SearchVideoAsync(apiKey, segment.SearchPhrase, cancellationToken);
            var videoFile = SelectBestVideoFile(video)
                ?? throw new InvalidOperationException($"Pexels nie zwrocil pliku MP4 dla frazy: {segment.SearchPhrase}");

            var filePath = Path.Combine(
                outputDirectory,
                $"{segment.Index:00}_{SanitizeFileName(segment.SearchPhrase)}.mp4");

            await DownloadFileAsync(apiKey, videoFile.Link, filePath, cancellationToken);

            clips.Add(new DownloadedVideoClip
            {
                SegmentIndex = segment.Index,
                SearchPhrase = segment.SearchPhrase,
                FilePath = filePath,
                PexelsUrl = video.Url,
                AuthorName = video.User?.Name ?? "Pexels",
                AuthorUrl = video.User?.Url ?? "https://www.pexels.com"
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

    private async Task<PexelsVideo> SearchVideoAsync(
        string apiKey,
        string query,
        CancellationToken cancellationToken)
    {
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

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Pexels zwrocil blad HTTP {(int)response.StatusCode}: {responseBody}");
        }

        var searchResponse = JsonSerializer.Deserialize<PexelsSearchResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Pexels zwrocil pusta odpowiedz.");

        return searchResponse.Videos
            .OrderByDescending(video => video.Height > video.Width)
            .ThenByDescending(video => video.Duration)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Pexels nie znalazl pionowego klipu dla frazy: {query}");
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
        return video.VideoFiles
            .Where(file => !string.IsNullOrWhiteSpace(file.Link))
            .Where(file => file.FileType.Contains("mp4", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.Height >= file.Width)
            .ThenBy(file => Math.Abs(file.Width - 1080) + Math.Abs(file.Height - 1920))
            .ThenBy(file => file.Quality.Equals("hd", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
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
