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
        Assert.False(File.Exists($"{clips[0].FilePath}.part"));
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

    [Fact]
    public async Task DownloadVideosAsync_WhenFirstCandidateHasLowSemanticFit_SelectsMatchingMetadata()
    {
        var searchJson = """
            {
              "videos": [
                {
                  "width": 1080,
                  "height": 1920,
                  "duration": 8,
                  "url": "https://www.pexels.com/video/cooking-recipe-kitchen-studio-1/",
                  "image": "https://images.pexels.com/videos/cooking-recipe-kitchen-studio.jpg",
                  "user": { "name": "Chef Studio", "url": "https://www.pexels.com/@chef" },
                  "video_files": [
                    {
                      "quality": "hd",
                      "file_type": "video/mp4",
                      "width": 1080,
                      "height": 1920,
                      "link": "https://cdn.test/cooking.mp4"
                    }
                  ]
                },
                {
                  "width": 1080,
                  "height": 1920,
                  "duration": 8,
                  "url": "https://www.pexels.com/video/smartphone-photogrammetry-object-scan-2/",
                  "image": "https://images.pexels.com/videos/smartphone-photogrammetry-object-scan.jpg",
                  "user": { "name": "Tech Studio", "url": "https://www.pexels.com/@tech" },
                  "video_files": [
                    {
                      "quality": "hd",
                      "file_type": "video/mp4",
                      "width": 1080,
                      "height": 1920,
                      "link": "https://cdn.test/scan.mp4"
                    }
                  ]
                }
              ]
            }
            """;
        using var fixture = new StockVideoFixture(searchJson);
        var service = new StockVideoService(fixture.HttpClient);

        var clips = await service.DownloadVideosAsync(
            [CreateSegment("smartphone photogrammetry object scan", "smartphone photogrammetry object scan app preview")],
            fixture.OutputDirectory,
            new ShortGeneratorOptions { PexelsApiKey = "test-key" });

        Assert.Single(clips);
        Assert.Equal("https://www.pexels.com/video/smartphone-photogrammetry-object-scan-2/", clips[0].PexelsUrl);
        Assert.Equal(2, clips[0].PexelsRank);
        Assert.Contains("semanticFit", clips[0].SelectionReason);
    }

    [Fact]
    public async Task DownloadVideosAsync_WhenSearchIsRateLimited_RetriesBeforeFailing()
    {
        var searchJson = """
            {
              "videos": [
                {
                  "width": 1080,
                  "height": 1920,
                  "duration": 8,
                  "url": "https://www.pexels.com/video/retry-success/",
                  "user": { "name": "Tester", "url": "https://www.pexels.com/@tester" },
                  "video_files": [
                    {
                      "quality": "hd",
                      "file_type": "video/mp4",
                      "width": 1080,
                      "height": 1920,
                      "link": "https://cdn.test/retry-success.mp4"
                    }
                  ]
                }
              ]
            }
            """;
        using var fixture = new StockVideoFixture(
            (HttpStatusCode.TooManyRequests, "{\"error\":\"rate limit\"}"),
            (HttpStatusCode.OK, searchJson));
        var service = new StockVideoService(fixture.HttpClient);

        var clips = await service.DownloadVideosAsync(
            [CreateSegment("phone notification settings minimalism")],
            fixture.OutputDirectory,
            new ShortGeneratorOptions { PexelsApiKey = "test-key" });

        Assert.Single(clips);
        Assert.Equal("https://www.pexels.com/video/retry-success/", clips[0].PexelsUrl);
        Assert.Equal(2, fixture.SearchRequestCount);
    }

    [Fact]
    public async Task DownloadVideosAsync_WhenPexelsHasNoCandidates_UsesPixabayFallback()
    {
        var pexelsSearchJson = """
            {
              "videos": []
            }
            """;
        var pixabaySearchJson = """
            {
              "hits": [
                {
                  "id": 4455,
                  "pageURL": "https://pixabay.com/videos/business-owner-laptop-planning-4455/",
                  "tags": "business, owner, laptop, planning",
                  "duration": 8,
                  "user": "Pixabay Tester",
                  "videos": {
                    "medium": {
                      "url": "https://cdn.pixabay.test/business-owner.mp4",
                      "width": 1080,
                      "height": 1920,
                      "thumbnail": "https://cdn.pixabay.test/business-owner.jpg"
                    }
                  }
                }
              ]
            }
            """;
        using var fixture = StockVideoFixture.WithPixabayResponses(
            [(HttpStatusCode.OK, pexelsSearchJson)],
            [(HttpStatusCode.OK, pixabaySearchJson)]);
        var service = new StockVideoService(fixture.HttpClient);

        var clips = await service.DownloadVideosAsync(
            [CreateSegment("small business owner planning customer tasks", "business owner planning work on laptop")],
            fixture.OutputDirectory,
            new ShortGeneratorOptions
            {
                PexelsApiKey = "test-pexels-key",
                PixabayApiKey = "test-pixabay-key"
            });

        Assert.Single(clips);
        Assert.Equal("https://pixabay.com/videos/business-owner-laptop-planning-4455/", clips[0].PexelsUrl);
        Assert.Equal("https://cdn.pixabay.test/business-owner.jpg", clips[0].ThumbnailUrl);
        Assert.Contains("provider=Pixabay", clips[0].SelectionReason);
        Assert.Equal("Pixabay Tester", clips[0].AuthorName);
        Assert.True(File.Exists(clips[0].FilePath));
        Assert.False(File.Exists($"{clips[0].FilePath}.part"));
        Assert.Equal(1, fixture.SearchRequestCount);
        Assert.Equal(1, fixture.PixabaySearchRequestCount);
    }

    [Fact]
    public async Task DownloadVideosAsync_WhenOnlyPixabayApiKeyIsConfigured_SearchesPixabayWithConfiguredKey()
    {
        using var _ = new EnvironmentVariableScope("PEXELS_API_KEY", " ");
        var pixabaySearchJson = """
            {
              "hits": [
                {
                  "id": 7788,
                  "pageURL": "https://pixabay.com/videos/business-owner-laptop-planning-7788/",
                  "tags": "small business, owner, planning, laptop",
                  "duration": 8,
                  "user": "Pixabay Tester",
                  "videos": {
                    "medium": {
                      "url": "https://cdn.pixabay.test/pixabay-only.mp4",
                      "width": 1080,
                      "height": 1920,
                      "thumbnail": "https://cdn.pixabay.test/pixabay-only.jpg"
                    }
                  }
                }
              ]
            }
            """;
        using var fixture = StockVideoFixture.WithPixabayResponses(
            [],
            [(HttpStatusCode.OK, pixabaySearchJson)]);
        var service = new StockVideoService(fixture.HttpClient);

        var clips = await service.DownloadVideosAsync(
            [CreateSegment("small business owner planning customer tasks", "business owner planning work on laptop")],
            fixture.OutputDirectory,
            new ShortGeneratorOptions
            {
                PixabayApiKey = "test-pixabay-key"
            });

        Assert.Single(clips);
        Assert.Equal("https://pixabay.com/videos/business-owner-laptop-planning-7788/", clips[0].PexelsUrl);
        Assert.Contains("provider=Pixabay", clips[0].SelectionReason);
        Assert.Equal(0, fixture.SearchRequestCount);
        Assert.Equal(1, fixture.PixabaySearchRequestCount);
        Assert.NotNull(fixture.LastPixabaySearchRequestUri);
        Assert.Contains("key=test-pixabay-key", fixture.LastPixabaySearchRequestUri!.Query);
        Assert.Contains("q=small%20business%20owner%20planning%20customer%20tasks", fixture.LastPixabaySearchRequestUri.Query);
    }

    private static VoiceSegment CreateSegment(
        string searchPhrase,
        string visualDescription = "Telefon z uporzadkowanym ekranem.")
    {
        return new VoiceSegment
        {
            Index = 0,
            Name = "hook",
            Text = "Test lektora.",
            OnScreenText = "Test",
            VisualDescription = visualDescription,
            SearchPhrase = searchPhrase,
            AudioPath = "test.wav",
            Duration = TimeSpan.FromSeconds(1)
        };
    }

    private sealed class StockVideoFixture : IDisposable
    {
        public StockVideoFixture(string searchJson)
            : this((HttpStatusCode.OK, searchJson))
        {
        }

        public StockVideoFixture(params (HttpStatusCode StatusCode, string Body)[] searchResponses)
            : this(searchResponses, [])
        {
        }

        private StockVideoFixture(
            (HttpStatusCode StatusCode, string Body)[] pexelsSearchResponses,
            (HttpStatusCode StatusCode, string Body)[] pixabaySearchResponses)
        {
            OutputDirectory = Path.Combine(Path.GetTempPath(), "TikTokGenerator.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(OutputDirectory);
            Handler = new StubHttpMessageHandler(pexelsSearchResponses, pixabaySearchResponses);
            HttpClient = new HttpClient(Handler);
        }

        public static StockVideoFixture WithPixabayResponses(
            (HttpStatusCode StatusCode, string Body)[] pexelsSearchResponses,
            (HttpStatusCode StatusCode, string Body)[] pixabaySearchResponses)
        {
            return new StockVideoFixture(pexelsSearchResponses, pixabaySearchResponses);
        }

        public HttpClient HttpClient { get; }

        public string OutputDirectory { get; }

        public int SearchRequestCount => Handler.SearchRequestCount;

        public int PixabaySearchRequestCount => Handler.PixabaySearchRequestCount;

        public Uri? LastPixabaySearchRequestUri => Handler.LastPixabaySearchRequestUri;

        private StubHttpMessageHandler Handler { get; }

        public void Dispose()
        {
            HttpClient.Dispose();
            if (Directory.Exists(OutputDirectory))
            {
                Directory.Delete(OutputDirectory, recursive: true);
            }
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue, EnvironmentVariableTarget.Process);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> _pexelsSearchResponses;
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> _pixabaySearchResponses;
        private (HttpStatusCode StatusCode, string Body) _lastPexelsSearchResponse = (HttpStatusCode.OK, "{\"videos\":[]}");
        private (HttpStatusCode StatusCode, string Body) _lastPixabaySearchResponse = (HttpStatusCode.OK, "{\"hits\":[]}");

        public StubHttpMessageHandler(
            (HttpStatusCode StatusCode, string Body)[] pexelsSearchResponses,
            (HttpStatusCode StatusCode, string Body)[] pixabaySearchResponses)
        {
            _pexelsSearchResponses = new Queue<(HttpStatusCode StatusCode, string Body)>(pexelsSearchResponses);
            _pixabaySearchResponses = new Queue<(HttpStatusCode StatusCode, string Body)>(pixabaySearchResponses);
        }

        public int SearchRequestCount { get; private set; }

        public int PixabaySearchRequestCount { get; private set; }

        public Uri? LastPixabaySearchRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host.Equals("api.pexels.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                SearchRequestCount++;
                var response = _pexelsSearchResponses.Count == 0 ? _lastPexelsSearchResponse : _pexelsSearchResponses.Dequeue();
                _lastPexelsSearchResponse = response;
                return Task.FromResult(new HttpResponseMessage(response.StatusCode)
                {
                    Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri?.Host.Equals("pixabay.com", StringComparison.OrdinalIgnoreCase) == true &&
                request.RequestUri.AbsolutePath.Contains("/api/videos", StringComparison.OrdinalIgnoreCase))
            {
                PixabaySearchRequestCount++;
                LastPixabaySearchRequestUri = request.RequestUri;
                var response = _pixabaySearchResponses.Count == 0 ? _lastPixabaySearchResponse : _pixabaySearchResponses.Dequeue();
                _lastPixabaySearchResponse = response;
                return Task.FromResult(new HttpResponseMessage(response.StatusCode)
                {
                    Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            });
        }
    }
}
