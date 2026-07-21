# 新机器一键引导脚本 Design

**Goal:** 在全新 Windows 机器上，用一个自提权、幂等的脚本把 SensorMonitor 的开发环境从零拉起到"能构建、能跑测试、扩展已部署、Host 计划任务已注册"，把原本散落在多篇文档里的手动步骤收敛成一次运行。

**决策前提（已与用户确认）:**
- 脚本范围：**全套安装**（含系统级工具链，不只是项目引导）。
- Visual Studio：**不装 VS，纯 CLI**（`dotnet` + PowerShell `Add-AppxPackage` 复刻 VS Deploy）。
- 阶段 4 CLI 部署失败：**尽力而为 + 打印原始错误 + 给手动兜底**，不阻断其余阶段。
- PawnIO 内核驱动：**默认静默安装**，总结里标注可能需重启。

**Tech Stack:** Windows PowerShell 5.1+（系统自带）、winget（已确认本机 v1.29）、既有 .NET CLI。无新增代码依赖。

---

## 交付物

| 文件 | 作用 |
|------|------|
| `scripts/setup.ps1` | 主脚本：自提权、分阶段、幂等 |
| `scripts/setup.cmd` | 薄包装：`powershell -ExecutionPolicy Bypass -File %~dp0setup.ps1 %*`，新机器双击/命令行即用，绕过默认 ExecutionPolicy |
| `CLAUDE.md`（改） | "常用命令"补一行 `scripts\setup.cmd`；坑 #1 待真机跑通 CLI 部署后回写确切命令 |

## 架构

单文件 PowerShell 脚本，结构为「预检 → 6 个幂等阶段 → 总结报告」。

**自提权：** 脚本开头检测 `[Security.Principal.WindowsPrincipal]` 是否 Administrator；否则 `Start-Process powershell -Verb RunAs` 重启自身并退出当前非提权实例 → 全程一次 UAC。

**幂等：** 每阶段先检测当前状态（`winget list <id>`、注册表值、`schtasks /Query`、Appx 包是否已注册），已满足就跳过并标 `已就绪`。可反复重跑。

**仓库根定位：** 由 `$PSScriptRoot` 上溯一级（脚本固定在 `scripts/` 下），不写死盘符路径。

## 参数

| 参数 | 行为 |
|------|------|
| （无） | 全流程 |
| `-CheckOnly` | 只跑各阶段的**检测**逻辑，打印体检报告（缺什么 + 对应安装命令），不改系统、不提权 |
| `-SkipInstall` | 跳过阶段 1（工具链已装时，只跑构建/部署/计划任务） |

## 执行阶段

| # | 阶段 | 检测（幂等键） | 动作 | admin |
|---|------|----------------|------|:---:|
| 0 | 预检 | winget 是否存在（`Get-Command winget`）；是否 admin | winget 缺失 → 报错退出并指向 App Installer；非 admin 且非 `-CheckOnly` → 自提权重启 | — |
| 1 | 工具链 | **功能性探测**（.NET 用 `dotnet --list-runtimes/--list-sdks`；PowerToys 查 exe 落点；PawnIO 查服务/目录/注册表三路信号）——winget-ID 检测会漏报非 winget 渠道安装，仅作安装通道 | `winget install -e --id` 装 `Microsoft.DotNet.Runtime.8`、`Microsoft.DotNet.SDK.9`、`Microsoft.PowerToys`、`namazso.PawnIO`（`--silent --accept-package-agreements --accept-source-agreements`）。.NET 8 只需运行时：构建统一由 SDK 9 承担（net8.0 targeting pack 走 NuGet），运行需 `Microsoft.NETCore.App 8.0`（major 不 roll-forward，装 SDK 9 不覆盖） | ✅ |
| 2 | 开发者模式 | 读 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock\AllowDevelopmentWithoutDevLicense` | 置 `1`（松散 MSIX 注册前提） | ✅ |
| 3 | Host 构建+测试 | — | `taskkill /f /im SensorMonitor.Host.exe`（忽略未运行，避坑 #6 锁 bin）→ `dotnet restore SensorMonitor.sln` → `dotnet build` → `dotnet test tests/SensorMonitor.Host.Tests`（期望 11 绿） | — |
| 4 | 扩展部署 | 目标 Appx 包是否已注册（`Get-AppxPackage`） | `dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Debug -p:Platform=x64` → 定位输出 `AppxManifest.xml` → `Add-AppxPackage -Register <manifest>`。**尽力而为**：失败则打印原始错误 + 手动兜底提示，不阻断阶段 5/6 | — |
| 5 | 计划任务 | `schtasks /Query /TN SensorMonitor.Host` | 跑阶段 3 构建出的 `SensorMonitor.Host.exe --install-task`（D7 静默提权通道） | ✅ |
| 6 | 总结 | — | 打印每阶段 ✅/⚠/❌ + 人工收尾清单：PawnIO 装后可能需**重启**；装 PawnIO 并重启后 CPU 传感器才全（扩展重载由阶段 4 的 `x-cmdpal://reload` 自动完成，无需手动 Reload） | — |

