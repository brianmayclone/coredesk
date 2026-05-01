#define AppName "CoreDesk"
#define AppPublisher "CoreDesk"
#define AppExeName "CoreDesk.App.exe"
#define AppVersion GetEnv("COREDESK_VERSION")
#if AppVersion == ""
#define AppVersion "0.1.0"
#endif
#define PublishDir GetEnv("COREDESK_PUBLISH_DIR")
#if PublishDir == ""
#define PublishDir "..\artifacts\publish\win-x64"
#endif
#define OutputDir GetEnv("COREDESK_SETUP_DIR")
#if OutputDir == ""
#define OutputDir "..\artifacts\setup"
#endif

[Setup]
AppId={{D14A9D07-5C9D-4C2F-A933-5D5998E0E7DB}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\CoreDesk
DefaultGroupName=CoreDesk
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=CoreDesk-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
SetupIconFile=..\src\CoreDesk.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "CoreDesk with Windows starten"; GroupDescription: "Startup"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CoreDesk"; Filename: "{app}\{#AppExeName}"
Name: "{group}\CoreDesk Safe Mode"; Filename: "{app}\{#AppExeName}"; Parameters: "--safe-mode --diagnostics"
Name: "{autodesktop}\CoreDesk"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CoreDesk"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,CoreDesk}"; Flags: nowait postinstall skipifsilent
