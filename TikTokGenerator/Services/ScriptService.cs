using System.Diagnostics;
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

    public async Task<SourceAnalysis> AnalyzeSourceAsync(
        SelectedTopic topic,
        ShortGeneratorOptions options,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic.SourceText))
        {
            throw new InvalidOperationException("Wklej material zrodlowy. Sam tytul tematu nie wystarczy do bezpiecznego scenariusza.");
        }

        var raw = await CallOllamaAsync(
            CreateSourceAnalysisPrompt(topic),
            CreateSourceAnalysisSchema(),
            "source-analysis",
            options,
            logger,
            cancellationToken,
            temperature: 0.1,
            numPredict: 1400);

        if (TryDeserializeModelOutput(raw, out SourceAnalysis? analysis, out var error) && analysis is not null)
        {
            NormalizeSourceAnalysis(analysis, topic);
            var rawDiagnostics = SourceAnalysisDiagnosticsService.CreateDiagnostics(topic, analysis);
            if (logger is not null)
            {
                await logger.SaveJsonAsync("source-analysis-diagnostics-raw.json", rawDiagnostics, cancellationToken);
                LogSourceAnalysisDiagnostics(logger, "raw", rawDiagnostics);
            }

            SourceAnalysisDiagnosticsService.SanitizeUnsupportedContent(topic, analysis, logger);
            NormalizeSourceAnalysis(analysis, topic);
            var diagnostics = SourceAnalysisDiagnosticsService.CreateDiagnostics(topic, analysis);
            if (logger is not null)
            {
                await logger.SaveJsonAsync("source-analysis-diagnostics.json", diagnostics, cancellationToken);
                LogSourceAnalysisDiagnostics(logger, "sanitized", diagnostics);
            }

            if (diagnostics.HasBlockingIssues)
            {
                throw new InvalidOperationException("Analiza zrodla nadal zawiera niepotwierdzone informacje po sanityzacji. Szczegoly sa w debug/source-analysis-diagnostics.json.");
            }

            return analysis;
        }

        logger?.Warning($"Source analysis parse failed: {error}. Using local source analysis fallback.");
        var fallback = CreateFallbackSourceAnalysis(topic);
        var fallbackDiagnostics = SourceAnalysisDiagnosticsService.CreateDiagnostics(topic, fallback);
        if (logger is not null)
        {
            await logger.SaveJsonAsync("source-analysis-diagnostics.json", fallbackDiagnostics, cancellationToken);
            LogSourceAnalysisDiagnostics(logger, "fallback", fallbackDiagnostics);
        }

        return fallback;
    }

    public async Task<ScriptConceptSelection> GenerateConceptsAsync(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortGeneratorOptions options,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var raw = await CallOllamaAsync(
            CreateConceptPrompt(topic, analysis),
            CreateConceptSelectionSchema(),
            "concept-selection",
            options,
            logger,
            cancellationToken,
            temperature: 0.35,
            numPredict: 1600);

        if (TryDeserializeModelOutput(raw, out ScriptConceptSelection? selection, out var error) && selection is not null)
        {
            NormalizeConceptSelection(selection);
            if (selection.Directions.Count == 3)
            {
                return selection;
            }

            logger?.Warning($"Concept selection returned {selection.Directions.Count} directions instead of 3. Using local concept fallback.");
            return CreateFallbackConceptSelection(topic, analysis);
        }

        logger?.Warning($"Concept selection parse failed: {error}. Using local concept fallback.");
        return CreateFallbackConceptSelection(topic, analysis);
    }

    public async Task<ShortScript> GenerateScriptAsync(
        SelectedTopic topic,
        ShortGeneratorOptions options,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var analysis = CreateFallbackSourceAnalysis(topic);
        var selection = CreateFallbackConceptSelection(topic, analysis);
        return await GenerateScriptAsync(topic, analysis, selection.SelectedDirection, options, logger, cancellationToken);
    }

    public async Task<ShortScript> GenerateScriptAsync(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ScriptConcept? selectedConcept,
        ShortGeneratorOptions options,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic.SourceText))
        {
            throw new InvalidOperationException("Wklej material zrodlowy. Sam tytul tematu nie wystarczy do bezpiecznego scenariusza.");
        }

        var raw = await CallOllamaAsync(
            CreatePrompt(topic, analysis, selectedConcept),
            CreateScriptSchema(),
            "script",
            options,
            logger,
            cancellationToken,
            temperature: 0.2,
            numPredict: 1800);

        var script = ParseScriptOrFallback(raw, topic, logger, analysis, out var qualityReport);
        script.SelectedConceptId = selectedConcept?.Id ?? string.Empty;
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

    public async Task<ContentReview> ReviewScriptAsync(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortScript script,
        ShortGeneratorOptions options,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var raw = await CallOllamaAsync(
            CreateReviewPrompt(topic, analysis, script),
            CreateContentReviewSchema(),
            "content-review",
            options,
            logger,
            cancellationToken,
            temperature: 0.1,
            numPredict: 2400);

        if (TryDeserializeModelOutput(raw, out ContentReview? review, out var error) && review is not null)
        {
            NormalizeReview(review);
            SanitizeReviewAgainstSource(topic, analysis, script, review, logger);
            NormalizeReview(review);
            return review;
        }

        logger?.Warning($"Content review parse failed: {error}. Using heuristic review fallback.");
        var fallback = CreateHeuristicReview(topic, script);
        fallback.Issues.Insert(0, new ContentReviewIssue
        {
            Severity = "warning",
            Segment = "content-review",
            Code = "review_parse_failed",
            Message = "Odpowiedz recenzenta merytorycznego byla niepoprawnym albo ucietym JSON-em. Uzyto lokalnej recenzji heurystycznej.",
            SuggestedFix = "Zmniejsz dlugosc odpowiedzi recenzenta albo zwieksz num_predict dla etapu content-review."
        });
        return fallback;
    }

    public ShortScript RepairScriptAfterReview(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortScript script,
        ContentReview review,
        GenerationDebugLogger? logger = null)
    {
        var repaired = CloneScript(script);
        logger?.Warning($"Repairing script after content review. Approved={review.Approved}; Critical={review.HasCriticalErrors}; Issues={review.Issues.Count}");
        var diagnostics = ShortDiagnosticsService.CreateScriptDiagnostics(topic, repaired);
        if (ShouldRebuildFromSource(review, diagnostics, analysis))
        {
            logger?.Warning($"Content review repair is rebuilding script from source steps. UnsupportedClaims={diagnostics.Summary.HasUnsupportedClaims}; Errors={diagnostics.Summary.ErrorCount}; SourceSteps={analysis.Steps.Count}");
            var rebuilt = BuildSourceSafeScript(topic, analysis, repaired);
            NormalizeScript(rebuilt, topic, analysis);
            return rebuilt;
        }

        if (HasPromiseProblem(review) || !review.Approved)
        {
            RepairHookAndEnding(topic, analysis, repaired, logger);
        }

        if (HasNewInformationProblem(review) || !review.Approved)
        {
            RepairSceneNewInformation(repaired, analysis, logger);
        }

        if (HasStepClarityProblem(review) || !review.Approved)
        {
            RepairStepLabels(repaired, logger);
        }

        NormalizeScript(repaired, topic, analysis);
        return repaired;
    }

    public async Task<VisualPlan> CreateVisualPlanAsync(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortScript script,
        ShortGeneratorOptions options,
        GenerationDebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var raw = await CallOllamaAsync(
            CreateVisualPlanPrompt(topic, analysis, script),
            CreateVisualPlanSchema(),
            "visual-plan",
            options,
            logger,
            cancellationToken,
            temperature: 0.25,
            numPredict: 1800);

        if (TryDeserializeModelOutput(raw, out VisualPlan? visualPlan, out var error) && visualPlan is not null)
        {
            NormalizeVisualPlan(visualPlan, script);
            return visualPlan;
        }

        logger?.Warning($"Visual plan parse failed: {error}. Using local visual plan fallback.");
        return CreateFallbackVisualPlan(script);
    }

    public ShortScript ApplyVisualPlan(ShortScript script, VisualPlan visualPlan)
    {
        return ApplyVisualPlan(script, visualPlan, new SelectedTopic
        {
            Title = script.Title,
            SourceText = script.Title
        });
    }

    public ShortScript ApplyVisualPlan(ShortScript script, VisualPlan visualPlan, SelectedTopic topic)
    {
        foreach (var item in script.Scenes.Select((scene, index) => new { scene, index }))
        {
            var segmentName = $"scene_{item.index + 1:00}";
            var plan = visualPlan.Segments.FirstOrDefault(segment =>
                segment.SegmentName.Equals(segmentName, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                continue;
            }

            item.scene.VisualDescription = NormalizeWhitespace(string.Join(
                " ",
                plan.VisibleContent,
                plan.PersonAction,
                plan.PrimaryObject,
                plan.ShotType,
                plan.MovementStart,
                plan.MovementEnd,
                plan.ResultToShow));
            item.scene.AvoidVisuals = NormalizeWhitespace(string.Join(
                ", ",
                plan.AvoidVisuals,
                visualPlan.GlobalAvoidVisuals,
                BuildDomainAvoidVisuals(topic, item.scene.VoiceOver)));
            item.scene.SearchPhrases = NormalizeVisualSearchPhrases(topic, item.scene.VoiceOver, plan.SearchPhrases);
            item.scene.SearchPhrase = item.scene.SearchPhrases.FirstOrDefault()
                ?? BuildSearchPhrase(item.scene, topic);
        }

        if (visualPlan.Segments.FirstOrDefault(segment => segment.SegmentName.Equals("hook", StringComparison.OrdinalIgnoreCase)) is { } hookPlan)
        {
            script.HookSearchPhrase = NormalizeVisualSearchPhrases(topic, script.Hook, hookPlan.SearchPhrases)
                .FirstOrDefault()
                ?? script.HookSearchPhrase;
        }

        if (visualPlan.Segments.FirstOrDefault(segment => segment.SegmentName.Equals("ending", StringComparison.OrdinalIgnoreCase)) is { } endingPlan)
        {
            script.EndingSearchPhrase = NormalizeVisualSearchPhrases(topic, script.Ending, endingPlan.SearchPhrases)
                .FirstOrDefault()
                ?? script.EndingSearchPhrase;
        }

        return script;
    }

    private static List<string> NormalizeVisualSearchPhrases(
        SelectedTopic topic,
        string segmentText,
        IEnumerable<string> rawPhrases)
    {
        var phrases = rawPhrases
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Select(NormalizeWhitespace)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var context = SourceAnalysisDiagnosticsService.Normalize($"{topic.Title} {topic.SourceText} {segmentText}");
        if (IsAiNotesContext(context))
        {
            var domainPhrases = BuildAiNotesSearchPhrases(segmentText);
            var filteredPhrases = phrases
                .Where(phrase => !IsWeakAiNotesSearchPhrase(phrase))
                .ToList();

            return domainPhrases
                .Concat(filteredPhrases)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
        }

        return phrases
            .Take(4)
            .ToList();
    }

    private static string BuildDomainAvoidVisuals(SelectedTopic topic, string segmentText)
    {
        var context = SourceAnalysisDiagnosticsService.Normalize($"{topic.Title} {topic.SourceText} {segmentText}");
        if (IsAiNotesContext(context))
        {
            return "food delivery app, smartphone home screen, social media scrolling, beauty routine, random selfie, unrelated phone gallery";
        }

        if (IsScanner3DContext(context))
        {
            return "qr code, barcode, food delivery app, phone gallery, social media scrolling, random selfie, unrelated phone app";
        }

        return string.Empty;
    }

    private static bool IsAiNotesContext(string normalized)
    {
        return normalized.Contains("transkrypc", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("spotkan", StringComparison.OrdinalIgnoreCase)
            || (normalized.Contains("nagran", StringComparison.OrdinalIgnoreCase)
                && (normalized.Contains("notatk", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("decyzj", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("zadan", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("liste", StringComparison.OrdinalIgnoreCase)))
            || (normalized.Contains("aplikac", StringComparison.OrdinalIgnoreCase)
                && normalized.Contains("notatk", StringComparison.OrdinalIgnoreCase))
            || (normalized.Contains("decyzj", StringComparison.OrdinalIgnoreCase)
                && (normalized.Contains("notatk", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("nagran", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("transkrypc", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("spotkan", StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> BuildAiNotesSearchPhrases(string text)
    {
        var normalized = SourceAnalysisDiagnosticsService.Normalize(text);
        if (normalized.Contains("porownaj", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("fragment", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("wynik", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "person checking meeting transcript and notes on laptop",
                "person reviewing audio transcript on laptop"
            ];
        }

        if (normalized.Contains("popros", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("liste", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("zadan", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "person typing meeting notes prompt on laptop",
                "meeting notes action items laptop"
            ];
        }

        if (normalized.Contains("wgraj", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("transkrypc", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("nagranie", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "person uploading audio transcript on laptop",
                "meeting transcript on laptop screen"
            ];
        }

        return
        [
            "person reviewing meeting transcript on laptop",
            "hands typing meeting notes on laptop"
        ];
    }

    private static bool IsWeakAiNotesSearchPhrase(string phrase)
    {
        var normalized = SourceAnalysisDiagnosticsService.Normalize(phrase);
        return normalized.Contains("smartphone home screen", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("home screen apps", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("phone gallery", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("food delivery", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("close up smartphone productivity app", StringComparison.OrdinalIgnoreCase);
    }

    public ShortScript ShortenToWordBudget(
        ShortScript script,
        int maxWords,
        GenerationDebugLogger? logger = null)
    {
        if (CountScriptWords(script) <= maxWords)
        {
            return script;
        }

        var shortened = CloneScript(script);
        logger?.Warning($"Script exceeds word budget. Words={CountScriptWords(shortened)}; Budget={maxWords}. Shortening before next stage.");

        shortened.Hook = TakeWords(shortened.Hook, 12);
        shortened.HookOnScreenText = ShortenForScreen(shortened.HookOnScreenText, 42);
        shortened.Ending = TakeWords(shortened.Ending, 10);
        shortened.EndingOnScreenText = ShortenForScreen(shortened.EndingOnScreenText, 42);

        var remainingBudget = Math.Max(maxWords - CountWords(shortened.Hook) - CountWords(shortened.Ending), 8);
        while (shortened.Scenes.Count > 1 && CountScriptWords(shortened) > maxWords)
        {
            shortened.Scenes.RemoveAt(shortened.Scenes.Count - 2);
        }

        var perSceneBudget = Math.Max(8, remainingBudget / Math.Max(shortened.Scenes.Count, 1));
        foreach (var scene in shortened.Scenes)
        {
            scene.VoiceOver = TakeWords(scene.VoiceOver, perSceneBudget);
            scene.OnScreenText = ShortenForScreen(scene.OnScreenText, 42);
            scene.OnScreenEmphasis = scene.OnScreenText;
            scene.EstimatedWords = CountWords(scene.VoiceOver);
        }

        return shortened;
    }

    private async Task<string> CallOllamaAsync(
        string prompt,
        object formatSchema,
        string debugName,
        ShortGeneratorOptions options,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken,
        double temperature,
        int numPredict)
    {
        var request = new
        {
            model = string.IsNullOrWhiteSpace(options.OllamaModel) ? "qwen3:4b" : options.OllamaModel,
            prompt,
            stream = false,
            format = formatSchema,
            think = false,
            options = new
            {
                temperature,
                num_predict = numPredict
            }
        };

        var endpoint = new Uri(new Uri(options.OllamaBaseUrl.TrimEnd('/') + "/"), "api/generate");
        logger?.Info($"Calling Ollama endpoint={endpoint} model={request.model} stage={debugName}; promptChars={prompt.Length}; schemaType={formatSchema.GetType().Name}; temperature={temperature:0.###}; numPredict={numPredict}");

        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await _httpClient.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            logger?.Info($"Ollama HTTP response stage={debugName}; status={(int)response.StatusCode}; elapsedMs={stopwatch.ElapsedMilliseconds}; bodyChars={responseBody.Length}");
            if (logger is not null)
            {
                await logger.SaveTextAsync($"ollama-{debugName}-http-response.json", responseBody, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Ollama zwrocila blad HTTP {(int)response.StatusCode}: {responseBody}");
            }

            var ollamaResponse = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseBody, JsonOptions)
                ?? throw new InvalidOperationException("Ollama zwrocila pusta odpowiedz.");
            logger?.Info($"Ollama model response stage={debugName}; responseChars={ollamaResponse.Response.Length}");

            if (logger is not null)
            {
                await logger.SaveTextAsync($"ollama-{debugName}-raw.txt", ollamaResponse.Response, cancellationToken);
            }

            return ollamaResponse.Response;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Nie moge polaczyc sie z Ollama. Uruchom Ollama i wykonaj: ollama pull qwen3:4b",
                ex);
        }
    }

    private static bool TryDeserializeModelOutput<T>(
        string value,
        out T? result,
        out string? error)
    {
        if (TryDeserializeClean(value, out result, out error))
        {
            return true;
        }

        if (TryExtractCompleteJsonObject(value, out var jsonObject)
            && TryDeserializeClean(jsonObject, out result, out error))
        {
            return true;
        }

        return false;
    }

    private static bool TryDeserializeClean<T>(
        string value,
        out T? result,
        out string? error)
    {
        try
        {
            result = JsonSerializer.Deserialize<T>(RemoveMarkdownFence(value), JsonOptions);
            error = null;
            return result is not null;
        }
        catch (JsonException ex)
        {
            result = default;
            error = ex.Message;
            return false;
        }
    }

    private static string CreateSourceAnalysisPrompt(SelectedTopic topic)
    {
        return $$"""
            Przeanalizuj material zrodlowy przed pisaniem filmu. Nie tworz scenariusza.

            Brief:
            {{JsonSerializer.Serialize(topic.Brief, JsonOptions)}}

            Zadanie:
            - Wydobadz glowna teze.
            - Wypisz fakty jako F1, F2, F3...
            - Wypisz kroki jako S1, S2, S3...
            - Oddziel przyklady, ograniczenia i ryzykowne twierdzenia, ale tylko jesli sa literalnie w materiale.
            - Wskaz najbardziej uzyteczny fragment dla odbiorcy z briefu.
            - Nie dodawaj informacji, przykladow, ograniczen, trybow dzialania, dat, procentow ani nazw spoza zrodla.
            - Jesli zrodlo nie podaje przykladu, zwroc pusta tablice examples.
            - Jesli zrodlo nie podaje ograniczen, zwroc pusta tablice limitations.
            - Zwracaj wylacznie JSON zgodny ze schematem.

            Temat:
            {{topic.Title}}

            URL zrodla:
            {{topic.SourceUrl}}

            Material zrodlowy:
            {{TrimSource(topic.SourceText)}}
            """;
    }

    private static string CreateConceptPrompt(SelectedTopic topic, SourceAnalysis analysis)
    {
        return $$"""
            Zaproponuj trzy rozne kierunki shorta i wybierz najlepszy. Nie pisz scenariusza.

            Brief:
            {{JsonSerializer.Serialize(topic.Brief, JsonOptions)}}

            Analiza zrodla:
            {{JsonSerializer.Serialize(analysis, JsonOptions)}}

            Wymagania:
            - Kierunki maja byc realnie rozne, np. blad -> poprawka, przed -> po, trzyetapowy tutorial.
            - Kazdy kierunek ocen w skali 0-10: usefulness, specificity, freshness, sourceAlignment, visualPotential, hookStrength.
            - Wybierz tylko jeden kierunek do dalszego pisania.
            - Zwracaj wylacznie JSON zgodny ze schematem.
            """;
    }

    private static string CreateReviewPrompt(SelectedTopic topic, SourceAnalysis analysis, ShortScript script)
    {
        return $$"""
            Jestes recenzentem merytorycznym. Nie pisz filmu od nowa, tylko krytykuj scenariusz.

            Brief:
            {{JsonSerializer.Serialize(topic.Brief, JsonOptions)}}

            Analiza zrodla:
            {{JsonSerializer.Serialize(analysis, JsonOptions)}}

            Scenariusz:
            {{JsonSerializer.Serialize(script, JsonOptions)}}

            Sprawdz:
            - powtorzenia semantyczne,
            - oczywiste porady bez wartosci,
            - zgodnosc z faktami zrodla,
            - czy payoff spelnia obietnice hooka,
            - wykonalnosc krokow,
            - wartosc dla wskazanego odbiorcy,
            - czy kazda scena wnosi newInformation.

            Ustaw severity=error i approved=false przy niepotwierdzonym twierdzeniu, braku nowej informacji w scenie albo niespelnionej obietnicy.
            Jesli approved=false, przynajmniej jeden issue musi miec severity=error.
            Dla drobnych uwag uzyj severity=warning i zostaw approved=true.
            SuggestedFix nie moze dodawac liczb, procentow, statystyk, dat, nazw ani przykladow, ktorych nie ma w materiale zrodlowym.
            Jesli brakuje konkretnego przykladu w zrodle, zaproponuj usuniecie lub uproszczenie sceny zamiast wymyslania przykladu.
            Definicja newInformation: scena ma wnosic nowy element wzgledem poprzednich scen filmu, np. osobny krok, mechanizm, ograniczenie albo rezultat. To nie znaczy, ze scena ma dodawac fakt spoza zrodla.
            Nie oznaczaj sceny jako brak newInformation tylko dlatego, ze jej krok jest juz w analizie zrodla. Scena ma byc oparta na zrodle.
            Jesli scene_01, scene_02 i scene_03 pokazuja trzy rozne kroki ze zrodla, to spelniaja wymog newInformation nawet wtedy, gdy kazdy krok jest juz wymieniony w zrodle.
            Zwracaj wylacznie JSON zgodny ze schematem.
            """;
    }

    private static string CreateVisualPlanPrompt(SelectedTopic topic, SourceAnalysis analysis, ShortScript script)
    {
        return $$"""
            Stworz osobny plan wizualny do zatwierdzonego scenariusza. Nie zmieniaj tresci lektora.

            Brief:
            {{JsonSerializer.Serialize(topic.Brief, JsonOptions)}}

            Analiza zrodla:
            {{JsonSerializer.Serialize(analysis, JsonOptions)}}

            Scenariusz:
            {{JsonSerializer.Serialize(script, JsonOptions)}}

            Dla segmentow hook, scene_01, scene_02 itd. oraz ending okresl:
            - co dokladnie ma byc widoczne,
            - co wykonuje osoba,
            - najwazniejszy obiekt,
            - rodzaj kadru,
            - poczatek i koniec ruchu,
            - rezultat do pokazania,
            - czego nie wolno pokazywac,
            - 3-4 konkretne zapytania Pexels po angielsku.

            Unikaj przypadkowego B-rollu, selfie, beauty routine i stockow niezwiazanych z krokiem.
            Zwracaj wylacznie JSON zgodny ze schematem.
            """;
    }

    private static object CreateSourceAnalysisSchema()
    {
        var stringSchema = new { type = "string" };
        var stringArraySchema = new { type = "array", items = stringSchema };

        return new
        {
            type = "object",
            properties = new
            {
                mainThesis = stringSchema,
                facts = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = stringSchema,
                            text = stringSchema,
                            evidence = stringSchema
                        },
                        required = new[] { "id", "text", "evidence" },
                        additionalProperties = false
                    }
                },
                steps = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = stringSchema,
                            text = stringSchema,
                            sourceFactIds = stringArraySchema
                        },
                        required = new[] { "id", "text", "sourceFactIds" },
                        additionalProperties = false
                    }
                },
                examples = stringArraySchema,
                limitations = stringArraySchema,
                riskyClaims = stringArraySchema,
                mostUsefulFragment = stringSchema
            },
            required = new[] { "mainThesis", "facts", "steps", "examples", "limitations", "riskyClaims", "mostUsefulFragment" },
            additionalProperties = false
        };
    }

    private static object CreateConceptSelectionSchema()
    {
        var stringSchema = new { type = "string" };
        var integerSchema = new { type = "integer", minimum = 0, maximum = 10 };

        return new
        {
            type = "object",
            properties = new
            {
                directions = new
                {
                    type = "array",
                    minItems = 3,
                    maxItems = 3,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = stringSchema,
                            name = stringSchema,
                            structure = stringSchema,
                            hookAngle = stringSchema,
                            payoff = stringSchema,
                            scores = new
                            {
                                type = "object",
                                properties = new
                                {
                                    usefulness = integerSchema,
                                    specificity = integerSchema,
                                    freshness = integerSchema,
                                    sourceAlignment = integerSchema,
                                    visualPotential = integerSchema,
                                    hookStrength = integerSchema
                                },
                                required = new[] { "usefulness", "specificity", "freshness", "sourceAlignment", "visualPotential", "hookStrength" },
                                additionalProperties = false
                            }
                        },
                        required = new[] { "id", "name", "structure", "hookAngle", "payoff", "scores" },
                        additionalProperties = false
                    }
                },
                selectedDirectionId = stringSchema,
                selectedReason = stringSchema
            },
            required = new[] { "directions", "selectedDirectionId", "selectedReason" },
            additionalProperties = false
        };
    }

    private static object CreateScriptSchema()
    {
        var stringSchema = new { type = "string" };
        var stringArraySchema = new { type = "array", items = stringSchema };

        return new
        {
            type = "object",
            properties = new
            {
                title = stringSchema,
                hook = stringSchema,
                hookOnScreenText = stringSchema,
                scenes = new
                {
                    type = "array",
                    minItems = 1,
                    maxItems = 5,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            role = new { type = "string", @enum = new[] { "problem", "mechanism", "action", "proof", "payoff", "cta" } },
                            voiceOver = stringSchema,
                            sourceFactIds = stringArraySchema,
                            newInformation = stringSchema,
                            onScreenEmphasis = stringSchema,
                            onScreenText = stringSchema,
                            estimatedWords = new { type = "integer", minimum = 1, maximum = 30 },
                            sceneGoal = stringSchema
                        },
                        required = new[] { "role", "voiceOver", "sourceFactIds", "newInformation", "onScreenEmphasis", "estimatedWords", "sceneGoal" },
                        additionalProperties = false
                    }
                },
                ending = stringSchema,
                endingOnScreenText = stringSchema
            },
            required = new[] { "title", "hook", "hookOnScreenText", "scenes", "ending", "endingOnScreenText" },
            additionalProperties = false
        };
    }

    private static object CreateContentReviewSchema()
    {
        var stringSchema = new { type = "string" };
        var stringArraySchema = new { type = "array", items = stringSchema };

        return new
        {
            type = "object",
            properties = new
            {
                issues = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            severity = new { type = "string", @enum = new[] { "info", "warning", "error" } },
                            segment = stringSchema,
                            code = stringSchema,
                            message = stringSchema,
                            suggestedFix = stringSchema
                        },
                        required = new[] { "severity", "segment", "code", "message", "suggestedFix" },
                        additionalProperties = false
                    }
                },
                repetitionCheck = stringSchema,
                obviousAdviceCheck = stringSchema,
                sourceComparison = stringSchema,
                promiseCheck = stringSchema,
                feasibilityCheck = stringSchema,
                audienceValueCheck = stringSchema,
                suggestedFixes = stringArraySchema,
                usefulnessScore = new { type = "integer", minimum = 0, maximum = 10 },
                approved = new { type = "boolean" }
            },
            required = new[] { "issues", "repetitionCheck", "obviousAdviceCheck", "sourceComparison", "promiseCheck", "feasibilityCheck", "audienceValueCheck", "suggestedFixes", "usefulnessScore", "approved" },
            additionalProperties = false
        };
    }

    private static object CreateVisualPlanSchema()
    {
        var stringSchema = new { type = "string" };
        var stringArraySchema = new { type = "array", minItems = 2, maxItems = 4, items = stringSchema };

        return new
        {
            type = "object",
            properties = new
            {
                globalAvoidVisuals = stringSchema,
                segments = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            segmentName = stringSchema,
                            visibleContent = stringSchema,
                            personAction = stringSchema,
                            primaryObject = stringSchema,
                            shotType = stringSchema,
                            movementStart = stringSchema,
                            movementEnd = stringSchema,
                            resultToShow = stringSchema,
                            avoidVisuals = stringSchema,
                            searchPhrases = stringArraySchema
                        },
                        required = new[] { "segmentName", "visibleContent", "personAction", "primaryObject", "shotType", "movementStart", "movementEnd", "resultToShow", "avoidVisuals", "searchPhrases" },
                        additionalProperties = false
                    }
                }
            },
            required = new[] { "globalAvoidVisuals", "segments" },
            additionalProperties = false
        };
    }

    private static void NormalizeSourceAnalysis(SourceAnalysis analysis, SelectedTopic topic)
    {
        analysis.MainThesis = string.IsNullOrWhiteSpace(analysis.MainThesis)
            ? ShortenSentence(topic.SourceText, 180)
            : NormalizeWhitespace(analysis.MainThesis);
        analysis.MostUsefulFragment = string.IsNullOrWhiteSpace(analysis.MostUsefulFragment)
            ? analysis.MainThesis
            : NormalizeWhitespace(analysis.MostUsefulFragment);

        if (analysis.Facts.Count == 0)
        {
            analysis.Facts.Add(new SourceFact
            {
                Id = "F1",
                Text = analysis.MainThesis,
                Evidence = ShortenSentence(topic.SourceText, 220)
            });
        }

        for (var i = 0; i < analysis.Facts.Count; i++)
        {
            analysis.Facts[i].Id = string.IsNullOrWhiteSpace(analysis.Facts[i].Id) ? $"F{i + 1}" : NormalizeWhitespace(analysis.Facts[i].Id);
            analysis.Facts[i].Text = NormalizeWhitespace(analysis.Facts[i].Text);
            analysis.Facts[i].Evidence = string.IsNullOrWhiteSpace(analysis.Facts[i].Evidence)
                ? analysis.Facts[i].Text
                : NormalizeWhitespace(analysis.Facts[i].Evidence);
        }

        for (var i = 0; i < analysis.Steps.Count; i++)
        {
            analysis.Steps[i].Id = string.IsNullOrWhiteSpace(analysis.Steps[i].Id) ? $"S{i + 1}" : NormalizeWhitespace(analysis.Steps[i].Id);
            analysis.Steps[i].Text = NormalizeWhitespace(analysis.Steps[i].Text);
            if (analysis.Steps[i].SourceFactIds.Count == 0)
            {
                analysis.Steps[i].SourceFactIds.Add(analysis.Facts[0].Id);
            }
        }
    }

    private static void LogSourceAnalysisDiagnostics(
        GenerationDebugLogger logger,
        string label,
        SourceAnalysisDiagnostics diagnostics)
    {
        logger.Info($"Source analysis diagnostics ({label}): issues={diagnostics.Issues.Count}; blocking={diagnostics.HasBlockingIssues}");
        foreach (var issue in diagnostics.Issues)
        {
            var message = $"Source analysis issue ({label}) [{issue.Severity}] {issue.Field}/{issue.Code}: {issue.Message} Value={issue.Value}";
            if (issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                logger.Warning(message);
            }
            else
            {
                logger.Info(message);
            }
        }
    }

    private static SourceAnalysis CreateFallbackSourceAnalysis(SelectedTopic topic)
    {
        var facts = ExtractSourceSentences(topic.SourceText)
            .Take(5)
            .Select((sentence, index) => new SourceFact
            {
                Id = $"F{index + 1}",
                Text = sentence,
                Evidence = sentence
            })
            .ToList();

        if (facts.Count == 0)
        {
            facts.Add(new SourceFact { Id = "F1", Text = topic.Title, Evidence = topic.Title });
        }

        var steps = facts
            .Where(fact => HasActionLikeText(fact.Text))
            .Take(4)
            .Select((fact, index) => new SourceStep
            {
                Id = $"S{index + 1}",
                Text = fact.Text,
                SourceFactIds = [fact.Id]
            })
            .ToList();

        return new SourceAnalysis
        {
            MainThesis = facts[0].Text,
            Facts = facts,
            Steps = steps,
            Examples = [],
            Limitations = topic.SourceText.Contains("Nie dodawaj", StringComparison.OrdinalIgnoreCase)
                ? ["Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych."]
                : [],
            RiskyClaims = [],
            MostUsefulFragment = facts[0].Text
        };
    }

    private static void NormalizeConceptSelection(ScriptConceptSelection selection)
    {
        selection.Directions = selection.Directions
            .Where(direction => !string.IsNullOrWhiteSpace(direction.Name))
            .Take(3)
            .ToList();

        for (var i = 0; i < selection.Directions.Count; i++)
        {
            var direction = selection.Directions[i];
            direction.Id = string.IsNullOrWhiteSpace(direction.Id) ? $"C{i + 1}" : NormalizeWhitespace(direction.Id);
            direction.Name = NormalizeWhitespace(direction.Name);
            direction.Structure = NormalizeWhitespace(direction.Structure);
            direction.HookAngle = NormalizeWhitespace(direction.HookAngle);
            direction.Payoff = NormalizeWhitespace(direction.Payoff);
        }

        if (string.IsNullOrWhiteSpace(selection.SelectedDirectionId) && selection.Directions.Count > 0)
        {
            selection.SelectedDirectionId = selection.Directions.OrderByDescending(direction => direction.TotalScore).First().Id;
        }
    }

    private static ScriptConceptSelection CreateFallbackConceptSelection(SelectedTopic topic, SourceAnalysis analysis)
    {
        var directions = new List<ScriptConcept>
        {
            CreateConcept("C1", "Blad -> poprawka", "Nazwij blad, pokaz mechanizm, daj poprawke i rezultat.", "Zaczynasz od chaosu, bo wybierasz zbyt duzo naraz?", analysis.MostUsefulFragment, 8, 8, 6, 9, 8, 8),
            CreateConcept("C2", "Przed -> po", "Pokaz stan przed, jeden konkretny krok i widoczny efekt po.", "Przed startem dnia widzisz wszystko naraz?", analysis.MostUsefulFragment, 8, 7, 7, 9, 9, 7),
            CreateConcept("C3", "Trzyetapowy tutorial", "Podaj sekwencje krokow bez coachingu i zamknij zadaniem.", "W 25 sekund wybierz pierwszy priorytet bez rozkminiania.", analysis.MostUsefulFragment, 9, 9, 6, 9, 7, 8)
        };

        var selected = directions.OrderByDescending(direction => direction.TotalScore).First();
        return new ScriptConceptSelection
        {
            Directions = directions,
            SelectedDirectionId = selected.Id,
            SelectedReason = $"Najlepiej pasuje do briefu: {topic.Brief.ContentType}, {topic.Brief.Tone}."
        };
    }

    private static ScriptConcept CreateConcept(
        string id,
        string name,
        string structure,
        string hookAngle,
        string payoff,
        int usefulness,
        int specificity,
        int freshness,
        int sourceAlignment,
        int visualPotential,
        int hookStrength)
    {
        return new ScriptConcept
        {
            Id = id,
            Name = name,
            Structure = structure,
            HookAngle = hookAngle,
            Payoff = payoff,
            Scores = new ConceptScores
            {
                Usefulness = usefulness,
                Specificity = specificity,
                Freshness = freshness,
                SourceAlignment = sourceAlignment,
                VisualPotential = visualPotential,
                HookStrength = hookStrength
            }
        };
    }

    private static void NormalizeReview(ContentReview review)
    {
        review.UsefulnessScore = Math.Clamp(review.UsefulnessScore, 0, 10);
        review.Approved = review.Approved && !review.HasCriticalErrors;
    }

    internal static void SanitizeReviewAgainstSource(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortScript script,
        ContentReview review,
        GenerationDebugLogger? logger = null)
    {
        var changed = false;
        var hookPromiseDowngraded = false;
        var sourceStepCoverage = ScriptCoversSourceSteps(analysis, script);
        var sceneProgression = ScenesHaveDistinctNewInformation(script);
        logger?.Info($"Content review sanitation started. approved={review.Approved}; critical={review.HasCriticalErrors}; issues={review.Issues.Count}; sourceStepCoverage={sourceStepCoverage}; sceneProgression={sceneProgression}; promiseCheck=\"{ShortenSentence(review.PromiseCheck, 160)}\"");
        foreach (var issue in review.Issues)
        {
            if (IsFalseNewInformationComplaint(issue, sourceStepCoverage, sceneProgression))
            {
                logger?.Warning($"Content review issue downgraded because source-backed steps count as newInformation progression: {issue.Segment}/{issue.Code}");
                issue.Severity = "info";
                issue.Message = $"{issue.Message} [Zdegradowano: scena moze wnosic osobny krok ze zrodla jako newInformation.]";
                changed = true;
                continue;
            }

            if (IsFalseHookPromiseComplaint(topic, analysis, script, issue, sourceStepCoverage))
            {
                logger?.Warning($"Content review issue downgraded because hook/payoff is source-aligned: {issue.Segment}/{issue.Code}");
                issue.Severity = "warning";
                issue.Message = $"{issue.Message} [Zdegradowano: hook i payoff sa powiazane ze zrodlem oraz krokami scenariusza.]";
                changed = true;
                hookPromiseDowngraded = true;
            }
        }

        if (!changed)
        {
            logger?.Info("Content review sanitation finished without changes.");
            return;
        }

        if (!review.HasCriticalErrors)
        {
            review.Approved = true;
            if (hookPromiseDowngraded)
            {
                review.PromiseCheck = "Hook i payoff sa zgodne ze zrodlem oraz krokami scenariusza po sanityzacji recenzji.";
                logger?.Warning("Content review promiseCheck neutralized after source-aligned hook/payoff downgrade.");
            }

            if (review.UsefulnessScore == 0)
            {
                review.UsefulnessScore = 7;
            }
        }

        logger?.Info($"Content review sanitation finished. approved={review.Approved}; critical={review.HasCriticalErrors}; usefulness={review.UsefulnessScore}; promiseCheck=\"{ShortenSentence(review.PromiseCheck, 160)}\"");
    }

    private static bool IsFalseNewInformationComplaint(
        ContentReviewIssue issue,
        bool sourceStepCoverage,
        bool sceneProgression)
    {
        if (!sourceStepCoverage || !sceneProgression)
        {
            return false;
        }

        var code = issue.Code.ToLowerInvariant();
        if (!code.Contains("newinformation", StringComparison.OrdinalIgnoreCase)
            && !code.Contains("new_information", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = SourceAnalysisDiagnosticsService.Normalize($"{issue.Message} {issue.SuggestedFix}");
        if (text.Contains("nie wnosi", StringComparison.OrdinalIgnoreCase)
            || text.Contains("brak nowej", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no new", StringComparison.OrdinalIgnoreCase)
            || text.Contains("nonew", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return text.Contains("zrodla", StringComparison.OrdinalIgnoreCase)
            || text.Contains("zrodle", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tezie", StringComparison.OrdinalIgnoreCase)
            || text.Contains("fakt", StringComparison.OrdinalIgnoreCase)
            || text.Contains("source", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFalseHookPromiseComplaint(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortScript script,
        ContentReviewIssue issue,
        bool sourceStepCoverage)
    {
        if (!sourceStepCoverage)
        {
            return false;
        }

        var code = issue.Code.ToLowerInvariant();
        var mentionsHookOrPayoff = code.Contains("hook", StringComparison.OrdinalIgnoreCase)
            || code.Contains("payoff", StringComparison.OrdinalIgnoreCase)
            || code.Contains("proof", StringComparison.OrdinalIgnoreCase)
            || code.Contains("promise", StringComparison.OrdinalIgnoreCase);
        if (!mentionsHookOrPayoff)
        {
            return false;
        }

        var hookTerms = SourceAnalysisDiagnosticsService
            .ExtractTerms(SourceAnalysisDiagnosticsService.Normalize($"{script.Hook} {topic.Brief.ViewerProblem}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceTerms = SourceAnalysisDiagnosticsService
            .ExtractTerms(SourceAnalysisDiagnosticsService.Normalize($"{topic.SourceText} {analysis.MainThesis} {analysis.MostUsefulFragment}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var endingAndSceneTerms = SourceAnalysisDiagnosticsService
            .ExtractTerms(SourceAnalysisDiagnosticsService.Normalize($"{script.Ending} {string.Join(" ", script.Scenes.Select(scene => scene.VoiceOver))}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return hookTerms.Overlaps(sourceTerms) && endingAndSceneTerms.Overlaps(sourceTerms);
    }

    private static bool ScriptCoversSourceSteps(SourceAnalysis analysis, ShortScript script)
    {
        var sourceSteps = analysis.Steps
            .Select(step => SourceAnalysisDiagnosticsService.Normalize(step.Text))
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .ToList();
        if (sourceSteps.Count == 0)
        {
            return false;
        }

        var scriptText = SourceAnalysisDiagnosticsService.Normalize(string.Join(
            " ",
            script.Hook,
            script.Ending,
            string.Join(" ", script.Scenes.Select(scene => $"{scene.VoiceOver} {scene.NewInformation}"))));
        var coveredSteps = sourceSteps.Count(step => SourceAnalysisDiagnosticsService.IsSupported(scriptText, step));
        return coveredSteps >= Math.Min(sourceSteps.Count, 2);
    }

    private static bool ScenesHaveDistinctNewInformation(ShortScript script)
    {
        var newInformation = script.Scenes
            .Select(scene => NormalizeForComparison(scene.NewInformation))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        return newInformation.Count == script.Scenes.Count
            && newInformation.Distinct(StringComparer.OrdinalIgnoreCase).Count() == newInformation.Count;
    }

    private static bool ShouldRebuildFromSource(
        ContentReview review,
        ShortDiagnosticsReport diagnostics,
        SourceAnalysis analysis)
    {
        return (analysis.Steps.Count > 0 || analysis.Facts.Count > 0)
            && (review.HasCriticalErrors
                || !review.Approved
                || diagnostics.Summary.HasUnsupportedClaims
                || HasUnsafeReviewSuggestion(review));
    }

    private static bool HasUnsafeReviewSuggestion(ContentReview review)
    {
        var text = SourceAnalysisDiagnosticsService.Normalize(string.Join(
            " ",
            review.SuggestedFixes,
            review.Issues.Select(issue => issue.SuggestedFix)));
        return text.Contains("procent", StringComparison.OrdinalIgnoreCase)
            || text.Contains("statyst", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(text, "\\b\\d+(?:[,.]\\d+)?\\s*(?:%|procent|proc|sek|min|godz)?\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static ShortScript BuildSourceSafeScript(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortScript original)
    {
        var script = new ShortScript
        {
            Title = BuildSourceSafeTitle(topic, original),
            SelectedConceptId = original.SelectedConceptId,
            Hook = BuildSourceSafeHook(topic, analysis),
            HookOnScreenText = ShortenForScreen(BuildSourceSafeHook(topic, analysis), 42),
            HookSearchPhrase = BuildSearchPhraseFromText($"{topic.Title} {analysis.MainThesis}")
        };

        script.Scenes.AddRange(analysis.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.Text))
            .Take(4)
            .Select((step, index) => BuildSourceStepScene(topic, analysis, step, index)));

        if (script.Scenes.Count == 0)
        {
            script.Scenes.AddRange(CreateTopicAwareFallbackScenes(topic)
                .Take(3)
                .Select((scene, index) => PrepareSourceSafeFallbackScene(scene, analysis, index)));
        }

        script.Ending = analysis.Steps.Count > 0
            ? BuildEndingFromSourceSteps(analysis, original)
            : BuildEndingFromSafeScenes(script.Scenes);
        script.EndingOnScreenText = ShortenForScreen(script.Ending, 42);
        script.EndingSearchPhrase = BuildSearchPhraseFromText($"{topic.Title} {analysis.MostUsefulFragment} {script.Ending}");
        return script;
    }

    private static string BuildEndingFromSafeScenes(IReadOnlyList<ScriptScene> scenes)
    {
        var steps = scenes
            .Where(scene => scene.Role is "action" or "proof" or "payoff")
            .Select(scene => RemoveTrailingPunctuation(scene.VoiceOver))
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .Take(3)
            .ToList();
        if (steps.Count == 0)
        {
            steps = scenes
                .Select(scene => RemoveTrailingPunctuation(scene.VoiceOver))
                .Where(step => !string.IsNullOrWhiteSpace(step))
                .Take(2)
                .ToList();
        }

        if (steps.Count == 0)
        {
            return "Wybierz jeden maly krok i sprawdz efekt jeszcze dzis.";
        }

        var first = steps[0];
        var tail = steps.Skip(1).ToList();
        var sentence = tail.Count == 0
            ? first
            : $"{first}, {JoinWithPolishAnd(tail)}";
        return EnsureSentence(CapitalizeFirst(sentence));
    }

    private static ScriptScene PrepareSourceSafeFallbackScene(ScriptScene scene, SourceAnalysis analysis, int index)
    {
        scene.Role = string.IsNullOrWhiteSpace(scene.Role)
            ? index switch
            {
                0 => "problem",
                1 => "action",
                _ => "proof"
            }
            : scene.Role;
        scene.SourceFactIds = [analysis.Facts.FirstOrDefault()?.Id ?? "F1"];
        scene.NewInformation = string.IsNullOrWhiteSpace(scene.NewInformation)
            ? EnsureSentence($"Krok ze zrodla: {RemoveTrailingPunctuation(scene.VoiceOver)}")
            : scene.NewInformation;
        scene.OnScreenEmphasis = string.IsNullOrWhiteSpace(scene.OnScreenEmphasis)
            ? scene.OnScreenText
            : scene.OnScreenEmphasis;
        scene.EstimatedWords = scene.EstimatedWords <= 0 ? CountWords(scene.VoiceOver) : scene.EstimatedWords;
        return scene;
    }

    private static string BuildSourceSafeTitle(SelectedTopic topic, ShortScript original)
    {
        if (!ContainsUnsupportedNumberPhrase(original.Title, topic.SourceText)
            && !string.IsNullOrWhiteSpace(original.Title))
        {
            return original.Title;
        }

        return topic.Title;
    }

    private static string BuildSourceSafeHook(SelectedTopic topic, SourceAnalysis analysis)
    {
        var normalized = SourceAnalysisDiagnosticsService.Normalize($"{topic.Title} {topic.SourceText} {analysis.MainThesis}");
        if (normalized.Contains("reset", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("biurka", StringComparison.OrdinalIgnoreCase))
        {
            return "Jak szybko zresetowac biurko po pracy?";
        }

        if (normalized.Contains("skaner 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("skanowanie 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("fotogrametr", StringComparison.OrdinalIgnoreCase))
        {
            return "Telefon moze zrobic prosty skan 3D?";
        }

        if (normalized.Contains("minimalizm", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("powiadom", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ekran glown", StringComparison.OrdinalIgnoreCase))
        {
            return "Telefon rozprasza po odblokowaniu?";
        }

        if (normalized.Contains("1 min", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("planowania", StringComparison.OrdinalIgnoreCase))
        {
            return "Jak zaplanowac poranek w 1 minute?";
        }

        if (normalized.Contains("hasl", StringComparison.OrdinalIgnoreCase))
        {
            return "Powtarzasz to samo haslo w kilku miejscach?";
        }

        if (normalized.Contains("notatk", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("nagran", StringComparison.OrdinalIgnoreCase))
        {
            return "Gubisz decyzje po nagraniu lub spotkaniu?";
        }

        var thesis = RemoveTrailingPunctuation(analysis.MainThesis);
        return string.IsNullOrWhiteSpace(thesis)
            ? $"Jaki pierwszy krok zrobic w temacie: {ShortenSentence(topic.Title, 70)}?"
            : $"{CapitalizeFirst(ShortenSentence(thesis, 85))}?";
    }

    private static ScriptScene BuildSourceStepScene(
        SelectedTopic topic,
        SourceAnalysis analysis,
        SourceStep step,
        int index)
    {
        var voiceOver = EnsureSentence(CapitalizeFirst(EnrichSourceStepVoiceOver(topic, step.Text)));
        var searchPhrase = BuildSearchPhraseForSourceStep(topic, step.Text);
        var avoidVisuals = BuildDomainAvoidVisuals(topic, step.Text);
        return new ScriptScene
        {
            Role = "action",
            VoiceOver = voiceOver,
            SourceFactIds = ResolveStepFactIds(step, analysis),
            NewInformation = BuildSourceStepNewInformation(voiceOver, index),
            OnScreenText = ShortenForScreen(CapitalizeFirst(voiceOver), 42),
            OnScreenEmphasis = ShortenForScreen(CapitalizeFirst(voiceOver), 42),
            EstimatedWords = CountWords(voiceOver),
            VisualDescription = $"Osoba wykonuje krok ze zrodla: {voiceOver}",
            SearchPhrase = searchPhrase,
            SearchPhrases = BuildSearchPhrasesForSourceStep(topic, step.Text, searchPhrase),
            AvoidVisuals = string.IsNullOrWhiteSpace(avoidVisuals)
                ? "random selfie, unrelated beauty routine, generic social media recording, unrelated b-roll"
                : $"random selfie, unrelated beauty routine, generic social media recording, unrelated b-roll, {avoidVisuals}",
            SceneGoal = "Pokazac jeden krok wynikajacy bezposrednio ze zrodla."
        };
    }

    private static string EnrichSourceStepVoiceOver(SelectedTopic topic, string step)
    {
        var normalized = SourceAnalysisDiagnosticsService.Normalize($"{topic.Title} {topic.SourceText} {step}");
        if (!IsScanner3DContext(normalized))
        {
            return step;
        }

        if (normalized.Contains("wybierz", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("obiekt", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("faktur", StringComparison.OrdinalIgnoreCase))
        {
            return "Wybierz maly nieruchomy obiekt z wyrazna faktura";
        }

        if (normalized.Contains("sprawdz", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            return "Sprawdz w aplikacji, czy model 3D nie ma brakujacych fragmentow";
        }

        return step;
    }

    private static List<string> ResolveStepFactIds(SourceStep step, SourceAnalysis analysis)
    {
        var validFactIds = analysis.Facts
            .Select(fact => fact.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = step.SourceFactIds
            .Where(id => validFactIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count > 0)
        {
            return ids;
        }

        var normalizedStep = SourceAnalysisDiagnosticsService.Normalize(step.Text);
        var matchingFact = analysis.Facts.FirstOrDefault(fact =>
            SourceAnalysisDiagnosticsService.Normalize(fact.Text).Contains(normalizedStep, StringComparison.OrdinalIgnoreCase)
            || SourceAnalysisDiagnosticsService.Normalize(fact.Evidence).Contains(normalizedStep, StringComparison.OrdinalIgnoreCase));
        return [matchingFact?.Id ?? validFactIds.FirstOrDefault() ?? "F1"];
    }

    private static string BuildSourceStepNewInformation(string step, int index)
    {
        var detail = RemoveLeadingActionVerb(RemoveTrailingPunctuation(step));
        return EnsureSentence($"Krok ze zrodla: {detail}");
    }

    private static string BuildSearchPhraseForSourceStep(SelectedTopic topic, string step)
    {
        var normalized = SourceAnalysisDiagnosticsService.Normalize($"{topic.Title} {step}");
        if (IsAiNotesContext(normalized))
        {
            return BuildAiNotesSearchPhrases(step).First();
        }

        if (IsScanner3DContext(normalized))
        {
            return BuildScanner3DSearchPhrases(step).First();
        }

        if (normalized.Contains("biurk", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("smieci", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("odloz", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("kartke", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Contains("kartke", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("zadaniem", StringComparison.OrdinalIgnoreCase)
                    ? "person writing task on paper at desk"
                    : "person organizing desk after work";
        }

        if (normalized.Contains("skaner 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("skanowanie 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("model 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("fotogrametr", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("obiekt", StringComparison.OrdinalIgnoreCase))
        {
            return "person scanning small object with smartphone";
        }

        if (normalized.Contains("telefon", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("aplikac", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("powiadom", StringComparison.OrdinalIgnoreCase))
        {
            return "person organizing smartphone home screen apps";
        }

        if (normalized.Contains("hasl", StringComparison.OrdinalIgnoreCase))
        {
            return "person using password manager on laptop";
        }

        return BuildSearchPhraseFromText($"{step} {topic.Title}");
    }

    private static List<string> BuildSearchPhrasesForSourceStep(
        SelectedTopic topic,
        string step,
        string primarySearchPhrase)
    {
        var normalized = SourceAnalysisDiagnosticsService.Normalize($"{topic.Title} {topic.SourceText} {step}");
        if (IsScanner3DContext(normalized))
        {
            return BuildScanner3DSearchPhrases(step)
                .Prepend(primarySearchPhrase)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
        }

        return
        [
            primarySearchPhrase,
            BuildSearchPhraseFromText($"{step} {topic.Title}")
        ];
    }

    private static bool IsScanner3DContext(string normalized)
    {
        return normalized.Contains("skaner 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("skanowanie 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("skanu 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("skan 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("model 3d", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("fotogrametr", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildScanner3DSearchPhrases(string text)
    {
        var normalized = SourceAnalysisDiagnosticsService.Normalize(text);
        if (normalized.Contains("sprawdz", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("model", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("fragment", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("podglad", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "smartphone photogrammetry 3d model preview",
                "phone 3d scan model app screen",
                "3d model preview on phone screen"
            ];
        }

        if (normalized.Contains("obejdz", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("kilku stron", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("telefonem", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "person filming small object with smartphone",
                "smartphone photogrammetry object scan",
                "phone 3d scanning object on table"
            ];
        }

        return
        [
            "smartphone photogrammetry object scan",
            "small textured object smartphone scan close up",
            "phone 3d scanning object on table"
        ];
    }

    private static bool ContainsUnsupportedNumberPhrase(string value, string sourceText)
    {
        var normalizedValue = SourceAnalysisDiagnosticsService.Normalize(value);
        var normalizedSource = SourceAnalysisDiagnosticsService.Normalize(sourceText);
        foreach (Match match in Regex.Matches(
            normalizedValue,
            "\\b\\d+(?:[,.]\\d+)?\\s*(?:%|procent|proc|sek|min|godz)?\\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var evidence = match.Value.Trim();
            if (!string.IsNullOrWhiteSpace(evidence)
                && !normalizedSource.Contains(evidence, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPromiseProblem(ContentReview review)
    {
        var text = NormalizeWhitespace(string.Join(
            " ",
            review.PromiseCheck,
            string.Join(" ", review.Issues.Select(issue => $"{issue.Code} {issue.Message}")))).ToLowerInvariant();

        return text.Contains("hook", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("notmet", StringComparison.OrdinalIgnoreCase)
                || text.Contains("not met", StringComparison.OrdinalIgnoreCase)
                || text.Contains("nie jest spe", StringComparison.OrdinalIgnoreCase)
                || text.Contains("niespeln", StringComparison.OrdinalIgnoreCase)
                || text.Contains("niespełn", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasNewInformationProblem(ContentReview review)
    {
        var text = NormalizeWhitespace(string.Join(
            " ",
            review.Issues.Select(issue => $"{issue.Code} {issue.Message}"),
            string.Join(" ", review.SuggestedFixes))).ToLowerInvariant();

        return text.Contains("newinformation", StringComparison.OrdinalIgnoreCase)
            || text.Contains("new information", StringComparison.OrdinalIgnoreCase)
            || text.Contains("nowa informac", StringComparison.OrdinalIgnoreCase)
            || text.Contains("nie wnosi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasStepClarityProblem(ContentReview review)
    {
        var text = NormalizeWhitespace(string.Join(
            " ",
            review.Issues.Select(issue => $"{issue.Code} {issue.Message}"),
            string.Join(" ", review.SuggestedFixes))).ToLowerInvariant();

        return text.Contains("step", StringComparison.OrdinalIgnoreCase)
            || text.Contains("krok", StringComparison.OrdinalIgnoreCase)
            || text.Contains("niejasn", StringComparison.OrdinalIgnoreCase);
    }

    private static void RepairHookAndEnding(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortScript script,
        GenerationDebugLogger? logger)
    {
        var normalizedSource = SourceAnalysisDiagnosticsService.Normalize(topic.SourceText);
        if (normalizedSource.Contains("1 min", StringComparison.OrdinalIgnoreCase)
            && normalizedSource.Contains("planowania", StringComparison.OrdinalIgnoreCase)
            && (normalizedSource.Contains("rano", StringComparison.OrdinalIgnoreCase)
                || normalizedSource.Contains("porann", StringComparison.OrdinalIgnoreCase)))
        {
            script.Hook = "Jak zaplanowac poranek w 1 minute?";
            script.HookOnScreenText = "Plan w 1 minute";
        }

        var repairedEnding = BuildEndingFromSourceSteps(analysis, script);
        if (!string.IsNullOrWhiteSpace(repairedEnding))
        {
            logger?.Warning($"Content review repair changed ending. Original={script.Ending}; Fixed={repairedEnding}");
            script.Ending = repairedEnding;
            script.EndingOnScreenText = ShortenForScreen(repairedEnding, 42);
            script.EndingSearchPhrase = BuildSearchPhraseFromText($"{script.Title} {topic.Title} {repairedEnding}");
        }
    }

    private static string BuildEndingFromSourceSteps(SourceAnalysis analysis, ShortScript script)
    {
        var steps = analysis.Steps
            .Select(step => NormalizeWhitespace(step.Text))
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .Take(3)
            .ToList();

        if (steps.Count == 0)
        {
            steps = script.Scenes
                .Where(scene => scene.Role is "action" or "proof" or "payoff")
                .Select(scene => NormalizeWhitespace(scene.VoiceOver))
                .Where(step => !string.IsNullOrWhiteSpace(step))
                .Take(3)
                .ToList();
        }

        if (steps.Count == 0)
        {
            return string.Empty;
        }

        var endingContext = SourceAnalysisDiagnosticsService.Normalize(string.Join(
            " ",
            analysis.MainThesis,
            analysis.MostUsefulFragment,
            string.Join(" ", steps)));
        if (IsScanner3DContext(endingContext))
        {
            return "Sprawdz w aplikacji, czy model 3D nie ma brakujacych fragmentow.";
        }

        var first = RemoveTrailingPunctuation(steps[0]);
        var tail = steps
            .Skip(1)
            .Select(RemoveTrailingPunctuation)
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .ToList();
        var sentence = tail.Count == 0
            ? first
            : $"{first}, {JoinWithPolishAnd(tail)}";
        return EnsureSentence(CapitalizeFirst(sentence));
    }

    private static void RepairSceneNewInformation(
        ShortScript script,
        SourceAnalysis analysis,
        GenerationDebugLogger? logger)
    {
        foreach (var item in script.Scenes.Select((scene, index) => new { scene, index }))
        {
            if (!NeedsNewInformationRepair(item.scene))
            {
                continue;
            }

            var fixedValue = BuildNewInformationForScene(item.scene, item.index, analysis);
            logger?.Warning($"Content review repair changed newInformation in scene_{item.index + 1:00}. Original={item.scene.NewInformation}; Fixed={fixedValue}");
            item.scene.NewInformation = fixedValue;
        }
    }

    private static void RepairStepLabels(ShortScript script, GenerationDebugLogger? logger)
    {
        foreach (var item in script.Scenes.Select((scene, index) => new { scene, index }))
        {
            var lower = item.scene.VoiceOver.ToLowerInvariant();
            var screenText = lower switch
            {
                var value when value.Contains("priorytet", StringComparison.OrdinalIgnoreCase) => "1. Priorytet",
                var value when value.Contains("male zadanie", StringComparison.OrdinalIgnoreCase) || value.Contains("małe zadanie", StringComparison.OrdinalIgnoreCase) => "2. Male zadanie",
                var value when value.Contains("nie robisz", StringComparison.OrdinalIgnoreCase) || value.Contains("odpuszcz", StringComparison.OrdinalIgnoreCase) => "3. Odpuszczasz",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(screenText))
            {
                continue;
            }

            item.scene.OnScreenText = screenText;
            item.scene.OnScreenEmphasis = screenText;
            item.scene.VisualDescription = "Close up hands writing a simple morning plan in a notebook.";
            item.scene.SearchPhrase = "person writing daily plan in notebook";
            item.scene.SearchPhrases = ["person writing daily plan in notebook", "close up writing task list notebook"];
            logger?.Warning($"Content review repair changed step label and visual query in scene_{item.index + 1:00}: {screenText}");
        }
    }

    private static bool NeedsNewInformationRepair(ScriptScene scene)
    {
        var newInfo = NormalizeForComparison(scene.NewInformation);
        var voice = NormalizeForComparison(scene.VoiceOver);
        return string.IsNullOrWhiteSpace(newInfo) || newInfo.Equals(voice, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildNewInformationForScene(ScriptScene scene, int index, SourceAnalysis analysis)
    {
        var lower = scene.VoiceOver.ToLowerInvariant();
        if (lower.Contains("chaos", StringComparison.OrdinalIgnoreCase))
        {
            return "Problem: chaos na starcie dnia.";
        }

        if (lower.Contains("minut", StringComparison.OrdinalIgnoreCase))
        {
            return "Mechanizm: jedna minuta planowania rano.";
        }

        if (lower.Contains("priorytet", StringComparison.OrdinalIgnoreCase))
        {
            return "Element planu: jeden priorytet.";
        }

        if (lower.Contains("male zadanie", StringComparison.OrdinalIgnoreCase) || lower.Contains("małe zadanie", StringComparison.OrdinalIgnoreCase))
        {
            return "Element planu: jedno male zadanie.";
        }

        if (lower.Contains("nie robisz", StringComparison.OrdinalIgnoreCase) || lower.Contains("odpuszcz", StringComparison.OrdinalIgnoreCase))
        {
            return "Element planu: jedna rzecz swiadomie odpuszczona dzisiaj.";
        }

        var matchingStep = analysis.Steps.ElementAtOrDefault(index);
        if (matchingStep is not null && !string.IsNullOrWhiteSpace(matchingStep.Text))
        {
            return EnsureSentence(CapitalizeFirst(matchingStep.Text));
        }

        return EnsureSentence($"Nowa informacja: {RemoveTrailingPunctuation(scene.VoiceOver)}");
    }

    private static string RemoveLeadingActionVerb(string value)
    {
        return Regex.Replace(
            NormalizeWhitespace(value),
            "^(zapisz|wybierz|dopisz|sprawdz|usun|wylacz|wlacz|otworz)\\s+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string RemoveTrailingPunctuation(string value)
    {
        return NormalizeWhitespace(value).TrimEnd('.', '!', '?', ':', ';', ',');
    }

    private static string JoinWithPolishAnd(IReadOnlyList<string> values)
    {
        return values.Count switch
        {
            0 => string.Empty,
            1 => values[0],
            2 => $"{values[0]} i {values[1]}",
            _ => $"{string.Join(", ", values.Take(values.Count - 1))} i {values[^1]}"
        };
    }

    private static string CapitalizeFirst(string value)
    {
        var normalized = NormalizeWhitespace(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static string NormalizeForComparison(string value)
    {
        return Regex.Replace(
            NormalizeWhitespace(value).ToLowerInvariant(),
            "[^a-z0-9ąćęłńóśźż]+",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
    }

    private static ContentReview CreateHeuristicReview(SelectedTopic topic, ShortScript script)
    {
        var report = ShortDiagnosticsService.CreateScriptDiagnostics(topic, script);
        var review = new ContentReview
        {
            RepetitionCheck = "Sprawdzono lokalnie podobienstwo newInformation i pustki w scenach.",
            ObviousAdviceCheck = "Sceny bez czasownika akcji zostaly oznaczone w diagnostyce.",
            SourceComparison = "Porownano liczby i mocne obietnice ze zrodlem.",
            PromiseCheck = "Hook i ending sa oceniane przez bramke jakosci.",
            FeasibilityCheck = "Sceny z czasownikami akcji uznano za wykonalne.",
            AudienceValueCheck = $"Odbiorca: {topic.Brief.Audience}; cel: {topic.Brief.DesiredOutcome}.",
            UsefulnessScore = report.Issues.Any(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)) ? 5 : 8,
            Approved = !report.Summary.HasUnsupportedClaims && report.Summary.ErrorCount == 0
        };

        review.Issues.AddRange(report.Issues.Select(issue => new ContentReviewIssue
        {
            Severity = issue.Severity,
            Segment = issue.Segment,
            Code = issue.Code,
            Message = issue.Message,
            SuggestedFix = issue.Recommendation
        }));

        return review;
    }

    private static void NormalizeVisualPlan(VisualPlan visualPlan, ShortScript script)
    {
        visualPlan.GlobalAvoidVisuals = string.IsNullOrWhiteSpace(visualPlan.GlobalAvoidVisuals)
            ? "random b-roll, unrelated selfie, beauty routine, generic social media recording"
            : NormalizeWhitespace(visualPlan.GlobalAvoidVisuals);

        var expectedNames = CreateSegmentNames(script).ToHashSet(StringComparer.OrdinalIgnoreCase);
        visualPlan.Segments = visualPlan.Segments
            .Where(segment => expectedNames.Contains(segment.SegmentName))
            .ToList();

        foreach (var segment in visualPlan.Segments)
        {
            segment.SegmentName = NormalizeWhitespace(segment.SegmentName);
            segment.VisibleContent = NormalizeWhitespace(segment.VisibleContent);
            segment.PersonAction = NormalizeWhitespace(segment.PersonAction);
            segment.PrimaryObject = NormalizeWhitespace(segment.PrimaryObject);
            segment.ShotType = NormalizeWhitespace(segment.ShotType);
            segment.MovementStart = NormalizeWhitespace(segment.MovementStart);
            segment.MovementEnd = NormalizeWhitespace(segment.MovementEnd);
            segment.ResultToShow = NormalizeWhitespace(segment.ResultToShow);
            segment.AvoidVisuals = NormalizeWhitespace(segment.AvoidVisuals);
            segment.SearchPhrases = segment.SearchPhrases
                .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
                .Select(NormalizeWhitespace)
                .Where(phrase => !ContainsPolishCharacters(phrase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
        }

        foreach (var missingName in expectedNames.Where(name => visualPlan.Segments.All(segment => !segment.SegmentName.Equals(name, StringComparison.OrdinalIgnoreCase))))
        {
            visualPlan.Segments.Add(CreateFallbackVisualSegment(missingName, script));
        }
    }

    private static VisualPlan CreateFallbackVisualPlan(ShortScript script)
    {
        return new VisualPlan
        {
            GlobalAvoidVisuals = "random b-roll, unrelated selfie, beauty routine, generic social media recording",
            Segments = CreateSegmentNames(script).Select(name => CreateFallbackVisualSegment(name, script)).ToList()
        };
    }

    private static VisualPlanSegment CreateFallbackVisualSegment(string segmentName, ShortScript script)
    {
        var text = segmentName switch
        {
            "hook" => script.Hook,
            "ending" => script.Ending,
            _ => script.Scenes.ElementAtOrDefault(ParseSceneIndex(segmentName))?.VoiceOver ?? script.Title
        };
        var phrase = BuildSearchPhraseFromText($"{text} {script.Title}");
        return new VisualPlanSegment
        {
            SegmentName = segmentName,
            VisibleContent = $"Osoba wykonuje krok zwiazany z: {ShortenSentence(text, 80)}",
            PersonAction = "Osoba pracuje przy biurku i pokazuje rezultat dzialania.",
            PrimaryObject = "notebook, phone or laptop screen",
            ShotType = "close up practical desk shot",
            MovementStart = "hands enter frame with messy starting point",
            MovementEnd = "frame ends on clear result",
            ResultToShow = "visible completed step or simplified screen",
            AvoidVisuals = "random selfie, unrelated lifestyle b-roll, beauty routine",
            SearchPhrases = [phrase, "person planning task at desk", "close up hands writing checklist"]
        };
    }

    private static List<string> CreateSegmentNames(ShortScript script)
    {
        var names = new List<string> { "hook" };
        names.AddRange(script.Scenes.Select((_, index) => $"scene_{index + 1:00}"));
        names.Add("ending");
        return names;
    }

    private static int ParseSceneIndex(string segmentName)
    {
        return int.TryParse(segmentName.Replace("scene_", string.Empty, StringComparison.OrdinalIgnoreCase), out var value)
            ? Math.Max(value - 1, 0)
            : 0;
    }

    private static IEnumerable<string> ExtractSourceSentences(string sourceText)
    {
        return Regex.Split(sourceText, @"(?<=[.!?])\s+|\r?\n")
            .Select(NormalizeWhitespace)
            .Where(sentence => sentence.Length > 0)
            .Select(sentence => sentence.Length > 220 ? sentence[..220].TrimEnd() + "." : sentence);
    }

    private static bool HasActionLikeText(string value)
    {
        return Regex.IsMatch(
            NormalizeWhitespace(value),
            "\\b(usun|wylacz|zostaw|zapisz|wybierz|dopisz|skresl|otworz|sprawdz|wlacz|pokaz|odloz|wyrzuc)\\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static ShortScript CloneScript(ShortScript script)
    {
        return JsonSerializer.Deserialize<ShortScript>(JsonSerializer.Serialize(script, JsonOptions), JsonOptions)
            ?? script;
    }

    private static int CountScriptWords(ShortScript script)
    {
        return CountWords(script.Hook)
            + CountWords(script.Ending)
            + script.Scenes.Sum(scene => CountWords(scene.VoiceOver));
    }

    private static int CountWords(string value)
    {
        return NormalizeWhitespace(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }

    private static string TakeWords(string value, int maxWords)
    {
        var words = NormalizeWhitespace(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length <= maxWords)
        {
            return EnsureSentence(string.Join(' ', words));
        }

        return EnsureSentence(string.Join(' ', words.Take(Math.Max(maxWords, 1))));
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
        return ParseScriptOrFallback(ollamaResponse, topic, logger, null, out qualityReport);
    }

    internal static ShortScript ParseScriptOrFallback(
        string ollamaResponse,
        SelectedTopic topic,
        GenerationDebugLogger? logger,
        SourceAnalysis? analysis,
        out ScriptQualityReport qualityReport)
    {
        if (TryDeserializeScript(ollamaResponse, out var directScript, out var directError))
        {
            logger?.Info("Parsed Ollama script directly.");
            qualityReport = NormalizeScript(directScript, topic, analysis);
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
                qualityReport = NormalizeScript(extractedScript, topic, analysis);
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
            qualityReport = NormalizeScript(looseScript, topic, analysis);
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

    private static string CreatePrompt(SelectedTopic topic, SourceAnalysis analysis, ScriptConcept? selectedConcept)
    {
        return $$"""
            Napisz tresciowy scenariusz pionowego shorta po polsku wylacznie na podstawie analizy zrodla.

            Brief:
            {{JsonSerializer.Serialize(topic.Brief, JsonOptions)}}

            Wybrany kierunek:
            {{JsonSerializer.Serialize(selectedConcept, JsonOptions)}}

            Analiza zrodla:
            {{JsonSerializer.Serialize(analysis, JsonOptions)}}

            Zasady:
            - Nie dodawaj faktow spoza analizy zrodla.
            - Film ma trwac maksymalnie {{topic.Brief.DurationSeconds}} sekund.
            - Hook ma obiecac dokladnie payoff, ktory dowieziesz w ending.
            - Liczba scen wynika z tresci i czasu; nie wymuszaj trzech scen.
            - Kazda scena musi miec role: problem, mechanism, action, proof, payoff albo cta.
            - Kazda scena musi miec sourceFactIds z faktow F1, F2...
            - Pole newInformation ma nazwac informacje, ktorej nie bylo wczesniej.
            - voiceOver to tekst czytany przez lektora. Nie wolno pisac: "pierwsza scena", "widzimy", "kamera", "ujecie", "kadr".
            - onScreenEmphasis to bardzo krotki akcent ekranowy, maksymalnie 55 znakow.
            - Nie tworz jeszcze planu wizualnego, opisow kadrow ani fraz Pexels.
            - Zwracaj wylacznie JSON zgodny ze schematem.

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
                Role = ReadJsonStringProperty(body, "role"),
                VoiceOver = voiceOver,
                LegacyText = string.IsNullOrWhiteSpace(legacyText) ? null : legacyText,
                NewInformation = ReadJsonStringProperty(body, "newInformation"),
                OnScreenEmphasis = ReadJsonStringProperty(body, "onScreenEmphasis"),
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

    internal static ScriptQualityReport NormalizeScript(ShortScript script, SelectedTopic topic, SourceAnalysis? analysis = null)
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
            NormalizeScene(item.scene, topic, analysis, item.index, report);
        }

        if (script.Scenes.Count == 0)
        {
            var fallbackScene = CreateFallbackScenes(topic, 0).First();
            NormalizeScene(fallbackScene, topic, analysis, 0, report);
            script.Scenes.Add(fallbackScene);
            AddIssue(
                report,
                "warning",
                "scene_01",
                "fallback_scene",
                "Scenariusz nie mial zadnej sceny, wiec dodano jedna lokalna scene fallbackowa.",
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
        SourceAnalysis? analysis,
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
        scene.Role = NormalizeRole(scene.Role, index);
        scene.SourceFactIds = NormalizeSourceFactIds(scene.SourceFactIds, analysis, segment, report);
        scene.NewInformation = NormalizeNewInformation(scene.NewInformation, scene.VoiceOver, segment, report);
        scene.OnScreenText = NormalizeScreenText(
            string.IsNullOrWhiteSpace(scene.OnScreenText) ? scene.OnScreenEmphasis : scene.OnScreenText,
            scene.VoiceOver,
            segment,
            report);
        scene.OnScreenEmphasis = scene.OnScreenText;
        scene.EstimatedWords = scene.EstimatedWords <= 0 ? CountWords(scene.VoiceOver) : scene.EstimatedWords;
        scene.VisualDescription = NormalizeVisualDescription(scene.VisualDescription, scene, segment, report);
        scene.SearchPhrase = ResolveSceneSearchPhrase(scene, topic, segment, report);
        scene.SearchPhrases = scene.SearchPhrases
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Select(NormalizeWhitespace)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        if (scene.SearchPhrases.Count == 0)
        {
            scene.SearchPhrases.Add(scene.SearchPhrase);
        }
        scene.AvoidVisuals = string.IsNullOrWhiteSpace(scene.AvoidVisuals)
            ? "generic social media recording, unrelated beauty routine, random phone selfie"
            : NormalizeWhitespace(scene.AvoidVisuals);
        scene.SceneGoal = string.IsNullOrWhiteSpace(scene.SceneGoal)
            ? "Przekazac praktyczny krok albo korzysc dla widza."
            : NormalizeWhitespace(scene.SceneGoal);
        scene.LegacyText = null;
        scene.ExtraFields = null;
    }

    private static string NormalizeRole(string value, int index)
    {
        var normalized = NormalizeWhitespace(value).ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "problem",
            "mechanism",
            "action",
            "proof",
            "payoff",
            "cta"
        };

        if (allowed.Contains(normalized))
        {
            return normalized;
        }

        return index switch
        {
            0 => "problem",
            1 => "mechanism",
            _ => "action"
        };
    }

    private static List<string> NormalizeSourceFactIds(
        List<string> sourceFactIds,
        SourceAnalysis? analysis,
        string segment,
        ScriptQualityReport report)
    {
        var validFactIds = analysis?.Facts
            .Select(fact => fact.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        var normalized = sourceFactIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(NormalizeWhitespace)
            .Where(id => validFactIds.Count == 0 || validFactIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (normalized.Count > 0)
        {
            return normalized;
        }

        var fallback = validFactIds.FirstOrDefault() ?? "F1";
        AddIssue(
            report,
            "warning",
            segment,
            "missing_source_fact_ids",
            "Scena nie miala poprawnego powiazania z faktem zrodlowym. Dodano najbezpieczniejszy identyfikator fallbackowy.",
            string.Join(", ", sourceFactIds),
            fallback);
        return [fallback];
    }

    private static string NormalizeNewInformation(
        string value,
        string voiceOver,
        string segment,
        ScriptQualityReport report)
    {
        var normalized = NormalizeWhitespace(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return ShortenSentence(normalized, 120);
        }

        var fallback = ShortenSentence(voiceOver, 120);
        AddIssue(
            report,
            "warning",
            segment,
            "missing_new_information",
            "Scena nie nazwala nowej informacji. Uzyto skroconej wersji voiceOver jako fallbacku.",
            string.Empty,
            fallback);
        return fallback;
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
        var source = NormalizeWhitespace(topic.SourceText).ToLowerInvariant();
        var topicText = $"{title} {source}";
        if (topicText.Contains("skaner 3d", StringComparison.OrdinalIgnoreCase)
            || topicText.Contains("skanowanie 3d", StringComparison.OrdinalIgnoreCase)
            || topicText.Contains("model 3d", StringComparison.OrdinalIgnoreCase)
            || topicText.Contains("fotogrametr", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new ScriptScene
                {
                    Role = "problem",
                    VoiceOver = "Telefon moze posluzyc do prostego skanu 3D malego obiektu.",
                    NewInformation = "Telefon moze zrobic prosty skan 3D obiektu.",
                    OnScreenText = "Telefon jako skaner 3D",
                    VisualDescription = "Dlon trzyma telefon nad malym nieruchomym obiektem na dobrze oswietlonym stole.",
                    SearchPhrase = "person scanning small object with smartphone",
                    AvoidVisuals = "random selfie, unrelated phone notifications, social media feed",
                    SceneGoal = "Pokazac temat bez obiecywania profesjonalnej dokladnosci."
                },
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Wybierz maly nieruchomy obiekt z wyrazna faktura.",
                    NewInformation = "Dobry obiekt do skanu jest nieruchomy i ma wyrazna fakture.",
                    OnScreenText = "Obiekt z faktura",
                    VisualDescription = "Na stole lezy maly nieruchomy obiekt z wyrazna faktura, a telefon ustawia kadr.",
                    SearchPhrase = "small textured object smartphone scan close up",
                    SearchPhrases =
                    [
                        "small textured object smartphone scan close up",
                        "smartphone photogrammetry object scan",
                        "phone 3d scanning object on table"
                    ],
                    AvoidVisuals = "flat blank wall, moving person, unrelated app interface",
                    SceneGoal = "Dac pierwszy konkretny warunek udanego skanu."
                },
                new ScriptScene
                {
                    Role = "proof",
                    VoiceOver = "Obejdz go telefonem z kilku stron i sprawdz, czy model nie ma brakujacych fragmentow.",
                    NewInformation = "Skan wymaga ujet z kilku stron i kontroli brakujacych fragmentow.",
                    OnScreenText = "Obejdz i sprawdz model",
                    VisualDescription = "Telefon porusza sie wokol obiektu, a potem pokazuje podglad modelu 3D.",
                    SearchPhrase = "smartphone photogrammetry 3d model preview",
                    SearchPhrases =
                    [
                        "smartphone photogrammetry 3d model preview",
                        "phone 3d scan model app screen",
                        "3d model preview on phone screen"
                    ],
                    AvoidVisuals = "notification settings, random phone selfie, generic social media recording",
                    SceneGoal = "Pokazac widoczny rezultat i sposob sprawdzenia efektu."
                }
            ];
        }

        if (title.Contains("minimalizm", StringComparison.OrdinalIgnoreCase)
            || title.Contains("aplikacjach na telefonie", StringComparison.OrdinalIgnoreCase)
            || source.Contains("powiadomien", StringComparison.OrdinalIgnoreCase)
            || source.Contains("ekranie glownym", StringComparison.OrdinalIgnoreCase)
            || source.Contains("pierwszym ekranie", StringComparison.OrdinalIgnoreCase))
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
        var normalized = SourceAnalysisDiagnosticsService.Normalize(value);
        if (IsAiNotesContext(normalized))
        {
            return BuildAiNotesSearchPhrases(value).First();
        }

        if (IsScanner3DContext(normalized))
        {
            return BuildScanner3DSearchPhrases(value).First();
        }

        if (lower.Contains("notatnik")
            || lower.Contains("notes")
            || lower.Contains("notebook")
            || lower.Contains("plan")
            || lower.Contains("zapisz")
            || lower.Contains("priorytet")
            || lower.Contains("zadanie")
            || lower.Contains("odpuszcz")
            || lower.Contains("nie robisz"))
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
