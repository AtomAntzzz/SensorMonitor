using System;
using System.Diagnostics;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace SensorMonitorExtension.Commands;

// ⚠️ 依赖 Toolkit 的 InvokableCommand / CommandResult，未经编译验证；以生成模板的真实 API 为准。

internal sealed partial class LaunchHostCommand : InvokableCommand
{
    public LaunchHostCommand()
    {
        Name = "启动传感器 Host";
        Icon = new IconInfo("\uE768");  // Play 字形：启动 Host
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
        // D7：优先静默通道 —— 触发已注册的最高权限计划任务，无 UAC。
        if (TryRunScheduledTask()) return true;
        // 回退：直接拉起 exe，弹 UAC（仅用户显式点击时可接受）。
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

    /// <summary>D7：自动场景专用 —— 只走计划任务静默通道，绝不弹 UAC。</summary>
    internal static bool TryLaunchSilent() => TryRunScheduledTask();

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

    public override CommandResult Invoke()
    {
        TryLaunch();
        return CommandResult.KeepOpen();
    }
}
