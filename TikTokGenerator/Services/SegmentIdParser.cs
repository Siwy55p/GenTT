using System.Text.RegularExpressions;

namespace TikTokGenerator.Services;

internal static class SegmentIdParser
{
    public static int ParseSceneIndexOrDefault(string segmentName, int defaultIndex = 0)
    {
        return int.TryParse(segmentName.Replace("scene_", string.Empty, StringComparison.OrdinalIgnoreCase), out var value)
            ? Math.Max(value - 1, 0)
            : defaultIndex;
    }

    public static int FindSceneIndexOrDefault(string segmentName, int defaultIndex = -1)
    {
        var match = Regex.Match(
            segmentName,
            @"scene_(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var index)
            ? index - 1
            : defaultIndex;
    }
}
