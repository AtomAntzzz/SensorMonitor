# Post-MVP 加固与体验优化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 MVP 复查发现的 4 类缺陷（管道可被挂死、后台循环静默死亡、扩展进程可被 Timer 异常带崩、Host 可见窗口/UAC 骚扰），并落地免 UAC 静默拉起与传感器浏览页。

**Architecture:** 不改双进程 + 命名管道的既有架构（`docs/architecture.md` D1–D6）。新增两条决策：**D7 自动拉起绝不弹 UAC**（自动路径只走计划任务静默通道，UAC 只发生在用户显式点击）；**D8 Host 无窗口化 + 文件日志**（`%ProgramData%\SensorMonitor\host.log`）。

**Tech Stack:** 同 MVP。新依赖仅 Windows 自带 `schtasks`（计划任务注册/触发）。

**缺陷复查结论（2026-07-18，对应任务标注在括号里）:**

| # | 严重度 | 缺陷 | 修复 |
|---|--------|------|------|
| F1 | 中 | 管道服务端串行处理且无单连接超时：一个连上后不发请求/不关闭的客户端会**永久阻塞**所有后续请求 | Task 1 |
| F2 | 中 | Accept 循环遇到未预期异常（非 IOException/OCE）会静默死亡（`Task.Run` 未观察）→ Host 进程活着但永不供数 | Task 1 |
| F3 | 中 | 扩展 `Refresh` 在 Timer 线程执行，任何未防护异常直接崩掉扩展进程；`{"Sensors":null}` 即触发 NRE | Task 4 |
| F4 | 中·UX | Host 是可见控制台窗口，常驻任务栏；出错信息随窗口关闭丢失 | Task 3 |
| F5 | 中·UX | 扩展构造即启动轮询并自动 UAC 拉起 → **每次登录弹一次 UAC**，band 没上 Dock 也弹 | Task 5+6 |
| F6 | 低 | Host 刷新用周期 Timer，`Read()` 超过 2s 时回调重入（LHM 非线程安全） | Task 2 |
| F7 | 低 | Host 刷新持续失败时扩展永远显示冻结旧值，无过期提示（Timestamp 未被利用） | Task 4 |
| F8 | 低 | 开发循环：Host 运行时锁死 `bin/`，无法重建/跑测试（已实测撞上） | Task 8（文档化 stop 命令） |
| F9 | 信息 | 管道名可被本地恶意进程抢注伪造数据 —— 数据只读且非敏感，接受不修 | — |
| F10 | 碎 | 面板页仍是模板 TODO；`docs/staged-extension/` 与 src 双份必漂移 | Task 7 / Task 8 |

---

## Phase A — 缺陷修复

### Task 1: 管道服务端加固（单连接超时 + 循环永不死亡）（修 F1/F2）

**Files:**
- Modify: `src/SensorMonitor.Host/Ipc/PipeJsonServer.cs`
- Modify: `src/SensorMonitor.Host/Program.cs`（构造参数变化）
- Test: `tests/SensorMonitor.Host.Tests/PipeJsonServerTests.cs`

- [ ] **Step 1: 写失败测试（挂死客户端不阻塞下一个客户端）**

在 `PipeJsonServerTests.cs` 追加：

