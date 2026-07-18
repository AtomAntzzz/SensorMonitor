# A1 Dock 槽位控件 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把单条合并 Dock band 替换为 4 个独立预设控件（CPU 频率/CPU 温度/GPU 温度/主板温度），类内右键轮换、"启动 Host"沉底、共享快照缓存，交互对齐 Performance Monitor。

**Architecture:** 方案甲——通用 `SensorSlotBand`（ListItem）由声明式 `SlotCategory` 参数化，4 份定义实例化同一类；`SnapshotCache` 单例统一轮询管道（避免 4 控件×2s 打 4 倍串行请求）；纯逻辑收进 `SlotLogic` 静态类。

**Tech Stack:** 同现有扩展（net9.0-windows / CmdPal Toolkit ≥0.9.260303001）。无新依赖。

**Spec:** `docs/superpowers/specs/2026-07-19-a1-dock-slot-bands-design.md`（含验收清单，勿重开设计讨论）

**测试说明（TDD 偏离，有意为之）：** 扩展项目无测试工程（WinUI TFM），spec 已定本期以部署实测清单验收；筛选/轮换纯逻辑集中在 `SlotLogic`/`SlotCategories`（无 UI 依赖），为将来补单测留口。Host 侧代码零改动，既有 11 单测不受影响。

**已核实事实（勿重新查证）：**
- 上下文菜单 API：`ListItem.MoreCommands = [new CommandContextItem(cmd), ...]`（`.github/instructions/cmdpal-extension.instructions.md` 确认）。
- `LaunchHostCommand`（`Commands/LaunchHostCommand.cs`）已有 `Name="启动传感器 Host"`、`Id`、`TryLaunchSilent()`，可直接复用为主命令。
- 标签显隐 = Dock 宿主"编辑停靠栏"内置右键能力，零代码（用户实机确认）。
- 部署序列（本机已验证）：杀 `SensorMonitorExtension.exe`（锁 bin，坑 #6 扩展版）→ `dotnet build -p:Platform=x64` → `Add-AppxPackage -Register` → `x-cmdpal://reload`（AllowExternalReload 已开启）。
- CmdPal settings.json 勿用 ConvertFrom-Json 碰（重复大小写键，见 setup 计划 P13）——本计划不涉及。

**⚠ 风险闸（Task 1 是闸门）：** spec 列了 3 个实现假设（多 band、MoreCommands 渲染、主命令沉底）。Task 1 用一次性冒烟证伪；若 MoreCommands 不渲染，**停下回报用户**再定降级方案（spec 风险 3），不要自行往下做。

---

## 文件结构

| 文件 | 动作 | 职责 |
|------|------|------|
| `src/SensorMonitorExtension/SensorMonitorExtension/Ipc/SnapshotCache.cs` | 新增 | 单例轮询 + 缓存 + 静默拉起节流 |
| `.../Dock/SlotCategory.cs` | 新增 | `SlotCategory` / `SlotCandidate` 两个 record |
| `.../Dock/SlotLogic.cs` | 新增 | Resolve/Cycle 纯函数 |
| `.../Dock/SlotCategories.cs` | 新增 | 4 个类别的声明式定义 |
| `.../Dock/SlotStore.cs` | 新增 | 轮换选择持久化（slots.json） |
| `.../Dock/SensorSlotBand.cs` | 新增 | 槽位控件 + CycleSlotCommand |
| `.../SensorMonitorExtensionCommandsProvider.cs` | 修改 | 返回 4 个 WrappedDockItem |
| `.../Dock/SensorDockBand.cs` | 删除 | 旧合并 band |

> 下文所有相对路径省略前缀 `src/SensorMonitorExtension/SensorMonitorExtension/`。构建/部署命令均在仓库根 `D:/Workspace/SensorMonitor` 执行。

---

## Task 1: 风险证伪冒烟（一次性代码，验证后还原，不提交）

**Files:**
- Modify: `SensorMonitorExtensionCommandsProvider.cs`（临时）

