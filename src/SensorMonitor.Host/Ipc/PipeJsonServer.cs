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

    // 末次 GET 请求的 UTC ticks（accept 循环写、Host 空闲 Timer 读，Interlocked 保证 64 位原子可见）。
    private long _lastRequestTicks;

    /// <summary>末次收到 GET 请求的时刻；构造时初始化为启动时刻（未连接过也从启动起计）。</summary>
    public DateTimeOffset LastRequestUtc =>
        new(Interlocked.Read(ref _lastRequestTicks), TimeSpan.Zero);

    public PipeJsonServer(string pipeName, Func<SensorSnapshot> snapshotProvider,
        TimeSpan? connectionTimeout = null, Action<string>? log = null)
    {
        _pipeName = pipeName;
        _snapshotProvider = snapshotProvider;
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(5);
        _log = log ?? (_ => { });
        _lastRequestTicks = DateTimeOffset.UtcNow.UtcTicks;
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
                Interlocked.Exchange(ref _lastRequestTicks, DateTimeOffset.UtcNow.UtcTicks);
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
