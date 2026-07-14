using System.Text.RegularExpressions;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public static class ContentBriefService
{
    public static ContentBrief CreateForTopic(
        string title,
        string sourceText,
        string category = "",
        int durationSeconds = 25)
    {
        var text = Normalize($"{title} {category} {sourceText}");
        var brief = new ContentBrief
        {
            KnowledgeLevel = "poczatkujacy",
            ContentType = "praktyczny tutorial",
            Tone = "konkretny, bez coachingu",
            DurationSeconds = durationSeconds <= 0 ? 25 : durationSeconds
        };

        if (ContainsAny(text, "poranny", "rano", "priorytet", "planowania rano"))
        {
            brief.Audience = "osoby pracujace przy komputerze";
            brief.ViewerProblem = "chaos po rozpoczeciu dnia";
            brief.DesiredOutcome = "wybrac pierwszy priorytet";
            return brief;
        }

        if (ContainsAny(text, "minimalizm", "telefon", "powiadomien", "ekranie glownym"))
        {
            brief.Audience = "osoby korzystajace z telefonu na co dzien";
            brief.ViewerProblem = "rozpraszajacy ekran telefonu i nadmiar powiadomien";
            brief.DesiredOutcome = "uporzadkowac pierwszy ekran telefonu";
            return brief;
        }

        if (ContainsAny(text, "hasl", "password", "logowanie", "menedzer hasel", "dwustopniowe"))
        {
            brief.Audience = "osoby korzystajace z wielu kont online";
            brief.ViewerProblem = "powtarzane hasla i trudny porzadek w kontach";
            brief.DesiredOutcome = "zaczac bezpieczniej porzadkowac hasla";
            return brief;
        }

        if (ContainsAny(text, "notatk", "nagran", "spotkan", "decyzj", "zadania"))
        {
            brief.Audience = "osoby uczestniczace w spotkaniach lub pracujace z nagraniami";
            brief.ViewerProblem = "trudno szybko znalezc decyzje i zadania po nagraniu";
            brief.DesiredOutcome = "sprawdzic najwazniejsze decyzje i zadania";
            return brief;
        }

        if (ContainsAny(text, "biurk", "porzadek", "reset biurka", "po pracy"))
        {
            brief.Audience = "osoby pracujace przy biurku";
            brief.ViewerProblem = "balagan po zakonczonej pracy";
            brief.DesiredOutcome = "zostawic biurko gotowe na kolejny start";
            return brief;
        }

        if (ContainsAny(text, "sen", "spanie", "wieczor", "sypialn"))
        {
            brief.Audience = "osoby chcace prostszej wieczornej rutyny";
            brief.ViewerProblem = "trudno wyciszyc sie przed snem";
            brief.DesiredOutcome = "wybrac jeden spokojny krok przed snem";
            return brief;
        }

        if (ContainsAny(text, "tydzien", "przeciazen", "plan tygodnia"))
        {
            brief.Audience = "osoby planujace prace i obowiazki";
            brief.ViewerProblem = "za duzo zadan na tydzien bez jasnego wyboru";
            brief.DesiredOutcome = "wybrac najwazniejsze zadania tygodnia";
            return brief;
        }

        if (ContainsAny(text, "finans", "miesiaca", "budzet", "nawyk finansowy"))
        {
            brief.Audience = "osoby porzadkujace domowe finanse";
            brief.ViewerProblem = "brak prostego rytualu finansowego na start miesiaca";
            brief.DesiredOutcome = "wykonac jeden prosty przeglad finansow";
            return brief;
        }

        if (ContainsAny(text, "ofert", "kampanii", "reklam", "mikroprodukt", "obsluge klienta", "firma", "biznes"))
        {
            brief.Audience = "osoby rozwijajace maly biznes lub projekt online";
            brief.ViewerProblem = "trudno szybko wybrac najwazniejszy komunikat";
            brief.DesiredOutcome = "doprecyzowac jeden praktyczny krok biznesowy";
            return brief;
        }

        var benefit = ExtractLabelValue(sourceText, "Korzysc dla widza");
        var steps = ExtractLabelValue(sourceText, "Konkretne kroki");
        brief.Audience = CreateFallbackAudience(category);
        brief.ViewerProblem = CreateFallbackProblem(title, sourceText);
        brief.DesiredOutcome = CreateFallbackOutcome(benefit, steps, title);
        return brief;
    }

    public static ContentBrief FillMissing(ContentBrief value, ContentBrief fallback)
    {
        value.Audience = Pick(value.Audience, fallback.Audience);
        value.KnowledgeLevel = Pick(value.KnowledgeLevel, fallback.KnowledgeLevel);
        value.ViewerProblem = Pick(value.ViewerProblem, fallback.ViewerProblem);
        value.DesiredOutcome = Pick(value.DesiredOutcome, fallback.DesiredOutcome);
        value.ContentType = Pick(value.ContentType, fallback.ContentType);
        value.Tone = Pick(value.Tone, fallback.Tone);
        value.DurationSeconds = value.DurationSeconds <= 0 ? fallback.DurationSeconds : value.DurationSeconds;
        return value;
    }

    private static string CreateFallbackAudience(string category)
    {
        var normalized = Normalize(category);
        return normalized switch
        {
            "technologia" => "osoby korzystajace z prostych narzedzi technologicznych",
            "biznes" => "osoby rozwijajace maly biznes lub projekt online",
            "lifestyle" => "osoby chcace prostszych codziennych nawykow",
            _ => "osoby zainteresowane praktycznym tematem"
        };
    }

    private static string CreateFallbackProblem(string title, string sourceText)
    {
        var sourceProblem = ExtractLabelValue(sourceText, "Problem");
        if (!string.IsNullOrWhiteSpace(sourceProblem))
        {
            return TrimSentence(sourceProblem, 90);
        }

        var shortTopic = TrimSentence(title, 60).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(shortTopic)
            ? "brak jasnego pierwszego kroku"
            : $"brak jasnego pierwszego kroku w temacie: {shortTopic}";
    }

    private static string CreateFallbackOutcome(string benefit, string steps, string title)
    {
        if (!string.IsNullOrWhiteSpace(steps))
        {
            var firstStep = Regex.Split(steps, @",|\boraz\b|\bi\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(value => value.Trim())
                .FirstOrDefault(value => value.Length > 0);
            if (!string.IsNullOrWhiteSpace(firstStep))
            {
                return TrimSentence(firstStep, 90);
            }
        }

        if (!string.IsNullOrWhiteSpace(benefit))
        {
            return TrimSentence(benefit, 90);
        }

        var shortTopic = TrimSentence(title, 60).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(shortTopic)
            ? "wykonac jeden konkretny krok"
            : $"wykonac jeden konkretny krok w temacie: {shortTopic}";
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

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(Normalize(needle), StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ToLowerInvariant()
            .Replace('ą', 'a')
            .Replace('ć', 'c')
            .Replace('ę', 'e')
            .Replace('ł', 'l')
            .Replace('ń', 'n')
            .Replace('ó', 'o')
            .Replace('ś', 's')
            .Replace('ź', 'z')
            .Replace('ż', 'z');
        return Regex.Replace(normalized, "\\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static string Pick(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string TrimSentence(string value, int maxLength)
    {
        var trimmed = Regex.Replace(value.Trim(), "\\s+", " ", RegexOptions.CultureInvariant)
            .Trim('.', '!', '?', ':', ';', ',');
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + ".";
    }
}
