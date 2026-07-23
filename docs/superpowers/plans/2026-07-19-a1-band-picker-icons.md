# A1 增强（选择页 + band 图标 + B1 bug）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给已发布的 A1 Dock 槽位控件加三项：① 单击 band 打开类别选择页、点选即换传感器；② 编辑停靠栏 add-menu 里 band 显示类别图标；③ 修 B1（band 取消固定后重加消失）。

**Architecture:** 三项都落在 band 生命周期 + Provider 接线一小片。B1 领头修复=缓存 4 个 SensorSlotBand 实例（保留 A1 防订阅泄漏）但每次 GetDockBands 用新 WrappedDockItem 包装（对齐官方示例，给 CmdPal 干净槽位）；图标随包装一并设。选择页是新 ListPage，复用 SnapshotCache/SlotLogic/SlotStore，band 主命令从 NoOp 改为该页。

**Tech Stack:** 同现有扩展（net9.0-windows / CmdPal Toolkit）。无新依赖。

**Spec:** `docs/superpowers/specs/2026-07-19-a1-band-picker-icons-design.md`（含 6 项验收，勿重开设计）

**测试说明（无自动化测试，有意）：** 扩展侧无测试工程（spec 沿 A1 决定，部署实测验收）。纯逻辑仍在 SlotLogic/SlotCategories，本次不新增可测纯逻辑。Host 代码零改动，11 单测不受影响。

**已核实事实（编译探针确认，勿重查）：**
- `ListItem.Command` 可写、且接受 `ListPage`（探针：ctor 体内 `Command = new SysPulseExtensionPage();` 0 错误）→ band 主命令可在构造体内改为 picker 页。
- `CommandResult.GoBack()` 存在（探针 0 错误）→ 选择页项选完可退回。
- `WrappedDockItem.Icon` 可写（A1 增强探针：`wdi.Icon = new IconInfo(glyph)` 0 错误）。
- `IPage`（ListPage）作为命令，点击即导航到该页（CmdPal 语义，A3 浏览页即 ListPage）。
- 官方 add-dock-band 示例每次 `GetDockBands` new WrappedDockItem（`.github/skills/add-dock-band/SKILL.md`）——B1 领头假设的依据。

**部署三件套（A1 已验证）：**
```
cd "D:/Workspace/SysPulse" && taskkill //f //im SysPulseExtension.exe 2>/dev/null; dotnet build src/SysPulseExtension/SysPulseExtension/SysPulseExtension.csproj -c Debug -p:Platform=x64 2>&1 | tail -3
powershell -NoProfile -Command "Add-AppxPackage -Register 'D:\Workspace\SysPulse\src\SysPulseExtension\SysPulseExtension\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64\AppxManifest.xml'; Start-Process 'x-cmdpal://reload'"
```

---

## 文件结构

| 文件 | 动作 | 职责 |
|------|------|------|
| `.../Dock/SensorSlotBand.cs` | 修改 | 暴露 `Category`/`CurrentKey`、加 `SetSelection`、主命令改 picker 页 |
| `.../Pages/SensorPickerPage.cs` | 新增 | 类别选择页 + `SelectSensorCommand` |
| `.../SysPulseExtensionCommandsProvider.cs` | 修改 | 缓存 band、每次 new WrappedDockItem + 设 Icon（修 B1 + 图标） |

> 路径省略前缀 `src/SysPulseExtension/SysPulseExtension/`。命令在仓库根执行。

---

## Task 1: B1 根因确认 + Provider 修复（含 band 图标）

**Files:**
- Modify: `Dock/SensorSlotBand.cs`（暴露 `Category`）
- Modify: `SysPulseExtensionCommandsProvider.cs`

- [ ] **Step 1: systematic-debugging —— 用户复现 B1，确认假设**

先部署当前版本（三件套），请用户操作并回报：
1. 在 dock 编辑停靠栏，把某个 band（如"CPU 温度"）**取消固定**。
2. 再通过 add-menu 重新添加它。
3. 观察：band 是否消失？add-menu 里是否还能找到它？

Expected（领头假设成立）：重加后 band 不上 dock、add-menu 里也没了。
- **假设成立** → 进 Step 2（缓存整个含 WrappedDockItem 的 `_dockBands`、每次返回同一实例导致）。
- **假设证伪**（现象不同/别的表现）→ **停止**，按用户描述的实际现象回到 systematic-debugging 重定方案，不套用下面的修复。

- [ ] **Step 2: band 暴露 Category（供 provider 每次新建包装读取）**

`Dock/SensorSlotBand.cs` 中 `private readonly SlotCategory _cat;` 之后加一行公开只读访问：

```csharp
    private readonly SlotCategory _cat;
    internal SlotCategory Category => _cat;
```

- [ ] **Step 3: Provider 改为缓存 band + 每次新建 WrappedDockItem（修 B1）+ 设 Icon（图标）**

`SysPulseExtensionCommandsProvider.cs` 整文件替换为：

