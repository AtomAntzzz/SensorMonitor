using System.Runtime.InteropServices;
using System.Text.Json;
using SensorMonitor.Host;
using SensorMonitor.Host.Ipc;
using SensorMonitor.Host.Model;
using SensorMonitor.Host.Sensors;

const string PipeName = "SensorMonitor.Host.v1";
const int RefreshMs = 1000;

// 调试分支：打印一次传感器快照后退出（放在建 Mutex 之前，不占单实例名额）。
// WinExe 无自带控制台（D8），贴附父进程控制台以保住从终端启动时的输出。
if (args is ["--dump"])
{
    AttachConsole(-1); // -1 = ATTACH_PARENT_PROCESS
    using var dumpReader = new LhmSensorReader();
    Console.WriteLine(JsonSerializer.Serialize(dumpReader.Read(),
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args is ["--install-task"]) return TaskInstaller.Install();
if (args is ["--uninstall-task"]) return TaskInstaller.Uninstall();

using var singleInstance = new Mutex(initiallyOwned: true, "Global\\SensorMonitor.Host", out var isNew);
if (!isNew)
{
    HostLog.Write("SensorMonitor.Host 已在运行，退出。");
    return 1;
}

using var reader = new LhmSensorReader();

// 传感器读取较慢（数十 ms 级），缓存快照、后台定时刷新，管道请求只读缓存。
SensorSnapshot cached = reader.Read();
// one-shot 重排：上一轮 Read() 结束才排下一轮，杜绝重入（LHM 非线程安全，F6）。
Timer refreshTimer = null!;
refreshTimer = new Timer(_ =>
{
    try { Volatile.Write(ref cached, reader.Read()); }
    catch (Exception ex) { HostLog.Write($"刷新失败: {ex}"); }
    finally
    {
        try { refreshTimer.Change(RefreshMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { /* 停机竞态：忽略 */ }
    }
}, null, RefreshMs, Timeout.Infinite);
using var _ = refreshTimer;

using var server = new PipeJsonServer(PipeName, () => Volatile.Read(ref cached),
    log: HostLog.Write);
server.Start();

HostLog.Write($"Host 启动，管道: {PipeName}，刷新间隔: {RefreshMs}ms。");
var exit = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); }; // 无控制台时不会触发，保留无害
exit.Wait();
return 0;

[DllImport("kernel32.dll")]
static extern bool AttachConsole(int dwProcessId);
