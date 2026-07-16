using System.Text.Json;

namespace TikTokGenerator.Services;

public sealed class GenerationDebugLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();

    public GenerationDebugLogger(string projectDirectory)
    {
        DirectoryPath = Path.Combine(projectDirectory, "debug");
        Directory.CreateDirectory(DirectoryPath);
        LogPath = Path.Combine(DirectoryPath, "debug.log");
    }

    public string DirectoryPath { get; }

    public string LogPath { get; }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warning(string message)
    {
        Write("WARN", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        var details = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", details);
    }

    public async Task SaveTextAsync(string fileName, string content, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(DirectoryPath, FileNameSanitizer.ForDebugFile(fileName));
        await File.WriteAllTextAsync(path, content, cancellationToken);
        Info($"Saved debug text: {path}");
    }

    public async Task SaveJsonAsync<T>(string fileName, T value, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await SaveTextAsync(fileName, json, cancellationToken);
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [{level}] {message}{Environment.NewLine}";
        lock (_sync)
        {
            File.AppendAllText(LogPath, line);
        }
    }

}
