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

## Phase 1 — R7：Host 空闲自退出（推荐的第一个新功能）

**为什么先做它**：静默提权通道落地后，Host 成为登录常驻的提权进程；空闲自退出（N 分钟无管道请求即退出）
把"常驻提权"收敛为"按需提权"，且扩展已具备 30s 节流静默重拉能力，退出对用户完全无感。
范围小（只动 Host 侧计时逻辑 + 一个单测），是验证后热身的理想尺寸。

**立项条件**：Phase 0 全过。
**验收要点**：无请求 N 分钟后 Host 自退（host.log 留痕）；Dock 仍在轮询时永不退出；退出后 band 30s 内静默拉回。

## Phase 2 — R2：设置页（传感器选择 + 刷新间隔）

**为什么第二**：这是日常使用价值最大的一项，但交互设计依赖"Dock 用稳了"的真实使用反馈
（哪些指标想换、2s 间隔是否合适）。建议 Phase 0 后**日用观察几天**再立项，观察期记录想要的配置项。

**立项条件**：Phase 1 完成 + 至少数天日常使用。
**范围提示**：JSON 配置 + CmdPal 设置页（`.github/skills/add-extension-settings` 已有现成 skill 可用）；
设计时顺带定 R6（多 band / 阈值变色）的交互，两者共享"每指标一配置"的模型，可合并为一个计划。

## Phase 3 — R6：多 band + 温度阈值变色

与 R2 合并立项或紧随其后（配置模型互相耦合，分开做会返工）。

## 按需触发（不排期）

| # | 事项 | 触发条件 |
|---|------|---------|
| R4 | Host 打进 MSIX 随扩展分发 | 想在第二台设备安装、或对外分发时（消除 `SENSORMONITOR_HOST_EXE` 环境变量依赖） |
| R8 | 管道抢注防护 | 数据用途升级（如接入自动化决策）时；当前只读非敏感，维持接受风险 |
