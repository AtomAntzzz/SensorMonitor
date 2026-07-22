# 实机验证收口 + 路线 Plan

> 性质：**完成记录 + 方向路线**（无代码）。截至 2026-07-19，Phase 0 实机验证（V1–V8）与三项
> 产品诉求 A1/A2/A3（含增强）**均已交付**；本文档留作完成记录 + 后续按需路线。各项实现细节见
> 对应 `docs/superpowers/plans|specs/` 与 `CLAUDE.md` 状态段。

---

## 已交付（2026-07-19）

### Phase 0 — post-MVP 加固的实机验证 ✅ V1–V8 全通过

> 加固改动（WinExe、静默提权、懒启动、防崩过期提示）逐项桌面实测；细节见 hardening 计划各 task。

- **V1 部署**：CLI 部署通过（免 VS Deploy）。
- **V2 无窗口 Host**：双击→UAC→无窗口、进程可见、`host.log` 出现"Host 启动"、`--dump` 仍打印 JSON。
- **V3 静默提权通道**：`--install-task`→杀 Host→`schtasks /Run` 无 UAC 拉起（setup 脚本实跑取证；`/End` 亦免提权停）。
- **V4 防崩 + 过期提示**：数据过期 >10s → "⚠ 数据已 Ns 未更新"。
  ⚠ **测法修正（复用价值）**：原"挂起 Host 进程"不可行（挂起连管道一起冻，只会走"Host 未运行"分支）。
  正确测法：停真 Host 后起**假管道服务端**喂时间戳落后 60s 的快照（协议公开，F9 记录可抢注；需自愈式
  重试 + `schtasks /End` 压制静默重拉的复活竞态）。
- **V5 静默自动重连**：杀 Host → band 30s 内无 UAC 恢复（V4 测试期静默重拉两次复活真 Host，自愈强度额外背书）。
- **V6 浏览页**：搜 Sensor Monitor → 按硬件分组列全部传感器；杀 Host 重开 → 仅"Host 未运行"项。
- **V7 登录全链路**：注销重登 → 计划任务自启 Host → Dock 实时读数 → 全程无 UAC。
- **V8 收口**：CLAUDE.md 状态更新。

### A1 — Dock 控件拆分 + 预设 + 类内轮换 ✅（+ 增强）

诉求（参考 Performance Monitor）：每指标独立控件（解决截断）、CPU 频率/CPU 温度/GPU 温度/主板温度
4 预设、右键"标签→显示标题/字幕"（宿主内置，双关只剩图标）。

- **本体**：4 预设槽位控件、类内右键轮换（上一个/下一个带图标）、选择持久化（LocalState `slots.json`）、
  共享 SnapshotCache 每 1s 轮询、"启动 Host"菜单沉底、旧合并 band 移除。7 项验收全过。
  见 `docs/superpowers/plans/2026-07-19-a1-dock-slot-bands.md`。
- **增强（2026-07-19）**：单击 band 打开**类别选择页**（`Pages/SensorPickerPage.cs`，列该类候选、
  ✓ 标当前、点选即换、`RaiseItemsChanged` 刷新）；编辑停靠栏 add-menu 的 band 显示类别图标；
  Provider 改每次新建 WrappedDockItem（对齐官方）。见 `docs/superpowers/plans/2026-07-19-a1-band-picker-icons.md`。
- **学习（记 CLAUDE.md）**：验证 dock band 数量要**净启 CmdPal**——`x-cmdpal://reload` 会跨会话
  累加 band，制造"每个 band 重复 N 个"的假象（N == reload 次数），非发布 bug。
- **未做**：R6 温度阈值变色（后置，见下）。

### A2 — MSIX 打包链路验证 ✅

- 自签名 dev 身份（`CN=SensorMonitor Dev`）、x64 Release 已签名 .msix 实装 + Dock 正常、x64/ARM64 bundle 生成。
- **关键发现并修复**：Release 裁剪禁用反射式 System.Text.Json → 打包版曾全"Host 未运行"，
  改 source-gen JSON 上下文（`Ipc/SensorJsonContext.cs`）解决——这坑不提前打包就会潜伏到 R4/商店才爆。
- 复现步骤 `docs/references/msix-packaging.md`；实现 `docs/superpowers/plans/2026-07-19-a2-msix-packaging.md`。

### A3 — 搜索进入次级列表 ✅

- 本体既有浏览页已满足（搜 SensorMonitor → 回车列全部传感器）；band 单击选择页（见 A1 增强）
  进一步补齐"点进去换显示"的交互。

---

## 下一步（未排期，按需立项）

> A1/A2/A3 收口后建议**日用观察几天**再定优先级；R2 交互依赖真实使用反馈。

