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

- ✅ **空数据提示文案误导（"需 PawnIO 驱动"）** — 2026-07-22 干净机实测发现，**同日修复**。
  `cpuclock`/`cputemp`/`boardtemp` 三类的空态字幕曾恒为"需 PawnIO 驱动"；当 Host 在跑、驱动正常但某类别恰无传感器
  （如无 `/lpc` 主板温度）时会误导为缺驱动。修复：空态分两种——`SlotCategory` 加 `MissingHint` 字段；
  `SensorSlotBand.RefreshCore` 空态分支按 `SlotCategories.HasDriverData(snap.Sensors)` 择字。
  **判据未用原提案的"快照整体是否为空"，改用"是否存在**有效**的 PawnIO/ring0 驱动读数"**（有效 CPU 温度或任一
  `/lpc` 读数；有效=finite 且非 0）——因"无 PawnIO 但有 GPU"是常见机型，快照非空但 CPU 类确因缺驱动而空，
  整体空判据会误报"该机型无此传感器"、反劝退装驱动。有驱动数据→`MissingHint`"该机型无此传感器"；否则→`EmptyHint`"需 PawnIO 驱动"。
  GPU 类不依赖 PawnIO，两态同字"无 GPU 温度传感器"。
  **关键坑（2026-07-22 补修）：未装 PawnIO 时 CPU 温度/频率传感器仍在快照里但读 0（或 NaN）**——`LhmSensorReader`
  只滤 `null`（`sensor.Value is float v`），`0f`/`NaN` 照发。故：① 候选构建（`Temps` 与 cpuclock lambda）用
  `IsValid`（`float.IsFinite(v) && v != 0`）滤掉 0/NaN，使无 PawnIO 时 CPU 两类正确落空态而非显"0°C/0MHz"；
  ② `HasDriverData` 只认**有效** CPU **温度**或 `/lpc` 读数（CPU 负载走性能计数器、免驱动亦有效，不能当驱动信号），
  按值判而非仅按存在判——否则 ① 滤空后 CPU 类会被误报"该机型无此传感器"。纯扩展侧，Host 零改动。
- **设置页 dev-reload 空白（非真实 bug，不修）** — CmdPal 里"禁用扩展→再启用"后第一次打开设置页**整表单空白**，
  再开即恢复；**干净装 / net 重启 CmdPal 不复现**（2026-07-22 干净机确认）。经溯源（Toolkit 源码）我方设置内容生成
  完全正确，系 CmdPal 宿主**热重载缓存了旧的空设置页**所致，属宿主侧、我方无干净 hook 可控，且不影响真实分发。
  记此以免重复排查。
- **PawnIO 后装需重启 Host 才生效** — 2026-07-22 发现。Host 启动时 init 一次 `LibreHardwareMonitorLib`（枚举硬件/驱动），
  之后**再装 PawnIO 无效**，直到 Host 重启才带上驱动重新枚举。用户实测 workaround：手动杀掉后台 `SensorMonitor.Host.exe`
  → 计划任务静默重拉 → 重新 init 即读到数据。考虑（择一/组合）：① 文档提示"先装 PawnIO 再首启，或后装后重启 Host"；
  ② Host 侧检测"关键 sensor 长期读 0/缺失"时周期性重建 `Computer`；③ 安装器/PawnIO 安装后触发一次 `schtasks /End`。
  优先级低，多数用户装一次驱动即稳定。

---

## 新增待办（2026-07-23）— 收尾、发布与产品化

> 用户 2026-07-23 批量提出的收尾/发布事项。分四类：表述与命名优化、能力扩展、打包与本地化、开源发布。
> 其中「不需要决策」的项（图标/作者名、分支/历史清理、Contributors、README）适合自行推进；命名、改名、
> donate 等涉及产品取舍的项需先与用户确认再动。

### 表述与命名优化

- [x] **「该机型无此传感器」→「无此传感器」** — 已改（2026-07-23）：经评估「未检测到传感器」会模糊两态分工
  （把"缺驱动"误说成"传感器未检测到"），退回已修复的歧义。按要求改为 **"无此传感器"**（直白且保留两态语义：
  驱动有数据但该类确无 = 无此传感器 ≠ 需 PawnIO 驱动）。改了 `SlotCategories.cs` 3 处 `MissingHint` (cpuclock/cputemp/boardtemp)
  + `SlotCategory.cs` 记录注释；GPU 类两态原已同字"无 GPU 温度传感器"，未动。
