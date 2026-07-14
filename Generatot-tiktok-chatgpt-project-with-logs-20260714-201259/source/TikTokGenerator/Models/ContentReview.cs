namespace TikTokGenerator.Models;

public sealed class ContentReview
{
    public List<ContentReviewIssue> Issues { get; set; } = [];

    public string RepetitionCheck { get; set; } = string.Empty;

    public string ObviousAdviceCheck { get; set; } = string.Empty;

    public string SourceComparison { get; set; } = string.Empty;

    public string PromiseCheck { get; set; } = string.Empty;

    public string FeasibilityCheck { get; set; } = string.Empty;

    public string AudienceValueCheck { get; set; } = string.Empty;

    public List<string> SuggestedFixes { get; set; } = [];

    public int UsefulnessScore { get; set; }

    public bool Approved { get; set; }

    public bool HasCriticalErrors =>
        Issues.Any(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
}

public sealed class ContentReviewIssue
{
    public string Severity { get; set; } = "info";

    public string Segment { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string SuggestedFix { get; set; } = string.Empty;
}
