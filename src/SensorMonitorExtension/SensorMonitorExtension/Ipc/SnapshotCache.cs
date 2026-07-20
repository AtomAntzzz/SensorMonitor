using System;
using System.Threading;

namespace SensorMonitorExtension.Ipc;

/// <summary>
/// 全局快照缓存：一个 1s Timer、每周期一次管道请求，所有 Dock 控件读缓存。
/// Host 管道串行处理，多控件各自轮询会互相排队（spec：架构）。
/// 懒启动（F5）：首次 EnsureStarted 才起 Timer。
/// Host 未运行时走静默通道自动拉起，全局 30s 节流（D7，从旧 SensorDockBand 迁来）。
/// </summary>
// one-shot 重排（同 Host 侧 F6）：上一轮完成才排下一轮，杜绝回调重叠。
internal static class SnapshotCache
{
    private static int _refreshMs = 1000;   // 可配（R2 设置页）；int 读写原子，Timer 读/设置写无需锁

    /// <summary>设置刷新间隔（ms）。夹下限防 0/负值把 Timer 打成忙循环。下一轮重排生效。</summary>
    public static void SetIntervalMs(int ms) => _refreshMs = System.Math.Max(200, ms);

    /// <summary>温度单位等纯显示项变更后，令订阅方（dock band）以最新快照重绘。
    /// event 只能在声明类型内 invoke，外部无法直接触发，故加此公开触发口。</summary>
    public static void NotifyDisplayChanged()
    {
        // 订阅方自行防崩（F3）；此处再兜一层，异常不许出去（同刷新路径记日志便于排查）。
        try { Updated?.Invoke(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SnapshotCache 显示刷新失败: {ex}"); }
    }
    private static Timer? _timer;
    private static readonly object Gate = new();
    private static DateTimeOffset _lastAutoLaunch = DateTimeOffset.MinValue;

    /// <summary>最近一次成功取到的快照；Host 未运行/畸形数据为 null。</summary>
    public static SensorSnapshot? Current { get; private set; }

    /// <summary>每轮刷新完成后触发（Timer 线程回调；订阅方自行防崩，F3）。</summary>
    public static event Action? Updated;

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_timer is null)
            {
                _timer = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(0, Timeout.Infinite);   // 赋值完成后再放行首轮（防首轮跑赢赋值致 Change 空跳）
            }
        }
    }

    private static void Refresh()
    {
        try
        {
            var snapshot = PipeSensorClient.TryFetch();
            if (snapshot?.Sensors is null)
            {
                snapshot = null;  // {"Sensors":null} 畸形数据一并按未运行处理（F3）
                if (DateTimeOffset.Now - _lastAutoLaunch > TimeSpan.FromSeconds(30))
                {
                    _lastAutoLaunch = DateTimeOffset.Now;
                    Commands.LaunchHostCommand.TryLaunchSilent();
                }
            }
            Current = snapshot;
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            // Timer 线程未处理异常会带崩扩展进程（F3）：任何异常都不许出去。
            System.Diagnostics.Debug.WriteLine($"SnapshotCache 刷新失败: {ex}");
        }
        finally
        {
            try { _timer?.Change(_refreshMs, Timeout.Infinite); } catch (ObjectDisposedException) { }
        }
    }
}
