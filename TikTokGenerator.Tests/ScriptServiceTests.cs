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
              "scenes": [
                {
                  "text": "Pierwsza scena pokazuje problem.",
                  "searchPhrase": "person using phone"
                },
                {
                  "text": "Druga scena pokazuje korzysc.",
                  "searchPhrase": "productivity app phone"
                },
                {
                  "text": "Trzecia scena daje wskazowke.",
                  "searchPhrase": "person taking notes"
                }
              ],
              "ending": "Sprawdz to samodzielnie."
            }
            """;

        var script = ScriptService.ParseScriptOrFallback(response, topic);

        Assert.Equal("Dobry tytul", script.Title);
        Assert.Equal(3, script.Scenes.Count);
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.SearchPhrase)));
    }

    [Fact]
    public void ParseScriptOrFallback_WhenJsonIsTruncated_DoesNotThrowAndBuildsAtLeastThreeScenes()
    {
        var topic = CreateTopic();
        var response = """
            {
              "title": "Utnieta odpowiedz",
              "hook": "To powinno nadal zadzialac.",
              "scenes": [
                {
                  "text": "Pierwsza kompletna scena.",
                  "searchPhrase": "phone app vertical"
                },
                {
                  "text": "Druga kompletna scena.",
                  "searchPhrase": "meeting notes"
                },
                {
                  "text": "Trzecia scena jest ucie
            """;

        var script = ScriptService.ParseScriptOrFallback(response, topic);

        Assert.False(string.IsNullOrWhiteSpace(script.Title));
        Assert.False(string.IsNullOrWhiteSpace(script.Hook));
        Assert.True(script.Scenes.Count >= 3);
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.Text)));
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
    public void NormalizeScript_WhenSceneCountIsTooLow_AddsFallbackScenes()
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
                    Text = "Jedna scena z modelu.",
                    SearchPhrase = "one scene"
                }
            ]
        };

        ScriptService.NormalizeScript(script, topic);

        Assert.True(script.Scenes.Count >= 3);
        Assert.All(script.Scenes, scene => Assert.False(string.IsNullOrWhiteSpace(scene.SearchPhrase)));
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
