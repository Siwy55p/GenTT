using System.Text.Json;
using System.Text.Json.Nodes;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppSettings Load(string? path = null)
    {
        var settingsPath = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppContext.BaseDirectory, "appsettings.json")
            : path;
        var settingsJson = File.Exists(settingsPath)
            ? LoadJsonObject(settingsPath)
            : new JsonObject();
        var localSettingsPath = CreateLocalSettingsPath(settingsPath);
        if (File.Exists(localSettingsPath))
        {
            MergeJson(settingsJson, LoadJsonObject(localSettingsPath));
        }

        return settingsJson.Deserialize<AppSettings>(JsonOptions) ?? new AppSettings();
    }

    public static ShortGeneratorOptions CreateShortGeneratorOptions(
        AppSettings settings,
        string? pexelsApiKeyOverride = null,
        string? pixabayApiKeyOverride = null)
    {
        return CreateShortGeneratorOptions(settings, pexelsApiKeyOverride, pixabayApiKeyOverride, ResolveEnvironmentVariable);
    }

    internal static ShortGeneratorOptions CreateShortGeneratorOptions(
        AppSettings settings,
        string? pexelsApiKeyOverride,
        string? pixabayApiKeyOverride,
        Func<string, string?> readEnvironmentVariable)
    {
        return new ShortGeneratorOptions
        {
            ModelProvider = FirstNonEmpty(
                readEnvironmentVariable("TIKTOK_MODEL_PROVIDER"),
                readEnvironmentVariable("MODEL_PROVIDER"),
                settings.Model.Provider,
                "auto"),
            OllamaBaseUrl = FirstNonEmpty(
                readEnvironmentVariable("OLLAMA_BASE_URL"),
                settings.Model.Ollama.BaseUrl,
                "http://localhost:11434"),
            OllamaModel = FirstNonEmpty(
                readEnvironmentVariable("OLLAMA_MODEL"),
                settings.Model.Ollama.Model,
                "qwen3:4b"),
            OpenAIBaseUrl = FirstNonEmpty(
                readEnvironmentVariable("OPENAI_BASE_URL"),
                settings.Model.OpenAI.BaseUrl,
                "https://api.openai.com/v1"),
            OpenAIModel = FirstNonEmpty(
                readEnvironmentVariable("OPENAI_MODEL"),
                settings.Model.OpenAI.Model,
                "gpt-5.6-terra"),
            OpenAIReasoningEffort = FirstNonEmpty(
                readEnvironmentVariable("OPENAI_REASONING_EFFORT"),
                settings.Model.OpenAI.ReasoningEffort,
                "medium"),
            OpenAIApiKey = FirstNonEmpty(
                readEnvironmentVariable("OPENAI_API_KEY"),
                settings.OpenAI?.ApiKey,
                settings.Model.OpenAI.ApiKey),
            PexelsApiKey = FirstNonEmpty(
                pexelsApiKeyOverride,
                readEnvironmentVariable("PEXELS_API_KEY"),
                settings.Pexels.ApiKey),
            PixabayApiKey = FirstNonEmpty(
                pixabayApiKeyOverride,
                readEnvironmentVariable("PIXABAY_API_KEY"),
                settings.Pixabay.ApiKey),
            PiperExePath = FirstNonEmpty(
                readEnvironmentVariable("PIPER_EXE"),
                settings.Piper.ExePath),
            PiperModelPath = FirstNonEmpty(
                readEnvironmentVariable("PIPER_MODEL"),
                settings.Piper.ModelPath)
        };
    }

    public static string ResolvePexelsApiKey(AppSettings settings)
    {
        return FirstNonEmpty(
            ResolveEnvironmentVariable("PEXELS_API_KEY"),
            settings.Pexels.ApiKey);
    }

    public static string ResolvePixabayApiKey(AppSettings settings)
    {
        return FirstNonEmpty(
            ResolveEnvironmentVariable("PIXABAY_API_KEY"),
            settings.Pixabay.ApiKey);
    }

    private static string? ResolveEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static JsonObject LoadJsonObject(string path)
    {
        var json = File.ReadAllText(path);
        var documentOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        return JsonNode.Parse(json, documentOptions: documentOptions)?.AsObject() ?? new JsonObject();
    }

    private static string CreateLocalSettingsPath(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(settingsPath);
        var extension = Path.GetExtension(settingsPath);
        return Path.Combine(directory, $"{fileName}.local{extension}");
    }

    private static void MergeJson(JsonObject target, JsonObject overlay)
    {
        foreach (var property in overlay.ToList())
        {
            if (property.Value is JsonObject overlayObject
                && target[property.Key] is JsonObject targetObject)
            {
                MergeJson(targetObject, overlayObject);
                continue;
            }

            target[property.Key] = property.Value?.DeepClone();
        }
    }
}
