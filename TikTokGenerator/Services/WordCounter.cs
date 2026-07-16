namespace TikTokGenerator.Services;

internal static class WordCounter
{
    public static int CountSpaceSeparated(string value)
    {
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    public static int CountNormalizedWhitespace(string value)
    {
        return CountSpaceSeparated(TextNormalizer.NormalizeWhitespace(value));
    }
}