```csharp
[Fact]
public async Task Wedged_Client_Does_Not_Block_Next_Client()
{
    var pipeName = $"SensorMonitor.Test.{Guid.NewGuid():N}";
    var snapshot = new SensorSnapshot(DateTimeOffset.UtcNow,
        [new SensorReading("/cpu/0/clock/1", "CPU", "Core #1", "Clock", 5200f, "MHz")]);
    using var server = new PipeJsonServer(pipeName, () => snapshot,
        connectionTimeout: TimeSpan.FromMilliseconds(300));
    server.Start();

    // 挂死客户端：连上后既不发请求也不关闭。
    await using var wedged = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
    await wedged.ConnectAsync(2000);

    // 服务端应在 300ms 超时后丢弃它，继续服务下一个客户端。
    await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
    await client.ConnectAsync(5000);
    using var reader = new StreamReader(client);
    await using var writer = new StreamWriter(client) { AutoFlush = true };
    await writer.WriteLineAsync("GET");
    var readTask = reader.ReadLineAsync();
    var done = await Task.WhenAny(readTask, Task.Delay(5000));
    Assert.Same(readTask, done);            // 5s 内拿到响应，未被挂死客户端阻塞
    Assert.Contains("5200", await readTask);
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/SensorMonitor.Host.Tests --filter Wedged_Client_Does_Not_Block_Next_Client`
Expected: 编译失败（`PipeJsonServer` 尚无 `connectionTimeout` 参数）。
注意（F8）：跑测试前先停掉运行中的 Host —— 管理员终端 `taskkill /f /im SensorMonitor.Host.exe`，否则 bin 被锁构建失败。

- [ ] **Step 3: 实现 —— 拆出 ServeOneAsync、加单连接超时与兜底 catch**

`PipeJsonServer.cs` 整体替换为：

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
/// 单连接有独立超时：挂死客户端只浪费一个超时周期，不会阻塞后续请求（F1）。
/// </summary>
public sealed class PipeJsonServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<SensorSnapshot> _snapshotProvider;
    private readonly TimeSpan _connectionTimeout;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public PipeJsonServer(string pipeName, Func<SensorSnapshot> snapshotProvider,
        TimeSpan? connectionTimeout = null, Action<string>? log = null)
    {
        _pipeName = pipeName;
        _snapshotProvider = snapshotProvider;
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(5);
        _log = log ?? (_ => { });
    }

    public void Start() => _loop = Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await ServeOneAsync();
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // 单个连接的任何异常都不允许杀死接受循环（F2：否则 Host 活着但永不供数）。
                _log($"管道连接处理失败: {ex}");
            }
        }
    }

    private async Task ServeOneAsync()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        await using var server = NamedPipeServerStreamAcl.Create(
            _pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            inBufferSize: 0, outBufferSize: 0, security);

        // 等连接只受整体停机控制；连接建立后才开始计单连接超时。
        await server.WaitForConnectionAsync(_cts.Token);

        using var connCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        connCts.CancelAfter(_connectionTimeout);
        try
        {
            using var reader = new StreamReader(server, leaveOpen: true);
            await using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            var request = await reader.ReadLineAsync(connCts.Token);
            if (request == "GET")
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(_snapshotProvider()));
                // 等客户端读完并主动断开（EOF）后再关管道，避免客户端 flush 撞已关闭管道
                //（MVP Task 4 引入的收尾握手，保持不变）。
                await reader.ReadLineAsync(connCts.Token);
            }
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            // 单连接超时（挂死/不发请求/不关连接的客户端）：丢弃，循环继续（F1）。
        }
        catch (IOException) { /* 客户端异常断开：忽略 */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _cts.Dispose();
    }
}
```

`Program.cs` 中构造处暂改为（Task 3 会把 log 换成文件日志）：

```csharp
using var server = new PipeJsonServer(PipeName, () => Volatile.Read(ref cached),
    log: Console.Error.WriteLine);
```

- [ ] **Step 4: 跑全部测试确认通过**

Run: `dotnet test tests/SensorMonitor.Host.Tests`
Expected: 全绿（旧 10 个 + 新 1 个）。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "fix(host): per-connection timeout and unkillable accept loop (F1,F2)"
```

### Task 2: Host 刷新循环防重入（修 F6）

**Files:**
- Modify: `src/SensorMonitor.Host/Program.cs`

- [ ] **Step 1: 周期 Timer 改 one-shot 重排**

`Program.cs` 中刷新 Timer 段替换为：

