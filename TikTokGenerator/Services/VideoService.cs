using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.Json;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class VideoService
{
    public async Task<VideoProject> GenerateShortAsync(
        Trend trend,
        string script,
        string voiceOverPath,
        string backgroundDescription,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var projectDirectory = Path.GetDirectoryName(voiceOverPath)
            ?? Path.Combine(AppContext.BaseDirectory, "Output", SanitizeFileName($"{DateTime.Now:yyyyMMdd-HHmmss}-{trend.Title}"));
        Directory.CreateDirectory(projectDirectory);

        var outputPath = Path.Combine(projectDirectory, "short.mp4");
        var project = new VideoProject
        {
            Topic = trend.Title,
            Country = trend.Country,
            Category = trend.Category,
            Script = script,
            VoiceOverPath = voiceOverPath,
            BackgroundDescription = backgroundDescription,
            OutputPath = outputPath
        };

        progress?.Report(65);

        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath is null)
        {
            await SaveProjectAsync(projectDirectory, project, cancellationToken);
            throw new FileNotFoundException(
                "Nie znaleziono FFmpeg. Dodaj ffmpeg.exe do katalogu Tools projektu albo zainstaluj FFmpeg w PATH.",
                "ffmpeg.exe");
        }

        await RenderVideoAsync(ffmpegPath, project, cancellationToken);
        progress?.Report(90);

        await SaveProjectAsync(projectDirectory, project, cancellationToken);
        progress?.Report(100);

        return project;
    }

    private static string? FindFfmpeg()
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg.exe");
        if (File.Exists(localPath))
        {
            return localPath;
        }

        var sourcePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Tools", "ffmpeg.exe");
        if (File.Exists(sourcePath))
        {
            return Path.GetFullPath(sourcePath);
        }

        return IsCommandAvailable("ffmpeg") ? "ffmpeg" : null;
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(2000);
            return process is { HasExited: true, ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    private static async Task RenderVideoAsync(
        string ffmpegPath,
        VideoProject project,
        CancellationToken cancellationToken)
    {
        var projectDirectory = Path.GetDirectoryName(project.OutputPath)
            ?? AppContext.BaseDirectory;
        var posterPath = Path.Combine(projectDirectory, "poster.png");
        CreatePosterFrame(project, posterPath);

        using var process = new Process
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

        var args = process.StartInfo.ArgumentList;
        args.Add("-y");
        args.Add("-loop");
        args.Add("1");
        args.Add("-framerate");
        args.Add("25");
        args.Add("-i");
        args.Add(posterPath);
        args.Add("-f");
        args.Add("lavfi");
        args.Add("-i");
        args.Add("anullsrc=channel_layout=stereo:sample_rate=44100");
        args.Add("-t");
        args.Add("18");
        args.Add("-shortest");
        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add("-c:a");
        args.Add("aac");
        args.Add(project.OutputPath);

        process.Start();
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg nie utworzyl pliku MP4. Szczegoly: {error}");
        }
    }

    private static void CreatePosterFrame(VideoProject project, string posterPath)
    {
        const int width = 1080;
        const int height = 1920;
        const int margin = 96;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var backgroundBrush = new LinearGradientBrush(
            new Rectangle(0, 0, width, height),
            Color.FromArgb(17, 24, 39),
            Color.FromArgb(32, 41, 64),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(backgroundBrush, 0, 0, width, height);

        DrawAccentBars(graphics, width, height);
        DrawTextPanel(graphics, project, width, height, margin);

        bitmap.Save(posterPath, ImageFormat.Png);
    }

    private static void DrawAccentBars(Graphics graphics, int width, int height)
    {
        using var cyanPen = new Pen(Color.FromArgb(72, 209, 204), 10);
        using var redPen = new Pen(Color.FromArgb(255, 65, 108), 10);
        using var mutedPen = new Pen(Color.FromArgb(64, 82, 116), 4);

        graphics.DrawLine(cyanPen, 80, 180, width - 80, 180);
        graphics.DrawLine(redPen, 80, height - 180, width - 80, height - 180);

        for (var i = 0; i < 8; i++)
        {
            var y = 360 + i * 145;
            graphics.DrawLine(mutedPen, 120, y, width - 120, y);
        }
    }

    private static void DrawTextPanel(Graphics graphics, VideoProject project, int width, int height, int margin)
    {
        var panel = new RectangleF(margin, 470, width - margin * 2, height - 940);
        using var panelPath = CreateRoundedRectangle(panel, 34);
        using var panelBrush = new SolidBrush(Color.FromArgb(210, 0, 0, 0));
        graphics.FillPath(panelBrush, panelPath);

        using var titleFont = new Font("Segoe UI", 66, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Segoe UI", 42, FontStyle.Regular, GraphicsUnit.Pixel);
        using var metaFont = new Font("Segoe UI", 30, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(Color.White);
        using var bodyBrush = new SolidBrush(Color.FromArgb(230, 238, 247));
        using var metaBrush = new SolidBrush(Color.FromArgb(72, 209, 204));

        using var titleFormat = CreateTextFormat();
        using var bodyFormat = CreateTextFormat();

        graphics.DrawString(
            project.Category.ToUpperInvariant(),
            metaFont,
            metaBrush,
            new RectangleF(margin + 48, 525, width - margin * 2 - 96, 50),
            titleFormat);

        graphics.DrawString(
            WrapText(project.Topic, 22),
            titleFont,
            titleBrush,
            new RectangleF(margin + 48, 610, width - margin * 2 - 96, 330),
            titleFormat);

        graphics.DrawString(
            CreateShortCaption(project.Script),
            bodyFont,
            bodyBrush,
            new RectangleF(margin + 48, 970, width - margin * 2 - 96, 430),
            bodyFormat);
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

    private static StringFormat CreateTextFormat()
    {
        return new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.Word
        };
    }

    private static async Task SaveProjectAsync(
        string projectDirectory,
        VideoProject project,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(projectDirectory, "project.json");
        var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
    }

    private static string CreateShortCaption(string script)
    {
        var lines = script
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("Hook:", StringComparison.OrdinalIgnoreCase))
            .Take(3);

        return string.Join(Environment.NewLine, lines);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return sanitized.Length > 80 ? sanitized[..80] : sanitized;
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

        return string.Join(Environment.NewLine, lines);
    }
}
