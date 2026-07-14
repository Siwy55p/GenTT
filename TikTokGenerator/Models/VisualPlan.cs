namespace TikTokGenerator.Models;

public sealed class VisualPlan
{
    public List<VisualPlanSegment> Segments { get; set; } = [];

    public string GlobalAvoidVisuals { get; set; } = string.Empty;
}

public sealed class VisualPlanSegment
{
    public string SegmentName { get; set; } = string.Empty;

    public string VisibleContent { get; set; } = string.Empty;

    public string PersonAction { get; set; } = string.Empty;

    public string PrimaryObject { get; set; } = string.Empty;

    public string ShotType { get; set; } = string.Empty;

    public string MovementStart { get; set; } = string.Empty;

    public string MovementEnd { get; set; } = string.Empty;

    public string ResultToShow { get; set; } = string.Empty;

    public string AvoidVisuals { get; set; } = string.Empty;

    public List<string> SearchPhrases { get; set; } = [];
}
