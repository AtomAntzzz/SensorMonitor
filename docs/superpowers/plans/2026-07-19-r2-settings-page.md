# R2 设置页（刷新间隔 + 温度单位）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给扩展加一个 CmdPal 内置 Settings 设置页，承载两个全局项——刷新间隔（1s/2s/5s）与温度单位（°C/°F）。

**Architecture:** 走 CmdPal 内置 `Settings`（宿主自动渲染 + 持久化），不扩展 `slots.json`、不自建 UI。
`SettingsManager` 持有两个 `ChoiceSetSetting`，`SettingsChanged` 时把值推给两个静态消费点：
`SnapshotCache`（间隔）与 `TempDisplay`（温度单位纯转换助手）。Provider 设 `Settings = manager.Settings`
即出设置页。纯扩展侧改动，Host 零改动。

**Tech Stack:** C# / .NET (net9-windows) · CmdPal Extensions SDK Toolkit（`ChoiceSetSetting`/`Settings`）。

> **测试策略（重要，源自已批准 spec）**：本扩展**无单测工程**（仅 `tests/SensorMonitor.Host.Tests`，net8+Host），
> 且给 net9-windows/WinRT 扩展加测试工程摩擦大、经用户确认按 YAGNI **不做**。故各代码任务以
> **`dotnet build` 通过**为验证闸，功能正确性走 Task 6 的**实机手动验证**（与扩展侧 `SlotLogic` 现状一致）。
> 这偏离 writing-plans 的 TDD 默认，是 spec 明确决策（用户指令 > 技能默认）。

> **构建前先杀扩展进程**（坑 #6：松散注册的扩展被 CmdPal 激活后常驻、锁死 `bin/`）：
> 每次 build 前执行 `taskkill //f //im SensorMonitorExtension.exe 2>/dev/null || true`（无需提权）。
> Git Bash 下 `taskkill` 参数用 `//f //im`（双斜杠避免路径转换）。

---

### Task 1: TempDisplay 纯转换助手

**Files:**
- Create: `src/SensorMonitorExtension/SensorMonitorExtension/Settings/TempDisplay.cs`

- [ ] **Step 1: 创建纯静态转换助手**

按 `Unit == "°C"` 判温度（band/选择页拿到的 `SlotCandidate` 无 `Type`，只能按 Unit）：

```csharp
namespace SensorMonitorExtension.Settings;

/// <summary>
/// 温度显示单位转换（纯函数）。Fahrenheit 由 SettingsManager 依设置写入。
/// 按 Unit=="°C" 判温度（Host 仅温度输出 °C，SensorMapper.UnitOf）：
/// 非 °C 原样透传；°C 按当前单位换算（°F = °C·9/5+32）。三处显示位统一调用。
/// </summary>
internal static class TempDisplay
{
    /// <summary>仅 SettingsManager 写；显示位读。bool 读写原子，无需锁。</summary>
    public static bool Fahrenheit;

    public static (double Value, string Unit) Format(double value, string unit)
        => unit == "°C"
            ? (Fahrenheit ? value * 9 / 5 + 32 : value, Fahrenheit ? "°F" : "°C")
            : (value, unit);
}
```

- [ ] **Step 2: 构建验证**

先杀扩展进程再 build：

```bash
taskkill //f //im SensorMonitorExtension.exe 2>/dev/null || true
dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -p:Platform=x64
```

Expected: `Build succeeded`，0 Error。

- [ ] **Step 3: 提交**

```bash
git add src/SensorMonitorExtension/SensorMonitorExtension/Settings/TempDisplay.cs
git commit -m "feat: 温度单位纯转换助手 TempDisplay（°C/°F）"
```

---

### Task 2: SnapshotCache 可配间隔 + 显示变更通知

**Files:**
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/Ipc/SnapshotCache.cs`

- [ ] **Step 1: 把 RefreshMs 由 const 改可配字段**

将第 15 行 `private const int RefreshMs = 1000;` 改为：

```csharp
    private static int _refreshMs = 1000;   // 可配（R2 设置页）；int 读写原子，Timer 读/设置写无需锁

    /// <summary>设置刷新间隔（ms）。夹下限防 0/负值把 Timer 打成忙循环。下一轮重排生效。</summary>
    public static void SetIntervalMs(int ms) => _refreshMs = System.Math.Max(200, ms);

    /// <summary>温度单位等纯显示项变更后，令订阅方（dock band）以最新快照重绘。
    /// Updated 为私有 event，外部无法直接 invoke，故加此公开触发口。</summary>
    public static void NotifyDisplayChanged()
    {
        try { Updated?.Invoke(); } catch { /* 订阅方自行防崩，F3；此处再兜一层 */ }
    }
