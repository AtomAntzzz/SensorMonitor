# SensorMonitor MVP Implementation Plan

> ✅ **已完成归档（2026-07-18）**：MVP 全部落地并实机验证。后续工作见
> `docs/plans/2026-07-18-post-mvp-hardening.md`（缺陷修复 + 体验优化）。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 PowerToys Command Palette 的 Dock 上常驻显示硬件传感器读数（CPU 频率 / 主板温度 / GPU 温度），传感器数据源随扩展自动拉起。

**Architecture:** 双进程 —— ① `SensorMonitor.Host`：提权后台进程，用 LibreHardwareMonitorLib 读传感器，经命名管道以 JSON 快照对外提供；② `SensorMonitorExtension`：CmdPal MSIX 扩展，通过 `ICommandProvider3.GetDockBands()` 提供 Dock band，定时轮询管道并更新 `ListItem` 的 Title/Subtitle 实现实时刷新。扩展检测到 Host 未运行时自动（UAC）拉起。设计依据见 `docs/architecture.md`。

**Tech Stack:** C# / .NET 8 · Microsoft.CommandPalette.Extensions SDK ≥ 0.9.260303001 · LibreHardwareMonitorLib（NuGet，MPL-2.0）· System.IO.Pipes · xUnit

**前置阅读:** `docs/references/cmdpal-extension.md`（SDK/Dock API 细节）、`docs/references/sensor-sources.md`（LHM 权限/驱动坑）

---

## 目标目录结构（完成后）

```
SensorMonitor/
│  SensorMonitor.sln
│  .gitignore                     # 已存在；注意不忽略 launchSettings.json / *.pubxml
├─ docs/                          # 已存在（本规划与参考文档）
├─ src/
│  ├─ SensorMonitorExtension/     # CmdPal 模板生成（Task 1），MSIX 打包
│  │   SensorMonitorExtension.csproj
│  │   SensorMonitorExtensionCommandsProvider.cs
│  │   Dock/SensorDockBand.cs     # Task 6
│  │   Ipc/PipeSensorClient.cs    # Task 7
│  │   Ipc/SensorSnapshot.cs      # Task 7（与 Host 侧同构的 DTO，MVP 阶段两份拷贝）
│  │   Commands/LaunchHostCommand.cs  # Task 8
│  └─ SensorMonitor.Host/         # Task 2 起
│      SensorMonitor.Host.csproj
│      app.manifest               # requireAdministrator
│      Program.cs
│      Model/SensorSnapshot.cs
│      Sensors/ISensorReader.cs
│      Sensors/SensorMapper.cs
│      Sensors/LhmSensorReader.cs
│      Ipc/PipeJsonServer.cs
└─ tests/
   └─ SensorMonitor.Host.Tests/
       SensorSnapshotTests.cs
       SensorMapperTests.cs
       PipeJsonServerTests.cs
```

> 职责边界：`Sensors/` 只管"读硬件→领域模型"，`Ipc/` 只管"领域模型→线路协议"，两者通过 `SensorSnapshot` 解耦。硬件依赖被隔离在 `LhmSensorReader` 一个文件内，其余全部可无硬件单测。

---

## Phase 0 — 环境验证（Spike）

### Task 1: 验证 Dock 可用性并生成扩展模板

**Files:**
- Create: `src/SensorMonitorExtension/`（由 CmdPal 模板生成器产出整个项目）
- Modify: `.gitignore`（确认未忽略 `launchSettings.json` / `*.pubxml`）

- [ ] **Step 1: 确认 PowerToys 版本支持 Dock**

打开 PowerToys 设置 → 确认存在 **Dock** 功能页并启用。Dock 需要 2026-03 之后的 PowerToys 版本（扩展 SDK 0.9.260303001+）。若无此页，先升级 PowerToys，再继续。

- [ ] **Step 2: 用 CmdPal 内置模板生成扩展项目**

打开命令面板 → 运行 `Create a new extension`，填写：
- ExtensionName: `SensorMonitorExtension`
- Extension Display Name: `Sensor Monitor`
- Output Path: `D:\Workspace\SensorMonitor\src`（生成器会在其下建同名子目录）

