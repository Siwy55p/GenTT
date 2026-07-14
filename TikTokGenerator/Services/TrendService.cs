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
        var sourceText = CreateSourceText(title, category);
        return new OfflineTopic(
            title,
            sourceText,
            "offline://mvp-seed");
    }

    private static string CreateSourceText(string title, string category)
    {
        if (title.Contains("Poranny rytual", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: jedna minuta planowania rano moze ograniczyc chaos na starcie dnia.
            Konkretne kroki: zapisz jeden priorytet, jedno male zadanie do zrobienia od razu oraz jedna rzecz, ktorej dzis swiadomie nie robisz.
            Korzysc dla widza: zamiast zaczynac dzien od niepewnosci, widz ma prosty plan startowy.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("Minimalizm w aplikacjach", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: mniej ikon i mniej powiadomien pomaga szybciej znalezc to, co naprawde potrzebne.
            Konkretne kroki: usun z ekranu glownego aplikacje nieuzywane codziennie, wylacz niepilne powiadomienia, zostaw na pierwszym ekranie tylko najwazniejsze narzedzia.
            Korzysc dla widza: telefon staje sie prostszy w obsludze i mniej rozprasza.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("hasla", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: bezpieczniejsze hasla zaczynaja sie od menedzera hasel i unikalnego hasla dla kazdego konta.
            Konkretne kroki: wybierz menedzer hasel, zmien najwazniejsze haslo, wlacz dwustopniowe logowanie tam, gdzie jest dostepne.
            Korzysc dla widza: mniej powtarzania tych samych hasel i latwiejsze porzadkowanie kont.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("biurka", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: szybki reset biurka po pracy pomaga zamknac dzien i latwiej zaczac kolejny.
            Konkretne kroki: wyrzuc smieci, odloz rzeczy na miejsce, zostaw jedna kartke z pierwszym zadaniem na jutro.
            Korzysc dla widza: porzadek na starcie kolejnej sesji pracy.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        return $"""
        Temat roboczy: {title}
        Kategoria: {category}
        Scenariusz powinien byc krotki, konkretny i oparty tylko na tych informacjach.
        Struktura praktyczna: pokaz prosty problem, podaj jeden maly krok, pokaz jak sprawdzic efekt.
        Korzysc dla widza: po obejrzeniu ma wiedziec, co moze zrobic od razu.
        Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
        """;
    }

    private sealed record OfflineTopic(string Title, string SourceText, string SourceUrl);
}
