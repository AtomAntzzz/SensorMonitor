using System.Text.Json;
using SensorMonitor.Host.Ipc;
using SensorMonitor.Host.Model;
using SensorMonitor.Host.Sensors;

const string PipeName = "SensorMonitor.Host.v1";
const int RefreshMs = 2000;

// 调试分支：打印一次传感器快照后退出（放在建 Mutex 之前，不占单实例名额）。
if (args is ["--dump"])
{
    using var dumpReader = new LhmSensorReader();
    Console.WriteLine(JsonSerializer.Serialize(dumpReader.Read(),
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

using var singleInstance = new Mutex(initiallyOwned: true, "Global\\SensorMonitor.Host", out var isNew);
if (!isNew)
{
    Console.Error.WriteLine("SensorMonitor.Host 已在运行，退出。");
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
    catch (Exception ex) { Console.Error.WriteLine($"刷新失败: {ex}"); }
    finally
    {
        try { refreshTimer.Change(RefreshMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { /* 停机竞态：忽略 */ }
    }
}, null, RefreshMs, Timeout.Infinite);
using var _ = refreshTimer;

using var server = new PipeJsonServer(PipeName, () => Volatile.Read(ref cached),
    log: Console.Error.WriteLine);
server.Start();

Console.WriteLine($"SensorMonitor.Host 运行中，管道: {PipeName}，刷新间隔: {RefreshMs}ms。Ctrl+C 退出。");
var exit = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
exit.Wait();
return 0;
