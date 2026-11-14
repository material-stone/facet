; Per-user installer around the portable package (ADR 0087, 0098). No administrator rights are needed.
; Build: ISCC /DAppVersion=1.2.3 /DSourceDir=<unzipped Facet-<version>-win-x64 folder> /DOutputDir=<folder> installer\Facet.iss
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{6F3C2B1E-7A4D-4E58-9C1B-FACE70000001}
AppName=Facet
AppVersion={#AppVersion}
AppVerName=Facet {#AppVersion}
AppPublisher=Material Stone
AppPublisherURL=https://github.com/material-stone/facet
AppSupportURL=https://github.com/material-stone/facet/issues
AppUpdatesURL=https://github.com/material-stone/facet/releases
DefaultDirName={localappdata}\Programs\Facet
DefaultGroupName=Facet
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Facet-{#AppVersion}-setup-x64
SetupIconFile=..\impls\Facet\Design\AppIcon\Facet.ico
UninstallDisplayIcon={app}\Facet.Launcher.exe
UninstallDisplayName=Facet
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; A new version replaces the whole program folder; records live in %LOCALAPPDATA%\MaterialStone\DailyDock.
Type: filesandordirs; Name: "{app}\releases"
Type: filesandordirs; Name: "{app}\runtime"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Facet"; Filename: "{app}\Facet.Launcher.exe"
Name: "{autodesktop}\Facet"; Filename: "{app}\Facet.Launcher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Facet.Launcher.exe"; Description: "{cm:LaunchProgram,Facet}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM Facet.exe /T"; Flags: runhidden; RunOnceId: "StopFacet"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Registry]
; The app's own "start with Windows" value; removed with the program, never written here.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MaterialStoneDailyDock"; Flags: uninsdeletevalue dontcreatekey
