using System.Text.RegularExpressions;

namespace TikTokGenerator.Services;

internal static class TextNormalizer
{
    public static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), "\\s+", " ", RegexOptions.CultureInvariant);
    }
}
