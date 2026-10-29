#ifndef AppVersion
  #error 未指定版本号
#endif
#ifndef PublishDir
  #error 未指定发布目录
#endif
#ifndef OutputDir
  #error 未指定输出目录
#endif
#ifndef Runtime
  #error 未指定目标架构
#endif

[Setup]
AppId={{04A61DCA-5BE1-44D4-8D3A-AB39CA37E6B5}
AppName=SnapLine
AppVersion={#AppVersion}
AppPublisher=SnapLine
DefaultDirName={localappdata}\Programs\SnapLine
DefaultGroupName=SnapLine
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=SnapLine-{#AppVersion}-{#Runtime}-Setup
SetupIconFile=..\Assets\snapline.ico
UninstallDisplayIcon={app}\SnapLine.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
#if Runtime == "win-arm64"
ArchitecturesAllowed=arm64
#else
ArchitecturesAllowed=x64compatible
#endif

[Files]
Source: "{#PublishDir}\SnapLine.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SnapLine"; Filename: "{app}\SnapLine.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\SnapLine.exe"; Description: "启动 SnapLine"; Flags: nowait postinstall skipifsilent
