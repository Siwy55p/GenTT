using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void Load_WhenAppSettingsJsonExists_ReadsModelAndToolSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "TikTokGenerator.Tests", Guid.NewGuid().ToString("N"), "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "model": {
                "provider": "openai",
                "ollama": {
                  "baseUrl": "http://ollama.local:11434",
                  "model": "qwen3:8b"
                },
                "openAI": {
                  "baseUrl": "https://openai.local/v1",
                  "model": "gpt-5.6-sol",
                  "reasoningEffort": "high",
                  "apiKey": "settings-openai-key"
                }
              },
              "pexels": {
                "apiKey": "settings-pexels-key"
              },
              "pixabay": {
                "apiKey": "settings-pixabay-key"
              },
              "piper": {
                "exePath": "C:/Tools/piper.exe",
                "modelPath": "C:/Tools/voice.onnx"
              }
            }
            """);

        try
        {
            var settings = AppSettingsService.Load(path);

            Assert.Equal("openai", settings.Model.Provider);
            Assert.Equal("http://ollama.local:11434", settings.Model.Ollama.BaseUrl);
            Assert.Equal("qwen3:8b", settings.Model.Ollama.Model);
            Assert.Equal("https://openai.local/v1", settings.Model.OpenAI.BaseUrl);
            Assert.Equal("gpt-5.6-sol", settings.Model.OpenAI.Model);
            Assert.Equal("high", settings.Model.OpenAI.ReasoningEffort);
            Assert.Equal("settings-openai-key", settings.Model.OpenAI.ApiKey);
            Assert.Equal("settings-pexels-key", settings.Pexels.ApiKey);
            Assert.Equal("settings-pixabay-key", settings.Pixabay.ApiKey);
            Assert.Equal("C:/Tools/piper.exe", settings.Piper.ExePath);
            Assert.Equal("C:/Tools/voice.onnx", settings.Piper.ModelPath);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void CreateShortGeneratorOptions_WhenEnvironmentAndUiAreProvided_UsesExpectedPrecedence()
    {
        var settings = new AppSettings
        {
            Model = new ModelSettings
            {
                Provider = "ollama",
                Ollama = new OllamaSettings
                {
                    BaseUrl = "http://settings-ollama:11434",
                    Model = "settings-ollama-model"
                },
                OpenAI = new OpenAISettings
                {
                    BaseUrl = "https://settings-openai/v1",
                    Model = "settings-openai-model",
                    ReasoningEffort = "low",
                    ApiKey = "settings-openai-key"
                }
            },
            Pexels = new PexelsSettings
            {
                ApiKey = "settings-pexels-key"
            },
            Pixabay = new PixabaySettings
            {
                ApiKey = "settings-pixabay-key"
            },
            Piper = new PiperSettings
            {
                ExePath = "settings-piper.exe",
                ModelPath = "settings-voice.onnx"
            }
        };
        var environment = new Dictionary<string, string?>
        {
            ["TIKTOK_MODEL_PROVIDER"] = "openai",
            ["OLLAMA_MODEL"] = "env-ollama-model",
            ["OPENAI_MODEL"] = "env-openai-model",
            ["OPENAI_API_KEY"] = "env-openai-key",
            ["PEXELS_API_KEY"] = "env-pexels-key",
            ["PIXABAY_API_KEY"] = "env-pixabay-key",
            ["PIPER_MODEL"] = "env-voice.onnx"
        };

        var options = AppSettingsService.CreateShortGeneratorOptions(
            settings,
            pexelsApiKeyOverride: "ui-pexels-key",
            pixabayApiKeyOverride: "ui-pixabay-key",
            readEnvironmentVariable: name => environment.TryGetValue(name, out var value) ? value : null);

        Assert.Equal("openai", options.ModelProvider);
        Assert.Equal("http://settings-ollama:11434", options.OllamaBaseUrl);
        Assert.Equal("env-ollama-model", options.OllamaModel);
        Assert.Equal("https://settings-openai/v1", options.OpenAIBaseUrl);
        Assert.Equal("env-openai-model", options.OpenAIModel);
        Assert.Equal("low", options.OpenAIReasoningEffort);
        Assert.Equal("env-openai-key", options.OpenAIApiKey);
        Assert.Equal("ui-pexels-key", options.PexelsApiKey);
        Assert.Equal("ui-pixabay-key", options.PixabayApiKey);
        Assert.Equal("settings-piper.exe", options.PiperExePath);
        Assert.Equal("env-voice.onnx", options.PiperModelPath);
    }

    [Fact]
    public void Load_WhenLocalSettingsExists_MergesLocalOpenAISecret()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TikTokGenerator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var basePath = Path.Combine(directory, "appsettings.json");
        var localPath = Path.Combine(directory, "appsettings.local.json");
        File.WriteAllText(basePath, """
            {
              "model": {
                "provider": "openai",
                "openAI": {
                  "baseUrl": "https://settings-openai/v1",
                  "model": "gpt-5.6-terra",
                  "reasoningEffort": "medium",
                  "apiKey": ""
                }
              }
            }
            """);
        File.WriteAllText(localPath, """
            {
              "openAI": {
                "apiKey": "local-openai-key"
              }
            }
            """);

        try
        {
            var settings = AppSettingsService.Load(basePath);
            var options = AppSettingsService.CreateShortGeneratorOptions(
                settings,
                pexelsApiKeyOverride: null,
                pixabayApiKeyOverride: null,
                readEnvironmentVariable: _ => null);

            Assert.Equal("openai", options.ModelProvider);
            Assert.Equal("https://settings-openai/v1", options.OpenAIBaseUrl);
            Assert.Equal("gpt-5.6-terra", options.OpenAIModel);
            Assert.Equal("medium", options.OpenAIReasoningEffort);
            Assert.Equal("local-openai-key", options.OpenAIApiKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaultSettings()
    {
        var settings = AppSettingsService.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json"));

        Assert.Equal("auto", settings.Model.Provider);
        Assert.Equal("http://localhost:11434", settings.Model.Ollama.BaseUrl);
        Assert.Equal("qwen3:4b", settings.Model.Ollama.Model);
        Assert.Equal("https://api.openai.com/v1", settings.Model.OpenAI.BaseUrl);
        Assert.Equal("gpt-5.6-terra", settings.Model.OpenAI.Model);
        Assert.Equal("medium", settings.Model.OpenAI.ReasoningEffort);
    }
}
