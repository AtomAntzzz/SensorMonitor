using System;
using System.Collections.Generic;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension.Dock;

/// <summary>一类 Dock 槽位控件的声明式定义（spec：类别定义表）。加新类别只加一份定义。</summary>
internal sealed record SlotCategory(
    string Id,           // 持久化键 & band ID 后缀，如 "cpuclock"
    string DisplayName,  // 类别名 = 默认项的字幕，如 "CPU 频率"
    string CycleNoun,    // 轮换菜单名词：上一个{CycleNoun}，如 "核心"/"温度点"
    string IconGlyph,    // Segoe Fluent 字形
    string EmptyHint,    // 候选空 + 无驱动数据时的字幕（可能真缺驱动），如 "需 PawnIO 驱动"
    string MissingHint,  // 候选空 + 有驱动数据时的字幕（该机型确无此传感器），如 "该机型无此传感器"
    Func<IReadOnlyList<SensorReading>, List<SlotCandidate>> GetCandidates);

/// <summary>轮换候选。Key 用于持久化：传感器 Id，或合成项保留 Id（SlotLogic.MaxKey）。</summary>
internal sealed record SlotCandidate(string Key, string Label, float Value, string Unit, bool IsDefault);
