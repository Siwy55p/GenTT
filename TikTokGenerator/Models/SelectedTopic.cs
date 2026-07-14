namespace TikTokGenerator.Models;

public sealed class SelectedTopic
{
    public required string Title { get; init; }

    public required string SourceText { get; init; }

    public string SourceUrl { get; init; } = string.Empty;
}
