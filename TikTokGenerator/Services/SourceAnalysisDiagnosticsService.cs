using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public static class SourceAnalysisDiagnosticsService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "temat", "roboczy", "kategoria", "praktyczna", "teza", "konkretne", "kroki",
        "korzysc", "widza", "zamiast", "zaczynac", "dzien", "dnia", "dzis", "oraz",
        "ktory", "ktora", "ktore", "ktorej", "jedna", "jeden", "jedno", "male",
        "maly", "moze", "jest", "byc", "dla", "przez", "bez", "oraz", "albo",
        "nie", "tak", "sie", "jako", "tych", "tych", "tych", "tych"
    };

    public static SourceAnalysisDiagnostics CreateDiagnostics(
        SelectedTopic topic,
        SourceAnalysis analysis)
    {
        var report = new SourceAnalysisDiagnostics();
        var source = Normalize(topic.SourceText);

        CheckField(report, source, "mainThesis", analysis.MainThesis, requireEvidence: true);
        CheckField(report, source, "mostUsefulFragment", analysis.MostUsefulFragment, requireEvidence: true);

        foreach (var fact in analysis.Facts)
        {
            CheckField(report, source, $"fact:{fact.Id}", fact.Text, requireEvidence: true);
            CheckField(report, source, $"factEvidence:{fact.Id}", fact.Evidence, requireEvidence: false);
        }

        foreach (var step in analysis.Steps)
        {
            CheckField(report, source, $"step:{step.Id}", step.Text, requireEvidence: true);
        }

        foreach (var item in analysis.Examples.Select((value, index) => new { value, index }))
        {
            CheckField(report, source, $"example:{item.index + 1}", item.value, requireEvidence: true);
        }

        foreach (var item in analysis.Limitations.Select((value, index) => new { value, index }))
        {
            CheckField(report, source, $"limitation:{item.index + 1}", item.value, requireEvidence: true);
        }

        foreach (var item in analysis.RiskyClaims.Select((value, index) => new { value, index }))
        {
            CheckField(report, source, $"riskyClaim:{item.index + 1}", item.value, requireEvidence: true);
        }

        return report;
    }

    public static SourceAnalysis SanitizeUnsupportedContent(
        SelectedTopic topic,
        SourceAnalysis analysis,
        GenerationDebugLogger? logger = null)
    {
        var source = Normalize(topic.SourceText);
        analysis.Examples = KeepSupportedList(source, analysis.Examples, "examples", logger);
        analysis.Limitations = KeepSupportedList(source, analysis.Limitations, "limitations", logger);
        analysis.RiskyClaims = KeepSupportedList(source, analysis.RiskyClaims, "riskyClaims", logger);

        analysis.Facts = KeepSupportedFacts(source, analysis.Facts, logger);
        analysis.Steps = KeepSupportedSteps(source, analysis.Steps, logger);

        if (analysis.Facts.Count == 0)
        {
            analysis.Facts.Add(new SourceFact
            {
                Id = "F1",
                Text = FirstUsefulSourceSentence(topic.SourceText),
                Evidence = FirstUsefulSourceSentence(topic.SourceText)
            });
        }

        NormalizeStepFactIds(analysis.Steps, analysis.Facts, logger);

        if (analysis.Steps.Count == 0)
        {
            var sourceSteps = CreateStepsFromSourceLabels(topic.SourceText, analysis.Facts[0].Id);
            if (sourceSteps.Count > 0)
            {
                logger?.Warning($"Source analysis recovered {sourceSteps.Count} steps from source labels after removing unsupported model steps.");
                analysis.Steps.AddRange(sourceSteps);
            }
        }

        NormalizeStepFactIds(analysis.Steps, analysis.Facts, logger);

        if (!IsSupported(source, analysis.MainThesis))
        {
            logger?.Warning($"Source analysis mainThesis was unsupported and replaced. Original={analysis.MainThesis}");
            analysis.MainThesis = analysis.Facts[0].Text;
        }

        if (!IsSupported(source, analysis.MostUsefulFragment))
        {
            logger?.Warning($"Source analysis mostUsefulFragment was unsupported and replaced. Original={analysis.MostUsefulFragment}");
            analysis.MostUsefulFragment = analysis.Facts[0].Text;
        }

        return analysis;
    }

    public static bool IsSupportedBySource(SelectedTopic topic, string value)
    {
        return IsSupported(Normalize(topic.SourceText), value);
    }

    internal static bool IsSupported(string normalizedSource, string value)
    {
        var normalizedValue = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return true;
        }

        if (IsNormalizedFragment(normalizedSource, normalizedValue))
        {
            return true;
        }

        var valueTerms = ExtractTerms(normalizedValue).ToList();
        if (valueTerms.Count == 0)
        {
            return true;
        }

        var sourceTerms = ExtractTerms(normalizedSource).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlap = valueTerms.Count(sourceTerms.Contains) / (double)valueTerms.Count;
        return overlap >= 0.72;
    }

    internal static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (char.ToLowerInvariant(ch) == '\u0142')
            {
                builder.Append('l');
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        var normalized = Regex.Replace(builder.ToString(), "\\s+", " ", RegexOptions.CultureInvariant).Trim();
        return NormalizeNumberWords(normalized);
    }

    internal static IEnumerable<string> ExtractTerms(string normalizedValue)
    {
        return normalizedValue
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 4 || term.Any(char.IsDigit))
            .Where(term => !StopWords.Contains(term));
    }

    private static void CheckField(
        SourceAnalysisDiagnostics report,
        string normalizedSource,
        string field,
        string value,
        bool requireEvidence)
    {
        if (!requireEvidence || IsSupported(normalizedSource, value))
        {
            return;
        }

        report.Issues.Add(new SourceAnalysisIssue
        {
            Severity = "error",
            Code = "unsupported_source_analysis_value",
            Field = field,
            Value = value,
            Message = "Analiza zrodla zawiera wartosc, ktorej nie da sie uzasadnic materialem zrodlowym."
        });
    }

    private static List<string> KeepSupportedList(
        string normalizedSource,
        IEnumerable<string> values,
        string field,
        GenerationDebugLogger? logger)
    {
        var supported = new List<string>();
        foreach (var value in values)
        {
            if (IsSupported(normalizedSource, value))
            {
                supported.Add(value);
                continue;
            }

            logger?.Warning($"Source analysis removed unsupported {field}: {value}");
        }

        return supported;
    }

    private static List<SourceFact> KeepSupportedFacts(
        string normalizedSource,
        IEnumerable<SourceFact> facts,
        GenerationDebugLogger? logger)
    {
        var supported = new List<SourceFact>();
        foreach (var fact in facts)
        {
            if (IsSupported(normalizedSource, fact.Text))
            {
                supported.Add(fact);
                continue;
            }

            if (TryReplaceFactTextWithSupportedEvidence(normalizedSource, fact, out var replacementText))
            {
                logger?.Warning($"Source analysis fact {fact.Id} was unsupported and replaced with evidence excerpt. Original={fact.Text}");
                fact.Text = replacementText;
                supported.Add(fact);
                continue;
            }

            logger?.Warning($"Source analysis removed unsupported fact {fact.Id}: {fact.Text}");
        }

        return supported;
    }

    private static bool TryReplaceFactTextWithSupportedEvidence(
        string normalizedSource,
        SourceFact fact,
        out string replacementText)
    {
        replacementText = CleanEvidenceText(fact.Evidence);
        if (string.IsNullOrWhiteSpace(replacementText))
        {
            return false;
        }

        var normalizedEvidence = Normalize(replacementText);
        if (ExtractTerms(normalizedEvidence).Count() < 2)
        {
            return false;
        }

        return IsNormalizedFragment(normalizedSource, normalizedEvidence);
    }

    private static string CleanEvidenceText(string value)
    {
        return Regex.Replace(value.Trim(), "\\s+", " ", RegexOptions.CultureInvariant)
            .Trim(' ', '.', ',', ';', ':', '"', '\'', '`', '\u201c', '\u201d', '\u201e', '\u201a', '\u2018', '\u2019');
    }

    private static List<SourceStep> KeepSupportedSteps(
        string normalizedSource,
        IEnumerable<SourceStep> steps,
        GenerationDebugLogger? logger)
    {
        var supported = new List<SourceStep>();
        foreach (var step in steps)
        {
            if (IsSupported(normalizedSource, step.Text))
            {
                supported.Add(step);
                continue;
            }

            logger?.Warning($"Source analysis removed unsupported step {step.Id}: {step.Text}");
        }

        return supported;
    }

    private static void NormalizeStepFactIds(
        List<SourceStep> steps,
        IReadOnlyList<SourceFact> facts,
        GenerationDebugLogger? logger)
    {
        var validFactIds = facts
            .Select(fact => fact.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (validFactIds.Count == 0)
        {
            return;
        }

        var fallbackFactId = validFactIds.First();
        foreach (var step in steps)
        {
            var originalIds = step.SourceFactIds.ToArray();
            step.SourceFactIds = step.SourceFactIds
                .Where(id => validFactIds.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (step.SourceFactIds.Count == 0)
            {
                step.SourceFactIds.Add(fallbackFactId);
            }

            if (!originalIds.SequenceEqual(step.SourceFactIds, StringComparer.OrdinalIgnoreCase))
            {
                logger?.Warning($"Source analysis step {step.Id} sourceFactIds were updated after fact sanitization. Original={string.Join(",", originalIds)}; Fixed={string.Join(",", step.SourceFactIds)}");
            }
        }
    }

    private static bool IsNormalizedFragment(string normalizedSource, string normalizedValue)
    {
        return string.IsNullOrWhiteSpace(normalizedValue)
            || normalizedSource.Contains(normalizedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstUsefulSourceSentence(string sourceText)
    {
        return Regex.Split(sourceText, @"(?<=[.!?])\s+|\r?\n")
            .Select(value => Regex.Replace(value.Trim(), "\\s+", " ", RegexOptions.CultureInvariant))
            .FirstOrDefault(value => value.Length > 0)
            ?? sourceText.Trim();
    }

    private static List<SourceStep> CreateStepsFromSourceLabels(string sourceText, string factId)
    {
        var stepsText = ExtractLabelValue(sourceText, "Konkretne kroki");
        if (string.IsNullOrWhiteSpace(stepsText))
        {
            return [];
        }

        return Regex.Split(stepsText, @",|\boraz\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(value => Regex.Replace(value.Trim(), "\\s+", " ", RegexOptions.CultureInvariant)
                .Trim('.', '!', '?', ':', ';', ','))
            .Where(value => value.Length > 0)
            .Take(4)
            .Select((value, index) => new SourceStep
            {
                Id = $"S{index + 1}",
                Text = value,
                SourceFactIds = [factId]
            })
            .ToList();
    }

    private static string ExtractLabelValue(string sourceText, string label)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return string.Empty;
        }

        var match = Regex.Match(
            sourceText,
            $@"{Regex.Escape(label)}\s*:\s*(?<value>.+?)(?:\r?\n|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
    }

    private static string NormalizeNumberWords(string value)
    {
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
}
