#define MyAppName "SunTrace"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SunTrace"
#define MyAppExeName "SunTrace.exe"

[Setup]
AppId={{B8F83D1E-3A77-4D31-9C6F-7A1D4E2B8C55}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=© 2026 SunTrace
DefaultDirName={autopf}\SunTrace
DefaultGroupName=SunTrace
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousGroup=yes
OutputDir=..\release\Windows10-11
OutputBaseFilename=SunTrace-Setup-Windows10-11-x64
SetupIconFile=..\src\SunTrace.App\Assets\SunTrace.ico
UninstallDisplayIcon={app}\SunTrace.exe
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
AppMutex=Local\SunTrace.SingleInstance
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=1.0.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} - локальный журнал изменений программ для Windows 10/11
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
Source: "..\artifacts\publish-modern\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SunTrace"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Локальный журнал изменений программ"
Name: "{group}\Удалить SunTrace"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SunTrace"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить SunTrace"; Flags: nowait postinstall skipifsilent
