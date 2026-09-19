#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{B5A0E2C4-7D1F-4E3A-9C68-2F41D7A9E5B3}
AppName=Orbit
AppVersion={#AppVersion}
AppPublisher=LuckyMan0277
AppPublisherURL=https://github.com/LuckyMan0277/orbit
DefaultDirName={localappdata}\Programs\Orbit
DefaultGroupName=Orbit
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\release
OutputBaseFilename=Orbit-Setup
SetupIconFile=..\assets\orbit.ico
UninstallDisplayIcon={app}\Orbit.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\release\Orbit\*"; DestDir: "{app}"; Excludes: "self-test-results.txt"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Orbit"; Filename: "{app}\Orbit.exe"
Name: "{autodesktop}\Orbit"; Filename: "{app}\Orbit.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Orbit.exe"; Description: "Launch Orbit"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Orbit.exe"; Flags: nowait; Check: WizardSilent
