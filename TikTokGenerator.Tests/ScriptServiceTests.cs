using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class ScriptServiceTests
{
    [Fact]
    public void ParseScriptOrFallback_WhenJsonIsValid_ReturnsModelScenes()
    {
        var topic = CreateTopic();
        var response = """
            {
              "title": "Dobry tytul",
              "hook": "Pierwsze zdanie przyciaga uwage.",
              "hookOnScreenText": "Dobry start",
              "hookSearchPhrase": "person opening notes app",
              "scenes": [
                {
                  "voiceOver": "Zacznij od jednego konkretnego problemu.",
                  "onScreenText": "Jeden problem",
                  "visualDescription": "Osoba zapisuje problem w notesie.",
                  "searchPhrase": "person using phone",
                  "avoidVisuals": "random selfie",
                  "sceneGoal": "Pokazac problem."
                },
                {
                  "voiceOver": "Potem wybierz najmniejszy krok do wykonania.",
                  "onScreenText": "Maly krok",
                  "visualDescription": "Osoba wybiera zadanie z listy.",
                  "searchPhrase": "productivity app phone",
                  "avoidVisuals": "random selfie",
                  "sceneGoal": "Pokazac korzysc."
                },
                {
                  "voiceOver": "Na koniec sprawdz, czy ten krok pomaga.",
                  "onScreenText": "Sprawdz efekt",
                  "visualDescription": "Osoba odhacza zadanie.",
                  "searchPhrase": "person taking notes",
                  "avoidVisuals": "random selfie",
                  "sceneGoal": "Dac wskazowke."
                }
              ],
              "ending": "Sprawdz to samodzielnie.",
              "endingOnScreenText": "Sprawdz dzis",
              "endingSearchPhrase": "person checking completed task"
            }
            """;

        var script = ScriptService.ParseScriptOrFallback(response, topic);

        Assert.Equal("Dobry tytul", script.Title);
        Assert.Equal(3, script.Scenes.Count);
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.VoiceOver)));
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.OnScreenText)));
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.SearchPhrase)));
    }

    [Fact]
    public void ParseScriptOrFallback_WhenJsonIsTruncated_DoesNotThrowAndKeepsUsableScenes()
    {
        var topic = CreateTopic();
        var response = """
            {
              "title": "Utnieta odpowiedz",
              "hook": "To powinno nadal zadzialac.",
              "scenes": [
                {
                  "voiceOver": "Pierwsza kompletna wskazowka.",
                  "searchPhrase": "phone app vertical"
                },
                {
                  "voiceOver": "Druga kompletna wskazowka.",
                  "searchPhrase": "meeting notes"
                },
                {
                  "voiceOver": "Trzecia wskazowka jest ucie
            """;

        var script = ScriptService.ParseScriptOrFallback(response, topic);

        Assert.False(string.IsNullOrWhiteSpace(script.Title));
        Assert.False(string.IsNullOrWhiteSpace(script.Hook));
        Assert.True(script.Scenes.Count >= 1);
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.VoiceOver)));
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.SearchPhrase)));
        Assert.False(string.IsNullOrWhiteSpace(script.Ending));
    }

    [Fact]
    public void TryExtractCompleteJsonObject_WhenResponseHasMarkdownAndExtraText_ReturnsFirstObject()
    {
        var response = """
            ```json
            {
              "title": "Test",
              "hook": "Hook",
              "scenes": [],
              "ending": "Koniec"
            }
            ```
            dodatkowy tekst
            """;

        var ok = ScriptService.TryExtractCompleteJsonObject(response, out var json);

        Assert.True(ok);
        Assert.Contains("\"title\": \"Test\"", json);
        Assert.DoesNotContain("dodatkowy tekst", json);
    }

    [Fact]
    public void NormalizeScript_WhenOneSceneIsUseful_DoesNotForceThreeScenes()
    {
        var topic = CreateTopic();
        var script = new ShortScript
        {
            Title = "Test",
            Hook = "Hook",
            Ending = "Koniec",
            Scenes =
            [
                new ScriptScene
                {
                    VoiceOver = "Jedna wskazowka z modelu.",
                    SearchPhrase = "one scene"
                }
            ]
        };

        ScriptService.NormalizeScript(script, topic);

        Assert.Single(script.Scenes);
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.SearchPhrase)));
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.NewInformation)));
    }

    [Fact]
    public void NormalizeScript_WhenPhoneAppTopicHasNoScenes_AddsOnePhoneSpecificFallbackScene()
    {
        var topic = new SelectedTopic
        {
            Title = "Minimalizm w aplikacjach na telefonie",
            SourceUrl = "offline://test",
            SourceText = "Usun nieuzywane aplikacje, wylacz niepilne powiadomienia i zostaw tylko najwazniejsze narzedzia."
        };
        var script = new ShortScript
        {
            Title = topic.Title,
            Hook = "Uporzadkuj telefon w prosty sposob.",
            Ending = "Zostaw tylko to, co pomaga.",
            Scenes = []
        };

        ScriptService.NormalizeScript(script, topic);

        Assert.Single(script.Scenes);
        Assert.Contains(script.Scenes, scene => scene.SearchPhrase.Contains("smartphone", StringComparison.OrdinalIgnoreCase)
            || scene.SearchPhrase.Contains("phone notification", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(script.Scenes, scene => scene.SearchPhrase.Contains("notebook", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseScriptOrFallback_WhenModelWritesStoryboardText_RewritesVoiceOver()
    {
        var topic = new SelectedTopic
        {
            Title = "Poranny rytual",
            SourceUrl = "offline://test",
            SourceText = "Zapisz jeden priorytet, jedno male zadanie oraz jedna rzecz do odpuszczenia."
        };
        var response = """
            {
              "title": "Poranny rytual",
              "hook": "Czy wiesz, ze 70% osob zaczyna dzien z niepewnoscia?",
              "scenes": [
                {
                  "text": "W pierwszej scenie widzimy osobe zaczynajace poranek z niepewnoscia, patrzac na zegar.",
                  "searchPhrase": "morning routine confused person looking at clock"
                },
                {
                  "text": "Druga scena: osoba zapisuje sie w notatniku, zastanawiajac sie nad planami dnia.",
                  "searchPhrase": "person writing daily plan in notebook"
                },
                {
                  "text": "Trzecia scena: osoba zaczyna poranek z jasnym planem, usmiechajac sie z satysfakcja.",
                  "search: ": "person smiling with clear morning plan"
                }
              ],
              "ending": "Jedna minuta w porannej rutynie moze zmienic cale tempo dnia."
            }
            """;

        var script = ScriptService.ParseScriptOrFallback(response, topic, null, out var report);

        Assert.All(script.Scenes, scene => Assert.DoesNotContain("scena", scene.VoiceOver, StringComparison.OrdinalIgnoreCase));
        Assert.All(script.Scenes, scene => Assert.DoesNotContain("widzimy", scene.VoiceOver, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("70%", script.Hook, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("person smiling with clear morning plan", script.Scenes[2].SearchPhrase);
        Assert.Contains(report.Issues, issue => issue.Code == "storyboard_in_voiceover");
        Assert.Contains(report.Issues, issue => issue.Code == "malformed_search_key");
        Assert.Contains(report.Issues, issue => issue.Code == "unsupported_statistic");
    }

    private static SelectedTopic CreateTopic()
    {
        return new SelectedTopic
        {
            Title = "Aplikacja AI do notatek",
            SourceUrl = "offline://test",
            SourceText = "Aplikacja AI moze pomoc uporzadkowac notatki z nagran spotkan. Najwazniejsza korzysc to szybsze znalezienie decyzji i zadan. Wynik nadal warto sprawdzic samodzielnie."
        };
    }
}
