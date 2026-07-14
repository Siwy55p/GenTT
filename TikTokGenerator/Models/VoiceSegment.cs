namespace TikTokGenerator.Models;

public sealed class VoiceSegment
{
    public required int Index { get; init; }

    public required string Name { get; init; }

    public required string Text { get; init; }

    public required string OnScreenText { get; init; }

    public required string VisualDescription { get; init; }

    public required string SearchPhrase { get; init; }

    public required string AudioPath { get; init; }

    public required TimeSpan Duration { get; init; }
}
