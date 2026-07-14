using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public interface IVideoService
{
    Task<string> RenderVideoAsync(
        ShortScript script,
        IReadOnlyList<VoiceSegment> voiceSegments,
        IReadOnlyList<DownloadedVideoClip> clips,
        string projectDirectory,
        IProgress<ShortGenerationProgress>? progress = null,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default);
}

public sealed class VideoService : IVideoService
{
    public async Task<string> RenderVideoAsync(
        ShortScript script,
        IReadOnlyList<VoiceSegment> voiceSegments,
        IReadOnlyList<DownloadedVideoClip> clips,
        string projectDirectory,
        IProgress<ShortGenerationProgress>? progress = null,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpegPath = ToolLocator.FindFfmpeg()
            ?? throw new FileNotFoundException("Nie znaleziono FFmpeg. Dodaj ffmpeg.exe do Tools albo zainstaluj FFmpeg w PATH.");
        var ffprobePath = ToolLocator.FindFfprobe()
            ?? throw new FileNotFoundException("Nie znaleziono ffprobe.exe. Dodaj ffprobe.exe do Tools albo zainstaluj FFmpeg w PATH.");

        Directory.CreateDirectory(projectDirectory);
        logger?.Info($"Rendering video with FFmpeg={ffmpegPath}; FFprobe={ffprobePath}; segments={voiceSegments.Count}; clips={clips.Count}");
        var subtitleDirectory = Path.Combine(projectDirectory, "subtitles");
        var segmentDirectory = Path.Combine(projectDirectory, "segments");
        Directory.CreateDirectory(subtitleDirectory);
        Directory.CreateDirectory(segmentDirectory);

        var segmentPaths = new List<string>();

        for (var i = 0; i < voiceSegments.Count; i++)
        {
            var segment = voiceSegments[i];
            var clip = clips.FirstOrDefault(item => item.SegmentIndex == segment.Index)
                ?? throw new InvalidOperationException($"Brakuje klipu wideo dla segmentu {segment.Index}.");

            progress?.Report(new ShortGenerationProgress(
                70 + i * 20 / Math.Max(voiceSegments.Count, 1),
                $"Montuje segment {i + 1}/{voiceSegments.Count}"));

            var subtitlePath = Path.Combine(subtitleDirectory, $"{segment.Index:00}_{segment.Name}.ass");
            await CreateSubtitleFileAsync(segment, subtitlePath, cancellationToken);
            logger?.Info($"Created dynamic subtitles segment={segment.Index} path={subtitlePath}");

            var segmentPath = Path.Combine(segmentDirectory, $"{segment.Index:00}_{segment.Name}.mp4");
            logger?.Info($"Rendering segment={segment.Index} duration={segment.Duration.TotalSeconds:0.###}s clip={clip.FilePath} audio={segment.AudioPath}");
            await RenderSegmentAsync(
                ffmpegPath,
                clip.FilePath,
                segment.AudioPath,
                subtitlePath,
                segment.Duration,
                segmentPath,
                cancellationToken);

            segmentPaths.Add(segmentPath);
            await ValidateMediaFileAsync(ffprobePath, segmentPath, $"segment {segment.Index}", cancellationToken);
            logger?.Info($"Rendered segment={segment.Index} path={segmentPath}");
        }

        progress?.Report(new ShortGenerationProgress(92, "Lacze segmenty"));
        var outputPath = Path.Combine(projectDirectory, "short.mp4");
        await ConcatSegmentsAsync(ffmpegPath, ffprobePath, segmentPaths, outputPath, logger, cancellationToken);
        await ValidateMediaFileAsync(ffprobePath, outputPath, "final short", cancellationToken);
        logger?.Info($"Concatenated final video path={outputPath}");

        progress?.Report(new ShortGenerationProgress(96, "Zapisuje metadane"));
        await SaveCreditsAsync(script, clips, projectDirectory, cancellationToken);

        progress?.Report(new ShortGenerationProgress(100, "Gotowe"));
        return outputPath;
    }

