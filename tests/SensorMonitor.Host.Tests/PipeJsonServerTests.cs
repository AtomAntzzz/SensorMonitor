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
        // reader 先声明→后释放：writer 先 Dispose 时先 flush（此刻管道仍开），
        // 避免 reader 抢先关闭底层流后 writer flush 撞上已关闭管道。
        using var reader = new StreamReader(client);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync("GET");
        var line = await reader.ReadLineAsync();

        var back = JsonSerializer.Deserialize<SensorSnapshot>(line!);
        Assert.Equal(5200f, back!.Sensors[0].Value);
    }

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
}
