#define MyAppName "SunTrace Legacy"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SunTrace"
#define MyAppExeName "SunTrace.exe"

[Setup]
AppId={{C4D2A1F7-6E3B-4A58-9F21-8B7D3E6C2A90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=© 2026 SunTrace
DefaultDirName={autopf}\SunTrace Legacy
DefaultGroupName=SunTrace Legacy
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousGroup=yes
OutputDir=..\release\Windows7-8.1
OutputBaseFilename=SunTrace-Setup-Windows7-8.1-x64
SetupIconFile=..\src\SunTrace.App\Assets\SunTrace.ico
UninstallDisplayIcon={app}\SunTrace.exe
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
PrivilegesRequired=admin
AppMutex=Local\SunTrace.SingleInstance
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=1.0.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} - локальный журнал изменений программ для Windows 7/8.1
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные значки:"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.xml"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\createdump.exe"
Type: filesandordirs; Name: "{app}\ru"
Type: filesandordirs; Name: "{app}\runtimes"

[Files]
Source: "..\artifacts\publish-legacy\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SunTrace Legacy"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Локальный журнал изменений программ"
Name: "{group}\Удалить SunTrace Legacy"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SunTrace Legacy"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить SunTrace Legacy"; Flags: nowait postinstall skipifsilent
