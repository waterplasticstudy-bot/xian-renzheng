; 闲·认证 Inno Setup 脚本。需要 UTF-8 BOM。

#define MyAppName "闲·认证"
#define MyAppVersion "0.3.0"
#define MyAppPublisher "xian-renzheng"
#define MyAppExeName "XianAuth.exe"

[Setup]
AppId={{A7C4E2B1-5D18-4F6A-9C31-2E8B7A6D4C90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\XianRenzheng
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=XianRenzheng-Setup-0.3.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
SetupIconFile=..\assets\xian-auth.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion=0.3.0.0

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\闲·认证"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\闲·认证"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"

[Dirs]
Name: "{app}\logs"
Name: "{app}\profiles"
Name: "{app}\backups"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动闲·认证"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\XianAuthCore.exe"; Parameters: "--rollback"; Flags: runhidden waituntilterminated

[Code]
var
  UpgradeBackup: String;

function PreviousInstallDir(): String;
var
  Location: String;
begin
  Location := '';
  if RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7C4E2B1-5D18-4F6A-9C31-2E8B7A6D4C90}_is1', 'InstallLocation', Location) then
    Result := Location
  else if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7C4E2B1-5D18-4F6A-9C31-2E8B7A6D4C90}_is1', 'InstallLocation', Location) then
    Result := Location
  else
    Result := '';
end;

procedure CopyIfExists(const Source, Dest: String);
begin
  if FileExists(Source) then
  begin
    ForceDirectories(ExtractFileDir(Dest));
    FileCopy(Source, Dest, False);
  end;
end;

procedure CopyProfiles(const SourceDir, DestDir: String);
var
  FindRec: TFindRec;
begin
  if not DirExists(SourceDir) then
    Exit;
  ForceDirectories(DestDir);
  if FindFirst(SourceDir + '\*.json', FindRec) then
  begin
    try
      repeat
        FileCopy(SourceDir + '\' + FindRec.Name, DestDir + '\' + FindRec.Name, False);
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure BackupUserData();
var
  Source: String;
begin
  Source := PreviousInstallDir();
  if Source = '' then
    Exit;
  if (Length(Source) > 0) and (Source[Length(Source)] = '\') then
    Source := Copy(Source, 1, Length(Source) - 1);
  UpgradeBackup := ExpandConstant('{localappdata}\XianRenzheng-upgrade');
  DelTree(UpgradeBackup, True, True, True);
  ForceDirectories(UpgradeBackup);
  CopyIfExists(Source + '\account.dat', UpgradeBackup + '\account.dat');
  CopyIfExists(Source + '\config.json', UpgradeBackup + '\config.json');
  CopyIfExists(Source + '\state.json', UpgradeBackup + '\state.json');
  CopyProfiles(Source + '\profiles', UpgradeBackup + '\profiles');
end;

procedure RestoreUserData();
var
  Dest: String;
begin
  if UpgradeBackup = '' then
    Exit;
  if not DirExists(UpgradeBackup) then
    Exit;
  Dest := ExpandConstant('{app}');
  CopyIfExists(UpgradeBackup + '\account.dat', Dest + '\account.dat');
  CopyIfExists(UpgradeBackup + '\config.json', Dest + '\config.json');
  CopyIfExists(UpgradeBackup + '\state.json', Dest + '\state.json');
  CopyProfiles(UpgradeBackup + '\profiles', Dest + '\profiles');
  DelTree(UpgradeBackup, True, True, True);
end;

function InitializeSetup(): Boolean;
begin
  BackupUserData();
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RestoreUserData();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;
  { 默认按钮是“否”：静默卸载和升级都保留账号、配置和门户资料。 }
  if MsgBox('是否删除本机保存的账号、配置和门户资料？' + #13#10 + #13#10 + '选择“否”将保留这些文件（推荐）。', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
    Exit;
  AppDir := ExpandConstant('{app}');
  DelTree(AppDir + '\profiles', True, True, True);
  DelTree(AppDir + '\logs', True, True, True);
  DelTree(AppDir + '\backups', True, True, True);
  DeleteFile(AppDir + '\account.dat');
  DeleteFile(AppDir + '\config.json');
  DeleteFile(AppDir + '\state.json');
  DeleteFile(AppDir + '\配置变更记录.json');
end;