```

- [ ] **Step 2: 让重排读取可配字段**

将第 62 行 `try { _timer?.Change(RefreshMs, Timeout.Infinite); } catch (ObjectDisposedException) { }` 中的
`RefreshMs` 改为 `_refreshMs`：

```csharp
            try { _timer?.Change(_refreshMs, Timeout.Infinite); } catch (ObjectDisposedException) { }
```

- [ ] **Step 3: 构建验证**

```bash
taskkill //f //im SensorMonitorExtension.exe 2>/dev/null || true
dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -p:Platform=x64
```

Expected: `Build succeeded`，0 Error。

- [ ] **Step 4: 提交**

```bash
git add src/SensorMonitorExtension/SensorMonitorExtension/Ipc/SnapshotCache.cs
git commit -m "feat: SnapshotCache 刷新间隔可配 + 显示变更通知口"
```

---

### Task 3: SettingsManager（CmdPal 内置 Settings）

**Files:**
- Create: `src/SensorMonitorExtension/SensorMonitorExtension/Settings/SettingsManager.cs`

- [ ] **Step 1: 创建 SettingsManager**

两个 `ChoiceSetSetting`；`SettingsChanged` 与构造末尾各调一次 `Apply()`（首帧前推持久化初值）。
注意 `using` 别名避开命名空间 `SensorMonitorExtension.Settings` 与 Toolkit 类型 `Settings` 撞名：

```csharp
using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SensorMonitorExtension.Ipc;
using CmdPalSettings = Microsoft.CommandPalette.Extensions.Toolkit.Settings;

namespace SensorMonitorExtension.Settings;

/// <summary>
/// 扩展全局设置（走 CmdPal 内置 Settings，宿主自动渲染/持久化）。
/// 两项：刷新间隔（1/2/5s）、温度单位（°C/°F）。变更时推给 SnapshotCache 与 TempDisplay。
/// </summary>
internal sealed class SettingsManager
{
    private readonly CmdPalSettings _settings;

    public SettingsManager()
    {
        _settings = new CmdPalSettings();

        var refreshInterval = new ChoiceSetSetting(
            "refreshInterval", "刷新间隔", "Dock 读数轮询间隔",
            [
                new ChoiceSetSetting.Choice("1 秒", "1000"),
                new ChoiceSetSetting.Choice("2 秒", "2000"),
                new ChoiceSetSetting.Choice("5 秒", "5000"),
            ],
            "1000");

        var tempUnit = new ChoiceSetSetting(
            "tempUnit", "温度单位", "温度显示单位",
            [
                new ChoiceSetSetting.Choice("摄氏 °C", "C"),
                new ChoiceSetSetting.Choice("华氏 °F", "F"),
            ],
            "C");

        _settings.AddSetting(refreshInterval);
        _settings.AddSetting(tempUnit);
        _settings.SettingsChanged += OnSettingsChanged;
        Apply();   // 推持久化初值：首轮轮询即用持久间隔、首帧即用持久单位
    }

    public ICommandSettings Settings => _settings;

    public int RefreshIntervalMs =>
        int.TryParse(_settings.GetSetting<string>("refreshInterval"), out var ms) ? ms : 1000;

    public bool Fahrenheit => _settings.GetSetting<string>("tempUnit") == "F";

    private void OnSettingsChanged(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        SnapshotCache.SetIntervalMs(RefreshIntervalMs);
        TempDisplay.Fahrenheit = Fahrenheit;
        SnapshotCache.NotifyDisplayChanged();   // 令 dock band 立即以新单位重绘
    }
}
```

- [ ] **Step 2: 构建验证**

```bash
taskkill //f //im SensorMonitorExtension.exe 2>/dev/null || true
dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -p:Platform=x64
```

Expected: `Build succeeded`，0 Error。若报 `Settings` 命名歧义，检查 `CmdPalSettings` 别名是否漏用。

- [ ] **Step 3: 提交**

```bash
git add src/SensorMonitorExtension/SensorMonitorExtension/Settings/SettingsManager.cs
git commit -m "feat: SettingsManager——刷新间隔+温度单位（CmdPal 内置 Settings）"
```

---

### Task 4: Provider 接线——出设置页

**Files:**
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtensionCommandsProvider.cs`

