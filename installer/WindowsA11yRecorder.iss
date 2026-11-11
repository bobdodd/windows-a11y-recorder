; Inno Setup script for the Windows A11y Recorder installer, for Inno Setup
; 6.3 or later (x64os needs 6.3).
;
; Built by scripts\Build-Installer.ps1, which prepares the staging folder and
; passes these values with /D:
;   AppVersion     product version, from Directory.Build.props
;   NumericVersion the same version as numbers only, for the file's version
;   Revision       the git revision the programs were built from
;   StagingDir     folder holding app\ (the installed files) and vc_redist.x64.exe
;   OutputDir      where the installer is written
;
; See docs\architecture\windows-installer.md.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef Revision
  #define Revision "local"
#endif
#ifndef StagingDir
  #error StagingDir must be defined; run scripts\Build-Installer.ps1.
#endif
#ifndef OutputDir
  #define OutputDir "output"
#endif

#define AppName "Windows A11y Recorder"
#define AppExe "Recorder.App.exe"

[Setup]
; The AppId identifies the installation for upgrades and must never change.
AppId={{6C1F3A52-8E4B-4C2D-9A71-3B5E0D2F8C44}
AppName={#AppName}
AppVersion={#AppVersion} ({#Revision})
AppVerName={#AppName} {#AppVersion} ({#Revision})
AppPublisher=Bob Dodd
AppPublisherURL=https://github.com/bobdodd/windows-a11y-recorder
AppSupportURL=https://github.com/bobdodd/windows-a11y-recorder/issues
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoDescription={#AppName} setup
; Per user, without administrator rights. {autopf} is
; %LOCALAPPDATA%\Programs in this mode.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
; Setup itself is 32-bit, so the script compiles with Inno Setup 6.3 and
; later (winget's JRSoftware.InnoSetup is 6.7.3) as well as 7; it installs
; the 64-bit programs in 64-bit mode, on x64 Windows only.
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
; Windows 10 version 2004, the oldest the programs target
; (net10.0-windows10.0.19041.0).
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=WindowsA11yRecorder-Setup-{#Revision}
Compression=lzma2/max
SolidCompression=yes
DiskSpanning=no
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Put a shortcut on the desktop"; Flags: unchecked

[InstallDelete]
; An upgrade replaces these folders whole, so no file from an older build is
; left behind. They hold only installed programs; the recordings, the catalog
; and the model settings are elsewhere.
Type: filesandordirs; Name: "{app}\browser"
Type: filesandordirs; Name: "{app}\llama"
Type: filesandordirs; Name: "{app}\pgsql"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.exe"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.pdb"

[Files]
Source: "{#StagingDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StagingDir}\vc_redist.x64.exe"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autoprograms}\{#AppName} third-party notices"; Filename: "{app}\THIRD-PARTY-NOTICES.txt"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--setup-models"; WorkingDir: "{app}"; \
  Description: "Start the recorder and set up the analysis models"; \
  Flags: postinstall nowait skipifsilent

[Code]
const
  VcRuntimeKey = 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';
  // Read and run, inherited, for ALL APPLICATION PACKAGES and ALL
  // RESTRICTED APPLICATION PACKAGES; see GrantBrowserSandboxAccess.
  BrowserGrants = '/grant "*S-1-15-2-1:(OI)(CI)(RX)" /grant "*S-1-15-2-2:(OI)(CI)(RX)"';

function VcRuntimeInstalled: Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, VcRuntimeKey, 'Installed', Installed) and (Installed = 1);
end;

function NvidiaDriverPresent: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\nvidia-smi.exe'));
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then
    Result := Result + MemoTasksInfo + NewLine + NewLine;
  Result := Result + 'Included:' + NewLine +
    Space + 'The recorder, the player and the analysis worker' + NewLine +
    Space + 'PostgreSQL, for the catalog of recordings' + NewLine +
    Space + 'The instrumented Chromium browser' + NewLine +
    Space + 'The llama.cpp server with the NVIDIA CUDA 12.4 run time' + NewLine +
    Space + 'The scrcpy server and the companion app, for recording an Android phone' + NewLine + NewLine +
    'Model files are chosen and downloaded after the installation.';
  if not VcRuntimeInstalled then
    Result := Result + NewLine + NewLine +
      'The Microsoft Visual C++ run time is missing, so its installer runs next. ' +
      'Windows asks for permission, because it installs for every user.';
  if not NvidiaDriverPresent then
    Result := Result + NewLine + NewLine +
      'Warning: no NVIDIA driver was found (nvidia-smi.exe is not in ' +
      ExpandConstant('{sys}') + '). The recorder and player work, but the ' +
      'analysis models run on the graphics card and will be very slow or fail ' +
      'without one.';
end;

procedure InstallVcRuntime;
var
  ResultCode: Integer;
  Redistributable: String;
begin
  if VcRuntimeInstalled then
    exit;
  WizardForm.StatusLabel.Caption := 'Installing the Microsoft Visual C++ run time...';
  ExtractTemporaryFile('vc_redist.x64.exe');
  Redistributable := ExpandConstant('{tmp}\vc_redist.x64.exe');
  if not ShellExec('runas', Redistributable, '/install /quiet /norestart', '',
    SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  // 3010 means installed, with a restart needed for files that were in use.
  if not VcRuntimeInstalled then
    SuppressibleMsgBox(
      'The Microsoft Visual C++ run time was not installed (result ' +
      IntToStr(ResultCode) + '). The llama.cpp server needs it to run the ' +
      'analysis models.' + #13#10#13#10 +
      'Install it later by running vc_redist.x64.exe from ' +
      'https://aka.ms/vs/17/release/vc_redist.x64.exe, then start the recorder again.',
      mbError, MB_OK, IDOK)
  else if ResultCode = 3010 then
    SuppressibleMsgBox(
      'The Microsoft Visual C++ run time was installed. Restart Windows before ' +
      'running an analysis.', mbInformation, MB_OK, IDOK);
end;

// Chromium starts some of its processes, the network service among them, in
// an AppContainer, and before it does so it checks that the container can
// read and run chrome.exe; when it cannot, the browser stops at startup
// ("Sandbox cannot access executable", sandbox\policy\win\sandbox_win.cc).
// Chromium's build grants this on its output folder
// (testing\scripts\common.py, set_lpac_acls), but a folder under
// %LOCALAPPDATA%\Programs does not have it, so it is granted here: read and
// run for ALL APPLICATION PACKAGES (S-1-15-2-1) and ALL RESTRICTED
// APPLICATION PACKAGES (S-1-15-2-2), inherited by everything in the folder.
// Found on the target machine, 2026-11-07.
procedure GrantBrowserSandboxAccess;
var
  ResultCode: Integer;
  Browser: String;
begin
  Browser := ExpandConstant('{app}\browser');
  WizardForm.StatusLabel.Caption := 'Letting the browser''s sandbox read its programs...';
  if not Exec(ExpandConstant('{sys}\icacls.exe'), '"' + Browser + '" ' + BrowserGrants + ' /q',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  if ResultCode <> 0 then
    SuppressibleMsgBox(
      'The browser''s sandbox could not be given access to its folder (result ' +
      IntToStr(ResultCode) + '). Without it the browser stops as it starts, so ' +
      'recording a browser and inspecting a page do not work.' + #13#10#13#10 +
      'Run this in PowerShell to give it, then start the recorder again:' + #13#10#13#10 +
      'icacls "' + Browser + '" ' + BrowserGrants,
      mbError, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    GrantBrowserSandboxAccess;
    InstallVcRuntime;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    SuppressibleMsgBox(
      'The recorder was removed. Your recordings, the catalog, the model ' +
      'settings and the downloaded models were kept, in:' + #13#10#13#10 +
      ExpandConstant('{localappdata}') + '\Windows A11y Recorder' + #13#10 +
      ExpandConstant('{userdocs}') + '\Windows A11y Recorder' + #13#10#13#10 +
      'Delete those folders to remove them. Models saved to another folder are ' +
      'still in that folder.',
      mbInformation, MB_OK, IDOK);
end;
