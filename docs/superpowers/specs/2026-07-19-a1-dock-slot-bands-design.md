# A1 — Dock 槽位控件（拆分 + 预设 + 类内轮换）Design

> 状态：已获用户批准（2026-07-19，方案甲）。对应诉求：`docs/plans/2026-07-18-verification-and-next-phase.md` A1 节 + 本次澄清新增项。

## 目标

把现有单条合并 Dock band（`CPU xxxMHz · CPU xx°C · GPU xx°C`，会截断）替换为 **4 个独立预设控件**，
交互对齐 PowerToys Performance Monitor：控件 = 类别，类内右键"上一个/下一个"轮换实例，
"启动传感器 Host"沉底；标题/字幕显隐由 Dock 宿主编辑模式内置提供（零代码）。

## 需求定型（澄清结论，勿重新讨论）

1. **标签显隐是 Dock 宿主能力**：右键空白 band → "编辑停靠栏" → 右键任意 band → "标签"（显示标题/显示字幕）+ "取消固定"。拆分后自动获得，A1.3 无需代码。
2. **非编辑模式右键菜单**（新增诉求）：提供"上一个 X / 下一个 X"（类内轮换），"启动传感器 Host"排最下（对齐 Performance Monitor 的"打开任务管理器"沉底模式）。
3. **预设 4 个全要**：CPU 频率 / CPU 温度 / GPU 温度 / 主板温度。
4. **旧合并 band 移除**（不保留为第 5 预设）；用户升级后需在编辑停靠栏重新固定新控件（一次性成本，已知悉）。
5. 阈值变色（R6 剩余）**本期不做**。
6. 图标：初版按类别用 Segoe Fluent 字形区分；定制图标后续再做（类别划分已为其铺路）。

## 架构（方案甲：通用槽位类 + 声明式类别定义）

```
SensorMonitorExtensionCommandsProvider
  └─ GetDockBands() → 4 × WrappedDockItem（各包一个 SensorSlotBand）
       band ID：com.sensormonitor.cpuclock / cputemp / gputemp / boardtemp

SensorSlotBand : ListItem       ← 一个可复用类，4 个实例
  由 SlotCategory 定义参数化：{ Id, 类别名, 图标, Filter(快照→候选列表), 默认项选择 }
  Command      = LaunchHostCommand（主命令，菜单沉底 + 单击行为）
  MoreCommands = [上一个 X, 下一个 X]

SnapshotCache（单例）
  一个 2s Timer + 一次管道请求/周期，4 个控件读缓存
  （Host 管道串行处理，4 控件各自轮询会互相排队 → 必须共享）
  懒启动：首次 GetDockBands 才起 Timer（F5 语义保留）
  Host 未运行自动静默拉起 + 30s 节流（从旧 band 迁来，D7 语义不变）

SlotLogic（静态类，无 UI 依赖）
  候选筛选、轮换 index 计算、默认项回退 —— 纯函数，为将来单测留口
```

## 类别定义（筛选规则沿用已实机验证的 FormatLine 匹配）

| 控件 | 候选列表（轮换域，按快照动态生成） | 默认项 |
|------|--------------------------------|--------|
| CPU 频率 | 合成项"全核最大"（候选中 Clock 最大值）+ 各核心 Clock（Id 前缀 `/intelcpu`\|`/amdcpu`，Type==Clock） | 全核最大 |
| CPU 温度 | CPU Package + 各核心温度（同前缀，Type==Temperature） | CPU Package |
| GPU 温度 | `/gpu*` 前缀全部 Temperature（GPU Core、Hot Spot…；多卡自然并入） | Name=="GPU Core" 首项，缺则列表首项 |
| 主板温度 | Hardware 为 SuperIO（本机 ITE IT8655E，Id 前缀 `/lpc`）的 Temperature #1–#6 | 列表首项 |

候选按传感器 Id 排序保证轮换顺序稳定。

## 显示规则