- [ ] **Step 3: 校验 .gitignore 不吞部署关键文件**

模板依赖 `Properties/launchSettings.json` 与 `*.pubxml` 才能部署 MSIX。检查仓库根 `.gitignore` **没有**这两条忽略规则（本仓库初始 .gitignore 已处理，核对即可）。

- [ ] **Step 4: 确认 SDK 版本满足 Dock 要求**

打开 `src/SensorMonitorExtension` 下的 `Directory.Packages.props`（或 csproj），确认 `Microsoft.CommandPalette.Extensions` ≥ `0.9.260303001`；不足则升级该 PackageReference。

- [ ] **Step 5: 部署并验证基础扩展**

Visual Studio 打开 `SensorMonitorExtension.sln` → 菜单 `Build` → `Deploy SensorMonitorExtension`（注意必须 **Deploy**，仅 Build 不更新包）。然后在命令面板运行 `Reload`（副标题为 Reload Command Palette Extension），列表底部应出现 `Sensor Monitor` 占位命令。

- [ ] **Step 6: 加一个最小 Dock band 验证 Dock 链路**

在 `SensorMonitorExtensionCommandsProvider.cs` 中覆写：

```csharp
public override ICommandItem[]? GetDockBands()
{
    var page = new SensorMonitorExtensionPage(); // 模板自带的页
    return [new CommandItem(page) { Title = "Sensor Monitor" }];
}
```

重新 Deploy + Reload → PowerToys 设置里把本扩展的 band 添加到 Dock → 屏幕边缘 Dock 上应出现 `Sensor Monitor` 按钮。

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: scaffold CmdPal extension with minimal dock band (spike verified)"
```

---

## Phase 1 — SensorMonitor.Host（传感器数据源）

### Task 2: Host 项目骨架 + SensorSnapshot 模型

**Files:**
- Create: `SensorMonitor.sln`（把 Host/Tests/Extension 收进同一 sln）
- Create: `src/SensorMonitor.Host/SensorMonitor.Host.csproj`
- Create: `src/SensorMonitor.Host/Model/SensorSnapshot.cs`
- Test: `tests/SensorMonitor.Host.Tests/SensorSnapshotTests.cs`

- [ ] **Step 1: 建项目与解决方案**

```bash
cd /d/Workspace/SensorMonitor
dotnet new sln -n SensorMonitor
dotnet new console -n SensorMonitor.Host -o src/SensorMonitor.Host -f net8.0
dotnet new xunit -n SensorMonitor.Host.Tests -o tests/SensorMonitor.Host.Tests -f net8.0
dotnet sln add src/SensorMonitor.Host tests/SensorMonitor.Host.Tests
dotnet add tests/SensorMonitor.Host.Tests reference src/SensorMonitor.Host
dotnet add src/SensorMonitor.Host package LibreHardwareMonitorLib
```

注：Extension 项目因 MSIX 打包属性特殊，可留在自己的 sln 里，也可加入本 sln —— 加入后统一用 VS 打开即可，dotnet CLI 只构建 Host/Tests。

- [ ] **Step 2: 写失败测试（JSON round-trip）**

`tests/SensorMonitor.Host.Tests/SensorSnapshotTests.cs`:

```csharp
using System.Text.Json;
using SensorMonitor.Host.Model;
using Xunit;

public class SensorSnapshotTests
{
    [Fact]
    public void Snapshot_RoundTrips_Through_Json()
    {
        var snapshot = new SensorSnapshot(
            DateTimeOffset.Parse("2026-07-18T10:00:00+08:00"),
            [new SensorReading("/intelcpu/0/temperature/8", "CPU", "CPU Package", "Temperature", 65.5f, "°C")]);

        var json = JsonSerializer.Serialize(snapshot);
        var back = JsonSerializer.Deserialize<SensorSnapshot>(json);

        Assert.NotNull(back);
        Assert.Equal(snapshot.Timestamp, back!.Timestamp);
        Assert.Equal(snapshot.Sensors[0], back.Sensors[0]);
    }
}
```

- [ ] **Step 3: 跑测试确认编译失败**

Run: `dotnet test tests/SensorMonitor.Host.Tests`
Expected: FAIL —— `SensorSnapshot` 类型不存在。

- [ ] **Step 4: 实现模型**

`src/SensorMonitor.Host/Model/SensorSnapshot.cs`:

```csharp
namespace SensorMonitor.Host.Model;