```csharp
// one-shot 重排：上一轮 Read() 结束才排下一轮，杜绝重入（LHM 非线程安全，F6）。
Timer refreshTimer = null!;
refreshTimer = new Timer(_ =>
{
    try { Volatile.Write(ref cached, reader.Read()); }
    catch (Exception ex) { Console.Error.WriteLine($"刷新失败: {ex}"); }
    finally
    {
        try { refreshTimer.Change(RefreshMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { /* 停机竞态：忽略 */ }
    }
}, null, RefreshMs, Timeout.Infinite);
using var _ = refreshTimer;
```

- [ ] **Step 2: 手动验证**

管理员终端启动 Host，普通终端用 MVP Task 5 Step 3 的 PowerShell 片段连管道取两次（间隔 >2s），确认两次 `Timestamp` 不同（刷新仍在跑）。

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "fix(host): one-shot reschedule prevents refresh reentrancy (F6)"
```

### Task 3: Host 无窗口化 + 文件日志（修 F4，决策 D8）

**Files:**
- Modify: `src/SensorMonitor.Host/SensorMonitor.Host.csproj`（`OutputType` → `WinExe`）
- Create: `src/SensorMonitor.Host/HostLog.cs`
- Modify: `src/SensorMonitor.Host/Program.cs`

- [ ] **Step 1: csproj 切 WinExe**

```xml
<OutputType>WinExe</OutputType>
```

- [ ] **Step 2: 文件日志**

`src/SensorMonitor.Host/HostLog.cs`:

```csharp
namespace SensorMonitor.Host;

/// <summary>
/// WinExe 无控制台（D8），错误全部落 %ProgramData%\SensorMonitor\host.log。
/// 超 1MB 直接重开新文件 —— 这是错误日志不是运行日志，正常运行几乎不增长。
/// </summary>
internal static class HostLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SensorMonitor", "host.log");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                    File.Delete(LogPath);
                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

- [ ] **Step 3: Program.cs 接日志 + `--dump` 挂回父控制台**

WinExe 下 stdout 默认无处可去；`--dump` 分支前贴附父进程控制台，保持调试体验：

```csharp
using System.Runtime.InteropServices;

[DllImport("kernel32.dll")]
static extern bool AttachConsole(int dwProcessId);

if (args is ["--dump"])
{
    AttachConsole(-1); // -1 = ATTACH_PARENT_PROCESS；从终端启动时输出回到该终端
    using var dumpReader = new LhmSensorReader();
    Console.WriteLine(JsonSerializer.Serialize(dumpReader.Read(),
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
```

其余 `Console.Error.WriteLine(...)` 全部替换为 `HostLog.Write(...)`（含 Task 1 的 `log:` 参数与 Task 2 的刷新失败分支、单实例退出提示）。末尾的 `Console.CancelKeyPress` 保留无害（无控制台时不会触发）；启动成功后写一条 `HostLog.Write("Host 启动");`。

注：顶层语句中 `[DllImport]` 需放本地函数形式，若编译器拒绝，把它挪进 `HostLog.cs` 旁新建的 `internal static class NativeConsole` 中。

- [ ] **Step 4: 手动验证**

重新构建，双击 exe → UAC → **无窗口**；任务管理器可见进程；普通终端连管道取到 JSON；`%ProgramData%\SensorMonitor\host.log` 出现"Host 启动"。管理员终端 `SensorMonitor.Host.exe --dump` 仍在终端打印 JSON。

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "fix(host): windowless WinExe with file logging (F4, D8)"
```

### Task 4: 扩展防崩 + 数据过期提示（修 F3/F7）

**Files:**
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/Dock/SensorDockBand.cs`

- [ ] **Step 1: Refresh 包裹兜底 + Sensors null 容忍 + 过期标注**

`SensorDockBand` 的 `Refresh` 拆为两层：

