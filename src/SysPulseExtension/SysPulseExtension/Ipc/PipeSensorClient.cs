using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace SysPulseExtension.Ipc;

internal static class PipeSensorClient
{
    private const string PipeName = "SysPulse.Host.v1";

    /// <summary>连接 Host 取一次快照；Host 未运行/超时返回 null。</summary>
    public static SensorSnapshot? TryFetch(int timeoutMs = 500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            client.Connect(timeoutMs);
            // ⚠️ 修正（对照计划原稿）：reader 先声明、writer 后声明。
            // using 逆序释放 → writer 先 Dispose 并 flush（此刻管道仍开），
            // 再由 reader/client 关闭底层流。若按原稿顺序（writer 先声明），
            // reader 会抢先关流，writer.Dispose 的 flush 撞上已关闭管道抛
            // ObjectDisposedException —— 而下面的 catch 原本不含该类型，会直接崩。
            // 该竞态在 Host 侧 PipeJsonServerTests 已复现并验证（Task 4）。
            using var reader = new StreamReader(client);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("GET");
            var line = reader.ReadLine();
            // 用 source-gen 上下文而非反射（裁剪构建禁用反射式序列化，A2 实测）。
            return line is null ? null : JsonSerializer.Deserialize(line, SensorJsonContext.Default.SensorSnapshot);
        }
        catch (Exception ex) when (
            ex is TimeoutException or IOException or JsonException or ObjectDisposedException)
        {
            return null;
        }
    }
}
