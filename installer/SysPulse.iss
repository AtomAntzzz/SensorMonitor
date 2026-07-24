; SysPulse.iss — compiled by installer/build.ps1, which passes external paths/vars via /D.
; Do not compile by double-clicking (missing /D vars will fail).

#ifndef MyVersion
  #define MyVersion "0.0.2.0"
#endif
#ifndef MyArch
  #define MyArch "x64"
#endif
#if MyArch == "x64"
  #define ArchAllowed "x64compatible"
#else
  #define ArchAllowed "arm64"
#endif

[Setup]
AppId={{7A2E6B84-3C1D-4E5F-9A70-5E4C0B9D2F11}}
AppName=SysPulse
AppVersion={#MyVersion}
AppPublisher=AtomAntzzz
SetupIconFile=SysPulse.ico
DefaultDirName={autopf}\SysPulse
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode=x64compatible arm64
OutputDir=Output
OutputBaseFilename=SysPulseSetup_{#MyArch}
UninstallDisplayName=SysPulse
UninstallDisplayIcon={app}\Host\SysPulse.Host.exe
WizardStyle=modern

[Files]
; Self-contained Host -> {app}\Host
Source: "{#MyHostDir}\*"; DestDir: "{app}\Host"; Flags: recursesubdirs createallsubdirs ignoreversion
; Extension msix + signing certificate public key -> temp dir, deleted after install
Source: "{#MyMsix}"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#MyCer}";  DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; 0) Clean up leftover SensorMonitor (first upgrade after the SysPulse rename; harmless on a fresh install — schtasks /Delete /F returns 0 even if the task does not exist)
Filename: "{sys}\schtasks.exe"; Parameters: "/End /TN SensorMonitor.Host"; Flags: runhidden waituntilterminated
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN SensorMonitor.Host /F"; Flags: runhidden waituntilterminated
Filename: "powershell.exe"; Parameters: "-NoProfile -Command ""Get-AppxPackage *SensorMonitorExtension* | Remove-AppxPackage; Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*SensorMonitorExtension*' | ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName }"""; Flags: runhidden waituntilterminated
; 1) Trust the dev certificate (delete this line when using a certificate that chains to a trusted root)
Filename: "{sys}\certutil.exe"; Parameters: "-addstore -f TrustedPeople ""{tmp}\{#MyCerName}"""; Flags: runhidden waituntilterminated; StatusMsg: "Trusting signing certificate..."
; 2) Register the scheduled task: runs the installed Host; TaskInstaller uses Environment.ProcessPath as /TR, i.e. this {app}\Host path
Filename: "{app}\Host\SysPulse.Host.exe"; Parameters: "--install-task"; Flags: runhidden waituntilterminated; StatusMsg: "Registering background service..."
; 3) Provision the extension MSIX machine-wide + register it for the current user immediately
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Add-AppxProvisionedPackage -Online -PackagePath '{tmp}\{#MyMsixName}' -SkipLicense; Add-AppxPackage -Path '{tmp}\{#MyMsixName}' -ForceUpdateFromAnyVersion -ForceApplicationShutdown"""; Flags: runhidden waituntilterminated; StatusMsg: "Registering Command Palette extension..."
; 4) Start the Host now (no need to wait for the next sign-in)
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN SysPulse.Host"; Flags: runhidden

[UninstallRun]
; Stop Host -> delete task -> remove the extension (current user + provisioned)
Filename: "{sys}\schtasks.exe"; Parameters: "/End /TN SysPulse.Host"; Flags: runhidden waituntilterminated; RunOnceId: "EndHost"
Filename: "{app}\Host\SysPulse.Host.exe"; Parameters: "--uninstall-task"; Flags: runhidden waituntilterminated; RunOnceId: "DelTask"
Filename: "powershell.exe"; Parameters: "-NoProfile -Command ""Get-AppxPackage *SysPulseExtension* | Remove-AppxPackage; Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*SysPulseExtension*' | ForEach-Object {{ Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName }"""; Flags: runhidden waituntilterminated; RunOnceId: "RemovePkg"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{commonappdata}\SysPulse"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var rc: Integer;
begin
  // Before overwriting on upgrade, stop the running Host (try both old and new task names) to avoid locking {app}\Host\SysPulse.Host.exe.
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN SensorMonitor.Host', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN SysPulse.Host', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Result := '';
end;