```csharp
private void Refresh()
{
    try
    {
        RefreshCore();
    }
    catch (Exception ex)
    {
        // Timer 线程的未处理异常会带崩整个扩展进程（F3）——宁可显示错误也不崩。
        Title = "传感器: 内部错误";
        Subtitle = ex.GetType().Name;
    }
}

private void RefreshCore()
{
    var snapshot = PipeSensorClient.TryFetch();
    if (snapshot?.Sensors is null)   // Sensors==null 的畸形 JSON 一并按未运行处理（F3）
    {
        if (!_autoLaunchAttempted)
        {
            _autoLaunchAttempted = true;
            Commands.LaunchHostCommand.TryLaunch();
        }
        Title = "传感器: Host 未运行（点击启动）";
        Subtitle = "";
        return;
    }
    Title = FormatLine(snapshot);
    // Host 刷新循环若持续失败，快照会冻结在旧值 —— 用 Timestamp 显式标注（F7）。
    var age = DateTimeOffset.Now - snapshot.Timestamp;
    Subtitle = age > TimeSpan.FromSeconds(10) ? $"⚠ 数据已 {age.TotalSeconds:F0}s 未更新" : "";
}
```

- [ ] **Step 2: 部署验证**

VS Deploy → CmdPal `Reload`。三个场景：① 正常显示读数、Subtitle 空；② 管理员终端挂起 Host 进程（资源监视器暂停或调试器 attach 后 break）>10s → Subtitle 出现"未更新"；③ 杀掉 Host → band 回到"Host 未运行"。

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "fix(ext): crash-proof refresh and stale-data indicator (F3,F7)"
```

---

## Phase B — 体验优化

### Task 5: 计划任务静默提权通道（修 F5 前半，决策 D7；原路线 R3）

**Files:**
- Create: `src/SensorMonitor.Host/TaskInstaller.cs`
- Modify: `src/SensorMonitor.Host/Program.cs`
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/Commands/LaunchHostCommand.cs`

原理：以最高权限注册计划任务需要提权一次（Host 本来就带 UAC），但**触发**自己名下 `/RL HIGHEST` 的任务不需要提权 —— 这是自有软件免重复 UAC 的标准做法。

- [ ] **Step 1: Host 侧注册/注销命令**

`src/SensorMonitor.Host/TaskInstaller.cs`:

```csharp
using System.Diagnostics;

namespace SensorMonitor.Host;

internal static class TaskInstaller
{
    public const string TaskName = "SensorMonitor.Host";

    /// <summary>注册登录自启 + 可按需触发的最高权限任务（本进程已提权，schtasks 直接成功）。</summary>
    public static int Install()
    {
        var exe = Environment.ProcessPath!;
        return Run($"/Create /TN {TaskName} /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F");
    }

    public static int Uninstall() => Run($"/Delete /TN {TaskName} /F");

    private static int Run(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks", args)
        { UseShellExecute = false, CreateNoWindow = true })!;
        p.WaitForExit();
        HostLog.Write($"schtasks {args} → {p.ExitCode}");
        return p.ExitCode;
    }
}
```

`Program.cs` 的 `--dump` 分支后追加：

```csharp
if (args is ["--install-task"]) return TaskInstaller.Install();
if (args is ["--uninstall-task"]) return TaskInstaller.Uninstall();
```

- [ ] **Step 2: 扩展侧优先走计划任务**

`LaunchHostCommand.cs` 的 `TryLaunch` 改为：

```csharp
public static bool TryLaunch()
{
    // D7：优先静默通道 —— 触发已注册的最高权限计划任务，无 UAC。
    if (TryRunScheduledTask()) return true;
    // 回退：直接拉起 exe，弹 UAC（仅用户显式点击时可接受）。
    var path = ResolveHostPath();
    if (!File.Exists(path)) return false;
    try
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return true;
    }
    catch (System.ComponentModel.Win32Exception) { return false; }
}

private static bool TryRunScheduledTask()
{
    try
    {
        using var p = Process.Start(new ProcessStartInfo(
            "schtasks", "/Run /TN SensorMonitor.Host")
        { UseShellExecute = false, CreateNoWindow = true });
        if (p is null) return false;
        return p.WaitForExit(3000) && p.ExitCode == 0;  // 任务不存在 → 非 0 → 走回退
    }
    catch (System.ComponentModel.Win32Exception) { return false; }
}
```

