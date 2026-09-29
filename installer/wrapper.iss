// This compiles to the public-facing "EBME-Setup-<version>.exe" that people actually
// run/reference. It never shows any UI of its own - it always immediately hands off to
// the real installer (EBME-Setup-<version>-core.exe, built from setup.iss, sitting in
// the same folder), translating a custom "/s" silent switch into the ones Inno Setup's
// engine actually recognizes (/VERYSILENT /SUPPRESSMSGBOXES /NORESTART) and passing any
// other original arguments straight through.
//
// This two-file split exists because a setup.exe cannot reliably copy-and-relaunch
// itself (the running exe's own file is effectively locked, and self-copying a "setup"
// binary into temp and executing it looks exactly like dropper/self-replication
// behavior to antivirus heuristics - it was observed being silently blocked here even
// though the underlying file copy is otherwise unremarkable). Launching a *different*,
// statically-named sibling file avoids both problems entirely.

#define MyAppName "EXIF Batch Metadata Editor"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "Mark LaForest"
#define CoreExeName "EBME-Setup-" + MyAppVersion + "-core.exe"

[Setup]
AppId={{C2F2C5B1-7A3B-4C9F-AD2E-4F3A7BAE8E21}
AppName={#MyAppName} Setup
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
OutputDir=output
OutputBaseFilename=EBME-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\AppIcon.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Never actually installs anything itself - always hands off to the core installer.
DisableWelcomePage=yes

[Code]
procedure ExitProcess(uExitCode: UINT);
  external 'ExitProcess@kernel32.dll stdcall';

function InitializeSetup(): Boolean;
var
  I: Integer;
  Param: String;
  ExtraParams: String;
  WantsSilent: Boolean;
  ResultCode: Integer;
  CoreExe: String;
begin
  Result := True;
  WantsSilent := False;
  ExtraParams := '';

  for I := 1 to ParamCount do
  begin
    Param := ParamStr(I);
    if CompareText(Param, '/s') = 0 then
      WantsSilent := True
    else
      ExtraParams := ExtraParams + ' "' + Param + '"';
  end;

  if WantsSilent then
    ExtraParams := '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' + ExtraParams;

  CoreExe := ExtractFileDir(ExpandConstant('{srcexe}')) + '\{#CoreExeName}';

  if not FileExists(CoreExe) then
  begin
    SuppressibleMsgBox('Could not find ' + CoreExe + '. Make sure it is in the same folder as this installer.',
      mbCriticalError, MB_OK, IDOK);
    ExitProcess(1);
  end;

  if Exec(CoreExe, ExtraParams, ExtractFileDir(CoreExe), SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    ExitProcess(ResultCode)
  else
    ExitProcess(1);
end;
