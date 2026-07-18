# 实机验证收口 + 下一阶段路线 Plan

> 性质：**验证清单 + 方向路线**（无代码）。post-mvp-hardening（Task 1–8）代码已全部提交、11 单测全绿；
> 本计划先收口遗留的桌面手动验证，再给出后续功能的推荐顺序与立项条件。

**2026-07-18 完成度核查结论：**

| 项 | 状态 |
|----|------|
| Task 1–8 提交 | ✅ 8 个 commit 与计划一一对应，工作区干净 |
| 单测 | ✅ 11/11 全绿（含挂死客户端超时用例） |
| 关键实现抽查 | ✅ `PipeJsonServer` 单连接超时、`HostLog`、`TaskInstaller`、`TryLaunchSilent`、浏览页均落地 |
| 文档收口 | ✅ architecture.md 已含 D7/D8；`docs/staged-extension/` 已删除 |
| **实机验证** | ⏳ **未做**：计划任务未注册、`%ProgramData%\SensorMonitor\host.log` 不存在、Host 未运行 |

---

## Phase 0 — 实机验证收口（阻塞项，需桌面会话 + 管理员权限）

> 依据：hardening 计划各 task 的验证步骤。全部通过前**不开新功能**——加固改动（WinExe、静默提权、懒启动）任何一环失效都会改变后续路线的前提。

- [ ] **V1 部署新扩展**：VS Deploy（非 Build）→ CmdPal 内 Reload。
- [ ] **V2 无窗口 Host（Task 3）**：双击 exe → UAC → 无窗口；任务管理器可见进程；
      `host.log` 出现"Host 启动"；管理员终端 `--dump` 仍在终端打印 JSON。
- [ ] **V3 静默提权通道（Task 5）**：管理员终端 `--install-task`（exit 0）→ 杀 Host →
      普通终端 `schtasks /Run /TN SensorMonitor.Host` → **无 UAC**、进程出现。
- [ ] **V4 扩展防崩与过期提示（Task 4）**：band 正常读数；挂起 Host >10s → "未更新"提示；杀 Host → "Host 未运行"。
- [ ] **V5 静默自动重连（Task 6）**：杀 Host → band 30s 内自动恢复，全程无 UAC；点击 band 立即恢复。
- [ ] **V6 浏览页（Task 7）**：面板打开 Sensor Monitor → 按硬件分组列出全部传感器；杀 Host 重开 → 仅"Host 未运行"项。
- [ ] **V7 登录全链路**：注销重登 → 计划任务自启 Host → Dock 实时读数 → 全程无 UAC。
- [ ] **V8 收口**：CLAUDE.md 状态行去掉"⏳ 待桌面手动验证"；如有验证失败项，按 systematic-debugging 定位后修复再回归。

## 产品诉求清单（2026-07-19 记录，来自实际使用反馈；立项时按此为准）

> A1/A3 均指定了**参考设计**：实现前先实机把玩对应扩展（Performance Monitor / Weather），
> 对齐其交互再动手；诉求描述与参考设计冲突时以诉求为准。

### A1 — Dock 控件拆分 + 预设 + 标签显隐（参考 Performance Monitor 设计，细化 R6）

1. **每项数据独立控件**：不再全部挤在一个 band 里（现状会截断，后面的信息看不见）；
   一个指标一个 Dock 控件。
2. **预设控件**：提供若干预设，内容含 CPU 频率 / CPU 温度 / GPU 温度及其他可用指标。
3. **单控件右键菜单 → 标签 → 显示标题 / 显示字幕**（与 Performance Monitor 一致）：
   - **标题** = 实际数据 + 单位（如 `4700MHz`）；
   - **字幕** = 该项说明（如 `GPU温度`）；
   - 控件整体 = **图标 + 标题 + 字幕**；标题与字幕都取消显示时**只剩图标**。

### A2 — 测试 MSIX 打包（R4 的第一步）

当前部署是松散布局注册（`Add-AppxPackage -Register`）。验证**正式 MSIX 打包**产物
（`dotnet build`/`msbuild` 出 .msix → 签名 → 安装）在本机可安装、可加载、Dock 正常，
为 R4（Host 随包分发）铺路。打包流程参考 `src/SensorMonitorExtension/.github/skills/publish-extension/`。

### A3 — 搜索进入次级列表（参考 Weather 设计）

CmdPal 搜索 `sensormonitor` → 回车进入**次级列表页**，展示所有可用信息（全部传感器读数）。
现有浏览页（hardening Task 7）已具雏形，需对齐 Weather 的进入方式与列表体验核对差距
（顶层命令命名/图标、回车直达、列表分组与刷新）。

## Phase 1 — A1：Dock 控件拆分 + 预设 + 标签显隐（吸收 R6 多 band）

**为什么第一**：截断问题是**当下真实使用痛点**（2026-07-19 用户反馈），且 A1 的"每指标一控件"
正是原 R6 多 band 的形态，直接按 A1 规格实施。R6 的温度阈值变色可视工作量顺带或后置。

**立项条件**：Phase 0 全过。
**动手前**：实机把玩 Performance Monitor 扩展对齐交互；确认 CmdPal Dock API 对多 band、
右键菜单（标签显隐）、图标态的支持面（`.github/skills/add-dock-band` 可参考）。
**验收要点**：按 A1 三条逐项对照；预设控件开箱即用；标题/字幕独立开关，双关只剩图标。

## Phase 2 — A3 次级列表对齐 + A2 MSIX 打包测试（两个独立小项）

- **A3**：对齐 Weather 的搜索→回车→次级列表体验，在现有浏览页基础上核对差距改进。
- **A2**：走通正式 MSIX 打包（构建→签名→安装→Dock 正常），为 R4 分发铺路；
  产出打包步骤文档（成功命令序列记入 CLAUDE.md 或 publish-extension skill 笔记）。

两项互不依赖，可穿插在 Phase 1 前后的间隙做；A2 若发现松散部署与打包行为差异，优先记录再决定是否阻塞。

## Phase 3 — R2：设置页（传感器选择 + 刷新间隔）

**说明**：A1 落地后会自然产生"每控件配置"模型（选哪些指标、标签显隐持久化），R2 的设置页
应基于该模型扩展（加刷新间隔等全局项），避免另起炉灶。`.github/skills/add-extension-settings`
已有现成 skill 可用。

**立项条件**：Phase 1 完成 + 数天日常使用反馈。

## 后续/按需（不排期）

| # | 事项 | 触发条件 |
|---|------|---------|
| R7 | Host 空闲自退出（N 分钟无管道请求自退，静默通道会拉回） | 用户诉求消化完后的收尾优化；把"常驻提权"收敛为"按需提权" |
| R4 | Host 打进 MSIX 随扩展分发（A2 是其第一步） | 想在第二台设备安装、或对外分发时（消除 `SENSORMONITOR_HOST_EXE` 环境变量依赖） |
| R8 | 管道抢注防护 | 数据用途升级（如接入自动化决策）时；当前只读非敏感，维持接受风险 |
