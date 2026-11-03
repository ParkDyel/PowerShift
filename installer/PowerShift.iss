#ifndef AppVersion
  #define AppVersion "0.4.0"
#endif

[Setup]
AppId={{58DE4696-91D0-4A18-A3F3-DCE538F13087}
AppName=PowerShift
AppVersion={#AppVersion}
AppPublisher=ParkDyel
AppPublisherURL=https://github.com/ParkDyel/PowerShift
AppSupportURL=https://github.com/ParkDyel/PowerShift/issues
AppUpdatesURL=https://github.com/ParkDyel/PowerShift/releases
DefaultDirName={localappdata}\Programs\PowerShift
DefaultGroupName=PowerShift
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
DisableWelcomePage=no
WizardStyle=modern
UninstallDisplayIcon={sys}\powercpl.dll,0
UninstallDisplayName=PowerShift
OutputDir=..\dist
OutputBaseFilename=PowerShift-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
SetupMutex=Local\PowerShift.Setup.58DE4696

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[CustomMessages]
english.StartupTask=Start PowerShift when I sign in to Windows
korean.StartupTask=Windows 로그인 시 PowerShift 자동 실행
english.CloseAppError=PowerShift could not close safely. Close it from the tray menu and try again.
korean.CloseAppError=PowerShift를 정상 종료할 수 없습니다. 트레이 메뉴에서 종료한 뒤 다시 시도해 주세요.
english.StartupError=Could not update the sign-in startup setting.
korean.StartupError=로그인 시 자동 실행 설정을 변경할 수 없습니다.

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"; Flags: unchecked

[Files]
Source: "..\bin\PowerShift.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\PowerShift"; Filename: "{app}\PowerShift.exe"; Parameters: "--settings"; WorkingDir: "{app}"; IconFilename: "{sys}\powercpl.dll"; IconIndex: 0

[Run]
Filename: "{app}\PowerShift.exe"; Parameters: "--settings"; Description: "{cm:LaunchProgram,PowerShift}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function OpenEventW(Access: LongWord; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(EventHandle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function StopPowerShift: Boolean;
var
  EventHandle: THandle;
  Attempt: Integer;
begin
  Result := True;
  if not CheckForMutexes('Local\PowerShift.3B8D7A3E') then Exit;
  EventHandle := OpenEventW(2, False, 'Local\PowerShift.Exit.3B8D7A3E');
  if EventHandle = 0 then begin Result := False; Exit; end;
  try
    if not SetEvent(EventHandle) then begin Result := False; Exit; end;
  finally
    CloseHandle(EventHandle);
  end;
  for Attempt := 1 to 100 do begin
    if not CheckForMutexes('Local\PowerShift.3B8D7A3E') then Exit;
    Sleep(100);
  end;
  Result := False;
end;

procedure InitializeWizard;
var
  StartupCommand: String;
  Index: Integer;
begin
  { Explicit unattended task selections always take precedence. }
  for Index := 1 to ParamCount do
    if (Pos('/TASKS=', UpperCase(ParamStr(Index))) = 1) or
       (Pos('/MERGETASKS=', UpperCase(ParamStr(Index))) = 1) or
       (Pos('/LOADINF=', UpperCase(ParamStr(Index))) = 1) then Exit;
  { Carry forward an existing portable installation's startup preference. }
  if RegQueryStringValue(HKCU, RunKey, 'PowerShift', StartupCommand) and
     (StartupCommand <> '') then WizardSelectTasks('startup');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopPowerShift then Result := CustomMessage('CloseAppError');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    if WizardIsTaskSelected('startup') then begin
      if not RegWriteStringValue(HKCU, RunKey, 'PowerShift',
        '"' + ExpandConstant('{app}\PowerShift.exe') + '"') then
        RaiseException(CustomMessage('StartupError'));
    end else if RegValueExists(HKCU, RunKey, 'PowerShift') then begin
      if not RegDeleteValue(HKCU, RunKey, 'PowerShift') then
        RaiseException(CustomMessage('StartupError'));
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then begin
    if not StopPowerShift then RaiseException(CustomMessage('CloseAppError'));
    { Only remove startup entries owned by this installation. Keep settings/logs. }
    if RegQueryStringValue(HKCU, RunKey, 'PowerShift', StartupCommand) and
       (CompareText(StartupCommand, '"' + ExpandConstant('{app}\PowerShift.exe') + '"') = 0) then
      RegDeleteValue(HKCU, RunKey, 'PowerShift');
  end;
end;
