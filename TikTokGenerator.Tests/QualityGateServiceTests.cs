using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class QualityGateServiceTests
{
    [Fact]
    public void EvaluateBeforeRender_WhenMeasuredVoiceIsOverBriefLimit_BlocksRender()
    {
        var topic = new SelectedTopic
        {
            Title = "Poranny priorytet",
            SourceUrl = "offline://test",
            SourceText = "Zapisz jeden priorytet i sprawdz efekt po wykonaniu.",
            Brief = new ContentBrief { DurationSeconds = 25 }
        };
        var analysis = new SourceAnalysis
        {
            MainThesis = "Jeden priorytet pomaga zaczac dzien.",
            Facts =
            [
                new SourceFact
                {
                    Id = "F1",
                    Text = "Zapisz jeden priorytet i sprawdz efekt po wykonaniu.",
                    Evidence = "Zapisz jeden priorytet i sprawdz efekt po wykonaniu."
                }
            ],
            MostUsefulFragment = "Zapisz jeden priorytet."
        };
        var script = new ShortScript
        {
            Title = topic.Title,
            Hook = "Chaos rano znika, gdy wybierzesz jeden priorytet.",
            HookOnScreenText = "Jeden priorytet",
            HookSearchPhrase = "person writing priority at desk",
            Ending = "Po wykonaniu sprawdz efekt i dopiero wtedy dodaj kolejne zadanie.",
            EndingOnScreenText = "Sprawdz efekt",
            EndingSearchPhrase = "person checking completed task",
            Scenes =
            [
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Zapisz jedno zadanie, ktore ma isc do przodu jako pierwsze.",
                    SourceFactIds = ["F1"],
                    NewInformation = "Pierwsze zadanie ma byc jednym priorytetem.",
                    OnScreenText = "Zapisz 1 zadanie",
                    VisualDescription = "Osoba zapisuje jeden priorytet w notesie.",
                    SearchPhrase = "person writing priority in notebook",
                    SearchPhrases = ["person writing priority in notebook"],
                    AvoidVisuals = "random selfie",
                    SceneGoal = "Pokazac konkretny krok."
                }
            ]
        };
        var review = new ContentReview
        {
            Approved = true,
            UsefulnessScore = 10,
            AudienceValueCheck = "Daje odbiorcy jasny pierwszy krok.",
            PromiseCheck = "Ending dowozi obietnice hooka."
        };
        var visualPlan = new VisualPlan
        {
            Segments =
            [
                new VisualPlanSegment
                {
                    SegmentName = "scene_01",
                    VisibleContent = "Notes z jednym zadaniem.",
                    ResultToShow = "Widoczny efekt: jeden priorytet na kartce.",
                    SearchPhrases = ["person writing priority in notebook"]
                }
            ]
        };
        var voiceSegments = new[]
        {
            CreateVoiceSegment(0, "hook", TimeSpan.FromSeconds(8)),
            CreateVoiceSegment(1, "scene_01", TimeSpan.FromSeconds(13)),
            CreateVoiceSegment(2, "ending", TimeSpan.FromSeconds(10))
        };
        var clips = new[]
        {
            new DownloadedVideoClip
            {
                SegmentIndex = 0,
                SearchPhrase = "person writing priority at desk",
                VisualDescription = "Osoba zapisuje priorytet.",
                FilePath = "hook.mp4",
                PexelsUrl = "https://www.pexels.com/video/hook/",
                PexelsRank = 1,
                SelectionReason = "test",
                AuthorName = "Pexels",
                AuthorUrl = "https://www.pexels.com"
            },
            new DownloadedVideoClip
            {
                SegmentIndex = 1,
                SearchPhrase = "person writing priority in notebook",
                VisualDescription = "Osoba zapisuje priorytet.",
                FilePath = "scene.mp4",
                PexelsUrl = "https://www.pexels.com/video/scene/",
                PexelsRank = 1,
                SelectionReason = "test",
                AuthorName = "Pexels",
                AuthorUrl = "https://www.pexels.com"
            },
            new DownloadedVideoClip
            {
                SegmentIndex = 2,
                SearchPhrase = "person checking completed task",
                VisualDescription = "Osoba sprawdza efekt.",
                FilePath = "ending.mp4",
                PexelsUrl = "https://www.pexels.com/video/ending/",
                PexelsRank = 1,
                SelectionReason = "test",
                AuthorName = "Pexels",
                AuthorUrl = "https://www.pexels.com"
            }
        };

        var report = QualityGateService.EvaluateBeforeRender(topic, analysis, script, review, visualPlan, voiceSegments, clips);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Code == "duration_over_limit");
    }

    private static VoiceSegment CreateVoiceSegment(int index, string name, TimeSpan duration)
    {
        return new VoiceSegment
        {
            Index = index,
            Name = name,
            Role = name == "scene_01" ? "action" : name,
            Text = name == "scene_01"
                ? "Zapisz jedno zadanie, ktore ma isc do przodu jako pierwsze."
                : "Sprawdz efekt po wykonaniu.",
            SourceFactIds = name == "scene_01" ? ["F1"] : [],
            NewInformation = name == "scene_01" ? "Pierwsze zadanie ma byc jednym priorytetem." : string.Empty,
            OnScreenText = name == "scene_01" ? "Zapisz 1 zadanie" : "Sprawdz efekt",
            VisualDescription = "Osoba wykonuje praktyczny krok.",
            SearchPhrase = "person writing priority in notebook",
            SearchPhrases = ["person writing priority in notebook"],
            AvoidVisuals = "random selfie",
            AudioPath = string.Empty,
            Duration = duration
        };
    }
}
