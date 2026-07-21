using System;
using System.Collections.Generic;
using System.Linq;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension.Dock;

/// <summary>4 个预设控件的类别定义（spec：类别定义表）。候选按 Id 排序保证轮换顺序稳定。</summary>
internal static class SlotCategories
{
    private static bool IsCpu(SensorReading r) =>
        r.Id.StartsWith("/intelcpu") || r.Id.StartsWith("/amdcpu");

    public static readonly SlotCategory[] All =
    [
        new("cpuclock", "CPU 频率", "核心", "\uEC4A", "需 PawnIO 驱动", s =>
        {
            var clocks = s.Where(r => r.Type == "Clock" && IsCpu(r)).OrderBy(r => r.Id).ToList();
            if (clocks.Count == 0) return [];
            var list = new List<SlotCandidate>
            {
                new(SlotLogic.MaxKey, "全核最大", clocks.Max(r => r.Value), clocks[0].Unit, IsDefault: true),
            };
            list.AddRange(clocks.Select(r => new SlotCandidate(r.Id, r.Name, r.Value, r.Unit, false)));
            return list;
        }),
        new("cputemp", "CPU 温度", "温度点", "\uE950", "需 PawnIO 驱动", s =>
            Temps(s, IsCpu, r => r.Name == "CPU Package")),
        new("gputemp", "GPU 温度", "温度点", "\uE7F4", "无 GPU 温度传感器", s =>
            Temps(s, r => r.Id.StartsWith("/gpu"), r => r.Name == "GPU Core")),
        new("boardtemp", "主板温度", "温度点", "\uE9CA", "需 PawnIO 驱动", s =>
            Temps(s, r => r.Id.StartsWith("/lpc"), r => false)),  // 默认=排序首项
    ];

    // 温度类通用构建：defaultMatch 无命中时列表首项兜底为默认（spec：类别定义表）。
    private static List<SlotCandidate> Temps(IReadOnlyList<SensorReading> s,
        Func<SensorReading, bool> match, Func<SensorReading, bool> defaultMatch)
    {
        var sorted = s.Where(r => r.Type == "Temperature" && match(r)).OrderBy(r => r.Id).ToList();
        var defIdx = sorted.FindIndex(r => defaultMatch(r));
        if (defIdx < 0) defIdx = 0;
        return sorted.Select((r, i) => new SlotCandidate(r.Id, r.Name, r.Value, r.Unit, i == defIdx)).ToList();
    }
}
