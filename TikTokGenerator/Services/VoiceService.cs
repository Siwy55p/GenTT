using System.Diagnostics;
using System.Globalization;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class VoiceService
{
    public async Task<IReadOnlyList<VoiceSegment>> GenerateVoiceAsync(
        ShortScript script,
        string outputDirectory,
        ShortGeneratorOptions options,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var piperPath = ToolLocator.FindPiper(options.PiperExePath)
            ?? throw new FileNotFoundException("Nie znaleziono Piper. Ustaw PIPER_EXE albo dodaj piper.exe do Tools\\Piper.");

        var modelPath = ToolLocator.FindPiperModel(options.PiperModelPath)
            ?? throw new FileNotFoundException("Nie znaleziono modelu glosu Piper .onnx. Ustaw PIPER_MODEL albo dodaj model do Tools\\Piper.");

        var ffprobePath = ToolLocator.FindFfprobe()
            ?? throw new FileNotFoundException("Nie znaleziono ffprobe.exe. Zainstaluj FFmpeg albo dodaj ffprobe.exe do Tools.");

        var rawSegments = CreateRawSegments(script);
        var segments = new List<VoiceSegment>();

        foreach (var rawSegment in rawSegments)
        {
            var audioPath = Path.Combine(outputDirectory, $"{rawSegment.Index:00}_{rawSegment.Name}.wav");
            var textPath = Path.ChangeExtension(audioPath, ".txt");
            await File.WriteAllTextAsync(textPath, rawSegment.Text, cancellationToken);

            await RunPiperAsync(piperPath, modelPath, rawSegment.Text, audioPath, cancellationToken);
            var duration = await GetAudioDurationAsync(ffprobePath, audioPath, cancellationToken);

            segments.Add(new VoiceSegment
            {
                Index = rawSegment.Index,
                Name = rawSegment.Name,
                Text = rawSegment.Text,
                SearchPhrase = rawSegment.SearchPhrase,
                AudioPath = audioPath,
                Duration = duration
            });
        }

        return segments;
    }

    private static IReadOnlyList<RawVoiceSegment> CreateRawSegments(ShortScript script)
    {
        var segments = new List<RawVoiceSegment>();
        var firstSearchPhrase = script.Scenes.First().SearchPhrase;
        var lastSearchPhrase = script.Scenes.Last().SearchPhrase;

        segments.Add(new RawVoiceSegment(0, "hook", script.Hook, firstSearchPhrase));

        for (var i = 0; i < script.Scenes.Count; i++)
        {
            segments.Add(new RawVoiceSegment(
                i + 1,
                $"scene_{i + 1:00}",
                script.Scenes[i].Text,
                script.Scenes[i].SearchPhrase));
        }

        segments.Add(new RawVoiceSegment(segments.Count, "ending", script.Ending, lastSearchPhrase));
        return segments;
    }

    private static async Task RunPiperAsync(
        string piperPath,
        string modelPath,
        string text,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = piperPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        var args = process.StartInfo.ArgumentList;
        args.Add("--model");
        args.Add(modelPath);
        args.Add("--output_file");
        args.Add(outputPath);

        process.Start();
        await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException($"Piper nie utworzyl pliku WAV. Output: {output} Error: {error}");
        }
    }

    private static async Task<TimeSpan> GetAudioDurationAsync(
        string ffprobePath,
        string audioPath,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        var args = process.StartInfo.ArgumentList;
        args.Add("-v");
        args.Add("error");
        args.Add("-show_entries");
        args.Add("format=duration");
        args.Add("-of");
        args.Add("default=noprint_wrappers=1:nokey=1");
        args.Add(audioPath);

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe nie odczytal dlugosci WAV. Szczegoly: {error}");
        }

        if (!double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            throw new InvalidOperationException($"Nie udalo sie odczytac dlugosci WAV: {output}");
        }

        return TimeSpan.FromSeconds(Math.Max(seconds, 0.5));
    }

    private sealed record RawVoiceSegment(int Index, string Name, string Text, string SearchPhrase);
}
