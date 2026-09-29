# EXIF Batch Metadata Editor

A Windows desktop app (WPF, .NET 8) for viewing and batch-editing EXIF metadata across multiple JPEG files at once, including GPS latitude, longitude, and altitude.

## Features

- Load individual files or an entire folder of `.jpg`/`.jpeg` files, with thumbnail previews (orientation-corrected).
- Full raw EXIF tag editor: every tag on the selected file is listed and editable in place, including adding brand-new tags (by name or raw numeric ID) and removing existing ones.
- Dedicated support for GPS position: `GPSLatitude`/`GPSLongitude` edit as plain degrees/minutes/seconds, with `GPSLatitudeRef`/`GPSLongitudeRef`/`GPSAltitudeRef` as dropdowns instead of raw codes, and blank GPS fields show a grayed-out example of the expected format.
- Multi-file editing: select multiple files in the list and any tag edit, add, or removal in the grid is applied - and saved - to every selected file at once.
- Double-click a file to open it in your system's default image viewer.

## Installer

[`installer/output/`](installer/output/) has two files, both needed together:

- **`EBME-Setup-1.0.3.exe`** — the one to run/distribute. It's a thin wrapper with no payload of its own.
- **`EBME-Setup-1.0.3-core.exe`** — the real self-contained installer (built with [Inno Setup](https://jrsoftware.org/isinfo.php); no .NET installation needed on the target machine). Must sit in the same folder as the wrapper.

Run `EBME-Setup-1.0.3.exe`, follow the wizard, and it adds a Start Menu entry and an optional desktop shortcut.

(Why two files: a setup.exe can't reliably copy-and-relaunch itself to add custom switch handling — the running exe's own file is effectively locked, and antivirus heuristics tend to flag a "setup" binary copying itself into temp and executing it as dropper-like behavior. The wrapper instead launches the separate, statically-named core installer, which sidesteps both problems.)

Rebuild after a version bump with `installer\setup.iss` (the core) and `installer\wrapper.iss` (the wrapper) — compile both with Inno Setup's `ISCC.exe`, core first.

### Silent installation

The wrapper accepts a plain **`/s`** switch for a fully silent, unattended install:

```
EBME-Setup-1.0.3.exe /s
```

This is a convenience the wrapper adds — Inno Setup's own installers don't recognize `/s` natively, only `/SILENT`/`/VERYSILENT`. `/s` translates to those internally; any other arguments you pass alongside `/s` (e.g. `/DIR=`) are forwarded through as-is:

- `/DIR="C:\Some\Path"` — install to a specific folder instead of the default.
- `/TASKS="desktopicon"` (or `/TASKS="!desktopicon"`) — force the desktop shortcut on (or off).
- `/LOG="install.log"` — write an install log.

Uninstalling silently uses the generated uninstaller directly (it's a plain Inno Setup binary, so it takes the native switches): `"C:\Program Files\EXIF Batch Metadata Editor\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.

## Ready-to-run binary

The [`/bin`](bin/) folder in this repo has a pre-built, ready-to-run copy of the app (`ExifBatchEditor.exe` + its dependencies). It's the small "framework-dependent" build, so it requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) to already be installed on the machine running it — it will not run standalone on a machine with nothing installed. Download the folder's contents and run `ExifBatchEditor.exe`.

## Requirements

- Windows 10/11
- To build/run from source: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and, optionally, Visual Studio 2022 with the ".NET desktop development" workload.

## Building and running

Open `ExifBatchEditor.sln` in Visual Studio and press F5, or from a terminal:

```
dotnet run --project ExifBatchEditor.csproj
```

## Publishing a standalone build

This produces a single self-contained `.exe` that runs on any 64-bit Windows PC without .NET installed:

```
dotnet publish ExifBatchEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The output lands in `bin\Release\net8.0-windows\win-x64\publish\`. Copy the whole folder (not just the `.exe` - a few native WPF interop DLLs have to sit alongside it), then run `ExifBatchEditor.exe`.

## Dependencies

- [ExifLibNet](https://github.com/oozcitak/exiflibrary) for EXIF reading/writing, via NuGet.
