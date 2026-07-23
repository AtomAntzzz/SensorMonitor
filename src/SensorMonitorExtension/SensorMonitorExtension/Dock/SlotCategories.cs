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

    // 有效读数：非 NaN/无穷且非 0。未装 PawnIO 时 CPU 温度/频率仍在快照里但读 0（或 NaN），视作无数据。
    private static bool IsValid(float v) => float.IsFinite(v) && v != 0f;

    /// <summary>
    /// 提权硬件驱动（PawnIO/ring0）就绪信号：存在**有效**的 CPU 温度或主板 LPC 读数即判驱动在工作
    /// （某类别仍空即"无此传感器"而非缺驱动，区分空态提示用）。
    /// 只认温度/LPC：CPU 负载走性能计数器、免驱动亦有效，不能当驱动信号；
    /// 且未装驱动时 CPU 温度传感器仍在快照里但读 0，故须按值判而非仅按存在判。
    /// </summary>
    public static bool HasDriverData(IReadOnlyList<SensorReading> s) =>
        s.Any(r => IsValid(r.Value) &&
            ((IsCpu(r) && r.Type == "Temperature") || r.Id.StartsWith("/lpc")));

    public static readonly SlotCategory[] All =
    [
        new("cpuclock", "CPU 频率", "核心", "\uEC4A", "需 PawnIO 驱动", "无此传感器", s =>
        {
            var clocks = s.Where(r => r.Type == "Clock" && IsValid(r.Value) && IsCpu(r)).OrderBy(r => r.Id).ToList();
            if (clocks.Count == 0) return [];
            var list = new List<SlotCandidate>
            {
                new(SlotLogic.MaxKey, "全核最大", clocks.Max(r => r.Value), clocks[0].Unit, IsDefault: true),
            };
            list.AddRange(clocks.Select(r => new SlotCandidate(r.Id, r.Name, r.Value, r.Unit, false)));
            return list;
        }),
        new("cputemp", "CPU 温度", "温度点", "\uE950", "需 PawnIO 驱动", "无此传感器", s =>
            Temps(s, IsCpu, r => r.Name == "CPU Package")),
        // GPU 不依赖 PawnIO，两态提示同字（HasDriverData 分支对 GPU 无实义）。
        new("gputemp", "GPU 温度", "温度点", "\uE7F4", "无 GPU 温度传感器", "无 GPU 温度传感器", s =>
            Temps(s, r => r.Id.StartsWith("/gpu"), r => r.Name == "GPU Core")),
        new("boardtemp", "主板温度", "温度点", "\uE9CA", "需 PawnIO 驱动", "无此传感器", s =>
            Temps(s, r => r.Id.StartsWith("/lpc"), r => false)),  // 默认=排序首项
    ];

    // 温度类通用构建：defaultMatch 无命中时列表首项兜底为默认（spec：类别定义表）。
    private static List<SlotCandidate> Temps(IReadOnlyList<SensorReading> s,
        Func<SensorReading, bool> match, Func<SensorReading, bool> defaultMatch)
    {
        var sorted = s.Where(r => r.Type == "Temperature" && IsValid(r.Value) && match(r)).OrderBy(r => r.Id).ToList();
        var defIdx = sorted.FindIndex(r => defaultMatch(r));
        if (defIdx < 0) defIdx = 0;
        return sorted.Select((r, i) => new SlotCandidate(r.Id, r.Name, r.Value, r.Unit, i == defIdx)).ToList();
    }
}