| # | 事项 | 触发条件 / 说明 |
|---|------|----------------|
| ✅ R2 | 设置页（刷新间隔 1/2/5s + 温度单位 °C/°F，全局项） | **已完成（2026-07-20）**：走 CmdPal 内置 Settings，`SettingsManager` 继承 `JsonSettingsManager` 自持久化（宿主不自动存，坑已记录）。范围经 brainstorming 收敛——传感器选择仍归 A1 的 `slots.json`，槽位显隐用原生 pin/unpin，均按 YAGNI 未纳入。见 `docs/superpowers/plans/2026-07-19-r2-settings-page.md` |
| ~~R6~~ | ~~温度阈值变色~~（**spike 证伪，2026-07-19 搁置**） | Dock band **不渲染 Tag/颜色**（红底"热"Tag 实测在 dock 完全不显；SDK 0.9.260303001）。若要做只能降级"超阈值换红图标字形/标题加⚠"，价值有限，暂不做 |
| ✅ R4 | 分发安装器（签名 Inno：自包含 Host + 计划任务 + 完整扩展 MSIX 全机预置） | **已完成（2026-07-20）**：一键装、装时一次 UAC、运行期零 UAC、一处卸载全清；消除 `SENSORMONITOR_HOST_EXE`。方案经 brainstorm 收敛——**非 MS Store**（驱动+提权 spike 证伪）、非 sparse（CmdPal 发现未证实），扩展仍完整 MSIX。干净机实测通过（发现+读数+卸载清任务；CPU/主板温度仍需用户另装 PawnIO）。见 `docs/superpowers/plans/2026-07-20-r4-installer-distribution.md` + `docs/references/installer.md`。**R4b**（WinGet/Release 提交 + 真证书 + 安装器健壮性硬化）后续 |
| ✅ R7 | Host 空闲自退出（5min 无管道请求自退，静默通道会拉回） | **已完成（2026-07-19）**：`PipeJsonServer.LastRequestUtc` + Program.cs 空闲 Timer；见 `docs/superpowers/plans/2026-07-19-r7-host-idle-exit.md` |
| R8 | 管道抢注防护（校验服务端签名/路径） | 数据用途升级（如接入自动化决策）时；当前只读非敏感，维持接受风险 |

## 小问题 / 待办细项（已记录，未排期）

- **空数据提示文案误导（"需 PawnIO 驱动"）** — 2026-07-22 干净机实测发现。`Dock/SlotCategories.cs` 里
  `cpuclock`/`cputemp`/`boardtemp` 三类的 `EmptyHint` 恒为"需 PawnIO 驱动"。但当 **Host 在跑、快照有其它传感器数据**
  （说明驱动正常——如干净机上 CPU 温度/频率能读）却唯独某类别空时，真实原因多是"**该机型无此传感器**"（如无 `/lpc`
  主板温度）而非缺驱动 → 提示误导。建议：空态分两种——快照整体为空 / Host 未运行 → 保留"需 PawnIO 驱动"；
  快照有数据但本类别空 → 显示"该机型无此传感器"（或类似）。落点：`SlotCategories.cs` 的 `EmptyHint` +
  `SensorSlotBand.RefreshCore` 的空态分支（那里能拿到 `SnapshotCache.Current` 判整体是否有数据）。
  **同一问题的另一面（2026-07-22 补记）**：**未装 PawnIO 时 CPU 频率/温度 sensor 存在但读 0** → 该类别**非空**（候选值=0）
  → band 显示"0 MHz"/"0°C" 而非空提示，**比空提示更误导**（像真实读数）。同处一并修：`SlotCategories.cs` 候选构建把
  温度/频率的 **0 / NaN 视作无数据**（不生成候选），落到空态分支走上面的文案逻辑。
- **设置页 dev-reload 空白（非真实 bug，不修）** — CmdPal 里"禁用扩展→再启用"后第一次打开设置页**整表单空白**，
  再开即恢复；**干净装 / net 重启 CmdPal 不复现**（2026-07-22 干净机确认）。经溯源（Toolkit 源码）我方设置内容生成
  完全正确，系 CmdPal 宿主**热重载缓存了旧的空设置页**所致，属宿主侧、我方无干净 hook 可控，且不影响真实分发。
  记此以免重复排查。
- **PawnIO 后装需重启 Host 才生效** — 2026-07-22 发现。Host 启动时 init 一次 `LibreHardwareMonitorLib`（枚举硬件/驱动），
  之后**再装 PawnIO 无效**，直到 Host 重启才带上驱动重新枚举。用户实测 workaround：手动杀掉后台 `SensorMonitor.Host.exe`
  → 计划任务静默重拉 → 重新 init 即读到数据。考虑（择一/组合）：① 文档提示"先装 PawnIO 再首启，或后装后重启 Host"；
  ② Host 侧检测"关键 sensor 长期读 0/缺失"时周期性重建 `Computer`；③ 安装器/PawnIO 安装后触发一次 `schtasks /End`。
  优先级低，多数用户装一次驱动即稳定。