- [ ] **Step 1: 临时改 GetDockBands 返回 3 条 band（现有 1 + 冒烟 2）**

`GetDockBands()` 整体替换为：

```csharp
    public override ICommandItem[]? GetDockBands()
    {
        _band.EnsureStarted();
        var smoke1 = new ListItem(new Commands.LaunchHostCommand())
        {
            Title = "冒烟1",
            Subtitle = "菜单顺序测试",
            MoreCommands =
            [
                new CommandContextItem(new NoOpCommand() { Name = "测试A", Id = "com.sensormonitor.spike.a" }),
                new CommandContextItem(new NoOpCommand() { Name = "测试B", Id = "com.sensormonitor.spike.b" }),
            ],
        };
        var smoke2 = new ListItem(new Commands.LaunchHostCommand())
        {
            Title = "冒烟2",
            Subtitle = "多band测试",
        };
        return
        [
            new WrappedDockItem([_band], "com.sensormonitor.dock", "Sensor Monitor"),
            new WrappedDockItem([smoke1], "com.sensormonitor.spike1", "冒烟1"),
            new WrappedDockItem([smoke2], "com.sensormonitor.spike2", "冒烟2"),
        ];
    }
```

- [ ] **Step 2: 构建 + 部署**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Debug -p:Platform=x64 2>&1 | tail -3
```
Expected: `0 个错误`。

```bash
powershell -NoProfile -Command "Add-AppxPackage -Register 'D:\Workspace\SensorMonitor\src\SensorMonitorExtension\SensorMonitorExtension\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\AppxManifest.xml'; Start-Process 'x-cmdpal://reload'"
```
Expected: 无输出（成功）。

- [ ] **Step 3: 人工核验（需用户看 Dock，逐项记录）**

1. 编辑停靠栏中可见 3 条独立 band（Sensor Monitor / 冒烟1 / 冒烟2），可分别固定 → **多 band 假设成立？**
2. 固定"冒烟1"，非编辑模式右键 → 菜单是否含 测试A / 测试B / 启动传感器 Host → **MoreCommands 渲染假设成立？**
3. 记录菜单顺序：主命令（启动传感器 Host）是否**沉底** → **沉底假设成立？** 若主命令置顶：后续 Task 5 改用 fallback（`Command`=NoOp，三项全放 MoreCommands 末位放启动 Host，单击行为改为无操作——需先回报用户确认）。
4. 编辑停靠栏右键"冒烟1" → 标签 → 关标题/关字幕逐个试 → **宿主标签显隐对自建 band 生效？**

⚠ 若第 2 项失败（MoreCommands 不渲染）：**停止执行本计划**，回报用户定夺（spec 风险 3 的降级：单击=下一个）。

- [ ] **Step 4: 还原冒烟代码（不提交任何内容）**

```bash
cd "D:/Workspace/SensorMonitor" && git checkout -- src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtensionCommandsProvider.cs && git status --short
```
Expected: 无输出（工作区干净）。冒烟结论记录在本任务 Step 3 的勾选注记里即可。

---

## Task 2: SnapshotCache（共享轮询 + 静默拉起节流）

**Files:**
- Create: `Ipc/SnapshotCache.cs`

- [ ] **Step 1: 写实现**

```csharp
using System;
using System.Threading;

namespace SensorMonitorExtension.Ipc;

/// <summary>
/// 全局快照缓存：一个 2s Timer、每周期一次管道请求，所有 Dock 控件读缓存。
/// Host 管道串行处理，多控件各自轮询会互相排队（spec：架构）。
/// 懒启动（F5）：首次 EnsureStarted 才起 Timer。
/// Host 未运行时走静默通道自动拉起，全局 30s 节流（D7，从旧 SensorDockBand 迁来）。
/// </summary>
internal static class SnapshotCache
{
    private const int RefreshMs = 2000;
    private static Timer? _timer;
    private static readonly object Gate = new();
    private static DateTimeOffset _lastAutoLaunch = DateTimeOffset.MinValue;

    /// <summary>最近一次成功取到的快照；Host 未运行/畸形数据为 null。</summary>
    public static SensorSnapshot? Current { get; private set; }

