; Inno Setup script for Taskbar Banner.
; Build the app first:
;   powershell -File .\scripts\publish.ps1
; Then compile with Inno Setup (ISCC.exe) from repo root:
;   ISCC .\installer\TaskbarBanner.iss
; Output: installer\Output\TaskbarBanner-Setup-<Version>.exe

#define MyAppName "Taskbar Banner"
#define MyAppVersion "0.7.0"
#define MyAppPublisher "MarWorckspace"
#define MyAppExeName "TaskbarBanner.App.exe"
#define MySourceDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{3F2C41D0-9A6E-4B1A-BF7C-1E4D8A5C7B30}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\TaskbarBanner
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=TaskbarBanner-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
; Requires Windows 10 or later
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#MySourceDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
