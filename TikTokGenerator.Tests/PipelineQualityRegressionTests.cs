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
    public void SanitizeUnsupportedContent_WhenModelInventsScannerSteps_RecoversConcreteStepsFromSource()
    {
        var topic = new SelectedTopic
        {
            Title = "Ciekawostka technologiczna: telefon jako skaner 3D",
            SourceUrl = "offline://test",
            SourceText = """
            Temat roboczy: Ciekawostka technologiczna: telefon jako skaner 3D
            Kategoria: Technologia
            Praktyczna teza: telefon moze posluzyc do prostego skanu 3D obiektu, gdy aplikacja sklada zdjecia lub nagranie z kilku stron w model.
            Konkretne kroki: wybierz maly nieruchomy obiekt z wyrazna faktura, obejdz go telefonem z kilku stron, sprawdz w aplikacji czy model nie ma brakujacych fragmentow.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """
        };
        var analysis = new SourceAnalysis
        {
            MainThesis = "Telefon tworzy idealny model 3D bez specjalnego sprzetu.",
            Facts =
            [
                new SourceFact { Id = "F1", Text = "Telefon tworzy idealny model 3D bez specjalnego sprzetu.", Evidence = "wymyslone" }
            ],
            Steps =
            [
                new SourceStep { Id = "S1", Text = "Kup profesjonalny skaner 3D.", SourceFactIds = ["F1"] }
            ],
            MostUsefulFragment = "Telefon tworzy idealny model 3D bez specjalnego sprzetu."
        };

        SourceAnalysisDiagnosticsService.SanitizeUnsupportedContent(topic, analysis);
        var diagnostics = SourceAnalysisDiagnosticsService.CreateDiagnostics(topic, analysis);

        Assert.False(diagnostics.HasBlockingIssues);
        Assert.Equal(3, analysis.Steps.Count);
        Assert.Contains(analysis.Steps, step => step.Text.Contains("wybierz maly nieruchomy obiekt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Steps, step => step.Text.Contains("obejdz go telefonem", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.Steps, step => step.Text.Contains("model nie ma brakujacych", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(analysis.Steps, step => step.Text.Contains("Kup profesjonalny", StringComparison.OrdinalIgnoreCase));
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
    public void EvaluateBeforeRender_WhenReviewerWarnsHookNotMet_ReducesHookPayoffScoreWithoutBlocking()
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
        Assert.True(report.Passed);
        Assert.DoesNotContain(report.Issues, issue => issue.Code == "promise_not_met");
    }

    [Fact]
    public void EvaluateBeforeRender_WhenReviewerErrorsHookNotMet_BlocksRender()
    {
        var topic = CreateMorningTopic();
        var analysis = CreateMorningAnalysis();
        var script = CreateMorningScript(payoff: "Zapisz jeden priorytet, jedno male zadanie i jedna rzecz, ktorej dzis nie robisz.");
        var review = CreateApprovedReview();
        review.Approved = false;
        review.Issues.Add(new ContentReviewIssue
        {
            Severity = "error",
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

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Code == "promise_not_met");
    }

    [Fact]
    public void EvaluateBeforeRender_WhenAiNotesReviewWasDowngraded_PassesQualityGate()
    {
        var topic = CreateAiNotesTopic();
        var analysis = CreateAiNotesAnalysis();
        var script = CreateAiNotesScript();
        var review = new ContentReview
        {
            Approved = true,
            UsefulnessScore = 7,
            AudienceValueCheck = "Daje odbiorcy konkretne kroki do sprawdzenia decyzji i zadan po nagraniu.",
            PromiseCheck = "Obietnica hooka nie jest spelniona, bo kroki wymagaja manualnego sprawdzenia.",
            Issues =
            [
                new ContentReviewIssue
                {
                    Severity = "warning",
                    Segment = "hook",
                    Code = "hookDoesNotMatchPayoff",
                    Message = "Hook nie jest spelniony przez payoff.",
                    SuggestedFix = "Dopasuj payoff do hooka."
                }
            ]
        };
        ScriptService.SanitizeReviewAgainstSource(topic, analysis, script, review);

        var report = QualityGateService.EvaluateBeforeRender(
            topic,
            analysis,
            script,
            review,
            CreateAiNotesVisualPlan(),
            CreateVoiceSegments(script),
            CreateClips(5));

        Assert.True(report.Passed);
        Assert.True(report.Score >= report.MinimumScore);
        Assert.DoesNotContain("nie jest spe", review.PromiseCheck, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(report.Issues, issue => issue.Code == "promise_not_met");
        Assert.Equal(15, Assert.Single(report.Criteria, criterion => criterion.Name == "Konkretnosc i wykonalnosc").Points);
        Assert.True(Assert.Single(report.Criteria, criterion => criterion.Name == "Hook zgodny z payoffem").Points > 3);
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

    private static SelectedTopic CreateAiNotesTopic()
    {
        return new SelectedTopic
        {
            Title = "Aplikacja AI, ktora robi notatki z nagran",
            SourceUrl = "offline://test",
            SourceText = """
            Praktyczna teza: aplikacja AI do nagran moze pomoc szybciej znalezc decyzje i zadania, ale wynik trzeba sprawdzic.
            Konkretne kroki: wgraj jedno nagranie lub transkrypcje, popros o liste decyzji i zadan, porownaj wynik z najwazniejszym fragmentem nagrania.
            Korzysc dla widza: widz dostaje sposob na szybki przeglad spotkania bez zaufania w ciemno.
            """,
            Brief = new ContentBrief
            {
                Audience = "osoby uczestniczace w spotkaniach",
                ViewerProblem = "trudno szybko znalezc decyzje i zadania po nagraniu",
                DesiredOutcome = "sprawdzic najwazniejsze decyzje i zadania",
                DurationSeconds = 25
            }
        };
    }

    private static SourceAnalysis CreateAiNotesAnalysis()
    {
        return new SourceAnalysis
        {
            MainThesis = "Aplikacja AI do nagran moze pomoc szybciej znalezc decyzje i zadania, ale wynik trzeba sprawdzic.",
            Facts =
            [
                new SourceFact { Id = "F1", Text = "Aplikacja AI do nagran moze pomoc szybciej znalezc decyzje i zadania.", Evidence = "aplikacja AI do nagran moze pomoc szybciej znalezc decyzje i zadania" },
                new SourceFact { Id = "F2", Text = "Wynik trzeba sprawdzic.", Evidence = "wynik trzeba sprawdzic" }
            ],
            Steps =
            [
                new SourceStep { Id = "S1", Text = "Wgraj jedno nagranie lub transkrypcje", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S2", Text = "Popros o liste decyzji i zadan", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S3", Text = "Porownaj wynik z najwazniejszym fragmentem nagrania", SourceFactIds = ["F2"] }
            ],
            MostUsefulFragment = "Wgraj jedno nagranie lub transkrypcje, popros o liste decyzji i zadan, porownaj wynik z najwazniejszym fragmentem nagrania."
        };
    }

    private static ShortScript CreateAiNotesScript()
    {
        return new ShortScript
        {
            Title = "Szybki przeglad decyzji po nagraniu",
            Hook = "Gubisz decyzje po nagraniu lub spotkaniu?",
            HookOnScreenText = "Gubisz decyzje",
            HookSearchPhrase = "person reviewing meeting transcript on laptop",
            Ending = "Wgraj jedno nagranie, popros o liste decyzji i porownaj wynik z fragmentem nagrania.",
            EndingOnScreenText = "Sprawdz decyzje",
            EndingSearchPhrase = "person checking meeting transcript and notes on laptop",
            Scenes =
            [
                CreateAiNotesScene("Wgraj jedno nagranie lub transkrypcje.", "Wgraj nagranie"),
                CreateAiNotesScene("Popros o liste decyzji i zadan.", "Lista decyzji"),
                CreateAiNotesScene("Porownaj wynik z najwazniejszym fragmentem nagrania.", "Porownaj wynik")
            ]
        };
    }

    private static ScriptScene CreateAiNotesScene(string voiceOver, string onScreenText)
    {
        return new ScriptScene
        {
            Role = "action",
            VoiceOver = voiceOver,
            SourceFactIds = ["F1"],
            NewInformation = voiceOver,
            OnScreenText = onScreenText,
            VisualDescription = "Osoba sprawdza transkrypcje spotkania na laptopie.",
            SearchPhrase = "person reviewing meeting transcript on laptop",
            SearchPhrases = ["person reviewing meeting transcript on laptop"],
            AvoidVisuals = "food delivery app, smartphone home screen",
            SceneGoal = "Pokazac krok z nagraniem."
        };
    }

    private static VisualPlan CreateAiNotesVisualPlan()
    {
        return new VisualPlan
        {
            GlobalAvoidVisuals = "food delivery app, smartphone home screen",
            Segments =
            [
                new VisualPlanSegment
                {
                    SegmentName = "scene_01",
                    VisibleContent = "Transkrypcja spotkania",
                    PersonAction = "Osoba przeglada transkrypcje",
                    PrimaryObject = "laptop",
                    ResultToShow = "Widoczna lista decyzji",
                    SearchPhrases = ["person reviewing meeting transcript on laptop"]
                }
            ]
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
        return CreateClips(4);
    }

    private static IReadOnlyList<DownloadedVideoClip> CreateClips(int count)
    {
        return Enumerable.Range(0, count)
            .Select(CreateClip)
            .ToList();
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
