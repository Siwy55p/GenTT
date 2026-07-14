using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class PipelineQualityRegressionTests
{
    [Fact]
    public void SourceAnalysisDiagnostics_WhenAnalysisAddsOfflineMode_FlagsUnsupportedValue()
    {
        var topic = new SelectedTopic
        {
            Title = "Aplikacja AI do notatek",
            SourceUrl = "offline://test",
            SourceText = "Aplikacja AI robi notatki z nagran. Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych."
        };
        var analysis = new SourceAnalysis
        {
            MainThesis = "Aplikacja AI robi notatki z nagran.",
            Facts =
            [
                new SourceFact
                {
                    Id = "F1",
                    Text = "Aplikacja dziala offline i generuje notatki bez internetu.",
                    Evidence = "tryb offline"
                }
            ],
            Examples = ["Klient chce zwiekszyc sprzedaz przez 20% w 3 miesiace."],
            MostUsefulFragment = "Aplikacja AI robi notatki z nagran."
        };

        var diagnostics = SourceAnalysisDiagnosticsService.CreateDiagnostics(topic, analysis);

        Assert.Contains(diagnostics.Issues, issue => issue.Field == "fact:F1");
        Assert.Contains(diagnostics.Issues, issue => issue.Field == "example:1");
        Assert.True(diagnostics.HasBlockingIssues);
    }

    [Fact]
    public void CreateScriptDiagnostics_WhenSourceSaysOneMinute_DoesNotFlagDigitOneAsUnsupported()
    {
        var topic = new SelectedTopic
        {
            Title = "Poranny rytual",
            SourceUrl = "offline://test",
            SourceText = "Praktyczna teza: jedna minuta planowania rano moze ograniczyc chaos na starcie dnia."
        };
        var script = new ShortScript
        {
            Title = "Poranny rytual",
            Hook = "Jak zaczac planowac w 1 min, jesli dzisiaj jest chaos?",
            HookOnScreenText = "1 min planowania",
            HookSearchPhrase = "person writing morning plan",
            Ending = "Zapisz prosty plan startowy.",
            EndingOnScreenText = "Prosty plan",
            EndingSearchPhrase = "person writing morning plan",
            Scenes =
            [
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Zapisz jeden priorytet.",
                    SourceFactIds = ["F1"],
                    NewInformation = "Jeden priorytet na start.",
                    OnScreenText = "Jeden priorytet",
                    VisualDescription = "Osoba zapisuje priorytet.",
                    SearchPhrase = "person writing morning priority",
                    SceneGoal = "Pokazac krok."
                }
            ]
        };

        var report = ShortDiagnosticsService.CreateScriptDiagnostics(topic, script);

        Assert.DoesNotContain(report.Issues, issue => issue.Code == "unsupported_number" && issue.Evidence == "1");
    }

    [Fact]
    public void EvaluateBeforeRender_WhenPayoffUsesUnsupportedEmailExample_BlocksRender()
    {
        var topic = CreateMorningTopic();
        var analysis = CreateMorningAnalysis();
        var script = CreateMorningScript(payoff: "Odpowiedz na e-mail z klientem.");
        var review = CreateApprovedReview();
        var visualPlan = CreateGoodVisualPlan();

        var report = QualityGateService.EvaluateBeforeRender(
            topic,
            analysis,
            script,
            review,
            visualPlan,
            CreateVoiceSegments(script),
            CreateClips());

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Code == "unsupported_payoff");
    }

    [Fact]
    public void EvaluateBeforeRender_WhenReviewerSaysHookNotMet_ReducesHookPayoffScore()
    {
        var topic = CreateMorningTopic();
        var analysis = CreateMorningAnalysis();
        var script = CreateMorningScript(payoff: "Zapisz jeden priorytet, jedno male zadanie i jedna rzecz, ktorej dzis nie robisz.");
        var review = CreateApprovedReview();
        review.Issues.Add(new ContentReviewIssue
        {
            Severity = "warning",
            Segment = "hook",
            Code = "hookNotMet",
            Message = "Hook nie jest spelniony przez payoff.",
            SuggestedFix = "Dopasuj payoff do hooka."
        });
        review.PromiseCheck = "Obietnica hooka nie jest spelniona.";

        var report = QualityGateService.EvaluateBeforeRender(
            topic,
            analysis,
            script,
            review,
            CreateGoodVisualPlan(),
            CreateVoiceSegments(script),
            CreateClips());

        var hookCriterion = Assert.Single(report.Criteria, criterion => criterion.Name == "Hook zgodny z payoffem");
        Assert.Equal(3, hookCriterion.Points);
        Assert.False(report.Passed);
    }

    [Fact]
    public void EvaluateBeforeRender_WhenSourceAnalysisContainsUnsupportedExample_BlocksRender()
    {
        var topic = CreateMorningTopic();
        var analysis = CreateMorningAnalysis();
        analysis.Examples = ["Odpowiedz na e-mail z klientem."];
        var script = CreateMorningScript(payoff: "Zapisz prosty plan startowy.");

        var report = QualityGateService.EvaluateBeforeRender(
            topic,
            analysis,
            script,
            CreateApprovedReview(),
            CreateGoodVisualPlan(),
            CreateVoiceSegments(script),
            CreateClips());

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Code == "source_analysis_unsupported");
    }

    [Fact]
    public void SanitizeUnsupportedContent_WhenAnalysisAddsEmailExample_RemovesIt()
    {
        var topic = CreateMorningTopic();
        var analysis = CreateMorningAnalysis();
        analysis.Examples = ["Odpowiedz na e-mail z klientem."];

        SourceAnalysisDiagnosticsService.SanitizeUnsupportedContent(topic, analysis);
        var diagnostics = SourceAnalysisDiagnosticsService.CreateDiagnostics(topic, analysis);

        Assert.Empty(analysis.Examples);
        Assert.False(diagnostics.HasBlockingIssues);
    }

    [Fact]
    public void EvaluateBeforeRender_WhenVisualPlanRepeatsAlarmForActionScenes_BlocksRender()
    {
        var topic = CreateMorningTopic();
        var analysis = CreateMorningAnalysis();
        var script = CreateMorningScript(payoff: "Masz prosty plan startowy.");
        var visualPlan = new VisualPlan
        {
            Segments =
            [
                CreateAlarmVisual("scene_01"),
                CreateAlarmVisual("scene_02"),
                CreateAlarmVisual("scene_03")
            ]
        };

        var report = QualityGateService.EvaluateBeforeRender(
            topic,
            analysis,
            script,
            CreateApprovedReview(),
            visualPlan,
            CreateVoiceSegments(script),
            CreateClips());

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Code == "repeated_visual_plan");
    }

    [Fact]
    public void EvaluateBeforeRender_WhenBriefOutcomeDominatesUnrelatedTopic_BlocksRender()
    {
        var topic = new SelectedTopic
        {
            Title = "Aplikacja AI, ktora robi notatki z nagran",
            SourceUrl = "offline://test",
            SourceText = "Aplikacja AI robi notatki z nagran.",
            Brief = new ContentBrief
            {
                DesiredOutcome = "wybrac pierwszy priorytet",
                ViewerProblem = "chaos po rozpoczeciu dnia"
            }
        };
        var analysis = new SourceAnalysis
        {
            MainThesis = "Aplikacja AI robi notatki z nagran.",
            Facts =
            [
                new SourceFact
                {
                    Id = "F1",
                    Text = "Aplikacja AI robi notatki z nagran.",
                    Evidence = "Aplikacja AI robi notatki z nagran."
                }
            ],
            MostUsefulFragment = "Aplikacja AI robi notatki z nagran."
        };
        var script = new ShortScript
        {
            Title = "Wybierz pierwszy priorytet w 2 minuty",
            Hook = "Dzien zaczyna sie chaosem?",
            HookOnScreenText = "Chaos",
            HookSearchPhrase = "close up smartphone productivity app",
            Ending = "Wybierz pierwszy priorytet.",
            EndingOnScreenText = "Pierwszy priorytet",
            EndingSearchPhrase = "person planning task",
            Scenes =
            [
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Wybierz pierwszy priorytet.",
                    SourceFactIds = ["F1"],
                    NewInformation = "Wybieranie pierwszego priorytetu.",
                    OnScreenText = "Pierwszy priorytet",
                    VisualDescription = "Osoba planuje zadanie.",
                    SearchPhrase = "person planning task",
                    SceneGoal = "Pokazac krok."
                }
            ]
        };

        var report = QualityGateService.EvaluateBeforeRender(
            topic,
            analysis,
            script,
            CreateApprovedReview(),
            CreateGoodVisualPlan(),
            CreateVoiceSegments(script),
            CreateClips());

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Code == "topic_brief_drift");
    }

    private static SelectedTopic CreateMorningTopic()
    {
        return new SelectedTopic
        {
            Title = "Poranny rytual",
            SourceUrl = "offline://test",
            SourceText = "Jedna minuta planowania rano moze ograniczyc chaos. Zapisz jeden priorytet, jedno male zadanie i jedna rzecz, ktorej dzis swiadomie nie robisz.",
            Brief = new ContentBrief()
        };
    }

    private static SourceAnalysis CreateMorningAnalysis()
    {
        return new SourceAnalysis
        {
            MainThesis = "Jedna minuta planowania rano moze ograniczyc chaos.",
            Facts =
            [
                new SourceFact { Id = "F1", Text = "Jedna minuta planowania rano moze ograniczyc chaos.", Evidence = "Jedna minuta planowania rano moze ograniczyc chaos." },
                new SourceFact { Id = "F2", Text = "Zapisz jeden priorytet.", Evidence = "Zapisz jeden priorytet." },
                new SourceFact { Id = "F3", Text = "Zapisz jedno male zadanie.", Evidence = "Zapisz jedno male zadanie." }
            ],
            MostUsefulFragment = "Zapisz jeden priorytet, jedno male zadanie i jedna rzecz, ktorej dzis swiadomie nie robisz."
        };
    }

    private static ShortScript CreateMorningScript(string payoff)
    {
        return new ShortScript
        {
            Title = "Poranny rytual",
            Hook = "Jak zaczac planowac w 1 min, gdy jest chaos?",
            HookOnScreenText = "1 min planowania",
            HookSearchPhrase = "person writing morning plan",
            Ending = payoff,
            EndingOnScreenText = payoff,
            EndingSearchPhrase = "person writing morning plan",
            Scenes =
            [
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Zapisz jeden priorytet.",
                    SourceFactIds = ["F2"],
                    NewInformation = "Jeden priorytet.",
                    OnScreenText = "Jeden priorytet",
                    VisualDescription = "Osoba zapisuje priorytet w notesie.",
                    SearchPhrase = "person writing priority in notebook",
                    SceneGoal = "Pokazac pierwszy krok."
                },
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Dopisz jedno male zadanie.",
                    SourceFactIds = ["F3"],
                    NewInformation = "Jedno male zadanie.",
                    OnScreenText = "Male zadanie",
                    VisualDescription = "Osoba dopisuje male zadanie.",
                    SearchPhrase = "person writing small task",
                    SceneGoal = "Pokazac drugi krok."
                }
            ]
        };
    }

    private static ContentReview CreateApprovedReview()
    {
        return new ContentReview
        {
            Approved = true,
            UsefulnessScore = 9,
            AudienceValueCheck = "Daje odbiorcy konkretny krok.",
            PromiseCheck = "Hook i payoff sa spojne."
        };
    }

    private static VisualPlan CreateGoodVisualPlan()
    {
        return new VisualPlan
        {
            Segments =
            [
                new VisualPlanSegment
                {
                    SegmentName = "scene_01",
                    VisibleContent = "Notes z priorytetem.",
                    PersonAction = "Osoba zapisuje priorytet.",
                    PrimaryObject = "notebook",
                    ResultToShow = "Widoczny rezultat: prosty plan.",
                    SearchPhrases = ["person writing priority in notebook"]
                }
            ]
        };
    }

    private static VisualPlanSegment CreateAlarmVisual(string segmentName)
    {
        return new VisualPlanSegment
        {
            SegmentName = segmentName,
            VisibleContent = "Alarm clock",
            PersonAction = "person turning off morning alarm clock",
            PrimaryObject = "alarm clock",
            ShotType = "close up",
            ResultToShow = "alarm clock",
            SearchPhrases = ["person turning off morning alarm clock", "alarm clock"]
        };
    }

    private static IReadOnlyList<VoiceSegment> CreateVoiceSegments(ShortScript script)
    {
        var segments = new List<VoiceSegment>
        {
            CreateVoiceSegment(0, "hook", script.Hook, TimeSpan.FromSeconds(3))
        };
        segments.AddRange(script.Scenes.Select((scene, index) =>
            CreateVoiceSegment(index + 1, $"scene_{index + 1:00}", scene.VoiceOver, TimeSpan.FromSeconds(3), scene)));
        segments.Add(CreateVoiceSegment(script.Scenes.Count + 1, "ending", script.Ending, TimeSpan.FromSeconds(3)));
        return segments;
    }

    private static VoiceSegment CreateVoiceSegment(
        int index,
        string name,
        string text,
        TimeSpan duration,
        ScriptScene? scene = null)
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
            VisualDescription = scene?.VisualDescription ?? "Osoba wykonuje krok.",
            SearchPhrase = scene?.SearchPhrase ?? "person writing plan",
            SearchPhrases = scene is null ? ["person writing plan"] : [scene.SearchPhrase],
            AvoidVisuals = "random selfie",
            AudioPath = string.Empty,
            Duration = duration
        };
    }

    private static IReadOnlyList<DownloadedVideoClip> CreateClips()
    {
        return
        [
            CreateClip(0),
            CreateClip(1),
            CreateClip(2),
            CreateClip(3)
        ];
    }

    private static DownloadedVideoClip CreateClip(int segmentIndex)
    {
        return new DownloadedVideoClip
        {
            SegmentIndex = segmentIndex,
            SearchPhrase = "person writing plan",
            VisualDescription = "Osoba zapisuje plan.",
            FilePath = $"clip-{segmentIndex}.mp4",
            PexelsUrl = $"https://www.pexels.com/video/{segmentIndex}/",
            PexelsRank = 1,
            SelectionReason = "test",
            AuthorName = "Pexels",
            AuthorUrl = "https://www.pexels.com"
        };
    }
}
