using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class VideoService
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

        Directory.CreateDirectory(projectDirectory);
        logger?.Info($"Rendering video with FFmpeg={ffmpegPath}; segments={voiceSegments.Count}; clips={clips.Count}");
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

            var subtitlePath = Path.Combine(subtitleDirectory, $"{segment.Index:00}_{segment.Name}.png");
            CreateSubtitleImage(segment.OnScreenText, subtitlePath);
            logger?.Info($"Created subtitle image segment={segment.Index} path={subtitlePath}");

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
            logger?.Info($"Rendered segment={segment.Index} path={segmentPath}");
        }

        progress?.Report(new ShortGenerationProgress(92, "Lacze segmenty"));
        var outputPath = Path.Combine(projectDirectory, "short.mp4");
        await ConcatSegmentsAsync(ffmpegPath, segmentPaths, outputPath, cancellationToken);
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
        args.Add("-loop");
        args.Add("1");
        args.Add("-i");
        args.Add(subtitlePath);
        args.Add("-t");
        args.Add(duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        args.Add("-filter_complex");
        args.Add("[0:v]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,setsar=1,fps=30[base];[base][2:v]overlay=0:0:format=auto[v]");
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
        IReadOnlyList<string> segmentPaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var listPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory, "concat.txt");
        var lines = segmentPaths.Select(path => $"file '{EscapeConcatPath(path)}'");
        await File.WriteAllLinesAsync(listPath, lines, cancellationToken);

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

    private static void CreateSubtitleImage(string text, string outputPath)
    {
        const int width = 1080;
        const int height = 1920;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var fontSize = text.Length > 120 ? 48 : 58;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        using var shadowBrush = new SolidBrush(Color.FromArgb(215, 0, 0, 0));
        using var borderPen = new Pen(Color.FromArgb(110, 255, 255, 255), 2);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.Word
        };

        var wrappedText = WrapText(text, 27);
        var textArea = new RectangleF(90, 1270, 900, 360);
        var panel = new RectangleF(70, 1235, 940, 430);

        using var panelPath = CreateRoundedRectangle(panel, 36);
        graphics.FillPath(shadowBrush, panelPath);
        graphics.DrawPath(borderPen, panelPath);
        graphics.DrawString(wrappedText, font, textBrush, textArea, format);

        bitmap.Save(outputPath, ImageFormat.Png);
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
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
            "Video clips provided by Pexels:",
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

    private static string EscapeConcatPath(string path)
    {
        return path.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "'\\''", StringComparison.Ordinal);
    }
}
