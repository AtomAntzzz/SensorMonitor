# R4 — Inno 安装器分发（Host 随扩展一键装）Design

> 状态：已获设计批准（brainstorming，2026-07-20，方案 = Inno 安装器 + 完整扩展 MSIX + 松散自包含 Host）；待用户复核本 spec。
> 对应路线 `docs/plans/2026-07-18-verification-and-next-phase.md` 的 R4。

## 目标

产出一个**签名的 Inno 安装器（.exe）**，让 SysPulse 能一键装到任意干净 Windows 机（无需装 .NET、无需 dev 环境变量、无需手动跑 `setup.ps1`）：装时一次 UAC 完成「装自包含 Host + 注册计划任务 + 注册扩展 MSIX」，装完运行期全程无 UAC，卸载一处清干净。消除 `SYSPULSE_HOST_EXE` 依赖。分发渠道 = **GitHub Release + WinGet**（installer type: inno），**不走 MS Store**。

## 需求定型（澄清结论）

本设计经多轮验证收敛，关键决策及其证据：

- **MS Store 出局**（spike 证伪，2026-07-20）：① 依赖第三方内核驱动（PawnIO）"默认不允许、仅个案豁免 + 需 EV 证书"；② 提权模型——MSIX 不支持 requireAdministrator、`allowElevation` 基本导致拒审、`/RL HIGHEST` 计划任务被视为绕过提权模型。官方对"确需提权"的建议即"unpackaged/MSI/bootstrapper 优于 Store MSIX"。
- **sparse 包出局**（未经证实、风险高）：sparse 包虽授予身份、理论上支持 `windows.appExtension`，但**无证据表明 CmdPal 会发现 sparse 注册的扩展**（PowerToys 有 `Unpackaged` 枚举值却"never consulted during discovery"，且对纯 out-of-proc COM server 从外置位置激活是额外未知）。放弃。
- **扩展必须完整 MSIX**（已证实）：CmdPal 只从 MSIX 包目录发现 provider（`com.microsoft.commandpalette` AppExtension + out-of-proc COM server）。本项目今天就这么跑、A2 已产出可被发现的签名 MSIX → 沿用**完整 MSIX**（proven），只是**包内不含 Host**。
- **Host 松散装 + 自包含**：Host 是纯 Win32 命名管道服务，不必进 MSIX。由安装器装到稳定路径 `%ProgramFiles%\SysPulse\Host\`；`dotnet publish --self-contained`（win-x64 / win-arm64）→ 目标机不依赖 .NET 8。
- **提权与部署由安装器承担**：Inno 装时已提权 → 直接装 Host 文件 + 注册 `/RL HIGHEST` 任务（`/TR` 指向 ProgramFiles 稳定路径），**无首运 UAC**、无 R4a 的 ProgramData 拷贝/`--setup`/版本追踪机制。
- **MSIX 注册作用域 = 全机预置**（默认，不放安装选项）：用 `Add-AppxProvisionedPackage` 避开"提权后注册到哪个用户"的歧义；不在安装器 UI 放"当前用户/全用户"勾选。
- **双卸载入口 = 文档 + 命名引导**：接受 MSIX 与 Inno 各列一条；正常卸载走 Inno 主入口（连 MSIX/Host/任务一起清），MSIX 组件命名上引导用户别单独卸它。

## 前提事实（已核实）

- 扩展 = 完整 MSIX（`Package.appxmanifest`：`runFullTrust` + `com.microsoft.commandpalette` AppExtension + `com:ExeServer` out-of-proc COM）。A2 已验证签名 MSIX 可被 CmdPal 发现。
- Host = `net8.0` WinExe，`app.manifest` 要求管理员，依赖 `LibreHardwareMonitorLib`（+ PawnIO 内核驱动读全量传感器），当前**框架依赖**（需目标机装 .NET 8）。
- 现有 Host 发现：`LaunchHostCommand.ResolveHostPath()` = env var `SYSPULSE_HOST_EXE`（dev）否则回退 `AppContext.BaseDirectory\Host\SysPulse.Host.exe`。
- 现有提权：计划任务 `SysPulse.Host`（`TaskInstaller`：`/Create /TN SysPulse.Host /TR "<ProcessPath>" /SC ONLOGON /RL HIGHEST /F`），`--install-task`/`--uninstall-task` 已存在；`schtasks /Run` 静默拉起、`/End` 停（均免提权）。`Environment.ProcessPath` 决定 `/TR`。
- MSIX 文件重定向：`%APPDATA%` 写被重定向进包私有存储；`%LOCALAPPDATA%` 行为含糊 → **跨包/进程稳定路径不能放 AppData**。`%ProgramFiles%` / `%ProgramData%` 不受此重定向影响（Host 今天就把日志写 `%ProgramData%\SysPulse\host.log`）。
- 预置签名 MSIX 要求签名证书**被信任**（A2 靠 `Import-Certificate ... TrustedPeople`）。dev 自签证书 → 安装器须先导入公钥到信任库；真实证书（链到受信根）则免此步（R4b）。
- A2 已铺路：双架构（x64/ARM64）MSIX 构建 + signtool/makeappx 签名链路（`docs/references/msix-packaging.md`）。
- R7 空闲自退（5min）与本设计正交：任务 ONLOGON 起、空闲自退、band 轮询静默拉回，均不变。

## 设计

### ① 构建管线（新增）

- **Host 自包含发布**：`dotnet publish src/SysPulse.Host -c Release -r <rid> --self-contained -p:PublishSingleFile=false`，`<rid>` ∈ {`win-x64`, `win-arm64`}。产出含 .NET 运行时的 Host 目录。
- **扩展 MSIX**：复用 A2 链路构建 + 签名（`GenerateAppxPackageOnBuild` + signtool），**包内不加 Host**（与现状一致）。
- **Inno 打包**：新增 `installer/SysPulse.iss`（+ 构建脚本 `installer/build.ps1`）：把「对应架构的自包含 Host 目录」+「签名的扩展 .msix」+「签名证书公钥 .cer」收进安装器；编译出 `SysPulseSetup_<arch>.exe` 并用 A2 证书签名。x64 / ARM64 各一个安装器（或单安装器按 `ProcessorArchitecture` 选装对应 Host）。

### ② 安装器：安装流程（Inno，管理员一次 UAC）

`installer/SysPulse.iss` 关键段：
- `PrivilegesRequired=admin`（装 Host 到 ProgramFiles + 注册 HIGHEST 任务 + 预置 MSIX 均需提权）。
- `[Files]`：自包含 Host → `{autopf}\SysPulse\Host\`（= `%ProgramFiles%\SysPulse\Host\`）；扩展 `.msix`、`.cer` → `{tmp}`。
- `[Run]`（装完，提权上下文，均 `runhidden`）按序：
  1. **导入签名证书**（dev 证书才需；真证书跳过）：`certutil -addstore TrustedPeople "{tmp}\SysPulseDev.cer"` —— 否则预置签名 MSIX 失败。
  2. **注册计划任务**：跑**已装好的** `{autopf}\SysPulse\Host\SysPulse.Host.exe --install-task` —— `TaskInstaller.Install()` 以 `Environment.ProcessPath` 作 `/TR`，即 ProgramFiles 稳定路径。（等价直接 `schtasks /Create`，此处复用 Host 现成逻辑保持 DRY。）
  3. **注册扩展 MSIX（全机预置）**：`powershell -Command "Add-AppxProvisionedPackage -Online -PackagePath '{tmp}\SysPulseExtension_<ver>_<arch>.msix' -SkipLicense"`；并 `Add-AppxPackage -Register` 让**当前登录用户即时可用**（预置只保证新登录/其它用户；当前会话需显式注册一次）。
  4. **可选即时启动**：`schtasks /Run /TN SysPulse.Host`（装完立即有读数，免等下次登录）。

### ③ 运行时（装完之后全程无 UAC）

- 计划任务装时已注册 → 扩展 `SnapshotCache` band 轮询走 `LaunchHostCommand.TryLaunchSilent()` = `schtasks /Run` **静默**拉起；登录 ONLOGON 自启；R7 空闲自退；静默重连。**没有首运 UAC**。
- **扩展唯一代码改动**：`LaunchHostCommand.ResolveHostPath()` 回退路径由 `AppContext.BaseDirectory\Host\...` 改为
  `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SysPulse", "Host", "SysPulse.Host.exe")`。
  env var 仍优先（dev 覆盖不变）。`TryLaunch` 的回退直接-UAC-起-exe 分支保留作安全网（任务意外缺失时）。
