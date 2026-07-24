# CmdPal 扩展开发参考（含 Dock API）

> 提炼自微软官方文档（2026-03 版），实现时以最新文档与 SDK 实际 API 为准。
> - 创建扩展：https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/creating-an-extension
> - Dock 支持：https://learn.microsoft.com/zh-cn/windows/powertoys/command-palette/adding-dock-support

## 基本事实

- 扩展 = C# + WinAppSDK 的 **MSIX 打包应用**，CmdPal 自动发现已部署的扩展包。
- 官方推荐生成方式：命令面板内运行 **`Create a new extension`** 模板生成器（比手搓项目可靠得多，MSIX 打包属性繁琐）。
- 前置：Visual Studio（WinUI/AppSDK 工作负载）、PowerToys、Windows 开发者模式。

## 模板项目结构

```
<ExtensionName>/
│  Directory.Build.props / Directory.Packages.props / nuget.config / <ExtensionName>.sln
└─ <ExtensionName>/
    app.manifest / Package.appxmanifest / Program.cs
    <ExtensionName>.cs                    # IExtension 实现（COM server 入口）
    <ExtensionName>CommandsProvider.cs    # CommandProvider 子类 —— 主要工作区
    Pages/<ExtensionName>Page.cs
    Properties/launchSettings.json + PublishProfiles/*.pubxml
```

## 部署-调试循环（⚠️ 三个坑）

1. **必须 Deploy 不是 Build**：VS 菜单 `Build → Deploy <ExtensionName>`。仅 Build 不更新已部署包；"(Unpackaged)" 启动配置也不部署。
2. **改完必须手动 Reload**：CmdPal 不感知包更新。面板里运行 `Reload`（副标题 "Reload Command Palette Extension"）。
3. **标准 C# .gitignore 会毁掉可部署性**：必须**不**忽略 `**/Properties/launchSettings.json` 与 `*.pubxml`（WinAppSDK 部署依赖），本仓库 `.gitignore` 已按此处理。

## Dock API（SDK ≥ 0.9.260303001）

两个接口，`CommandProvider` 基类直接 override：

| 接口 | 方法 | 用途 |
|------|------|------|
| `ICommandProvider3` | `GetDockBands()` | 返回 `ICommandItem[]`，每项 = Dock 上一个原子 band |
| `ICommandProvider4` | `GetCommandItem(string id)` | 让用户能把扩展内**嵌套**命令固定到 Dock（MVP 不需要） |

band 渲染规则（由 `ICommandItem.Command` 类型决定）：

| Command 类型 | Dock 表现 |
|--------------|----------|
| `IInvokableCommand` | 单个按钮，点击执行 |
| `IListPage` | 页内所有 item 平铺成一排按钮 |
| `IContentPage` | 单个可展开按钮（弹出面板） |

关键规则与模式：

- ⚠️ **每个返回的 `ICommandItem` 的 `Command` 必须有非空 `Id`**，否则该项被静默忽略。
- 多按钮一条 band：`new WrappedDockItem([listItem1, listItem2], "带唯一id", "显示名")`；也有 `WrappedDockItem(ICommand, string)` 单命令重载。
- **实时刷新模式**（官方 Time & Date 扩展的 `NowDockBand` 先例）：band item 是一个 `ListItem`，定时更新其 `Title`/`Subtitle`，Dock 随属性变更通知自动重绘。本项目 `SensorDockBand` 就是这个模式。
- 官方扩展源码可对照：PowerToys 仓库 `src/modules/cmdpal/ext/`。

## 环境要求汇总

- PowerToys：需带 Dock 功能的版本（2026-03 后发布）；以设置里存在 Dock 页为准。
- `Microsoft.CommandPalette.Extensions` NuGet ≥ **0.9.260303001**。
- Toolkit 常用类：`CommandProvider` / `CommandItem` / `ListItem` / `InvokableCommand` / `WrappedDockItem`（`Microsoft.CommandPalette.Extensions.Toolkit` 命名空间）。

## i18n（多语言，2026-07-24 交付）

扩展 UI 走 **`.resx` + `System.Resources.ResourceManager`**（不是 WinUI，用 `.resw`/`ResourceLoader`
反而多依赖；官方 CmdPal Toolkit 自身就用 `.resx`）。三件套在 `src/SysPulseExtension/SysPulseExtension/Localization/`：

- `Strings.resx` — **英文 = 中性/默认**，随主程序集内嵌（trim/single-file 安全）。
- `Strings.zh-CN.resx` — 中文，出卫星程序集 `zh-CN/SysPulseExtension.resources.dll`。
- `L.cs` — `L.Get("Key")` / `L.Format("Key", arg)`；`Get` 按 `CultureInfo.CurrentUICulture` 选串（缺键回落键名，
  不抛异常），`Format` 的**数值格式化**用 `CurrentCulture`（与原插值串一致，避 CA1305）。

**语言跟随系统**：扩展跑在用户会话里，`CurrentUICulture` 即用户 Windows 显示语言，零检测代码；v1 无设置页手动覆盖。

**加一个字符串**：在 `Strings.resx` 和**每个** `Strings.<lang>.resx` 各加一条 `<data name="Key">`，调用处写 `L.Get("Key")`。
**加一门语言**：新增 `Strings.<culture>.resx`（键与英文版对齐），SDK 自动出对应卫星程序集，无需改 `.csproj`。

坑与验证：

- `ResourceManager` baseName 必须 = `RootNamespace + 文件夹 + 文件名` = **`SysPulseExtension.Localization.Strings`**；
  写错不报错、只静默回落英文（漏翻假象）。改动后可反射校验清单名：
  主程序集应有 `SysPulseExtension.Localization.Strings.resources`，zh-CN 卫星应有 `...Strings.zh-CN.resources`。
- **必须在打包裁剪版实测**：Release 是 `PublishTrimmed`+`PublishSingleFile`+`IsAotCompatible`。英文为中性内嵌无忧；
  zh-CN 卫星须确认被打进单文件且能加载——把 Windows 显示语言切成中文、净启 CmdPal（`x-cmdpal://reload` 会累加 band，
  见坑 #9）看 dock/浏览页/选择页/设置页是否出中文。这是本仓库 trim 雷区（曾坑过反射式 STJ）的既定验证动作。
- **不本地化**：品牌名 `SysPulse`、各 `Id`/`Command.Id`、Segoe 字形 `\uExxx`、`°C`/`°F` 符号、上游传感器名/`Type` 枚举（动态英文源）。
