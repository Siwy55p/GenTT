namespace TikTokGenerator.Models;

public sealed class AppSettings
{
    public ModelSettings Model { get; set; } = new();

    public OpenAISettings? OpenAI { get; set; }

    public PexelsSettings Pexels { get; set; } = new();

    public PixabaySettings Pixabay { get; set; } = new();

    public PiperSettings Piper { get; set; } = new();
}

public sealed class ModelSettings
{
    public string Provider { get; set; } = "auto";

    public OllamaSettings Ollama { get; set; } = new();

    public OpenAISettings OpenAI { get; set; } = new();
}

public sealed class OllamaSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "qwen3:4b";
}

public sealed class OpenAISettings
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string Model { get; set; } = "gpt-5.6-terra";

    public string ReasoningEffort { get; set; } = "medium";

    public string ApiKey { get; set; } = string.Empty;
}

public sealed class PexelsSettings
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class PixabaySettings
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class PiperSettings
{
    public string ExePath { get; set; } = string.Empty;

    public string ModelPath { get; set; } = string.Empty;
}