- [ ] **Step 3: 验证**

管理员终端 `SensorMonitor.Host.exe --install-task`（exit 0，`schtasks /Query /TN SensorMonitor.Host` 可见）→ 杀掉 Host → 普通终端 `schtasks /Run /TN SensorMonitor.Host` → **无 UAC**、Host 进程出现。再验证扩展：杀 Host → Dock band 点击 → 无 UAC 恢复读数。注销一次再点击 → 弹 UAC（回退路径正常）→ 重新 `--install-task`。

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: scheduled-task silent elevation channel (R3, D7)"
```

### Task 6: 自动拉起策略收紧 + band 懒启动（修 F5 后半）

**Files:**
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/Dock/SensorDockBand.cs`
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/SensorMonitorExtensionCommandsProvider.cs`
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/Commands/LaunchHostCommand.cs`

- [ ] **Step 1: 自动路径只走静默通道**

`LaunchHostCommand` 拆出仅静默的入口（自动重连用），把 `TryRunScheduledTask` 从 `private` 改为：

```csharp
/// <summary>D7：自动场景专用 —— 只走计划任务静默通道，绝不弹 UAC。</summary>
internal static bool TryLaunchSilent() => TryRunScheduledTask();
```

`SensorDockBand.RefreshCore` 中自动拉起处 `Commands.LaunchHostCommand.TryLaunch()` 改为 `Commands.LaunchHostCommand.TryLaunchSilent()`。同时删掉 `_autoLaunchAttempted` 的"只试一次"限制、换成节流（静默通道无骚扰，可持续重试）：

```csharp
private DateTimeOffset _lastAutoLaunch = DateTimeOffset.MinValue;

// RefreshCore 的未运行分支：
if (DateTimeOffset.Now - _lastAutoLaunch > TimeSpan.FromSeconds(30))
{
    _lastAutoLaunch = DateTimeOffset.Now;
    Commands.LaunchHostCommand.TryLaunchSilent();  // 未注册任务时静默失败，不弹窗
}
Title = "传感器: Host 未运行（点击启动）";
```

- [ ] **Step 2: band 懒启动 —— 不进 Dock 流程就不轮询**

`SensorDockBand` 构造函数不再建 Timer，改为：

```csharp
private Timer? _timer;
private readonly object _gate = new();

public void EnsureStarted()
{
    lock (_gate)
        _timer ??= new Timer(_ => Refresh(), null, 0, 2000);
}

public void Dispose() => _timer?.Dispose();
```

Provider 的 `GetDockBands()` 首行调 `_band.EnsureStarted();`。

⚠️ 实现时验证：若实测发现 CmdPal 在 band 未被添加到 Dock 时也调用 `GetDockBands()`（如设置页枚举），懒启动的收益只剩"打开过设置才轮询"—— 可接受；因 Step 1 已保证自动路径无 UAC，F5 的骚扰本体已消除，此步只省资源。

- [ ] **Step 3: 部署验证**

