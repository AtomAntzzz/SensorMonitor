using System;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SensorMonitorExtension.Ipc;
using CmdPalSettings = Microsoft.CommandPalette.Extensions.Toolkit.Settings;

namespace SensorMonitorExtension.Settings;

/// <summary>
/// 扩展全局设置。继承 Toolkit 的 JsonSettingsManager：宿主只负责渲染设置页、并在用户改动时
/// 触发 SettingsChanged——持久化是扩展自己的事（宿主不会替我们存），故 LoadSettings() 读盘、
/// SaveSettings() 写盘缺一不可。两项：刷新间隔（1/2/5s）、温度单位（°C/°F）。
/// 变更时先存盘再推给 SnapshotCache（间隔）与 TempDisplay（单位）。
/// </summary>
internal sealed class SettingsManager : JsonSettingsManager
{
    public SettingsManager()
    {
        // 持久化文件：<每用户设置根>/SensorMonitorExtension/settings.json（BaseSettingsPath 处理打包/非打包）。
        var dir = Utilities.BaseSettingsPath("SensorMonitorExtension");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "settings.json");

        var refreshInterval = new ChoiceSetSetting(
            "refreshInterval", "刷新间隔", "Dock 读数轮询间隔",
            [
                new ChoiceSetSetting.Choice("1 秒", "1000"),
                new ChoiceSetSetting.Choice("2 秒", "2000"),
                new ChoiceSetSetting.Choice("5 秒", "5000"),
            ])
        { Value = "1000" };

        var tempUnit = new ChoiceSetSetting(
            "tempUnit", "温度单位", "温度显示单位",
            [
                new ChoiceSetSetting.Choice("摄氏 °C", "C"),
                new ChoiceSetSetting.Choice("华氏 °F", "F"),
            ])
        { Value = "C" };

        Settings.Add(refreshInterval);
        Settings.Add(tempUnit);

        // 读盘：有持久值则覆盖上面的种子默认。文件缺失/损坏不能拖垮扩展加载，故兜异常回落默认（F3 同理）。
        try { LoadSettings(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"设置读盘失败，回落默认值: {ex}"); }

        Settings.SettingsChanged += OnSettingsChanged;
        Apply();   // 用（读盘后的）当前值做首次推送：首轮轮询即用持久间隔、首帧即用持久单位
    }

    public int RefreshIntervalMs =>
        int.TryParse(Settings.GetSetting<string>("refreshInterval"), out var ms) ? ms : 1000;

    public bool Fahrenheit => Settings.GetSetting<string>("tempUnit") == "F";

    private void OnSettingsChanged(object sender, CmdPalSettings args)
    {
        // 写盘：跨会话保留。写盘异常（磁盘满/占用）不能带崩宿主的 SettingsChanged 回调。
        try { SaveSettings(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"设置写盘失败: {ex}"); }
        Apply();
    }

    private void Apply()
    {
        SnapshotCache.SetIntervalMs(RefreshIntervalMs);
        TempDisplay.Fahrenheit = Fahrenheit;
        SnapshotCache.NotifyDisplayChanged();   // 令 dock band 立即以新单位重绘
    }
}
