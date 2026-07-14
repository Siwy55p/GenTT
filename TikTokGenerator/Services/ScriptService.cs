using System.Net.Http.Json;
using System.Text.Json;
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
                num_predict = 900
            }
        };

        var endpoint = new Uri(new Uri(options.OllamaBaseUrl.TrimEnd('/') + "/"), "api/generate");

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Ollama zwrocila blad HTTP {(int)response.StatusCode}: {responseBody}");
            }

            var ollamaResponse = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseBody, JsonOptions)
                ?? throw new InvalidOperationException("Ollama zwrocila pusta odpowiedz.");

            var scriptJson = ExtractJsonObject(ollamaResponse.Response);
            var script = JsonSerializer.Deserialize<ShortScript>(scriptJson, JsonOptions)
                ?? throw new InvalidOperationException("Nie udalo sie odczytac scenariusza JSON z odpowiedzi Ollamy.");

            NormalizeScript(script, topic);
            return script;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Nie moge polaczyc sie z Ollama. Uruchom Ollama i wykonaj: ollama pull qwen3:4b",
                ex);
        }
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

    private static string ExtractJsonObject(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException($"Ollama nie zwrocila obiektu JSON. Odpowiedz: {value}");
        }

        return trimmed[start..(end + 1)];
    }

    private static void NormalizeScript(ShortScript script, SelectedTopic topic)
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