Deploy + Reload：① 未注册计划任务、Host 未运行 → band 显示"未运行"，**全程无 UAC**；② 注册任务后 → band 30s 内自动恢复读数；③ 点击 band → 立即恢复。

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(ext): silent-only auto-launch with throttle, lazy band polling (F5)"
```

### Task 7: 传感器浏览页替换模板 TODO 页（原路线 R1 + R5 提示）

**Files:**
- Modify: `src/SensorMonitorExtension/SensorMonitorExtension/Pages/SensorMonitorExtensionPage.cs`

- [ ] **Step 1: 实现页面**

整体替换为：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension;

internal sealed partial class SensorMonitorExtensionPage : ListPage
{
    public SensorMonitorExtensionPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Sensor Monitor";
        Name = "Open";
    }

    public override IListItem[] GetItems()
    {
        var snapshot = PipeSensorClient.TryFetch(timeoutMs: 1500);
        if (snapshot?.Sensors is null)
        {
            return [new ListItem(new Commands.LaunchHostCommand())
                { Title = "Host 未运行", Subtitle = "回车启动传感器 Host" }];
        }

        var items = new List<IListItem>();

        // R5：无任何 CPU 传感器 ⇒ PawnIO 未装（见 sensor-sources.md 实测），给安装引导。
        bool cpuVisible = snapshot.Sensors.Any(r =>
            r.Id.StartsWith("/intelcpu") || r.Id.StartsWith("/amdcpu"));
        if (!cpuVisible)
        {
            items.Add(new ListItem(new OpenUrlCommand("https://pawnio.eu/"))
            {
                Title = "⚠ CPU 传感器不可见",
                Subtitle = "需安装 PawnIO 驱动（回车打开官网），安装后重启 Host",
            });
        }

        items.AddRange(snapshot.Sensors
            .OrderBy(r => r.Hardware).ThenBy(r => r.Type).ThenBy(r => r.Id)
            .Select(r => (IListItem)new ListItem(new NoOpCommand())
            {
                Title = $"{r.Name}: {r.Value:F1} {r.Unit}",
                Subtitle = $"{r.Hardware} · {r.Type}",
            }));
        return [.. items];
    }
}
```

- [ ] **Step 2: 部署验证**

Deploy + Reload → 面板打开 `Sensor Monitor`：按硬件分组有序列出全部传感器（本机装 PawnIO 后 135 项）；杀掉 Host 重开页面 → 只剩"Host 未运行"项，回车可拉起。

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(ext): sensor browser page with PawnIO guidance (R1,R5)"
```

### Task 8: 清理与文档收口（修 F8/F10 残留）

**Files:**
- Delete: `docs/staged-extension/`（已并入 src，双份必漂移）
- Modify: `CLAUDE.md`（状态改"MVP+加固完成"；常用命令补 `taskkill /f /im SensorMonitor.Host.exe`（管理员）与 `--install-task`；高频坑补"Host 运行时锁 bin，重建前先停"）
- Modify: `docs/architecture.md`（追加 D7 静默拉起、D8 无窗口+日志，含依据）
- Modify: `docs/plans/2026-07-18-sensormonitor-mvp.md`（顶部加一行"已完成，后续见 post-mvp-hardening 计划"）

- [ ] **Step 1: 按上述清单逐项修改**
- [ ] **Step 2: 全量回归**

Run: `dotnet test tests/SensorMonitor.Host.Tests`（记得先 taskkill Host）
Expected: 全绿。再手动过一遍：登录 → Host 自启（计划任务）→ Dock 实时读数 → 无任何 UAC。

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore: remove staged copies, document post-MVP state (F8,F10)"
```

---

## 后续路线（本计划之外，按需再立计划）

| # | 事项 | 备注 |
|---|------|------|
| R2 | 传感器选择 + 刷新间隔设置 | JSON 配置 + CmdPal 设置页；等 Dock 用稳了再定交互 |
| R4 | Host 打进 MSIX 包随扩展分发 | 消除 `SENSORMONITOR_HOST_EXE` 环境变量依赖 |
| R6 | 多 band（每指标一按钮）、温度阈值变色 | `WrappedDockItem` 多 ListItem 形态 |
| R7 | Host 空闲自退出（N 分钟无管道请求）| 有计划任务静默重启后才有意义，避免常驻提权进程 |
| R8 | 管道抢注防护（校验服务端进程签名/路径） | F9 升级时再做；当前数据只读非敏感，接受风险 |
