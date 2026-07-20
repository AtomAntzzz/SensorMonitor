# R2 — 设置页（刷新间隔 + 温度单位）Design

> 状态：已获设计批准（brainstorming，2026-07-19，方案 A）；待用户复核本 spec。
> 对应路线 `docs/plans/2026-07-18-verification-and-next-phase.md` 的 R2。

## 目标

给扩展加一个**全局设置页**，走 CmdPal **内置 `Settings`** 机制（宿主自动渲染设置页；持久化经 Toolkit `JsonSettingsManager` 自管，见前提事实修正），
承载两个全局项：**刷新间隔** 与 **温度单位 °C/°F**。逐传感器选择继续由 A1 的单击选择页 +
`slots.json` 承担，本期不动；"槽位显隐"经确认与停靠栏原生 pin/unpin 冗余，**去掉**。

## 需求定型（澄清结论）

- **范围收敛到 2 项**（brainstorming）：刷新间隔、温度单位 °C/°F。
- **槽位显隐去掉** —— CmdPal 停靠栏 add-menu 原生就能 pin/unpin 每个 band，设置页再加一层是冗余（YAGNI）。
- **数据过期阈值不做** —— 保持 `SensorSlotBand` 里写死的 10s。
- **方案 A（选定）**：用 CmdPal 内置 `Settings`（`add-extension-settings` skill 的标准路径，宿主自动持久化/渲染），
  **不**扩展 `slots.json`。这两个全局项都不吃"每类别一槽位"的形状，塞进 `slots.json` 是硬套；
  "勿另起炉灶"= 复用标准配置路径，CmdPal `Settings` **就是**那条路径。
- **刷新间隔**：`ChoiceSetSetting` 下拉预设 **1s / 2s / 5s**，默认 **1s**（= 现行为）。
  不用自由文本，免去校验 "0"/乱输入。**上限 5s** 的理由：过期提示阈值写死 10s（本期不配），
  间隔远小于 10s 才不会让"⚠ 数据已 Ns 未更新"每轮误触发。
- **温度单位**：`ChoiceSetSetting`，°C（默认）/ °F，应用到**全部三处**显示位保持一致。
- **转换器内联**（无扩展测试工程）+ 手动验证 —— 与扩展侧 `SlotLogic` 目前无单测的现状一致。

## 前提事实（已核实）

- `SensorMonitorExtensionCommandsProvider` **未设 `Settings` 属性** → 设置页是绿地。
- `SnapshotCache.RefreshMs = 1000`（`const`），刷新在 `finally` 里 `_timer.Change(RefreshMs, …)` 重排。
- 过期阈值 10s 写死在 `SensorSlotBand.RefreshCore`（`age > TimeSpan.FromSeconds(10)`）。
- 温度读数：Host `SensorMapper.UnitOf` **仅温度**映射 `Unit == "°C"`。注意 band/选择页拿到的是
  `SlotCandidate`（只带 `Value`/`Unit`，**无 `Type`**），浏览页才是带 `Type` 的 `SensorReading`；
  故转换统一按 `Unit == "°C"` 判定，三处一致、无需 `Type`。
- **三处温度显示位**：`SensorSlotBand.RefreshCore`（dock band）、`SensorPickerPage.GetItems`（选择页）、
  `SensorMonitorExtensionPage.GetItems`（浏览页，`{r.Value:F1} {r.Unit}`）。
- `add-extension-settings` skill：`ToggleSetting`/`TextSetting`/`ChoiceSetSetting`；`SettingsChanged` 事件；
  在 `CommandProvider` 上设 `Settings = manager.Settings` 即自动出设置页。
  ⚠️ **持久化更正（2026-07-20 实测）**：宿主**不**自动存扩展设置——它只渲染设置页、并在用户改动时触发 `SettingsChanged`。
  持久化须扩展自管：`SettingsManager` **继承 Toolkit `JsonSettingsManager`**，设 `FilePath`
  （`Utilities.BaseSettingsPath("SensorMonitorExtension")/settings.json`），构造末尾 `LoadSettings()` 读盘、
  `SettingsChanged` 里 `SaveSettings()` 写盘。缺此则每次启动回落种子默认（本期首版即踩此坑）。
- **无扩展侧测试工程**（仅 `tests/SensorMonitor.Host.Tests`）。
- R7 Host 空闲自退 = 5min 无请求；本期间隔上限 5s ≪ 5min，band 固定时 Host 永不空闲，R7 不受影响。

## 设计（方案 A）

### ① SettingsManager + Provider 接线

新增 `src/SensorMonitorExtension/SensorMonitorExtension/Settings/SettingsManager.cs`（对齐 skill）：

- 持有 `Microsoft.CommandPalette.Extensions.Toolkit.Settings _settings`，装入下面两个 `ChoiceSetSetting`。
- 暴露 `ICommandSettings Settings => _settings;` 与类型化 getter（`RefreshIntervalMs`、`Fahrenheit`）。
- 订阅 `_settings.SettingsChanged`；构造时先推一次初始值（宿主可能在首帧前已恢复持久化值）。
- **单一 `OnSettingsChanged` 出口**：读两项 → `SnapshotCache.SetIntervalMs(RefreshIntervalMs)`；
  `TempDisplay.Fahrenheit = Fahrenheit` → `SnapshotCache.NotifyDisplayChanged()`（令 band 立即重绘）。

