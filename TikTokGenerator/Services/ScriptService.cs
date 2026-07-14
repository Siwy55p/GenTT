using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class ScriptService
{
    private readonly HttpClient _httpClient;

    public ScriptService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<string> GenerateScriptAsync(Trend trend, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var script = $"""
            Hook: {trend.Title}

            1. Zacznij od konkretu, ktory widz rozumie w pierwszych dwoch sekundach.
            2. Pokaz prosty przyklad z zycia codziennego.
            3. Zakoncz jedna praktyczna wskazowka, ktora mozna od razu sprawdzic.

            CTA: Zapisz ten film, jesli chcesz wrocic do tematu pozniej.
            """;

        return Task.FromResult(script);
    }
}
