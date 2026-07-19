// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using SensorMonitorExtension.Dock;
using SensorMonitorExtension.Ipc;

namespace SensorMonitorExtension;

public partial class SensorMonitorExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    // 缓存 band 实例（各订阅静态 SnapshotCache.Updated 一次，不泄漏）；
    // 但每次 GetDockBands 用新 WrappedDockItem 包装（B1/重复症状：CmdPal 视 WrappedDockItem
    // 为一次性槽位，缓存同一实例会致 add-menu 重复列出/取消固定后重加异常；官方示例每次 new）。
    private readonly SensorSlotBand[] _bands;

    public SensorMonitorExtensionCommandsProvider()
    {
        DisplayName = "Sensor Monitor";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands = [
            new CommandItem(new SensorMonitorExtensionPage()) { Title = DisplayName },
        ];
        _bands = SlotCategories.All.Select(c => new SensorSlotBand(c)).ToArray();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    // Dock band（ICommandProvider3）：每次新建包装缓存 band（修 B1/重复），并给包装设
    // 类别图标（编辑停靠栏 add-menu 才有图标）。
    public override ICommandItem[]? GetDockBands()
    {
        SnapshotCache.EnsureStarted(); // 懒启动（F5）：未进 Dock 流程不轮询、不触发自动拉起
        return _bands.Select(b => (ICommandItem)new WrappedDockItem(
                [b], $"com.sensormonitor.{b.Category.Id}", b.Category.DisplayName)
            { Icon = new IconInfo(b.Category.IconGlyph) })
            .ToArray();
    }
}
