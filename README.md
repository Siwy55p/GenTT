# TikTokGenerator

Prosty MVP desktopowy w C# WinForms na .NET 10.

## Pipeline MVP

Przycisk `Wygeneruj short` wykonuje teraz jeden przeplyw:

1. Ollama + `qwen3:4b` tworzy scenariusz JSON na podstawie materialu zrodlowego.
2. Piper TTS tworzy osobne pliki WAV dla hooka, scen i zakonczenia.
3. Pexels API pobiera pionowe klipy MP4 dla fraz `searchPhrase`.
4. Program mierzy dlugosc WAV przez `ffprobe`.
5. Program tworzy napisy jako przezroczyste obrazy PNG.
6. FFmpeg sklada segmenty 1080x1920, 30 FPS, H.264/AAC do `short.mp4`.

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
debug/voice-segments.json
debug/pexels-clips.json
debug/pexels-search-*.json
```

Jesli generowanie zakonczy sie bledem, okno programu pokaze sciezke do `debug.log`.

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
- zwrot samego JSON-a.

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
