# SensorMonitor（中文说明）

> 🌐 **[English →](README.md)**

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![PowerToys](https://img.shields.io/badge/PowerToys-Command%20Palette-blue)

一个 **PowerToys Command Palette（命令面板）的 Dock 常驻扩展** —— 在屏幕边缘的命令面板 Dock 工具条上，
实时显示 CPU 频率与 CPU / GPU / 主板温度。

传感器数据来自开源的 [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
（MPL-2.0），由随扩展**按需自动拉起的提权后台进程**提供。

## 功能

- **四个预设 Dock 槽位** —— CPU 频率、CPU 温度、GPU 温度、主板温度，每项独立控件，不会被截断。
- **右键槽位**在同类别里切换上一个 / 下一个指标；**单击槽位**打开候选选择页，列出该类别的可选项。
- **设置页** —— 刷新间隔（1 / 2 / 5 秒）与温度单位（°C / °F）。
- **选择持久化** —— 槽位选择重启后保留。
- **静默自启** —— 安装后后台 Host 走计划任务运行，运行期**无 UAC 弹窗**。

## 环境要求

- Windows 10 / 11（x64 或 ARM64）
- 装有 **Command Palette** 的 PowerToys（CmdPal Extensions SDK ≥ 0.9.260303001）
- CPU 与主板温度需**另装 PawnIO 驱动**；GPU 温度无需驱动即可读。

## 安装

分发方式为**签名 Inno 安装器**：内置自包含 Host、注册计划任务、并把扩展 MSIX 全机预置 ——
安装时一次 UAC、运行期零 UAC，卸载入口一处清干净。

> ⏳ 预编译的 **Release / WinGet** 包尚未发布（记为 R4b）。在此之前请从源码构建安装器（见下）。

## 从源码构建

```powershell
# Host 单元测试
dotnet test tests/SensorMonitor.Host.Tests

# 构建签名安装器 → installer/Output/SensorMonitorSetup_x64.exe
powershell -ExecutionPolicy Bypass -File installer/build.ps1
```

扩展本身可 CLI 构建（`dotnet build -p:Platform=x64`），但**开发期部署**仍走 Visual Studio
（**Deploy** → 命令面板内 **Reload**）。细节见 [docs/references/installer.md](docs/references/installer.md)
与 [docs/references/msix-packaging.md](docs/references/msix-packaging.md)。

## 工作原理

双进程：

- **`SensorMonitor.Host`** —— 提权进程，用 LibreHardwareMonitorLib 读传感器，通过命名管道提供数据快照。
- **`SensorMonitorExtension`** —— CmdPal MSIX 扩展；Dock 槽位控件每秒轮询共享快照缓存，检测到 Host 未运行时
  由计划任务静默拉起。

为什么双进程（提权 + 驱动访问）不可避免：见 [docs/architecture.md](docs/architecture.md)（D1）。

## 文档

| 主题 | 文档 |
|------|------|
| 架构与设计决策 | [docs/architecture.md](docs/architecture.md) |
| 路线图 / 当前状态 | [docs/plans/2026-07-18-verification-and-next-phase.md](docs/plans/2026-07-18-verification-and-next-phase.md) |
| 扩展与 Dock 说明 | [docs/references/cmdpal-extension.md](docs/references/cmdpal-extension.md) |
| 传感器与驱动 | [docs/references/sensor-sources.md](docs/references/sensor-sources.md) |

## 致谢

- 传感器后端：[LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)（MPL-2.0）。
- 作者：**AtomAntzzz**

## 赞助

如果 SensorMonitor 对你有帮助，欢迎支持项目。可用渠道见 [`.github/FUNDING.yml`](.github/FUNDING.yml)
——取消对应行注释并填入你的 ID 即可，提交后仓库自动出现 Sponsor 按钮。
