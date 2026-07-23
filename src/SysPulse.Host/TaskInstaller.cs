using System.Diagnostics;

namespace SysPulse.Host;

internal static class TaskInstaller
{
    public const string TaskName = "SysPulse.Host";

    /// <summary>注册登录自启 + 可按需触发的最高权限任务（本进程已提权，schtasks 直接成功）。</summary>
    public static int Install()
    {
        var exe = Environment.ProcessPath!;
        return Run($"/Create /TN {TaskName} /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F");
    }

    public static int Uninstall() => Run($"/Delete /TN {TaskName} /F");

    private static int Run(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks", args)
        { UseShellExecute = false, CreateNoWindow = true })!;
        p.WaitForExit();
        HostLog.Write($"schtasks {args} → {p.ExitCode}");
        return p.ExitCode;
    }
}
