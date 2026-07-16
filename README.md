# TikTokGenerator

Prosty MVP desktopowy w C# WinForms na .NET 10.

## Pipeline

Przycisk `Wygeneruj short` wykonuje teraz wieloetapowy przeplyw jakosciowy:

1. Formularz zbiera temat, material zrodlowy i `Brief JSON` z odbiorca, problemem, celem, tonem i limitem czasu.
2. Ollama osobno analizuje zrodlo: teza, fakty `F1...`, kroki, przyklady, ograniczenia, ryzykowne twierdzenia i najprzydatniejszy fragment.
3. Ollama proponuje trzy kierunki scenariusza, ocenia je i wybiera najlepszy.
4. Ollama pisze scenariusz tresciowy z rolami scen, `sourceFactIds`, `newInformation`, `onScreenEmphasis` i budzetem slow. Ten etap nie tworzy jeszcze planu wizualnego.
5. Osobne wywolanie Ollamy recenzuje scenariusz merytorycznie: powtorzenia, oczywiste porady, zgodnosc ze zrodlem, obietnica hooka, wykonalnosc i wartosc dla briefu.
6. Program sprawdza budzet slow, tworzy TTS w Piper, mierzy WAV przez `ffprobe` i automatycznie skraca scenariusz oraz regeneruje TTS, jezeli przekroczy limit z briefu.
7. Dopiero po tresci powstaje osobny plan wizualny: co widac, akcja osoby, glowny obiekt, typ kadru, ruch, rezultat, zakazy i kilka zapytan Pexels.
8. Pexels API pobiera kandydatow z kilku zapytan na segment, punktuje ranking, orientacje, czas, duplikaty, zgodnosc z opisem i kare z `avoidVisuals`.
9. Bramka jakosci liczy wynik 100 pkt i zatrzymuje render przy bledach krytycznych albo wyniku ponizej 80.
10. FFmpeg sklada segmenty 1080x1920, 30 FPS, H.264/AAC i naklada dynamiczne napisy ASS w bezpiecznej strefie.

Nie ma jeszcze muzyki, publikowania ani generowania wideo przez AI.

## Uruchomienie

```powershell
dotnet build
dotnet run --project .\TikTokGenerator\TikTokGenerator.csproj
```

Mozesz tez otworzyc `TikTokGenerator.slnx` w Visual Studio.

## Testy

```powershell
dotnet test TikTokGenerator.slnx
```

Testy sprawdzaja miedzy innymi parser odpowiedzi Ollamy. Jesli model zwroci ucity albo niepoprawny JSON, aplikacja nie powinna przerywac generowania tylko zapisac debug i zbudowac bezpieczny scenariusz fallbackowy z materialu zrodlowego.

## Debug

Kazde generowanie tworzy osobny katalog w `Output`. W nim znajduje sie folder:

```text
debug/
```

Najwazniejsze pliki:

```text
debug/debug.log
debug/ollama-source-analysis-http-response.json
debug/ollama-source-analysis-raw.txt
debug/source-analysis.json
debug/ollama-concept-selection-http-response.json
debug/concept-selection.json
debug/ollama-script-http-response.json
debug/ollama-script-raw.txt
debug/script-normalized.json
debug/script-quality-report.json
debug/script-analysis.json
debug/content-review.json
debug/script-word-budget.json
debug/script-after-tts-shorten.json
debug/voice-segments.json
debug/voice-analysis.json
debug/voice-segments-shortened.json
debug/voice-analysis-shortened.json
debug/visual-plan.json
debug/script-with-visual-plan.json
debug/pexels-clips.json
debug/clip-analysis.json
debug/pexels-search-*-q*.json
debug/pexels-selection-*.json
debug/quality-gate.json
debug/short-diagnostics.json
```

Jesli generowanie zakonczy sie bledem, okno programu pokaze sciezke do `debug.log`.

W folderze `audio/` kazdy segment ma dodatkowo:

```text
NN_name.txt         - tekst czytany przez lektora
NN_name.screen.txt  - krotki napis ekranowy
NN_name.visual.txt  - opis kadru / intencja wizualna
```

`source-analysis.json` pokazuje fakty i kroki wydobyte ze zrodla. `concept-selection.json` pokazuje trzy kierunki, ich punktacje oraz wybrany kierunek.

`script-quality-report.json` pokazuje ostrzezenia i automatyczne poprawki, na przyklad:

- lektor brzmial jak opis sceny,
- model podal bledny klucz JSON dla `searchPhrase`,
- model dodal niepotwierdzona statystyke,
- napis ekranowy byl zbyt dlugi,
- brakowalo `sourceFactIds`,
- brakowalo `newInformation`.

`content-review.json` jest osobna recenzja merytoryczna. Nie pisze filmu od nowa, tylko wskazuje powtorzenia, oczywiste porady, problemy ze zrodlem, niespelniona obietnice, slaba wykonalnosc i sugerowane poprawki.

`visual-plan.json` powstaje po tresci i opisuje obraz dla segmentow: widoczna zawartosc, akcja osoby, glowny obiekt, kadr, ruch, rezultat, `avoidVisuals` i kilka zapytan Pexels.

`quality-gate.json` zawiera punktacje:

- zgodnosc wszystkich twierdzen ze zrodlem: 25,
- uzytecznosc dla wskazanego odbiorcy: 20,
- konkretnosc i wykonalnosc: 15,
- brak powtorzen i logiczna progresja: 10,
- hook zgodny z payoffem: 10,
- dopasowanie wizualne: 10,
- czytelnosc i czas: 10.

