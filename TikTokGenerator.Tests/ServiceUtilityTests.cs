using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class ServiceUtilityTests
{
    [Fact]
    public void WordCounter_CountSpaceSeparated_PreservesExistingSpaceOnlyBehavior()
    {
        Assert.Equal(2, WordCounter.CountSpaceSeparated("jedno\tdwa trzy"));
    }

    [Fact]
    public void WordCounter_CountNormalizedWhitespace_CollapsesAllWhitespace()
    {
        Assert.Equal(3, WordCounter.CountNormalizedWhitespace(" jedno\tdwa \r\n trzy "));
    }

    [Fact]
    public void SegmentIdParser_ParseSceneIndexOrDefault_UsesLegacyFallback()
    {
        Assert.Equal(0, SegmentIdParser.ParseSceneIndexOrDefault("hook"));
        Assert.Equal(0, SegmentIdParser.ParseSceneIndexOrDefault("scene_00"));
        Assert.Equal(2, SegmentIdParser.ParseSceneIndexOrDefault("scene_03"));
        Assert.Equal(7, SegmentIdParser.ParseSceneIndexOrDefault("scene_03_extra", defaultIndex: 7));
    }

    [Fact]
    public void SegmentIdParser_FindSceneIndexOrDefault_FindsSceneTokenInsideSegmentName()
    {
        Assert.Equal(-1, SegmentIdParser.FindSceneIndexOrDefault("hook"));
        Assert.Equal(2, SegmentIdParser.FindSceneIndexOrDefault("visual_scene_03_extra"));
    }

    [Fact]
    public void FileNameSanitizer_ForDebugFile_ReplacesInvalidCharactersWithoutTruncating()
    {
        var invalid = Path.GetInvalidFileNameChars()[0];
        var value = $"debug{invalid}file name";

        Assert.Equal("debug-file name", FileNameSanitizer.ForDebugFile(value));
    }

    [Fact]
    public void FileNameSanitizer_ForProjectDirectory_ReplacesInvalidCharactersAndTruncatesToProjectLimit()
    {
        var invalid = Path.GetInvalidFileNameChars()[0];
        var value = $"project{invalid}{new string('a', 100)}";

        var sanitized = FileNameSanitizer.ForProjectDirectory(value);

        Assert.StartsWith("project-", sanitized);
        Assert.Equal(90, sanitized.Length);
    }

    [Fact]
    public void FileNameSanitizer_ForStockVideoFile_ReplacesSpacesAndTruncatesToStockLimit()
    {
        var invalid = Path.GetInvalidFileNameChars()[0];
        var value = $"stock file{invalid}{new string('a', 100)}";

        var sanitized = FileNameSanitizer.ForStockVideoFile(value);

        Assert.StartsWith("stock_file-", sanitized);
        Assert.Equal(48, sanitized.Length);
    }
}
