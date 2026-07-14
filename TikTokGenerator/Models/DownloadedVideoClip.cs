namespace TikTokGenerator.Models;

public sealed class DownloadedVideoClip
{
    public required int SegmentIndex { get; init; }

    public required string SearchPhrase { get; init; }

    public List<string> SearchPhrases { get; init; } = [];

    public string AvoidVisuals { get; init; } = string.Empty;

    public required string VisualDescription { get; init; }

    public required string FilePath { get; init; }

    public required string PexelsUrl { get; init; }

    public string ThumbnailUrl { get; init; } = string.Empty;

    public required int PexelsRank { get; init; }

    public int CandidateCount { get; init; }

    public double ContentScore { get; init; }

    public required string SelectionReason { get; init; }

    public required string AuthorName { get; init; }

    public required string AuthorUrl { get; init; }
}
