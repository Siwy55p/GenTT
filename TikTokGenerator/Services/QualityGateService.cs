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

        var report = new QualityGateReport();

        AddCriterion(
            report,
            "Zgodnosc wszystkich twierdzen ze zrodlem",
            ScoreSourceAlignment(scriptDiagnostics, review),
            25,
            scriptDiagnostics.Summary.HasUnsupportedClaims || review.HasCriticalErrors
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
            ScoreVisuals(visualPlan, clipDiagnostics),
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

        AddBlockingIssues(report, topic, analysis, script, review, visualPlan, scriptDiagnostics, voiceDiagnostics, clipDiagnostics);
        report.Passed = report.Score >= report.MinimumScore
            && report.Issues.All(issue => !issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));

        return report;
    }

    private static int ScoreSourceAlignment(ShortDiagnosticsReport scriptDiagnostics, ContentReview review)
    {
        if (scriptDiagnostics.Summary.HasUnsupportedClaims || review.HasCriticalErrors)
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
        if (review.Issues.Any(issue => issue.Code.Contains("promise", StringComparison.OrdinalIgnoreCase)))
        {
            return 3;
        }

        var hookWords = ExtractUsefulWords(script.Hook).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var endingWords = ExtractUsefulWords(script.Ending).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return hookWords.Overlaps(endingWords) ? 10 : 7;
    }

    private static int ScoreVisuals(VisualPlan visualPlan, ShortDiagnosticsReport clipDiagnostics)
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
        ShortDiagnosticsReport voiceDiagnostics,
        ShortDiagnosticsReport clipDiagnostics)
    {
        if (scriptDiagnostics.Summary.HasUnsupportedClaims)
        {
            AddIssue(report, "error", "unsupported_claims", "Bramka zatrzymala render: scenariusz ma niepotwierdzone twierdzenia.");
        }

        if (review.HasCriticalErrors || !review.Approved)
        {
            AddIssue(report, "error", "review_not_approved", "Recenzent merytoryczny nie zatwierdzil scenariusza.");
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