    private static async Task RenderSegmentAsync(
        string ffmpegPath,
        string clipPath,
        string audioPath,
        string subtitlePath,
        TimeSpan duration,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var process = CreateFfmpegProcess(ffmpegPath);
        var args = process.StartInfo.ArgumentList;

        args.Add("-y");
        args.Add("-stream_loop");
        args.Add("-1");
        args.Add("-i");
        args.Add(clipPath);
        args.Add("-i");
        args.Add(audioPath);
        args.Add("-t");
        args.Add(duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        args.Add("-filter_complex");
        args.Add($"[0:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,setsar=1,fps=30[base];[base]subtitles='{EscapeFilterPath(subtitlePath)}'[v]");
        args.Add("-map");
        args.Add("[v]");
        args.Add("-map");
        args.Add("1:a");
        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-preset");
        args.Add("veryfast");
        args.Add("-profile:v");
        args.Add("high");
        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add("-c:a");
        args.Add("aac");
        args.Add("-b:a");
        args.Add("192k");
        args.Add("-shortest");
        args.Add(outputPath);

        await RunProcessAsync(process, "FFmpeg nie wyrenderowal segmentu.", cancellationToken);
    }

    private static async Task ConcatSegmentsAsync(
        string ffmpegPath,
        string ffprobePath,
        IReadOnlyList<string> segmentPaths,
        string outputPath,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var listPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory, "concat.txt");
        var lines = segmentPaths.Select(path => $"file '{EscapeConcatPath(path)}'");
        await File.WriteAllLinesAsync(listPath, lines, cancellationToken);
        var canCopy = await SegmentStreamsAreCompatibleAsync(ffprobePath, segmentPaths, logger, cancellationToken);
        if (!canCopy)
        {
            logger?.Warning("Segment streams differ. Using FFmpeg concat with re-encode instead of concat demuxer stream copy.");
            await ConcatSegmentsWithReencodeAsync(ffmpegPath, listPath, outputPath, cancellationToken);
            return;
        }

        using var process = CreateFfmpegProcess(ffmpegPath);
        var args = process.StartInfo.ArgumentList;

        args.Add("-y");
        args.Add("-f");
        args.Add("concat");
        args.Add("-safe");
        args.Add("0");
        args.Add("-i");
        args.Add(listPath);
        args.Add("-c");
        args.Add("copy");
        args.Add(outputPath);

        await RunProcessAsync(process, "FFmpeg nie polaczyl segmentow.", cancellationToken);
    }

    private static async Task ConcatSegmentsWithReencodeAsync(
        string ffmpegPath,
        string listPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var process = CreateFfmpegProcess(ffmpegPath);
        var args = process.StartInfo.ArgumentList;

        args.Add("-y");
        args.Add("-f");
        args.Add("concat");
        args.Add("-safe");
        args.Add("0");
        args.Add("-i");
        args.Add(listPath);
        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-preset");
        args.Add("veryfast");
        args.Add("-profile:v");
        args.Add("high");
        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add("-c:a");
        args.Add("aac");
        args.Add("-b:a");
        args.Add("192k");
        args.Add(outputPath);

        await RunProcessAsync(process, "FFmpeg nie polaczyl segmentow przez reenkodowanie.", cancellationToken);
    }

    private static async Task<bool> SegmentStreamsAreCompatibleAsync(
        string ffprobePath,
        IReadOnlyList<string> segmentPaths,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        if (segmentPaths.Count <= 1)
        {
            return true;
        }

        var signatures = new List<string>();
        foreach (var segmentPath in segmentPaths)
        {
            var signature = await ReadMediaSignatureAsync(ffprobePath, segmentPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(signature))
            {
                logger?.Warning($"ffprobe did not produce a usable stream signature for {segmentPath}.");
                return false;
            }

            signatures.Add(signature);
        }

        var compatible = StreamSignaturesAreCompatible(signatures);
        if (!compatible)
        {
            logger?.Warning($"Segment stream signatures differ: {string.Join(" | ", signatures)}");
        }

        return compatible;
    }

    internal static bool StreamSignaturesAreCompatible(IReadOnlyList<string> signatures)
    {
        if (signatures.Count <= 1)
        {
            return true;
        }

        var first = signatures[0];
        return !string.IsNullOrWhiteSpace(first)
            && signatures.All(signature => string.Equals(signature, first, StringComparison.Ordinal));
    }

