using TikTokGenerator.Models;

namespace TikTokGenerator.Services;

public sealed class TrendService
{
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
                Source: "Zestaw startowy offline",
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
            CreateTopic($"Nowy temat startowy w kraju {country}", category),
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

        if (title.Contains("Nowa funkcja telefonu", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: funkcja skrotow lub automatyzacji w telefonie moze oszczedzic czas przy powtarzalnej czynnosci.
            Konkretne kroki: wybierz jedna czynnosc powtarzana codziennie, ustaw dla niej prosty skrot w telefonie, przetestuj skrot na jednej sytuacji.
            Korzysc dla widza: widz wie, jak zaczac od jednej automatyzacji zamiast szukac wielu aplikacji.
            Ograniczenia: nie podawaj nazw systemow, aplikacji ani obietnic ile minut da sie zaoszczedzic.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("Aplikacja AI", StringComparison.OrdinalIgnoreCase)
            && title.Contains("notatki", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: aplikacja AI do nagran moze pomoc szybciej znalezc decyzje i zadania, ale wynik trzeba sprawdzic.
            Konkretne kroki: wgraj jedno nagranie lub transkrypcje, popros o liste decyzji i zadan, porownaj wynik z najwazniejszym fragmentem nagrania.
            Korzysc dla widza: widz dostaje sposob na szybki przeglad spotkania bez zaufania w ciemno.
            Ograniczenia: nie obiecuj idealnej dokladnosci ani pracy bez internetu.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("skaner 3D", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: telefon moze posluzyc do prostego skanu 3D obiektu przez fotogrametrie, gdy aplikacja sklada zdjecia lub nagranie z kilku stron w model.
            Konkretne kroki: wybierz maly nieruchomy obiekt z wyrazna faktura, obejdz go telefonem z kilku stron, sprawdz w aplikacji czy model nie ma brakujacych fragmentow.
            Korzysc dla widza: widz rozumie, ze skan 3D telefonem zaczyna sie od stabilnego obiektu, dobrego swiatla i obejscia obiektu kamera.
            Ograniczenia: nie obiecuj dokladnosci technicznej ani profesjonalnego skanu.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("Trik w Windows", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: historia schowka w Windows pomaga wrocic do niedawno skopiowanego tekstu bez ponownego szukania.
            Konkretne kroki: wlacz historie schowka, skopiuj dwa rozne fragmenty tekstu, wybierz potrzebny fragment z panelu historii.
            Korzysc dla widza: widz wie, jak odzyskac ostatnio kopiowany tekst w prostym zadaniu.
            Ograniczenia: nie obiecuj odzyskania rzeczy sprzed wlaczenia historii schowka.
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

        if (title.Contains("nawyk finansowy", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: jeden prosty przeglad finansow na start miesiaca pomaga szybciej zobaczyc, co wymaga uwagi.
            Konkretne kroki: sprawdz saldo i stale oplaty, wybierz jeden koszt do obserwacji, zapisz jedna decyzje finansowa na ten miesiac.
            Korzysc dla widza: widz zaczyna miesiac od jednego konkretnego przegladu zamiast ogolnego stresu.
            Ograniczenia: nie dawaj porad inwestycyjnych ani obietnic oszczednosci.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("automatyzuja obsluge klienta", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: mala firma moze zaczac automatyzacje obslugi klienta od powtarzalnych pytan, a nie od duzego systemu.
            Konkretne kroki: wypisz trzy najczestsze pytania klientow, przygotuj krotka odpowiedz szablonowa, okresl kiedy rozmowe ma przejac czlowiek.
            Korzysc dla widza: widz widzi prosty start automatyzacji bez utraty kontroli nad rozmowa.
            Ograniczenia: nie obiecuj zastapienia calej obslugi klienta.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("mikroprodukt", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: mikroprodukt cyfrowy powinien rozwiazywac jeden maly problem, ktory odbiorca rozumie od razu.
            Konkretne kroki: wybierz jeden powtarzalny problem odbiorcy, zamien rozwiazanie w checkliste lub szablon, pokaz prosty przyklad uzycia.
            Korzysc dla widza: widz wie, jak zawezic pomysl do malego produktu zamiast budowac duzy kurs.
            Ograniczenia: nie obiecuj sprzedazy ani wyniku finansowego.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("pierwszej kampanii reklamowej", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: pierwsza kampania reklamowa latwo traci sens, gdy promuje zbyt wiele komunikatow naraz.
            Konkretne kroki: wybierz jeden cel kampanii, napisz jeden glowny komunikat, sprawdz czy reklama prowadzi do jednej akcji.
            Korzysc dla widza: widz umie uproscic pierwsza kampanie przed uruchomieniem.
            Ograniczenia: nie podawaj stawek, procentow ani gwarancji wyniku.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("opisac oferte", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: dobra krotka oferta mowi komu pomaga, jaki problem rozwiazuje i jaki jest pierwszy krok.
            Konkretne kroki: nazw odbiorce, nazw jeden problem, zakoncz oferta jednym konkretnym wezwaniem do dzialania.
            Korzysc dla widza: widz potrafi skrocic opis oferty do jasnego komunikatu.
            Ograniczenia: nie dodawaj wynikow sprzedazy ani obietnic bez dowodu.
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

        if (title.Contains("poprawia sen", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: spokojniejszy wieczor latwiej zaczac od jednego malego sygnalu odciecia dnia.
            Konkretne kroki: wybierz jedna czynnosc bez ekranu, odloz telefon poza zasieg reki, przygotuj jedna rzecz na rano.
            Korzysc dla widza: widz ma prosty wieczorny krok zamiast dlugiej rutyny.
            Ograniczenia: nie skladaj obietnic medycznych ani gwarancji lepszego snu.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        if (title.Contains("zaplanowac tydzien", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            Temat roboczy: {title}
            Kategoria: {category}
            Praktyczna teza: plan tygodnia jest latwiejszy, gdy najpierw oddziela sie zadania konieczne od opcjonalnych.
            Konkretne kroki: wypisz wszystkie zadania tygodnia, oznacz trzy rzeczy konieczne, zostaw miejsce na nieprzewidziane sprawy.
            Korzysc dla widza: widz zaczyna tydzien z jasnym wyborem najwazniejszych zadan.
            Ograniczenia: nie obiecuj braku stresu ani idealnej kontroli tygodnia.
            Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
            """;
        }

        return $"""
        Temat roboczy: {title}
        Kategoria: {category}
        Praktyczna teza: temat wymaga wybrania jednego konkretnego problemu i jednego prostego pierwszego kroku.
        Konkretne kroki: nazw problem odbiorcy, wybierz najmniejszy mozliwy krok, sprawdz widoczny rezultat po wykonaniu kroku.
        Korzysc dla widza: po obejrzeniu widz wie, jaki pierwszy krok moze wykonac bez dodatkowych narzedzi.
        Ograniczenia: nie dodawaj danych, nazw firm ani obietnic, ktorych nie ma w tym materiale.
        Nie dodawaj statystyk, procentow, nazw firm ani aktualnych danych.
        """;
    }

    private sealed record OfflineTopic(string Title, string SourceText, string SourceUrl);
}
