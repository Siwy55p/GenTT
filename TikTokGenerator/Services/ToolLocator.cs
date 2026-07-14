using System.Diagnostics;

namespace TikTokGenerator.Services;

public static class ToolLocator
{
    public static string? FindFfmpeg()
    {
        return FindExecutable("ffmpeg.exe", "ffmpeg");
    }

    public static string? FindFfprobe()
    {
        return FindExecutable("ffprobe.exe", "ffprobe");
    }

    public static string? FindPiper(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var envPath = GetEnvironmentVariable("PIPER_EXE");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        foreach (var path in GetToolSearchPaths("piper.exe", "Piper"))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return IsCommandAvailable("piper") ? "piper" : null;
    }

    public static string? FindPiperModel(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var envPath = GetEnvironmentVariable("PIPER_MODEL");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        foreach (var directory in GetToolDirectories("Piper").Concat(GetToolDirectories()))
        {
            if (Directory.Exists(directory))
            {
                var model = Directory
                    .EnumerateFiles(directory, "*.onnx", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (model is not null)
                {
                    return model;
                }
            }
        }

        return null;
    }

    private static string? FindExecutable(string exeName, string commandName)
    {
        foreach (var path in GetToolSearchPaths(exeName))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return IsCommandAvailable(commandName) ? commandName : null;
    }

    private static IEnumerable<string> GetToolSearchPaths(string fileName, string? childDirectory = null)
    {
        foreach (var directory in GetToolDirectories(childDirectory))
        {
            yield return Path.Combine(directory, fileName);
        }
    }

    private static IEnumerable<string> GetToolDirectories(string? childDirectory = null)
    {
        var baseTools = Path.Combine(AppContext.BaseDirectory, "Tools");
        var sourceTools = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Tools"));

        yield return childDirectory is null ? baseTools : Path.Combine(baseTools, childDirectory);
        yield return childDirectory is null ? sourceTools : Path.Combine(sourceTools, childDirectory);
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--help",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(2000);
            return process is { HasExited: true };
        }
        catch
        {
            return false;
        }
    }

    private static string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
    }
}
