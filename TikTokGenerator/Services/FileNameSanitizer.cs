namespace TikTokGenerator.Services;

internal static class FileNameSanitizer
{
    public static string ForDebugFile(string value)
    {
        return ReplaceInvalidFileNameChars(value);
    }

    public static string ForProjectDirectory(string value)
    {
        return Truncate(ReplaceInvalidFileNameChars(value), 90);
    }

    public static string ForStockVideoFile(string value)
    {
        return Truncate(ReplaceInvalidFileNameChars(value).Replace(' ', '_'), 48);
    }

    private static string ReplaceInvalidFileNameChars(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length > maxLength ? value[..maxLength] : value;
    }
}