```csharp
// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using SysPulseExtension.Dock;
using SysPulseExtension.Ipc;

namespace SysPulseExtension;

public partial class SysPulseExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    // 缓存 band 实例（各订阅静态 SnapshotCache.Updated 一次，不泄漏）；
    // 但每次 GetDockBands 用新 WrappedDockItem 包装（B1：CmdPal 视 WrappedDockItem 为
    // 一次性槽位，取消固定后重加同一实例既不上 dock 也不列 add-menu；官方示例每次 new）。
    private readonly SensorSlotBand[] _bands;

    public SysPulseExtensionCommandsProvider()
    {
        DisplayName = "SysPulse";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands = [
            new CommandItem(new SysPulseExtensionPage()) { Title = DisplayName },
        ];
        _bands = SlotCategories.All.Select(c => new SensorSlotBand(c)).ToArray();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    // Dock band（ICommandProvider3）：每次新建包装缓存 band（修 B1），并给包装设
    // 类别图标（编辑停靠栏 add-menu 才有图标）。
    public override ICommandItem[]? GetDockBands()
    {
        SnapshotCache.EnsureStarted(); // 懒启动（F5）
        return _bands.Select(b => (ICommandItem)new WrappedDockItem(
                [b], $"com.syspulse.{b.Category.Id}", b.Category.DisplayName)
            { Icon = new IconInfo(b.Category.IconGlyph) })
            .ToArray();
    }
}
```

- [ ] **Step 4: 构建 + 部署**

三件套构建 Expected `0 个错误`；部署后 `x-cmdpal://reload`。

- [ ] **Step 5: 用户验证 B1 + 图标**

请用户确认：
1. 编辑停靠栏 add-menu 中 4 个 band 各显示类别图标（不再像"主板温度"那样空白）。
2. 取消固定某 band → 重新添加 → band 正常回到 dock、显示读数；add-menu 仍能列出未固定的 band。

全部通过 → 进 Step 6。仍失败 → 回 Step 1 的 systematic-debugging（假设不完整）。

- [ ] **Step 6: Commit**

```bash
cd "D:/Workspace/SysPulse" && git add src/SysPulseExtension/SysPulseExtension/Dock/SensorSlotBand.cs src/SysPulseExtension/SysPulseExtension/SysPulseExtensionCommandsProvider.cs && git commit -m "fix(ext): fresh WrappedDockItem per GetDockBands + band icons (B1)"
```

---

## Task 2: 单击 band 打开类别选择页

**Files:**
- Create: `Pages/SensorPickerPage.cs`
- Modify: `Dock/SensorSlotBand.cs`

- [ ] **Step 1: 写选择页 + 选择命令**

`Pages/SensorPickerPage.cs`（新文件，全文）：

```csharp
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SysPulseExtension.Dock;
using SysPulseExtension.Ipc;

namespace SysPulseExtension.Pages;

/// <summary>
/// 单击 band 打开的"类别选择页"：列出该 band 类别的候选（右键轮换同一批），
/// 点选即把 band 换成该传感器。仅类别内选择，保持"每类一控件"语义（spec：需求 1）。
/// </summary>
internal sealed partial class SensorPickerPage : ListPage
{
    private readonly SensorSlotBand _band;
    private readonly SlotCategory _cat;

    public SensorPickerPage(SensorSlotBand band, SlotCategory cat)
    {
        _band = band;
        _cat = cat;
        Title = "选择" + cat.DisplayName;
        Name = "选择";
        Icon = new IconInfo(cat.IconGlyph);
        Id = $"com.syspulse.{cat.Id}.picker";  // 非空 Id（坑 #3）
    }

    public override IListItem[] GetItems()
    {
        var snap = SnapshotCache.Current;
        if (snap?.Sensors is null)
        {
            return [new ListItem(new Commands.LaunchHostCommand())
                { Title = "Host 未运行", Subtitle = "回车启动传感器 Host" }];
        }
        var candidates = _cat.GetCandidates(snap.Sensors);
        if (candidates.Count == 0)
        {
            return [new ListItem(new NoOpCommand()) { Title = "--", Subtitle = _cat.EmptyHint }];
        }
        var items = new List<IListItem>();
        foreach (var c in candidates)
        {
            items.Add(new ListItem(new SelectSensorCommand(_band, c.Key))
            {
                Title = $"{c.Label} {c.Value:F0}{c.Unit}",
                Subtitle = c.Key == _band.CurrentKey ? "✓ 当前" : "",
            });
        }
        return [.. items];
    }
}

/// <summary>选择页里一项的命令：把 band 换成该 key 并退回。</summary>
internal sealed partial class SelectSensorCommand : InvokableCommand
{
    private readonly SensorSlotBand _band;
    private readonly string _key;

    public SelectSensorCommand(SensorSlotBand band, string key)
    {
        _band = band;
        _key = key;
        Name = "选为当前";
        Id = $"com.syspulse.select.{key}";  // 非空 Id（坑 #3）
    }

    public override CommandResult Invoke()
    {
        _band.SetSelection(_key);
        return CommandResult.GoBack();  // 选完退回 dock/上一页
    }
}
```

