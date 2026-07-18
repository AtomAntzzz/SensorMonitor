# SensorMonitor — Agent 导航

> PowerToys Command Palette **Dock 常驻硬件传感器显示扩展**（CPU 频率 / 主板温度 / GPU 温度）。
> C# / .NET 8 · CmdPal Extensions SDK ≥ 0.9.260303001 · LibreHardwareMonitorLib

## 当前状态

**MVP + 加固优化（post-mvp-hardening Task 1–8）代码完成，11 单测全绿；部分实机验证待桌面会话。**

- ✅ MVP 全链路已在实机验证过（Dock 实时显示 CPU 频率/CPU 温度/GPU 温度）。
- ✅ 加固：管道单连接超时、刷新防重入、Host 无窗口化+文件日志（`%ProgramData%\SensorMonitor\host.log`）、
  扩展防崩+数据过期提示、计划任务静默提权（`--install-task`）、band 懒启动、传感器浏览页。
- ⏳ 待桌面手动验证：VS Deploy 新扩展 + Reload、`--install-task` 注册与静默拉起、无窗口 Host 观察
  （清单见 `docs/plans/2026-07-18-post-mvp-hardening.md` 各 task 的验证步骤）。

后续优化路线（R2/R4/R6/R7/R8）见 `docs/plans/2026-07-18-post-mvp-hardening.md` 末尾。

## 一句话架构

双进程：`SensorMonitor.Host`（提权，LibreHardwareMonitorLib 读传感器，命名管道供数据）+ `SensorMonitorExtension`（CmdPal MSIX 扩展，Dock band 每 2s 轮询刷新，检测到 Host 未运行自动 UAC 拉起）。**为什么必须双进程**：见 `docs/architecture.md` D1。

## 文档地图（按需读，勿一次全读）

| 要做什么 | 读 |
|----------|-----|
| 实施 / 续做任何 task | `docs/plans/2026-07-18-post-mvp-hardening.md`（MVP 计划已完结归档） |
| 动架构 / 质疑某个设计 | `docs/architecture.md`（D1–D8，推翻前先读依据） |
| 碰扩展 / Dock / 部署问题 | `docs/references/cmdpal-extension.md` |
| 碰传感器 / 权限 / 驱动问题 | `docs/references/sensor-sources.md` |

## 高频坑（详情在对应参考文档）

1. VS 里必须 **Deploy**（不是 Build）+ 面板内手动 **Reload**，改动才生效。
2. `.gitignore` 严禁忽略 `launchSettings.json` / `*.pubxml`（MSIX 部署依赖，本仓库已处理，别用标准模板覆盖）。
3. Dock item 的 `Command.Id` 为空 → 被静默忽略。
4. CPU/主板温度需要 **管理员 + PawnIO 驱动**（WinRing0 已被 Defender 封杀）；非提权进程只读得到 GPU。
5. 提权管道服务端必须显式 `PipeSecurity` 放开 Authenticated Users，否则非提权扩展连不上。
6. **Host 运行时锁死自己的 `bin/`**：重建/跑测试前先停 Host（管理员终端 `taskkill /f /im SensorMonitor.Host.exe`），
   或用 `--artifacts-path` 输出到独立目录绕开（agent 会话无提权时的标准做法）。
   **扩展同理**：松散注册的扩展被 CmdPal 激活后进程常驻，锁死扩展 `bin/`——CLI 重建前先
   `taskkill /f /im SensorMonitorExtension.exe`（无需提权；`scripts/setup.ps1` 阶段 4 已内置）。
7. 自动拉起遵守 D7：自动路径只走计划任务静默通道，UAC 只允许出现在用户显式点击。

## 常用命令

```bash
scripts\setup.cmd                 # 新机器一键引导（自提权，装工具链+构建+部署+计划任务）；-CheckOnly 只体检
dotnet test tests/SensorMonitor.Host.Tests            # Host 侧全部单测
dotnet run --project src/SensorMonitor.Host -- --dump  # 管理员终端：打印本机传感器 JSON
# 管理员终端一次性注册计划任务（此后扩展可静默拉起 Host，无 UAC）：
#   src/SensorMonitor.Host/bin/.../SensorMonitor.Host.exe --install-task
# 停 Host（管理员）：taskkill /f /im SensorMonitor.Host.exe
```

扩展的构建可 CLI（`dotnet build -p:Platform=x64`），**部署**仍走 Visual Studio（Deploy → CmdPal 内 Reload）。
