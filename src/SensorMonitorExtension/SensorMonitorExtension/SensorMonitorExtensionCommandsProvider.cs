// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using SensorMonitorExtension.Dock;

namespace SensorMonitorExtension;

public partial class SensorMonitorExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly SensorDockBand _band = new();

    public SensorMonitorExtensionCommandsProvider()
    {
        DisplayName = "Sensor Monitor";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _commands = [
            new CommandItem(new SensorMonitorExtensionPage()) { Title = DisplayName },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    // Dock band（SDK ≥ 0.9.260303001 的 ICommandProvider3）：
    public override ICommandItem[]? GetDockBands()
    {
        _band.EnsureStarted(); // 懒启动（F5）：未进 Dock 流程的用户不轮询、不触发自动拉起
        return [new WrappedDockItem([_band], "com.sensormonitor.dock", "Sensor Monitor")];
    }
}