- [ ] **Step 1: 在 Provider 里挂 SettingsManager**

第 14-16 行的字段区，`_bands` 之上加：

```csharp
    private readonly Settings.SettingsManager _settings = new();
```

- [ ] **Step 2: 构造函数里设 Settings 属性**

在构造函数 `SensorMonitorExtensionCommandsProvider()` 里（第 24 行 `Icon = ...` 之后、`_commands = ...` 之前）加：

```csharp
        Settings = _settings.Settings;   // 设了才出设置页；初值已在 SettingsManager 构造时 Apply
```

- [ ] **Step 3: 构建验证**

```bash
taskkill //f //im SensorMonitorExtension.exe 2>/dev/null || true
dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -p:Platform=x64
```

Expected: `Build succeeded`，0 Error。

- [ ] **Step 4: 提交**

```bash
git add src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtensionCommandsProvider.cs
git commit -m "feat: Provider 挂 SettingsManager，暴露设置页"
```

---

### Task 5: 三处显示位套用温度单位

**Files:**
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/Dock/SensorSlotBand.cs:99`
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/Pages/SensorPickerPage.cs:55`
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/Pages/SensorMonitorExtensionPage.cs:46`

- [ ] **Step 1: dock band（SensorSlotBand.RefreshCore）**

将第 99 行 `SetDisplay($"{current.Value:F0}{current.Unit}", subtitle);` 改为：

```csharp
        var (dispVal, dispUnit) = Settings.TempDisplay.Format(current.Value, current.Unit);
        SetDisplay($"{dispVal:F0}{dispUnit}", subtitle);
```

- [ ] **Step 2: 选择页（SensorPickerPage.GetItems）**

将第 52-57 行的 `ListItem` 里 `Title` 行改为先转换再拼串。把：

```csharp
            items.Add(new ListItem(new SelectSensorCommand(_band, c.Key))
            {
                // 当前项标题前置普通 Unicode ✓（避免与列表焦点高亮混淆；不碰 PUA 字形）。
                Title = (isCurrent ? "✓ " : "") + $"{c.Label} {c.Value:F0}{c.Unit}",
                Subtitle = isCurrent ? "当前" : "",
            });
```

改为：

```csharp
            var (dispVal, dispUnit) = Settings.TempDisplay.Format(c.Value, c.Unit);
            items.Add(new ListItem(new SelectSensorCommand(_band, c.Key))
            {
                // 当前项标题前置普通 Unicode ✓（避免与列表焦点高亮混淆；不碰 PUA 字形）。
                Title = (isCurrent ? "✓ " : "") + $"{c.Label} {dispVal:F0}{dispUnit}",
                Subtitle = isCurrent ? "当前" : "",
            });
```

- [ ] **Step 3: 浏览页（SensorMonitorExtensionPage.GetItems）**

将第 42-48 行的 `Select` 投影改为逐条转换。把：

```csharp
        items.AddRange(snapshot.Sensors
            .OrderBy(r => r.Hardware).ThenBy(r => r.Type).ThenBy(r => r.Id)
            .Select(r => (IListItem)new ListItem(new NoOpCommand())
            {
                Title = $"{r.Name}: {r.Value:F1} {r.Unit}",
                Subtitle = $"{r.Hardware} · {r.Type}",
            }));
```

改为：

```csharp
        items.AddRange(snapshot.Sensors
            .OrderBy(r => r.Hardware).ThenBy(r => r.Type).ThenBy(r => r.Id)
            .Select(r =>
            {
                var (dispVal, dispUnit) = Settings.TempDisplay.Format(r.Value, r.Unit);
                return (IListItem)new ListItem(new NoOpCommand())
                {
                    Title = $"{r.Name}: {dispVal:F1} {dispUnit}",
                    Subtitle = $"{r.Hardware} · {r.Type}",
                };
            }));
