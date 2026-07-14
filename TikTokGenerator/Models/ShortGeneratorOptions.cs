namespace TikTokGenerator.Models;

public sealed class ShortGeneratorOptions
{
    public string ModelProvider { get; init; } = "auto";

    public string OllamaBaseUrl { get; init; } = "http://localhost:11434";

    public string OllamaModel { get; init; } = "qwen3:4b";

    public string OpenAIBaseUrl { get; init; } = "https://api.openai.com/v1";

    public string OpenAIModel { get; init; } = "gpt-5.6-terra";

    public string OpenAIReasoningEffort { get; init; } = "medium";

    public string OpenAIApiKey { get; init; } = string.Empty;

    public string PexelsApiKey { get; init; } = string.Empty;

    public string PixabayApiKey { get; init; } = string.Empty;

    public string PiperExePath { get; init; } = string.Empty;

    public string PiperModelPath { get; init; } = string.Empty;
}
