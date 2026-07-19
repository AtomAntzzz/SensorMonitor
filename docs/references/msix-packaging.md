# MSIX 打包（本机复现步骤）

> A2 验证产出（2026-07-19）。身份 = `CN=SensorMonitor Dev`（自签名 dev；商店提交时换 Partner Center 身份）。
> 工具：Windows Kits 10.0.26100.0 的 signtool/makeappx，位于
> `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\`（本机 PATH 无 signtool，用全路径）。

## 一次性准备

```powershell
# 1) 自签名代码签名证书（非提权，CurrentUser\My）
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=SensorMonitor Dev" `
    -CertStoreLocation "Cert:\CurrentUser\My" -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(5)
$cert.Thumbprint                      # 记下，签名要用
Export-Certificate -Cert $cert -FilePath "$env:TEMP\SensorMonitorDev.cer"

# 2) 信任导入（管理员，一次 UAC）—— 签名的 .msix 才被系统信任
Import-Certificate -FilePath "$env:TEMP\SensorMonitorDev.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

## 构建 + 签名（每架构）

`<CSPROJ>` = `src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj`；`<THUMB>` = 上面证书 thumbprint。

```powershell
# x64（ARM64 同理换 Platform=ARM64、目录 ARM64）
dotnet build <CSPROJ> -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true `
    -p:AppxBundle=Never -p:AppxPackageDir="AppPackages\x64\"
& "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe" sign /fd SHA256 /sha1 <THUMB> "AppPackages\x64\...\*.msix"
& "...\signtool.exe" verify /pa "AppPackages\x64\...\*.msix"   # 期望 Successfully verified
```

## 合 bundle（商店提交形态）

```powershell
# 把两架构 .msix 拷进一个暂存目录，再合包并签名
& "...\makeappx.exe" bundle /d AppPackages\bundle_stage /p AppPackages\SensorMonitorExtension_0.0.1.0.msixbundle
& "...\signtool.exe" sign /fd SHA256 /sha1 <THUMB> AppPackages\SensorMonitorExtension_0.0.1.0.msixbundle
```

## 本机实装（x64）

```powershell
Get-AppxPackage *SensorMonitor* | Remove-AppxPackage           # 先移除松散/旧版，避身份冲突
Add-AppxPackage -Path AppPackages\x64\...\SensorMonitorExtension_0.0.1.0_x64.msix
(Get-AppxPackage *SensorMonitor*).SignatureKind                # 期望 Developer（非 None）
```
Host **不在包内** —— 靠计划任务 `SensorMonitor.Host` 静默拉起（R4 才随包分发）。

## ⚠ 裁剪发现（Release `PublishTrimmed=true`）—— A2 关键产出

**现象**：首次打包（Release 裁剪）实装后，Dock 4 控件全显"Host 未运行"，但 Host 进程在跑、管道正常返回 17KB JSON、松散 Debug 版正常。

**根因**（隔离 harness 单变量确认）：`PublishTrimmed=true` 在 .NET 9 下置 `JsonSerializerIsReflectionEnabledByDefault=false`，反射式 `JsonSerializer.Deserialize<T>` 抛
`InvalidOperationException: Reflection-based serialization has been disabled`。该异常不在 `PipeSensorClient.TryFetch` 的 catch 过滤器内 → 冒泡到 `SnapshotCache.Refresh` catch-all 吞掉 → `Current` 恒 null → 全"Host 未运行"。`SlotStore` 的 `Dictionary<string,string>` (反)序列化同坑（打包版持久化会静默失效）。

**修复**（commit `fix(ext): source-gen JSON context...`）：新增 `Ipc/SensorJsonContext.cs`——source-generated `JsonSerializerContext`（`[JsonSerializable(SensorSnapshot)]` + `Dictionary<string,string>`，trim/AOT 安全）；`PipeSensorClient`、`SlotStore` 改用其 `JsonTypeInfo` 重载。裁剪构建重装后 Dock 恢复正常读数（本机实测）。

**教训**：任何走 `System.Text.Json` 的类型，在裁剪/AOT 打包前必须用 source-gen 上下文，反射式 API 会在打包版静默失效（Debug 松散版看不出来）。

## R4 待办
- Host 随包分发（打进 MSIX，替换对计划任务的依赖）。
- Partner Center 真实身份 + 商店/WinGet 提交（换 Identity Publisher/csproj `AppxPackagePublisher`）。
- 商店要求 `.msixbundle`（本文档已验证生成链路，未提交）。
