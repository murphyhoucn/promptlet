#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PackageDir
  #error PackageDir is required
#endif

[Setup]
AppId=Promptlet
AppName=Promptlet
AppVersion={#AppVersion}
AppPublisher=Promptlet contributors
AppPublisherURL=https://github.com/murphyhoucn/promptlet
AppSupportURL=https://github.com/murphyhoucn/promptlet/issues
DefaultDirName={localappdata}\Programs\Promptlet\{#AppVersion}
DefaultGroupName=Promptlet
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=Promptlet-{#AppVersion}-Setup-x64
SetupIconFile={#PackageDir}\App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\Promptlet.exe
LicenseFile={#LicenseFile}
WizardStyle=modern
Compression=lzma2/normal
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=Promptlet.exe
RestartApplications=no
Uninstallable=yes
SetupLogging=no

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimp.DesktopIcon=创建桌面快捷方式
english.DesktopIcon=Create a desktop shortcut
chinesesimp.LaunchApp=启动 Promptlet
english.LaunchApp=Launch Promptlet
chinesesimp.ExitFirst=请从系统托盘退出 Promptlet，然后重试。
english.ExitFirst=Please exit Promptlet from the system tray and retry.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PackageDir}\App\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#CompilerLicenseFile}"; DestDir: "{app}\Licenses"; DestName: "Inno-Setup-License.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Promptlet"; Filename: "{app}\Promptlet.exe"; Parameters: "--background"; Check: not TestMode
Name: "{autodesktop}\Promptlet"; Filename: "{app}\Promptlet.exe"; Parameters: "--background"; Tasks: desktopicon; Check: not TestMode

[Run]
Filename: "{app}\Promptlet.exe"; Parameters: "--settings"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent; Check: not TestMode

[Code]
function OpenEvent(Access: LongWord; Inherit: Integer; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): Integer;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Integer;
  external 'CloseHandle@kernel32.dll stdcall';

function TestMode: Boolean;
begin
  Result := ExpandConstant('{param:TESTMODE|0}') = '1';
end;

procedure RequestAppExit;
var Event: THandle; Attempt: Integer;
begin
  Event := OpenEvent($0002, 0, 'Local\Promptlet.Desktop.Quit');
  if Event <> 0 then begin
    SetEvent(Event);
    CloseHandle(Event);
    for Attempt := 1 to 100 do begin
      if not CheckForMutexes('Local\Promptlet.Desktop.Singleton') then break;
      Sleep(100);
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  RequestAppExit;
  Result := '';
  if CheckForMutexes('Local\Promptlet.Desktop.Singleton') then
    Result := CustomMessage('ExitFirst');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var StartupCommand: String;
begin
  if (CurUninstallStep = usUninstall) and not TestMode then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Promptlet', StartupCommand) then
      if Pos(Lowercase(ExpandConstant('{app}\Promptlet.exe')), Lowercase(StartupCommand)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Promptlet');
end;

function InitializeUninstall: Boolean;
begin
  RequestAppExit;
  Result := not CheckForMutexes('Local\Promptlet.Desktop.Singleton');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var PreviousLocation, StartupCommand: String;
begin
  if (CurStep = ssPostInstall) and not TestMode then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Promptlet', 'InstallLocation', PreviousLocation) then
      if CompareText(PreviousLocation, ExpandConstant('{app}')) = 0 then
        RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Promptlet');
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Promptlet', StartupCommand) then
      if Pos('promptlet.exe', Lowercase(StartupCommand)) > 0 then
        RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Promptlet', ExpandConstant('"{app}\Promptlet.exe" --background'));
  end;
end;
