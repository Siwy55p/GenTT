using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public interface IModelClient
{
    Task<ModelJsonResponse> GenerateJsonAsync(
        ModelJsonRequest request,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken);
}

public sealed record ModelJsonRequest(
    string Prompt,
    object Schema,
    string StageName,
    ShortGeneratorOptions Options,
    double Temperature,
    int MaxOutputTokens);

public sealed record ModelJsonResponse
{
    public string Provider { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string StageName { get; init; } = string.Empty;

    public string SchemaName { get; init; } = string.Empty;

    public bool StrictSchema { get; init; }

    public string RawText { get; init; } = string.Empty;

    public string ResponseId { get; init; } = string.Empty;

    public int StatusCode { get; init; }

    public long ElapsedMilliseconds { get; init; }

    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }

    public int? TotalTokens { get; init; }
}

public sealed class ModelClient : IModelClient
{
    private const int MaxTransientAttempts = 3;
    private const int MaxOllamaJsonAttempts = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;

    public ModelClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ModelJsonResponse> GenerateJsonAsync(
        ModelJsonRequest request,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(request.Options);
        return provider switch
        {
            "openai" => CallOpenAIAsync(request, logger, cancellationToken),
            "ollama" => CallOllamaAsync(request, logger, cancellationToken),
            _ => throw new InvalidOperationException($"Nieznany provider modelu AI: {provider}. Uzyj: auto, openai albo ollama.")
        };
    }

    private async Task<ModelJsonResponse> CallOpenAIAsync(
        ModelJsonRequest request,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var apiKey = ResolveOpenAIApiKey(request.Options);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Brakuje klucza OpenAI API. Ustaw OPENAI_API_KEY albo wybierz provider ollama.");
        }

        var model = ResolveOpenAIModel(request.Options);
        var schemaName = CreateSchemaName(request.StageName);
        var endpoint = new Uri(new Uri(ResolveOpenAIBaseUrl(request.Options).TrimEnd('/') + "/"), "responses");
        var body = CreateOpenAIRequestBody(request, model, schemaName);

        logger?.Info($"Calling OpenAI Responses endpoint={endpoint} model={model} stage={request.StageName}; promptChars={request.Prompt.Length}; schemaName={schemaName}; strictSchema=true; maxOutputTokens={request.MaxOutputTokens}; reasoningEffort={ResolveOpenAIReasoningEffort(request.Options)}");

