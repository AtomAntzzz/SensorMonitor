using System.Collections.Generic;
using System.Linq;

namespace SysPulseExtension.Dock;

/// <summary>候选解析与轮换的纯函数（无 UI 依赖，为将来单测留口，spec：架构）。</summary>
internal static class SlotLogic
{
    /// <summary>合成项"全核最大"的持久化保留 Id（spec：轮换与持久化）。</summary>
    public const string MaxKey = "__max__";

    /// <summary>按 Key 找当前项；Key 缺失/失效（换硬件、无 PawnIO）回退默认项；候选为空返回 null。</summary>
    public static SlotCandidate? Resolve(List<SlotCandidate> candidates, string? savedKey)
    {
        if (candidates.Count == 0) return null;
        if (savedKey is not null)
        {
            var hit = candidates.FirstOrDefault(c => c.Key == savedKey);
            if (hit is not null) return hit;
        }
        return candidates.FirstOrDefault(c => c.IsDefault) ?? candidates[0];
    }

    /// <summary>循环轮换：从当前项偏移 delta（±1），到尾回头。</summary>
    public static SlotCandidate? Cycle(List<SlotCandidate> candidates, string? currentKey, int delta)
    {
        var current = Resolve(candidates, currentKey);
        if (current is null) return null;
        var i = candidates.FindIndex(c => c.Key == current.Key);
        var n = ((i + delta) % candidates.Count + candidates.Count) % candidates.Count;
        return candidates[n];
    }
}
