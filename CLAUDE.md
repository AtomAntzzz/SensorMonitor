# SensorMonitor — Agent 导航

> PowerToys Command Palette **Dock 常驻硬件传感器显示扩展**（CPU 频率 / 主板温度 / GPU 温度）。
> C# / .NET 8 · CmdPal Extensions SDK ≥ 0.9.260303001 · LibreHardwareMonitorLib

## 当前状态

**Phase 1（Host）已实现并验证；Phase 0/2/3（扩展）待桌面会话完成。**

- ✅ **Host（Task 2–5）**：`src/SensorMonitor.Host` + `tests/SensorMonitor.Host.Tests`，10 单测全绿；
  实测读到传感器（i9-12900K，未装 PawnIO → 仅 GPU 温度可用，见 `docs/references/sensor-sources.md` 末尾）；
  提权单实例、缓存刷新、命名管道 JSON 服务均已跑通（管道取回 71 传感器）。
- ⏳ **扩展（Task 1/6/7/8）**：依赖 CmdPal 模板生成器 / VS Deploy / PowerToys Dock / UAC，
  须在桌面会话手动完成。扩展侧源码（含两处实测修正）已暂存 `docs/staged-extension/`，按其 README 拷入。

实施入口：`docs/plans/2026-07-18-sensormonitor-mvp.md`；续做扩展看 `docs/staged-extension/README.md`。

## 一句话架构

双进程：`SensorMonitor.Host`（提权，LibreHardwareMonitorLib 读传感器，命名管道供数据）+ `SensorMonitorExtension`（CmdPal MSIX 扩展，Dock band 每 2s 轮询刷新，检测到 Host 未运行自动 UAC 拉起）。**为什么必须双进程**：见 `docs/architecture.md` D1。

## 文档地图（按需读，勿一次全读）

| 要做什么 | 读 |
|----------|-----|
| 实施 / 续做任何 task | `docs/plans/2026-07-18-sensormonitor-mvp.md`（先看 checkbox 进度） |
| 动架构 / 质疑某个设计 | `docs/architecture.md`（D1–D6，推翻前先读依据） |
| 碰扩展 / Dock / 部署问题 | `docs/references/cmdpal-extension.md` |
| 碰传感器 / 权限 / 驱动问题 | `docs/references/sensor-sources.md` |

## 高频坑（详情在对应参考文档）

1. VS 里必须 **Deploy**（不是 Build）+ 面板内手动 **Reload**，改动才生效。
2. `.gitignore` 严禁忽略 `launchSettings.json` / `*.pubxml`（MSIX 部署依赖，本仓库已处理，别用标准模板覆盖）。
3. Dock item 的 `Command.Id` 为空 → 被静默忽略。
4. CPU/主板温度需要 **管理员 + PawnIO 驱动**（WinRing0 已被 Defender 封杀）；非提权进程只读得到 GPU。
5. 提权管道服务端必须显式 `PipeSecurity` 放开 Authenticated Users，否则非提权扩展连不上。

## 常用命令

```bash
dotnet test                                          # Host 侧全部单测
dotnet run --project src/SensorMonitor.Host -- --dump  # 管理员终端：打印本机传感器 JSON
```

扩展的构建部署走 Visual Studio（Deploy → CmdPal 内 Reload），无 CLI 等价物。
