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
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(_snapshotProvider()));
                    // 让客户端读完并主动断开后我方再关管道。若服务端抢先 Dispose，
                    // 客户端的 using StreamWriter 在 Dispose 时 flush 会撞上已关闭管道，
                    // 抛 ObjectDisposedException（Task 7 的 PipeSensorClient 不捕获它）。
                    // 再读一行：客户端关闭连接即得到 EOF(null)，此时我方安全关闭。
                    await reader.ReadLineAsync(_cts.Token);
                }
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
