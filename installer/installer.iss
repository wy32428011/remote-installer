#ifndef MyAppName
  #define MyAppName "RemoteInstaller"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef MyAppSourceDir
  #define MyAppSourceDir "..\\RemoteInstaller\\bin\\Release\\net10.0-windows\\publish"
#endif

#ifndef MyOutputDir
  #define MyOutputDir "..\\artifacts\\installer"
#endif

#define MyLicenseFile "..\\LICENSE"
#define MySetupIconFile "..\\RemoteInstaller\\Assets\\Brand\\remoteinstaller-icon.ico"
#define MyAppPublisher "Leon"
#define MyAppExeName MyAppName + ".exe"

[Setup]
AppId={{A1572115-7D3E-4D10-A5B4-AE5C47D9E701}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
LicenseFile={#MyLicenseFile}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#MySetupIconFile}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Excludes: "data.db,data.db-wal,data.db-shm"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
