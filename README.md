# EXIF Batch Metadata Editor

A Windows desktop app (WPF, .NET 8) for viewing and batch-editing EXIF metadata across multiple JPEG files at once, including GPS latitude, longitude, and altitude.

## Features

- Load individual files or an entire folder of `.jpg`/`.jpeg` files, with thumbnail previews (orientation-corrected).
- Full raw EXIF tag editor: every tag on the selected file is listed and editable in place, including adding brand-new tags (by name or raw numeric ID) and removing existing ones.
- Dedicated support for GPS position: `GPSLatitude`/`GPSLongitude` edit as plain degrees/minutes/seconds, with `GPSLatitudeRef`/`GPSLongitudeRef`/`GPSAltitudeRef` as dropdowns instead of raw codes, and blank GPS fields show a grayed-out example of the expected format.
- Multi-file editing: select multiple files in the list and any tag edit, add, or removal in the grid is applied - and saved - to every selected file at once.
- Double-click a file to open it in your system's default image viewer.

## Installer

[`installer/output/EBME-Setup-1.0.3.exe`](installer/output/) is a self-contained Windows installer (built with [Inno Setup](https://jrsoftware.org/isinfo.php)) — no .NET installation needed on the target machine. Run it, follow the wizard, and it adds a Start Menu entry and an optional desktop shortcut. Rebuild it after a version bump with `installer\setup.iss` (requires Inno Setup's `ISCC.exe`).

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
