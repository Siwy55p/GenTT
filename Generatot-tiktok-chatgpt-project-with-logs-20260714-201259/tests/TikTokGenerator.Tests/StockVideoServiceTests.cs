using System.Net;
using System.Text;
using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class StockVideoServiceTests
{
    [Fact]
    public async Task DownloadVideosAsync_WhenPexelsQualityIsNull_DoesNotThrow()
    {
        var searchJson = """
            {
              "videos": [
                {
                  "width": 1080,
                  "height": 1920,
                  "duration": 8,
                  "url": "https://www.pexels.com/video/null-quality/",
                  "user": { "name": "Tester", "url": "https://www.pexels.com/@tester" },
                  "video_files": [
                    {
                      "quality": null,
                      "file_type": "video/mp4",
                      "width": 1080,
                      "height": 1920,
                      "link": "https://cdn.test/null-quality.mp4"
                    }
                  ]
                }
              ]
            }
            """;
        using var fixture = new StockVideoFixture(searchJson);
        var service = new StockVideoService(fixture.HttpClient);

        var clips = await service.DownloadVideosAsync(
            [CreateSegment("phone notification settings minimalism")],
            fixture.OutputDirectory,
            new ShortGeneratorOptions { PexelsApiKey = "test-key" });

        Assert.Single(clips);
        Assert.Equal("https://www.pexels.com/video/null-quality/", clips[0].PexelsUrl);
        Assert.Equal(1, clips[0].PexelsRank);
        Assert.True(File.Exists(clips[0].FilePath));
    }

    [Fact]
    public async Task DownloadVideosAsync_WhenSecondResultIsLonger_KeepsFirstRelevantPexelsRank()
    {
        var searchJson = """
            {
              "videos": [
                {
                  "width": 1080,
                  "height": 1920,
                  "duration": 5,
                  "url": "https://www.pexels.com/video/rank-one/",
                  "user": { "name": "Rank One", "url": "https://www.pexels.com/@rank-one" },
                  "video_files": [
                    {
                      "quality": "sd",
                      "file_type": "video/mp4",
                      "width": 1080,
                      "height": 1920,
                      "link": "https://cdn.test/rank-one.mp4"
                    }
                  ]
                },
                {
                  "width": 1080,
                  "height": 1920,
                  "duration": 55,
                  "url": "https://www.pexels.com/video/rank-two-longer/",
                  "user": { "name": "Rank Two", "url": "https://www.pexels.com/@rank-two" },
                  "video_files": [
                    {
                      "quality": "hd",
                      "file_type": "video/mp4",
                      "width": 1080,
                      "height": 1920,
                      "link": "https://cdn.test/rank-two.mp4"
                    }
                  ]
                }
              ]
            }
            """;
        using var fixture = new StockVideoFixture(searchJson);
        var service = new StockVideoService(fixture.HttpClient);

        var clips = await service.DownloadVideosAsync(
            [CreateSegment("minimalist phone screen clean")],
            fixture.OutputDirectory,
            new ShortGeneratorOptions { PexelsApiKey = "test-key" });

        Assert.Single(clips);
        Assert.Equal("https://www.pexels.com/video/rank-one/", clips[0].PexelsUrl);
        Assert.Equal(1, clips[0].PexelsRank);
    }

    private static VoiceSegment CreateSegment(string searchPhrase)
    {
        return new VoiceSegment
        {
            Index = 0,
            Name = "hook",
            Text = "Test lektora.",
            OnScreenText = "Test",
            VisualDescription = "Telefon z uporzadkowanym ekranem.",
            SearchPhrase = searchPhrase,
            AudioPath = "test.wav",
            Duration = TimeSpan.FromSeconds(1)
        };
    }

    private sealed class StockVideoFixture : IDisposable
    {
        public StockVideoFixture(string searchJson)
        {
            OutputDirectory = Path.Combine(Path.GetTempPath(), "TikTokGenerator.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(OutputDirectory);
            HttpClient = new HttpClient(new StubHttpMessageHandler(searchJson));
        }

        public HttpClient HttpClient { get; }

        public string OutputDirectory { get; }

        public void Dispose()
        {
            HttpClient.Dispose();
            if (Directory.Exists(OutputDirectory))
            {
                Directory.Delete(OutputDirectory, recursive: true);
            }
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _searchJson;

        public StubHttpMessageHandler(string searchJson)
        {
            _searchJson = searchJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host.Equals("api.pexels.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_searchJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            });
        }
    }
}
