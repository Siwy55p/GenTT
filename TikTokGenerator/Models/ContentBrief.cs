namespace TikTokGenerator.Models;

public sealed class ContentBrief
{
    public string Audience { get; set; } = "osoby pracujace przy komputerze";

    public string KnowledgeLevel { get; set; } = "poczatkujacy";

    public string ViewerProblem { get; set; } = "chaos po rozpoczeciu dnia";

    public string DesiredOutcome { get; set; } = "wybrac pierwszy priorytet";

    public string ContentType { get; set; } = "praktyczny tutorial";

    public string Tone { get; set; } = "konkretny, bez coachingu";

    public int DurationSeconds { get; set; } = 25;

    public static ContentBrief CreateDefault() => new();
}
