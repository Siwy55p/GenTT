namespace TikTokGenerator.Models;

public sealed class ShortDiagnosticsReport
{
    public string Stage { get; set; } = string.Empty;

    public string TopicTitle { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public ShortDiagnosticsSummary Summary { get; set; } = new();

    public ScriptDiagnostics Script { get; set; } = new();

    public List<SegmentDiagnostics> Segments { get; set; } = [];

    public List<ShortDiagnosticIssue> Issues { get; set; } = [];
}

public sealed class ShortDiagnosticsSummary
{
    public int SceneCount { get; set; }

    public int SegmentCount { get; set; }

    public int PracticalSegmentCount { get; set; }

    public int IssueCount { get; set; }

    public int WarningCount { get; set; }

    public int ErrorCount { get; set; }

    public double EstimatedDurationSeconds { get; set; }

    public double SourceCoverageRatio { get; set; }

    public bool HasUnsupportedClaims { get; set; }
}

public sealed class ScriptDiagnostics
{
    public string Title { get; set; } = string.Empty;

    public string Hook { get; set; } = string.Empty;

    public string Ending { get; set; } = string.Empty;

    public int TotalVoiceWords { get; set; }

    public int TotalVoiceCharacters { get; set; }

    public List<string> SourceKeywords { get; set; } = [];

    public List<string> ScriptKeywords { get; set; } = [];

    public List<string> MatchedSourceKeywords { get; set; } = [];

    public List<string> GeneratedKeywordsNotInSource { get; set; } = [];
}

public sealed class SegmentDiagnostics
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string VoiceOver { get; set; } = string.Empty;

    public List<string> SourceFactIds { get; set; } = [];

    public string NewInformation { get; set; } = string.Empty;

    public string OnScreenText { get; set; } = string.Empty;

    public string VisualDescription { get; set; } = string.Empty;

    public string SearchPhrase { get; set; } = string.Empty;

    public int VoiceWordCount { get; set; }

    public int VoiceCharacterCount { get; set; }

    public int OnScreenTextLength { get; set; }

    public double DurationSeconds { get; set; }

    public double WordsPerMinute { get; set; }

    public bool HasActionVerb { get; set; }

    public bool HasStoryboardLanguage { get; set; }

    public bool HasUnsupportedNumber { get; set; }

    public bool HasGenericVisualDescription { get; set; }

    public List<string> SourceKeywordHits { get; set; } = [];

    public List<string> GeneratedKeywordsNotInSource { get; set; } = [];

    public string ClipUrl { get; set; } = string.Empty;

    public int? PexelsRank { get; set; }

    public string ClipSelectionReason { get; set; } = string.Empty;
}

public sealed class ShortDiagnosticIssue
{
    public required string Severity { get; init; }

    public required string Stage { get; init; }

    public required string Segment { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public string Evidence { get; init; } = string.Empty;

    public string Recommendation { get; init; } = string.Empty;
}