    /// <summary>每轮刷新完成后触发（Timer 线程回调；订阅方自行防崩，F3）。</summary>
    public static event Action? Updated;

    public static void EnsureStarted()
    {
        lock (Gate)
            _timer ??= new Timer(_ => Refresh(), null, 0, RefreshMs);
    }

    private static void Refresh()
    {
        try
        {
            var snapshot = PipeSensorClient.TryFetch();
            if (snapshot?.Sensors is null)
            {
                snapshot = null;  // {"Sensors":null} 畸形数据一并按未运行处理（F3）
                if (DateTimeOffset.Now - _lastAutoLaunch > TimeSpan.FromSeconds(30))
                {
                    _lastAutoLaunch = DateTimeOffset.Now;
                    Commands.LaunchHostCommand.TryLaunchSilent();
                }
            }
            Current = snapshot;
            Updated?.Invoke();
        }
        catch
        {
            // Timer 线程未处理异常会带崩扩展进程（F3）：任何异常都不许出去。
        }
    }
}
```

- [ ] **Step 2: 构建验证**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Debug -p:Platform=x64 2>&1 | tail -3
```
Expected: `0 个错误`。

- [ ] **Step 3: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add src/SensorMonitorExtension/SensorMonitorExtension/Ipc/SnapshotCache.cs && git commit -m "feat(ext): shared snapshot cache with throttled silent relaunch"
```

---

## Task 3: 槽位模型 + 纯逻辑 + 4 个类别定义

**Files:**
- Create: `Dock/SlotCategory.cs`
- Create: `Dock/SlotLogic.cs`
- Create: `Dock/SlotCategories.cs`

- [ ] **Step 1: 写模型 `Dock/SlotCategory.cs`**

```csharp
using System;
using System.Collections.Generic;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension.Dock;

/// <summary>一类 Dock 槽位控件的声明式定义（spec：类别定义表）。加新类别只加一份定义。</summary>
internal sealed record SlotCategory(
    string Id,           // 持久化键 & band ID 后缀，如 "cpuclock"
    string DisplayName,  // 类别名 = 默认项的字幕，如 "CPU 频率"
    string CycleNoun,    // 轮换菜单名词：上一个{CycleNoun}，如 "核心"/"温度点"
    string IconGlyph,    // Segoe Fluent 字形
    string EmptyHint,    // 候选为空时的字幕，如 "需 PawnIO 驱动"
    Func<IReadOnlyList<SensorReading>, List<SlotCandidate>> GetCandidates);

/// <summary>轮换候选。Key 用于持久化：传感器 Id，或合成项保留 Id（SlotLogic.MaxKey）。</summary>
internal sealed record SlotCandidate(string Key, string Label, float Value, string Unit, bool IsDefault);
```

- [ ] **Step 2: 写纯逻辑 `Dock/SlotLogic.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace SensorMonitorExtension.Dock;

/// <summary>候选解析与轮换的纯函数（无 UI 依赖，为将来单测留口，spec：架构）。</summary>
internal static class SlotLogic
{
    /// <summary>合成项"全核最大"的持久化保留 Id（spec：轮换与持久化）。</summary>
    public const string MaxKey = "__max__";

    /// <summary>按 Key 找当前项；Key 缺失/失效（换硬件、无 PawnIO）回退默认项；候选为空返回 null。</summary>
    public static SlotCandidate? Resolve(List<SlotCandidate> candidates, string? savedKey)
    {
        if (candidates.Count == 0) return null;
        if (savedKey is not null)
        {
            var hit = candidates.FirstOrDefault(c => c.Key == savedKey);
            if (hit is not null) return hit;
        }
        return candidates.FirstOrDefault(c => c.IsDefault) ?? candidates[0];
    }

