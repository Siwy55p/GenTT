namespace TikTokGenerator.Models;

public sealed class QualityGateReport
{
    public int Score { get; set; }

    public int MinimumScore { get; set; } = 80;

    public bool Passed { get; set; }

    public List<QualityGateCriterion> Criteria { get; set; } = [];

    public List<QualityGateIssue> Issues { get; set; } = [];
}

public sealed class QualityGateCriterion
{
    public string Name { get; set; } = string.Empty;

    public int Points { get; set; }

    public int MaxPoints { get; set; }

    public string Reason { get; set; } = string.Empty;
}

public sealed class QualityGateIssue
{
    public string Severity { get; set; } = "info";

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
