namespace TikTokGenerator.Models;

public sealed class ContentBrief
{
    public string Audience { get; set; } = "osoby zainteresowane praktycznym tematem";

    public string KnowledgeLevel { get; set; } = "poczatkujacy";

    public string ViewerProblem { get; set; } = "brak jasnego pierwszego kroku";

    public string DesiredOutcome { get; set; } = "wykonac jeden konkretny krok";

    public string ContentType { get; set; } = "praktyczny tutorial";

    public string Tone { get; set; } = "konkretny, bez coachingu";

    public int DurationSeconds { get; set; } = 25;

    public static ContentBrief CreateDefault() => new();
}