public sealed record SensorReading(
    string Id,        // LHM 传感器标识符，如 /intelcpu/0/temperature/8
    string Hardware,  // 所属硬件显示名，如 "Intel Core i7-14700K"
    string Name,      // 传感器名，如 "CPU Package"
    string Type,      // SensorType 枚举名：Temperature / Clock / Load / Fan / Power
    float Value,
    string Unit);     // °C / MHz / % / RPM / W

public sealed record SensorSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<SensorReading> Sensors);
```

注意：`record` 的 `IReadOnlyList` 属性默认按引用比较，Step 2 的断言逐元素比较（`Sensors[0]`）规避了这点，不要改成整对象 `Assert.Equal(snapshot, back)`。

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test tests/SensorMonitor.Host.Tests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(host): project skeleton and SensorSnapshot model"
```

### Task 3: 传感器读取（LhmSensorReader + 可单测的 SensorMapper）

**Files:**
- Create: `src/SensorMonitor.Host/Sensors/ISensorReader.cs`
- Create: `src/SensorMonitor.Host/Sensors/SensorMapper.cs`
- Create: `src/SensorMonitor.Host/Sensors/LhmSensorReader.cs`
- Test: `tests/SensorMonitor.Host.Tests/SensorMapperTests.cs`

- [ ] **Step 1: 写失败测试（映射与过滤，纯函数，无硬件依赖）**

`tests/SensorMonitor.Host.Tests/SensorMapperTests.cs`:

```csharp
using LibreHardwareMonitor.Hardware;
using SensorMonitor.Host.Sensors;
using Xunit;

public class SensorMapperTests
{
    [Theory]
    [InlineData(SensorType.Temperature, true, "°C")]
    [InlineData(SensorType.Clock, true, "MHz")]
    [InlineData(SensorType.Load, true, "%")]
    [InlineData(SensorType.Fan, true, "RPM")]
    [InlineData(SensorType.Power, true, "W")]
    [InlineData(SensorType.Data, false, "")]      // 不关心的类型被过滤
    [InlineData(SensorType.SmallData, false, "")]
    public void Relevance_And_Unit(SensorType type, bool relevant, string unit)
    {
        Assert.Equal(relevant, SensorMapper.IsRelevant(type));
        if (relevant)
            Assert.Equal(unit, SensorMapper.UnitOf(type));
    }

    [Fact]
    public void ToReading_Maps_All_Fields()
    {
        var reading = SensorMapper.ToReading(
            id: "/gpu-nvidia/0/temperature/0", hardwareName: "NVIDIA GeForce RTX 4080",
            sensorName: "GPU Core", type: SensorType.Temperature, value: 61.0f);

        Assert.Equal("/gpu-nvidia/0/temperature/0", reading.Id);
        Assert.Equal("NVIDIA GeForce RTX 4080", reading.Hardware);
        Assert.Equal("GPU Core", reading.Name);
        Assert.Equal("Temperature", reading.Type);
        Assert.Equal(61.0f, reading.Value);
        Assert.Equal("°C", reading.Unit);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SensorMonitor.Host.Tests --filter SensorMapperTests`
Expected: FAIL —— `SensorMapper` 不存在。

- [ ] **Step 3: 实现 SensorMapper 与 ISensorReader**

`src/SensorMonitor.Host/Sensors/ISensorReader.cs`:

```csharp
using SensorMonitor.Host.Model;

namespace SensorMonitor.Host.Sensors;

public interface ISensorReader : IDisposable
{
    SensorSnapshot Read();
}
```

`src/SensorMonitor.Host/Sensors/SensorMapper.cs`:

