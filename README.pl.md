# PhotoManager

*[English](README.md) · **Polski***

Lekkie narzędzie dla Windows, które działa w tle, wykrywa podłączenie aparatu, telefonu lub
karty pamięci i proponuje proste operacje na zdjęciach — import z organizacją wg daty,
deduplikację i podgląd — **bez ciężkiego oprogramowania do katalogowania**.

Zaprojektowane pod szybki, wygodny import z lustrzanek/bezlusterkowców (testowane na Sony A7 III,
tryb Mass Storage i MTP) oraz czytników wielogniazdowych.

## Funkcje

- **Wykrywanie sprzętu w tle** — dyski wymienne (litera + folder `DCIM`) oraz aparaty/telefony
  w trybie MTP/PTP (przez Windows Portable Devices). Reakcja na `WM_DEVICECHANGE`.
- **Szybki import** z organizacją wg daty EXIF do wzorca `RRRR/RRRR-MM-DD` (konfigurowalny).
- **Deduplikacja przez porównanie z fizyczną biblioteką** — każdy plik dopasowywany do swojego
  miejsca w bibliotece (folder wg daty + nazwa + rozmiar), bez rejestru. Plik skasowany z biblioteki
  wraca jako nowy; opcjonalna weryfikacja zawartości (skrótem) chroni przed uszkodzoną kopią.
- **Okno podglądu** z listą zdjęć, **miniaturą** i **szczegółami metadanych** (aparat, obiektyw,
  ISO, przysłona, czas, ogniskowa, wymiary, natywna rozdzielczość RAW, pola Sony). Dwuklik
  otwiera zdjęcie w skojarzonym programie.
- **Obsługa wielu kart naraz** — jedno okno z listą źródeł; przełączasz się między nośnikami.
- **Filtr rozszerzeń** przy imporcie (np. tylko `.arw`).
- **Kopiuj / Przenieś** z weryfikacją skrótu; przy przenoszeniu źródło kasowane dopiero po
  pewnej kopii. Operacje w pełni anulowalne.
- **Dyski przenośne** — czytelny komunikat, gdy biblioteka jest odłączona; odnajdywanie biblioteki
  po numerze seryjnym woluminu, nawet po zmianie litery dysku.
- **Przenoszenie biblioteki** między katalogami / na dysk przenośny.
- **Aplikacja w zasobniku** z własną ikoną, autostartem i `config.json` (profile per urządzenie).

## Pobierz

Najnowszą **samodzielną** wersję pobierzesz ze strony
[Releases](https://github.com/bartoszwarzocha/PhotoManager/releases) — rozpakuj i uruchom
`PhotoManager.App.exe`. Nie wymaga instalacji .NET. Interfejs PL/EN.

## Wymagania

- Windows 10/11
- Do budowania ze źródeł: .NET SDK 10

## Budowanie i uruchomienie

```powershell
# Uruchomienie aplikacji (tacka systemowa)
dotnet run --project src/PhotoManager.App
```

Aplikacja chowa się do zasobnika. Prawy klik na ikonie → **Ustawienia…** i wskaż domyślny folder
biblioteki. Po podłączeniu karty z folderem `DCIM` otworzy się okno podglądu importu.

### Instalacja / aktualizacja lokalna

```powershell
pwsh -File install.ps1
```

Publikuje aplikację, wgrywa do `%LOCALAPPDATA%\Programs\PhotoManager`, tworzy skróty (menu Start
i pulpit), dodaje wpis autostartu i uruchamia. Konfiguracja: `%APPDATA%\PhotoManager\config.json`.

## Struktura

```
src/
  PhotoManager.Core/            # logika niezależna od GUI
    Devices/                    # wykrywanie urządzeń, woluminy (serial)
    Metadata/                   # data i szczegóły EXIF/MakerNotes
    Import/                     # silnik importu, deduplikacja (porównanie fizyczne), przenoszenie biblioteki
    Config/                     # config.json, profile urządzeń
  PhotoManager.App/             # aplikacja WPF w zasobniku
  PhotoManager.ConsoleHarness/  # tester CLI (wykrywanie, import, diagnostyka metadanych)
install.ps1                     # publikacja + instalacja lokalna
```

## Tester konsolowy (diagnostyka)

```powershell
# Podgląd wykrywania urządzeń
dotnet run --project src/PhotoManager.ConsoleHarness

# Import z linii poleceń (--move, --dry-run)
dotnet run --project src/PhotoManager.ConsoleHarness -- import D:\DCIM E:\Zdjecia --dry-run

# Zrzut wszystkich metadanych / sformatowanych szczegółów pliku
dotnet run --project src/PhotoManager.ConsoleHarness -- dumpmeta "D:\DCIM\100MSDCF\DSC00190.ARW"
dotnet run --project src/PhotoManager.ConsoleHarness -- details  "D:\DCIM\100MSDCF\DSC00190.ARW"
```

## Technologia

C# / .NET 10, WPF. Zależności: [MediaDevices](https://www.nuget.org/packages/MediaDevices)
(MTP/WPD), [MetadataExtractor](https://www.nuget.org/packages/MetadataExtractor) (EXIF).

## Licencja

[MIT](LICENSE) © Bartosz Warzocha
