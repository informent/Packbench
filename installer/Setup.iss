#ifndef AppVersion
  #error AppVersion must be supplied
#endif
#ifndef PayloadDir
  #error PayloadDir must be supplied
#endif
#ifndef OutputDir
  #error OutputDir must be supplied
#endif
[Setup]
AppId={{7447A452-6E6F-5A4D-81A1-30E626D32DAC}
AppName=Packbench
AppVersion={#AppVersion}
AppPublisher=informent
AppPublisherURL=https://github.com/informent/Packbench
DefaultDirName={localappdata}\Programs\Packbench
DefaultGroupName=Packbench
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=Packbench-v{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Packbench.exe
CloseApplications=yes
CloseApplicationsFilter=Packbench.exe
RestartApplications=no
SetupLogging=yes
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\Packbench"; Filename: "{app}\Packbench.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Packbench"; Filename: "{app}\Packbench.exe"; WorkingDir: "{app}"; Tasks: desktopicon
[Run]
Filename: "{app}\Packbench.exe"; Description: "Open Packbench"; Flags: nowait postinstall skipifsilent unchecked