## 关键风险与处理（诚实项）

1. **阶段 4 CLI 部署 —— 已实测通过（2026-07-18 本机 spike）。**
   - 验证结果：`dotnet build 扩展 -c Debug -p:Platform=x64` 产出松散 `AppxManifest.xml`（路径 `bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\AppxManifest.xml`）；`Add-AppxPackage -Register <manifest>` 成功，`Get-AppxPackage SensorMonitorExtension` → `Status: Ok`。**CLI 可替代 VS Deploy**（前提：开发者模式已开，阶段 2 保证）。
   - **外部重新加载亦可脚本化**（spike 确认）：CmdPal 设置 `AllowExternalReload` 存于 `%LOCALAPPDATA%\Packages\<CmdPal PackageFamilyName>\LocalState\settings.json`（本机 `Microsoft.CommandPalette_8wekyb3d8bbwe`）；置 `true` 后 `Start-Process 'x-cmdpal://reload'` 可无 GUI 触发扩展重载（`x-cmdpal` 协议已注册）。→ 阶段 4 追加：注册后 patch 该键为 `true` 并触发 reload，**"CmdPal 里手动 Reload"从人工收尾降级为自动**。
   - 保留兜底：若某新机器上 build 未生成松散布局或 runtime 包缺，仍**如实报错**并提示补 WinAppSDK runtime / 退回 VS Deploy，不假装成功。
   - 待办：跑通后把 CLI 部署命令回写 CLAUDE.md 坑 #1（"必须 VS Deploy"→"CLI 亦可，命令如下"）。
2. **PawnIO 是签名内核驱动**：静默装，装后可能需重启；本机主板 SuperIO 芯片 LHM 0.9.6 不认（主板温度照缺，非驱动问题，脚本不承诺解决）。
3. **网络依赖**：winget 阶段需联网；离线机器阶段 1 会失败——总结如实标注，不静默吞。

## 错误处理约定

- 每阶段独立 try/catch，产出 `已就绪 / 已完成 / ⚠ 部分 / ❌ 失败(原因)` 之一，写入总结表。
- 阶段 3 测试失败 → 非零退出（构建/测试红是硬失败）。
- 阶段 4 失败 → **不**改变退出码（尽力而为项），仅在总结标 ⚠。
- 全程 `-ErrorAction` 显式控制，避免静默吞异常。

## 测试策略

- 无自动化单测（脚本本身是环境编排，副作用是系统级安装，难在 CI 沙盒复现）。
- 验证靠：① `-CheckOnly` 在本机跑，核对检测逻辑输出与实际环境一致；② 真机全流程跑一遍，逐阶段核对总结表与手动预期；③ 二次重跑验证幂等（全部 `已就绪`，无重复安装）。

## 不做（YAGNI）

- 不装 Visual Studio（用户已选纯 CLI）。
- 不做卸载/回滚脚本（新机器引导，反向需求另立）。
- 不做 CI 集成、不做多机批量。
- 不封装 PawnIO 重启自动化（交用户决定重启时机）。