- **Host 业务零改**：自包含只是发布开关；`--install-task`/`--uninstall-task` 保留供安装器调用。R4a 的 `--setup`/`HostDeployment`/ProgramData 拷贝**均不需要**。

### ④ 安装器：卸载流程（一处清干净）

- `[UninstallRun]`（卸载时，提权，`runhidden`）：
  1. `{app}\Host\SysPulse.Host.exe --uninstall-task`（删计划任务；等价 `schtasks /Delete /TN SysPulse.Host /F`）。先 `schtasks /End` 停掉在跑的 Host 免锁文件。
  2. `powershell -Command "Get-AppxPackage *SysPulseExtension* | Remove-AppxPackage; Remove-AppxProvisionedPackage -Online -PackageName <full>"` 移除扩展（当前用户 + 预置）。
- `[UninstallDelete]`：删 `{app}`（ProgramFiles\SysPulse）；删 `%ProgramData%\SysPulse\`（日志）。
- **双入口引导**：扩展 MSIX 的**包 DisplayName** 后缀区分（如 `SysPulse (extension component)`），并在 README/Release note 注明"卸载请走『程序和功能 → SysPulse』主入口，勿单独卸 MSIX 组件"（单独卸 MSIX 只清扩展、残留 Host+任务，退化为手动清理）。CmdPal 面板内展示名（`uap3:AppExtension DisplayName`）仍保持 `SysPulse` 不受影响。

## 边界 / 错误处理

- **per-user 注册歧义**：默认全机预置（`Add-AppxProvisionedPackage`）+ 当前用户 `Add-AppxPackage -Register`，避开"提权到别的 admin 账号"导致当前用户不可见。
- **证书信任**：dev 证书须先 `certutil -addstore TrustedPeople`，否则预置签名 MSIX 报不受信任；真实证书（R4b）免此步。
- **Host 在跑锁文件**：卸载/覆盖装前 `schtasks /End`。
- **架构不匹配**：安装器按 `ProcessorArchitecture` 选装对应 RID 的 Host + 对应架构 MSIX；x64/ARM64 分别出安装器（或单器内选）。
- **升级重装**：覆盖装 → Inno 覆盖 Host 文件 + 重注册任务（`/F` 幂等）+ `Add-AppxPackage` 高版本覆盖 MSIX。任务 `/TR` 路径不变。
- **dev 流不受影响**：开发仍走 VS Deploy（松散 MSIX）+ `SYSPULSE_HOST_EXE` + `setup.ps1`；安装器只服务分发。

## 测试

- **Host 单测**：Host 近乎零改 → 现有 12 单测应仍全绿（回归确认，非新增）。
- **手动验收（关键，需干净 VM 或第二台机）**：
  1. 跑 `SysPulseSetup_x64.exe` → **一次 UAC** → 无报错完成。
  2. 打开 CmdPal → 出现 SysPulse 扩展 + Dock 有实时读数（**无 `SYSPULSE_HOST_EXE`、无 .NET 8 预装**）。
  3. 重启机器 → 登录后任务自启、读数恢复，全程无 UAC。
  4. 卸载（走 Inno 主入口）→ 计划任务、`%ProgramFiles%\SysPulse\`、`%ProgramData%\SysPulse\`、扩展包**全部清除**。
  5. 覆盖装高版本 → 正常升级、读数正常。

## 验收清单

1. `installer/` 产出**签名**的 `SysPulseSetup_x64.exe`（ARM64 同理），内含自包含 Host + 签名 MSIX + 证书。
2. 干净机一次 UAC 装完即用，CmdPal 出扩展 + 实时读数，无 env var / 无 .NET 依赖。
3. 装完运行期（含重启登录、band 轮询拉回）全程无 UAC。
4. Inno 主入口卸载一处清干净（任务 + Host 文件 + 日志 + 扩展包）。
5. 扩展侧仅 `ResolveHostPath` 一处改动；Host 12 单测仍全绿。

## 明确不做（YAGNI）

- MS Store 提交（spike 证伪）、sparse 包（未证实/风险）。
- WinGet 清单编写 + GitHub Release 上传/提交链路（**R4b** 后续；本期只产出可分发的签名安装器 + 复现文档）。
- Partner Center 真实身份 / 真实代码签名证书（仍用 A2 dev 自签；换真证书属 R4b，届时去掉证书导入步）。
- Host `--setup` / 自更新 / ProgramData 拷贝 / 版本追踪（安装器装时一次搞定，均不需要）。
- 扩展 MSIX 里塞 Host（改由安装器并列装）。
- 安装器 UI 放"当前用户 / 全用户"选项（默认全机预置，够用）。
- 彻底消除双卸载入口（installer+完整 MSIX 下无干净办法，仅文档 + 命名缓解）。
