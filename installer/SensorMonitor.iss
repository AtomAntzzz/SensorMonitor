; SensorMonitor.iss —— 由 installer/build.ps1 用 /D 传入外部路径/变量后编译。
; 不手工双击编译（缺 /D 变量会失败）。

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
AppName=Sensor Monitor
AppVersion={#MyVersion}
AppPublisher=AtomAntzzz
DefaultDirName={autopf}\SensorMonitor
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode=x64compatible arm64
OutputDir=Output
OutputBaseFilename=SensorMonitorSetup_{#MyArch}
UninstallDisplayName=Sensor Monitor
UninstallDisplayIcon={app}\Host\SensorMonitor.Host.exe
WizardStyle=modern

[Files]
; 自包含 Host → {app}\Host
Source: "{#MyHostDir}\*"; DestDir: "{app}\Host"; Flags: recursesubdirs createallsubdirs ignoreversion
; 扩展 msix + 签名证书公钥 → 临时目录，装完删
Source: "{#MyMsix}"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "{#MyCer}";  DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; 1) 信任 dev 证书（换真实链到受信根的证书时删掉本行）
Filename: "{sys}\certutil.exe"; Parameters: "-addstore -f TrustedPeople ""{tmp}\{#MyCerName}"""; Flags: runhidden waituntilterminated; StatusMsg: "信任签名证书…"
; 2) 注册计划任务：跑装好的 Host，TaskInstaller 以 Environment.ProcessPath 作 /TR，即本 {app}\Host 路径
Filename: "{app}\Host\SensorMonitor.Host.exe"; Parameters: "--install-task"; Flags: runhidden waituntilterminated; StatusMsg: "注册后台服务…"
; 3) 全机预置扩展 MSIX + 当前用户即时注册
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Add-AppxProvisionedPackage -Online -PackagePath '{tmp}\{#MyMsixName}' -SkipLicense; Add-AppxPackage -Path '{tmp}\{#MyMsixName}' -ForceUpdateFromAnyVersion -ForceApplicationShutdown"""; Flags: runhidden waituntilterminated; StatusMsg: "注册命令面板扩展…"
; 4) 立即启动 Host（免等下次登录）
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN SensorMonitor.Host"; Flags: runhidden

[UninstallRun]
; 停 Host → 删任务 → 移除扩展（当前用户 + 预置）
Filename: "{sys}\schtasks.exe"; Parameters: "/End /TN SensorMonitor.Host"; Flags: runhidden waituntilterminated; RunOnceId: "EndHost"
Filename: "{app}\Host\SensorMonitor.Host.exe"; Parameters: "--uninstall-task"; Flags: runhidden waituntilterminated; RunOnceId: "DelTask"
Filename: "powershell.exe"; Parameters: "-NoProfile -Command ""Get-AppxPackage *SensorMonitorExtension* | Remove-AppxPackage; Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*SensorMonitorExtension*' | ForEach-Object {{ Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName }"""; Flags: runhidden waituntilterminated; RunOnceId: "RemovePkg"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{commonappdata}\SensorMonitor"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var rc: Integer;
begin
  // 升级覆盖前停掉在跑的 Host，避免锁死 {app}\Host\SensorMonitor.Host.exe。首装时任务不存在，schtasks 返回非 0，忽略即可。
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN SensorMonitor.Host', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Result := '';
end;
