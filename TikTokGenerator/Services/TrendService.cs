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
            .Select((topic, index) => new Trend(
                Rank: index + 1,
                Title: topic.Title,
                Country: country,
                Category: category,
                Source: "MVP offline seed",
                SourceText: topic.SourceText,
                SourceUrl: topic.SourceUrl,
                DiscoveredAt: now))
            .ToList();

        return Task.FromResult<IReadOnlyList<Trend>>(topics);
    }

    private static IReadOnlyList<OfflineTopic> CreateOfflineTopics(string country, string category)
    {
        if (category.Equals("Technologia", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                CreateTopic("Nowa funkcja telefonu, ktora oszczedza czas", category),
                CreateTopic("Aplikacja AI, ktora robi notatki z nagran", category),
                CreateTopic("Ciekawostka technologiczna: telefon jako skaner 3D", category),
                CreateTopic("Trik w Windows, ktory malo kto zna", category),
                CreateTopic("Najprostszy sposob na bezpieczne hasla", category)
            ];
        }

        if (category.Equals("Biznes", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                CreateTopic("Prosty nawyk finansowy na start miesiaca", category),
                CreateTopic("Dlaczego male firmy automatyzuja obsluge klienta", category),
                CreateTopic("Pomysl na mikroprodukt cyfrowy", category),
                CreateTopic("Bledy przy pierwszej kampanii reklamowej", category),
                CreateTopic("Jak opisac oferte w 15 sekund", category)
            ];
        }

        if (category.Equals("Lifestyle", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                CreateTopic("Poranny rytual, ktory zmienia tempo dnia", category),
                CreateTopic("Minimalizm w aplikacjach na telefonie", category),
                CreateTopic("Szybki reset biurka po pracy", category),
                CreateTopic("Jedna rzecz, ktora poprawia sen", category),
                CreateTopic("Jak zaplanowac tydzien bez przeciazenia", category)
            ];
        }

        return
        [
            CreateTopic($"Popularny temat w kategorii {category}", category),
            CreateTopic($"Nowy trend w kraju {country}", category),
            CreateTopic("Krotka ciekawostka do formatu short", category),
            CreateTopic("Lista trzech rzeczy, ktore warto wiedziec", category),
            CreateTopic("Mit kontra fakt w prostym wideo", category)
        ];
    }

    private static OfflineTopic CreateTopic(string title, string category)
    {
        return new OfflineTopic(
            title,
            $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Scenariusz powinien byc krotki, konkretny i oparty tylko na tych informacjach. Pokaz widzowi prosty problem, praktyczna korzysc i jedno zdanie podsumowania. Nie dodawaj aktualnych danych, statystyk ani nazw firm, ktorych tu nie ma.
            """,
            "offline://mvp-seed");
    }

    private sealed record OfflineTopic(string Title, string SourceText, string SourceUrl);
}