Render jest zatrzymywany, gdy wynik jest ponizej 80/100, pojawia sie niepotwierdzone twierdzenie, scena nie ma nowej informacji, brakuje przykladu/demonstracji/rezultatu, TTS przekracza limit albo recenzent merytoryczny wykryje blad krytyczny.

`script-analysis.json`, `voice-analysis.json`, `clip-analysis.json` i `short-diagnostics.json` sa raportami diagnostycznymi dla kolejnych etapow. Kazdy raport ma:

- `summary` z liczba scen, segmentow, ostrzezen, bledow, czasem audio i pokryciem slow ze zrodla,
- `script` z keywordami zrodla i scenariusza,
- `segments` z rola, lektorem, `sourceFactIds`, `newInformation`, napisem ekranowym, opisem wizualnym, fraza Pexels, czasem audio i metrykami,
- `issues` z kodem problemu, etapem, segmentem, dowodem i rekomendacja naprawy.

Najwazniejsze kody problemow:

- `unsupported_claim_phrase` - lektor zawiera mocna obietnice, ktorej nie ma w materiale zrodlowym,
- `unsupported_number` - liczba lub czas nie wystepuje w zrodle,
- `missing_action_verb` - scena nie daje widzowi konkretnego kroku,
- `no_source_keyword_overlap` - scena slabo wynika z materialu zrodlowego,
- `long_voice_segment` albo `total_duration_over_target` - lektor jest zbyt dlugi,
- `duplicate_clip_url` - ten sam klip Pexels trafil do wiecej niz jednego segmentu,
- `generic_visual_description` albo `generic_search_phrase` - opis obrazu/fraza stockowa sa zbyt ogolne.

`pexels-selection-*.json` pokazuje kandydatow z Pexels z wielu zapytan, miniatury, duplikaty URL-i, kare za `avoidVisuals`, lokalny wynik dopasowania i wybrany plik MP4.

## Wymagane narzedzia

### FFmpeg

FFmpeg jest juz skonfigurowany lokalnie w:

```text
TikTokGenerator/Tools/ffmpeg.exe
TikTokGenerator/Tools/ffprobe.exe
TikTokGenerator/Tools/ffplay.exe
```

Projekt kopiuje te pliki do katalogu builda.

### Ollama

Zainstaluj Ollama i pobierz model:

```powershell
ollama pull qwen3:4b
```

Program laczy sie z lokalnym API:

```text
http://localhost:11434/api/generate
```

### Piper TTS

Dodaj `piper.exe` i polski model glosu do:

```text
TikTokGenerator/Tools/Piper/
```

Przykladowy uklad:

```text
TikTokGenerator/Tools/Piper/piper.exe
TikTokGenerator/Tools/Piper/pl_PL-voice.onnx
TikTokGenerator/Tools/Piper/pl_PL-voice.onnx.json
```

Mozesz tez ustawic sciezki globalnie:

```powershell
setx PIPER_EXE "C:\sciezka\do\piper.exe"
setx PIPER_MODEL "C:\sciezka\do\polski-model.onnx"
```

Sprawdz licencje konkretnego modelu glosu w jego pliku `MODEL_CARD` lub dokumentacji modelu.

### Pexels API

W oknie programu mozna wpisac klucz Pexels API. Alternatywnie ustaw zmienna:

```powershell
setx PEXELS_API_KEY "twoj_klucz_pexels"
```

Po zmianie zmiennych srodowiskowych otworz nowe okno terminala lub uruchom ponownie Visual Studio.

## Material zrodlowy

Scenariusz nie jest generowany tylko z tytulu. Do generatora trafia:

```csharp
public class SelectedTopic
{
    public string Title { get; set; }
    public string SourceText { get; set; }
    public string SourceUrl { get; set; }
}
```

Prompt wymusza:

- pisanie wylacznie na podstawie `SourceText`,
- osobna analiza zrodla przed scenariuszem,
- trzy koncepcje z punktacja przed wyborem kierunku,
- brak dodawania faktow spoza zrodla,
- limit czasu z `Brief JSON`,
- JSON Schema dla odpowiedzi Ollamy,
- role scen, `sourceFactIds`, `newInformation` i `onScreenEmphasis`,
- brak wymuszania minimum trzech scen,
- osobna recenzja merytoryczna,
- osobny plan wizualny po tresci,
- zakaz uzywania w lektorze opisow typu `pierwsza scena`, `widzimy`, `kamera`.

## Struktura

```text
TikTokGenerator/
+-- Forms/
|   +-- MainForm.cs
|   +-- MainForm.Designer.cs
+-- Services/
|   +-- ShortGenerator.cs
|   +-- TrendService.cs
|   +-- ScriptService.cs
|   +-- VoiceService.cs
|   +-- StockVideoService.cs
|   +-- VideoService.cs
|   +-- ToolLocator.cs
+-- Models/
|   +-- SelectedTopic.cs
|   +-- ShortScript.cs
|   +-- VoiceSegment.cs
|   +-- DownloadedVideoClip.cs
|   +-- ShortGenerationProgress.cs
|   +-- ShortGeneratorOptions.cs
|   +-- Trend.cs
+-- Tools/
|   +-- Piper/
+-- Output/
```
