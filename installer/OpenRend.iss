; OpenRend Windows Installer Script
; Compiled using Inno Setup 6

#define MyAppName "OpenRend"
#define MyAppVersion "0.2.1"
#define MyAppPublisher "OpenRend"
#define MyAppURL "https://github.com/jamiejustcodes/OpenRend"
#define MyAppExeName "OpenRend.exe"

[Setup]
AppId={{9F8425AE-B647-4185-9831-F5E242AA0741}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

; Default destination: User's Local AppData (per-user installation, no admin UAC needed)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Visual styling & branding
SetupIconFile=..\assets\OpenRend.ico
WizardStyle=modern
DisableWelcomePage=no

; Output & Solid Ultra Compression (Lightweight & Smallest Possible)
OutputDir=..\dist
OutputBaseFilename=OpenRend-Setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMADictionarySize=1048576
LZMANumBlockThreads=4
LZMANumFastBytes=273

; Uninstallation
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "fileassoc"; Description: "Associate {#MyAppName} with .openrend project files"; GroupDescription: "File Associations:"

[Files]
; Order files to maximize solid LZMA2 dictionary matches across similar PE headers and binaries
Source: "..\dist\OpenRend\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\OpenRend\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\OpenRend\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.dll,*.exe"
Source: "..\assets\OpenRend.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\OpenRend.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\OpenRend.ico"; Tasks: desktopicon

[Registry]
; File association for .openrend project files
Root: HKA; Subkey: "Software\Classes\.openrend"; ValueType: string; ValueName: ""; ValueData: "OpenRend.Project"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\OpenRend.Project"; ValueType: string; ValueName: ""; ValueData: "OpenRend Project"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\OpenRend.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\OpenRend.ico,0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\OpenRend.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

; App Paths registration so running "openrend" from Run dialog or cmd works
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; WorkingDir: "{app}"
