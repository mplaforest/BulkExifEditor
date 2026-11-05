#define MyAppName "EXIF Batch Metadata Editor"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "Mark LaForest"
#define MyAppExeName "ExifBatchEditor.exe"

[Setup]
AppId={{B1E1B4A0-6F2A-4B8E-9C1D-3E2F6A9C7D10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=EBME-Setup-{#MyAppVersion}-core
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; GoogleMapsApiKey.txt is excluded deliberately - it's the developer's own personal
; Google Maps key (gitignored, never committed) and this installer output IS committed
; to the public GitHub repo, so bundling it would publicly leak the key. The app already
; degrades gracefully with the map panel disabled when this file is absent (see
; InitMapAsync in MainWindow.xaml.cs) - an end user who wants the map feature supplies
; their own key by dropping a GoogleMapsApiKey.txt next to the installed exe's MapAssets\.
Source: "..\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "MapAssets\GoogleMapsApiKey.txt"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
