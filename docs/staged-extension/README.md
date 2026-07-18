# 暂存的扩展源码（Task 6–8）—— 拷贝参考 + 构建排错

> 由 Agent 预写。**Phase 1（Host）已完整实现并验证**；Phase 2/3（扩展）依赖 GUI 工具链
> （CmdPal 模板生成器 / Visual Studio Deploy / PowerToys Dock / UAC）。
>
> ✅ **2026-07-18 更新**：桌面会话已生成模板并拷入这些文件，Agent 修正了拷贝位置/Provider/
> 目标框架等问题，扩展项目**已能通过 C# 编译**（`dotnet build` 0 CS 错误）。本目录保留为
> 权威副本；下方「构建排错」记录了实际踩到的坑，重装/换机复现时照做。

## 目标位置（⚠️ 模板是双层同名嵌套）

生成器产出的结构是外层解决方案文件夹 + 内层同名**项目**文件夹：

```
src/SensorMonitorExtension/                    ← 解决方案层（.sln、Directory.*.props、nuget.config）
└─ SensorMonitorExtension/                     ← 真正的项目（.csproj 在这里！）
    SensorMonitorExtension.csproj
    SensorMonitorExtensionCommandsProvider.cs
    Ipc/ Dock/ Commands/                       ← 本目录的 .cs 必须放这一层（与 csproj 同级）
```

| 暂存文件 | 目标位置 | 状态 |
|----------|----------|------|
| `Ipc/SensorSnapshot.cs` | `src/SensorMonitorExtension/SensorMonitorExtension/Ipc/` | ✅ 已编译通过 |
| `Ipc/PipeSensorClient.cs` | `…/SensorMonitorExtension/Ipc/` | ✅ 含释放竞态修正 |
| `Dock/SensorDockBand.cs` | `…/SensorMonitorExtension/Dock/` | ✅ 业务逻辑已按实测修正 |
| `Commands/LaunchHostCommand.cs` | `…/SensorMonitorExtension/Commands/` | ✅ |

> ⚠️ 放到**外层**（与 .sln 同级）不会被编译 —— csproj 默认按项目目录递归包含 .cs，外层不在其内。

## 相对计划原稿的修正（均来自实测/实证）

1. **`PipeSensorClient` 释放顺序**：`reader` 先声明、`writer` 后声明，`writer` 先 flush 于仍开的管道；
   catch 增加 `ObjectDisposedException`。原稿顺序会在清理时抛未捕获异常（Host 侧 Task 4 已复现）。
2. **`FormatLine` CPU 频率匹配**：用 `Id` 前缀 `/intelcpu`|`/amdcpu`，**不**用 `Name.Contains("Core")`
   （后者命中 GPU Core 时钟，把 GPU 频率误显为 CPU）。见 `docs/references/sensor-sources.md` 末尾。
3. **显式 `using`**：本项目未开 `ImplicitUsings`，四个文件都补了 `using System;` 等（否则
   `DateTimeOffset/IReadOnlyList/Timer/IDisposable` 报 CS0246）。

## Provider 挂接（改 `…/SensorMonitorExtension/SensorMonitorExtensionCommandsProvider.cs`）

⚠️ 字段和方法必须放在**类体内部**，不是命名空间层。最终形态（已应用）：

```csharp
using SensorMonitorExtension.Dock;   // 加这一行 using

public partial class SensorMonitorExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly SensorDockBand _band = new();          // ← 类内字段

    public SensorMonitorExtensionCommandsProvider() { /* 模板原样 */ }
    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands()          // ← 类内方法
        => [new WrappedDockItem([_band], "com.sensormonitor.dock", "Sensor Monitor")];
}
```

## 构建排错（本机实际踩到的坑）

生成项目默认 **`net10.0`**；本机只有 .NET SDK 9.0.310 且 VS < 18，直接 Deploy 报两个错。已改法：

| 现象 | 位置 | 改法 |
|------|------|------|
| `NETSDK…当前 VS 不支持面向 .NET 10.0` | csproj `<TargetFramework>` | `net10.0-windows10.0.26100.0` → **`net9.0-windows10.0.26100.0`** |
| `PublishReadyToRun 找不到有效运行时包` | `Properties/PublishProfiles/win-{x64,arm64}.pubxml` | `<PublishReadyToRun>` → **`False`**（Deploy/调试不需要 R2R） |
| `CS0246 找不到 DateTimeOffset/Timer…` | 四个拷入的 .cs | 已补显式 `using`（见上「修正 3」） |
| `APPX3217 UAP 10.0.26100.0 SDK folder cannot be located` | MSIX PRI 打包步骤 | **命令行构建缺 Windows SDK 10.0.26100**。VS 的 MSIX 工具链通常自带；若 VS Deploy 仍报此错，用 **VS Installer → 单个组件**装 **Windows 11 SDK (10.0.26100)**。 |

> 备选（不降级）：装 .NET 10 SDK **且**升级到 Visual Studio 18.0+，即可保持模板原生 net10。
> 本机走的是降级到 net9 路线（改动小、无需装 VS 预览版）。

## 桌面会话继续步骤（Task 6/7/8）
1. VS 重新加载解决方案（拾取移动后的文件与 net9 改动）。
2. `Build → Deploy SensorMonitorExtension`（**必须 Deploy**，非 Build）。若报 APPX3217 见上表。
3. 命令面板运行 `Reload`；PowerToys 设置 → Dock → 添加本扩展 band。
4. 设环境变量指向已构建 Host（Task 8）：
   `SENSORMONITOR_HOST_EXE=D:\Workspace\SensorMonitor\src\SensorMonitor.Host\bin\Debug\net8.0\SensorMonitor.Host.exe`
   （需 CmdPal 进程可继承处设置：系统级，或 setx 后重启 PowerToys）。
5. 预期：Dock 弹 UAC → 接受 → 数秒内显示读数并每 2s 刷新；关掉 Host ≤2s 变「Host 未运行」。
   本机（未装 PawnIO）只有 **GPU 温度**有真实值，CPU 频率/主板温度显示 `--`（正确降级，装 PawnIO 才有，路线 R5）。

### 关键验证点
- **Dock 随 Title 变更重绘**（Task 6）：不刷新则对照官方 Time & Date 扩展 `NowDockBand`
  的属性变更通知写法（PowerToys 仓库 `src/modules/cmdpal/ext/`）。
- **跨完整性级别管道**（Task 5/7）：提权 Host ← 非提权扩展。Host 侧 ACL 已放开 Authenticated Users。
  Phase 1 已验证同权限连通（71 传感器、GPU 48°C），非提权→提权这条真正跨级路径待扩展部署后走到。