        var result = await SendJsonWithTransientRetryAsync(
            () =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                httpRequest.Content = JsonContent.Create(body, options: JsonOptions);
                return httpRequest;
            },
            "OpenAI",
            request.StageName,
            logger,
            cancellationToken);

        logger?.Info($"OpenAI HTTP response stage={request.StageName}; status={result.StatusCode}; elapsedMs={result.ElapsedMilliseconds}; attempts={result.Attempts}; bodyChars={result.ResponseBody.Length}");
        if (logger is not null)
        {
            await logger.SaveTextAsync($"openai-{request.StageName}-http-response.json", result.ResponseBody, cancellationToken);
        }

        if (result.StatusCode < 200 || result.StatusCode > 299)
        {
            throw new InvalidOperationException($"OpenAI zwrocilo blad HTTP {result.StatusCode}: {result.ResponseBody}");
        }

        var parsed = ParseOpenAIResponse(result.ResponseBody);
        logger?.Info($"OpenAI model response stage={request.StageName}; responseId={parsed.ResponseId}; model={parsed.Model}; responseChars={parsed.RawText.Length}; inputTokens={parsed.InputTokens?.ToString() ?? string.Empty}; outputTokens={parsed.OutputTokens?.ToString() ?? string.Empty}; totalTokens={parsed.TotalTokens?.ToString() ?? string.Empty}");

        if (logger is not null)
        {
            await logger.SaveTextAsync($"openai-{request.StageName}-raw.txt", parsed.RawText, cancellationToken);
            await logger.SaveJsonAsync(
                $"model-call-{request.StageName}-summary.json",
                new
                {
                    provider = "openai",
                    parsed.Model,
                    request.StageName,
                    schemaName,
                    strictSchema = true,
                    promptChars = request.Prompt.Length,
                    responseChars = parsed.RawText.Length,
                    parsed.ResponseId,
                    parsed.InputTokens,
                    parsed.OutputTokens,
                    parsed.TotalTokens,
                    elapsedMs = result.ElapsedMilliseconds,
                    attempts = result.Attempts
                },
                cancellationToken);
        }

        return parsed with
        {
            Provider = "openai",
            StageName = request.StageName,
            SchemaName = schemaName,
            StrictSchema = true,
            StatusCode = result.StatusCode,
            ElapsedMilliseconds = result.ElapsedMilliseconds
        };
    }

    private async Task<ModelJsonResponse> CallOllamaAsync(
        ModelJsonRequest request,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(request.Options.OllamaModel)
            ? "qwen3:4b"
            : request.Options.OllamaModel;
        var endpoint = new Uri(new Uri(request.Options.OllamaBaseUrl.TrimEnd('/') + "/"), "api/generate");
        logger?.Warning($"Calling Ollama in degraded schema mode. endpoint={endpoint} model={model} stage={request.StageName}; promptChars={request.Prompt.Length}; schemaType={request.Schema.GetType().Name}; temperature={request.Temperature:0.###}; numPredict={request.MaxOutputTokens}; strictSchema=false");

        try
        {
            OllamaGenerateResponse? ollamaResponse = null;
            HttpSendResult? lastResult = null;
            for (var attempt = 1; attempt <= MaxOllamaJsonAttempts; attempt++)
            {
                var prompt = attempt == 1
                    ? CreateOllamaPrompt(request.Prompt, request.Schema)
                    : CreateOllamaRetryPrompt(request.Prompt, request.Schema);
                var body = new
                {
                    model,
                    prompt,
                    stream = false,
                    format = request.Schema,
                    think = false,
                    options = new
                    {
                        temperature = attempt == 1 ? request.Temperature : 0,
                        num_predict = request.MaxOutputTokens
                    }
                };

                var result = await SendJsonWithTransientRetryAsync(
                    () => new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = JsonContent.Create(body, options: JsonOptions)
                    },
                    "Ollama",
                    request.StageName,
                    logger,
                    cancellationToken);
                lastResult = result;
                logger?.Info($"Ollama HTTP response stage={request.StageName}; status={result.StatusCode}; elapsedMs={result.ElapsedMilliseconds}; attempts={result.Attempts}; jsonAttempt={attempt}; bodyChars={result.ResponseBody.Length}");
                if (logger is not null)
                {
                    await logger.SaveTextAsync($"ollama-{request.StageName}-http-response-attempt-{attempt:00}.json", result.ResponseBody, cancellationToken);
                    await logger.SaveTextAsync($"ollama-{request.StageName}-http-response.json", result.ResponseBody, cancellationToken);
                }

                if (result.StatusCode < 200 || result.StatusCode > 299)
                {
                    throw new InvalidOperationException($"Ollama zwrocila blad HTTP {result.StatusCode}: {result.ResponseBody}");
                }

                ollamaResponse = JsonSerializer.Deserialize<OllamaGenerateResponse>(result.ResponseBody, JsonOptions)
                    ?? throw new InvalidOperationException("Ollama zwrocila pusta odpowiedz.");
                if (LooksLikeCompleteJsonObject(ollamaResponse.Response))
                {
                    break;
                }

                logger?.Warning($"Ollama response for stage={request.StageName} did not contain a complete JSON object on attempt {attempt}. Retrying with stricter local prompt.");
            }

            if (ollamaResponse is null || lastResult is null)
            {
                throw new InvalidOperationException("Ollama nie zwrocila odpowiedzi.");
            }

            logger?.Info($"Ollama model response stage={request.StageName}; responseChars={ollamaResponse.Response.Length}");

            if (logger is not null)
            {
                await logger.SaveTextAsync($"ollama-{request.StageName}-raw.txt", ollamaResponse.Response, cancellationToken);
                await logger.SaveJsonAsync(
                    $"model-call-{request.StageName}-summary.json",
                    new
                    {
                        provider = "ollama",
                        model,
                        request.StageName,
                        schemaName = request.StageName,
                        strictSchema = false,
                        degradedSchemaMode = true,
                        promptChars = request.Prompt.Length,
                        responseChars = ollamaResponse.Response.Length,
                        elapsedMs = lastResult.ElapsedMilliseconds,
                        attempts = lastResult.Attempts
                    },
                    cancellationToken);
            }

            return new ModelJsonResponse
            {
                Provider = "ollama",
                Model = model,
                StageName = request.StageName,
                SchemaName = request.StageName,
                StrictSchema = false,
                RawText = ollamaResponse.Response,
                StatusCode = lastResult.StatusCode,
                ElapsedMilliseconds = lastResult.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Nie moge polaczyc sie z Ollama. Uruchom Ollama i wykonaj: ollama pull qwen3:4b",
                ex);
        }
    }

    private async Task<HttpSendResult> SendJsonWithTransientRetryAsync(
        Func<HttpRequestMessage> createRequest,
        string provider,
        string stageName,
        GenerationDebugLogger? logger,
        CancellationToken cancellationToken)
    {
        var totalElapsed = Stopwatch.StartNew();
        var attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                using var httpRequest = createRequest();
                using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!IsTransientStatusCode((int)response.StatusCode) || attempts >= MaxTransientAttempts)
                {
                    totalElapsed.Stop();
                    return new HttpSendResult((int)response.StatusCode, responseBody, totalElapsed.ElapsedMilliseconds, attempts);
                }

                logger?.Warning($"{provider} transient HTTP {(int)response.StatusCode} at stage={stageName}; retry={attempts}/{MaxTransientAttempts}.");
            }
            catch (HttpRequestException) when (attempts < MaxTransientAttempts)
            {
                logger?.Warning($"{provider} transient network error at stage={stageName}; retry={attempts}/{MaxTransientAttempts}.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempts < MaxTransientAttempts)
            {
                logger?.Warning($"{provider} transient timeout at stage={stageName}; retry={attempts}/{MaxTransientAttempts}.");
            }

            await Task.Delay(CreateRetryDelay(attempts), cancellationToken);
        }
    }

    private static TimeSpan CreateRetryDelay(int attempt)
    {
        var jitterMs = Random.Shared.Next(40, 140);
        var baseMs = Math.Min(250 * (1 << Math.Max(attempt - 1, 0)), 2000);
        return TimeSpan.FromMilliseconds(baseMs + jitterMs);
    }

    private static bool IsTransientStatusCode(int statusCode)
    {
        return statusCode == 408 || statusCode == 429 || statusCode >= 500;
    }

    private static string CreateOllamaPrompt(string prompt, object schema)
    {
        var schemaJson = JsonSerializer.Serialize(schema, JsonOptions);
        return $"""
            {prompt}

            JSON schema, ktorego musisz sie trzymac:
            {schemaJson}
            """;
    }

    private static string CreateOllamaRetryPrompt(string prompt, object schema)
    {
        var schemaJson = JsonSerializer.Serialize(schema, JsonOptions);
        return $"""
            Poprzednia odpowiedz nie byla kompletnym pojedynczym obiektem JSON.
            Zwroc teraz wylacznie jeden kompletny obiekt JSON. Bez markdown, bez komentarzy, bez tekstu poza JSON.

            Zadanie:
            {prompt}

            JSON schema:
            {schemaJson}
            """;
    }

    private static bool LooksLikeCompleteJsonObject(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Dictionary<string, object?> CreateOpenAIRequestBody(
        ModelJsonRequest request,
        string model,
        string schemaName)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["input"] = request.Prompt,
            ["store"] = false,
            ["max_output_tokens"] = request.MaxOutputTokens,
            ["text"] = new
            {
                format = new
                {
                    type = "json_schema",
                    name = schemaName,
                    strict = true,
                    schema = request.Schema
                }
            }
        };

        var reasoningEffort = ResolveOpenAIReasoningEffort(request.Options);
        if (!string.IsNullOrWhiteSpace(reasoningEffort)
            && !reasoningEffort.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            body["reasoning"] = new { effort = reasoningEffort };
        }

        return body;
    }

    private static ModelJsonResponse ParseOpenAIResponse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var status = ReadString(root, "status");
        if (!string.IsNullOrWhiteSpace(status)
            && !status.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"OpenAI nie zakonczyl odpowiedzi statusem completed. Status={status}; Body={responseBody}");
        }

        var text = ExtractOpenAIText(root);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"OpenAI nie zwrocilo tekstu w odpowiedzi Responses API. Body={responseBody}");
        }

        var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;
        return new ModelJsonResponse
        {
            Model = ReadString(root, "model"),
            ResponseId = ReadString(root, "id"),
            RawText = text,
            InputTokens = ReadInt(usage, "input_tokens"),
            OutputTokens = ReadInt(usage, "output_tokens"),
            TotalTokens = ReadInt(usage, "total_tokens")
        };
    }

    private static string ExtractOpenAIText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static string ResolveProvider(ShortGeneratorOptions options)
    {
        var configured = FirstNonEmpty(
            options.ModelProvider,
            Environment.GetEnvironmentVariable("TIKTOK_MODEL_PROVIDER"),
            Environment.GetEnvironmentVariable("MODEL_PROVIDER"));
        if (string.IsNullOrWhiteSpace(configured)
            || configured.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(ResolveOpenAIApiKey(options)) ? "ollama" : "openai";
        }

        return configured.Trim().ToLowerInvariant();
    }

    private static string ResolveOpenAIApiKey(ShortGeneratorOptions options)
    {
        return FirstNonEmpty(
            options.OpenAIApiKey,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.Machine));
    }

    private static string ResolveOpenAIBaseUrl(ShortGeneratorOptions options)
    {
        return FirstNonEmpty(
            options.OpenAIBaseUrl,
            Environment.GetEnvironmentVariable("OPENAI_BASE_URL"),
            "https://api.openai.com/v1");
    }

    private static string ResolveOpenAIModel(ShortGeneratorOptions options)
    {
        return FirstNonEmpty(
            options.OpenAIModel,
            Environment.GetEnvironmentVariable("OPENAI_MODEL"),
            "gpt-5.6-terra");
    }

    private static string ResolveOpenAIReasoningEffort(ShortGeneratorOptions options)
    {
        return FirstNonEmpty(
            options.OpenAIReasoningEffort,
            Environment.GetEnvironmentVariable("OPENAI_REASONING_EFFORT"),
            "medium");
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string CreateSchemaName(string stageName)
    {
        var normalized = Regex.Replace(stageName.ToLowerInvariant(), "[^a-z0-9_]+", "_");
        normalized = Regex.Replace(normalized, "_+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "model_output" : normalized;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private sealed record HttpSendResult(
        int StatusCode,
        string ResponseBody,
        long ElapsedMilliseconds,
        int Attempts);

    private sealed class OllamaGenerateResponse
    {
        public string Response { get; set; } = string.Empty;
    }
}
