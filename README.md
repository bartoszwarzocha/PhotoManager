# PhotoManager

***English** · [Polski](README.pl.md)*

A lightweight Windows tool that runs in the background, detects when a camera, phone, or memory
card is connected, and offers simple photo operations — date-based import, deduplication, and
preview — **without heavy cataloging software**.

Built for fast, convenient importing from DSLR/mirrorless cameras (tested on a Sony A7 III, both
Mass Storage and MTP modes) and multi-slot card readers.

## Features

- **Background device detection** — removable drives (drive letter + `DCIM` folder) and cameras/
  phones in MTP/PTP mode (via Windows Portable Devices). Reacts to `WM_DEVICECHANGE`.
- **Fast import** with date-based organization from EXIF into a `YYYY/YYYY-MM-DD` pattern
  (configurable).
- **Deduplication by comparing against the physical library** — each file is matched to its place
  in the library (date folder + name + size), with no registry. A file deleted from the library
  correctly reappears as new; optional content verification (checksum) guards against corrupted copies.
- **Preview window** with a file list, **thumbnail**, and **metadata details** (camera, lens, ISO,
  aperture, shutter, focal length, dimensions, native RAW resolution, Sony fields). Double-click
  opens the photo in the associated application.
- **Multiple cards at once** — a single window with a source list; switch between media.
- **Extension filter** at import time (e.g. only `.arw`).
- **Copy / Move** with checksum verification; when moving, the source is deleted only after a
  verified copy. Operations are fully cancellable.
- **Removable drives** — a clear message when the library is disconnected; the library is located
  by volume serial number even if the drive letter changes.
- **Move the library** between folders / to a removable drive.
- **System tray app** with a custom icon, autostart, and `config.json` (per-device profiles).

> **Note:** the user interface is currently **Polish only** (localization is planned).

## Download

Grab the latest **self-contained** build from the
[Releases](https://github.com/bartoszwarzocha/PhotoManager/releases) page — unzip and run
`PhotoManager.App.exe`. No .NET installation required. UI in Polish/English.

## Requirements

- Windows 10/11
- To build from source: .NET SDK 10

## Build & run

```powershell
# Run the app (system tray)
dotnet run --project src/PhotoManager.App
```

The app minimizes to the tray. Right-click the icon → **Settings** and choose the default library
folder. When a card with a `DCIM` folder is connected, the import preview window opens.

### Local install / update

```powershell
pwsh -File install.ps1
```

Publishes the app, deploys it to `%LOCALAPPDATA%\Programs\PhotoManager`, creates shortcuts (Start
menu and Desktop), adds an autostart entry, and launches it. Config: `%APPDATA%\PhotoManager\config.json`.

## Project structure

```
src/
  PhotoManager.Core/            # GUI-independent logic
    Devices/                    # device detection, volumes (serial)
    Metadata/                   # EXIF/MakerNotes date and details
    Import/                     # import engine, dedup (physical comparison), library move
    Config/                     # config.json, device profiles
  PhotoManager.App/             # WPF system-tray application
  PhotoManager.ConsoleHarness/  # CLI harness (detection, import, metadata diagnostics)
install.ps1                     # publish + local install
```

## Console harness (diagnostics)

```powershell
# Watch device detection
dotnet run --project src/PhotoManager.ConsoleHarness

# Command-line import (--move, --dry-run)
dotnet run --project src/PhotoManager.ConsoleHarness -- import D:\DCIM E:\Photos --dry-run

# Dump all metadata / formatted details of a file
dotnet run --project src/PhotoManager.ConsoleHarness -- dumpmeta "D:\DCIM\100MSDCF\DSC00190.ARW"
dotnet run --project src/PhotoManager.ConsoleHarness -- details  "D:\DCIM\100MSDCF\DSC00190.ARW"
```

## Technology

C# / .NET 10, WPF. Dependencies: [MediaDevices](https://www.nuget.org/packages/MediaDevices)
(MTP/WPD), [MetadataExtractor](https://www.nuget.org/packages/MetadataExtractor) (EXIF).

## License

[MIT](LICENSE) © Bartosz Warzocha
