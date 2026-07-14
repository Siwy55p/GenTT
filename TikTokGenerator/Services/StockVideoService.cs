using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class StockVideoService
{
    private readonly HttpClient _httpClient;

    public StockVideoService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<string> FindBackgroundAsync(Trend trend, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var description = trend.Category switch
        {
            "Technologia" => "ciemne tlo technologiczne z delikatna siatka i jasnym tekstem",
            "Biznes" => "jasne tlo biurowe z kontrastowym paskiem na napisy",
            "Lifestyle" => "spokojne tlo domowe z czytelnym centrum kadru",
            _ => "neutralne tlo pod pionowy film short"
        };

        return Task.FromResult(description);
    }
}
