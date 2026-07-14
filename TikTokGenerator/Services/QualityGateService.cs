using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public static class QualityGateService
{
    public static QualityGateReport EvaluateBeforeRender(
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortScript script,
        ContentReview review,
        VisualPlan visualPlan,
        IReadOnlyList<VoiceSegment> voiceSegments,
        IReadOnlyList<DownloadedVideoClip> clips)
    {
        var scriptDiagnostics = ShortDiagnosticsService.CreateScriptDiagnostics(topic, script);
        var voiceDiagnostics = ShortDiagnosticsService.CreateVoiceDiagnostics(topic, script, voiceSegments);
        var clipDiagnostics = ShortDiagnosticsService.CreateClipDiagnostics(topic, script, voiceSegments, clips);
        var sourceDiagnostics = SourceAnalysisDiagnosticsService.CreateDiagnostics(topic, analysis);

        var report = new QualityGateReport();

        AddCriterion(
            report,
            "Zgodnosc wszystkich twierdzen ze zrodlem",
            ScoreSourceAlignment(scriptDiagnostics, sourceDiagnostics, review, topic, script),
            25,
            scriptDiagnostics.Summary.HasUnsupportedClaims || sourceDiagnostics.HasBlockingIssues || review.HasCriticalErrors
                ? "Sa niepotwierdzone twierdzenia albo krytyczne uwagi recenzenta."
                : "Nie wykryto niepotwierdzonych liczb ani mocnych obietnic.");

        AddCriterion(
            report,
            "Uzytecznosc dla wskazanego odbiorcy",
            Math.Clamp(review.UsefulnessScore * 2, 0, 20),
            20,
            string.IsNullOrWhiteSpace(review.AudienceValueCheck)
                ? "Recenzja nie opisala wartosci dla odbiorcy."
                : review.AudienceValueCheck);

        AddCriterion(
            report,
            "Konkretnosc i wykonalnosc",
            ScoreConcreteness(scriptDiagnostics),
            15,
            $"{scriptDiagnostics.Summary.PracticalSegmentCount}/{Math.Max(script.Scenes.Count, 1)} scen ma czasownik akcji.");

        AddCriterion(
            report,
            "Brak powtorzen i logiczna progresja",
            ScoreProgression(script, review),
            10,
            HasRepeatedNewInformation(script)
                ? "Co najmniej dwie sceny niosa te sama newInformation."
                : "Sceny maja odroznialne newInformation.");

        AddCriterion(
            report,
            "Hook zgodny z payoffem",
            ScoreHookPayoff(script, review),
            10,
            string.IsNullOrWhiteSpace(review.PromiseCheck)
                ? "Sprawdzono lokalnie hook i ending."
                : review.PromiseCheck);

        AddCriterion(
            report,
            "Dopasowanie wizualne",
            ScoreVisuals(topic, script, visualPlan, clipDiagnostics),
            10,
            clipDiagnostics.Summary.ErrorCount > 0
                ? "Sa bledy w klipach albo brakujace segmenty."
                : "Plan wizualny i klipy pokrywaja segmenty.");

        AddCriterion(
            report,
            "Czytelnosc i czas",
            ScoreReadabilityAndTime(topic, voiceDiagnostics),
            10,
            $"Zmierzony czas audio: {voiceDiagnostics.Summary.EstimatedDurationSeconds:0.###}s / {topic.Brief.DurationSeconds}s.");

        report.Score = report.Criteria.Sum(criterion => criterion.Points);

        AddBlockingIssues(report, topic, analysis, script, review, visualPlan, scriptDiagnostics, sourceDiagnostics, voiceDiagnostics, clipDiagnostics);
        report.Passed = report.Score >= report.MinimumScore
            && report.Issues.All(issue => !issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));

        return report;
    }

    private static int ScoreSourceAlignment(
        ShortDiagnosticsReport scriptDiagnostics,
        SourceAnalysisDiagnostics sourceDiagnostics,
        ContentReview review,
        SelectedTopic topic,
        ShortScript script)
    {
        if (scriptDiagnostics.Summary.HasUnsupportedClaims
            || sourceDiagnostics.HasBlockingIssues
            || review.HasCriticalErrors
            || HasUnsupportedPayoff(topic, script))
        {
            return 0;
        }

        var sourceWarnings = scriptDiagnostics.Issues.Count(issue =>
            issue.Code is "no_source_keyword_overlap" or "missing_source_fact_ids");
        return sourceWarnings == 0 ? 25 : Math.Max(15, 25 - sourceWarnings * 5);
    }

    private static int ScoreConcreteness(ShortDiagnosticsReport scriptDiagnostics)
    {
        var sceneCount = Math.Max(scriptDiagnostics.Summary.SceneCount, 1);
        var ratio = (double)scriptDiagnostics.Summary.PracticalSegmentCount / sceneCount;
        return Math.Clamp((int)Math.Round(ratio * 15), 0, 15);
    }

    private static int ScoreProgression(ShortScript script, ContentReview review)
    {
        if (HasRepeatedNewInformation(script)
            || review.Issues.Any(issue => issue.Code.Contains("repeat", StringComparison.OrdinalIgnoreCase)))
        {
            return 4;
        }

        return script.Scenes.All(scene => !string.IsNullOrWhiteSpace(scene.NewInformation)) ? 10 : 6;
    }

    private static int ScoreHookPayoff(ShortScript script, ContentReview review)
    {
        if (HasPromiseConcern(review))
        {
            return 3;
        }

        var hookWords = ExtractUsefulWords(script.Hook).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var endingWords = ExtractUsefulWords(script.Ending).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return hookWords.Overlaps(endingWords) ? 10 : 7;
    }

    private static int ScoreVisuals(
        SelectedTopic topic,
        ShortScript script,
        VisualPlan visualPlan,
        ShortDiagnosticsReport clipDiagnostics)
    {
        if (clipDiagnostics.Summary.ErrorCount > 0)
        {
            return 0;
        }

        var missingPlan = visualPlan.Segments.Count == 0;
        var duplicateClip = clipDiagnostics.Issues.Any(issue => issue.Code == "duplicate_clip_url");
        var weakVisual = clipDiagnostics.Issues.Any(issue => issue.Code is "generic_visual_description" or "generic_search_phrase");
        var score = 10;
        if (missingPlan)
        {
            score -= 5;
        }

        if (duplicateClip)
        {
            score -= 3;
        }

        if (weakVisual)
        {
            score -= 2;
        }

        if (HasRepeatedIrrelevantVisualPlan(topic, script, visualPlan))
        {
            score -= 6;
        }

        return Math.Clamp(score, 0, 10);
    }

    private static int ScoreReadabilityAndTime(SelectedTopic topic, ShortDiagnosticsReport voiceDiagnostics)
    {
        var score = 10;
        if (voiceDiagnostics.Summary.EstimatedDurationSeconds > topic.Brief.DurationSeconds)
        {
            score -= 6;
        }

        if (voiceDiagnostics.Issues.Any(issue => issue.Code is "long_on_screen_text" or "voice_too_fast"))
        {
            score -= 3;
        }

        return Math.Clamp(score, 0, 10);
    }

    private static void AddBlockingIssues(
        QualityGateReport report,
        SelectedTopic topic,
        SourceAnalysis analysis,
        ShortScript script,
        ContentReview review,
        VisualPlan visualPlan,
        ShortDiagnosticsReport scriptDiagnostics,
        SourceAnalysisDiagnostics sourceDiagnostics,
        ShortDiagnosticsReport voiceDiagnostics,
        ShortDiagnosticsReport clipDiagnostics)
    {
        if (sourceDiagnostics.HasBlockingIssues)
        {
            AddIssue(report, "error", "source_analysis_unsupported", "Analiza zrodla zawiera informacje, ktorych nie ma w materiale zrodlowym.");
        }

        if (scriptDiagnostics.Summary.HasUnsupportedClaims)
        {
            AddIssue(report, "error", "unsupported_claims", "Bramka zatrzymala render: scenariusz ma niepotwierdzone twierdzenia.");
        }

        if (HasTopicBriefDrift(topic, script))
        {
            AddIssue(report, "error", "topic_brief_drift", "Brief albo domyslny cel zdominowal temat: scenariusz obiecuje cos, czego nie ma w zrodle ani tytule.");
        }

        if (HasUnsupportedPayoff(topic, script))
        {
            AddIssue(report, "error", "unsupported_payoff", "Payoff lub zakonczenie zawiera konkretny wynik/przyklad, ktorego nie ma w materiale zrodlowym.");
        }

        if (HasBlockingPromiseProblem(review))
        {
            AddIssue(report, "error", "promise_not_met", "Payoff nie spelnia obietnicy hooka wedlug recenzji merytorycznej.");
        }

        if (review.HasCriticalErrors)
        {
            AddIssue(report, "error", "review_not_approved", "Recenzent merytoryczny nie zatwierdzil scenariusza.");
        }
        else if (!review.Approved)
        {
            AddIssue(report, "warning", "review_not_approved", "Recenzent merytoryczny oznaczyl scenariusz jako niezatwierdzony, ale nie wskazal bledu krytycznego.");
        }

        foreach (var scene in script.Scenes.Select((value, index) => new { value, index }))
        {
            if (string.IsNullOrWhiteSpace(scene.value.NewInformation))
            {
                AddIssue(report, "error", $"missing_new_information_{scene.index + 1:00}", "Kazda scena musi wnosic nowa informacje.");
            }

            if (scene.value.SourceFactIds.Count == 0
                || scene.value.SourceFactIds.Any(id => analysis.Facts.All(fact => !fact.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
            {
                AddIssue(report, "error", $"missing_source_fact_{scene.index + 1:00}", "Kazda scena musi byc powiazana z faktem ze zrodla.");
            }
        }

        if (HasRepeatedNewInformation(script))
        {
            AddIssue(report, "error", "repeated_new_information", "Co najmniej dwie sceny powtarzaja te sama informacje.");
        }

        if (!HasConcreteExampleOrResult(script, visualPlan))
        {
            AddIssue(report, "error", "missing_example_or_result", "Film musi zawierac konkretny przyklad, demonstracje albo widoczny rezultat.");
        }

        if (voiceDiagnostics.Summary.EstimatedDurationSeconds > topic.Brief.DurationSeconds)
        {
            AddIssue(report, "error", "duration_over_limit", "Zmierzony czas TTS przekracza limit z briefu.");
        }

        if (clipDiagnostics.Summary.ErrorCount > 0)
        {
            AddIssue(report, "error", "clip_errors", "Brakuje klipow albo selekcja wideo ma bledy krytyczne.");
        }

        if (HasRepeatedIrrelevantVisualPlan(topic, script, visualPlan))
        {
            AddIssue(report, "error", "repeated_visual_plan", "Plan wizualny powtarza ten sam niezwiazany obiekt zamiast pokazywac rozne kroki.");
        }
    }

    private static bool HasUnsupportedPayoff(SelectedTopic topic, ShortScript script)
    {
        var payoffTexts = script.Scenes
            .Where(scene => scene.Role.Equals("payoff", StringComparison.OrdinalIgnoreCase))
            .Select(scene => $"{scene.VoiceOver} {scene.NewInformation}")
            .Append(script.Ending)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (payoffTexts.Count == 0)
        {
            return false;
        }

        var normalizedSource = SourceAnalysisDiagnosticsService.Normalize(topic.SourceText);
        foreach (var text in payoffTexts)
        {
            if (SourceAnalysisDiagnosticsService.IsSupported(normalizedSource, text))
            {
                continue;
            }

            var terms = SourceAnalysisDiagnosticsService.ExtractTerms(SourceAnalysisDiagnosticsService.Normalize(text)).ToList();
            var sourceTerms = SourceAnalysisDiagnosticsService.ExtractTerms(normalizedSource).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unsupportedTerms = terms
                .Where(term => !sourceTerms.Contains(term))
                .Where(term => !IsGenericPayoffTerm(term))
                .ToList();

            if (unsupportedTerms.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPromiseConcern(ContentReview review)
    {
        var activeIssues = review.Issues
            .Where(issue => !IsDowngradedReviewIssue(issue))
            .ToList();
        if (activeIssues.Any(IsPromiseIssue))
        {
            return true;
        }

        var text = Normalize($"{review.PromiseCheck} {string.Join(" ", activeIssues.Select(issue => $"{issue.Code} {issue.Message}"))}");
        return text.Contains("not met", StringComparison.OrdinalIgnoreCase)
            || text.Contains("notmet", StringComparison.OrdinalIgnoreCase)
            || text.Contains("nie jest spe", StringComparison.OrdinalIgnoreCase)
            || text.Contains("niespeln", StringComparison.OrdinalIgnoreCase)
            || text.Contains("niespełn", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasBlockingPromiseProblem(ContentReview review)
    {
        return review.Issues.Any(issue =>
            issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)
            && !IsDowngradedReviewIssue(issue)
            && IsPromiseIssue(issue));
    }

    private static bool IsDowngradedReviewIssue(ContentReviewIssue issue)
    {
        return issue.Message.Contains("[Zdegradowano:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPromiseIssue(ContentReviewIssue issue)
    {
        return issue.Code.Contains("promise", StringComparison.OrdinalIgnoreCase)
            || issue.Code.Contains("hooknotmet", StringComparison.OrdinalIgnoreCase)
            || issue.Code.Contains("hook_not_met", StringComparison.OrdinalIgnoreCase)
            || (issue.Code.Contains("hook", StringComparison.OrdinalIgnoreCase)
                && issue.Code.Contains("mismatch", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasTopicBriefDrift(SelectedTopic topic, ShortScript script)
    {
        var sourceAndTitle = SourceAnalysisDiagnosticsService.Normalize($"{topic.Title} {topic.SourceText}");
        var scriptText = SourceAnalysisDiagnosticsService.Normalize($"{script.Title} {script.Hook} {script.Ending} {string.Join(" ", script.Scenes.Select(scene => scene.VoiceOver))}");
        var briefText = SourceAnalysisDiagnosticsService.Normalize($"{topic.Brief.ViewerProblem} {topic.Brief.DesiredOutcome}");
        var sourceTerms = SourceAnalysisDiagnosticsService.ExtractTerms(sourceAndTitle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var briefTerms = SourceAnalysisDiagnosticsService.ExtractTerms(briefText)
            .Where(term => !sourceTerms.Contains(term))
            .ToList();

        if (briefTerms.Count == 0)
        {
            return false;
        }

        var scriptTerms = SourceAnalysisDiagnosticsService.ExtractTerms(scriptText).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var driftingBriefTerms = briefTerms.Count(scriptTerms.Contains);
        if (driftingBriefTerms == 0)
        {
            return false;
        }

        var sourceOverlap = scriptTerms.Count(sourceTerms.Contains) / (double)Math.Max(scriptTerms.Count, 1);
        return sourceOverlap < 0.2;
    }

    private static bool HasRepeatedIrrelevantVisualPlan(
        SelectedTopic topic,
        ShortScript script,
        VisualPlan visualPlan)
    {
        var scenePlans = visualPlan.Segments
            .Where(segment => segment.SegmentName.StartsWith("scene_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (scenePlans.Count < 2)
        {
            return false;
        }

        var sourceTerms = SourceAnalysisDiagnosticsService
            .ExtractTerms(SourceAnalysisDiagnosticsService.Normalize($"{topic.Title} {topic.SourceText}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repeatedVisualGroups = scenePlans
            .Select(segment => new
            {
                Segment = segment,
                Key = BuildVisualKey(segment)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= Math.Max(2, (int)Math.Ceiling(scenePlans.Count * 0.5)))
            .ToList();

        foreach (var group in repeatedVisualGroups)
        {
            var keyTerms = SourceAnalysisDiagnosticsService.ExtractTerms(group.Key).ToList();
            if (keyTerms.Count == 0 || keyTerms.Any(sourceTerms.Contains))
            {
                continue;
            }

            var relatedToAnyScene = group.Any(item =>
            {
                var index = ParseSceneIndex(item.Segment.SegmentName);
                var scene = script.Scenes.ElementAtOrDefault(index);
                if (scene is null)
                {
                    return false;
                }

                var sceneTerms = SourceAnalysisDiagnosticsService
                    .ExtractTerms(SourceAnalysisDiagnosticsService.Normalize($"{scene.VoiceOver} {scene.NewInformation}"))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return keyTerms.Any(sceneTerms.Contains);
            });

            if (!relatedToAnyScene && IsLikelyGenericBrollKey(group.Key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGenericPayoffTerm(string term)
    {
        return term is
            "masz" or
            "mamy" or
            "zrob" or
            "zrobisz" or
            "zacznij" or
            "dzis" or
            "dzisiaj" or
            "prosty" or
            "gotowy" or
            "plan" or
            "startowy" or
            "wynik" or
            "efekt" or
            "rezultat" or
            "koniec";
    }

    private static string BuildVisualKey(VisualPlanSegment segment)
    {
        var candidates = new[]
        {
            segment.PrimaryObject,
            segment.VisibleContent,
            segment.ResultToShow,
            segment.PersonAction,
            string.Join(" ", segment.SearchPhrases)
        };

        foreach (var candidate in candidates)
        {
            var terms = SourceAnalysisDiagnosticsService
                .ExtractTerms(SourceAnalysisDiagnosticsService.Normalize(candidate))
                .Select(CanonicalizeVisualTerm)
                .Where(term => !IsGenericVisualTerm(term))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (terms.Count > 0)
            {
                return string.Join(" ", terms);
            }
        }

        return string.Empty;
    }

    private static int ParseSceneIndex(string segmentName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            segmentName,
            @"scene_(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var index)
            ? index - 1
            : -1;
    }

    private static string CanonicalizeVisualTerm(string term)
    {
        return term switch
        {
            "budzik" or "alarmowy" => "alarm",
            "telefon" or "smartfon" => "phone",
            "komputer" => "computer",
            "notatnik" or "notes" => "notebook",
            "biurko" => "desk",
            _ => term
        };
    }

    private static bool IsGenericVisualTerm(string term)
    {
        return term is
            "person" or
            "osoba" or
            "people" or
            "close" or
            "shot" or
            "ujecie" or
            "kadr" or
            "morning" or
            "rano" or
            "visible" or
            "widoczny" or
            "result" or
            "rezultat" or
            "pokazuje" or
            "wykonuje";
    }

    private static bool IsLikelyGenericBrollKey(string key)
    {
        var terms = SourceAnalysisDiagnosticsService
            .ExtractTerms(SourceAnalysisDiagnosticsService.Normalize(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return terms.Overlaps(
        [
            "alarm",
            "clock",
            "coffee",
            "laptop",
            "keyboard",
            "phone",
            "smartphone",
            "office",
            "desk",
            "screen",
            "computer"
        ]);
    }

    private static bool HasRepeatedNewInformation(ShortScript script)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var normalized in script.Scenes.Select(scene => Normalize(scene.NewInformation)))
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (!seen.Add(normalized))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasConcreteExampleOrResult(ShortScript script, VisualPlan visualPlan)
    {
        var text = Normalize(string.Join(
            " ",
            script.Hook,
            script.Ending,
            string.Join(" ", script.Scenes.Select(scene => $"{scene.VoiceOver} {scene.NewInformation}")),
            string.Join(" ", visualPlan.Segments.Select(segment => $"{segment.ResultToShow} {segment.VisibleContent}"))));

        return text.Contains("przyklad", StringComparison.OrdinalIgnoreCase)
            || text.Contains("demonstrac", StringComparison.OrdinalIgnoreCase)
            || text.Contains("rezultat", StringComparison.OrdinalIgnoreCase)
            || text.Contains("efekt", StringComparison.OrdinalIgnoreCase)
            || text.Contains("przed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("po", StringComparison.OrdinalIgnoreCase)
            || text.Contains("pokaz", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractUsefulWords(string value)
    {
        return Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length >= 4)
            .Where(word => word is not ("ktory" or "ktora" or "ktore" or "jeden" or "jedna" or "masz" or "zrob"));
    }

    private static string Normalize(string value)
    {
        var normalized = new string(value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
            .ToArray());
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static void AddCriterion(
        QualityGateReport report,
        string name,
        int points,
        int maxPoints,
        string reason)
    {
        report.Criteria.Add(new QualityGateCriterion
        {
            Name = name,
            Points = Math.Clamp(points, 0, maxPoints),
            MaxPoints = maxPoints,
            Reason = reason
        });
    }

    private static void AddIssue(
        QualityGateReport report,
        string severity,
        string code,
        string message)
    {
        report.Issues.Add(new QualityGateIssue
        {
            Severity = severity,
            Code = code,
            Message = message
        });
    }
}
