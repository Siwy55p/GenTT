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

            var script = ParseScriptOrFallback(ollamaResponse.Response, topic, logger, out var qualityReport);
            if (logger is not null)
            {
                await logger.SaveJsonAsync("script-quality-report.json", qualityReport, cancellationToken);
                var scriptDiagnostics = ShortDiagnosticsService.CreateScriptDiagnostics(topic, script, qualityReport);
                await logger.SaveJsonAsync("script-analysis.json", scriptDiagnostics, cancellationToken);
                ShortDiagnosticsService.LogSummary(logger, "Script", scriptDiagnostics);
            }

            foreach (var issue in qualityReport.Issues)
            {
                logger?.Warning($"Script quality issue [{issue.Severity}] {issue.Segment}/{issue.Code}: {issue.Message}");
            }

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
        return ParseScriptOrFallback(ollamaResponse, topic, logger, out _);
    }

    internal static ShortScript ParseScriptOrFallback(
        string ollamaResponse,
        SelectedTopic topic,
        GenerationDebugLogger? logger,
        out ScriptQualityReport qualityReport)
    {
        if (TryDeserializeScript(ollamaResponse, out var directScript, out var directError))
        {
            logger?.Info("Parsed Ollama script directly.");
            qualityReport = NormalizeScript(directScript, topic);
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
                qualityReport = NormalizeScript(extractedScript, topic);
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
            qualityReport = NormalizeScript(looseScript, topic);
            return looseScript;
        }

        logger?.Warning("Using fully local fallback script because Ollama JSON was invalid or empty.");
        var fallbackScript = CreateFallbackScript(topic);
        qualityReport = CreateReportWithIssue(
            "warning",
            "script",
            "fallback_script",
            "Ollama nie zwrocila poprawnego scenariusza, wiec uzyto lokalnego fallbacku.",
            ollamaResponse,
            JsonSerializer.Serialize(fallbackScript, JsonOptions));
        return fallbackScript;
    }

    private static string CreatePrompt(SelectedTopic topic)
    {
        return $$"""
            Napisz scenariusz pionowego shorta po polsku wylacznie na podstawie podanych informacji.

            Zasady:
            - Nie dodawaj faktow, ktorych nie ma w materiale zrodlowym.
            - Film ma trwac maksymalnie 25 sekund.
            - Pierwsze zdanie ma przyciagac uwage i obiecac konkretna korzysc.
            - Utworz 3 do 5 scen.
            - Kazda scena ma dawac widzowi praktyczna wskazowke, prosty krok albo konkretna obserwacje.
            - voiceOver to tekst czytany przez lektora. Nie wolno w nim pisac: "pierwsza scena", "druga scena", "widzimy", "kamera", "ujecie", "kadr".
            - onScreenText to bardzo krotki napis ekranowy, maksymalnie 55 znakow, nie kopia calego voiceOver.
            - visualDescription opisuje co ma byc widac w kadrze. Tu wolno opisac osobe, rekwizyt i akcje.
            - searchPhrase musi byc po angielsku, konkretna fraza do Pexels. Nie uzywaj ogolnych fraz typu "social media video".
            - avoidVisuals po angielsku wymienia czego unikac w stockach.
            - sceneGoal po polsku mowi po co istnieje scena.
            - Klucze JSON musza nazywac sie dokladnie: title, hook, hookOnScreenText, hookSearchPhrase, scenes, voiceOver, onScreenText, visualDescription, searchPhrase, avoidVisuals, sceneGoal, ending, endingOnScreenText, endingSearchPhrase.
            - Zwracaj wylacznie JSON, bez markdown, bez komentarzy.

            Format:
            {
              "title": "krotki tytul",
              "hook": "pierwsze zdanie lektora",
              "hookOnScreenText": "krotki napis do hooka",
              "hookSearchPhrase": "english stock video search phrase for the hook",
              "scenes": [
                {
                  "voiceOver": "jedno praktyczne zdanie do lektora",
                  "onScreenText": "krotki napis",
                  "visualDescription": "co widac w kadrze",
                  "searchPhrase": "english stock video search phrase",
                  "avoidVisuals": "english list of visual mistakes to avoid",
                  "sceneGoal": "cel sceny"
                }
              ],
              "ending": "ostatnie zdanie lektora",
              "endingOnScreenText": "krotki napis koncowy",
              "endingSearchPhrase": "english stock video search phrase for the ending"
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
            HookOnScreenText = ReadJsonStringProperty(value, "hookOnScreenText"),
            HookSearchPhrase = ReadJsonStringProperty(value, "hookSearchPhrase"),
            Ending = ReadJsonStringProperty(value, "ending"),
            EndingOnScreenText = ReadJsonStringProperty(value, "endingOnScreenText"),
            EndingSearchPhrase = ReadJsonStringProperty(value, "endingSearchPhrase"),
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
            "\\{(?<body>[^{}]*)\\}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in matches)
        {
            var body = match.Groups["body"].Value;
            var voiceOver = ReadJsonStringProperty(body, "voiceOver");
            var legacyText = ReadJsonStringProperty(body, "text");
            var searchPhrase = ReadJsonStringProperty(body, "searchPhrase");

            if (string.IsNullOrWhiteSpace(voiceOver)
                && string.IsNullOrWhiteSpace(legacyText)
                && string.IsNullOrWhiteSpace(searchPhrase))
            {
                continue;
            }

            yield return new ScriptScene
            {
                VoiceOver = voiceOver,
                LegacyText = string.IsNullOrWhiteSpace(legacyText) ? null : legacyText,
                OnScreenText = ReadJsonStringProperty(body, "onScreenText"),
                VisualDescription = ReadJsonStringProperty(body, "visualDescription"),
                SearchPhrase = searchPhrase,
                AvoidVisuals = ReadJsonStringProperty(body, "avoidVisuals"),
                SceneGoal = ReadJsonStringProperty(body, "sceneGoal")
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
            || script.Scenes.Any(scene => !string.IsNullOrWhiteSpace(GetSceneVoiceOver(scene)))
            || !string.IsNullOrWhiteSpace(script.Ending);
    }

    internal static ScriptQualityReport NormalizeScript(ShortScript script, SelectedTopic topic)
    {
        var report = new ScriptQualityReport();

        script.Title = NormalizeWhitespace(script.Title);
        if (string.IsNullOrWhiteSpace(script.Title))
        {
            AddIssue(
                report,
                "warning",
                "title",
                "missing_title",
                "Brakowalo tytulu, wiec uzyto tytulu tematu.",
                script.Title,
                topic.Title);
            script.Title = topic.Title;
        }

        script.Hook = NormalizeVoiceText(script.Hook, topic, "hook", report);
        if (string.IsNullOrWhiteSpace(script.Hook))
        {
            script.Hook = $"Zacznij od prostego kroku: {topic.Title}.";
            AddIssue(report, "warning", "hook", "missing_hook", "Brakowalo hooka, wiec zbudowano lokalny hook.", string.Empty, script.Hook);
        }

        script.HookOnScreenText = NormalizeScreenText(script.HookOnScreenText, script.Hook, "hook", report);

        script.Ending = NormalizeVoiceText(script.Ending, topic, "ending", report);
        if (string.IsNullOrWhiteSpace(script.Ending))
        {
            script.Ending = "Wybierz jeden maly krok i sprawdz efekt jeszcze dzis.";
            AddIssue(report, "warning", "ending", "missing_ending", "Brakowalo zakonczenia, wiec zbudowano lokalne zakonczenie.", string.Empty, script.Ending);
        }

        script.EndingOnScreenText = NormalizeScreenText(script.EndingOnScreenText, script.Ending, "ending", report);

        script.Scenes = script.Scenes
            .Where(scene => !string.IsNullOrWhiteSpace(GetSceneVoiceOver(scene)))
            .Take(5)
            .ToList();

        foreach (var item in script.Scenes.Select((scene, index) => new { scene, index }))
        {
            NormalizeScene(item.scene, topic, item.index, report);
        }

        foreach (var fallbackScene in CreateFallbackScenes(topic, script.Scenes.Count))
        {
            if (script.Scenes.Count >= 3)
            {
                break;
            }

            script.Scenes.Add(fallbackScene);
            AddIssue(
                report,
                "warning",
                $"scene_{script.Scenes.Count:00}",
                "fallback_scene",
                "Scenariusz mial mniej niz 3 sceny, wiec dodano lokalna scene fallbackowa.",
                string.Empty,
                fallbackScene.VoiceOver);
        }

        script.HookSearchPhrase = NormalizeSegmentSearchPhrase(
            script.HookSearchPhrase,
            script.Scenes.FirstOrDefault()?.SearchPhrase,
            script.Hook,
            topic,
            "hook",
            report);
        script.EndingSearchPhrase = NormalizeSegmentSearchPhrase(
            script.EndingSearchPhrase,
            script.Scenes.LastOrDefault()?.SearchPhrase,
            script.Ending,
            topic,
            "ending",
            report);

        return report;
    }

    private static void NormalizeScene(
        ScriptScene scene,
        SelectedTopic topic,
        int index,
        ScriptQualityReport report)
    {
        var segment = $"scene_{index + 1:00}";
        var originalVoiceOver = GetSceneVoiceOver(scene);

        if (string.IsNullOrWhiteSpace(scene.VoiceOver) && !string.IsNullOrWhiteSpace(scene.LegacyText))
        {
            AddIssue(
                report,
                "warning",
                segment,
                "legacy_text_field",
                "Model uzywal starego pola text. Przepisano je do voiceOver.",
                scene.LegacyText,
                scene.LegacyText);
        }

        scene.VoiceOver = NormalizeVoiceText(originalVoiceOver, topic, segment, report);
        scene.OnScreenText = NormalizeScreenText(scene.OnScreenText, scene.VoiceOver, segment, report);
        scene.VisualDescription = NormalizeVisualDescription(scene.VisualDescription, scene, segment, report);
        scene.SearchPhrase = ResolveSceneSearchPhrase(scene, topic, segment, report);
        scene.AvoidVisuals = string.IsNullOrWhiteSpace(scene.AvoidVisuals)
            ? "generic social media recording, unrelated beauty routine, random phone selfie"
            : NormalizeWhitespace(scene.AvoidVisuals);
        scene.SceneGoal = string.IsNullOrWhiteSpace(scene.SceneGoal)
            ? "Przekazac praktyczny krok albo korzysc dla widza."
            : NormalizeWhitespace(scene.SceneGoal);
        scene.LegacyText = null;
        scene.ExtraFields = null;
    }

    private static string NormalizeVoiceText(
        string value,
        SelectedTopic topic,
        string segment,
        ScriptQualityReport report)
    {
        var original = NormalizeWhitespace(value);
        var normalized = original;

        if (ContainsUnsupportedStatistic(normalized, topic.SourceText))
        {
            normalized = BuildFactSafeVoiceOver(topic, segment, normalized);
            AddIssue(
                report,
                "warning",
                segment,
                "unsupported_statistic",
                "Tekst zawieral liczbe lub procent, ktorego nie bylo w materiale zrodlowym. Zastapiono go bezpieczna wersja bez statystyki.",
                original,
                normalized);
        }

        if (ContainsStoryboardLanguage(normalized))
        {
            var fixedValue = RewriteStoryboardNarration(normalized, topic, segment);
            AddIssue(
                report,
                "warning",
                segment,
                "storyboard_in_voiceover",
                "Tekst lektora brzmial jak opis sceny. Przepisano go na zdanie dla widza.",
                normalized,
                fixedValue);
            normalized = fixedValue;
        }

        normalized = ShortenSentence(normalized, 165);
        return EnsureSentence(normalized);
    }

    private static string NormalizeScreenText(
        string value,
        string voiceOver,
        string segment,
        ScriptQualityReport report)
    {
        var original = NormalizeWhitespace(value);
        var normalized = original;

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = BuildOnScreenText(voiceOver);
            AddIssue(
                report,
                "info",
                segment,
                "generated_on_screen_text",
                "Brakowalo osobnego napisu ekranowego, wiec zbudowano krotki napis z lektora.",
                original,
                normalized);
        }

        if (ContainsStoryboardLanguage(normalized))
        {
            var fixedValue = BuildOnScreenText(RewriteStoryboardNarration(
                normalized,
                new SelectedTopic { Title = string.Empty, SourceText = string.Empty },
                segment));
            AddIssue(
                report,
                "warning",
                segment,
                "storyboard_in_on_screen_text",
                "Napis ekranowy brzmial jak opis sceny. Skrocono go do hasla dla widza.",
                normalized,
                fixedValue);
            normalized = fixedValue;
        }

        if (normalized.Length > 55)
        {
            var fixedValue = ShortenForScreen(normalized, 55);
            AddIssue(
                report,
                "warning",
                segment,
                "long_on_screen_text",
                "Napis ekranowy byl zbyt dlugi. Skrocono go do formatu shorta.",
                normalized,
                fixedValue);
            normalized = fixedValue;
        }

        return normalized;
    }

    private static string NormalizeVisualDescription(
        string value,
        ScriptScene scene,
        string segment,
        ScriptQualityReport report)
    {
        var normalized = NormalizeWhitespace(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        normalized = BuildVisualDescription(scene);
        AddIssue(
            report,
            "info",
            segment,
            "generated_visual_description",
            "Brakowalo opisu wizualnego, wiec zbudowano go z tresci sceny.",
            string.Empty,
            normalized);
        return normalized;
    }

    private static string ResolveSceneSearchPhrase(
        ScriptScene scene,
        SelectedTopic topic,
        string segment,
        ScriptQualityReport report)
    {
        var original = NormalizeWhitespace(scene.SearchPhrase);
        var recovered = RecoverSearchPhraseFromExtraFields(scene);

        if (string.IsNullOrWhiteSpace(original) && !string.IsNullOrWhiteSpace(recovered))
        {
            AddIssue(
                report,
                "warning",
                segment,
                "malformed_search_key",
                "Model zwrocil fraze wyszukiwania pod blednym kluczem. Odzyskano ja z dodatkowych pol JSON.",
                string.Empty,
                recovered);
            original = recovered;
        }

        if (string.IsNullOrWhiteSpace(original)
            || IsGenericSearchPhrase(original)
            || ContainsPolishCharacters(original))
        {
            var fixedValue = BuildSearchPhrase(scene, topic);
            AddIssue(
                report,
                string.IsNullOrWhiteSpace(original) ? "warning" : "info",
                segment,
                string.IsNullOrWhiteSpace(original) ? "missing_search_phrase" : "weak_search_phrase",
                "Fraza Pexels byla pusta, generyczna albo nieangielska. Zastapiono ja konkretniejsza fraza.",
                original,
                fixedValue);
            return fixedValue;
        }

        return original;
    }

    internal static ShortScript CreateFallbackScript(SelectedTopic topic)
    {
        var script = new ShortScript
        {
            Title = topic.Title,
            Hook = $"Zacznij od prostego kroku: {topic.Title}.",
            HookOnScreenText = "Jeden prosty krok",
            HookSearchPhrase = BuildSearchPhraseFromText(topic.Title),
            Ending = "Wybierz jeden maly krok i sprawdz efekt jeszcze dzis.",
            EndingOnScreenText = "Sprawdz dzis",
            EndingSearchPhrase = BuildSearchPhraseFromText(topic.Title)
        };

        script.Scenes.AddRange(CreateFallbackScenes(topic, 0).Take(3));
        NormalizeScript(script, topic);
        return script;
    }

    private static IEnumerable<ScriptScene> CreateFallbackScenes(SelectedTopic topic, int existingSceneCount)
    {
        var fallbackScenes = CreateTopicAwareFallbackScenes(topic).ToList();

        for (var i = existingSceneCount; i < 3; i++)
        {
            yield return fallbackScenes[i];
        }
    }

    private static IEnumerable<ScriptScene> CreateTopicAwareFallbackScenes(SelectedTopic topic)
    {
        var title = NormalizeWhitespace(topic.Title).ToLowerInvariant();
        if (title.Contains("minimalizm", StringComparison.OrdinalIgnoreCase)
            || title.Contains("aplikac", StringComparison.OrdinalIgnoreCase)
            || title.Contains("telefon", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new ScriptScene
                {
                    VoiceOver = "Usun z ekranu glownego aplikacje, ktorych nie uzywasz codziennie.",
                    OnScreenText = "Wyczysc ekran",
                    VisualDescription = "Zblizenie na telefon z porzadkowanymi ikonami aplikacji na ekranie glownym.",
                    SearchPhrase = "person organizing smartphone home screen apps",
                    AvoidVisuals = "random selfie, social media recording, unrelated beauty routine",
                    SceneGoal = "Pokazac pierwszy praktyczny krok minimalizmu w telefonie."
                },
                new ScriptScene
                {
                    VoiceOver = "Wylacz powiadomienia, ktore nie wymagaja reakcji od razu.",
                    OnScreenText = "Mniej powiadomien",
                    VisualDescription = "Dlon zmienia ustawienia powiadomien na ekranie telefonu.",
                    SearchPhrase = "close up phone notification settings",
                    AvoidVisuals = "busy social media feed, random phone selfie, gaming footage",
                    SceneGoal = "Ograniczyc rozpraszacze w aplikacjach."
                },
                new ScriptScene
                {
                    VoiceOver = "Na pierwszym ekranie zostaw tylko narzedzia, od ktorych chcesz zaczynac.",
                    OnScreenText = "Tylko najwazniejsze",
                    VisualDescription = "Minimalistyczny ekran telefonu z kilkoma uporzadkowanymi ikonami.",
                    SearchPhrase = "minimal smartphone home screen close up",
                    AvoidVisuals = "cluttered phone screen, unrelated laptop work, social media recording",
                    SceneGoal = "Domknac short konkretnym efektem wizualnym."
                }
            ];
        }

        if (title.Contains("poranny", StringComparison.OrdinalIgnoreCase)
            || title.Contains("dnia", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new ScriptScene
                {
                    VoiceOver = "Zanim ruszysz w dzien, zapisz jedna rzecz, ktora naprawde ma isc do przodu.",
                    OnScreenText = "1 priorytet",
                    VisualDescription = "Osoba rano zapisuje jeden priorytet w notesie przy biurku.",
                    SearchPhrase = "person writing morning priority in notebook",
                    AvoidVisuals = "random phone selfie, makeup routine, generic social media recording",
                    SceneGoal = "Pokazac pierwszy konkretny krok porannego rytualu."
                },
                new ScriptScene
                {
                    VoiceOver = "Potem dopisz najmniejsze zadanie, ktore mozesz zrobic od razu bez przygotowan.",
                    OnScreenText = "Male zadanie",
                    VisualDescription = "Zblizenie na dlon dopisujaca krotkie zadanie do planu dnia.",
                    SearchPhrase = "close up hand writing simple task in daily planner",
                    AvoidVisuals = "unrelated office meeting, scrolling phone, beauty routine",
                    SceneGoal = "Zamienic plan w szybkie dzialanie."
                },
                new ScriptScene
                {
                    VoiceOver = "Na koniec skresl jedna rzecz, ktorej dzis swiadomie nie robisz.",
                    OnScreenText = "Jedna rzecz mniej",
                    VisualDescription = "Osoba skresla pozycje w notesie i zostawia prosty plan dnia.",
                    SearchPhrase = "person crossing out task in notebook daily plan",
                    AvoidVisuals = "phone selfie, social media recording, random talking head",
                    SceneGoal = "Dodac praktyczna zasade ograniczania chaosu."
                }
            ];
        }

        return
        [
            new ScriptScene
            {
                VoiceOver = "Zacznij od jednego konkretnego problemu, zamiast lapac caly temat naraz.",
                OnScreenText = "Jeden problem",
                VisualDescription = "Osoba zapisuje jeden problem na kartce i porzadkuje notatki.",
                SearchPhrase = "person writing one problem on notebook desk",
                AvoidVisuals = "generic social media video, random phone selfie, unrelated beauty shot",
                SceneGoal = "Ustawic prosty punkt startu dla widza."
            },
            new ScriptScene
            {
                VoiceOver = "Dopisz najmniejszy nastepny krok, ktory da sie wykonac jeszcze dzis.",
                OnScreenText = "Maly krok",
                VisualDescription = "Zblizenie na checklist z jednym prostym zadaniem do wykonania.",
                SearchPhrase = "close up checklist one simple task notebook",
                AvoidVisuals = "unrelated meeting, scrolling social media, abstract animation",
                SceneGoal = "Zamienic informacje w dzialanie."
            },
            new ScriptScene
            {
                VoiceOver = "Po wykonaniu sprawdz, czy efekt jest przydatny, zanim dokladasz kolejne kroki.",
                OnScreenText = "Sprawdz efekt",
                VisualDescription = "Osoba sprawdza notatki i odhacza wykonane zadanie.",
                SearchPhrase = "person checking completed task in notebook",
                AvoidVisuals = "phone recording selfie, unrelated lifestyle shot, generic city footage",
                SceneGoal = "Zamknac short praktycznym testem."
            }
        ];
    }

    private static string ShortenSentence(string sentence, int maxLength = 145)
    {
        var trimmed = NormalizeWhitespace(sentence);
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + ".";
    }

    private static string NormalizeSegmentSearchPhrase(
        string value,
        string? fallback,
        string text,
        SelectedTopic topic,
        string segment,
        ScriptQualityReport report)
    {
        var normalized = NormalizeWhitespace(value);
        if (!string.IsNullOrWhiteSpace(normalized) && !IsGenericSearchPhrase(normalized))
        {
            return normalized;
        }

        var fixedValue = !string.IsNullOrWhiteSpace(fallback) && !IsGenericSearchPhrase(fallback)
            ? fallback
            : BuildSearchPhraseFromText($"{text} {topic.Title}");
        AddIssue(
            report,
            "info",
            segment,
            "generated_segment_search_phrase",
            "Brakowalo osobnej frazy wizualnej dla hooka albo zakonczenia, wiec dobrano konkretna fraze.",
            normalized,
            fixedValue);
        return fixedValue;
    }

    private static string GetSceneVoiceOver(ScriptScene scene)
    {
        return string.IsNullOrWhiteSpace(scene.VoiceOver) ? scene.LegacyText ?? string.Empty : scene.VoiceOver;
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), "\\s+", " ", RegexOptions.CultureInvariant);
    }

    private static bool ContainsStoryboardLanguage(string value)
    {
        return Regex.IsMatch(
            value,
            "\\b(scena|scenie|widzimy|kamera|ujecie|uj.cie|kadr)\\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string RewriteStoryboardNarration(string value, SelectedTopic topic, string segment)
    {
        var cleaned = Regex.Replace(
            value,
            "^(w\\s+)?(pierwszej|drugiej|trzeciej|czwartej|piatej)\\s+scenie\\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(
            cleaned,
            "^(pierwsza|druga|trzecia|czwarta|piata)\\s+scena\\s*:\\s*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, "\\bwidzimy\\b", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        cleaned = NormalizeWhitespace(cleaned);

        var lower = cleaned.ToLowerInvariant();
        if (lower.Contains("notatnik") || lower.Contains("notes") || lower.Contains("plan"))
        {
            return "Zapisz jeden priorytet, male zadanie i jedna rzecz, ktora dzis odpuszczasz.";
        }

        if (lower.Contains("zegar") || lower.Contains("niepewn"))
        {
            return "Zanim ruszysz w dzien, wybierz pierwszy maly krok zamiast zaczynac od chaosu.";
        }

        if (lower.Contains("jasnym plan") || lower.Contains("satysfakc"))
        {
            return "Jasny plan rano daje decyzje na start, zamiast kolejnej rzeczy do rozkminiania.";
        }

        if (!string.IsNullOrWhiteSpace(topic.Title))
        {
            return $"Zamien temat \"{topic.Title}\" w jeden konkretny krok, ktory da sie sprawdzic dzis.";
        }

        return segment.Equals("hook", StringComparison.OrdinalIgnoreCase)
            ? "Zacznij od jednego konkretnego kroku, ktory da sie zrobic od razu."
            : "Wybierz jeden maly krok i sprawdz efekt jeszcze dzis.";
    }

    private static bool ContainsUnsupportedStatistic(string value, string sourceText)
    {
        foreach (Match match in Regex.Matches(value, "\\b\\d+(?:[,.]\\d+)?\\s*(?:%|procent|proc\\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (!sourceText.Contains(match.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildFactSafeVoiceOver(SelectedTopic topic, string segment, string original)
    {
        if (segment.Equals("hook", StringComparison.OrdinalIgnoreCase))
        {
            return $"Masz temat \"{topic.Title}\"? Zacznij od jednej rzeczy, ktora od razu porzadkuje dzialanie.";
        }

        if (segment.Equals("ending", StringComparison.OrdinalIgnoreCase))
        {
            return "Najlepszy test jest prosty: zrob jeden krok i sprawdz, czy pomaga.";
        }

        return "Zamiast opierac sie na statystyce, wybierz jeden praktyczny krok i sprawdz go w swoim dniu.";
    }

    private static string BuildOnScreenText(string voiceOver)
    {
        var clean = NormalizeWhitespace(voiceOver);
        var colon = clean.IndexOf(':');
        if (colon >= 0 && colon < 28)
        {
            clean = clean[..colon];
        }

        var sentenceEnd = clean.IndexOfAny(['.', '!', '?']);
        if (sentenceEnd > 12)
        {
            clean = clean[..sentenceEnd];
        }

        return ShortenForScreen(clean, 55);
    }

    private static string ShortenForScreen(string value, int maxLength)
    {
        var clean = NormalizeWhitespace(value).Trim('.', '!', '?', ':', ';', ',');
        if (clean.Length <= maxLength)
        {
            return clean;
        }

        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = string.Empty;
        foreach (var word in words)
        {
            var candidate = string.IsNullOrWhiteSpace(result) ? word : result + " " + word;
            if (candidate.Length > maxLength)
            {
                break;
            }

            result = candidate;
        }

        return string.IsNullOrWhiteSpace(result) ? clean[..Math.Min(clean.Length, maxLength)] : result;
    }

    private static string BuildVisualDescription(ScriptScene scene)
    {
        if (!string.IsNullOrWhiteSpace(scene.SearchPhrase))
        {
            return $"Kadr stockowy dopasowany do frazy: {scene.SearchPhrase}.";
        }

        return $"Osoba wykonuje praktyczny krok opisany w lektorze: {ShortenSentence(scene.VoiceOver, 90)}";
    }

    private static string RecoverSearchPhraseFromExtraFields(ScriptScene scene)
    {
        if (scene.ExtraFields is null)
        {
            return string.Empty;
        }

        foreach (var (key, value) in scene.ExtraFields)
        {
            if (!key.Contains("search", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool IsGenericSearchPhrase(string value)
    {
        var normalized = value.ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("vertical smartphone social media video", StringComparison.Ordinal)
            || normalized.Contains("social media video", StringComparison.Ordinal)
            || normalized.Contains("vertical video", StringComparison.Ordinal)
            || normalized.Contains("stock video", StringComparison.Ordinal);
    }

    private static bool ContainsPolishCharacters(string value)
    {
        const string polishCharacters = "\u0105\u0107\u0119\u0142\u0144\u00f3\u015b\u017a\u017c\u0104\u0106\u0118\u0141\u0143\u00d3\u015a\u0179\u017b";
        return value.Any(ch => polishCharacters.Contains(ch, StringComparison.Ordinal));
    }

    private static string BuildSearchPhrase(ScriptScene scene, SelectedTopic topic)
    {
        return BuildSearchPhraseFromText($"{scene.VisualDescription} {scene.VoiceOver} {scene.OnScreenText} {topic.Title}");
    }

    private static string BuildSearchPhraseFromText(string value)
    {
        var lower = value.ToLowerInvariant();
        if (lower.Contains("notatnik") || lower.Contains("notes") || lower.Contains("notebook") || lower.Contains("plan"))
        {
            return "person writing daily plan in notebook";
        }

        if (lower.Contains("zegar") || lower.Contains("clock") || lower.Contains("alarm") || lower.Contains("porann"))
        {
            return "person turning off morning alarm clock";
        }

        if (lower.Contains("hasl") || lower.Contains("password"))
        {
            return "person using password manager on laptop";
        }

        if (lower.Contains("telefon") || lower.Contains("phone") || lower.Contains("aplikac") || lower.Contains("app"))
        {
            return "close up smartphone productivity app";
        }

        if (lower.Contains("biurko") || lower.Contains("desk"))
        {
            return "person organizing desk after work";
        }

        if (lower.Contains("sen") || lower.Contains("sleep"))
        {
            return "calm evening routine bedroom notebook";
        }

        if (lower.Contains("firma") || lower.Contains("klient") || lower.Contains("business"))
        {
            return "small business owner planning customer tasks";
        }

        return "person planning task in notebook at desk";
    }

    private static string EnsureSentence(string value)
    {
        var normalized = NormalizeWhitespace(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return ".!?".Contains(normalized[^1], StringComparison.Ordinal) ? normalized : normalized + ".";
    }

    private static ScriptQualityReport CreateReportWithIssue(
        string severity,
        string segment,
        string code,
        string message,
        string originalValue,
        string fixedValue)
    {
        var report = new ScriptQualityReport();
        AddIssue(report, severity, segment, code, message, originalValue, fixedValue);
        return report;
    }

    private static void AddIssue(
        ScriptQualityReport report,
        string severity,
        string segment,
        string code,
        string message,
        string originalValue,
        string fixedValue)
    {
        report.Issues.Add(new ScriptQualityIssue
        {
            Severity = severity,
            Segment = segment,
            Code = code,
            Message = message,
            OriginalValue = originalValue,
            FixedValue = fixedValue
        });
    }

    private sealed class OllamaGenerateResponse
    {
        public string Response { get; set; } = string.Empty;
    }
}
