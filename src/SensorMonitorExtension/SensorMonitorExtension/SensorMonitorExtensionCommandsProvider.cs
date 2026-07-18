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
    // band 一次性创建，保持对象身份稳定（Dock 依赖属性变更通知重绘）。
    private readonly ICommandItem[] _dockBands;

    public SensorMonitorExtensionCommandsProvider()
    {
        DisplayName = "Sensor Monitor";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands = [
            new CommandItem(new SensorMonitorExtensionPage()) { Title = DisplayName },
        ];
        _dockBands = SlotCategories.All
            .Select(c => (ICommandItem)new WrappedDockItem(
                [new SensorSlotBand(c)], $"com.sensormonitor.{c.Id}", c.DisplayName))
            .ToArray();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    // Dock band（SDK ≥ 0.9.260303001 的 ICommandProvider3）：4 个独立预设控件（spec A1）。
    public override ICommandItem[]? GetDockBands()
    {
        SnapshotCache.EnsureStarted(); // 懒启动（F5）：未进 Dock 流程不轮询、不触发自动拉起
        return _dockBands;
    }
}
