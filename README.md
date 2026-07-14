# TikTokGenerator

Prosty MVP desktopowy w C# WinForms na .NET 10.

## Pipeline MVP

Przycisk `Wygeneruj short` wykonuje teraz jeden przeplyw:

1. Ollama + `qwen3:4b` tworzy scenariusz JSON na podstawie materialu zrodlowego.
2. Scenariusz jest normalizowany i walidowany: osobno `voiceOver`, `onScreenText`, `visualDescription` i `searchPhrase`.
3. Piper TTS tworzy osobne pliki WAV dla hooka, scen i zakonczenia.
4. Pexels API pobiera pionowe klipy MP4 dla fraz `searchPhrase`.
5. Program mierzy dlugosc WAV przez `ffprobe`.
6. Program tworzy napisy jako przezroczyste obrazy PNG z `onScreenText`.
7. FFmpeg sklada segmenty 1080x1920, 30 FPS, H.264/AAC do `short.mp4`.

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
debug/ollama-http-response.json
debug/ollama-script-raw.txt
debug/script-normalized.json
debug/script-quality-report.json
debug/script-analysis.json
debug/voice-segments.json
debug/voice-analysis.json
debug/pexels-clips.json
debug/clip-analysis.json
debug/pexels-search-*.json
debug/pexels-selection-*.json
debug/short-diagnostics.json
```

Jesli generowanie zakonczy sie bledem, okno programu pokaze sciezke do `debug.log`.

W folderze `audio/` kazdy segment ma dodatkowo:

```text
NN_name.txt         - tekst czytany przez lektora
NN_name.screen.txt  - krotki napis ekranowy
NN_name.visual.txt  - opis kadru / intencja wizualna
```

`script-quality-report.json` pokazuje ostrzezenia i automatyczne poprawki, na przyklad:

- lektor brzmial jak opis sceny,
- model podal bledny klucz JSON dla `searchPhrase`,
- model dodal niepotwierdzona statystyke,
- napis ekranowy byl zbyt dlugi,
- brakowalo osobnego opisu wizualnego.

`script-analysis.json`, `voice-analysis.json`, `clip-analysis.json` i `short-diagnostics.json` sa raportami diagnostycznymi dla kolejnych etapow. Kazdy raport ma:

- `summary` z liczba scen, segmentow, ostrzezen, bledow, czasem audio i pokryciem slow ze zrodla,
- `script` z keywordami zrodla i scenariusza,
- `segments` z lektorem, napisem ekranowym, opisem wizualnym, fraza Pexels, czasem audio i metrykami,
- `issues` z kodem problemu, etapem, segmentem, dowodem i rekomendacja naprawy.

Najwazniejsze kody problemow:

- `unsupported_claim_phrase` - lektor zawiera mocna obietnice, ktorej nie ma w materiale zrodlowym,
- `unsupported_number` - liczba lub czas nie wystepuje w zrodle,
- `missing_action_verb` - scena nie daje widzowi konkretnego kroku,
- `no_source_keyword_overlap` - scena slabo wynika z materialu zrodlowego,
- `long_voice_segment` albo `total_duration_over_target` - lektor jest zbyt dlugi,
- `duplicate_clip_url` - ten sam klip Pexels trafil do wiecej niz jednego segmentu,
- `generic_visual_description` albo `generic_search_phrase` - opis obrazu/fraza stockowa sa zbyt ogolne.

`pexels-selection-*.json` pokazuje kandydatow z Pexels w kolejnosci rankingu, duplikaty URL-i i wybrany plik MP4. Wybor klipu preferuje trafnosc rankingu Pexels oraz unikanie duplikatow przed dlugoscia filmu.

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
- brak dodawania faktow spoza zrodla,
- maksymalnie okolo 25 sekund,
- zwrot samego JSON-a,
- rozdzielenie lektora, napisu ekranowego, opisu wizualnego i frazy Pexels,
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
|   +-- VideoProject.cs
+-- Tools/
|   +-- Piper/
+-- Output/
```
