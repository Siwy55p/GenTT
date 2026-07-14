using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

internal static class ShortDiagnosticsService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "temat", "roboczy", "kategoria", "scenariusz", "powinien", "krotki", "konkretny",
        "oparty", "tylko", "tych", "informacjach", "struktura", "praktyczna", "pokaz",
        "prosty", "problem", "podaj", "jeden", "maly", "krok", "sprawdzic", "efekt",
        "korzysc", "widza", "obejrzeniu", "wiedziec", "moze", "zrobic", "razu",
        "dodawaj", "statystyk", "procentow", "nazw", "firm", "aktualnych", "danych",
        "oraz", "ktory", "ktora", "ktore", "jest", "jako", "tego", "teza", "source"
    };

    private static readonly Regex ActionVerbRegex = new(
        "\\b(otworz|usun|wlacz|wylacz|sprawdz|zapisz|wybierz|skanuj|przejdz|zablokuj|dopisz|skresl|zostaw|uporzadkuj|wez|kliknij|zacznij)\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StoryboardRegex = new(
        "\\b(scena|scenie|widzimy|kamera|ujecie|uj.cie|kadr)\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NumberRegex = new(
        "\\b\\d+(?:[,.]\\d+)?\\s*(?:%|procent|proc\\.|sekund|sek|s|minut|min|godzin|godz)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void LogSummary(
        GenerationDebugLogger? logger,
        string label,
        ShortDiagnosticsReport report)
    {
        if (logger is null)
        {
            return;
        }

        logger.Info(
            $"{label} diagnostics: stage={report.Stage}; segments={report.Summary.SegmentCount}; scenes={report.Summary.SceneCount}; issues={report.Summary.IssueCount}; warnings={report.Summary.WarningCount}; errors={report.Summary.ErrorCount}; sourceCoverage={report.Summary.SourceCoverageRatio:0.###}; duration={report.Summary.EstimatedDurationSeconds:0.###}s");

        foreach (var issue in report.Issues.Where(issue => !issue.Severity.Equals("info", StringComparison.OrdinalIgnoreCase)))
        {
            logger.Warning($"{label} diagnostic issue [{issue.Severity}] {issue.Stage}/{issue.Segment}/{issue.Code}: {issue.Message} Evidence={issue.Evidence}");
        }
    }

    public static ShortDiagnosticsReport CreateScriptDiagnostics(
        SelectedTopic topic,
        ShortScript script,
        ScriptQualityReport? qualityReport = null)
    {
        var sourceKeywords = ExtractKeywords(topic.SourceText);
        var sourceKeywordSet = sourceKeywords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var segmentInputs = CreateScriptSegmentInputs(script);
        var report = CreateBaseReport("script", topic, script, sourceKeywords, segmentInputs);

        foreach (var input in segmentInputs)
        {
            var diagnostics = BuildSegmentDiagnostics(input, sourceKeywordSet);
            report.Segments.Add(diagnostics);
            AddScriptSegmentIssues(report, topic, diagnostics);
        }

        if (qualityReport is not null)
        {
            foreach (var issue in qualityReport.Issues)
            {
                AddIssue(
                    report,
                    issue.Severity,
                    "script-normalization",
                    issue.Segment,
                    issue.Code,
                    issue.Message,
                    issue.OriginalValue,
                    string.IsNullOrWhiteSpace(issue.FixedValue) ? "Sprawdz wejscie modelu i reguly normalizacji." : $"Poprawiono na: {issue.FixedValue}");
            }
        }

        CompleteSummary(report);
        return report;
    }

    public static ShortDiagnosticsReport CreateVoiceDiagnostics(
        SelectedTopic topic,
        ShortScript script,
        IReadOnlyList<VoiceSegment> voiceSegments)
    {
        var sourceKeywords = ExtractKeywords(topic.SourceText);
        var sourceKeywordSet = sourceKeywords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var report = CreateBaseReport("voice", topic, script, sourceKeywords, CreateVoiceSegmentInputs(voiceSegments));

        foreach (var input in CreateVoiceSegmentInputs(voiceSegments))
        {
            var diagnostics = BuildSegmentDiagnostics(input, sourceKeywordSet);
            report.Segments.Add(diagnostics);
            AddVoiceSegmentIssues(report, diagnostics, input.AudioPath);
        }

        if (report.Segments.Sum(segment => segment.DurationSeconds) > 25)
        {
            AddIssue(
                report,
                "warning",
                "voice",
                "all",
                "total_duration_over_target",
                "Suma segmentow audio przekracza docelowe 25 sekund.",
                $"{report.Segments.Sum(segment => segment.DurationSeconds):0.###}s",
                "Skroc hook, zakonczenie albo liczbe scen.");
        }

        CompleteSummary(report);
        return report;
    }

    public static ShortDiagnosticsReport CreateClipDiagnostics(
        SelectedTopic topic,
        ShortScript script,
        IReadOnlyList<VoiceSegment> voiceSegments,
        IReadOnlyList<DownloadedVideoClip> clips)
    {
        var sourceKeywords = ExtractKeywords(topic.SourceText);
        var sourceKeywordSet = sourceKeywords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var report = CreateBaseReport("clips", topic, script, sourceKeywords, CreateVoiceSegmentInputs(voiceSegments));
        var clipBySegment = clips.ToDictionary(clip => clip.SegmentIndex);
        var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in CreateVoiceSegmentInputs(voiceSegments))
        {
            var diagnostics = BuildSegmentDiagnostics(input, sourceKeywordSet);
            if (clipBySegment.TryGetValue(input.Index, out var clip))
            {
                diagnostics.ClipUrl = clip.PexelsUrl;
                diagnostics.PexelsRank = clip.PexelsRank;
                diagnostics.ClipSelectionReason = clip.SelectionReason;
                diagnostics.VisualDescription = string.IsNullOrWhiteSpace(diagnostics.VisualDescription)
                    ? clip.VisualDescription
                    : diagnostics.VisualDescription;

                if (!usedUrls.Add(clip.PexelsUrl))
                {
                    AddIssue(
                        report,
                        "warning",
                        "clips",
                        input.Name,
                        "duplicate_clip_url",
                        "Ten sam klip Pexels zostal uzyty wiecej niz raz.",
                        clip.PexelsUrl,
                        "Wybierz kolejnego kandydata albo zmien searchPhrase dla segmentu.");
                }

                if (clip.PexelsRank > 3)
                {
                    AddIssue(
                        report,
                        "info",
                        "clips",
                        input.Name,
                        "lower_rank_clip",
                        "Wybrany klip nie byl w pierwszej trojce wynikow Pexels.",
                        $"rank={clip.PexelsRank}; url={clip.PexelsUrl}",
                        "Sprawdz pexels-selection dla tego segmentu.");
                }
            }
            else
            {
                AddIssue(
                    report,
                    "error",
                    "clips",
                    input.Name,
                    "missing_clip",
                    "Brakuje klipu dla segmentu.",
                    input.SearchPhrase,
                    "Sprawdz pobieranie Pexels i searchPhrase.");
            }

            report.Segments.Add(diagnostics);
            AddClipSegmentIssues(report, diagnostics);
        }

        CompleteSummary(report);
        return report;
    }

    public static ShortDiagnosticsReport CreateFinalDiagnostics(
        SelectedTopic topic,
        ShortScript script,
        IReadOnlyList<VoiceSegment> voiceSegments,
        IReadOnlyList<DownloadedVideoClip> clips,
        string outputPath)
    {
        var report = CreateClipDiagnostics(topic, script, voiceSegments, clips);
        report.Stage = "final";

        if (!File.Exists(outputPath))
        {
            AddIssue(
                report,
                "error",
                "render",
                "output",
                "missing_output_file",
                "Plik short.mp4 nie istnieje po renderze.",
                outputPath,
                "Sprawdz log FFmpeg i segmenty.");
        }

        CompleteSummary(report);
        return report;
    }

    private static ShortDiagnosticsReport CreateBaseReport(
        string stage,
        SelectedTopic topic,
        ShortScript script,
        List<string> sourceKeywords,
        IReadOnlyList<SegmentInput> segmentInputs)
    {
        var scriptText = string.Join(
            " ",
            segmentInputs.Select(segment => $"{segment.VoiceOver} {segment.OnScreenText} {segment.VisualDescription} {segment.SearchPhrase}"));
        var scriptKeywords = ExtractKeywords(scriptText);
        var sourceKeywordSet = sourceKeywords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchedKeywords = scriptKeywords
            .Where(sourceKeywordSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ShortDiagnosticsReport
        {
            Stage = stage,
            TopicTitle = topic.Title,
            SourceUrl = topic.SourceUrl,
            Script = new ScriptDiagnostics
            {
                Title = script.Title,
                Hook = script.Hook,
                Ending = script.Ending,
                TotalVoiceWords = segmentInputs.Sum(segment => CountWords(segment.VoiceOver)),
                TotalVoiceCharacters = segmentInputs.Sum(segment => segment.VoiceOver.Length),
                SourceKeywords = sourceKeywords,
                ScriptKeywords = scriptKeywords,
                MatchedSourceKeywords = matchedKeywords,
                GeneratedKeywordsNotInSource = scriptKeywords
                    .Where(keyword => !sourceKeywordSet.Contains(keyword))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(25)
                    .ToList()
            },
            Summary =
            {
                SceneCount = script.Scenes.Count,
                SegmentCount = segmentInputs.Count
            }
        };
    }

    private static List<SegmentInput> CreateScriptSegmentInputs(ShortScript script)
    {
        var segments = new List<SegmentInput>
        {
            new(0, "hook", "problem", script.Hook, [], "Ustawia problem i obietnice filmu.", script.HookOnScreenText, string.Empty, script.HookSearchPhrase, string.Empty)
        };

        segments.AddRange(script.Scenes.Select((scene, index) => new SegmentInput(
            index + 1,
            $"scene_{index + 1:00}",
            string.IsNullOrWhiteSpace(scene.Role) ? "action" : scene.Role,
            scene.VoiceOver,
            scene.SourceFactIds,
            scene.NewInformation,
            scene.OnScreenText,
            scene.VisualDescription,
            scene.SearchPhrase,
            scene.SceneGoal)));

        segments.Add(new SegmentInput(
            segments.Count,
            "ending",
            "cta",
            script.Ending,
            [],
            "Domyka obietnice filmu i daje jedno zadanie.",
            script.EndingOnScreenText,
            string.Empty,
            script.EndingSearchPhrase,
            string.Empty));
        return segments;
    }

    private static List<SegmentInput> CreateVoiceSegmentInputs(IReadOnlyList<VoiceSegment> voiceSegments)
    {
        return voiceSegments
            .Select(segment => new SegmentInput(
                segment.Index,
                segment.Name,
                string.IsNullOrWhiteSpace(segment.Role) ? segment.Name : segment.Role,
                segment.Text,
                segment.SourceFactIds,
                segment.NewInformation,
                segment.OnScreenText,
                segment.VisualDescription,
                segment.SearchPhrase,
                string.Empty,
                segment.Duration,
                segment.AudioPath))
            .ToList();
    }

    private static SegmentDiagnostics BuildSegmentDiagnostics(
        SegmentInput input,
        HashSet<string> sourceKeywordSet)
    {
        var textForKeywords = $"{input.VoiceOver} {input.OnScreenText} {input.VisualDescription} {input.SearchPhrase}";
        var generatedKeywords = ExtractKeywords(textForKeywords);
        var sourceHits = generatedKeywords
            .Where(sourceKeywordSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var wordCount = CountWords(input.VoiceOver);
        var durationSeconds = input.Duration?.TotalSeconds ?? 0;

        return new SegmentDiagnostics
        {
            Index = input.Index,
            Name = input.Name,
            Role = input.Role,
            VoiceOver = input.VoiceOver,
            SourceFactIds = input.SourceFactIds,
            NewInformation = input.NewInformation,
            OnScreenText = input.OnScreenText,
            VisualDescription = input.VisualDescription,
            SearchPhrase = input.SearchPhrase,
            VoiceWordCount = wordCount,
            VoiceCharacterCount = input.VoiceOver.Length,
            OnScreenTextLength = input.OnScreenText.Length,
            DurationSeconds = durationSeconds,
            WordsPerMinute = durationSeconds <= 0 ? 0 : wordCount / durationSeconds * 60,
            HasActionVerb = HasActionVerb(input.VoiceOver),
            HasStoryboardLanguage = HasStoryboardLanguage(input.VoiceOver) || HasStoryboardLanguage(input.OnScreenText),
            HasUnsupportedNumber = false,
            HasGenericVisualDescription = IsGenericVisualDescription(input.VisualDescription),
            SourceKeywordHits = sourceHits,
            GeneratedKeywordsNotInSource = generatedKeywords
                .Where(keyword => !sourceKeywordSet.Contains(keyword))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList()
        };
    }

    private static void AddScriptSegmentIssues(
        ShortDiagnosticsReport report,
        SelectedTopic topic,
        SegmentDiagnostics segment)
    {
        if (segment.HasStoryboardLanguage)
        {
            AddIssue(report, "warning", "script", segment.Name, "storyboard_language", "Segment nadal brzmi jak opis sceny.", segment.VoiceOver, "Popraw voiceOver albo normalizacje.");
        }

        if (IsSceneSegment(segment) && !segment.HasActionVerb)
        {
            AddIssue(report, "info", "script", segment.Name, "missing_action_verb", "Scena nie ma wyraznego czasownika akcji.", segment.VoiceOver, "Dodaj praktyczny krok: otworz, zapisz, sprawdz, usun, wlacz albo wybierz.");
        }

        if (IsSceneSegment(segment) && segment.SourceKeywordHits.Count == 0)
        {
            AddIssue(report, "warning", "merytoryka", segment.Name, "no_source_keyword_overlap", "Scena nie ma widocznego pokrycia slowami z materialu zrodlowego.", segment.VoiceOver, "Sprawdz, czy scena wynika ze zrodla albo uzupelnij material zrodlowy.");
        }

        if (IsSceneSegment(segment) && string.IsNullOrWhiteSpace(segment.NewInformation))
        {
            AddIssue(report, "warning", "merytoryka", segment.Name, "missing_new_information", "Scena nie deklaruje nowej informacji.", segment.VoiceOver, "Uzupelnij pole newInformation.");
        }

        if (IsSceneSegment(segment) && segment.SourceFactIds.Count == 0)
        {
            AddIssue(report, "warning", "merytoryka", segment.Name, "missing_source_fact_ids", "Scena nie ma powiazania z faktem zrodlowym.", segment.VoiceOver, "Dodaj sourceFactIds, np. F1.");
        }

        foreach (var evidence in FindUnsupportedNumbers(segment.VoiceOver, topic.SourceText))
        {
            segment.HasUnsupportedNumber = true;
            AddIssue(report, "warning", "merytoryka", segment.Name, "unsupported_number", "Tekst zawiera liczbe, ktorej nie ma w zrodle.", evidence, "Usun liczbe albo dodaj ja do materialu zrodlowego.");
        }

        foreach (var evidence in FindUnsupportedClaimPhrases(segment.VoiceOver, topic.SourceText))
        {
            AddIssue(report, "warning", "merytoryka", segment.Name, "unsupported_claim_phrase", "Tekst zawiera mocna obietnice lub doprecyzowanie, ktorego nie ma w zrodle.", evidence, "Dopisz dowod do zrodla albo oslab sformulowanie.");
        }
    }

    private static void AddVoiceSegmentIssues(
        ShortDiagnosticsReport report,
        SegmentDiagnostics segment,
        string audioPath)
    {
        if (segment.DurationSeconds > 6.5)
        {
            AddIssue(report, "info", "voice", segment.Name, "long_voice_segment", "Segment lektora jest dlugi jak na short.", $"{segment.DurationSeconds:0.###}s", "Skroc voiceOver lub rozbij scene.");
        }

        if (segment.WordsPerMinute > 190)
        {
            AddIssue(report, "warning", "voice", segment.Name, "voice_too_fast", "Szacowane tempo lektora jest bardzo szybkie.", $"{segment.WordsPerMinute:0.#} WPM", "Skroc tekst albo zwolnij TTS.");
        }

        if (segment.OnScreenTextLength > 55)
        {
            AddIssue(report, "warning", "subtitles", segment.Name, "long_on_screen_text", "Napis ekranowy jest zbyt dlugi.", $"{segment.OnScreenTextLength} znakow", "Skroc onScreenText.");
        }

        if (!string.IsNullOrWhiteSpace(audioPath) && !File.Exists(audioPath))
        {
            AddIssue(report, "error", "voice", segment.Name, "missing_audio_file", "Brakuje pliku audio dla segmentu.", audioPath, "Sprawdz Piper i zapis WAV.");
        }
    }

    private static void AddClipSegmentIssues(
        ShortDiagnosticsReport report,
        SegmentDiagnostics segment)
    {
        if (string.IsNullOrWhiteSpace(segment.SearchPhrase))
        {
            AddIssue(report, "error", "clips", segment.Name, "missing_search_phrase", "Segment nie ma searchPhrase.", segment.VoiceOver, "Dodaj konkretna fraze Pexels.");
        }

        if (IsGenericSearchPhrase(segment.SearchPhrase))
        {
            AddIssue(report, "warning", "clips", segment.Name, "generic_search_phrase", "Fraza Pexels jest zbyt ogolna.", segment.SearchPhrase, "Uzyj konkretnego obiektu, akcji i kontekstu.");
        }

        if (segment.HasGenericVisualDescription)
        {
            AddIssue(report, "info", "clips", segment.Name, "generic_visual_description", "Opis wizualny jest ogolny i utrudnia ocene dopasowania klipu.", segment.VisualDescription, "Dodaj konkretny obiekt, akcje i kadr.");
        }
    }

    private static void CompleteSummary(ShortDiagnosticsReport report)
    {
        report.Summary.PracticalSegmentCount = report.Segments.Count(segment => IsSceneSegment(segment) && segment.HasActionVerb);
        report.Summary.EstimatedDurationSeconds = Math.Round(report.Segments.Sum(segment => segment.DurationSeconds), 3);
        report.Summary.IssueCount = report.Issues.Count;
        report.Summary.WarningCount = report.Issues.Count(issue => issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase));
        report.Summary.ErrorCount = report.Issues.Count(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        report.Summary.HasUnsupportedClaims = report.Issues.Any(issue => issue.Code is "unsupported_number" or "unsupported_claim_phrase");

        var allScriptKeywords = report.Script.ScriptKeywords.Count;
        report.Summary.SourceCoverageRatio = allScriptKeywords == 0
            ? 0
            : Math.Round((double)report.Script.MatchedSourceKeywords.Count / allScriptKeywords, 3);
    }

    private static List<string> ExtractKeywords(string value)
    {
        return NormalizeForSearch(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length >= 4 || word.Any(char.IsDigit))
            .Where(word => !StopWords.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(word => word, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeForSearch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        var normalizedText = Regex.Replace(builder.ToString(), "\\s+", " ", RegexOptions.CultureInvariant).Trim();
        return NormalizeNumberWords(normalizedText);
    }

    private static bool HasActionVerb(string value)
    {
        return ActionVerbRegex.IsMatch(NormalizeForSearch(value));
    }

    private static bool HasStoryboardLanguage(string value)
    {
        return StoryboardRegex.IsMatch(NormalizeForSearch(value));
    }

    private static IEnumerable<string> FindUnsupportedNumbers(string value, string sourceText)
    {
        var source = NormalizeForSearch(sourceText);
        foreach (Match match in NumberRegex.Matches(NormalizeForSearch(value)))
        {
            var evidence = match.Value.Trim();
            if (string.IsNullOrWhiteSpace(evidence))
            {
                continue;
            }

            if (!source.Contains(evidence, StringComparison.OrdinalIgnoreCase))
            {
                yield return evidence;
            }
        }
    }

    private static string NormalizeNumberWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(
            value,
            "\\b(jeden|jedna|jedno)\\b",
            "1",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            "\\b(dwa|dwie)\\b",
            "2",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            "\\b(trzy)\\b",
            "3",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            "\\b(cztery)\\b",
            "4",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            "\\b(piec|pięc)\\b",
            "5",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            "\\b(minuta|minuty|minut)\\b",
            "min",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            "\\b(sekunda|sekundy|sekund)\\b",
            "sek",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            "\\b(godzina|godziny|godzin)\\b",
            "godz",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return normalized;
    }

    private static IEnumerable<string> FindUnsupportedClaimPhrases(string value, string sourceText)
    {
        var normalizedValue = NormalizeForSearch(value);
        var normalizedSource = NormalizeForSearch(sourceText);
        var patterns = new[]
        {
            "bez dodatk",
            "bez urzadzen",
            "dziala bez",
            "w 30 sekund",
            "darmow",
            "zawsze",
            "nigdy",
            "najlepsz",
            "najprostsz"
        };

        foreach (var pattern in patterns)
        {
            if (normalizedValue.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                && !normalizedSource.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                yield return pattern;
            }
        }
    }

    private static int CountWords(string value)
    {
        return NormalizeForSearch(value).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static bool IsGenericVisualDescription(string value)
    {
        var normalized = NormalizeForSearch(value);
        return string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("hook otwierajacy", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("zakonczenie shorta", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("praktyczny kontekst", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGenericSearchPhrase(string value)
    {
        var normalized = NormalizeForSearch(value);
        return string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("social media video", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("vertical video", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("stock video", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSceneSegment(SegmentDiagnostics segment)
    {
        return segment.Name.StartsWith("scene_", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddIssue(
        ShortDiagnosticsReport report,
        string severity,
        string stage,
        string segment,
        string code,
        string message,
        string evidence,
        string recommendation)
    {
        report.Issues.Add(new ShortDiagnosticIssue
        {
            Severity = severity,
            Stage = stage,
            Segment = segment,
            Code = code,
            Message = message,
            Evidence = evidence,
            Recommendation = recommendation
        });
    }

    private sealed record SegmentInput(
        int Index,
        string Name,
        string Role,
        string VoiceOver,
        List<string> SourceFactIds,
        string NewInformation,
        string OnScreenText,
        string VisualDescription,
        string SearchPhrase,
        string SceneGoal,
        TimeSpan? Duration = null,
        string AudioPath = "");
}
