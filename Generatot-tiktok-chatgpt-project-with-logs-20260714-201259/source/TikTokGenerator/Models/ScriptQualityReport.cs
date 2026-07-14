namespace TikTokGenerator.Models;

public sealed class ScriptQualityReport
{
    public List<ScriptQualityIssue> Issues { get; set; } = [];

    public bool HasBlockingIssues => Issues.Any(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));

    public bool HasWarnings => Issues.Any(issue => issue.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase));
}

public sealed class ScriptQualityIssue
{
    public required string Severity { get; init; }

    public required string Segment { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public string OriginalValue { get; init; } = string.Empty;

    public string FixedValue { get; init; } = string.Empty;
}