    /// <summary>循环轮换：从当前项偏移 delta（±1），到尾回头。</summary>
    public static SlotCandidate? Cycle(List<SlotCandidate> candidates, string? currentKey, int delta)
    {
        var current = Resolve(candidates, currentKey);
        if (current is null) return null;
        var i = candidates.FindIndex(c => c.Key == current.Key);
        var n = ((i + delta) % candidates.Count + candidates.Count) % candidates.Count;
        return candidates[n];
    }
}
```

- [ ] **Step 3: 写类别定义 `Dock/SlotCategories.cs`**

筛选规则沿用已实机验证的 FormatLine 匹配（CPU 限定 `/intelcpu|/amdcpu` 前缀防误配 GPU 时钟；主板 = `/lpc` 前缀 SuperIO）：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension.Dock;

/// <summary>4 个预设控件的类别定义（spec：类别定义表）。候选按 Id 排序保证轮换顺序稳定。</summary>
internal static class SlotCategories
{
    private static bool IsCpu(SensorReading r) =>
        r.Id.StartsWith("/intelcpu") || r.Id.StartsWith("/amdcpu");

    public static readonly SlotCategory[] All =
    [
        new("cpuclock", "CPU 频率", "核心", "", "需 PawnIO 驱动", s =>
        {
            var clocks = s.Where(r => r.Type == "Clock" && IsCpu(r)).OrderBy(r => r.Id).ToList();
            if (clocks.Count == 0) return [];
            var list = new List<SlotCandidate>
            {
                new(SlotLogic.MaxKey, "全核最大", clocks.Max(r => r.Value), clocks[0].Unit, IsDefault: true),
            };
            list.AddRange(clocks.Select(r => new SlotCandidate(r.Id, r.Name, r.Value, r.Unit, false)));
            return list;
        }),
        new("cputemp", "CPU 温度", "温度点", "", "需 PawnIO 驱动", s =>
            Temps(s, IsCpu, r => r.Name == "CPU Package")),
        new("gputemp", "GPU 温度", "温度点", "", "无 GPU 温度传感器", s =>
            Temps(s, r => r.Id.StartsWith("/gpu"), r => r.Name == "GPU Core")),
        new("boardtemp", "主板温度", "温度点", "", "需 PawnIO 驱动", s =>
            Temps(s, r => r.Id.StartsWith("/lpc"), r => false)),  // 默认=排序首项
    ];

    // 温度类通用构建：defaultMatch 无命中时列表首项兜底为默认（spec：类别定义表）。
    private static List<SlotCandidate> Temps(IReadOnlyList<SensorReading> s,
        Func<SensorReading, bool> match, Func<SensorReading, bool> defaultMatch)
    {
        var sorted = s.Where(r => r.Type == "Temperature" && match(r)).OrderBy(r => r.Id).ToList();
        var defIdx = sorted.FindIndex(r => defaultMatch(r));
        if (defIdx < 0) defIdx = 0;
        return sorted.Select((r, i) => new SlotCandidate(r.Id, r.Name, r.Value, r.Unit, i == defIdx)).ToList();
    }
}
```

- [ ] **Step 4: 构建验证**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Debug -p:Platform=x64 2>&1 | tail -3
```
Expected: `0 个错误`。

- [ ] **Step 5: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add src/SensorMonitorExtension/SensorMonitorExtension/Dock/SlotCategory.cs src/SensorMonitorExtension/SensorMonitorExtension/Dock/SlotLogic.cs src/SensorMonitorExtension/SensorMonitorExtension/Dock/SlotCategories.cs && git commit -m "feat(ext): slot model, cycle logic, and 4 preset category definitions"
```

---

## Task 4: SlotStore（轮换选择持久化）

**Files:**
- Create: `Dock/SlotStore.cs`

- [ ] **Step 1: 写实现**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace SensorMonitorExtension.Dock;

/// <summary>
/// 各控件当前轮换选择的持久化：LocalState\slots.json，形如 {"cpuclock":"__max__",...}。
/// 读到失效 Key 由 SlotLogic.Resolve 回退默认，本类不清理（硬件回来自动恢复，spec：轮换与持久化）。
/// </summary>
internal static class SlotStore
{
    private static readonly string FilePath = Path.Combine(
        ApplicationData.Current.LocalFolder.Path, "slots.json");
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _map;

