; Inno Setup script for Job Application Manager.
;
; Build it through build\build-installer.ps1 rather than by hand - that script publishes the app
; first and passes the version through from Directory.Build.props.
;
;   powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
;
; Requires Inno Setup 6:  winget install JRSoftware.InnoSetup

; Defaults so the script still compiles if opened directly in the Inno Setup IDE.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

#define AppName    "Job Application Manager"
#define AppExeName "JobApplicationManager.exe"
#define Publisher  "Kyle Basirico"

[Setup]
; AppId is how Windows tells an upgrade from a second, parallel installation.
; NEVER change it - a new GUID means every existing user ends up with two entries in
; Add/Remove Programs and two copies on disk.
AppId={{B7AAE76C-0279-4163-9EB2-93204215A711}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
VersionInfoVersion={#AppVersion}
UninstallDisplayName={#AppName}

; Per-user install: no UAC prompt, works on a locked-down machine where the user is not an admin.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\JobApplicationManager
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; The publish is win-x64 only.
ArchitecturesAllowed=x64compatible

; ~180 MB of self-contained .NET runtime - the compression settings are worth the build time.
Compression=lzma2/max
SolidCompression=yes

; Shut a running copy down rather than failing on a locked exe when installing over an
; existing version.
CloseApplications=yes
RestartApplications=no

OutputDir=..\artifacts
OutputBaseFilename=JobApplicationManager-Setup-{#AppVersion}
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Everything the publish produced, minus the debug symbols - end users have no use for them.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";        Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent

; There is deliberately no [UninstallDelete] section.
;
; The database lives at %LOCALAPPDATA%\JobApplicationManager\jobapps.db, which this installer
; never creates - the app does, on first run. Inno only removes what it installed, so leaving
; this section empty is exactly what keeps a user's applications intact across an uninstall and
; a later reinstall. The correct behaviour here looks like an omission; it isn't.
