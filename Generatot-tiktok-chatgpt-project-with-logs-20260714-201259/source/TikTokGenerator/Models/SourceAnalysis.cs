namespace TikTokGenerator.Models;

public sealed class SourceAnalysis
{
    public string MainThesis { get; set; } = string.Empty;

    public List<SourceFact> Facts { get; set; } = [];

    public List<SourceStep> Steps { get; set; } = [];

    public List<string> Examples { get; set; } = [];

    public List<string> Limitations { get; set; } = [];

    public List<string> RiskyClaims { get; set; } = [];

    public string MostUsefulFragment { get; set; } = string.Empty;
}

public sealed class SourceFact
{
    public string Id { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Evidence { get; set; } = string.Empty;
}

public sealed class SourceStep
{
    public string Id { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public List<string> SourceFactIds { get; set; } = [];
}
