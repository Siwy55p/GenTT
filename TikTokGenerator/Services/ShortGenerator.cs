using System.Text.Json;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class ShortGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ScriptService _scriptService;
    private readonly IVoiceService _voiceService;
    private readonly IStockVideoService _stockVideoService;
    private readonly IVideoService _videoService;

    public ShortGenerator(
        ScriptService scriptService,
        IVoiceService voiceService,
        IStockVideoService stockVideoService,
        IVideoService videoService)
    {
        _scriptService = scriptService;
        _voiceService = voiceService;
        _stockVideoService = stockVideoService;
        _videoService = videoService;
    }

    public async Task<string> GenerateAsync(
        SelectedTopic topic,
        ShortGeneratorOptions options,
        IProgress<ShortGenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var projectDirectory = CreateProjectDirectory(topic.Title);
        Directory.CreateDirectory(projectDirectory);
        var logger = new GenerationDebugLogger(projectDirectory);

        try
        {
            logger.Info($"Started generation. ProjectDirectory={projectDirectory}");
            await logger.SaveJsonAsync("runtime-options.json", CreateRuntimeOptionsLog(options), cancellationToken);
            await logger.SaveJsonAsync("topic.json", topic, cancellationToken);

            progress?.Report(new ShortGenerationProgress(5, "Analizuje material zrodlowy"));
            var sourceAnalysis = await _scriptService.AnalyzeSourceAsync(topic, options, logger, cancellationToken);
            await logger.SaveJsonAsync("source-analysis.json", sourceAnalysis, cancellationToken);

            progress?.Report(new ShortGenerationProgress(12, "Wybieram najlepsza koncepcje"));
            var conceptSelection = await _scriptService.GenerateConceptsAsync(topic, sourceAnalysis, options, logger, cancellationToken);
            await logger.SaveJsonAsync("concept-selection.json", conceptSelection, cancellationToken);

            progress?.Report(new ShortGenerationProgress(20, "Tworze scenariusz w modelu AI"));
            var script = await _scriptService.GenerateScriptAsync(
                topic,
                sourceAnalysis,
                conceptSelection.SelectedDirection,
                options,
                logger,
                cancellationToken);
            await SaveJsonAsync(Path.Combine(projectDirectory, "script.json"), script, cancellationToken);
            await logger.SaveJsonAsync("script-normalized.json", script, cancellationToken);

            progress?.Report(new ShortGenerationProgress(28, "Recenzuje merytoryke"));
            var contentReview = await _scriptService.ReviewScriptAsync(topic, sourceAnalysis, script, options, logger, cancellationToken);
            await logger.SaveJsonAsync("content-review-initial.json", contentReview, cancellationToken);
            var preRepairDiagnostics = ShortDiagnosticsService.CreateScriptDiagnostics(topic, script);
            if (preRepairDiagnostics.Summary.HasUnsupportedClaims)
            {
                await logger.SaveJsonAsync("script-analysis-before-review-repair.json", preRepairDiagnostics, cancellationToken);
                logger.Warning("Local script diagnostics found unsupported claims after content review. Repairing even though the reviewer may have approved the script.");
            }

            if (ShouldRepairAfterReview(contentReview) || preRepairDiagnostics.Summary.HasUnsupportedClaims)
            {
                progress?.Report(new ShortGenerationProgress(31, "Poprawiam scenariusz po recenzji"));
                script = _scriptService.RepairScriptAfterReview(topic, sourceAnalysis, script, contentReview, logger);
                await SaveJsonAsync(Path.Combine(projectDirectory, "script.json"), script, cancellationToken);
                await logger.SaveJsonAsync("script-after-content-review-repair.json", script, cancellationToken);

                contentReview = await _scriptService.ReviewScriptAsync(topic, sourceAnalysis, script, options, logger, cancellationToken);
                await logger.SaveJsonAsync("content-review-after-repair.json", contentReview, cancellationToken);
            }

            await logger.SaveJsonAsync("content-review.json", contentReview, cancellationToken);
            if (contentReview.HasCriticalErrors || !contentReview.Approved)
            {
                throw new InvalidOperationException("Recenzent merytoryczny nie zatwierdzil scenariusza po probie poprawy. Render zostal zatrzymany. Szczegoly sa w debug/content-review.json i debug/script-after-content-review-repair.json.");
            }

            var wordBudget = CalculateWordBudget(topic.Brief.DurationSeconds);
            script = _scriptService.ShortenToWordBudget(script, wordBudget, logger);
            await logger.SaveJsonAsync("script-word-budget.json", script, cancellationToken);

            progress?.Report(new ShortGenerationProgress(35, "Tworze lektora w Piper"));
            var audioDirectory = Path.Combine(projectDirectory, "audio");
            var voiceSegments = await _voiceService.GenerateVoiceAsync(script, audioDirectory, options, logger, cancellationToken);
            await logger.SaveJsonAsync("voice-segments.json", voiceSegments, cancellationToken);
            var voiceDiagnostics = ShortDiagnosticsService.CreateVoiceDiagnostics(topic, script, voiceSegments);
            await logger.SaveJsonAsync("voice-analysis.json", voiceDiagnostics, cancellationToken);
            ShortDiagnosticsService.LogSummary(logger, "Voice", voiceDiagnostics);

            if (voiceDiagnostics.Summary.EstimatedDurationSeconds > topic.Brief.DurationSeconds)
            {
                progress?.Report(new ShortGenerationProgress(42, "Skracam po pomiarze TTS"));
                var measuredBudget = CalculateMeasuredWordBudget(
                    script,
                    topic.Brief.DurationSeconds,
                    voiceDiagnostics.Summary.EstimatedDurationSeconds);
                script = _scriptService.ShortenToWordBudget(script, measuredBudget, logger);
                await logger.SaveJsonAsync("script-after-tts-shorten.json", script, cancellationToken);

                var shortenedAudioDirectory = Path.Combine(projectDirectory, "audio-shortened");
                voiceSegments = await _voiceService.GenerateVoiceAsync(script, shortenedAudioDirectory, options, logger, cancellationToken);
                await logger.SaveJsonAsync("voice-segments-shortened.json", voiceSegments, cancellationToken);
                voiceDiagnostics = ShortDiagnosticsService.CreateVoiceDiagnostics(topic, script, voiceSegments);
                await logger.SaveJsonAsync("voice-analysis-shortened.json", voiceDiagnostics, cancellationToken);
                ShortDiagnosticsService.LogSummary(logger, "Voice after shorten", voiceDiagnostics);
            }

            progress?.Report(new ShortGenerationProgress(48, "Tworze plan wizualny"));
            var visualPlan = await _scriptService.CreateVisualPlanAsync(topic, sourceAnalysis, script, options, logger, cancellationToken);
            await logger.SaveJsonAsync("visual-plan.json", visualPlan, cancellationToken);
            script = _scriptService.ApplyVisualPlan(script, visualPlan, topic);
            await logger.SaveJsonAsync("script-with-visual-plan.json", script, cancellationToken);
            voiceSegments = ApplyScriptMetadataToVoiceSegments(script, voiceSegments);

            progress?.Report(new ShortGenerationProgress(55, "Pobieram klipy stock"));
            var videoDirectory = Path.Combine(projectDirectory, "videos");
            var clips = await _stockVideoService.DownloadVideosAsync(
                voiceSegments,
                videoDirectory,
                options,
                progress,
                logger,
                cancellationToken);
            await logger.SaveJsonAsync("pexels-clips.json", clips, cancellationToken);
            var clipDiagnostics = ShortDiagnosticsService.CreateClipDiagnostics(topic, script, voiceSegments, clips);
            await logger.SaveJsonAsync("clip-analysis.json", clipDiagnostics, cancellationToken);
            ShortDiagnosticsService.LogSummary(logger, "Clips", clipDiagnostics);

            progress?.Report(new ShortGenerationProgress(68, "Sprawdzam bramke jakosci"));
            var qualityGateScriptDiagnostics = ShortDiagnosticsService.CreateScriptDiagnostics(topic, script);
            await logger.SaveJsonAsync("quality-gate-script-analysis.json", qualityGateScriptDiagnostics, cancellationToken);
            ShortDiagnosticsService.LogSummary(logger, "Quality gate script", qualityGateScriptDiagnostics);
            await logger.SaveJsonAsync(
                "quality-gate-input-summary.json",
                new
                {
                    script = qualityGateScriptDiagnostics.Summary,
                    voice = voiceDiagnostics.Summary,
                    clips = clipDiagnostics.Summary,
                    review = new
                    {
                        contentReview.Approved,
                        contentReview.UsefulnessScore,
                        contentReview.PromiseCheck,
                        contentReview.AudienceValueCheck,
                        issueCount = contentReview.Issues.Count,
                        criticalIssueCount = contentReview.Issues.Count(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
                    }
                },
                cancellationToken);
            var qualityGate = QualityGateService.EvaluateBeforeRender(
                topic,
                sourceAnalysis,
                script,
                contentReview,
                visualPlan,
                voiceSegments,
                clips);
            await logger.SaveJsonAsync("quality-gate.json", qualityGate, cancellationToken);
            LogQualityGate(logger, qualityGate);
            if (!qualityGate.Passed)
            {
                var blockingIssues = qualityGate.Issues
                    .Where(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (blockingIssues.Count == 0)
                {
                    blockingIssues = qualityGate.Issues
                        .Where(issue => issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                var errors = string.Join(
                    Environment.NewLine,
                    blockingIssues.Select(issue => $"- {issue.Code}: {issue.Message}"));
                if (string.IsNullOrWhiteSpace(errors))
                {
                    errors = string.Join(
                        Environment.NewLine,
                        qualityGate.Criteria
                            .Where(criterion => criterion.Points < criterion.MaxPoints)
                            .OrderBy(criterion => criterion.Points - criterion.MaxPoints)
                            .Select(criterion => $"- {criterion.Name}: {criterion.Points}/{criterion.MaxPoints}. {criterion.Reason}"));
                }

                throw new InvalidOperationException(
                    $"Bramka jakosci zatrzymala render. Wynik: {qualityGate.Score}/{qualityGate.Criteria.Sum(item => item.MaxPoints)}.{Environment.NewLine}{errors}{Environment.NewLine}{Environment.NewLine}Szczegoly: debug/quality-gate.json");
            }

            progress?.Report(new ShortGenerationProgress(70, "Montuje film w FFmpeg"));
            var outputPath = await _videoService.RenderVideoAsync(
                script,
                voiceSegments,
                clips,
                projectDirectory,
                progress,
                logger,
                cancellationToken);
            var finalDiagnostics = ShortDiagnosticsService.CreateFinalDiagnostics(topic, script, voiceSegments, clips, outputPath);
            await logger.SaveJsonAsync("short-diagnostics.json", finalDiagnostics, cancellationToken);
            ShortDiagnosticsService.LogSummary(logger, "Final", finalDiagnostics);

            await SaveJsonAsync(
                Path.Combine(projectDirectory, "project.json"),
                new
                {
                    topic,
                    sourceAnalysis,
                    conceptSelection,
                    script,
                    contentReview,
                    visualPlan,
                    qualityGate,
                    voiceSegments,
                    clips,
                    diagnostics = finalDiagnostics,
                    outputPath,
                    debugLogPath = logger.LogPath,
                    createdAt = DateTimeOffset.Now
                },
                cancellationToken);

            logger.Info($"Generation finished. OutputPath={outputPath}");
            return outputPath;
        }
        catch (Exception ex)
        {
            logger.Error("Generation failed.", ex);
            throw new InvalidOperationException($"{ex.Message}{Environment.NewLine}{Environment.NewLine}Debug log:{Environment.NewLine}{logger.LogPath}", ex);
        }
    }

    private static async Task SaveJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static object CreateRuntimeOptionsLog(ShortGeneratorOptions options)
    {
        var openAIKeyProvided = HasValue(
            options.OpenAIApiKey,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Machine));
        var configuredProvider = string.IsNullOrWhiteSpace(options.ModelProvider)
            ? "auto"
            : options.ModelProvider.Trim().ToLowerInvariant();
        var resolvedProvider = configuredProvider.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? openAIKeyProvided ? "openai" : "ollama"
            : configuredProvider;
        return new
        {
            modelProvider = options.ModelProvider,
            resolvedProvider,
            ollamaBaseUrl = options.OllamaBaseUrl,
            ollamaModel = options.OllamaModel,
            openAIBaseUrl = options.OpenAIBaseUrl,
            openAIModel = options.OpenAIModel,
            openAIReasoningEffort = options.OpenAIReasoningEffort,
            openAIKeyProvided,
            strictModelSchema = resolvedProvider.Equals("openai", StringComparison.OrdinalIgnoreCase),
            degradedModelMode = !resolvedProvider.Equals("openai", StringComparison.OrdinalIgnoreCase),
            pexelsKeyProvided = HasValue(
                options.PexelsApiKey,
                Environment.GetEnvironmentVariable("PEXELS_API_KEY"),
                Environment.GetEnvironmentVariable("PEXELS_API_KEY", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("PEXELS_API_KEY", EnvironmentVariableTarget.Machine)),
            pixabayKeyProvided = HasValue(
                options.PixabayApiKey,
                Environment.GetEnvironmentVariable("PIXABAY_API_KEY"),
                Environment.GetEnvironmentVariable("PIXABAY_API_KEY", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("PIXABAY_API_KEY", EnvironmentVariableTarget.Machine)),
            piperExeConfigured = HasValue(options.PiperExePath, Environment.GetEnvironmentVariable("PIPER_EXE")),
            piperModelConfigured = HasValue(options.PiperModelPath, Environment.GetEnvironmentVariable("PIPER_MODEL"))
        };
    }

    private static bool HasValue(params string?[] values)
    {
        return values.Any(value => !string.IsNullOrWhiteSpace(value));
    }

    internal static bool ShouldRepairAfterReview(ContentReview review)
    {
        var activeBlockingIssues = review.Issues
            .Where(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            .Where(issue => !IsDowngradedReviewIssue(issue))
            .ToList();
        return !review.Approved || activeBlockingIssues.Count > 0;
    }

    private static bool IsDowngradedReviewIssue(ContentReviewIssue issue)
    {
        return issue.Message.Contains("[Zdegradowano:", StringComparison.OrdinalIgnoreCase)
            || issue.SuggestedFix.Contains("[Zdegradowano:", StringComparison.OrdinalIgnoreCase);
    }

    private static void LogQualityGate(GenerationDebugLogger logger, QualityGateReport report)
    {
        var maxScore = report.Criteria.Sum(criterion => criterion.MaxPoints);
        logger.Info($"Quality gate result: passed={report.Passed}; score={report.Score}/{maxScore}; minimum={report.MinimumScore}; issueCount={report.Issues.Count}");
        foreach (var criterion in report.Criteria)
        {
            var missing = criterion.MaxPoints - criterion.Points;
            logger.Info($"Quality gate criterion: {criterion.Name}; points={criterion.Points}/{criterion.MaxPoints}; missing={missing}; reason={criterion.Reason}");
        }

        foreach (var issue in report.Issues)
        {
            if (issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                logger.Error($"Quality gate issue [{issue.Severity}] {issue.Code}: {issue.Message}");
            }
            else
            {
                logger.Warning($"Quality gate issue [{issue.Severity}] {issue.Code}: {issue.Message}");
            }
        }

        if (!report.Passed)
        {
            var weakest = report.Criteria
                .OrderByDescending(criterion => criterion.MaxPoints - criterion.Points)
                .FirstOrDefault();
            if (weakest is not null)
            {
                logger.Warning($"Quality gate failed. Weakest criterion={weakest.Name}; points={weakest.Points}/{weakest.MaxPoints}; reason={weakest.Reason}");
            }
        }
    }

    private static string CreateProjectDirectory(string title)
    {
        var outputRoot = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputRoot);

        return Path.Combine(outputRoot, SanitizeFileName($"{DateTime.Now:yyyyMMdd-HHmmss}-{title}"));
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return sanitized.Length > 90 ? sanitized[..90] : sanitized;
    }

    private static int CalculateWordBudget(int durationSeconds)
    {
        return Math.Max(18, (int)Math.Floor(Math.Max(durationSeconds, 10) * 2.25));
    }

    private static int CalculateMeasuredWordBudget(
        ShortScript script,
        int durationSeconds,
        double measuredSeconds)
    {
        var currentWords = CountScriptWords(script);
        if (measuredSeconds <= 0)
        {
            return CalculateWordBudget(durationSeconds);
        }

        return Math.Max(14, (int)Math.Floor(currentWords * durationSeconds / measuredSeconds * 0.92));
    }

    private static int CountScriptWords(ShortScript script)
    {
        return CountWords(script.Hook)
            + CountWords(script.Ending)
            + script.Scenes.Sum(scene => CountWords(scene.VoiceOver));
    }

    private static int CountWords(string value)
    {
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static IReadOnlyList<VoiceSegment> ApplyScriptMetadataToVoiceSegments(
        ShortScript script,
        IReadOnlyList<VoiceSegment> voiceSegments)
    {
        return voiceSegments.Select(segment =>
        {
            if (segment.Name.Equals("hook", StringComparison.OrdinalIgnoreCase))
            {
                return CloneVoiceSegment(
                    segment,
                    "problem",
                    [],
                    "Ustawia problem i obietnice filmu.",
                    script.HookSearchPhrase,
                    [script.HookSearchPhrase],
                    "generic social media recording, unrelated beauty routine, random phone selfie",
                    segment.VisualDescription);
            }

            if (segment.Name.Equals("ending", StringComparison.OrdinalIgnoreCase))
            {
                return CloneVoiceSegment(
                    segment,
                    "cta",
                    [],
                    "Domyka obietnice filmu i daje jedno zadanie.",
                    script.EndingSearchPhrase,
                    [script.EndingSearchPhrase],
                    "generic social media recording, unrelated beauty routine, random phone selfie",
                    segment.VisualDescription);
            }

            var sceneIndex = ParseSceneIndex(segment.Name);
            var scene = script.Scenes.ElementAtOrDefault(sceneIndex);
            if (scene is null)
            {
                return segment;
            }

            return CloneVoiceSegment(
                segment,
                scene.Role,
                scene.SourceFactIds,
                scene.NewInformation,
                scene.SearchPhrase,
                scene.SearchPhrases.Count == 0 ? [scene.SearchPhrase] : scene.SearchPhrases,
                scene.AvoidVisuals,
                scene.VisualDescription);
        }).ToList();
    }

    private static VoiceSegment CloneVoiceSegment(
        VoiceSegment segment,
        string role,
        List<string> sourceFactIds,
        string newInformation,
        string searchPhrase,
        List<string> searchPhrases,
        string avoidVisuals,
        string visualDescription)
    {
        return new VoiceSegment
        {
            Index = segment.Index,
            Name = segment.Name,
            Role = role,
            Text = segment.Text,
            SourceFactIds = sourceFactIds,
            NewInformation = newInformation,
            OnScreenText = segment.OnScreenText,
            VisualDescription = visualDescription,
            SearchPhrase = searchPhrase,
            SearchPhrases = searchPhrases,
            AvoidVisuals = avoidVisuals,
            AudioPath = segment.AudioPath,
            Duration = segment.Duration
        };
    }

    private static int ParseSceneIndex(string segmentName)
    {
        return int.TryParse(segmentName.Replace("scene_", string.Empty, StringComparison.OrdinalIgnoreCase), out var value)
            ? Math.Max(value - 1, 0)
            : 0;
    }
}
