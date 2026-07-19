using System;
using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension.Dock;

/// <summary>
/// 一个"类别槽位"Dock 控件：显示类内当前选中传感器，右键 上一个/下一个 轮换（spec：显示规则）。
/// 主命令=NoOp（Task 1 实测主命令在右键菜单置顶、不沉底；用户定单击=无操作），
/// "启动传感器 Host"放 MoreCommands 末位实现沉底（对齐 Performance Monitor 的"打开任务管理器"位置）。
/// 标题/字幕显隐由 Dock 宿主编辑模式控制，本类不管（A1.3 零代码）。
/// </summary>
internal sealed partial class SensorSlotBand : ListItem
{
    private readonly SlotCategory _cat;
    private string? _currentKey;

    internal SlotCategory Category => _cat;
    internal string? CurrentKey => _currentKey;
    private readonly Pages.SensorPickerPage _picker;

    public SensorSlotBand(SlotCategory cat)
        : base(new NoOpCommand() { Id = $"com.sensormonitor.{cat.Id}.noop" })  // Dock 项 Command.Id 为空会被静默忽略（坑 #3）
    {
        _cat = cat;
        Icon = new IconInfo(cat.IconGlyph);
        Title = "--";
        Subtitle = cat.DisplayName;
        _currentKey = SlotStore.Get(cat.Id);
        MoreCommands =
        [
            new CommandContextItem(new CycleSlotCommand(this, cat, -1)),
            new CommandContextItem(new CycleSlotCommand(this, cat, +1)),
            new CommandContextItem(new Commands.LaunchHostCommand()),  // 末位=菜单沉底
        ];
        // 单击 band 打开类别选择页（探针确认 Command 可写且接受 Page）。
        // base(...) 的 NoOp 仅占位，此处覆盖为真正的主命令。持引用以便选择变更后通知刷新。
        _picker = new Pages.SensorPickerPage(this, cat);
        Command = _picker;
        // 订阅静态事件：band 与扩展进程同生命周期，无需退订；
        // 若将来 band 支持重建，必须配对退订，否则静态事件根挂实例（泄漏）。
        SnapshotCache.Updated += Refresh;
        Refresh();
    }

    internal void Cycle(int delta)
    {
        var snap = SnapshotCache.Current;
        if (snap?.Sensors is null) return;  // 无数据时轮换无意义
        var next = SlotLogic.Cycle(_cat.GetCandidates(snap.Sensors), _currentKey, delta);
        if (next is null) return;
        _currentKey = next.Key;
        SlotStore.Set(_cat.Id, next.Key);
        Refresh();
    }

    /// <summary>绝对选择（picker 页用）：直接切到指定 key，写持久化并刷新。</summary>
    internal void SetSelection(string key)
    {
        _currentKey = key;
        SlotStore.Set(_cat.Id, key);
        Refresh();
        _picker.NotifyChanged();  // 让选择页的"✓ 当前"标记跟着移动
    }

    private void Refresh()
    {
        try
        {
            RefreshCore();
        }
        catch (Exception ex)
        {
            // Timer 线程的未处理异常会带崩扩展进程（F3）——宁可显示错误也不崩。
            Title = "内部错误";
            Subtitle = ex.GetType().Name;
        }
    }

    private void RefreshCore()
    {
        var snap = SnapshotCache.Current;
        if (snap?.Sensors is null)
        {
            SetDisplay("--", "Host 未运行");
            return;
        }
        var current = SlotLogic.Resolve(_cat.GetCandidates(snap.Sensors), _currentKey);
        if (current is null)
        {
            SetDisplay("--", _cat.EmptyHint);  // 如无 PawnIO 时 CPU 两类候选为空
            return;
        }
        var age = DateTimeOffset.Now - snap.Timestamp;
        var subtitle = age > TimeSpan.FromSeconds(10)
            ? $"⚠ 数据已 {age.TotalSeconds:F0}s 未更新"                  // F7 过期提示优先
            : (current.IsDefault ? _cat.DisplayName : current.Label);   // spec：显示规则
        SetDisplay($"{current.Value:F0}{current.Unit}", subtitle);
    }

    /// <summary>
    /// 变化保护：只在字符串真变时才赋值，避免每 1s 无谓地给 CmdPal 宿主发属性变更事件。
    /// 高频冗余更新会淹没宿主 dock 更新队列、卡住其它扩展的合并 band（实测 Perf Monitor）。
    /// </summary>
    private void SetDisplay(string title, string subtitle)
    {
        if (Title != title) Title = title;
        if (Subtitle != subtitle) Subtitle = subtitle;
    }
}

/// <summary>类内轮换命令：上一个/下一个{CycleNoun}。</summary>
internal sealed partial class CycleSlotCommand : InvokableCommand
{
    private readonly SensorSlotBand _band;
    private readonly int _delta;

    public CycleSlotCommand(SensorSlotBand band, SlotCategory cat, int delta)
    {
        _band = band;
        _delta = delta;
        Name = (delta > 0 ? "下一个" : "上一个") + cat.CycleNoun;
        Icon = new IconInfo(delta > 0 ? "\uE893" : "\uE892");  // Next / Previous 字形
        // Dock 项 Command.Id 为空会被静默忽略（坑 #3），上下文命令一并给足。
        Id = $"com.sensormonitor.{cat.Id}.{(delta > 0 ? "next" : "prev")}";
    }

    public override CommandResult Invoke()
    {
        _band.Cycle(_delta);
        return CommandResult.KeepOpen();
    }
}