```

- [ ] **Step 4: 构建验证**

```bash
taskkill //f //im SensorMonitorExtension.exe 2>/dev/null || true
dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -p:Platform=x64
```

Expected: `Build succeeded`，0 Error。

- [ ] **Step 5: 提交**

```bash
git add src/SensorMonitorExtension/SensorMonitorExtension/Dock/SensorSlotBand.cs \
        src/SensorMonitorExtension/SensorMonitorExtension/Pages/SensorPickerPage.cs \
        src/SensorMonitorExtension/SensorMonitorExtension/Pages/SensorMonitorExtensionPage.cs
git commit -m "feat: 三处显示位套用温度单位（dock band/选择页/浏览页）"
```

---

### Task 6: 部署 + 实机手动验收

**Files:** 无（部署 + 验证）

> 部署走 Visual Studio（**Deploy**，非 Build）→ CmdPal 面板内 **Reload**（坑 #1）。
> 验证 dock band 数量/行为要**净启 CmdPal**（坑 #9：`x-cmdpal://reload` 跨会话累加 band 制造假象）。

- [ ] **Step 1: 部署**

VS 打开 `src/SensorMonitorExtension/SensorMonitorExtension.sln` → 右键扩展项目 **Deploy**（x64）→ CmdPal 内 Reload。

- [ ] **Step 2: 验收——设置页出现**

CmdPal 设置里出现 **Sensor Monitor** 扩展的两项：刷新间隔、温度单位。
Expected: 两项可见、可改，默认 = 1 秒 / 摄氏 °C。

- [ ] **Step 3: 验收——刷新间隔**

把刷新间隔改 **5 秒** → 观察某个温度/频率 band 读数约每 5s 才跳一次；改回 **1 秒** → 立即恢复每秒跳。
Expected: 生效即时、无需重启扩展。

- [ ] **Step 4: 验收——温度单位**

把温度单位改 **华氏 °F** → 三个温度 band（CPU/GPU/主板温度）+ 单击选择页 + 浏览页里的温度全部转 °F
（如 50°C → 122°F），CPU **频率** band 不受影响仍显 MHz；改回 **°C** → 全部恢复。
Expected: 三处一致转换、非温度项不受影响、切换后 dock band 立即重绘（不用等下一轮）。

- [ ] **Step 5: 验收——持久化 + 全绿回归**

改两项后**净启 CmdPal**（或注销重登）→ 两项设置保留。再跑 Host 单测确认无回归：

```bash
dotnet test tests/SensorMonitor.Host.Tests
```

Expected: 设置持久化保留；12 单测全绿（本期纯扩展侧改动，Host 不受影响）。

- [ ] **Step 6: 收口——更新状态文档**

在 `CLAUDE.md` 「当前状态」段追加一条 R2 完成记录（仿 R7 条目：日期、两项设置、CmdPal 内置 Settings、
纯扩展侧），并在 `docs/plans/2026-07-18-verification-and-next-phase.md` 的 R2 行标 ✅ 完成 + 指向本 plan。

```bash
git add CLAUDE.md docs/plans/2026-07-18-verification-and-next-phase.md
git commit -m "docs: R2 设置页完成收口，更新状态与路线"
```

---

## 自检（plan vs spec）

- **Spec 覆盖**：刷新间隔（Task 2+3+4，Task 6 Step 3 验收）✓；温度单位（Task 1+3+5，Step 4 验收）✓；
  走 CmdPal 内置 Settings（Task 3+4）✓；三处显示位一致（Task 5）✓；持久化（Task 6 Step 5）✓；
  YAGNI 去除项（槽位显隐/过期阈值/空闲时长/测试工程/自建持久化）——plan 不含，符合 spec「明确不做」✓。
- **类型/签名一致**：`TempDisplay.Format(double,string)→(double,string)`（Task 1 定义，Task 5 三处调用一致）✓；
  `SnapshotCache.SetIntervalMs(int)`/`NotifyDisplayChanged()`（Task 2 定义，Task 3 调用）✓；
  `SettingsManager.Settings : ICommandSettings`（Task 3 定义，Task 4 赋给 `Provider.Settings`）✓；
  `_refreshMs`（Task 2 Step 1 定义、Step 2 使用，同名）✓。
- **占位符扫描**：无 TBD/TODO/「类似上文」；每个代码步含完整代码与确切命令 ✓。