- [ ] **Step 2: band 加 CurrentKey/SetSelection + 主命令改 picker 页**

`Dock/SensorSlotBand.cs` 改三处：

(a) 在 `internal SlotCategory Category => _cat;`（Task 1 已加）之后加当前 key 只读访问：

```csharp
    internal SlotCategory Category => _cat;
    internal string? CurrentKey => _currentKey;
```

(b) 构造函数体内、`SnapshotCache.Updated += Refresh;` 之前，把主命令改为 picker 页（`base(...)` 仍用 NoOp 占位，因 `this` 在 base 调用时未就绪）：

```csharp
        // 单击 band 打开类别选择页（探针确认 Command 可写且接受 Page）。
        // base(...) 的 NoOp 仅占位，此处覆盖为真正的主命令。
        Command = new Pages.SensorPickerPage(this, cat);
        SnapshotCache.Updated += Refresh;
        Refresh();
```

(c) 在 `Cycle` 方法之后加绝对版选择（picker 用）：

```csharp
    /// <summary>绝对选择（picker 页用）：直接切到指定 key，写持久化并刷新。</summary>
    internal void SetSelection(string key)
    {
        _currentKey = key;
        SlotStore.Set(_cat.Id, key);
        Refresh();
    }
```

- [ ] **Step 3: 构建 + 部署**

三件套构建 Expected `0 个错误`（若报 `SysPulseExtension.Pages` 命名空间未引用，band 里用全限定 `Pages.SensorPickerPage` 已规避）；部署 + reload。

- [ ] **Step 4: 用户验证选择页**

请用户确认：
1. 单击任一 band → 打开"选择{类别}"页，列出该类别候选，当前项标"✓ 当前"。
2. 点选另一候选 → 退回、该 band 立即显示所选传感器。
3. 重启 CmdPal（`taskkill //f //im Microsoft.CmdPal.UI.exe` 后重开面板）→ 选择保持（持久化；agent 可核对 LocalState `slots.json`）。
4. 右键"上一个/下一个"轮换仍正常（未被 picker 取代）。
5. Host 未运行时打开 picker → 显示"Host 未运行"项，不崩。

- [ ] **Step 5: Commit**

```bash
cd "D:/Workspace/SysPulse" && git add src/SysPulseExtension/SysPulseExtension/Pages/SensorPickerPage.cs src/SysPulseExtension/SysPulseExtension/Dock/SensorSlotBand.cs && git commit -m "feat(ext): click band opens category picker page, select sets sensor"
```

---

## Task 3: 文档收口 + 推送

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/plans/2026-07-18-verification-and-next-phase.md`

- [ ] **Step 1: CLAUDE.md 状态补一行**

`CLAUDE.md` 当前状态段的 A2 行之后加：

```markdown
- ✅ A1 增强（2026-07-19）：单击 band 打开类别选择页（点选换传感器）、编辑停靠栏 add-menu
  band 显示类别图标、修复取消固定后重加消失（B1：每次 GetDockBands 新建 WrappedDockItem 包装缓存 band）。
```

- [ ] **Step 2: 路线计划标记**

`docs/plans/2026-07-18-verification-and-next-phase.md` 的 "A3 — 搜索进入次级列表" 段标题下加：

```markdown
> ✅ A3 本体既有浏览页已满足；2026-07-19 追加 band 单击选择页 + 图标 + B1 修复，
> 见 `docs/superpowers/plans/2026-07-19-a1-band-picker-icons.md`。
```

- [ ] **Step 3: Commit + push**

```bash
cd "D:/Workspace/SysPulse" && git add CLAUDE.md docs/plans/2026-07-18-verification-and-next-phase.md && git commit -m "docs: A1 enhancements (picker/icons/B1) complete" && git push
```

---

## Self-Review 结论

- **Spec 覆盖**：需求 1（选择页）→ Task 2；需求 2（图标）→ Task 1 Step 3 的 `{ Icon = ... }`；B1 → Task 1（Step 1 debug 闸 + Step 3 修复）。验收清单 6 项 → Task 1 Step 5（图标、B1）+ Task 2 Step 4（选择页、轮换保留、降级）。无缺口。
- **占位符**：无 TBD/TODO。Task 1 Step 1 的"假设证伪则回 debug"是 systematic-debugging 应有的分支闸，非含糊要求；每个改代码 Step 均含完整可粘贴代码。
- **一致性**：`Category`（Task 1 加）被 Task 1 provider 与 Task 2 picker 一致使用；`CurrentKey`（Task 2 加）被 picker 的"✓ 当前"判定用；`SetSelection(key)`（Task 2 加）被 `SelectSensorCommand.Invoke` 调；band 主命令 `Pages.SensorPickerPage` 全限定引用与新页命名空间 `SysPulseExtension.Pages` 一致；band id `com.syspulse.{Category.Id}` 跨 provider 与旧 A1 一致。
