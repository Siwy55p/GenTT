namespace TikTokGenerator.Models;

public sealed class VoiceSegment
{
    public required int Index { get; init; }

    public required string Name { get; init; }

    public string Role { get; init; } = string.Empty;

    public required string Text { get; init; }

    public List<string> SourceFactIds { get; init; } = [];

    public string NewInformation { get; init; } = string.Empty;

    public required string OnScreenText { get; init; }

    public required string VisualDescription { get; init; }

    public required string SearchPhrase { get; init; }

    public List<string> SearchPhrases { get; init; } = [];

    public string AvoidVisuals { get; init; } = string.Empty;

    public required string AudioPath { get; init; }

    public required TimeSpan Duration { get; init; }
}