    public static string? Get(string categoryId)
    {
        lock (Gate)
        {
            Load();
            return _map!.TryGetValue(categoryId, out var v) ? v : null;
        }
    }

    public static void Set(string categoryId, string key)
    {
        lock (Gate)
        {
            Load();
            _map![categoryId] = key;
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_map)); }
            catch (IOException) { }
            catch (System.UnauthorizedAccessException) { }
        }
    }

    private static void Load()
    {
        if (_map is not null) return;
        try { _map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)); }
        catch { _map = null; }  // 文件不存在/损坏 → 全默认
        _map ??= [];
    }
}
```

- [ ] **Step 2: 构建验证**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Debug -p:Platform=x64 2>&1 | tail -3
```
Expected: `0 个错误`。

- [ ] **Step 3: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add src/SensorMonitorExtension/SensorMonitorExtension/Dock/SlotStore.cs && git commit -m "feat(ext): per-slot selection persistence in LocalState slots.json"
```

---

## Task 5: SensorSlotBand + 轮换命令

**Files:**
- Create: `Dock/SensorSlotBand.cs`（含 `CycleSlotCommand`，仅本类使用）

- [ ] **Step 1: 写实现**

> ⚠ 若 Task 1 证伪了"主命令沉底"：把 base ctor 换成 `base(new NoOpCommand() { Id = "com.sensormonitor.slot.noop" })`，并把 `new CommandContextItem(new Commands.LaunchHostCommand())` 追加到 `MoreCommands` 末尾（先回报用户确认单击行为变化）。

```csharp
using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension.Dock;

/// <summary>
/// 一个"类别槽位"Dock 控件：显示类内当前选中传感器，右键 上一个/下一个 轮换（spec：显示规则）。
/// 主命令=启动 Host（单击行为 + 菜单沉底，对齐 Performance Monitor 的"打开任务管理器"模式）。
/// 标题/字幕显隐由 Dock 宿主编辑模式控制，本类不管（A1.3 零代码）。
/// </summary>
internal sealed partial class SensorSlotBand : ListItem
{
    private readonly SlotCategory _cat;
    private string? _currentKey;

    public SensorSlotBand(SlotCategory cat)
        : base(new Commands.LaunchHostCommand())
    {
        _cat = cat;
        Icon = new IconInfo(cat.IconGlyph);
        Title = "--";
        Subtitle = cat.DisplayName;
        _currentKey = SlotStore.Get(cat.Id);
        MoreCommands =
        [
            new CommandContextItem(new CycleSlotCommand(this, cat, -1)),
            new CommandContextItem(new CycleSlotCommand(this, cat, +1)),
        ];
        SnapshotCache.Updated += Refresh;
        Refresh();
    }

    internal void Cycle(int delta)
    {
        var snap = SnapshotCache.Current;
        if (snap?.Sensors is null) return;  // 无数据时轮换无意义
        var next = SlotLogic.Cycle(_cat.GetCandidates(snap.Sensors), _currentKey, delta);
        if (next is null) return;
        _currentKey = next.Key;
        SlotStore.Set(_cat.Id, next.Key);
        Refresh();
    }

    private void Refresh()
    {
        try
        {
            RefreshCore();
        }
        catch (Exception ex)
        {
            // Timer 线程的未处理异常会带崩扩展进程（F3）——宁可显示错误也不崩。
            Title = "内部错误";
            Subtitle = ex.GetType().Name;
        }
    }

    private void RefreshCore()
    {
        var snap = SnapshotCache.Current;
        if (snap?.Sensors is null)
        {
            Title = "--";
            Subtitle = "Host 未运行";
            return;
        }
        var current = SlotLogic.Resolve(_cat.GetCandidates(snap.Sensors), _currentKey);
        if (current is null)
        {
            Title = "--";
            Subtitle = _cat.EmptyHint;  // 如无 PawnIO 时 CPU 两类候选为空
            return;
        }
        Title = $"{current.Value:F0}{current.Unit}";
        var age = DateTimeOffset.Now - snap.Timestamp;
        Subtitle = age > TimeSpan.FromSeconds(10)
            ? $"⚠ 数据已 {age.TotalSeconds:F0}s 未更新"                       // F7 过期提示优先
            : (current.IsDefault ? _cat.DisplayName : current.Label);        // spec：显示规则
    }
}

