using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class ContentBriefServiceTests
{
    [Fact]
    public void CreateForTopic_WhenPhoneMinimalismTopic_ReturnsPhoneSpecificBrief()
    {
        var brief = ContentBriefService.CreateForTopic(
            "Minimalizm w aplikacjach na telefonie",
            """
            Praktyczna teza: mniej ikon i mniej powiadomien pomaga szybciej znalezc to, co naprawde potrzebne.
            Konkretne kroki: usun z ekranu glownego aplikacje nieuzywane codziennie, wylacz niepilne powiadomienia, zostaw na pierwszym ekranie tylko najwazniejsze narzedzia.
            """,
            "Lifestyle");

        Assert.Contains("telefon", brief.Audience, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ekran telefonu", brief.ViewerProblem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pierwszy ekran telefonu", brief.DesiredOutcome, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chaos po rozpoczeciu dnia", brief.ViewerProblem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pierwszy priorytet", brief.DesiredOutcome, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateForTopic_WhenMorningPlanningTopic_ReturnsMorningSpecificBrief()
    {
        var brief = ContentBriefService.CreateForTopic(
            "Poranny rytual, ktory zmienia tempo dnia",
            "Praktyczna teza: jedna minuta planowania rano moze ograniczyc chaos na starcie dnia.",
            "Lifestyle");

        Assert.Equal("osoby pracujace przy komputerze", brief.Audience);
        Assert.Equal("chaos po rozpoczeciu dnia", brief.ViewerProblem);
        Assert.Equal("wybrac pierwszy priorytet", brief.DesiredOutcome);
    }

    [Fact]
    public void CreateForTopic_WhenPhone3DScannerTopic_ReturnsScannerSpecificBrief()
    {
        var brief = ContentBriefService.CreateForTopic(
            "Ciekawostka technologiczna: telefon jako skaner 3D",
            """
            Praktyczna teza: telefon moze posluzyc do prostego skanu 3D obiektu.
            Konkretne kroki: wybierz maly nieruchomy obiekt, obejdz go telefonem z kilku stron, sprawdz czy model nie ma brakujacych fragmentow.
            """,
            "Technologia");

        Assert.Contains("telefon", brief.Audience, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("model 3D", brief.ViewerProblem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skan", brief.DesiredOutcome, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powiadomien", brief.ViewerProblem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pierwszy ekran", brief.DesiredOutcome, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FillMissing_WhenBriefHasEmptyValues_UsesThematicFallback()
    {
        var fallback = ContentBriefService.CreateForTopic(
            "Aplikacja AI, ktora robi notatki z nagran",
            "Korzysc dla widza: szybsze znalezienie decyzji i zadan.",
            "Technologia");
        var brief = new ContentBrief
        {
            Audience = "",
            ViewerProblem = "",
            DesiredOutcome = "",
            DurationSeconds = 0
        };

        ContentBriefService.FillMissing(brief, fallback);

        Assert.Contains("spotkan", brief.Audience, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decyzje", brief.ViewerProblem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decyzje", brief.DesiredOutcome, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(25, brief.DurationSeconds);
    }
}
