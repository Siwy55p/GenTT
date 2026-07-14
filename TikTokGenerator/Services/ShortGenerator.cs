using System.Text.Json;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class ShortGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ScriptService _scriptService;
    private readonly VoiceService _voiceService;
    private readonly StockVideoService _stockVideoService;
    private readonly VideoService _videoService;

    public ShortGenerator(
        ScriptService scriptService,
        VoiceService voiceService,
        StockVideoService stockVideoService,
        VideoService videoService)
    {
        _scriptService = scriptService;
        _voiceService = voiceService;
        _stockVideoService = stockVideoService;
        _videoService = videoService;
    }

    public async Task<string> GenerateAsync(
        SelectedTopic topic,
        ShortGeneratorOptions options,
        IProgress<ShortGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var projectDirectory = CreateProjectDirectory(topic.Title);
        Directory.CreateDirectory(projectDirectory);
        var logger = new GenerationDebugLogger(projectDirectory);

        try
        {
            logger.Info($"Started generation. ProjectDirectory={projectDirectory}");
            await logger.SaveJsonAsync("topic.json", topic, cancellationToken);

            progress?.Report(new ShortGenerationProgress(5, "Tworze scenariusz w Ollama"));
            var script = await _scriptService.GenerateScriptAsync(topic, options, logger, cancellationToken);
            await SaveJsonAsync(Path.Combine(projectDirectory, "script.json"), script, cancellationToken);
            await logger.SaveJsonAsync("script-normalized.json", script, cancellationToken);

            progress?.Report(new ShortGenerationProgress(25, "Tworze lektora w Piper"));
            var audioDirectory = Path.Combine(projectDirectory, "audio");
            var voiceSegments = await _voiceService.GenerateVoiceAsync(script, audioDirectory, options, logger, cancellationToken);
            await logger.SaveJsonAsync("voice-segments.json", voiceSegments, cancellationToken);

            progress?.Report(new ShortGenerationProgress(45, "Pobieram klipy z Pexels"));
            var videoDirectory = Path.Combine(projectDirectory, "videos");
            var clips = await _stockVideoService.DownloadVideosAsync(
                voiceSegments,
                videoDirectory,
                options,
                progress,
                logger,
                cancellationToken);
            await logger.SaveJsonAsync("pexels-clips.json", clips, cancellationToken);

            progress?.Report(new ShortGenerationProgress(70, "Montuje film w FFmpeg"));
            var outputPath = await _videoService.RenderVideoAsync(
                script,
                voiceSegments,
                clips,
                projectDirectory,
                progress,
                logger,
                cancellationToken);

            await SaveJsonAsync(
                Path.Combine(projectDirectory, "project.json"),
                new
                {
                    topic,
                    script,
                    voiceSegments,
                    clips,
                    outputPath,
                    debugLogPath = logger.LogPath,
                    createdAt = DateTimeOffset.Now
                },
                cancellationToken);

            logger.Info($"Generation finished. OutputPath={outputPath}");
            return outputPath;
        }
        catch (Exception ex)
        {
            logger.Error("Generation failed.", ex);
            throw new InvalidOperationException($"{ex.Message}{Environment.NewLine}{Environment.NewLine}Debug log:{Environment.NewLine}{logger.LogPath}", ex);
        }
    }

    private static async Task SaveJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static string CreateProjectDirectory(string title)
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputRoot);

        return Path.Combine(outputRoot, SanitizeFileName($"{DateTime.Now:yyyyMMdd-HHmmss}-{title}"));
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return sanitized.Length > 90 ? sanitized[..90] : sanitized;
    }
}
