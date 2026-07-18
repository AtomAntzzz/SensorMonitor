# 架构决策记录

> 每条 = 决策 + 依据。推翻任何一条前先读完它的依据，并更新本文件。

## D1 — 双进程：提权 Host + 非提权扩展

**决策**：传感器读取放独立进程 `SensorMonitor.Host`（manifest `requireAdministrator`），CmdPal 扩展只做显示与拉起。

**依据**：CmdPal 扩展是 MSIX 非提权进程；而 CPU 温度/主板温度依赖 ring0（PawnIO）+ 管理员权限（见 `references/sensor-sources.md`）。进程内引用 LibreHardwareMonitorLib 只能拿到残缺数据（大致只剩 GPU）。提权无法在进程内"升级"，只能另起进程。

**代价**：多一个进程、一次 UAC（后续路线 R3 用计划任务消除）、一层 IPC。

## D2 — 数据源：LibreHardwareMonitorLib 进程内库，而非 LHM 应用的 HTTP 接口

**决策**：Host 直接引用 NuGet 库读硬件，不拉起 LibreHardwareMonitor GUI 应用再抓它的 `:8085/data.json`。

**依据**：候选对比见 `references/sensor-sources.md`。库方案不依赖用户额外安装/配置 GUI 应用，"随扩展一起打开"体验干净（Host 是我们自己的无窗口进程）；HTTP JSON 结构非稳定契约。

## D3 — IPC：命名管道，一问一答行协议

**决策**：管道名 `SensorMonitor.Host.v1`；客户端写一行 `GET`，服务端回一行 JSON 快照后断开。服务端 ACL 显式放开 Authenticated Users。

**依据**：
- 提权服务端默认 ACL 会拒绝非提权客户端连接 —— 这是本架构的隐蔽坑，必须显式 `PipeSecurity`（跨完整性级别通信）。管道的 ACL 模型比共享内存（MMF）简单可靠。
- 一问一答 + 客户端轮询足够（显示端本来就是定时刷新），订阅推送是 YAGNI。
- localhost HTTP 被否：要选端口、防火墙提示、杀伤力过剩。
- 管道名带 `v1`：协议破坏性变更时换名，避免新旧版本互咬。

## D4 — 刷新模型：Host 缓存快照 2s 刷新，扩展 2s 轮询

**决策**：Host 后台定时读硬件更新缓存，管道请求只读缓存；扩展定时 `TryFetch` 更新 Dock band 的 `Title`。

**依据**：一次全量硬件 Update 数十 ms，按请求现读会放大延迟且多客户端时重复开销。2s 对温度/频率显示足够灵敏。Dock 刷新走官方 `NowDockBand` 同款模式（改 `ListItem.Title` 触发重绘），是文档背书的先例。

## D5 — DTO 在 Host 与扩展各留一份拷贝（MVP）

**决策**：`SensorSnapshot`/`SensorReading` 两边各一份，暂不抽共享项目。

**依据**：MSIX 打包项目引用普通 classlib 有打包坑；协议 v1 就 6 个字段，拷贝成本低于处理打包问题。字段稳定后（R4 打包阶段）再评估合并。

## D6 — MVP 默认只显示三项：CPU 频率 / CPU 温度 / GPU 温度（2026-07-18 修订）

**决策**：写死匹配规则（按本机实测传感器清单调整），传感器选择/配置推迟到 R2。第三格原定「主板温度」，修订为 **CPU 温度**。

**依据**：用户原始需求三项；传感器 Id 因硬件而异，做通用配置 UI 前先用实测清单落地一版能跑的；Dock 空间有限。改 CPU 温度的原因：实测主板 SuperIO 即使装了 PawnIO 也未被 LHM 0.9.6 识别（芯片支持问题，非权限问题，见 `references/sensor-sources.md` 实测记录），CPU Package 是现成可读且更有参考价值的整机温度。

## D7 — 自动拉起绝不弹 UAC（post-MVP 加固引入）

**决策**：Host 的自动拉起只走计划任务静默通道（`schtasks /Run` 触发 `--install-task` 注册的 `/RL HIGHEST` 任务，30s 节流持续重试）；直接拉起 exe（弹 UAC）只允许发生在用户显式点击。

**依据**：MVP 的"扩展加载即自动 UAC"意味着每次登录弹一次 UAC（缺陷 F5），band 未上 Dock 也弹。触发自己名下最高权限计划任务不需要提权，是自有软件免重复 UAC 的标准做法；未注册任务时静默失败无骚扰。注册本身借 Host 既有的 UAC 提权一次完成。

## D8 — Host 无窗口（WinExe）+ 文件日志（post-MVP 加固引入）

**决策**：Host `OutputType=WinExe`，错误落 `%ProgramData%\SensorMonitor\host.log`（超 1MB 重开）；`--dump` 用 `AttachConsole(-1)` 贴附父终端保住调试输出。

**依据**：常驻可见控制台窗口是 UX 硬伤（缺陷 F4），且窗口一关错误信息全丢。ProgramData 对提权进程可写、对用户可读。
