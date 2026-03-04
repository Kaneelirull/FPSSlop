#define MyAppName      "FPSSlop"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "Kaneelirull"
#define MyAppExeName   "FPSSlop.exe"
#define PublishDir     "FPSSlop\bin\Release\net8.0-windows\win-x64\publish"
#define DotNetVersion  "8.0"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Installer
OutputBaseFilename=FPSSlop-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup";     Description: "Start FPSSlop with Windows"; GroupDescription: "Options:"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut";  GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,ignored-processes.txt"
Source: "{#PublishDir}\ignored-processes.txt"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "FPSSlop"; ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch FPSSlop"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM FPSSlop.exe";    RunOnceId: "KillFPSSlop";    Flags: runhidden
Filename: "taskkill.exe"; Parameters: "/F /IM PresentMon.exe"; RunOnceId: "KillPresentMon"; Flags: runhidden

[Code]
function DotNetRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  // .NET installs desktop runtimes to this folder — any 8.x subfolder means we're good
  if FindFirst(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

procedure InitializeWizard;
var
  ResultCode: Integer;
begin
  if not DotNetRuntimeInstalled then
  begin
    MsgBox(
      '.NET 8 Desktop Runtime is required but not installed.' + #13#10 +
      'The installer will now download and install it.' + #13#10#13#10 +
      'Please follow the .NET installer prompts, then re-run FPSSlop-Setup.exe.',
      mbInformation, MB_OK);
    ShellExec('open',
      'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe',
      '', '', SW_SHOW, ewNoWait, ResultCode);
    Abort();
  end;
end;