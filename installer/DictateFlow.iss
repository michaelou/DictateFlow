; Inno Setup script for DictateFlow.
;
; Built by scripts\release.ps1, which passes the values below on the ISCC command line.
; You can also run it by hand from the installer\ folder after a publish, e.g.:
;
;   iscc /DMyAppVersion=0.1.0 /DPublishDir="..\artifacts\publish\win-x64" /DOutputDir="..\artifacts" DictateFlow.iss
;
; Required defines (with sensible fallbacks so a manual run still works):
;   MyAppVersion  the release version, e.g. 0.1.0
;   PublishDir    the self-contained publish output folder to package
;   OutputDir     where the setup .exe is written (the artifacts folder)

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define MyAppName "DictateFlow"
#define MyAppPublisher "Belugga"
#define MyAppExeName "DictateFlow.exe"
#define MyAppIcon "..\src\DictateFlow.App\Resources\appicon.ico"
#define MyAppUrl "https://github.com/michaelou/DictateFlow"

[Setup]
; A stable, DictateFlow-specific AppId so upgrades and uninstall are recognized across versions.
AppId={{8F3A1C6E-2B7D-4E9A-9C1F-7A5D3E8B2F41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
OutputDir={#OutputDir}
OutputBaseFilename=DictateFlowSetup-v{#MyAppVersion}
SetupIconFile={#MyAppIcon}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Install into Program Files by default. This requires administrator rights, so the setup
; prompts for UAC elevation on launch; with elevation {autopf} resolves to C:\Program Files.
; PrivilegesRequiredOverridesAllowed lets the user drop to a per-user install (into
; %LocalAppData%\Programs) from the wizard or command line if they prefer.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
; Support in-app updates: when DictateFlow launches this installer to update itself it closes
; first, but CloseApplications lets the restart manager close any instance that is still
; holding files. RestartApplications is off because we relaunch via the [Run] entry below
; (as the original, non-elevated user) rather than through the restart manager.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; runasoriginaluser: after an elevated (in-app) update the app relaunches as the normal user,
; not as administrator.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
