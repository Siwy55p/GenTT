namespace TikTokGenerator.Models;

public sealed record Trend(
    int Rank,
    string Title,
    string Country,
    string Category,
    string Source,
    string SourceText,
    string SourceUrl,
    DateTimeOffset DiscoveredAt)
{
    public override string ToString()
    {
        return Title;
    }
}