- [ ] **项目名「SensorMonitor」贴切性评估** — 用户确认目标为**对外发布**，需改更独特的名避免撞名。以下评估：

  **现状**："SensorMonitor" 描述强、一看就懂，但极通用——GitHub/Windows Store 大量同名项目，搜索与辨识度差。
  **改名成本**：身份锚点（MSIX Identity、管道名、计划任务、ProgramData 路径、命名空间、GitHub 仓库名）须联动改，
  且老用户升级时管道/任务/目录路径变 → 需迁移逻辑。改得越晚装机基数越大成本越高，**所以发布前改是最佳时机**。

  **候选（按 搜索友好 × 独特 × 易记 × 仍传达到意 排序）**：

  | # | 候选 | 含义 | 优势 | 劣势 |
  |---|------|------|------|------|
  | **1** | **SysPulse** | 系统脉搏 | 短(8字)、专业、搜索无撞名 | 有医疗 App 同名但硬件领域无冲突 |
  | **2** | **DockVitals** | Dock + 硬件体征 | 直白无需解释、说清在哪+做什么 | 略平淡、不够"品牌感" |
  | **3** | **LiveGauge** | 实时仪表盘 | 传达到实时读数本质、有辨识度 | 英文生僻词、中文推广需辅助说明 |
  | **4** | **PinSense** | Pin(钉在Dock) + Sense(感知) | 双关有趣、记忆点强 | 偏抽象、需上下文才能理解 |

  **我的推荐**：① **SysPulse**（对外发布首选——短/专业/独特）> ② DockVitals（安全保守——怕撞名就撞不到但也平平）。
  也可以你给一个偏好的方向（中文寓意？简短？幽默？）我再窄化候选。
  **需你拍板**这个再动（涉及仓库改名 + 全仓字符串联动）。

### 能力扩展

- [ ] **接入更多可用传感器** — 在现有 CPU 频率/CPU 温度/GPU 温度/主板温度之外，梳理 `LibreHardwareMonitorLib`
  还能稳定读到的传感器（风扇转速、功耗、电压、内存/存储温度、GPU 频率/占用等），评估可用性与呈现方式。
- [ ] **多语言支持（i18n）** — 扩展 UI 文案、band 标题/字幕、设置页、空态提示等的本地化框架（至少中/英）。

### 打包与作者信息

- [x] **作者名改为 AtomAntzzz** — 已改（2026-07-23）：MSIX `Package.appxmanifest` 的 `PublisherDisplayName`
  与 Inno `SensorMonitor.iss` 的 `AppPublisher` 均 `A Lone Developer`→`AtomAntzzz`。**未动** `Identity Publisher="CN=SensorMonitor Dev"`
  ——那是签名证书 subject，须与证书一致，非作者展示名。
- [x] **打包图标资产清单** — 已产出（2026-07-23）：`docs/references/icon-assets.md`。列清母版(1024²透明PNG)、
  MSIX `Assets/` 各 PNG（现存均为 VS 模板占位，含精确像素表 + scale/targetsize 建议）、Inno `SetupIconFile`
  与 Host `<ApplicationIcon>` 所需 `.ico`(16–256)。**待用户提供母版图**后即可脚本化导出收尾。

### R4b 与驱动自动化

- [ ] **完成 R4b** — WinGet/Release 提交 + 真证书 + 安装器健壮性硬化（承接 R4 收尾项）。
- [ ] **本体安装后自动装 PawnIO** — 评估安装器在装 SensorMonitor 时**顺带静默安装 PawnIO** 的可行性
  （许可/静默参数/UAC 次数/失败降级），与上文「PawnIO 后装需重启 Host」联动考虑。

### GitHub / 开源发布

- [x] **移除分支 `claude/optimistic-einstein-eb4c31`** — 已删（2026-07-23）：删前校验该分支**已完全并入 main**
  （`origin/main..` 0 独有提交、tip 是 main 祖先），无未合并工作丢失；远端 + 本地跟踪分支均已删，仅剩 `main`（was 135a0d9）。
- [ ] **清理 CLAUDE 相关历史（方案 A：只 strip 9 条 co-author 尾注）** — 用户已确认（2026-07-23）：单人仓，
  `szhaozechen@tencent.com` = 用户自己。"别的不管"。**仅**删 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
  尾注（9 条），不动 CLAUDE.md/`.claude/`。待执行 — filter-branch + force-push。
- [x] **完善 README.md** — 已改（2026-07-23）：`README.md` 重写为**英文主页**（顶部 `中文说明 →` 跳转、
  badges、Features/Requirements/Install/Build/架构/文档/Credits），作者署名 AtomAntzzz；新增
  `README.zh-CN.md` 中文对照（顶部 `English →` 回跳）。清掉旧「规划阶段」表述，Release/WinGet 标注为待办(R4b)、
  未编造 LICENSE / 死链。
- [ ] **移除 Contributors 中的 claude** — 同上一项同一操作。strip 尾注后 force-push，GitHub 自动刷新贡献者名单。
- [ ] **上架 Release 包** — 发布安装器/扩展产物到 GitHub Release。
- [x] **开放 donate（框架已就绪，待用户填渠道）** — 已建（2026-07-23）：`.github/FUNDING.yml` 模板含所有主流渠道注释
  （GitHub Sponsors / Ko-fi / Buy Me a Coffee / Patreon / 爱发电 / 支付宝/微信收款码链接），README 英/中两版底部分别
  加了 `## Support` 节指向 FUNDING.yml。**用户只需取消对应行注释 + 填入 ID/链接，提交即生效**（仓库自动出现 Sponsor 按钮）。
  也接受直接放收款码图片（放 `.github/` 或根目录后在 FUNDING.yml 贴 raw URL）。
