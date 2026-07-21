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

## 已知限制
- 双卸载入口：MSIX 在「设置>应用」另列一条；单独卸它只清扩展、残留 Host+任务。以主入口卸载为准。
- dev 证书需信任步；换 Partner Center 真证书（R4b）后去掉。
- R4b 待办：WinGet 清单 + GitHub Release 上传 + 真实证书/身份。