`SensorMonitorExtensionCommandsProvider`：`private readonly SettingsManager _settings = new();`，
构造函数里 `Settings = _settings.Settings;` → 设置页自动出现。显示位**不**逐个传 manager，
统一读静态 `TempDisplay.Fahrenheit`（与 `SnapshotCache`/`SlotStore` 的静态单例风格一致），
把显示位与 SettingsManager 解耦。

### ② 刷新间隔

- `ChoiceSetSetting`，id `"refreshInterval"`，选项 1s/2s/5s（value `"1000"/"2000"/"5000"` ms），默认 `"1000"`。
- `SettingsManager.RefreshIntervalMs`：`int.TryParse` 失败回退 1000。
- `SnapshotCache`：`const int RefreshMs = 1000;` → `private static int _refreshMs = 1000;`
  + `public static void SetIntervalMs(int ms)`（夹取到合理下限，如 `Math.Max(200, ms)`）。
  `finally` 的重排改用 `_refreshMs`。`_refreshMs` 为 `int`，Timer 回调读、SettingsChanged 写，
  int 读写原子，无需锁。改值在**下一轮重排**生效（≤ 当前间隔内），无需重启。

### ③ 温度单位

- `ChoiceSetSetting`，id `"tempUnit"`，选项 °C（value `"C"`）/ °F（value `"F"`），默认 `"C"`。
- 新增纯静态助手 `Settings/TempDisplay.cs`：

  ```csharp
  internal static class TempDisplay
  {
      public static bool Fahrenheit;   // 仅 SettingsManager 写
      // 按 Unit 判温度（band/选择页的 SlotCandidate 无 Type）：非 °C 原样透传；
      // °C 按当前单位换算（°F = °C·9/5+32）。
      public static (double Value, string Unit) Format(double value, string unit)
          => unit == "°C"
              ? (Fahrenheit ? value * 9 / 5 + 32 : value, Fahrenheit ? "°F" : "°C")
              : (value, unit);
  }
  ```

- 三处显示位改用 `TempDisplay.Format(...)` 取 `(value, unit)` 再套各自格式串
  （band/选择页 `F0`、浏览页 `F1` 带空格，格式不变，只换值与单位）。
- 单位切换时 `SnapshotCache.NotifyDisplayChanged()` → 复用 `Updated` 事件令 band 立即重绘；
  选择页/浏览页为短生命周期列表，下次打开 `GetItems` 自然重算（可接受，dock band 才是常驻面）。

### ④ SnapshotCache.NotifyDisplayChanged

`SnapshotCache` 新增 `public static void NotifyDisplayChanged() { try { Updated?.Invoke(); } catch { } }`
（`Updated` 为私有 event，外部无法直接 invoke，故加此公开触发口）。band 的 `Refresh()`
以最新 `Current` 快照 + 新单位重绘。

## 边界 / 错误处理

- `RefreshIntervalMs` 解析失败回退 1000；`SetIntervalMs` 夹下限防 0/负值把 Timer 打成忙循环。
- `_refreshMs` int 原子读写，无锁。
- `TempDisplay.Format` 纯函数、不抛。
- Host 未运行时 band 显示 `--`/`Host 未运行`，与单位无关；切单位不产生异常。
- 首帧前持久化恢复：SettingsManager 构造时推一次初始值，保证首轮轮询即用持久化间隔、首次绘制即用持久化单位。
- 间隔 ≤5s ≪ R7 的 5min 空闲阈值 → Host 空闲自退逻辑不受影响。

## 测试

- **无扩展单测工程** → `TempDisplay.Format` 内联、手动验证（同 `SlotLogic` 扩展侧现状；加测试工程列入"明确不做"）。
- **手动验证清单**（净启 CmdPal，避坑 #9 的 reload 累加假象）：
  1. CmdPal 设置里出现 Sensor Monitor 的两项（刷新间隔、温度单位）。
  2. 刷新间隔改 5s → dock 读数约每 5s 跳一次；改回 1s 立即恢复每秒。
  3. 温度单位改 °F → 三个温度 band + 选择页 + 浏览页的温度全部转 °F（如 50°C→122°F），
     CPU 频率不受影响；改回 °C 恢复。
  4. 重启 CmdPal / 重登 → 两项设置持久化保留（宿主存储）。

## 验收清单

1. 设置页出现且两项可改（provider 设了 `Settings`）。
2. 刷新间隔 1s/2s/5s 三档实机切换即时生效、无需重启，观察 dock 读数跳变节奏对应。
3. 温度单位 °C/°F 切换后三处显示位一致转换，非温度读数不受影响。
4. 设置跨会话持久化（宿主自动存储）。
5. 现有 12 单测仍全绿（本期无 Host 侧改动，纯扩展侧）。

## 明确不做（YAGNI）

- 槽位显隐（原生 pin/unpin 已覆盖）。
- 数据过期阈值可配（保持 10s 写死）。
- Host 空闲时长可配（R7 spec 曾提"R2 时再做"，本期范围外，仍保持 5min `const`）。
- 扩展侧单测工程（`TempDisplay` 手动验证，同 `SlotLogic` 现状）。
- 自定义设置 UI / 扩展 `slots.json`（一律走 CmdPal 内置 `Settings`）。
- ~~温度单位/间隔的自建持久化（宿主自动持久化）~~ —— **作废**：宿主不自动存，持久化经继承
  `JsonSettingsManager` 的 `FilePath`+`LoadSettings`/`SaveSettings` 实现（见前提事实修正），属**必做**而非不做。