- **Title** = `数据+单位`，无硬件前缀：`5697MHz`、`62°C`（说明职责归字幕与图标）。
- **Subtitle** = 说明：当前为默认项时显示**类别名**（如"CPU 温度"）；轮换到具体项时显示**传感器名**（如"Core #3"）。
- 显隐控制完全交 Dock 编辑模式（宿主内置）；两者都关只剩图标 —— 免费满足 A1.3。
- 图标（初版 Segoe Fluent，实现时微调）：频率类 speed/仪表字形，CPU 温度、GPU、主板温度各选可区分字形。

## 右键菜单与单击

- 菜单顺序目标：`上一个 X` / `下一个 X` / …… / `启动传感器 Host`（最下）。
- 实现假设：主命令（`Command`）在 Dock 上下文菜单中**沉底**、`MoreCommands` 在其上 ——
  与 Performance Monitor 观察一致（其"打开任务管理器"为主命令且沉底）。**实现时第一步实测**；
  若实际顺序不符，改为 `Command`=NoOp、三项全放 `MoreCommands` 按序排列（单击行为随之调整并在验证清单确认）。
- 单击 band = 启动 Host（静默通道优先；Host 已运行时 `schtasks /Run` 无害）。

## 轮换与持久化

- "下一个"到尾循环回头，"上一个"反向。
- 每控件当前选择按**传感器 Id** 持久化：`ApplicationData.Current.LocalFolder` 下 `slots.json`
  （形如 `{ "cpuclock": "__max__", "cputemp": "/amdcpu/0/temperature/2", ... }`；合成项用保留 Id `__max__`）。
- 启动加载；存的 Id 在当前快照候选中不存在（换硬件/无 PawnIO）→ 回退默认项，不改写文件（硬件回来时自动恢复）。
- 写入时机：轮换命令触发时同步写（频率低，无需防抖）。

## 降级与防崩（沿用既有语义）

| 场景 | Title | Subtitle |
|------|-------|----------|
| Host 未运行（快照 null） | `--` | `Host 未运行` |
| 数据过期 >10s（F7） | 正常值 | `⚠ 数据已 Ns 未更新` |
| 类别候选为空（无 PawnIO 时 CPU 两类） | `--` | `需 PawnIO 驱动` |

- 每控件 Refresh 外层 try/catch 兜底（F3 防崩结构原样保留，异常时 Title=`内部错误`）。
- 自动静默拉起在 SnapshotCache 层做（全局 30s 节流，而非每控件各自节流）。

## 变更范围

- 改：`SensorMonitorExtensionCommandsProvider.cs`（返回 4 band）
- 新增：`Dock/SensorSlotBand.cs`、`Dock/SlotCategory.cs`、`Dock/SlotLogic.cs`、`Ipc/SnapshotCache.cs`、轮换命令类
- 删：`Dock/SensorDockBand.cs`（旧合并 band）
- 不动：Host 全部、浏览页、`PipeSensorClient`、部署脚本

## 风险与未知项（实现时优先证伪）

1. **菜单顺序假设**（主命令沉底）——实现第一步实测，有 fallback（见上）。
2. `GetDockBands` 返回多条 band 未在本项目实测过（SDK 文档支持）——冒烟优先。
3. Dock 对 band 内 `MoreCommands` 的渲染支持——若不支持上下文项，轮换改为"单击=下一个"降级方案，需回报用户再定。
4. 图标字形在 Dock 缩放下的辨识度——验证清单人工确认。

## 验收清单（部署后人工过）

1. 编辑停靠栏可见 4 个新控件，可单独固定/排布；旧合并 band 消失。
2. 每控件：右键 上一个/下一个 轮换正常、循环；轮换后 Title/Subtitle 正确切换。
3. "启动传感器 Host"在菜单最下；单击 band 拉起 Host 无 UAC。
4. 编辑停靠栏 → 标签 → 关标题/关字幕/全关（只剩图标）逐项生效。
5. 重启 CmdPal（或重登）后各控件记住轮换选择。
6. 杀 Host → 4 控件同时降级"--/Host 未运行"→ 30s 内静默恢复（且只拉起一次，无 4 倍请求）。
7. 主板温度控件能轮换 SuperIO Temperature #1–#6（PawnIO 已装）。