```csharp
using LibreHardwareMonitor.Hardware;
using SensorMonitor.Host.Model;

namespace SensorMonitor.Host.Sensors;

public static class SensorMapper
{
    public static bool IsRelevant(SensorType type) => type is
        SensorType.Temperature or SensorType.Clock or SensorType.Load
        or SensorType.Fan or SensorType.Power;

    public static string UnitOf(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Clock => "MHz",
        SensorType.Load => "%",
        SensorType.Fan => "RPM",
        SensorType.Power => "W",
        _ => "",
    };

    public static SensorReading ToReading(
        string id, string hardwareName, string sensorName, SensorType type, float value)
        => new(id, hardwareName, sensorName, type.ToString(), value, UnitOf(type));
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/SensorMonitor.Host.Tests --filter SensorMapperTests`
Expected: PASS

- [ ] **Step 5: 实现 LhmSensorReader（硬件依赖集中在此，不写单测，靠 Step 6 手动冒烟）**

`src/SensorMonitor.Host/Sensors/LhmSensorReader.cs`:

```csharp
using LibreHardwareMonitor.Hardware;
using SensorMonitor.Host.Model;

namespace SensorMonitor.Host.Sensors;

public sealed class LhmSensorReader : ISensorReader
{
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    private readonly Computer _computer;

    public LhmSensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
        };
        _computer.Open();
    }

    public SensorSnapshot Read()
    {
        _computer.Accept(new UpdateVisitor());
        var readings = new List<SensorReading>();
        foreach (var hw in _computer.Hardware)
            Collect(hw, readings);
        return new SensorSnapshot(DateTimeOffset.Now, readings);
    }

    private static void Collect(IHardware hw, List<SensorReading> into)
    {
        foreach (var sensor in hw.Sensors)
        {
            if (SensorMapper.IsRelevant(sensor.SensorType) && sensor.Value is float v)
                into.Add(SensorMapper.ToReading(
                    sensor.Identifier.ToString(), hw.Name, sensor.Name, sensor.SensorType, v));
        }
        foreach (var sub in hw.SubHardware)
            Collect(sub, into);
    }

    public void Dispose() => _computer.Close();
}
```

- [ ] **Step 6: 手动冒烟（需管理员终端）**

给 `Program.cs` 临时加 `--dump` 分支（Task 5 会重写成正式版）：

```csharp
using System.Text.Json;
using SensorMonitor.Host.Sensors;

if (args is ["--dump"])
{
    using var reader = new LhmSensorReader();
    Console.WriteLine(JsonSerializer.Serialize(reader.Read(),
        new JsonSerializerOptions { WriteIndented = true }));
    return;
}
```

以**管理员**身份打开终端运行：
Run: `dotnet run --project src/SensorMonitor.Host -- --dump`
Expected: 打印 JSON，其中至少能看到 CPU 的 Clock 传感器与 GPU 的 Temperature 传感器。若 CPU 温度/主板温度缺失 → 查阅 `docs/references/sensor-sources.md` 的 PawnIO 一节（新版 LHM 需要 PawnIO 驱动读 MSR/SuperIO）。**把本机实测可见的传感器清单记录到该文档末尾**，Task 7 选默认传感器时要用。

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(host): LHM sensor reading with testable mapper"
```

### Task 4: 命名管道 JSON 服务

**Files:**
- Create: `src/SensorMonitor.Host/Ipc/PipeJsonServer.cs`
- Test: `tests/SensorMonitor.Host.Tests/PipeJsonServerTests.cs`

协议（v1，刻意最简）：客户端连接 `SensorMonitor.Host.v1` 管道 → 写一行 `GET` → 服务端回一行 JSON（`SensorSnapshot` 序列化）→ 断开。无版本协商、无订阅推送（YAGNI，轮询场景够用）。

- [ ] **Step 1: 写失败测试（进程内起服务端 + 客户端连通）**

`tests/SensorMonitor.Host.Tests/PipeJsonServerTests.cs`:

```csharp
using System.IO.Pipes;
using System.Text.Json;
using SensorMonitor.Host.Ipc;
using SensorMonitor.Host.Model;
using Xunit;

