namespace SysPulse.Host;

/// <summary>
/// WinExe 无控制台（D8），错误全部落 %ProgramData%\SysPulse\host.log。
/// 超 1MB 直接重开新文件 —— 这是错误日志不是运行日志，正常运行几乎不增长。
/// </summary>
internal static class HostLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SysPulse", "host.log");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                    File.Delete(LogPath);
                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