    private static async Task ValidateMediaFileAsync(
        string ffprobePath,
        string mediaPath,
        string label,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaPath) || new FileInfo(mediaPath).Length == 0)
        {
            throw new InvalidOperationException($"FFmpeg utworzyl pusty albo brakujacy plik dla {label}: {mediaPath}");
        }

        var json = await RunProcessForOutputAsync(
            ffprobePath,
            ["-v", "error", "-show_streams", "-of", "json", mediaPath],
            $"ffprobe nie potwierdzil poprawnosci pliku dla {label}.",
            cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("streams", out var streams)
            || streams.ValueKind != JsonValueKind.Array
            || streams.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"ffprobe nie znalazl streamow w pliku dla {label}: {mediaPath}");
        }
    }

    private static async Task<string> ReadMediaSignatureAsync(
        string ffprobePath,
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var json = await RunProcessForOutputAsync(
            ffprobePath,
            ["-v", "error", "-show_streams", "-of", "json", mediaPath],
            $"ffprobe nie odczytal streamow dla segmentu: {mediaPath}.",
            cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("streams", out var streams)
            || streams.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var stream in streams.EnumerateArray())
        {
            var codecType = ReadJsonString(stream, "codec_type");
            if (!codecType.Equals("video", StringComparison.OrdinalIgnoreCase)
                && !codecType.Equals("audio", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            parts.Add(string.Join(
                ":",
                codecType,
                ReadJsonString(stream, "codec_name"),
                ReadJsonString(stream, "profile"),
                ReadJsonString(stream, "pix_fmt"),
                ReadJsonString(stream, "sample_rate"),
                ReadJsonString(stream, "channel_layout"),
                ReadJsonString(stream, "r_frame_rate"),
                ReadJsonString(stream, "time_base"),
                ReadJsonInt(stream, "width"),
                ReadJsonInt(stream, "height")));
        }

        return string.Join("|", parts);
    }

    private static string ReadJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    private static string ReadJsonInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
                ? result.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
    }

    private static Process CreateFfmpegProcess(string ffmpegPath)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
    }

    private static async Task RunProcessAsync(
        Process process,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{errorMessage} Output: {output} Error: {error}");
        }
    }

    private static async Task<string> RunProcessForOutputAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{errorMessage} Output: {output} Error: {error}");
        }

        return output;
    }

    private static async Task CreateSubtitleFileAsync(
        VoiceSegment segment,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var events = CreateSubtitleEvents(segment.Text, segment.Duration).ToList();
        if (events.Count == 0)
        {
            events.Add(new SubtitleEvent(TimeSpan.Zero, segment.Duration, segment.OnScreenText));
        }

        var lines = new List<string>
        {
            "[Script Info]",
            "ScriptType: v4.00+",
            "PlayResX: 1080",
            "PlayResY: 1920",
            "ScaledBorderAndShadow: yes",
            string.Empty,
            "[V4+ Styles]",
            "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding",
            "Style: Default,Segoe UI,62,&H00FFFFFF,&H000000FF,&HCC000000,&H99000000,-1,0,0,0,100,100,0,0,1,5,1,2,90,90,340,1",
            string.Empty,
            "[Events]",
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text"
        };

        lines.AddRange(events.Select(item =>
            $"Dialogue: 0,{FormatAssTime(item.Start)},{FormatAssTime(item.End)},Default,,0,0,0,,{EscapeAssText(WrapText(item.Text, 24))}"));

        await File.WriteAllLinesAsync(outputPath, lines, cancellationToken);
    }

    private static IEnumerable<SubtitleEvent> CreateSubtitleEvents(string text, TimeSpan duration)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            yield break;
        }

        var chunkSize = words.Length <= 8 ? words.Length : 4;
        var chunks = words.Chunk(chunkSize)
            .Select(chunk => string.Join(' ', chunk))
            .ToList();
        var totalSeconds = Math.Max(duration.TotalSeconds, 0.5);
        var cursor = 0.0;

        foreach (var chunk in chunks)
        {
            if (cursor >= totalSeconds)
            {
                yield break;
            }

            var share = CountWords(chunk) / (double)Math.Max(words.Length, 1);
            var seconds = Math.Max(0.55, totalSeconds * share);
            var end = Math.Min(totalSeconds, cursor + seconds);
            yield return new SubtitleEvent(TimeSpan.FromSeconds(cursor), TimeSpan.FromSeconds(end), chunk);
            cursor = end;
        }
    }

    private static async Task SaveCreditsAsync(
        ShortScript script,
        IReadOnlyList<DownloadedVideoClip> clips,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var creditsPath = Path.Combine(projectDirectory, "credits.txt");
        var lines = new List<string>
        {
            $"Title: {script.Title}",
            "Video clips provided by stock providers:",
            string.Empty
        };

        lines.AddRange(clips.Select(clip => $"{clip.AuthorName} - {clip.PexelsUrl}"));
        lines.Add(string.Empty);
        lines.Add("Clip diagnostics:");
        lines.AddRange(clips.Select(clip => $"Segment {clip.SegmentIndex:00}: rank={clip.PexelsRank}; query={clip.SearchPhrase}; visual={clip.VisualDescription}; reason={clip.SelectionReason}"));
        await File.WriteAllLinesAsync(creditsPath, lines, cancellationToken);
    }

    private static string WrapText(string text, int maxLineLength)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lines = new List<string>();
        var currentLine = string.Empty;

        foreach (var word in words)
        {
            if (currentLine.Length == 0)
            {
                currentLine = word;
                continue;
            }

            if (currentLine.Length + word.Length + 1 > maxLineLength)
            {
                lines.Add(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine += " " + word;
            }
        }

        if (currentLine.Length > 0)
        {
            lines.Add(currentLine);
        }

        return string.Join(Environment.NewLine, lines.Take(5));
    }

    private static int CountWords(string text)
    {
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static string FormatAssTime(TimeSpan value)
    {
        return $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 10:00}";
    }

    private static string EscapeAssText(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace(Environment.NewLine, "\\N", StringComparison.Ordinal);
    }

    private static string EscapeFilterPath(string path)
    {
        return path
            .Replace("\\", "/", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }

    private static string EscapeConcatPath(string path)
    {
        return path.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "'\\''", StringComparison.Ordinal);
    }

    private sealed record SubtitleEvent(TimeSpan Start, TimeSpan End, string Text);
}