public class PipeJsonServerTests
{
    [Fact]
    public async Task Get_Returns_Latest_Snapshot_Json()
    {
        var pipeName = $"SensorMonitor.Test.{Guid.NewGuid():N}";
        var snapshot = new SensorSnapshot(DateTimeOffset.UtcNow,
            [new SensorReading("/cpu/0/clock/1", "CPU", "Core #1", "Clock", 5200f, "MHz")]);

        using var server = new PipeJsonServer(pipeName, () => snapshot);
        server.Start();

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await client.ConnectAsync(2000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        using var reader = new StreamReader(client);
        await writer.WriteLineAsync("GET");
        var line = await reader.ReadLineAsync();

        var back = JsonSerializer.Deserialize<SensorSnapshot>(line!);
        Assert.Equal(5200f, back!.Sensors[0].Value);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SensorMonitor.Host.Tests --filter PipeJsonServerTests`
Expected: FAIL —— `PipeJsonServer` 不存在。

- [ ] **Step 3: 实现 PipeJsonServer**

`src/SensorMonitor.Host/Ipc/PipeJsonServer.cs`:

```csharp
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using SensorMonitor.Host.Model;

namespace SensorMonitor.Host.Ipc;

/// <summary>
/// 极简单次问答管道服务：客户端写一行 "GET"，服务端回一行 JSON 快照后断开。
/// ACL 放开 Authenticated Users —— Host 提权运行而扩展不提权，默认 ACL 会拒连。
/// </summary>
public sealed class PipeJsonServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<SensorSnapshot> _snapshotProvider;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public PipeJsonServer(string pipeName, Func<SensorSnapshot> snapshotProvider)
    {
        _pipeName = pipeName;
        _snapshotProvider = snapshotProvider;
    }

    public void Start() => _loop = Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite, AccessControlType.Allow));

