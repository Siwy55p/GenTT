namespace TikTokGenerator.Models;

public sealed class DownloadedVideoClip
{
    public required int SegmentIndex { get; init; }

    public required string SearchPhrase { get; init; }

    public required string VisualDescription { get; init; }

    public required string FilePath { get; init; }

    public required string PexelsUrl { get; init; }

    public required int PexelsRank { get; init; }

    public required string SelectionReason { get; init; }

    public required string AuthorName { get; init; }

    public required string AuthorUrl { get; init; }
}
