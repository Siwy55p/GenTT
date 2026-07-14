using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class ShortDiagnosticsServiceTests
{
    [Fact]
    public void CreateScriptDiagnostics_WhenClaimIsNotInSource_FlagsUnsupportedClaim()
    {
        var topic = new SelectedTopic
        {
            Title = "Telefon jako skaner 3D",
            SourceUrl = "offline://test",
            SourceText = "Temat roboczy: Telefon jako skaner 3D. Pokaz prosty krok i efekt."
        };
        var script = new ShortScript
        {
            Title = "Telefon jako skaner 3D",
            Hook = "Telefon dziala jako skaner 3D bez dodatkowych urzadzen.",
            HookOnScreenText = "Skaner 3D w telefonie",
            HookSearchPhrase = "phone 3d scanner",
            Ending = "Sprawdz efekt w aplikacji.",
            EndingOnScreenText = "Sprawdz efekt",
            EndingSearchPhrase = "phone showing 3d model",
            Scenes =
            [
                new ScriptScene
                {
                    VoiceOver = "Otworz aplikacje i skanuj przedmiot powoli.",
                    OnScreenText = "Otworz aplikacje",
                    VisualDescription = "Telefon skanuje przedmiot na biurku.",
                    SearchPhrase = "phone scanning object on desk",
                    SceneGoal = "Pokazac pierwszy krok."
                }
            ]
        };

        var report = ShortDiagnosticsService.CreateScriptDiagnostics(topic, script);

        Assert.Contains(report.Issues, issue => issue.Code == "unsupported_claim_phrase");
        Assert.True(report.Summary.HasUnsupportedClaims);
        Assert.Contains("telefon", report.Script.MatchedSourceKeywords);
    }

    [Fact]
    public void CreateVoiceDiagnostics_WhenAudioIsOverTarget_FlagsTotalDuration()
    {
        var topic = CreateTopic();
        var script = CreateScript();
        var segments = new[]
        {
            CreateVoiceSegment(0, "hook", TimeSpan.FromSeconds(8)),
            CreateVoiceSegment(1, "scene_01", TimeSpan.FromSeconds(9)),
            CreateVoiceSegment(2, "scene_02", TimeSpan.FromSeconds(9))
        };

        var report = ShortDiagnosticsService.CreateVoiceDiagnostics(topic, script, segments);

        Assert.Contains(report.Issues, issue => issue.Code == "total_duration_over_target");
        Assert.True(report.Summary.EstimatedDurationSeconds > 25);
    }

    [Fact]
    public void CreateClipDiagnostics_WhenClipRepeats_FlagsDuplicateUrl()
    {
        var topic = CreateTopic();
        var script = CreateScript();
        var segments = new[]
        {
            CreateVoiceSegment(0, "hook", TimeSpan.FromSeconds(3)),
            CreateVoiceSegment(1, "scene_01", TimeSpan.FromSeconds(4))
        };
        var clips = new[]
        {
            CreateClip(0, "https://www.pexels.com/video/repeated/"),
            CreateClip(1, "https://www.pexels.com/video/repeated/")
        };

        var report = ShortDiagnosticsService.CreateClipDiagnostics(topic, script, segments, clips);

        Assert.Contains(report.Issues, issue => issue.Code == "duplicate_clip_url");
    }

    private static SelectedTopic CreateTopic()
    {
        return new SelectedTopic
        {
            Title = "Testowy short",
            SourceUrl = "offline://test",
            SourceText = "Pokaz prosty problem, jeden krok i sprawdzenie efektu."
        };
    }

    private static ShortScript CreateScript()
    {
        return new ShortScript
        {
            Title = "Testowy short",
            Hook = "Zacznij od jednego prostego kroku.",
            HookOnScreenText = "Jeden krok",
            HookSearchPhrase = "person planning task",
            Ending = "Sprawdz efekt po wykonaniu.",
            EndingOnScreenText = "Sprawdz efekt",
            EndingSearchPhrase = "person checking task",
            Scenes =
            [
                new ScriptScene
                {
                    VoiceOver = "Otworz liste i wybierz jedno zadanie.",
                    OnScreenText = "Wybierz zadanie",
                    VisualDescription = "Osoba wybiera zadanie z listy.",
                    SearchPhrase = "person choosing task from list",
                    SceneGoal = "Pokazac praktyczny krok."
                }
            ]
        };
    }

    private static VoiceSegment CreateVoiceSegment(int index, string name, TimeSpan duration)
    {
        return new VoiceSegment
        {
            Index = index,
            Name = name,
            Text = index == 0 ? "Zacznij od jednego prostego kroku." : "Otworz liste i wybierz jedno zadanie.",
            OnScreenText = index == 0 ? "Jeden krok" : "Wybierz zadanie",
            VisualDescription = "Osoba wykonuje praktyczny krok.",
            SearchPhrase = "person planning task",
            AudioPath = string.Empty,
            Duration = duration
        };
    }

    private static DownloadedVideoClip CreateClip(int segmentIndex, string url)
    {
        return new DownloadedVideoClip
        {
            SegmentIndex = segmentIndex,
            SearchPhrase = "person planning task",
            VisualDescription = "Osoba wykonuje praktyczny krok.",
            FilePath = $"clip-{segmentIndex}.mp4",
            PexelsUrl = url,
            PexelsRank = 1,
            SelectionReason = "test",
            AuthorName = "Pexels",
            AuthorUrl = "https://www.pexels.com"
        };
    }
}
