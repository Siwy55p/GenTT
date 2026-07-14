using System.Text.Json;
using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class ShortGeneratorEndToEndTests
{
    [Fact]
    public async Task GenerateAsync_WhenPipelineDataIsValid_CreatesShortManifestAndDiagnostics()
    {
        var modelClient = new DeterministicModelClient();
        var videoService = new FakeVideoService();
        var generator = CreateGenerator(modelClient, new FakeStockVideoService(), videoService);
        var progressEvents = new List<ShortGenerationProgress>();
        var progress = new Progress<ShortGenerationProgress>(progressEvents.Add);

        var outputPath = await generator.GenerateAsync(
            CreateScannerTopic(),
            CreateOptions(),
            progress);

        var projectDirectory = Path.GetDirectoryName(outputPath)!;
        try
        {
            Assert.True(File.Exists(outputPath));
            Assert.True(videoService.WasCalled);
            Assert.Contains(progressEvents, item => item.Percent == 100);
            Assert.Contains("source-analysis", modelClient.StageCalls);
            Assert.Contains("concept-selection", modelClient.StageCalls);
            Assert.Contains("script", modelClient.StageCalls);
            Assert.Contains("content-review", modelClient.StageCalls);
            Assert.Contains("visual-plan", modelClient.StageCalls);

            var scriptPath = Path.Combine(projectDirectory, "script.json");
            var projectPath = Path.Combine(projectDirectory, "project.json");
            var diagnosticsPath = Path.Combine(projectDirectory, "debug", "short-diagnostics.json");
            var qualityGatePath = Path.Combine(projectDirectory, "debug", "quality-gate.json");

            Assert.True(File.Exists(scriptPath));
            Assert.True(File.Exists(projectPath));
            Assert.True(File.Exists(diagnosticsPath));
            Assert.True(File.Exists(qualityGatePath));

            using var scriptDocument = JsonDocument.Parse(await File.ReadAllTextAsync(scriptPath));
            Assert.Equal(3, scriptDocument.RootElement.GetProperty("scenes").GetArrayLength());
            Assert.All(scriptDocument.RootElement.GetProperty("scenes").EnumerateArray(), scene =>
            {
                Assert.False(string.IsNullOrWhiteSpace(scene.GetProperty("voiceOver").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(scene.GetProperty("onScreenText").GetString()));
            });

            using var qualityGateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(qualityGatePath));
            Assert.True(qualityGateDocument.RootElement.GetProperty("passed").GetBoolean());

            using var diagnosticsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(diagnosticsPath));
            Assert.Equal(0, diagnosticsDocument.RootElement.GetProperty("summary").GetProperty("errorCount").GetInt32());
        }
        finally
        {
            DeleteProjectDirectory(projectDirectory);
        }
    }

    [Fact]
    public async Task GenerateAsync_WhenClipIsMissing_StopsBeforeRenderingShort()
    {
        var videoService = new FakeVideoService();
        var generator = CreateGenerator(
            new DeterministicModelClient(),
            new FakeStockVideoService(skipSegmentIndex: 2),
            videoService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync(CreateScannerTopic(), CreateOptions()));

        Assert.Contains("Bramka jakosci zatrzymala render", exception.Message);
        Assert.Contains("clip_errors", exception.Message);
        Assert.False(videoService.WasCalled);

        var projectDirectory = ExtractDebugProjectDirectory(exception.Message);
        try
        {
            var clipAnalysisPath = Path.Combine(projectDirectory, "debug", "clip-analysis.json");
            Assert.True(File.Exists(clipAnalysisPath));
            using var clipAnalysisDocument = JsonDocument.Parse(await File.ReadAllTextAsync(clipAnalysisPath));
            Assert.Contains(
                clipAnalysisDocument.RootElement.GetProperty("issues").EnumerateArray(),
                issue => issue.GetProperty("code").GetString() == "missing_clip");
        }
        finally
        {
            DeleteProjectDirectory(projectDirectory);
        }
    }

    private static ShortGenerator CreateGenerator(
        IModelClient modelClient,
        IStockVideoService stockVideoService,
        IVideoService videoService)
    {
        return new ShortGenerator(
            new ScriptService(new HttpClient(), modelClient),
            new FakeVoiceService(),
            stockVideoService,
            videoService);
    }

    private static ShortGeneratorOptions CreateOptions()
    {
        return new ShortGeneratorOptions
        {
            ModelProvider = "openai",
            OpenAIApiKey = "test-key",
            PexelsApiKey = "test-pexels-key"
        };
    }

    private static SelectedTopic CreateScannerTopic()
    {
        return new SelectedTopic
        {
            Title = "Telefon jako skaner 3D",
            SourceUrl = "offline://test",
            SourceText = """
            Praktyczna teza: telefon moze posluzyc do prostego skanu 3D obiektu przez fotogrametrie.
            Konkretne kroki: wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke, obejdz go telefonem z kilku stron, sprawdz w aplikacji czy model nie ma brakujacych fragmentow.
            Korzysc dla widza: widz rozumie, ze skan 3D telefonem zaczyna sie od stabilnego obiektu, dobrego swiatla i obejscia obiektu kamera.
            Ograniczenia: nie obiecuj dokladnosci technicznej ani profesjonalnego skanu.
            """,
            Brief = new ContentBrief
            {
                Audience = "osoby ciekawe prostych zastosowan telefonu",
                ViewerProblem = "brak jasnosci, jak telefon moze zamienic obiekt w model 3D",
                DesiredOutcome = "sprawdzic prosty skan malego obiektu telefonem",
                DurationSeconds = 25
            }
        };
    }

    private static void DeleteProjectDirectory(string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return;
        }

        var outputRoot = Path.Combine(AppContext.BaseDirectory, "Output");
        var fullProjectDirectory = Path.GetFullPath(projectDirectory);
        var fullOutputRoot = Path.GetFullPath(outputRoot);
        if (!fullProjectDirectory.StartsWith(fullOutputRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.Delete(fullProjectDirectory, recursive: true);
    }

    private static string ExtractDebugProjectDirectory(string message)
    {
        var marker = "Debug log:";
        var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return string.Empty;
        }

        var logPath = message[(markerIndex + marker.Length)..]
            .Trim()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();
        return string.IsNullOrWhiteSpace(logPath)
            ? string.Empty
            : Path.GetDirectoryName(Path.GetDirectoryName(logPath) ?? string.Empty) ?? string.Empty;
    }

    private sealed class DeterministicModelClient : IModelClient
    {
        public List<string> StageCalls { get; } = [];

        public Task<ModelJsonResponse> GenerateJsonAsync(
            ModelJsonRequest request,
            GenerationDebugLogger? logger,
            CancellationToken cancellationToken)
        {
            StageCalls.Add(request.StageName);
            return Task.FromResult(new ModelJsonResponse
            {
                Provider = "test",
                Model = "deterministic",
                StageName = request.StageName,
                SchemaName = request.StageName,
                StrictSchema = true,
                StatusCode = 200,
                ElapsedMilliseconds = 1,
                RawText = CreateResponse(request.StageName)
            });
        }

        private static string CreateResponse(string stageName)
        {
            return stageName switch
            {
                "source-analysis" => """
                    {
                      "mainThesis": "telefon moze posluzyc do prostego skanu 3D obiektu przez fotogrametrie",
                      "facts": [
                        {
                          "id": "F1",
                          "text": "telefon moze posluzyc do prostego skanu 3D obiektu przez fotogrametrie",
                          "evidence": "telefon moze posluzyc do prostego skanu 3D obiektu przez fotogrametrie"
                        },
                        {
                          "id": "F2",
                          "text": "wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke",
                          "evidence": "wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke"
                        },
                        {
                          "id": "F3",
                          "text": "sprawdz w aplikacji czy model nie ma brakujacych fragmentow",
                          "evidence": "sprawdz w aplikacji czy model nie ma brakujacych fragmentow"
                        }
                      ],
                      "steps": [
                        {
                          "id": "S1",
                          "text": "wybierz maly nieruchomy obiekt z wyrazna faktura",
                          "sourceFactIds": ["F2"]
                        },
                        {
                          "id": "S2",
                          "text": "obejdz go telefonem z kilku stron",
                          "sourceFactIds": ["F1"]
                        },
                        {
                          "id": "S3",
                          "text": "sprawdz w aplikacji czy model nie ma brakujacych fragmentow",
                          "sourceFactIds": ["F3"]
                        }
                      ],
                      "examples": ["kamien albo figurka"],
                      "limitations": ["nie obiecuj dokladnosci technicznej ani profesjonalnego skanu"],
                      "riskyClaims": [],
                      "mostUsefulFragment": "wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke, obejdz go telefonem z kilku stron, sprawdz w aplikacji czy model nie ma brakujacych fragmentow"
                    }
                    """,
                "concept-selection" => """
                    {
                      "directions": [
                        {
                          "id": "D1",
                          "name": "Prosty skan obiektu",
                          "structure": "problem-action-proof",
                          "hookAngle": "telefon jako skaner 3D",
                          "payoff": "sprawdzenie modelu w aplikacji",
                          "scores": {
                            "usefulness": 10,
                            "specificity": 10,
                            "freshness": 8,
                            "sourceAlignment": 10,
                            "visualPotential": 10,
                            "hookStrength": 8
                          }
                        },
                        {
                          "id": "D2",
                          "name": "Dobor obiektu",
                          "structure": "problem-action-proof",
                          "hookAngle": "obiekt z faktura",
                          "payoff": "mniej brakow w modelu",
                          "scores": {
                            "usefulness": 8,
                            "specificity": 8,
                            "freshness": 7,
                            "sourceAlignment": 8,
                            "visualPotential": 8,
                            "hookStrength": 7
                          }
                        },
                        {
                          "id": "D3",
                          "name": "Kontrola efektu",
                          "structure": "problem-action-proof",
                          "hookAngle": "sprawdz model",
                          "payoff": "model bez brakujacych fragmentow",
                          "scores": {
                            "usefulness": 8,
                            "specificity": 8,
                            "freshness": 7,
                            "sourceAlignment": 8,
                            "visualPotential": 8,
                            "hookStrength": 7
                          }
                        }
                      ],
                      "selectedDirectionId": "D1",
                      "selectedReason": "Najlepiej pokazuje konkretny przyklad i rezultat."
                    }
                    """,
                "script" => """
                    {
                      "title": "Telefon jako skaner 3D",
                      "hook": "Telefon moze posluzyc do prostego skanu 3D obiektu.",
                      "hookOnScreenText": "Skan 3D telefonem",
                      "scenes": [
                        {
                          "role": "action",
                          "voiceOver": "Wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke.",
                          "sourceFactIds": ["F2"],
                          "newInformation": "Wybierz maly nieruchomy obiekt z wyrazna faktura, na przyklad kamien albo figurke.",
                          "onScreenEmphasis": "Obiekt z faktura",
                          "onScreenText": "Obiekt z faktura",
                          "estimatedWords": 10,
                          "sceneGoal": "Pokazac konkretny obiekt do skanu."
                        },
                        {
                          "role": "action",
                          "voiceOver": "Obejdz go telefonem z kilku stron.",
                          "sourceFactIds": ["F1"],
                          "newInformation": "Obejdz go telefonem z kilku stron.",
                          "onScreenEmphasis": "Kilka stron",
                          "onScreenText": "Obejdz obiekt",
                          "estimatedWords": 6,
                          "sceneGoal": "Pokazac ruch telefonu wokol obiektu."
                        },
                        {
                          "role": "proof",
                          "voiceOver": "Sprawdz w aplikacji czy model nie ma brakujacych fragmentow.",
                          "sourceFactIds": ["F3"],
                          "newInformation": "Sprawdz w aplikacji czy model nie ma brakujacych fragmentow.",
                          "onScreenEmphasis": "Sprawdz model",
                          "onScreenText": "Sprawdz model",
                          "estimatedWords": 9,
                          "sceneGoal": "Pokazac widoczny rezultat na ekranie."
                        }
                      ],
                      "ending": "Telefon moze posluzyc do prostego skanu 3D obiektu, gdy sprawdzisz model w aplikacji.",
                      "endingOnScreenText": "Sprawdz model"
                    }
                    """,
                "content-review" => """
                    {
                      "issues": [],
                      "repetitionCheck": "Sceny nie powtarzaja tej samej informacji.",
                      "obviousAdviceCheck": "Sceny sa konkretne i wykonalne.",
                      "sourceComparison": "Scenariusz trzyma sie materialu zrodlowego.",
                      "promiseCheck": "Hook i payoff sa spojne.",
                      "feasibilityCheck": "Kroki sa mozliwe do wykonania.",
                      "audienceValueCheck": "Widz dostaje konkretny przyklad i widoczny rezultat.",
                      "suggestedFixes": [],
                      "usefulnessScore": 10,
                      "approved": true
                    }
                    """,
                "visual-plan" => """
                    {
                      "globalAvoidVisuals": "random selfie, unrelated phone app, social media scrolling",
                      "segments": [
                        {
                          "segmentName": "hook",
                          "visibleContent": "Telefon i maly obiekt na stole",
                          "personAction": "Osoba przygotowuje telefon do skanu",
                          "primaryObject": "smartphone photogrammetry object scan",
                          "shotType": "close up",
                          "movementStart": "telefon nad stolem",
                          "movementEnd": "telefon kieruje sie na obiekt",
                          "resultToShow": "widoczny obiekt do skanu",
                          "avoidVisuals": "random selfie",
                          "searchPhrases": ["smartphone photogrammetry object scan", "phone scanning small object"]
                        },
                        {
                          "segmentName": "scene_01",
                          "visibleContent": "Kamien albo figurka na stole jako maly obiekt z faktura",
                          "personAction": "Osoba wybiera obiekt do skanu",
                          "primaryObject": "kamien albo figurka",
                          "shotType": "close up",
                          "movementStart": "obiekt lezy na stole",
                          "movementEnd": "telefon ustawia kadr",
                          "resultToShow": "widoczny przyklad obiektu z faktura",
                          "avoidVisuals": "flat blank wall",
                          "searchPhrases": ["small textured object smartphone scan", "smartphone photogrammetry object scan"]
                        },
                        {
                          "segmentName": "scene_02",
                          "visibleContent": "Telefon obchodzi obiekt z kilku stron",
                          "personAction": "Osoba filmuje obiekt telefonem",
                          "primaryObject": "telefon i obiekt",
                          "shotType": "moving close up",
                          "movementStart": "telefon z jednej strony obiektu",
                          "movementEnd": "telefon po drugiej stronie obiektu",
                          "resultToShow": "ujecia z kilku stron",
                          "avoidVisuals": "random selfie",
                          "searchPhrases": ["person filming small object with smartphone", "smartphone object scan"]
                        },
                        {
                          "segmentName": "scene_03",
                          "visibleContent": "Ekran aplikacji pokazuje podglad modelu 3D",
                          "personAction": "Osoba sprawdza model w aplikacji",
                          "primaryObject": "model 3D w aplikacji",
                          "shotType": "screen close up",
                          "movementStart": "podglad modelu",
                          "movementEnd": "widoczny brakujacy fragment albo kompletna bryla",
                          "resultToShow": "na ekranie widac model 3D i kontrole brakujacych fragmentow",
                          "avoidVisuals": "phone gallery",
                          "searchPhrases": ["smartphone photogrammetry 3d model preview", "phone 3d scan model app screen"]
                        },
                        {
                          "segmentName": "ending",
                          "visibleContent": "Ekran aplikacji z modelem 3D",
                          "personAction": "Osoba obraca podglad modelu",
                          "primaryObject": "model 3D",
                          "shotType": "screen close up",
                          "movementStart": "model na ekranie",
                          "movementEnd": "sprawdzenie fragmentow",
                          "resultToShow": "widoczny rezultat skanu",
                          "avoidVisuals": "random selfie",
                          "searchPhrases": ["smartphone photogrammetry 3d model preview", "phone 3d scan model app screen"]
                        }
                      ]
                    }
                    """,
                _ => throw new InvalidOperationException($"Unexpected stage: {stageName}.")
            };
        }
    }

    private sealed class FakeVoiceService : IVoiceService
    {
        public async Task<IReadOnlyList<VoiceSegment>> GenerateVoiceAsync(
            ShortScript script,
            string outputDirectory,
            ShortGeneratorOptions options,
            GenerationDebugLogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputDirectory);
            var segments = new List<VoiceSegment>
            {
                await CreateSegmentAsync(outputDirectory, 0, "hook", script.Hook, script.HookOnScreenText, script.HookSearchPhrase, cancellationToken)
            };

            foreach (var scene in script.Scenes.Select((value, index) => new { value, index }))
            {
                segments.Add(await CreateSegmentAsync(
                    outputDirectory,
                    scene.index + 1,
                    $"scene_{scene.index + 1:00}",
                    scene.value.VoiceOver,
                    scene.value.OnScreenText,
                    scene.value.SearchPhrase,
                    cancellationToken,
                    scene.value));
            }

            segments.Add(await CreateSegmentAsync(
                outputDirectory,
                segments.Count,
                "ending",
                script.Ending,
                script.EndingOnScreenText,
                script.EndingSearchPhrase,
                cancellationToken));

            return segments;
        }

        private static async Task<VoiceSegment> CreateSegmentAsync(
            string outputDirectory,
            int index,
            string name,
            string text,
            string onScreenText,
            string searchPhrase,
            CancellationToken cancellationToken,
            ScriptScene? scene = null)
        {
            var audioPath = Path.Combine(outputDirectory, $"{index:00}_{name}.wav");
            await File.WriteAllTextAsync(audioPath, "fake wav", cancellationToken);
            var resolvedSearchPhrase = string.IsNullOrWhiteSpace(searchPhrase)
                ? "smartphone photogrammetry object scan"
                : searchPhrase;

            return new VoiceSegment
            {
                Index = index,
                Name = name,
                Role = scene?.Role ?? name,
                Text = text,
                SourceFactIds = scene?.SourceFactIds ?? [],
                NewInformation = scene?.NewInformation ?? string.Empty,
                OnScreenText = onScreenText,
                VisualDescription = scene?.VisualDescription ?? "Telefon pokazuje prosty skan 3D obiektu.",
                SearchPhrase = resolvedSearchPhrase,
                SearchPhrases = scene?.SearchPhrases.Count > 0 ? scene.SearchPhrases : [resolvedSearchPhrase],
                AvoidVisuals = scene?.AvoidVisuals ?? "random selfie",
                AudioPath = audioPath,
                Duration = TimeSpan.FromSeconds(3)
            };
        }
    }

    private sealed class FakeStockVideoService : IStockVideoService
    {
        private readonly int? _skipSegmentIndex;

        public FakeStockVideoService(int? skipSegmentIndex = null)
        {
            _skipSegmentIndex = skipSegmentIndex;
        }

        public async Task<IReadOnlyList<DownloadedVideoClip>> DownloadVideosAsync(
            IReadOnlyList<VoiceSegment> segments,
            string outputDirectory,
            ShortGeneratorOptions options,
            IProgress<ShortGenerationProgress>? progress = null,
            GenerationDebugLogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputDirectory);
            var clips = new List<DownloadedVideoClip>();
            foreach (var segment in segments.Where(segment => segment.Index != _skipSegmentIndex))
            {
                var filePath = Path.Combine(outputDirectory, $"{segment.Index:00}_{segment.Name}.mp4");
                await File.WriteAllTextAsync(filePath, "fake mp4", cancellationToken);
                clips.Add(new DownloadedVideoClip
                {
                    SegmentIndex = segment.Index,
                    SearchPhrase = segment.SearchPhrase,
                    SearchPhrases = segment.SearchPhrases,
                    AvoidVisuals = segment.AvoidVisuals,
                    VisualDescription = segment.VisualDescription,
                    FilePath = filePath,
                    PexelsUrl = $"https://www.pexels.com/video/test-{segment.Index}/",
                    ThumbnailUrl = $"https://images.pexels.com/test-{segment.Index}.jpg",
                    PexelsRank = 1,
                    CandidateCount = 3,
                    ContentScore = 98,
                    SelectionReason = "deterministic test clip",
                    AuthorName = "Pexels Test",
                    AuthorUrl = "https://www.pexels.com"
                });
            }

            return clips;
        }
    }

    private sealed class FakeVideoService : IVideoService
    {
        public bool WasCalled { get; private set; }

        public async Task<string> RenderVideoAsync(
            ShortScript script,
            IReadOnlyList<VoiceSegment> voiceSegments,
            IReadOnlyList<DownloadedVideoClip> clips,
            string projectDirectory,
            IProgress<ShortGenerationProgress>? progress = null,
            GenerationDebugLogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            Directory.CreateDirectory(projectDirectory);
            progress?.Report(new ShortGenerationProgress(100, "Gotowe"));
            var outputPath = Path.Combine(projectDirectory, "short.mp4");
            await File.WriteAllTextAsync(outputPath, "fake rendered short", cancellationToken);
            return outputPath;
        }
    }
}
