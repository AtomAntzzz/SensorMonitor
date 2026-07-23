# 图标资产清单（SensorMonitor）

> 目的：把「打包/发布需要哪些图、什么格式、放哪个目录」一次说清，方便一次性产出并替换掉当前的
> VS 模板占位图。截至 2026-07-23，`Assets/` 里全是模板默认图（纯占位），Inno 与 Host 均未设自定义图标。

## TL;DR — 你只需产出这几样

1. **1 张母版**：`1024×1024` 透明背景 PNG（或矢量 SVG）。其余尺寸都从它导出，保证风格一致。
2. **一组 MSIX PNG**（替换 `Assets/` 下同名文件，见 §1）。
3. **1 个多尺寸 `.ico`**（含 16/24/32/48/64/128/256），给 Inno 安装器与 Host exe 用（见 §2、§3）。

> 给我母版后，各 PNG 尺寸 + `.ico` 我可以用脚本（ImageMagick / .NET）自动导出，你不必手工缩放。

**设计注意**：主体图形收在中心 ~66% 安全区，四周留白——`Square44x44` 在开始菜单/任务栏会被加圆角与
plated 底，靠边的像素会被裁。所有 MSIX 资产用 **32-bit RGBA、透明背景**（manifest 里 `BackgroundColor="transparent"`）。

---

## §1. MSIX 扩展图标（PowerToys CmdPal 扩展）

- **目录**：`src/SensorMonitorExtension/SensorMonitorExtension/Assets/`
- **被 manifest 引用的基名**（`Package.appxmanifest`）：`StoreLogo.png`、`Square44x44Logo`、
  `Square150x150Logo`、`Wide310x150Logo`、`SplashScreen`。MSIX 按基名 + scale 后缀解析
  （如 `Square150x150Logo` → 实际取 `Square150x150Logo.scale-200.png`）。

| 资产（基名） | manifest 用途 | 基准尺寸 (scale-100) | 现有文件 & 像素 | 建议 |
|---|---|---|---|---|
| `StoreLogo.png` | Apps 列表 / 商店图标 | 50×50 | `StoreLogo.png` 50×50 ✅ | 替换；可补 scale-200 (100×100) |
| `Square44x44Logo` | 任务栏 / 开始菜单 / CmdPal 列表 | 44×44 | `.scale-200` 88×88 ✅；`.targetsize-24_altform-unplated` 24×24 ✅ | 替换；进阶补 `targetsize-16/32/48/256_altform-unplated` + `scale-100` 44×44 |
| `Square150x150Logo` | 中磁贴 | 150×150 | `.scale-200` 300×300 ✅ | 替换；可补 scale-100 150×150 |
| `Wide310x150Logo` | 宽磁贴 (DefaultTile) | 310×150 | `.scale-200` 620×300 ✅ | 替换（可选，磁贴少用） |
| `SplashScreen` | 启动图 | 620×300 | `.scale-200` 1240×600 ✅ | 替换（可选） |
| `LockScreenLogo` | **未被 manifest 引用** | 24×24 | `.scale-200` 48×48 | 模板残留，**可删** |

**最小可用**：只替换上表「现有文件」那批（保持同名同像素）即可品牌化，不必补全 scale。
**进阶（多 DPI 更清晰）**：每个 logo 补 `scale-100/125/150/200/400`；`Square44x44` 再补
`targetsize-16/24/32/48/256`（各带 `_altform-unplated`，透明无底板）。命名例：
`Square44x44Logo.targetsize-32_altform-unplated.png`。

---

## §2. Inno 安装器图标

- **目录**：`installer/`（放 `SensorMonitor.ico`，与 `SensorMonitor.iss` 同级）。
- **Setup 向导图标**：`[Setup]` 段当前**未设** `SetupIconFile`，用的是 Inno 默认图标。加一行：
  ```ini
  SetupIconFile=SensorMonitor.ico
  ```
  路径相对 `.iss` 所在目录（即 `installer/`）。注意本工程 `.iss` 由 `installer/build.ps1` 用 `/D`
  传参编译（勿手工双击编译）；`SetupIconFile` 是相对路径，放 `installer/` 下即可被 ISCC 找到。
- **卸载显示图标**：`UninstallDisplayIcon={app}\Host\SensorMonitor.Host.exe` —— 取 **Host exe 内嵌图标**。
  Host 目前没内嵌自定义图标（见 §3），所以现在是默认 .NET 图标。要品牌化须给 Host 设 `ApplicationIcon`。

**`.ico` 规格**：单文件内含多尺寸 **16 / 24 / 32 / 48 / 64 / 128 / 256**，32-bit RGBA；256 那层建议用
PNG 压缩存（现代 Windows 支持）。同一个 `.ico` 可同时给 Inno 与 Host 复用。

---

## §3. Host exe 内嵌图标（卸载项 / 任务管理器 / 任务栏都靠它）

- **目录**：`src/SensorMonitor.Host/`（放 `SensorMonitor.Host.ico`，或复用 §2 的同一 `.ico`）。
- **csproj 加**（`SensorMonitor.Host.csproj`）：
  ```xml
  <ApplicationIcon>SensorMonitor.Host.ico</ApplicationIcon>
  ```
  设置后 `UninstallDisplayIcon`（§2）会自动变成品牌图标，无需改 `.iss`。
- 规格同 §2 的 `.ico`。

---

## §4. 产出清单（交付给我即可自动收尾）

- [ ] 母版 `1024×1024` 透明 PNG（或 SVG）×1
- [ ] MSIX：替换 `Assets/` 下 PNG（至少现有那批同名同尺寸；进阶补 scale/ targetsize 变体）
- [ ] 多尺寸 `.ico` ×1（16–256），放 `installer/` 与 `src/SensorMonitor.Host/`（或一份两处引用）
- [ ] （可选，我来做）Host csproj 加 `<ApplicationIcon>`、`.iss` 加 `SetupIconFile`

> 交付母版后我可用 ImageMagick 一条命令导出全部 PNG 尺寸并打包 `.ico`；你只需给一张图。
