using System.Collections.Generic;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SensorMonitorExtension.Dock;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension.Pages;

/// <summary>
/// 单击 band 打开的"类别选择页"：列出该 band 类别的候选（右键轮换同一批），
/// 点选即把 band 换成该传感器。仅类别内选择，保持"每类一控件"语义（spec：需求 1）。
/// </summary>
internal sealed partial class SensorPickerPage : ListPage
{
    private readonly SensorSlotBand _band;
    private readonly SlotCategory _cat;

    public SensorPickerPage(SensorSlotBand band, SlotCategory cat)
    {
        _band = band;
        _cat = cat;
        Title = "选择" + cat.DisplayName;
        Name = "选择";
        Icon = new IconInfo(cat.IconGlyph);
        Id = $"com.sensormonitor.{cat.Id}.picker";  // 非空 Id（坑 #3）
    }

    /// <summary>band 选择变更后调用：令页面重取 items，使"✓ 当前"标记移到新选中项
    /// （ListPage 缓存 items，不主动通知则再开页仍显旧标记）。</summary>
    internal void NotifyChanged() => RaiseItemsChanged(0);

    public override IListItem[] GetItems()
    {
        var snap = SnapshotCache.Current;
        if (snap?.Sensors is null)
        {
            return [new ListItem(new Commands.LaunchHostCommand())
                { Title = "Host 未运行", Subtitle = "回车启动传感器 Host" }];
        }
        var candidates = _cat.GetCandidates(snap.Sensors);
        if (candidates.Count == 0)
        {
            return [new ListItem(new NoOpCommand()) { Title = "--", Subtitle = _cat.EmptyHint }];
        }
        // 当前选中项 = SlotLogic.Resolve 的结果（考虑失效 key 回退默认），
        // 而非裸比 CurrentKey —— 否则未持久化过时 CurrentKey=null，谁都不标"当前"。
        var current = SlotLogic.Resolve(candidates, _band.CurrentKey);
        var items = new List<IListItem>();
        foreach (var c in candidates)
        {
            bool isCurrent = current is not null && c.Key == current.Key;
            var (dispVal, dispUnit) = Settings.TempDisplay.Convert(c.Value, c.Unit);
            items.Add(new ListItem(new SelectSensorCommand(_band, c.Key))
            {
                // 当前项标题前置普通 Unicode ✓（避免与列表焦点高亮混淆；不碰 PUA 字形）。
                Title = (isCurrent ? "✓ " : "") + $"{c.Label} {dispVal:F0}{dispUnit}",
                Subtitle = isCurrent ? "当前" : "",
            });
        }
        return [.. items];
    }
}

/// <summary>选择页里一项的命令：把 band 换成该 key 并退回。</summary>
internal sealed partial class SelectSensorCommand : InvokableCommand
{
    private readonly SensorSlotBand _band;
    private readonly string _key;

    public SelectSensorCommand(SensorSlotBand band, string key)
    {
        _band = band;
        _key = key;
        Name = "选为当前";
        Id = $"com.sensormonitor.select.{key}";  // 非空 Id（坑 #3）
    }

    public override CommandResult Invoke()
    {
        _band.SetSelection(_key);
        return CommandResult.GoBack();  // 选完退回 dock/上一页
    }
}
