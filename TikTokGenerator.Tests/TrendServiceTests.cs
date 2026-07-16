using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class TrendServiceTests
{
    [Theory]
    [InlineData("Technologia")]
    [InlineData("Biznes")]
    [InlineData("Lifestyle")]
    public async Task FindPopularTopicsAsync_ForBuiltInCategories_ReturnsConcreteSourceForEveryTopic(string category)
    {
        var service = new TrendService();

        var topics = await service.FindPopularTopicsAsync("Polska", category);

        Assert.Equal(5, topics.Count);
        Assert.All(topics, topic =>
        {
            Assert.Contains("Praktyczna teza:", topic.SourceText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Konkretne kroki:", topic.SourceText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Korzysc dla widza:", topic.SourceText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Nie dodawaj statystyk", topic.SourceText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Scenariusz powinien byc krotki", topic.SourceText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Struktura praktyczna", topic.SourceText, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task FindPopularTopicsAsync_WhenTechnologyScannerTopic_ReturnsSourceWithConcrete3DScanSteps()
    {
        var service = new TrendService();

        var topics = await service.FindPopularTopicsAsync("Polska", "Technologia");
        var scannerTopic = topics.Single(topic =>
            topic.Title.Contains("skaner 3D", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("fotogrametr", scannerTopic.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Konkretne kroki", scannerTopic.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obiekt", scannerTopic.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("model", scannerTopic.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rozprasz", scannerTopic.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powiadomien", scannerTopic.SourceText, StringComparison.OrdinalIgnoreCase);
    }
}
