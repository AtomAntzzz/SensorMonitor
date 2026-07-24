using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SysPulseExtension.Ipc;
using SysPulseExtension.Localization;

namespace SysPulseExtension;

internal sealed partial class SysPulseExtensionPage : ListPage
{
    public SysPulseExtensionPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "SysPulse";
        Name = L.Get("PageName_Open");
    }

    public override IListItem[] GetItems()
    {
        var snapshot = PipeSensorClient.TryFetch(timeoutMs: 1500);
        if (snapshot?.Sensors is null)
        {
            return [new ListItem(new Commands.LaunchHostCommand())
                { Title = L.Get("HostNotRunning"), Subtitle = L.Get("PressEnterStartHost") }];
        }

        var items = new List<IListItem>();

        // R5：无任何 CPU 传感器 ⇒ PawnIO 未装（见 sensor-sources.md 实测），给安装引导。
        bool cpuVisible = snapshot.Sensors.Any(r =>
            r.Id.StartsWith("/intelcpu") || r.Id.StartsWith("/amdcpu"));
        if (!cpuVisible)
        {
            items.Add(new ListItem(new OpenUrlCommand("https://pawnio.eu/"))
            {
                Title = L.Get("CpuSensorsInvisible"),
                Subtitle = L.Get("PawnIoInstallHint"),
            });
        }

        items.AddRange(snapshot.Sensors
            .OrderBy(r => r.Hardware).ThenBy(r => r.Type).ThenBy(r => r.Id)
            .Select(r =>
            {
                var (dispVal, dispUnit) = Settings.TempDisplay.Convert(r.Value, r.Unit);
                return (IListItem)new ListItem(new NoOpCommand())
                {
                    Title = $"{r.Name}: {dispVal:F1} {dispUnit}",
                    Subtitle = $"{r.Hardware} · {r.Type}",
                };
            }));
        return [.. items];
    }
}
