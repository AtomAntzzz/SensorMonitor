# 传感器数据源调研与选型

## 结论

**选 LibreHardwareMonitorLib（NuGet，MPL-2.0），跑在独立提权进程 `SensorMonitor.Host` 里**，不在扩展进程内直接引用。理由见下文权限矩阵与 `docs/architecture.md` D1/D2。

## 候选对比

| 方案 | 开源 | 集成方式 | 否决/入选理由 |
|------|------|---------|--------------|
| **LibreHardwareMonitorLib** ✅ | MPL-2.0，活跃维护 | NuGet 库，进程内调用 | 覆盖 CPU/GPU/主板/内存/风扇最全；.NET 8 目标可用 |
| LibreHardwareMonitor 应用自带 HTTP server（`:8085/data.json`） | 同上 | 拉起整个 GUI 应用 + 解析其 JSON | 依赖整个 GUI 应用、JSON 结构非稳定契约、多一层配置（需用户开 web server 选项）；库方案更可控 |
| OpenHardwareMonitor | MPL-2.0 | NuGet | 长期停更，LHM 即其活跃分支，无理由选旧的 |
| HWiNFO shared memory | ❌ 闭源（共享内存接口需付费版解锁） | 读共享内存 | 不满足"开源接口"要求 |
| WMI `MSAcpi_ThermalZoneTemperature` | 系统自带 | WMI 查询 | 多数主板不上报或只有一个无意义温度，覆盖面完全不够 |

## ⚠️ 权限与内核驱动（本项目最大的坑）

读 CPU MSR（温度/频率细节）和主板 SuperIO（主板温度/风扇）需要 ring0 访问：

1. **WinRing0 时代已结束**：LHM 旧版用 WinRing0 驱动，其漏洞（CVE-2020-14979）导致 2025-03 起被 Windows Defender 按 `VulnerableDriver:WinNT/Winring0` / `HackTool:Win32/Winring0` 拦截，同类工具（FanControl、OpenRGB 等）全部中招。
2. **现行方案是 PawnIO**：LHM 新版（0.9.6+）迁移到 PawnIO（微软签名、受控执行的 ring0 驱动）。含义：
   - 目标机器可能需要**安装 PawnIO**（独立安装包或随 LHM 分发的模块）才能读到 CPU 温度/主板温度；
   - 读这些传感器仍需**管理员权限**运行调用方；
   - 已知问题：x86 构建下 PawnIO 有兼容 issue（本项目只出 x64，可规避）；PawnIO 会被个别反作弊（FACEIT）拦截 —— 对本项目只是"玩此类游戏时读数可能缺失"，不是阻断项。
3. **无管理员权限时的降级表现**（大致）：GPU 温度（走 NVML/NVAPI/ADL 用户态 API）通常仍可读；CPU 温度、主板温度、风扇转速读不到。这正是"必须有提权 Host 进程"的依据 —— CmdPal 扩展本体是 MSIX 非提权进程，进程内引用库只能拿到残缺数据。

## 库用法要点

- 入口 `Computer` 类：按需开 `IsCpuEnabled / IsGpuEnabled / IsMotherboardEnabled / IsMemoryEnabled`，`Open()` 后用标准 `UpdateVisitor` 模式遍历（`hardware.Update()` + 递归 `SubHardware`）。
- 传感器标识：`sensor.Identifier`（如 `/intelcpu/0/temperature/8`）稳定可作配置键；`SensorType` 枚举区分 Temperature/Clock/Load/Fan/Power 等。
- 主板传感器挂在 Motherboard 硬件的 **SubHardware**（SuperIO 芯片，如 Nuvoton NCT67xx）下，遍历时不可漏掉 SubHardware 递归。
- 一次全量 `Update()` 数十毫秒级 —— Host 缓存快照 + 定时刷新，勿按请求现读。

## 参考链接

- https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- https://www.nuget.org/packages/LibreHardwareMonitorLib/
- Defender 拦截 WinRing0 事件：https://it.slashdot.org/story/25/03/14/1351225/
- PawnIO 迁移背景（FanControl 侧记录）：https://deepwiki.com/Rem0o/FanControl.Releases/5.4-driver-evolution-and-anti-virus-issues
- x86 PawnIO issue：https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/1895

## 本机实测传感器清单（Task 3 Step 6 时填写）

> 运行 `dotnet run --project src/SensorMonitor.Host -- --dump`（管理员）后，把本机关键传感器的 `Id / Hardware / Name / Type` 记录在此，供 Task 7 的默认匹配规则使用。

（待填）
