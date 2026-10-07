#ifndef SourceRoot
  #error SourceRoot is required
#endif
#ifndef OutputRoot
  #error OutputRoot is required
#endif
#ifndef AppVersion
  #define AppVersion "0.1.8"
#endif
[Setup]
AppId={{861D3434-165F-4997-9D78-697BE50F8ABC}
AppName=HearthLeap
AppVersion={#AppVersion}
AppPublisher=HearthLeap
DefaultDirName={localappdata}\Programs\HearthLeap
DefaultGroupName=HearthLeap
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputRoot}
OutputBaseFilename=HearthLeap-{#AppVersion}-Setup
SetupIconFile={#IconPath}
UninstallDisplayIcon={app}\HearthLeap.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
InfoBeforeFile={#SourceRoot}\安装版使用说明.txt
[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："
[Files]
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autodesktop}\HearthLeap"; Filename: "{app}\HearthLeap.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{autoprograms}\HearthLeap\HearthLeap"; Filename: "{app}\HearthLeap.exe"; WorkingDir: "{app}"
Name: "{autoprograms}\HearthLeap\卸载 HearthLeap"; Filename: "{uninstallexe}"
[Run]
Filename: "{app}\HearthLeap.exe"; Description: "启动 HearthLeap"; Flags: nowait postinstall skipifsilent
[Code]
function HasVersion10(const Parent: String): Boolean;
var
  FindRec: TFindRec;
  Folder: String;
begin
  Result := False;
  if FindFirst(AddBackslash(Parent) + '10.0.*', FindRec) then
  begin
    try
      repeat
        Folder := AddBackslash(Parent) + FindRec.Name;
        if DirExists(Folder) and FileExists(Folder + '\.version') then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;
function HasDesktopRuntime(const Root: String): Boolean;
begin
  Result := HasVersion10(Root + '\shared\Microsoft.WindowsDesktop.App') and
    HasVersion10(Root + '\shared\Microsoft.NETCore.App');
end;
function InitializeSetup(): Boolean;
var
  Root: String;
begin
  Root := ExpandConstant('{commonpf64}\dotnet');
  Result := HasDesktopRuntime(Root);
  if not Result then
  begin
    if RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Root) then
      Result := HasDesktopRuntime(Root);
  end;
  if Result then Log('RUNTIME_CHECK=OK; .NET 10 Desktop Runtime x64')
  else
  begin
    Log('RUNTIME_CHECK=MISSING; no application files installed');
    if not WizardSilent then MsgBox('缺少 .NET 10 Windows Desktop Runtime（x64）。' + #13#10 +
      '请先从微软官方安装桌面运行时，再运行此安装程序。' + #13#10 +
      '注意：仅安装 ASP.NET Runtime 不够。安装已取消，尚未写入程序文件。', mbError, MB_OK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if Length(ExpandConstant('{app}')) > 150 then
    Result := '安装路径过长，请返回上一步选择更短的目录（例如默认安装目录）。尚未写入程序文件。';
end;
