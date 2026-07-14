using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class ScriptService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;

    public ScriptService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ShortScript> GenerateScriptAsync(
        SelectedTopic topic,
        ShortGeneratorOptions options,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic.SourceText))
        {
            throw new InvalidOperationException("Wklej material zrodlowy. Sam tytul tematu nie wystarczy do bezpiecznego scenariusza.");
        }

        var request = new
        {
            model = string.IsNullOrWhiteSpace(options.OllamaModel) ? "qwen3:4b" : options.OllamaModel,
            prompt = CreatePrompt(topic),
            stream = false,
            format = "json",
            think = false,
            options = new
            {
                temperature = 0.2,
                num_predict = 1600
            }
        };

        var endpoint = new Uri(new Uri(options.OllamaBaseUrl.TrimEnd('/') + "/"), "api/generate");
        logger?.Info($"Calling Ollama endpoint={endpoint} model={request.model}");

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (logger is not null)
            {
                await logger.SaveTextAsync("ollama-http-response.json", responseBody, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Ollama zwrocila blad HTTP {(int)response.StatusCode}: {responseBody}");
            }

            var ollamaResponse = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseBody, JsonOptions)
                ?? throw new InvalidOperationException("Ollama zwrocila pusta odpowiedz.");

            if (logger is not null)
            {
                await logger.SaveTextAsync("ollama-script-raw.txt", ollamaResponse.Response, cancellationToken);
            }

            var script = ParseScriptOrFallback(ollamaResponse.Response, topic, logger);
            return script;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Nie moge polaczyc sie z Ollama. Uruchom Ollama i wykonaj: ollama pull qwen3:4b",
                ex);
        }
    }

    internal static ShortScript ParseScriptOrFallback(
        string ollamaResponse,
        SelectedTopic topic,
        GenerationDebugLogger? logger = null)
    {
        if (TryDeserializeScript(ollamaResponse, out var directScript, out var directError))
        {
            logger?.Info("Parsed Ollama script directly.");
            NormalizeScript(directScript, topic);
            return directScript;
        }

        if (!string.IsNullOrWhiteSpace(directError))
        {
            logger?.Warning($"Direct script parse failed: {directError}");
        }

        if (TryExtractCompleteJsonObject(ollamaResponse, out var jsonObject))
        {
            if (TryDeserializeScript(jsonObject, out var extractedScript, out var extractedError))
            {
                logger?.Info("Parsed Ollama script from extracted complete JSON object.");
                NormalizeScript(extractedScript, topic);
                return extractedScript;
            }

            logger?.Warning($"Extracted JSON parse failed: {extractedError}");
        }
        else
        {
            logger?.Warning("Ollama response did not contain a balanced JSON object.");
        }

        var looseScript = ParseLooseScript(ollamaResponse);
        if (HasAnyUsefulContent(looseScript))
        {
            logger?.Warning("Using loosely recovered Ollama script because JSON was invalid.");
            NormalizeScript(looseScript, topic);
            return looseScript;
        }

        logger?.Warning("Using fully local fallback script because Ollama JSON was invalid or empty.");
        return CreateFallbackScript(topic);
    }

    private static string CreatePrompt(SelectedTopic topic)
    {
        return $$"""
            Napisz scenariusz pionowego shorta po polsku wylacznie na podstawie podanych informacji.

            Zasady:
            - Nie dodawaj faktow, ktorych nie ma w materiale zrodlowym.
            - Film ma trwac maksymalnie 25 sekund.
            - Pierwsze zdanie ma przyciagac uwage.
            - Utworz 3 do 5 scen.
            - Kazda scena ma miec krotki tekst do lektora oraz angielska fraze searchPhrase do wyszukania klipu stockowego.
            - Klucze JSON musza nazywac sie dokladnie: title, hook, scenes, text, searchPhrase, ending.
            - Zwracaj wylacznie JSON, bez markdown, bez komentarzy.

            Format:
            {
              "title": "krotki tytul",
              "hook": "pierwsze zdanie lektora",
              "scenes": [
                {
                  "text": "tekst sceny",
                  "searchPhrase": "english stock video search phrase"
                }
              ],
              "ending": "ostatnie zdanie"
            }

            Temat:
            {{topic.Title}}

            URL zrodla:
            {{topic.SourceUrl}}

            Material zrodlowy:
            {{TrimSource(topic.SourceText)}}
            """;
    }

    private static string TrimSource(string sourceText)
    {
        const int maxLength = 5000;
        return sourceText.Length <= maxLength ? sourceText : sourceText[..maxLength];
    }

    private static bool TryDeserializeScript(
        string json,
        out ShortScript script,
        out string? error)
    {
        try
        {
            var cleaned = RemoveMarkdownFence(json);
            script = JsonSerializer.Deserialize<ShortScript>(cleaned, JsonOptions) ?? new ShortScript();
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            script = new ShortScript();
            error = ex.Message;
            return false;
        }
    }

    private static string RemoveMarkdownFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return trimmed.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    internal static bool TryExtractCompleteJsonObject(string value, out string jsonObject)
    {
        var trimmed = RemoveMarkdownFence(value);
        var start = trimmed.IndexOf('{');
        if (start < 0)
        {
            jsonObject = string.Empty;
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    jsonObject = trimmed[start..(i + 1)];
                    return true;
                }
            }
        }

        jsonObject = string.Empty;
        return false;
    }

    private static ShortScript ParseLooseScript(string value)
    {
        return new ShortScript
        {
            Title = ReadJsonStringProperty(value, "title"),
            Hook = ReadJsonStringProperty(value, "hook"),
            Ending = ReadJsonStringProperty(value, "ending"),
            Scenes = ReadLooseScenes(value).ToList()
        };
    }

    private static string ReadJsonStringProperty(string value, string propertyName)
    {
        var match = Regex.Match(
            value,
            $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? UnescapeJsonString(match.Groups["value"].Value) : string.Empty;
    }

    private static IEnumerable<ScriptScene> ReadLooseScenes(string value)
    {
        var matches = Regex.Matches(
            value,
            "\\{\\s*\"text\"\\s*:\\s*\"(?<text>(?:\\\\.|[^\"\\\\])*)\"\\s*,\\s*\"searchPhrase\"\\s*:\\s*\"(?<search>(?:\\\\.|[^\"\\\\])*)\"\\s*\\}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in matches)
        {
            yield return new ScriptScene
            {
                Text = UnescapeJsonString(match.Groups["text"].Value),
                SearchPhrase = UnescapeJsonString(match.Groups["search"].Value)
            };
        }
    }

    private static string UnescapeJsonString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"", JsonOptions) ?? value;
        }
        catch
        {
            return value;
        }
    }

    private static bool HasAnyUsefulContent(ShortScript script)
    {
        return !string.IsNullOrWhiteSpace(script.Title)
            || !string.IsNullOrWhiteSpace(script.Hook)
            || script.Scenes.Any(scene => !string.IsNullOrWhiteSpace(scene.Text))
            || !string.IsNullOrWhiteSpace(script.Ending);
    }

    internal static void NormalizeScript(ShortScript script, SelectedTopic topic)
    {
        script.Title = script.Title.Trim();
        script.Hook = script.Hook.Trim();
        script.Ending = script.Ending.Trim();
        script.Scenes = script.Scenes
            .Where(scene => !string.IsNullOrWhiteSpace(scene.Text))
            .Take(5)
            .ToList();

        foreach (var scene in script.Scenes)
        {
            scene.Text = scene.Text.Trim();
            scene.SearchPhrase = string.IsNullOrWhiteSpace(scene.SearchPhrase)
                ? "vertical smartphone social media video"
                : scene.SearchPhrase.Trim();
        }

        if (string.IsNullOrWhiteSpace(script.Title))
        {
            script.Title = topic.Title;
        }

        if (string.IsNullOrWhiteSpace(script.Hook))
        {
            script.Hook = $"To warto wiedziec: {topic.Title}.";
        }

        foreach (var fallbackScene in CreateFallbackScenes(topic, script.Scenes.Count))
        {
            if (script.Scenes.Count >= 3)
            {
                break;
            }

            script.Scenes.Add(fallbackScene);
        }

        if (string.IsNullOrWhiteSpace(script.Ending))
        {
            script.Ending = "Warto to sprawdzic samodzielnie, zanim podejmiesz decyzje.";
        }
    }

    internal static ShortScript CreateFallbackScript(SelectedTopic topic)
    {
        var script = new ShortScript
        {
            Title = topic.Title,
            Hook = $"To warto wiedziec: {topic.Title}.",
            Ending = "Warto to sprawdzic samodzielnie, zanim podejmiesz decyzje."
        };

        script.Scenes.AddRange(CreateFallbackScenes(topic, 0).Take(3));
        NormalizeScript(script, topic);
        return script;
    }

    private static IEnumerable<ScriptScene> CreateFallbackScenes(SelectedTopic topic, int existingSceneCount)
    {
        var sourceSentences = topic.SourceText
            .Split(['.', '!', '?', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(sentence => sentence.Length > 20)
            .Take(3)
            .ToList();

        while (sourceSentences.Count < 3)
        {
            sourceSentences.Add(topic.Title);
        }

        var searchPhrases = new[]
        {
            "person using smartphone app vertical video",
            "close up phone screen productivity app",
            "people recording vertical video social media"
        };

        for (var i = existingSceneCount; i < 3; i++)
        {
            yield return new ScriptScene
            {
                Text = ShortenSentence(sourceSentences[i]),
                SearchPhrase = searchPhrases[i]
            };
        }
    }

    private static string ShortenSentence(string sentence)
    {
        const int maxLength = 145;
        var trimmed = sentence.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + ".";
    }

    private sealed class OllamaGenerateResponse
    {
        public string Response { get; set; } = string.Empty;
    }
}
