using System.Net;
using System.Text;
using System.Text.Json;
using TikTokGenerator.Models;
using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class ModelClientTests
{
    [Fact]
    public async Task GenerateJsonAsync_WhenProviderIsOpenAI_SendsStrictStructuredOutputsRequest()
    {
        var handler = new CapturingHandler("""
            {
              "id": "resp_test",
              "status": "completed",
              "model": "gpt-5.6-terra",
              "output": [
                {
                  "type": "message",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "{\"ok\":true}"
                    }
                  ]
                }
              ],
              "usage": {
                "input_tokens": 10,
                "output_tokens": 5,
                "total_tokens": 15
              }
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = new ModelClient(httpClient);

        var response = await client.GenerateJsonAsync(
            new ModelJsonRequest(
                "Zwroc JSON.",
                CreateSchema(),
                "source-analysis",
                new ShortGeneratorOptions
                {
                    ModelProvider = "openai",
                    OpenAIApiKey = "test-key",
                    OpenAIBaseUrl = "https://api.openai.test/v1",
                    OpenAIModel = "gpt-5.6-terra",
                    OpenAIReasoningEffort = "medium"
                },
                Temperature: 0.1,
                MaxOutputTokens: 300),
            logger: null,
            CancellationToken.None);

        Assert.Equal("openai", response.Provider);
        Assert.Equal("gpt-5.6-terra", response.Model);
        Assert.True(response.StrictSchema);
        Assert.Equal("source_analysis", response.SchemaName);
        Assert.Equal("{\"ok\":true}", response.RawText);
        Assert.Equal(10, response.InputTokens);
        Assert.Equal(5, response.OutputTokens);
        Assert.Equal(15, response.TotalTokens);

        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("test-key", handler.LastRequest?.Headers.Authorization?.Parameter);
        Assert.Equal("https://api.openai.test/v1/responses", handler.LastRequest?.RequestUri?.ToString());

        using var requestJson = JsonDocument.Parse(handler.LastRequestBody);
        var root = requestJson.RootElement;
        Assert.Equal("gpt-5.6-terra", root.GetProperty("model").GetString());
        Assert.Equal("Zwroc JSON.", root.GetProperty("input").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal(300, root.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("medium", root.GetProperty("reasoning").GetProperty("effort").GetString());

        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("source_analysis", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.False(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
        Assert.False(root.TryGetProperty("format", out _));
    }

    [Fact]
    public async Task GenerateJsonAsync_WhenProviderIsOllama_SendsOllamaSchemaRequest()
    {
        var handler = new CapturingHandler("""
            {
              "response": "{\"ok\":true}"
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = new ModelClient(httpClient);

        var response = await client.GenerateJsonAsync(
            new ModelJsonRequest(
                "Zwroc JSON.",
                CreateSchema(),
                "script",
                new ShortGeneratorOptions
                {
                    ModelProvider = "ollama",
                    OllamaBaseUrl = "http://ollama.test",
                    OllamaModel = "qwen3:4b"
                },
                Temperature: 0.2,
                MaxOutputTokens: 500),
            logger: null,
            CancellationToken.None);

        Assert.Equal("ollama", response.Provider);
        Assert.Equal("qwen3:4b", response.Model);
        Assert.False(response.StrictSchema);
        Assert.Equal("{\"ok\":true}", response.RawText);
        Assert.Equal("http://ollama.test/api/generate", handler.LastRequest?.RequestUri?.ToString());

        using var requestJson = JsonDocument.Parse(handler.LastRequestBody);
        var root = requestJson.RootElement;
        Assert.Equal("qwen3:4b", root.GetProperty("model").GetString());
        Assert.Contains("Zwroc JSON.", root.GetProperty("prompt").GetString());
        Assert.Contains("JSON schema", root.GetProperty("prompt").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal(0.2, root.GetProperty("options").GetProperty("temperature").GetDouble(), precision: 3);
        Assert.Equal(500, root.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.False(root.GetProperty("format").GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task GenerateJsonAsync_WhenOpenAIRateLimited_RetriesTransientRequest()
    {
        var handler = new CapturingHandler(
            (HttpStatusCode.TooManyRequests, "{\"error\":\"rate limit\"}"),
            (HttpStatusCode.OK, """
                {
                  "id": "resp_retry",
                  "status": "completed",
                  "model": "gpt-5.6-terra",
                  "output_text": "{\"ok\":true}"
                }
                """));
        using var httpClient = new HttpClient(handler);
        var client = new ModelClient(httpClient);

        var response = await client.GenerateJsonAsync(
            new ModelJsonRequest(
                "Zwroc JSON.",
                CreateSchema(),
                "source-analysis",
                new ShortGeneratorOptions
                {
                    ModelProvider = "openai",
                    OpenAIApiKey = "test-key",
                    OpenAIBaseUrl = "https://api.openai.test/v1"
                },
                Temperature: 0.1,
                MaxOutputTokens: 300),
            logger: null,
            CancellationToken.None);

        Assert.Equal("{\"ok\":true}", response.RawText);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task GenerateJsonAsync_WhenOllamaReturnsIncompleteJson_RetriesWithStricterPrompt()
    {
        var handler = new CapturingHandler(
            (HttpStatusCode.OK, "{\"response\":\"not json\"}"),
            (HttpStatusCode.OK, "{\"response\":\"{\\\"ok\\\":true}\"}"));
        using var httpClient = new HttpClient(handler);
        var client = new ModelClient(httpClient);

        var response = await client.GenerateJsonAsync(
            new ModelJsonRequest(
                "Zwroc JSON.",
                CreateSchema(),
                "script",
                new ShortGeneratorOptions
                {
                    ModelProvider = "ollama",
                    OllamaBaseUrl = "http://ollama.test",
                    OllamaModel = "qwen3:4b"
                },
                Temperature: 0.2,
                MaxOutputTokens: 500),
            logger: null,
            CancellationToken.None);

        Assert.Equal("{\"ok\":true}", response.RawText);
        Assert.Equal(2, handler.RequestBodies.Count);
        using var retryJson = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.Contains("Poprzednia odpowiedz", retryJson.RootElement.GetProperty("prompt").GetString());
        Assert.Equal(0, retryJson.RootElement.GetProperty("options").GetProperty("temperature").GetDouble());
    }

    private static object CreateSchema()
    {
        return new
        {
            type = "object",
            properties = new
            {
                ok = new { type = "boolean" }
            },
            required = new[] { "ok" },
            additionalProperties = false
        };
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses;

        public CapturingHandler(string responseBody)
            : this((HttpStatusCode.OK, responseBody))
        {
        }

        public CapturingHandler(params (HttpStatusCode StatusCode, string Body)[] responses)
        {
            _responses = new Queue<(HttpStatusCode StatusCode, string Body)>(responses);
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string LastRequestBody { get; private set; } = string.Empty;

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(LastRequestBody);

            var response = _responses.Count == 0
                ? (StatusCode: HttpStatusCode.OK, Body: "{}")
                : _responses.Dequeue();
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
            };
        }
    }
}
