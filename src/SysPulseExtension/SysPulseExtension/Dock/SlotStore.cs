using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SysPulseExtension.Ipc;
using Windows.Storage;

namespace SysPulseExtension.Dock;

/// <summary>
/// 各控件当前轮换选择的持久化：LocalState\slots.json，形如 {"cpuclock":"__max__",...}。
/// 读到失效 Key 由 SlotLogic.Resolve 回退默认，本类不清理（硬件回来自动恢复，spec：轮换与持久化）。
/// </summary>
internal static class SlotStore
{
    private static readonly string FilePath = Path.Combine(
        ApplicationData.Current.LocalFolder.Path, "slots.json");
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _map;

    public static string? Get(string categoryId)
    {
        lock (Gate)
        {
            Load();
            return _map!.TryGetValue(categoryId, out var v) ? v : null;
        }
    }

    public static void Set(string categoryId, string key)
    {
        lock (Gate)
        {
            Load();
            _map![categoryId] = key;
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_map, SensorJsonContext.Default.StringMap)); }
            catch (IOException) { }
            catch (System.UnauthorizedAccessException) { }
        }
    }

    private static void Load()
    {
        if (_map is not null) return;
        try { _map = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SensorJsonContext.Default.StringMap); }
        catch { _map = null; }  // 文件不存在/损坏 → 全默认
        _map ??= [];
    }
}
