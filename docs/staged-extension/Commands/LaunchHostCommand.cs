using System.Diagnostics;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace SensorMonitorExtension.Commands;

// ⚠️ 依赖 Toolkit 的 InvokableCommand / CommandResult，未经编译验证；以生成模板的真实 API 为准。

internal sealed partial class LaunchHostCommand : InvokableCommand
{
    public LaunchHostCommand()
    {
        Name = "启动传感器 Host";
        Id = "com.sensormonitor.launchhost";
    }

    /// <summary>
    /// 开发期用环境变量指向仓库构建产物；打包后回退到包内 Host 目录（后续路线 R4 落地）。
    /// 例：SENSORMONITOR_HOST_EXE=D:\Workspace\SensorMonitor\src\SensorMonitor.Host\bin\Debug\net8.0\SensorMonitor.Host.exe
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
