; Multi_calude — Windows installer (Inno Setup 6).
; Built by the release workflow:
;   iscc /DAppVersion=0.1.0 /DSourceDir=<folder with cli.exe and cli-tray.exe> setup.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\dist\win-x64-package"
#endif

[Setup]
AppId={{8AFFD4A1-BBBF-4CE3-80B3-470E24F45877}
AppName=Multi_calude
AppVersion={#AppVersion}
AppPublisher=Ivantech123
AppPublisherURL=https://github.com/Ivantech123/Multi_calude
AppSupportURL=https://github.com/Ivantech123/Multi_calude/issues
DefaultDirName={localappdata}\Programs\multi_calude
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
OutputBaseFilename=multi_calude-setup-x64
OutputDir=..\..\dist
SetupIconFile=..\..\assets\icon.ico
UninstallDisplayIcon={app}\cli.exe
UninstallDisplayName=Multi_calude
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ru.DesktopIcon=Ярлык на рабочем столе
en.DesktopIcon=Desktop shortcut
ru.TrayAutostart=Значок с лимитами в трее при запуске Windows
en.TrayAutostart=Tray icon with limits when Windows starts
ru.Tray=Multi_calude — лимиты в трее
en.Tray=Multi_calude — tray limits
ru.Launch=Открыть Multi_calude
en.Launch=Open Multi_calude

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"
Name: "trayautostart"; Description: "{cm:TrayAutostart}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\Multi_calude"; Filename: "{app}\cli.exe"; WorkingDir: "{%USERPROFILE}"
Name: "{userprograms}\{cm:Tray}"; Filename: "{app}\cli-tray.exe"
Name: "{userdesktop}\Multi_calude"; Filename: "{app}\cli.exe"; WorkingDir: "{%USERPROFILE}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClaudeAccountsTray"; ValueData: """{app}\cli-tray.exe"""; Flags: uninsdeletevalue; Tasks: trayautostart

[Run]
Filename: "{app}\cli.exe"; Parameters: "--install"; Flags: runhidden waituntilterminated
Filename: "{app}\cli-tray.exe"; Flags: nowait; Tasks: trayautostart
Filename: "{app}\cli.exe"; Description: "{cm:Launch}"; WorkingDir: "{%USERPROFILE}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/IM cli-tray.exe /F"; Flags: runhidden; RunOnceId: "StopTray"
Filename: "{app}\cli.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveClaudeConfig"

[Code]
const
  EnvKey = 'Environment';

procedure AddToPath(Dir: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKCU, EnvKey, 'Path', Paths) then Paths := '';
  if Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Paths) + ';') > 0 then Exit;
  if (Paths <> '') and (Copy(Paths, Length(Paths), 1) <> ';') then Paths := Paths + ';';
  RegWriteExpandStringValue(HKCU, EnvKey, 'Path', Paths + Dir);
end;

procedure RemoveFromPath(Dir: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKCU, EnvKey, 'Path', Paths) then Exit;
  Paths := ';' + Paths + ';';
  StringChangeEx(Paths, ';' + Dir + ';', ';', True);
  Paths := Copy(Paths, 2, Length(Paths) - 2);
  RegWriteExpandStringValue(HKCU, EnvKey, 'Path', Paths);
end;

// A running tray keeps its exe locked.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM cli-tray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then AddToPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then RemoveFromPath(ExpandConstant('{app}'));
end;
