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

    /// <summary>
    /// 提权硬件驱动（PawnIO/ring0）就绪信号：CPU(Intel/AMD) 或主板 LPC 只要读到任一传感器，
    /// 说明驱动在工作；此时某类别仍空即"该机型确无此传感器"而非缺驱动（区分空态提示用）。
    /// </summary>
    public static bool HasDriverData(IReadOnlyList<SensorReading> s) =>
        s.Any(r => IsCpu(r) || r.Id.StartsWith("/lpc"));

    public static readonly SlotCategory[] All =
    [
        new("cpuclock", "CPU 频率", "核心", "\uEC4A", "需 PawnIO 驱动", "该机型无此传感器", s =>
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
        new("cputemp", "CPU 温度", "温度点", "\uE950", "需 PawnIO 驱动", "该机型无此传感器", s =>
            Temps(s, IsCpu, r => r.Name == "CPU Package")),
        // GPU 不依赖 PawnIO，两态提示同字（HasDriverData 分支对 GPU 无实义）。
        new("gputemp", "GPU 温度", "温度点", "\uE7F4", "无 GPU 温度传感器", "无 GPU 温度传感器", s =>
            Temps(s, r => r.Id.StartsWith("/gpu"), r => r.Name == "GPU Core")),
        new("boardtemp", "主板温度", "温度点", "\uE9CA", "需 PawnIO 驱动", "该机型无此传感器", s =>
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
