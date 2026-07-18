using System;
using System.Linq;
using System.Threading;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension.Dock;

// ⚠️ 本文件依赖 CmdPal Extensions Toolkit 的实际 API（ListItem / NoOpCommand /
//    InvokableCommand / CommandResult 等），未经编译验证。Task 1 生成模板后，
//    以生成项目里 Toolkit 的真实类型为准做微调（尤其 NoOpCommand 是否存在、
//    ListItem 的属性变更通知机制）。业务逻辑（FormatLine）已按本机实测传感器修正。
//
// 这是 Task 8 收尾形态（真实数据 + 一次性自动拉起 Host）。
// 建议先按 Task 6 用假数据版本验证 Dock 会随 Title 变更重绘，再切到本版本。

/// <summary>
/// Dock 上的一个传感器显示项。仿官方 Time &amp; Date 扩展的 NowDockBand 模式：
/// ListItem 定时更新 Title，Dock 随属性变更通知自动重绘。
/// </summary>
internal sealed partial class SensorDockBand : ListItem, IDisposable
{
    private readonly Timer _timer;
    private bool _autoLaunchAttempted;

    public SensorDockBand()
        : base(new Commands.LaunchHostCommand()) // 点击 band 项即可手动拉起 Host
    {
        Title = "传感器: 连接中…";
        _timer = new Timer(_ => Refresh(), null, 0, 2000);
    }

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
                _autoLaunchAttempted = true;   // 只自动尝试一次，避免用户拒绝 UAC 后被反复骚扰
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

    /// <summary>
    /// MVP 默认三项：CPU 频率 / CPU 温度 / GPU 温度。匹配规则按本机实测传感器
    /// （docs/references/sensor-sources.md 末尾）确定：
    ///  - CPU 频率：限定 Id 前缀 /intelcpu 或 /amdcpu 的 Clock 取最大值。
    ///    ⚠️ 不能用计划原稿的 Name.Contains("Core") —— 那会命中 GPU Core 时钟，
    ///    把 GPU 频率误显为 CPU（实测机 RTX 3080 = 1710MHz）。无 PawnIO 时 CPU
    ///    无 Clock 传感器，此处得 null → 显示 "--"，属正确降级。
    ///  - CPU 温度：优先 Name == "CPU Package"，回退到该 CPU 首个温度传感器。
    ///    （原「主板温度」改为此项：这台主板的 SuperIO 即使装了 PawnIO 也未被 LHM 识别，
    ///    无 /lpc/ 主板温度传感器；CPU Package 是更有参考价值且现成可读的整机温度。）
    ///  - GPU 温度：Temperature 且 Name == "GPU Core"（实测 /gpu-nvidia/0/temperature/0）。
    /// </summary>
    private static string FormatLine(SensorSnapshot s)
    {
        static string Fmt(float? v, string unit) => v is null ? "--" : $"{v:F0}{unit}";

        float? cpuClock = s.Sensors
            .Where(r => r.Type == "Clock"
                && (r.Id.StartsWith("/intelcpu") || r.Id.StartsWith("/amdcpu")))
            .Select(r => (float?)r.Value)
            .Max();
        float? cpuTemp =
            s.Sensors.FirstOrDefault(r => r.Type == "Temperature" && r.Name == "CPU Package")?.Value
            ?? s.Sensors.FirstOrDefault(r => r.Type == "Temperature"
                   && (r.Id.StartsWith("/intelcpu") || r.Id.StartsWith("/amdcpu")))?.Value;
        float? gpuTemp = s.Sensors
            .FirstOrDefault(r => r.Type == "Temperature" && r.Name == "GPU Core")?.Value;

        return $"CPU {Fmt(cpuClock, "MHz")} · CPU {Fmt(cpuTemp, "°C")} · GPU {Fmt(gpuTemp, "°C")}";
    }

    public void Dispose() => _timer.Dispose();
}
