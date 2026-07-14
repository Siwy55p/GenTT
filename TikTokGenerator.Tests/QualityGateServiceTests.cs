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

    [Fact]
    public void EvaluateBeforeRender_WhenScanner3DHasNamedObjectExampleAndVisibleModelResult_AllowsRender()
    {
        var topic = CreateScannerTopic();
        var analysis = CreateScannerAnalysis();
        var script = CreateScannerScript();
        script.Scenes[0].VoiceOver = "Wybierz maly obiekt z faktura, na przyklad kamien albo figurke.";
        script.Scenes[0].VisualDescription = "Na stole lezy kamien albo figurka jako przyklad obiektu do skanu.";
        script.Ending = "Sprawdz w aplikacji, czy model 3D nie ma brakujacych fragmentow.";
        var visualPlan = CreateScannerVisualPlan(
            primaryObject: "Obiekt do skanu, np. kamien albo figurka",
            resultToShow: "Na ekranie widac podglad modelu 3D bez brakujacych fragmentow");

        var report = QualityGateService.EvaluateBeforeRender(
            topic,
            analysis,
            script,
            CreateApprovedReview(),
            visualPlan,
            CreateScannerVoiceSegments(script),
            CreateScannerClips());

        Assert.True(report.Passed, string.Join("; ", report.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.DoesNotContain(report.Issues, issue => issue.Code == "missing_example_or_result");
    }

    [Fact]
    public void EvaluateBeforeRender_WhenScanner3DOnlyShowsGenericPhone_BlocksMissingExampleOrResult()
    {
        var topic = CreateScannerTopic();
        var analysis = CreateScannerAnalysis();
        var script = CreateScannerScript();
        var visualPlan = CreateScannerVisualPlan(
            primaryObject: "Telefon",
            resultToShow: "Telefon trzymany w reku");

        var report = QualityGateService.EvaluateBeforeRender(
            topic,
            analysis,
            script,
            CreateApprovedReview(),
            visualPlan,
            CreateScannerVoiceSegments(script),
            CreateScannerClips());

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Code == "missing_example_or_result");
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

    private static SelectedTopic CreateScannerTopic()
    {
        return new SelectedTopic
        {
            Title = "Ciekawostka technologiczna: telefon jako skaner 3D",
            SourceUrl = "offline://test",
            SourceText = """
            Praktyczna teza: telefon moze posluzyc do prostego skanu 3D obiektu przez fotogrametrie.
            Konkretne kroki: wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke, obejdz go telefonem z kilku stron, sprawdz w aplikacji czy model nie ma brakujacych fragmentow.
            """,
            Brief = new ContentBrief { DurationSeconds = 25 }
        };
    }

    private static SourceAnalysis CreateScannerAnalysis()
    {
        return new SourceAnalysis
        {
            MainThesis = "Telefon moze posluzyc do prostego skanu 3D obiektu przez fotogrametrie.",
            Facts =
            [
                new SourceFact { Id = "F1", Text = "Telefon moze posluzyc do prostego skanu 3D obiektu przez fotogrametrie.", Evidence = "Praktyczna teza" },
                new SourceFact { Id = "F2", Text = "Wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke.", Evidence = "Konkretne kroki" },
                new SourceFact { Id = "F3", Text = "Sprawdz w aplikacji czy model nie ma brakujacych fragmentow.", Evidence = "Konkretne kroki" }
            ],
            Steps =
            [
                new SourceStep { Id = "S1", Text = "Wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke", SourceFactIds = ["F2"] },
                new SourceStep { Id = "S2", Text = "Obejdz go telefonem z kilku stron", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S3", Text = "Sprawdz w aplikacji czy model nie ma brakujacych fragmentow", SourceFactIds = ["F3"] }
            ],
            MostUsefulFragment = "Wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke, obejdz go telefonem z kilku stron, sprawdz w aplikacji czy model nie ma brakujacych fragmentow."
        };
    }

    private static ShortScript CreateScannerScript()
    {
        return new ShortScript
        {
            Title = "Telefon jako skaner 3D",
            Hook = "Telefon moze zrobic prosty skan 3D?",
            HookOnScreenText = "Skan 3D telefonem",
            HookSearchPhrase = "smartphone photogrammetry object scan",
            Ending = "Sprawdz w aplikacji czy model nie ma brakujacych fragmentow.",
            EndingOnScreenText = "Sprawdz model",
            EndingSearchPhrase = "smartphone photogrammetry 3d model preview",
            Scenes =
            [
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Wybierz maly nieruchomy obiekt z wyrazna faktura.",
                    SourceFactIds = ["F2"],
                    NewInformation = "Wybierasz stabilny obiekt z faktura.",
                    OnScreenText = "Wybierz obiekt",
                    VisualDescription = "Telefon i maly obiekt na stole.",
                    SearchPhrase = "smartphone photogrammetry object scan",
                    SearchPhrases = ["smartphone photogrammetry object scan"],
                    SceneGoal = "Pokazac obiekt do skanu."
                },
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Obejdz go telefonem z kilku stron.",
                    SourceFactIds = ["F1"],
                    NewInformation = "Skan wymaga ujet z kilku stron.",
                    OnScreenText = "Obejdz obiekt",
                    VisualDescription = "Osoba filmuje obiekt telefonem z kilku stron.",
                    SearchPhrase = "person filming small object with smartphone",
                    SearchPhrases = ["person filming small object with smartphone"],
                    SceneGoal = "Pokazac ruch telefonu wokol obiektu."
                },
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Sprawdz w aplikacji czy model nie ma brakujacych fragmentow.",
                    SourceFactIds = ["F3"],
                    NewInformation = "Sprawdzasz brakujace fragmenty modelu.",
                    OnScreenText = "Sprawdz model",
                    VisualDescription = "Ekran telefonu pokazuje model.",
                    SearchPhrase = "smartphone photogrammetry 3d model preview",
                    SearchPhrases = ["smartphone photogrammetry 3d model preview"],
                    SceneGoal = "Pokazac sprawdzenie modelu."
                }
            ]
        };
    }

    private static ContentReview CreateApprovedReview()
    {
        return new ContentReview
        {
            Approved = true,
            UsefulnessScore = 10,
            AudienceValueCheck = "Daje odbiorcy konkretny przyklad i rezultat.",
            PromiseCheck = "Hook i payoff sa spojne."
        };
    }

    private static VisualPlan CreateScannerVisualPlan(string primaryObject, string resultToShow)
    {
        return new VisualPlan
        {
            Segments =
            [
                new VisualPlanSegment
                {
                    SegmentName = "scene_01",
                    VisibleContent = "Maly obiekt na stole",
                    PersonAction = "Osoba wybiera obiekt do skanu",
                    PrimaryObject = primaryObject,
                    ResultToShow = resultToShow,
                    SearchPhrases = ["smartphone photogrammetry object scan"]
                }
            ]
        };
    }

    private static IReadOnlyList<VoiceSegment> CreateScannerVoiceSegments(ShortScript script)
    {
        return new[]
        {
            CreateScannerVoiceSegment(0, "hook", script.Hook, null),
            CreateScannerVoiceSegment(1, "scene_01", script.Scenes[0].VoiceOver, script.Scenes[0]),
            CreateScannerVoiceSegment(2, "scene_02", script.Scenes[1].VoiceOver, script.Scenes[1]),
            CreateScannerVoiceSegment(3, "scene_03", script.Scenes[2].VoiceOver, script.Scenes[2]),
            CreateScannerVoiceSegment(4, "ending", script.Ending, null)
        };
    }

    private static VoiceSegment CreateScannerVoiceSegment(int index, string name, string text, ScriptScene? scene)
    {
        return new VoiceSegment
        {
            Index = index,
            Name = name,
            Role = scene?.Role ?? name,
            Text = text,
            SourceFactIds = scene?.SourceFactIds ?? [],
            NewInformation = scene?.NewInformation ?? string.Empty,
            OnScreenText = scene?.OnScreenText ?? text,
            VisualDescription = scene?.VisualDescription ?? "Telefon pokazuje skan 3D.",
            SearchPhrase = scene?.SearchPhrase ?? "smartphone photogrammetry object scan",
            SearchPhrases = scene?.SearchPhrases ?? ["smartphone photogrammetry object scan"],
            AudioPath = string.Empty,
            Duration = TimeSpan.FromSeconds(3)
        };
    }

    private static IReadOnlyList<DownloadedVideoClip> CreateScannerClips()
    {
        return Enumerable.Range(0, 5)
            .Select(index => new DownloadedVideoClip
            {
                SegmentIndex = index,
                SearchPhrase = "smartphone photogrammetry object scan",
                VisualDescription = "Telefon pokazuje skan 3D.",
                FilePath = $"clip-{index}.mp4",
                PexelsUrl = $"https://www.pexels.com/video/scanner-{index}/",
                PexelsRank = 1,
                SelectionReason = "test",
                AuthorName = "Pexels",
                AuthorUrl = "https://www.pexels.com"
            })
            .ToList();
    }
}
