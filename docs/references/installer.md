# R4 分发安装器（本机复现）

> 产出：签名 Inno 安装器 `SensorMonitorSetup_<arch>.exe`——一键装「自包含 Host + 计划任务 + 完整扩展 MSIX」。
> 渠道 = GitHub Release + WinGet（installer type: inno）。**不走 MS Store**（驱动+提权约束，见 R4 spec）。

## 前置
- Inno Setup 6.3+（`ISCC.exe`）。
- dev 签名证书 `CN=SensorMonitor Dev`（`Cert:\CurrentUser\My`）；缺则见 `msix-packaging.md`。
- signtool（Windows Kits 10.0.26100.0）。

## 构建
```powershell
pwsh installer/build.ps1 -Arch x64      # ARM64 同理换 -Arch arm64
# 产出：installer/Output/SensorMonitorSetup_x64.exe（已签名）
```

## 安装做了什么（提权一次 UAC）
1. `certutil -addstore TrustedPeople`：信任 dev 证书（真实证书时安装器删此步）。
2. `Host.exe --install-task`：注册 `SensorMonitor.Host`（`/RL HIGHEST /SC ONLOGON`，`/TR`=`%ProgramFiles%\SensorMonitor\Host\SensorMonitor.Host.exe`）。
3. `Add-AppxProvisionedPackage`（全机）+ `Add-AppxPackage`（当前用户）注册扩展 MSIX。
4. `schtasks /Run` 立即起 Host。

## 卸载（走「程序和功能 → Sensor Monitor」主入口，勿单独卸 MSIX 组件）
删任务 + `Remove-AppxPackage`/`Remove-AppxProvisionedPackage` + 删 `%ProgramFiles%\SensorMonitor\` + `%ProgramData%\SensorMonitor\`。

## ⚠ 更新须知：每次发布必须 bump 版本号
`Add-AppxPackage` 对**同版本**包是无声 no-op——版本号不变时，装新包覆盖旧装**不会更新扩展**，
旧代码继续跑（2026-07-22 干净机实测踩坑：改了扩展但版本仍 `0.0.1.0`，测试机装了新安装器却仍显示旧行为）。
故每次改扩展要发布，**三处版本同步 bump**（当前 `0.0.2.0`）：
- `src/SensorMonitorExtension/SensorMonitorExtension/Package.appxmanifest` 的 `<Identity Version>`
- 同项目 csproj 的 `<AppxPackageVersion>`
- `installer/build.ps1` 的 `$Version` 默认值（= Inno AppVersion）
安装器另加了 `Add-AppxPackage ... -ForceUpdateFromAnyVersion -ForceApplicationShutdown` 兜底
（即便忘记 bump，也强制覆盖 + 关掉在跑的扩展进程），但**版本号仍应照常 bump**，否则宿主/商店侧判重复。

## 已知限制
- 双卸载入口：MSIX 在「设置>应用」另列一条；单独卸它只清扩展、残留 Host+任务。以主入口卸载为准。
- dev 证书需信任步；换 Partner Center 真证书（R4b）后去掉。
- R4b 待办：WinGet 清单 + GitHub Release 上传 + 真实证书/身份。