/// <summary>类内轮换命令：上一个/下一个{CycleNoun}。</summary>
internal sealed partial class CycleSlotCommand : InvokableCommand
{
    private readonly SensorSlotBand _band;
    private readonly int _delta;

    public CycleSlotCommand(SensorSlotBand band, SlotCategory cat, int delta)
    {
        _band = band;
        _delta = delta;
        Name = (delta > 0 ? "下一个" : "上一个") + cat.CycleNoun;
        // Dock 项 Command.Id 为空会被静默忽略（坑 #3），上下文命令一并给足。
        Id = $"com.sensormonitor.{cat.Id}.{(delta > 0 ? "next" : "prev")}";
    }

    public override CommandResult Invoke()
    {
        _band.Cycle(_delta);
        return CommandResult.KeepOpen();
    }
}
```

- [ ] **Step 2: 构建验证**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Debug -p:Platform=x64 2>&1 | tail -3
```
Expected: `0 个错误`。

- [ ] **Step 3: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add src/SensorMonitorExtension/SensorMonitorExtension/Dock/SensorSlotBand.cs && git commit -m "feat(ext): sensor slot band with in-category cycling"
```

---

## Task 6: Provider 接线 + 删除旧合并 band

**Files:**
- Modify: `SensorMonitorExtensionCommandsProvider.cs`
- Delete: `Dock/SensorDockBand.cs`

- [ ] **Step 1: Provider 整文件替换为**

```csharp
// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using SensorMonitorExtension.Dock;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension;

public partial class SensorMonitorExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    // band 一次性创建，保持对象身份稳定（Dock 依赖属性变更通知重绘）。
    private readonly ICommandItem[] _dockBands;

    public SensorMonitorExtensionCommandsProvider()
    {
        DisplayName = "Sensor Monitor";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands = [
            new CommandItem(new SensorMonitorExtensionPage()) { Title = DisplayName },
        ];
        _dockBands = SlotCategories.All
            .Select(c => (ICommandItem)new WrappedDockItem(
                [new SensorSlotBand(c)], $"com.sensormonitor.{c.Id}", c.DisplayName))
            .ToArray();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    // Dock band（SDK ≥ 0.9.260303001 的 ICommandProvider3）：4 个独立预设控件（spec A1）。
    public override ICommandItem[]? GetDockBands()
    {
        SnapshotCache.EnsureStarted(); // 懒启动（F5）：未进 Dock 流程不轮询、不触发自动拉起
        return _dockBands;
    }
}
```

- [ ] **Step 2: 删除旧 band**

```bash
cd "D:/Workspace/SensorMonitor" && git rm src/SensorMonitorExtension/SensorMonitorExtension/Dock/SensorDockBand.cs
```

- [ ] **Step 3: 构建验证**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; dotnet build src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtension.csproj -c Debug -p:Platform=x64 2>&1 | tail -3
```
Expected: `0 个错误`（若报 SensorDockBand 引用残留，检查 Provider 是否还有 `_band` 字段未删）。

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add -A && git commit -m "feat(ext): wire 4 preset slot bands, remove merged dock band"
```

---

## Task 7: 部署 + 验收清单（spec 验收清单，需用户参与）

**Files:** 无代码改动（发现问题按 systematic-debugging 定位修复后重部署）

- [ ] **Step 1: 部署**

```bash
cd "D:/Workspace/SensorMonitor" && taskkill //f //im SensorMonitorExtension.exe 2>/dev/null; powershell -NoProfile -Command "Add-AppxPackage -Register 'D:\Workspace\SensorMonitor\src\SensorMonitorExtension\SensorMonitorExtension\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\AppxManifest.xml'; Start-Process 'x-cmdpal://reload'"
```

- [ ] **Step 2: 逐项过 spec 验收清单（用户目视 + agent 辅助）**

1. 编辑停靠栏可见 4 个新控件（CPU 频率/CPU 温度/GPU 温度/主板温度），可单独固定/排布；旧合并 band 消失。
2. 每控件右键 上一个/下一个 轮换正常且循环；轮换后 Title（值+单位）/Subtitle（类别名↔传感器名）正确切换。
3. "启动传感器 Host"在菜单最下；单击 band 拉起 Host 无 UAC。
4. 编辑停靠栏 → 标签：关标题/关字幕/全关（只剩图标）逐项生效；4 个图标字形肉眼可区分。
5. 重启 CmdPal（`taskkill //f //im Microsoft.CmdPal.UI.exe` 后重开面板）→ 各控件记住轮换选择（agent 可核对 LocalState `slots.json` 内容）。
6. 停 Host（`schtasks //End //TN SensorMonitor.Host`）→ 4 控件同显 `--/Host 未运行` → 30s 内静默恢复；恢复期间 host.log 无异常刷屏（单次拉起，无 4 倍请求）。
7. 主板温度控件能轮换 SuperIO Temperature #1–#6（PawnIO 已装）。

