# TikTokGenerator

Prosty MVP desktopowy w C# WinForms na .NET 10.

## Co jest w pierwszej wersji

- Okno WinForms z krajem, kategoria, lista trendow i polem wybranego tematu.
- Dwa glowne przyciski: `Znajdz popularne tematy` i `Wygeneruj short`.
- Podzial logiki na osobne klasy w `Services`.
- Modele `Trend` i `VideoProject`.
- Zapis metadanych projektu do JSON.
- Render MP4 przez FFmpeg, jezeli `ffmpeg.exe` jest dostepny.

## Uruchomienie

```powershell
dotnet build
dotnet run --project .\TikTokGenerator\TikTokGenerator.csproj
```

Mozesz tez otworzyc `TikTokGenerator.slnx` w Visual Studio.

## FFmpeg

Aby przycisk `Wygeneruj short` mogl zapisac plik MP4, dodaj:

```text
TikTokGenerator/Tools/ffmpeg.exe
```

Alternatywnie zainstaluj FFmpeg globalnie tak, aby komenda `ffmpeg` byla dostepna w PATH.

## Playwright

Pakiet `Microsoft.Playwright` jest juz dodany do projektu. Gdy podlaczysz realne pobieranie z TikTok Creative Center, po buildzie zainstaluj przegladarki:

```powershell
.\TikTokGenerator\bin\Debug\net10.0-windows\playwright.ps1 install
```

Aktualnie `TrendService` dziala w trybie offline i zwraca przykladowe tematy, zeby MVP bylo uruchamialne bez logowania, kluczy API i dodatkowej konfiguracji.

## Struktura

```text
TikTokGenerator/
+-- Forms/
|   +-- MainForm.cs
|   +-- MainForm.Designer.cs
+-- Services/
|   +-- TrendService.cs
|   +-- ScriptService.cs
|   +-- VoiceService.cs
|   +-- VideoService.cs
|   +-- StockVideoService.cs
+-- Models/
|   +-- Trend.cs
|   +-- VideoProject.cs
+-- Tools/
+-- Output/
```
