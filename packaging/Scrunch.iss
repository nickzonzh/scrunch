#ifndef AppVersion
  #error AppVersion must come from Directory.Build.props
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef ArtifactDir
  #error ArtifactDir is required
#endif

[Setup]
AppId={{E538C51C-772C-4C53-B269-3AD1E50E34E1}
AppName=Scrunch
AppVersion={#AppVersion}
AppVerName=Scrunch {#AppVersion}
AppComments=Native desktop notes with tactile paper.
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=Scrunch
DefaultDirName={localappdata}\Programs\Scrunch
DisableDirPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#ArtifactDir}
OutputBaseFilename=Scrunch-{#AppVersion}-Setup
SetupIconFile=..\src\Scrunch\Assets\Scrunch.ico
UninstallDisplayIcon={app}\Scrunch.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\Scrunch.Installer.Resident
CloseApplications=no
RestartApplications=no
Uninstallable=yes
DisableProgramGroupPage=yes
SetupLogging=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Scrunch"; Filename: "{app}\Scrunch.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Scrunch.exe"

[Registry]
Root: HKCU; Subkey: "Software\Scrunch"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletevalue uninsdeletekeyifempty
; Do not enable startup on install or reset it on upgrade. Remove only our value on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Scrunch"; Flags: uninsdeletevalue

[InstallDelete]
; Retire obsolete application binaries during in-place upgrades. Never touch note data.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.xbf"
Type: files; Name: "{app}\*.pri"
Type: files; Name: "{app}\*.deps.json"
Type: files; Name: "{app}\*.runtimeconfig.json"

[Run]
Filename: "{app}\Scrunch.exe"; Description: "Open Scrunch"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{localappdata}\Programs\Scrunch')) <> 0 then
    Result := 'Scrunch uses its stable per-user installation folder. Remove the /DIR override and try again.';
end;
