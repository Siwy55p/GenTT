using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class TrendService
{
    private readonly HttpClient _httpClient;

    public TrendService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<IReadOnlyList<Trend>> FindPopularTopicsAsync(
        string country,
        string category,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.Now;
        var topics = CreateOfflineTopics(country, category)
            .Select((title, index) => new Trend(
                Rank: index + 1,
                Title: title,
                Country: country,
                Category: category,
                Source: "MVP offline seed",
                DiscoveredAt: now))
            .ToList();

        return Task.FromResult<IReadOnlyList<Trend>>(topics);
    }

    private static IReadOnlyList<string> CreateOfflineTopics(string country, string category)
    {
        if (category.Equals("Technologia", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Nowa funkcja telefonu, ktora oszczedza czas",
                "Aplikacja AI, ktora robi notatki z nagran",
                "Ciekawostka technologiczna: telefon jako skaner 3D",
                "Trik w Windows, ktory malo kto zna",
                "Najprostszy sposob na bezpieczne hasla"
            ];
        }

        if (category.Equals("Biznes", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Prosty nawyk finansowy na start miesiaca",
                "Dlaczego male firmy automatyzuja obsluge klienta",
                "Pomysl na mikroprodukt cyfrowy",
                "Bledy przy pierwszej kampanii reklamowej",
                "Jak opisac oferte w 15 sekund"
            ];
        }

        if (category.Equals("Lifestyle", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Poranny rytual, ktory zmienia tempo dnia",
                "Minimalizm w aplikacjach na telefonie",
                "Szybki reset biurka po pracy",
                "Jedna rzecz, ktora poprawia sen",
                "Jak zaplanowac tydzien bez przeciazenia"
            ];
        }

        return
        [
            $"Popularny temat w kategorii {category}",
            $"Nowy trend w kraju {country}",
            "Krotka ciekawostka do formatu short",
            "Lista trzech rzeczy, ktore warto wiedziec",
            "Mit kontra fakt w prostym wideo"
        ];
    }
}