            await using var server = NamedPipeServerStreamAcl.Create(
                _pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                inBufferSize: 0, outBufferSize: 0, security);
            try
            {
                await server.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(server, leaveOpen: true);
                await using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                var request = await reader.ReadLineAsync(_cts.Token);
                if (request == "GET")
                    await writer.WriteLineAsync(JsonSerializer.Serialize(_snapshotProvider()));
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { /* 客户端异常断开：忽略，继续接受下一个连接 */ }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _cts.Dispose();
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/SensorMonitor.Host.Tests`
Expected: 全部 PASS（含前两个 Task 的测试）。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(host): named pipe JSON server with cross-IL ACL"
```

### Task 5: Host 主程序（提权、单实例、缓存刷新循环)

**Files:**
- Create: `src/SensorMonitor.Host/app.manifest`
- Modify: `src/SensorMonitor.Host/Program.cs`（替换 Task 3 的临时版）
- Modify: `src/SensorMonitor.Host/SensorMonitor.Host.csproj`

- [ ] **Step 1: 提权清单**

`src/SensorMonitor.Host/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

csproj 的 `<PropertyGroup>` 中加入：

```xml
<ApplicationManifest>app.manifest</ApplicationManifest>
```

- [ ] **Step 2: 主程序**

`src/SensorMonitor.Host/Program.cs`:

```csharp
using SensorMonitor.Host.Ipc;
using SensorMonitor.Host.Model;
using SensorMonitor.Host.Sensors;

const string PipeName = "SensorMonitor.Host.v1";
const int RefreshMs = 2000;

using var singleInstance = new Mutex(initiallyOwned: true, "Global\\SensorMonitor.Host", out var isNew);
if (!isNew)
{
    Console.Error.WriteLine("SensorMonitor.Host 已在运行，退出。");
    return 1;
}

using var reader = new LhmSensorReader();

// 传感器读取较慢（数十 ms 级），缓存快照、后台定时刷新，管道请求只读缓存。
SensorSnapshot cached = reader.Read();
using var refreshTimer = new Timer(_ =>
{
    try { Volatile.Write(ref cached, reader.Read()); }
    catch (Exception ex) { Console.Error.WriteLine($"刷新失败: {ex.Message}"); }
}, null, RefreshMs, RefreshMs);

using var server = new PipeJsonServer(PipeName, () => Volatile.Read(ref cached));
server.Start();

Console.WriteLine($"SensorMonitor.Host 运行中，管道: {PipeName}，刷新间隔: {RefreshMs}ms。Ctrl+C 退出。");
var exit = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
exit.Wait();
return 0;
```

如需保留 `--dump` 调试分支，放在建 Mutex 之前。

- [ ] **Step 3: 手动验证（会弹 UAC）**

```bash
dotnet build src/SensorMonitor.Host
```

资源管理器双击 `src/SensorMonitor.Host/bin/Debug/net8.0/SensorMonitor.Host.exe`（或管理员终端运行）→ 接受 UAC → 控制台显示运行中。再从**普通（非管理员）**终端验证跨完整性级别连通：

```powershell
$c = [System.IO.Pipes.NamedPipeClientStream]::new('.', 'SensorMonitor.Host.v1', 'InOut')
$c.Connect(2000); $w = [System.IO.StreamWriter]::new($c); $w.AutoFlush = $true
$w.WriteLine('GET'); [System.IO.StreamReader]::new($c).ReadLine()
```

Expected: 输出一行 JSON 快照。这是本架构的关键验证点（提权服务端 ←→ 非提权客户端）。

- [ ] **Step 4: 二实例拒绝验证**

保持第一个实例运行，再启动一次 exe。Expected: 第二个实例打印"已在运行"并退出。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(host): elevated single-instance host with cached refresh loop"
```

---

## Phase 2 — Extension Dock 显示

### Task 6: Dock band 骨架（假数据，先验证实时刷新）

**Files:**
- Create: `src/SensorMonitorExtension/Dock/SensorDockBand.cs`
- Modify: `src/SensorMonitorExtension/SensorMonitorExtensionCommandsProvider.cs`

- [ ] **Step 1: 实现自刷新的 band item（先用假数据）**

`src/SensorMonitorExtension/Dock/SensorDockBand.cs`:

```csharp
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace SensorMonitorExtension.Dock;

/// <summary>
/// Dock 上的一个传感器显示项。仿官方 Time & Date 扩展的 NowDockBand 模式：
/// ListItem 定时更新 Title，Dock 随属性变更通知自动重绘。
/// </summary>
internal sealed partial class SensorDockBand : ListItem, IDisposable
{
    private readonly Timer _timer;
    private int _tick;

    public SensorDockBand() : base(new NoOpCommand() { Id = "com.sensormonitor.dock.readings" })
    {
        Title = "CPU --°C";
        _timer = new Timer(_ => Refresh(), null, 0, 2000);
    }

    private void Refresh()
    {
        // Task 7 将替换为真实管道数据；此处先验证 Dock 会随 Title 变更刷新。
        _tick++;
        Title = $"CPU {60 + _tick % 10}°C";
        Subtitle = $"GPU {50 + _tick % 8}°C";
    }

    public void Dispose() => _timer.Dispose();
}
```

注意：Dock 要求每个 item 的 `Command` 有非空 `Id`，否则被静默忽略（见 `docs/references/cmdpal-extension.md`）。若 Toolkit 无 `NoOpCommand`，用模板中任一现成 command 替代并设 Id，实现时以 SDK 实际 API 为准。

- [ ] **Step 2: Provider 挂接**

`SensorMonitorExtensionCommandsProvider.cs` 中（替换 Task 1 Step 6 的临时实现）：

```csharp
private readonly SensorDockBand _band = new();

public override ICommandItem[]? GetDockBands()
{
    return [new WrappedDockItem([_band], "com.sensormonitor.dock", "Sensor Monitor")];
}
```

- [ ] **Step 3: 部署验证实时刷新**

Deploy → Reload → Dock 添加本扩展 band。
Expected: Dock 上数字每 2 秒变化。**这是 Dock 链路最关键的验证点**：若 Title 变更不触发重绘，去查 Toolkit `ListItem` 属性通知机制（`OnPropertyChanged`），必要时换成官方 Time & Date 扩展源码同款写法（PowerToys 仓库 `src/modules/cmdpal/ext/` 下有源码可对照）。

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(ext): self-refreshing dock band with fake data"
```

### Task 7: 接入真实数据（管道客户端）

**Files:**
- Create: `src/SensorMonitorExtension/Ipc/SensorSnapshot.cs`（Host 侧 DTO 的拷贝）
- Create: `src/SensorMonitorExtension/Ipc/PipeSensorClient.cs`
- Modify: `src/SensorMonitorExtension/Dock/SensorDockBand.cs`

- [ ] **Step 1: DTO 拷贝**

`src/SensorMonitorExtension/Ipc/SensorSnapshot.cs` —— 内容与 Host 的 `Model/SensorSnapshot.cs` 相同，仅命名空间改为 `SensorMonitorExtension.Ipc`。MVP 接受两份拷贝（协议 v1 字段稳定后再考虑共享项目；MSIX 项目引用普通 classlib 有打包坑，不值得现在处理）。

- [ ] **Step 2: 管道客户端**

`src/SensorMonitorExtension/Ipc/PipeSensorClient.cs`:

```csharp
using System.IO.Pipes;
using System.Text.Json;

namespace SensorMonitorExtension.Ipc;

internal static class PipeSensorClient
{
    private const string PipeName = "SensorMonitor.Host.v1";

    /// <summary>连接 Host 取一次快照；Host 未运行/超时返回 null。</summary>
    public static SensorSnapshot? TryFetch(int timeoutMs = 500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            using var reader = new StreamReader(client);
            writer.WriteLine("GET");
            var line = reader.ReadLine();
            return line is null ? null : JsonSerializer.Deserialize<SensorSnapshot>(line);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 3: band 改用真实数据**

`SensorDockBand.Refresh()` 替换为：

```csharp
private void Refresh()
{
    var snapshot = PipeSensorClient.TryFetch();
    if (snapshot is null)
    {
        Title = "传感器: Host 未运行";
        Subtitle = "";
        return;
    }
    Title = FormatLine(snapshot);
}

/// <summary>
/// MVP 默认三项：CPU 频率取 Clock 中含 "Core" 的最大值；GPU 温度取 GPU 硬件的
/// "GPU Core" Temperature；主板温度取 Motherboard 硬件的首个 Temperature。
/// 具体传感器名以 Task 3 Step 6 记录的本机实测清单为准，在此调整匹配规则。
/// </summary>
private static string FormatLine(SensorSnapshot s)
{
    static string Fmt(float? v, string unit) => v is null ? "--" : $"{v:F0}{unit}";

    float? cpuClock = s.Sensors
        .Where(r => r.Type == "Clock" && r.Name.Contains("Core"))
        .Select(r => (float?)r.Value).Max();
    float? gpuTemp = s.Sensors
        .FirstOrDefault(r => r.Type == "Temperature" && r.Name == "GPU Core")?.Value;
    float? moboTemp = s.Sensors
        .FirstOrDefault(r => r.Type == "Temperature" && r.Id.StartsWith("/lpc/"))?.Value;
        // 主板 SuperIO 传感器的 Id 以 /lpc/ 开头（LHM 约定）；
        // 若本机实测清单（sensor-sources.md 末尾）显示前缀不同，改为按清单精确匹配。

    return $"CPU {Fmt(cpuClock, "MHz")} · GPU {Fmt(gpuTemp, "°C")} · 主板 {Fmt(moboTemp, "°C")}";
}
```

- [ ] **Step 4: 端到端验证**

先手动启动 Host（UAC），再 Deploy + Reload 扩展。
Expected: Dock 显示真实读数且每 2 秒刷新；关掉 Host 后 ≤2 秒内变为"Host 未运行"。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(ext): dock band shows live sensor data via named pipe"
```

---

## Phase 3 — Host 随扩展拉起

### Task 8: 自动拉起 Host（"随这个扩展一起打开"）

**Files:**
- Create: `src/SensorMonitorExtension/Commands/LaunchHostCommand.cs`
- Modify: `src/SensorMonitorExtension/Dock/SensorDockBand.cs`

- [ ] **Step 1: 拉起命令**

`src/SensorMonitorExtension/Commands/LaunchHostCommand.cs`:

```csharp
using System.Diagnostics;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace SensorMonitorExtension.Commands;

internal sealed partial class LaunchHostCommand : InvokableCommand
{
    public LaunchHostCommand()
    {
        Name = "启动传感器 Host";
        Id = "com.sensormonitor.launchhost";
    }

    /// <summary>
    /// 开发期用环境变量指向仓库构建产物；打包后回退到包内 Host 目录（后续路线 R4 落地）。
    /// </summary>
    internal static string ResolveHostPath() =>
        Environment.GetEnvironmentVariable("SENSORMONITOR_HOST_EXE")
        ?? Path.Combine(AppContext.BaseDirectory, "Host", "SensorMonitor.Host.exe");

    public static bool TryLaunch()
    {
        var path = ResolveHostPath();
        if (!File.Exists(path)) return false;
        try
        {
            // Host 的 manifest 要求管理员，UseShellExecute 触发 UAC；用户拒绝则抛 Win32Exception。
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public override CommandResult Invoke()
    {
        TryLaunch();
        return CommandResult.KeepOpen();
    }
}
```

- [ ] **Step 2: band 接入自动拉起**

`SensorDockBand` 构造中把 `NoOpCommand` 换成 `LaunchHostCommand`（点击 band 项即可手动拉起）；并在 Refresh 中加一次性自动拉起：

```csharp
private bool _autoLaunchAttempted;

private void Refresh()
{
    var snapshot = PipeSensorClient.TryFetch();
    if (snapshot is null)
    {
        if (!_autoLaunchAttempted)
        {
            _autoLaunchAttempted = true;   // 只自动尝试一次，避免用户拒绝 UAC 后被反复骚扰
            Commands.LaunchHostCommand.TryLaunch();
        }
        Title = "传感器: Host 未运行（点击启动）";
        Subtitle = "";
        return;
    }
    Title = FormatLine(snapshot);
}
```

- [ ] **Step 3: 端到端验证**

关闭 Host → 设置环境变量 `SENSORMONITOR_HOST_EXE` 指向 Host 构建产物（**注意**：需在系统级设置或 CmdPal 进程能继承处设置，用户级 setx 后需重启 PowerToys）→ Reload 扩展。
Expected: 弹出 UAC → 接受 → Dock 数秒内出现真实读数。拒绝 UAC → band 停在"点击启动"，点击可再次触发。

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(ext): auto-launch elevated host on demand"
```

- [ ] **Step 5: MVP 收尾自检**

对照本文件头部 Goal 逐项确认：Dock 常驻显示 ✓ / CPU 频率、主板温度、GPU 温度 ✓ / 开源数据源（LHM, MPL-2.0）✓ / 随扩展一起打开 ✓。运行 `dotnet test` 全绿后，按 superpowers:finishing-a-development-branch 处理分支。

---

## 后续路线（明确不在 MVP 内，勿提前实现）

| # | 事项 | 备注 |
|---|------|------|
| R1 | 面板内完整传感器浏览页（ListPage 按硬件分组） | 扩展已有 Page 骨架，加载 snapshot 渲染即可 |
| R2 | 传感器选择 + 刷新间隔设置 | CmdPal 设置页 / JSON 配置文件 |
| R3 | 计划任务提权（登录时最高权限静默启动 Host，免每次 UAC） | `schtasks /create /rl HIGHEST`；含 opt-in UI |
| R4 | Host 打进 MSIX 包（`Assets/Host/`）随扩展分发 | 解决 `ResolveHostPath` 的打包回退分支 |
| R5 | PawnIO 缺失检测与安装引导 | 见 sensor-sources.md；影响 CPU 温度/主板温度可见性 |
| R6 | 图标、多 band（每个指标一个按钮）、°C 阈值变色 | `WrappedDockItem` 多 ListItem 形态 |
