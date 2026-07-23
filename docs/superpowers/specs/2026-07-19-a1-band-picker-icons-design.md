# A1 增强 — 单击选择页 + band 图标 + 取消固定 bug Design

> 状态：已获用户批准（2026-07-19）。基于已发布 A1（`docs/superpowers/specs/2026-07-19-a1-dock-slot-bands-design.md`）的三项增强/修复。

## 目标

三项，均落在 band 生命周期 + Provider 接线一小片，内聚：
1. **单击 Band 打开"类别选择页"**：列出该 band 类别的候选（右键轮换的同一批），点选即换该 band 显示的传感器。
2. **编辑停靠栏 add-menu 的 band 图标**：现在我们的 band（如"主板温度"）在添加菜单里无图标，Performance Monitor 的都有——给 WrappedDockItem 设类别图标。
3. **Bug B1 修复**：band 取消固定后经"编辑停靠栏"重新添加，添加后 band 消失、且 add-menu 里也不再列出该 band。

## 需求定型（澄清结论，勿重新讨论）

- **选择页范围 = 仅该类别候选**（非全部传感器）：保持 A1"每类一控件"语义，图标/名称不失配；本质是用户体验升级（直选 vs 右键顺序轮换）。右键"上一个/下一个"轮换**保留**，picker 是直选的富交互补充。
- **图标**已编译探针确认 `WrappedDockItem.Icon` 可设（`wdi.Icon = new IconInfo(glyph)` 0 错误编译）。
- **B1 根因未程序化确认**：需实现时先走 systematic-debugging（用户在 dock 复现 → 取证 → 确认假设 → 落地）。领头假设见下。

## 前提事实（已核实）

- 官方 add-dock-band 示例**每次 GetDockBands 都 new WrappedDockItem**（`.github/skills/add-dock-band/SKILL.md` live-updating 段）；A1 当时改为缓存整个 `_dockBands`（含 WrappedDockItem）——这是 A1 的自主决定，其真实理由是避免 `SensorSlotBand` 每次重订阅静态 `SnapshotCache.Updated` 导致泄漏。
- `SensorSlotBand`（`Dock/SensorSlotBand.cs`）：主命令现为 `NoOpCommand`（A1 Task 1 冒烟结论：主命令在右键菜单置顶，故单击=无操作）；`MoreCommands = [上一个, 下一个, 启动 Host]`；构造时 `SnapshotCache.Updated += Refresh` 且不退订（进程生命周期）。
- `SlotCategory`（`Dock/SlotCategory.cs`）：`Id/DisplayName/CycleNoun/IconGlyph/EmptyHint/GetCandidates`。`SlotCandidate`：`Key/Label/Value/Unit/IsDefault`。
- `SlotLogic.Resolve/Cycle`、`SlotStore.Get/Set`、`SnapshotCache.Current/Updated` 均可复用。
- A3 浏览页（`Pages/SysPulseExtensionPage.cs`）是 `ListPage`，每传感器一个 `ListItem(NoOpCommand)`——picker 页可仿其结构。
- CmdPal 中命令若为 `IPage`（ListPage），点击即导航到该页；页内项命令返回 `CommandResult.GoBack()` 可退回。

## 设计

### 需求 1 — 单击打开类别选择页

**新增 `Pages/SensorPickerPage.cs`**（`ListPage`，每个 band 一个实例）：
- 构造：持 band 引用 + `SlotCategory`；`Title = "选择" + cat.DisplayName`、`Icon = new IconInfo(cat.IconGlyph)`、`Name = "选择"`。
- `GetItems()`：
  - `SnapshotCache.Current?.Sensors is null` → 单项 `ListItem(new Commands.LaunchHostCommand()) { Title="Host 未运行", Subtitle="回车启动传感器 Host" }`。
  - `cat.GetCandidates(sensors)` 为空 → 单项 `Title="--"、Subtitle=cat.EmptyHint`（NoOp）。
  - 否则每候选一个 `ListItem`，命令 = `SelectSensorCommand(band, key)`；`Title = $"{c.Label} {c.Value:F0}{c.Unit}"`；当前选中项（`c.Key == band.CurrentKey`）`Subtitle = "✓ 当前"`，其余 `Subtitle = ""`。

