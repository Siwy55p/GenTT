namespace TikTokGenerator.Models;

public sealed class ShortGeneratorOptions
{
    public string OllamaBaseUrl { get; init; } = "http://localhost:11434";

    public string OllamaModel { get; init; } = "qwen3:4b";

    public string PexelsApiKey { get; init; } = string.Empty;

    public string PiperExePath { get; init; } = string.Empty;

    public string PiperModelPath { get; init; } = string.Empty;
}
