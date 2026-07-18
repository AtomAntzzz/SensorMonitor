# 暂存的扩展源码（Task 6–8）—— 待 Task 1 生成模板后拷入

> 由 Agent 在执行 MVP 计划时预写。**Phase 1（Host）已完整实现并验证**，Phase 2/3（扩展）
> 因依赖 GUI 工具链（CmdPal 模板生成器 / Visual Studio Deploy / PowerToys Dock / UAC）
> 无法在无人环境完成，故把扩展侧源码暂存于此，供你在桌面会话里拷入生成的项目。
>
> ⚠️ **不要**在运行 CmdPal「Create a new extension」之前把这些文件放进 `src/SensorMonitorExtension/`
> —— 生成器要求目标目录不存在/为空，预置文件会导致生成失败。**先生成，再拷入。**

## 文件与验证状态

| 暂存文件 | 目标位置（生成项目内） | 状态 |
|----------|----------------------|------|
| `Ipc/SensorSnapshot.cs` | `src/SensorMonitorExtension/Ipc/SensorSnapshot.cs` | ✅ 纯 BCL，已验证正确 |
| `Ipc/PipeSensorClient.cs` | `src/SensorMonitorExtension/Ipc/PipeSensorClient.cs` | ✅ 纯 BCL，含 Task 4 复现过的释放竞态修正 |
| `Dock/SensorDockBand.cs` | `src/SensorMonitorExtension/Dock/SensorDockBand.cs` | ⚠️ 依赖 Toolkit API，未编译验证；业务逻辑已按实测修正 |
| `Commands/LaunchHostCommand.cs` | `src/SensorMonitorExtension/Commands/LaunchHostCommand.cs` | ⚠️ 依赖 Toolkit API，未编译验证 |

## 相对计划原稿的两处修正（均来自 Phase 1 实测/实证）

1. **`PipeSensorClient` 释放顺序**：`reader` 先声明、`writer` 后声明，使 `writer` 先 flush
   于仍打开的管道；并把 `ObjectDisposedException` 加进 catch。计划原稿顺序会在清理时
   抛未捕获的 `ObjectDisposedException`（已在 `PipeJsonServerTests` 复现，见 Task 4 提交）。
2. **`FormatLine` 的 CPU 频率匹配**：用 `Id` 前缀 `/intelcpu`|`/amdcpu` 限定，**不**用
   `Name.Contains("Core")`。后者在实测机上会命中 GPU Core 时钟（RTX 3080 = 1710MHz），
   把 GPU 频率误显为 CPU。详见 `docs/references/sensor-sources.md` 末尾实测清单。

## Provider 挂接（改生成项目里的 `SensorMonitorExtensionCommandsProvider.cs`）

```csharp
using SensorMonitorExtension.Dock;
using Microsoft.CommandPalette.Extensions.Toolkit;

// 字段：
private readonly SensorDockBand _band = new();

// 覆写（SDK ≥ 0.9.260303001 的 ICommandProvider3）：
public override ICommandItem[]? GetDockBands()
{
    return [new WrappedDockItem([_band], "com.sensormonitor.dock", "Sensor Monitor")];
}
```

> `WrappedDockItem` 具体重载以生成项目里 Toolkit 的实际签名为准（见
> `docs/references/cmdpal-extension.md` §Dock API）。每个 band item 的 `Command`
> 必须有非空 `Id`（本项目 band 的 Command 是 `LaunchHostCommand`，已设 Id）。

## 你需要在桌面会话里手动完成的步骤

### A. Task 1 — 生成扩展模板（GUI）
1. PowerToys 设置确认存在 **Dock** 功能页（需 2026-03 后版本）。
2. 命令面板运行 **`Create a new extension`**：
   - ExtensionName: `SensorMonitorExtension`
   - Display Name: `Sensor Monitor`
   - Output Path: `D:\Workspace\SensorMonitor\src`
3. 打开生成的 `Directory.Packages.props`/csproj，确认 `Microsoft.CommandPalette.Extensions`
   ≥ `0.9.260303001`，不足则升级。
4. `.gitignore` 已确认不忽略 `launchSettings.json`/`*.pubxml`（Agent 已核对）。

### B. 拷入暂存源码并挂接 Provider
把本目录下四个 `.cs` 按上表拷到 `src/SensorMonitorExtension/` 对应子目录；按上面片段改
Provider。（可选：先按计划 Task 6 用假数据版验证 Dock 会随 `Title` 变更重绘，再切真实数据版。）

### C. Task 6/7/8 — 部署与验证循环（VS + PowerToys）
1. VS 打开 `SensorMonitorExtension.sln` → `Build → Deploy SensorMonitorExtension`（**必须 Deploy**）。
2. 命令面板运行 `Reload`。
3. PowerToys 设置 → Dock → 添加本扩展 band。
4. 设环境变量让扩展找到已构建的 Host（Task 8）：
   `SENSORMONITOR_HOST_EXE=D:\Workspace\SensorMonitor\src\SensorMonitor.Host\bin\Debug\net8.0\SensorMonitor.Host.exe`
   —— 需在 CmdPal 进程能继承处设置（系统级，或 setx 后重启 PowerToys）。
5. 预期：Dock 弹 UAC → 接受 → 数秒内显示真实读数并每 2s 刷新；关掉 Host ≤2s 变「Host 未运行」。
   - 本机（未装 PawnIO）预期只有 **GPU 温度**有真实值，CPU 频率/主板温度显示 `--`（正确降级）。
     要三项齐全需装 PawnIO（后续路线 R5）。

### D. 关键验证点
- **Dock 随 Title 变更重绘**（Task 6）：若不刷新，对照官方 Time & Date 扩展 `NowDockBand`
  的属性变更通知写法（PowerToys 仓库 `src/modules/cmdpal/ext/`）。
- **跨完整性级别管道连通**（Task 5/7）：提权 Host ← 非提权扩展。Phase 1 已验证同权限进程间
  连通（71 传感器、GPU 48°C），但**非提权→提权**这条真正的跨级路径要等扩展部署后才走到；
  Host 侧 ACL 已显式放开 Authenticated Users（`PipeJsonServer`）。
