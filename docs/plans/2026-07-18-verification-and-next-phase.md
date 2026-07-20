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
| R4 | Host 打进 MSIX 随扩展分发（A2 已铺路） | 想在第二台设备安装/对外分发时；消除 `SENSORMONITOR_HOST_EXE` 依赖。前置：A2 的裁剪修复已就位 |
| ✅ R7 | Host 空闲自退出（5min 无管道请求自退，静默通道会拉回） | **已完成（2026-07-19）**：`PipeJsonServer.LastRequestUtc` + Program.cs 空闲 Timer；见 `docs/superpowers/plans/2026-07-19-r7-host-idle-exit.md` |
| R8 | 管道抢注防护（校验服务端签名/路径） | 数据用途升级（如接入自动化决策）时；当前只读非敏感，维持接受风险 |