- [ ] **Step 3: 如有修复，逐个 commit（信息注明修的验收项）**

---

## Task 8: 文档收口 + 推送

**Files:**
- Modify: `CLAUDE.md`（状态行 + 一句话架构里的"Dock band 每 2s 轮询"表述）
- Modify: `docs/plans/2026-07-18-verification-and-next-phase.md`（Phase 1 / A1 标记完成）

- [ ] **Step 1: CLAUDE.md 更新**

状态段追加一行：

```markdown
- ✅ A1（2026-07-19）：Dock 拆为 4 个预设槽位控件（CPU 频率/CPU 温度/GPU 温度/主板温度），
  类内右键轮换、选择持久化（LocalState slots.json）、共享 SnapshotCache 轮询；旧合并 band 已移除。
```

"一句话架构"中 `Dock band 每 2s 轮询刷新` 改为 `Dock 槽位控件共享 SnapshotCache 每 2s 轮询刷新`。

- [ ] **Step 2: 路线计划标记**

`docs/plans/2026-07-18-verification-and-next-phase.md` 的 "Phase 1 — A1" 节标题下加一行：

```markdown
> ✅ 已完成（2026-07-19）：实现见 `docs/superpowers/plans/2026-07-19-a1-dock-slot-bands.md`，验收清单全过。
```

- [ ] **Step 3: Commit + push**

```bash
cd "D:/Workspace/SensorMonitor" && git add -A && git commit -m "docs: mark A1 dock slot bands complete" && git push
```

---

## Self-Review 结论

- **Spec 覆盖**：架构（Task 2/5/6）、类别定义表（Task 3）、显示规则（Task 5 RefreshCore）、右键菜单与单击（Task 5 + Task 1 闸门/fallback）、轮换持久化（Task 4 + Task 5 Cycle）、降级防崩（Task 2 catch + Task 5 三分支）、旧 band 移除（Task 6）、风险证伪（Task 1）、验收清单（Task 7 = spec 清单 7 项）、文档（Task 8）。无缺口。
- **占位符**：无 TBD/TODO；每个代码步骤含完整可粘贴代码。
- **类型一致性**：`SlotCategory(Id, DisplayName, CycleNoun, IconGlyph, EmptyHint, GetCandidates)` 定义（Task 3）与使用（Task 3 定义表、Task 5 band、Task 6 provider `c.Id`/`c.DisplayName`）一致；`SlotCandidate(Key, Label, Value, Unit, IsDefault)` 与 `SlotLogic.Resolve/Cycle`、`RefreshCore` 取值一致；`SnapshotCache.Current/Updated/EnsureStarted`（Task 2）与 Task 5/6 调用一致；`CycleSlotCommand(band, cat, delta)` 构造与 Task 5 使用一致。