**新增 `SelectSensorCommand`**（`InvokableCommand`，可与 picker 同文件或 band 文件）：
- 构造持 band + key；`Name = "选为当前"`；`Id = $"com.syspulse.select.{key}"`（防坑 #3 空 Id）。
- `Invoke()`：`band.SetSelection(key)` → `return CommandResult.GoBack();`（选完退回 dock/上一页）。

**改 `SensorSlotBand`**：
- 主命令从 `NoOpCommand` 改为 picker 页。由于 `this` 在 base ctor 调用时未就绪，改为**构造体内设 `Command`**：先 `base(new NoOpCommand{...})` 占位，ctor 体内 `Command = new SensorPickerPage(this, cat);`（实现时编译探针确认 `Command` 可写；若不可写，改用可写路径并在计划记录）。
- 新增 `internal string? CurrentKey => _currentKey;`（picker 标"当前"用）。
- 新增 `internal void SetSelection(string key)`：绝对版轮换 —— `_currentKey = key; SlotStore.Set(_cat.Id, key); Refresh();`（与 `Cycle` 并列，`Cycle` 保留）。
- `MoreCommands` 保持 `[上一个, 下一个, 启动 Host]` 不变。

### 需求 2 — add-menu band 图标

**改 `SysPulseExtensionCommandsProvider`**：WrappedDockItem 构造后设 `Icon = new IconInfo(c.IconGlyph)`（与 band 主图标同字形）。随 B1 的 provider 改造一并落地。

### Bug B1 — 取消固定后重加消失

**领头假设**：CmdPal 把 `WrappedDockItem` 当一次性 dock 槽位——固定→取消固定后同一实例被视为已消费，A1 缓存整个 `_dockBands`（含 WrappedDockItem）每次返回同一实例 → 重加时 CmdPal 既不上 dock 也不列入 add-menu。官方示例每次 new WrappedDockItem。

**领头修复**（同时保留 A1 防泄漏理由）：
- 缓存 **4 个 `SensorSlotBand` 实例**（`_bands`，构造一次 → 4 个永久订阅，不泄漏）。
- `GetDockBands()` 每次用**新的 `WrappedDockItem`** 包装缓存的 band：
  ```
  SnapshotCache.EnsureStarted();
  return _bands.Select(b => (ICommandItem)new WrappedDockItem(
      [b], $"com.syspulse.{b.CategoryId}", b.DisplayName) { Icon = new IconInfo(b.IconGlyph) }).ToArray();
  ```
  （band 暴露 `CategoryId/DisplayName/IconGlyph` 或直接持 `SlotCategory` 供包装读取。）

**实现纪律**：B1 任务**先 systematic-debugging**——请用户在 dock 复现取消固定/重加，观察现象与假设是否一致；确认后再落地上述修复。若假设被证伪（如根因在别处），停下按证据重定方案，不硬套。

## 边界/降级/测试

- picker 页复用 SnapshotCache/SlotLogic/SlotStore；Host 未运行、无 PawnIO 各有提示项。
- 持久化与轮换共用 `_currentKey`：picker 的 `SetSelection` 与右键 `Cycle` 写同一 key、同一 `slots.json`。
- 无新自动化测试（同 A1，扩展侧无测试设施）；部署实测验收。

## 验收清单

1. 单击任一 band → 打开"选择{类别}"页，列出该类别候选，当前项标"✓ 当前"。
2. 页内点选另一候选 → 退回、该 band 立即显示所选传感器；重启 CmdPal 后保持（持久化）。
3. picker 页在 Host 未运行 / 无 PawnIO 时显示对应提示项，不崩。
4. 右键"上一个/下一个"轮换仍正常（未被 picker 取代）。
5. 编辑停靠栏 add-menu 中 4 个 band 均显示各自类别图标。
6. **B1**：任一 band 取消固定 → 经编辑停靠栏重新添加 → band 正常回到 dock、显示读数；add-menu 仍能正确列出未固定的 band。

## 明确不做（YAGNI）

- 全部传感器自由选（跨类别自定义 band）——已定为类别内选择。
- 温度阈值变色（R6 剩余）——本期不做。
- picker 页的搜索/过滤——候选量小（≤ 数十），直接列表即可。
