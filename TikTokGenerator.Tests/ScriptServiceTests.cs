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
    public void ApplyVisualPlan_WhenAiNotesPlanUsesPhoneHomeScreen_ReplacesWithTranscriptQueries()
    {
        var topic = new SelectedTopic
        {
            Title = "Aplikacja AI, ktora robi notatki z nagran",
            SourceUrl = "offline://test",
            SourceText = """
            Praktyczna teza: aplikacja AI do nagran moze pomoc szybciej znalezc decyzje i zadania, ale wynik trzeba sprawdzic.
            Konkretne kroki: wgraj jedno nagranie lub transkrypcje, popros o liste decyzji i zadan, porownaj wynik z najwazniejszym fragmentem nagrania.
            """
        };
        var script = new ShortScript
        {
            Title = topic.Title,
            Hook = "Gubisz decyzje po nagraniu lub spotkaniu?",
            Ending = "Sprawdz decyzje i zadania z jednego nagrania.",
            Scenes =
            [
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Popros o liste decyzji i zadan.",
                    OnScreenText = "Lista decyzji",
                    SearchPhrase = "person organizing smartphone home screen apps"
                }
            ]
        };
        var visualPlan = new VisualPlan
        {
            GlobalAvoidVisuals = "random selfie",
            Segments =
            [
                new VisualPlanSegment
                {
                    SegmentName = "scene_01",
                    VisibleContent = "Lista decyzji",
                    PersonAction = "Osoba wpisuje polecenie w aplikacji",
                    PrimaryObject = "Ekran telefonu",
                    SearchPhrases =
                    [
                        "person organizing smartphone home screen apps",
                        "close up smartphone productivity app"
                    ]
                }
            ]
        };
        var service = new ScriptService(new HttpClient());

        var result = service.ApplyVisualPlan(script, visualPlan, topic);

        Assert.Contains(result.Scenes[0].SearchPhrases, phrase => phrase.Contains("meeting notes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Scenes[0].SearchPhrases, phrase => phrase.Contains("home screen apps", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("food delivery app", result.Scenes[0].AvoidVisuals, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void RepairScriptAfterReview_WhenHookPayoffIsRejected_UsesSourceStepsAsEnding()
    {
        var service = new ScriptService(new HttpClient());
        var topic = new SelectedTopic
        {
            Title = "Poranny rytual, ktory zmienia tempo dnia",
            SourceUrl = "offline://test",
            SourceText = "Praktyczna teza: jedna minuta planowania rano moze ograniczyc chaos na starcie dnia. Konkretne kroki: zapisz jeden priorytet, jedno male zadanie do zrobienia od razu oraz jedna rzecz, ktorej dzis swiadomie nie robisz."
        };
        var analysis = new SourceAnalysis
        {
            MainThesis = "jedna minuta planowania rano moze ograniczyc chaos na starcie dnia",
            Facts =
            [
                new SourceFact { Id = "F1", Text = "jedna minuta planowania rano moze ograniczyc chaos na starcie dnia", Evidence = "Praktyczna teza" }
            ],
            Steps =
            [
                new SourceStep { Id = "S1", Text = "zapisz jeden priorytet", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S2", Text = "zapisz jedno male zadanie do zrobienia od razu", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S3", Text = "zapisz jedna rzecz, ktorej dzis swiadomie nie robisz", SourceFactIds = ["F1"] }
            ],
            MostUsefulFragment = "jedna minuta planowania rano moze ograniczyc chaos na starcie dnia"
        };
        var script = new ShortScript
        {
            Title = "Jedna minuta, jedna czynnosc",
            Hook = "Jak szybko zaczac planowac rano?",
            HookOnScreenText = "Jak szybko zaczac planowac rano?",
            Ending = "Jedna minuta planowania rano ogranicza chaos.",
            EndingOnScreenText = "Jedna minuta planowania",
            Scenes =
            [
                new ScriptScene { Role = "problem", VoiceOver = "Zaczynasz dzien z chaosem?", SourceFactIds = ["F1"], NewInformation = "Zaczynasz dzien z chaosem?", OnScreenText = "Chaos", SearchPhrase = "person turning off morning alarm clock" },
                new ScriptScene { Role = "mechanism", VoiceOver = "Wystarczy jedna minuta.", SourceFactIds = ["F1"], NewInformation = "Wystarczy jedna minuta.", OnScreenText = "1 minuta", SearchPhrase = "person turning off morning alarm clock" },
                new ScriptScene { Role = "action", VoiceOver = "Zapisz jeden priorytet.", SourceFactIds = ["F1"], NewInformation = "Zapisz jeden priorytet.", OnScreenText = "Priorytet", SearchPhrase = "person turning off morning alarm clock" },
                new ScriptScene { Role = "proof", VoiceOver = "Zapisz jedno male zadanie.", SourceFactIds = ["F1"], NewInformation = "Zapisz jedno male zadanie.", OnScreenText = "Zadanie", SearchPhrase = "person turning off morning alarm clock" },
                new ScriptScene { Role = "payoff", VoiceOver = "Zapisz jedna rzecz, ktorej dzisiaj nie robisz.", SourceFactIds = ["F1"], NewInformation = "Zapisz jedna rzecz, ktorej dzisiaj nie robisz.", OnScreenText = "Nie robisz", SearchPhrase = "person turning off morning alarm clock" }
            ]
        };
        var review = new ContentReview
        {
            Approved = false,
            PromiseCheck = "Obietnica hooka nie jest spelniona.",
            Issues =
            [
                new ContentReviewIssue
                {
                    Severity = "warning",
                    Segment = "hook",
                    Code = "hookNotMet",
                    Message = "Hook nie jest spelniony przez payoff.",
                    SuggestedFix = "Zmien payoff na trzy kroki ze zrodla."
                }
            ]
        };

        var repaired = service.RepairScriptAfterReview(topic, analysis, script, review);

        Assert.Equal("Jak zaplanowac poranek w 1 minute?", repaired.Hook);
        Assert.Contains("jeden priorytet", repaired.Ending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jedno male zadanie", repaired.Ending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jedna rzecz", repaired.Ending, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, repaired.Scenes.Count);
        Assert.Contains("Krok ze zrodla", repaired.Scenes[0].NewInformation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("notebook", repaired.Scenes[0].SearchPhrase, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("notebook", repaired.Scenes[2].SearchPhrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepairScriptAfterReview_WhenDeskScriptAddsUnsupportedTime_RebuildsFromSourceSteps()
    {
        var service = new ScriptService(new HttpClient());
        var topic = new SelectedTopic
        {
            Title = "Szybki reset biurka po pracy",
            SourceUrl = "offline://test",
            SourceText = "Praktyczna teza: szybki reset biurka po pracy pomaga zamknac dzien i latwiej zaczac kolejny. Konkretne kroki: wyrzuc smieci, odloz rzeczy na miejsce, zostaw jedna kartke z pierwszym zadaniem na jutro. Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.",
            Brief = new ContentBrief
            {
                Audience = "osoby pracujace przy biurku",
                ViewerProblem = "balagan po zakonczonej pracy",
                DesiredOutcome = "zostawic biurko gotowe na kolejny start"
            }
        };
        var analysis = new SourceAnalysis
        {
            MainThesis = "szybki reset biurka po pracy pomaga zamknac dzien i latwiej zaczac kolejny",
            Facts =
            [
                new SourceFact { Id = "F1", Text = "wyrzuc smieci", Evidence = "Konkretne kroki: wyrzuc smieci, odloz rzeczy na miejsce, zostaw jedna kartke z pierwszym zadaniem na jutro." },
                new SourceFact { Id = "F2", Text = "odloz rzeczy na miejsce", Evidence = "Konkretne kroki: wyrzuc smieci, odloz rzeczy na miejsce, zostaw jedna kartke z pierwszym zadaniem na jutro." },
                new SourceFact { Id = "F3", Text = "zostaw jedna kartke z pierwszym zadaniem na jutro", Evidence = "Konkretne kroki: wyrzuc smieci, odloz rzeczy na miejsce, zostaw jedna kartke z pierwszym zadaniem na jutro." }
            ],
            Steps =
            [
                new SourceStep { Id = "S1", Text = "wyrzuc smieci", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S2", Text = "odloz rzeczy na miejsce", SourceFactIds = ["F2"] },
                new SourceStep { Id = "S3", Text = "zostaw jedna kartke z pierwszym zadaniem na jutro", SourceFactIds = ["F3"] }
            ],
            MostUsefulFragment = "szybki reset biurka po pracy pomaga zamknac dzien i latwiej zaczac kolejny"
        };
        var script = new ShortScript
        {
            Title = "Szybki reset biurka w 3 krokach",
            Hook = "Co robic po pracy, aby biurko nie bylo balaganem?",
            HookOnScreenText = "Biurko nie musi byc balaganem",
            Ending = "Szybki reset biurka po pracy pomaga zamknac dzien i latwiej zaczac kolejny.",
            EndingOnScreenText = "Zamknij dzien",
            Scenes =
            [
                new ScriptScene { Role = "mechanism", VoiceOver = "Aby to naprawic, wykonaj 3 proste kroki.", SourceFactIds = ["F1"], NewInformation = "3 kroki naprawiaja balagan.", OnScreenText = "3 kroki", SearchPhrase = "person organizing desk after work" },
                new ScriptScene { Role = "proof", VoiceOver = "To sprawdza sie w 2 minutach.", SourceFactIds = ["F1"], NewInformation = "Reset w 2 minuty.", OnScreenText = "2 minuty", SearchPhrase = "person organizing desk after work" }
            ]
        };
        var review = new ContentReview
        {
            Approved = false,
            Issues =
            [
                new ContentReviewIssue
                {
                    Severity = "error",
                    Segment = "scene_04",
                    Code = "newInformationMissing",
                    Message = "Scena dodaje czas, ktorego nie ma w zrodle.",
                    SuggestedFix = "Nie dodawaj 2 minut."
                }
            ]
        };

        var repaired = service.RepairScriptAfterReview(topic, analysis, script, review);
        var diagnostics = ShortDiagnosticsService.CreateScriptDiagnostics(topic, repaired);

        Assert.Equal("Szybki reset biurka po pracy", repaired.Title);
        Assert.Equal("Jak szybko zresetowac biurko po pracy?", repaired.Hook);
        Assert.Equal(3, repaired.Scenes.Count);
        Assert.DoesNotContain("2 minut", string.Join(" ", repaired.Scenes.Select(scene => scene.VoiceOver)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("3 proste kroki", string.Join(" ", repaired.Scenes.Select(scene => scene.VoiceOver)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(repaired.Scenes, scene => scene.VoiceOver.Contains("Wyrzuc smieci", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(repaired.Scenes, scene => scene.VoiceOver.Contains("Odloz rzeczy na miejsce", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(repaired.Scenes, scene => scene.VoiceOver.Contains("Zostaw jedna kartke", StringComparison.OrdinalIgnoreCase));
        Assert.False(diagnostics.Summary.HasUnsupportedClaims);
    }

    [Fact]
    public void RepairScriptAfterReview_WhenPhoneMinimalismIsRebuilt_KeepsActionVerbsInEnding()
    {
        var service = new ScriptService(new HttpClient());
        var topic = new SelectedTopic
        {
            Title = "Minimalizm w aplikacjach na telefonie",
            SourceUrl = "offline://test",
            SourceText = "Praktyczna teza: mniej ikon i mniej powiadomien pomaga szybciej znalezc to, co naprawde potrzebne. Konkretne kroki: usun z ekranu glownego aplikacje nieuzywane codziennie, wylacz niepilne powiadomienia, zostaw na pierwszym ekranie tylko najwazniejsze narzedzia.",
            Brief = new ContentBrief
            {
                Audience = "osoby korzystajace z telefonu na co dzien",
                ViewerProblem = "rozpraszajacy ekran telefonu i nadmiar powiadomien",
                DesiredOutcome = "uporzadkowac pierwszy ekran telefonu"
            }
        };
        var analysis = CreatePhoneMinimalismAnalysis();
        var script = new ShortScript
        {
            Title = "Minimalizm na pierwszym ekranie",
            Hook = "Zostaw tylko to, co naprawde potrzebujesz na pierwszym ekranie.",
            Ending = "Zostaw tylko to, co naprawde potrzebujesz na pierwszym ekranie.",
            Scenes =
            [
                new ScriptScene { Role = "proof", VoiceOver = "Zostaw na pierwszym ekranie tylko najwazniejsze narzedzia.", SourceFactIds = ["F1"], NewInformation = "zostaw najwazniejsze narzedzia", OnScreenText = "Najwazniejsze narzedzia", SearchPhrase = "close up smartphone productivity app" }
            ]
        };
        var review = new ContentReview
        {
            Approved = false,
            Issues =
            [
                new ContentReviewIssue
                {
                    Severity = "error",
                    Segment = "hook",
                    Code = "hookDoesNotMatchProof",
                    Message = "Hook nie pasuje do proof.",
                    SuggestedFix = "Zmien hook."
                }
            ]
        };

        var repaired = service.RepairScriptAfterReview(topic, analysis, script, review);
        var diagnostics = ShortDiagnosticsService.CreateScriptDiagnostics(topic, repaired);

        Assert.Equal("Telefon rozprasza po odblokowaniu?", repaired.Hook);
        Assert.Contains("usun z ekranu glownego", repaired.Ending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wylacz niepilne powiadomienia", repaired.Ending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("zostaw na pierwszym ekranie", repaired.Ending, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, repaired.Scenes.Count);
        Assert.False(diagnostics.Summary.HasUnsupportedClaims);
    }

    [Fact]
    public void RepairScriptAfterReview_WhenScannerTopicDriftsToNotifications_RebuildsScannerScript()
    {
        var service = new ScriptService(new HttpClient());
        var topic = CreateScannerTopic();
        var analysis = new SourceAnalysis
        {
            MainThesis = "telefon moze posluzyc do prostego skanu 3D obiektu",
            Facts =
            [
                new SourceFact
                {
                    Id = "F1",
                    Text = "telefon moze posluzyc do prostego skanu 3D obiektu",
                    Evidence = "Praktyczna teza: telefon moze posluzyc do prostego skanu 3D obiektu"
                }
            ],
            Steps = [],
            MostUsefulFragment = "telefon moze posluzyc do prostego skanu 3D obiektu"
        };
        var script = new ShortScript
        {
            Title = "Krok po kroku: Jak zredukowac powiadomienia na ekranie",
            Hook = "Dlaczego Twoj telefon jest jak skaner 3D?",
            HookOnScreenText = "Telefon jako skaner 3D",
            Ending = "Zredukuj powiadomienia do 1 kroku, aby uporzadkowac ekran.",
            EndingOnScreenText = "Zredukuj do 1 kroku",
            Scenes =
            [
                new ScriptScene
                {
                    Role = "action",
                    VoiceOver = "Otworz ustawienia powiadomien i wybierz zredukuj do 1 kroku.",
                    SourceFactIds = ["F1"],
                    NewInformation = "Ustawienia powiadomien.",
                    OnScreenText = "Zredukuj do 1 kroku",
                    SearchPhrase = "close up smartphone productivity app"
                },
                new ScriptScene
                {
                    Role = "proof",
                    VoiceOver = "Po zredukowaniu ekran zostanie uporzadkowany bez dodatkowych powiadomien.",
                    SourceFactIds = ["F1"],
                    NewInformation = "Ekran uporzadkowany po zredukowaniu powiadomien.",
                    OnScreenText = "Ekran uporzadkowany",
                    SearchPhrase = "close up smartphone productivity app"
                }
            ]
        };
        var review = new ContentReview
        {
            Approved = false,
            Issues =
            [
                new ContentReviewIssue
                {
                    Severity = "error",
                    Segment = "scene_01",
                    Code = "sourceMismatch",
                    Message = "Scena dotyczy powiadomien, a zrodlo dotyczy skanu 3D.",
                    SuggestedFix = "Wroc do tematu skanowania 3D."
                }
            ]
        };

        var repaired = service.RepairScriptAfterReview(topic, analysis, script, review);
        var diagnostics = ShortDiagnosticsService.CreateScriptDiagnostics(topic, repaired);
        var repairedText = string.Join(" ", repaired.Hook, repaired.Ending, string.Join(" ", repaired.Scenes.Select(scene => scene.VoiceOver)));

        Assert.Contains("skan 3D", repaired.Hook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obiekt", repairedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("model", repairedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(repaired.Scenes, scene => scene.SearchPhrase.Contains("scanning", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("powiadom", repairedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zredukuj", repairedText, StringComparison.OrdinalIgnoreCase);
        Assert.False(diagnostics.Summary.HasUnsupportedClaims);
    }

    [Fact]
    public void SanitizeReviewAgainstSource_WhenReviewerRejectsSourceStepsAsNoNewInformation_ApprovesReview()
    {
        var topic = new SelectedTopic
        {
            Title = "Minimalizm w aplikacjach na telefonie",
            SourceUrl = "offline://test",
            SourceText = "Konkretne kroki: usun z ekranu glownego aplikacje nieuzywane codziennie, wylacz niepilne powiadomienia, zostaw na pierwszym ekranie tylko najwazniejsze narzedzia.",
            Brief = new ContentBrief
            {
                ViewerProblem = "rozpraszajacy ekran telefonu i nadmiar powiadomien",
                DesiredOutcome = "uporzadkowac pierwszy ekran telefonu"
            }
        };
        var analysis = CreatePhoneMinimalismAnalysis();
        var script = new ShortScript
        {
            Title = "Minimalizm na pierwszym ekranie",
            Hook = "Telefon rozprasza po odblokowaniu?",
            Ending = "Usun z ekranu glownego aplikacje nieuzywane codziennie, wylacz niepilne powiadomienia i zostaw na pierwszym ekranie tylko najwazniejsze narzedzia.",
            Scenes =
            [
                new ScriptScene { Role = "action", VoiceOver = "Usun z ekranu glownego aplikacje nieuzywane codziennie.", SourceFactIds = ["F1"], NewInformation = "Pierwszy krok: aplikacje nieuzywane codziennie.", OnScreenText = "Usun aplikacje", SearchPhrase = "person organizing smartphone home screen apps" },
                new ScriptScene { Role = "action", VoiceOver = "Wylacz niepilne powiadomienia.", SourceFactIds = ["F1"], NewInformation = "Drugi krok: niepilne powiadomienia.", OnScreenText = "Wylacz powiadomienia", SearchPhrase = "person organizing smartphone home screen apps" },
                new ScriptScene { Role = "action", VoiceOver = "Zostaw na pierwszym ekranie tylko najwazniejsze narzedzia.", SourceFactIds = ["F1"], NewInformation = "Trzeci krok: najwazniejsze narzedzia.", OnScreenText = "Najwazniejsze narzedzia", SearchPhrase = "person organizing smartphone home screen apps" }
            ]
        };
        var review = new ContentReview
        {
            Approved = false,
            UsefulnessScore = 0,
            Issues =
            [
                new ContentReviewIssue
                {
                    Severity = "error",
                    Segment = "scenes",
                    Code = "noNewInformationInScene",
                    Message = "Scena 1 nie wnosi nowej informacji, bo krok jest juz zawarty w tezie zrodla.",
                    SuggestedFix = "Usun scene, poniewaz krok jest juz opisany w zrodle."
                },
                new ContentReviewIssue
                {
                    Severity = "error",
                    Segment = "hook",
                    Code = "hookDoesNotMatchPayoff",
                    Message = "Hook nie spelnia obietnicy payoffu.",
                    SuggestedFix = "Zmien hook."
                }
            ]
        };

        ScriptService.SanitizeReviewAgainstSource(topic, analysis, script, review);

        Assert.True(review.Approved);
        Assert.DoesNotContain(review.Issues, issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(review.Issues, issue => issue.Code == "noNewInformationInScene" && issue.Severity == "info");
        Assert.Contains(review.Issues, issue => issue.Code == "hookDoesNotMatchPayoff" && issue.Severity == "warning");
        Assert.Equal(7, review.UsefulnessScore);
    }

    [Fact]
    public void SanitizeReviewAgainstSource_WhenReviewerDemandsExtraExamplesForSourceSteps_ApprovesReview()
    {
        var topic = new SelectedTopic
        {
            Title = "Szybki reset biurka po pracy",
            SourceUrl = "offline://test",
            SourceText = "Praktyczna teza: szybki reset biurka po pracy pomaga zamknac dzien i latwiej zaczac kolejny. Konkretne kroki: wyrzuc smieci, odloz rzeczy na miejsce, zostaw jedna kartke z pierwszym zadaniem na jutro.",
            Brief = new ContentBrief
            {
                ViewerProblem = "balagan po zakonczonej pracy",
                DesiredOutcome = "zostawic biurko gotowe na kolejny start"
            }
        };
        var analysis = new SourceAnalysis
        {
            MainThesis = "szybki reset biurka po pracy pomaga zamknac dzien i latwiej zaczac kolejny",
            Facts =
            [
                new SourceFact { Id = "F1", Text = "szybki reset biurka po pracy pomaga zamknac dzien i latwiej zaczac kolejny", Evidence = "Praktyczna teza" }
            ],
            Steps =
            [
                new SourceStep { Id = "S1", Text = "wyrzuc smieci", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S2", Text = "odloz rzeczy na miejsce", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S3", Text = "zostaw jedna kartke z pierwszym zadaniem na jutro", SourceFactIds = ["F1"] }
            ],
            MostUsefulFragment = "szybki reset biurka po pracy pomaga zamknac dzien i latwiej zaczac kolejny"
        };
        var script = new ShortScript
        {
            Title = "Szybki reset biurka po pracy",
            Hook = "Jak szybko zresetowac biurko po pracy?",
            Ending = "Wyrzuc smieci, odloz rzeczy na miejsce i zostaw jedna kartke z pierwszym zadaniem na jutro.",
            Scenes =
            [
                new ScriptScene { Role = "action", VoiceOver = "Wyrzuc smieci.", SourceFactIds = ["F1"], NewInformation = "Krok ze zrodla: wyrzuc smieci.", OnScreenText = "Wyrzuc smieci", SearchPhrase = "person organizing desk after work" },
                new ScriptScene { Role = "action", VoiceOver = "Odloz rzeczy na miejsce.", SourceFactIds = ["F1"], NewInformation = "Krok ze zrodla: odloz rzeczy na miejsce.", OnScreenText = "Odloz rzeczy", SearchPhrase = "person organizing desk after work" },
                new ScriptScene { Role = "action", VoiceOver = "Zostaw jedna kartke z pierwszym zadaniem na jutro.", SourceFactIds = ["F1"], NewInformation = "Krok ze zrodla: zostaw jedna kartke z pierwszym zadaniem na jutro.", OnScreenText = "Kartka z zadaniem", SearchPhrase = "person writing task on paper at desk" }
            ]
        };
        var review = new ContentReview
        {
            Approved = false,
            UsefulnessScore = 6,
            Issues =
            [
                new ContentReviewIssue
                {
                    Severity = "error",
                    Segment = "scene_01",
                    Code = "noNewInformation",
                    Message = "Scena 01 nie wnosi nowej informacji, bo nie dodaje kontekstu dla wyrzuc smieci.",
                    SuggestedFix = "Dodaj przyklad papierkow z poprzedniego dnia."
                },
                new ContentReviewIssue
                {
                    Severity = "error",
                    Segment = "scene_02",
                    Code = "noNewInformation",
                    Message = "Scena 02 nie wnosi nowej informacji, bo nie okresla miejsca.",
                    SuggestedFix = "Dodaj przyklad dokumentow na szafke."
                },
                new ContentReviewIssue
                {
                    Severity = "error",
                    Segment = "scene_03",
                    Code = "noNewInformation",
                    Message = "Scena 03 nie wnosi nowej informacji, bo nie podaje przykladu zadania.",
                    SuggestedFix = "Dodaj przyklad Zakupic mleko."
                }
            ]
        };

        ScriptService.SanitizeReviewAgainstSource(topic, analysis, script, review);

        Assert.True(review.Approved);
        Assert.DoesNotContain(review.Issues, issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        Assert.All(review.Issues, issue => Assert.Equal("info", issue.Severity));
    }

    private static SelectedTopic CreateScannerTopic()
    {
        return new SelectedTopic
        {
            Title = "Ciekawostka technologiczna: telefon jako skaner 3D",
            SourceUrl = "offline://test",
            SourceText = """
            Temat roboczy: Ciekawostka technologiczna: telefon jako skaner 3D
            Kategoria: Technologia
            Praktyczna teza: telefon moze posluzyc do prostego skanu 3D obiektu, gdy aplikacja sklada zdjecia lub nagranie z kilku stron w model.
            Konkretne kroki: wybierz maly nieruchomy obiekt z wyrazna faktura, obejdz go telefonem z kilku stron, sprawdz w aplikacji czy model nie ma brakujacych fragmentow.
            Korzysc dla widza: widz rozumie, ze skan 3D telefonem zaczyna sie od stabilnego obiektu, dobrego swiatla i obejscia obiektu kamera.
            Ograniczenia: nie obiecuj dokladnosci technicznej ani profesjonalnego skanu.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """,
            Brief = new ContentBrief
            {
                Audience = "osoby ciekawe prostych zastosowan telefonu",
                ViewerProblem = "brak jasnosci, jak telefon moze zamienic obiekt w model 3D",
                DesiredOutcome = "sprawdzic prosty skan malego obiektu telefonem"
            }
        };
    }

    private static SourceAnalysis CreatePhoneMinimalismAnalysis()
    {
        return new SourceAnalysis
        {
            MainThesis = "mniej ikon i mniej powiadomien pomaga szybciej znalezc to, co naprawde potrzebne",
            Facts =
            [
                new SourceFact { Id = "F1", Text = "mniej ikon i mniej powiadomien pomaga szybciej znalezc to, co naprawde potrzebne", Evidence = "Praktyczna teza" }
            ],
            Steps =
            [
                new SourceStep { Id = "S1", Text = "usun z ekranu glownego aplikacje nieuzywane codziennie", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S2", Text = "wylacz niepilne powiadomienia", SourceFactIds = ["F1"] },
                new SourceStep { Id = "S3", Text = "zostaw na pierwszym ekranie tylko najwazniejsze narzedzia", SourceFactIds = ["F1"] }
            ],
            MostUsefulFragment = "mniej ikon i mniej powiadomien pomaga szybciej znalezc to, co naprawde potrzebne"
        };
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
