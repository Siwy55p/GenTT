namespace TikTokGenerator.Models;

public sealed class SourceAnalysisDiagnostics
{
    public List<SourceAnalysisIssue> Issues { get; set; } = [];

    public bool HasBlockingIssues =>
        Issues.Any(issue => issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
}

public sealed class SourceAnalysisIssue
{
    public string Severity { get; set; } = "info";

    public string Code { get; set; } = string.Empty;

    public string Field { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
