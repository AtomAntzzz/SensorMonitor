using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SensorMonitorExtension.Ipc;
using CmdPalSettings = Microsoft.CommandPalette.Extensions.Toolkit.Settings;

namespace SensorMonitorExtension.Settings;

/// <summary>
/// 扩展全局设置（走 CmdPal 内置 Settings，宿主自动渲染/持久化）。
/// 两项：刷新间隔（1/2/5s）、温度单位（°C/°F）。变更时推给 SnapshotCache 与 TempDisplay。
/// </summary>
internal sealed class SettingsManager
{
    private readonly CmdPalSettings _settings;

    public SettingsManager()
    {
        _settings = new CmdPalSettings();

        // 注：本机 Toolkit（0.9.260303001）的 ChoiceSetSetting 无「默认值」构造重载，
        // 默认值经基类 Setting<T>.Value 设置（object initializer）。
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

        _settings.Add(refreshInterval);
        _settings.Add(tempUnit);
        _settings.SettingsChanged += OnSettingsChanged;
        Apply();   // 推持久化初值：首轮轮询即用持久间隔、首帧即用持久单位
    }

    public ICommandSettings Settings => _settings;

    public int RefreshIntervalMs =>
        int.TryParse(_settings.GetSetting<string>("refreshInterval"), out var ms) ? ms : 1000;

    public bool Fahrenheit => _settings.GetSetting<string>("tempUnit") == "F";

    private void OnSettingsChanged(object sender, CmdPalSettings args) => Apply();

    private void Apply()
    {
        SnapshotCache.SetIntervalMs(RefreshIntervalMs);
        TempDisplay.Fahrenheit = Fahrenheit;
        SnapshotCache.NotifyDisplayChanged();   // 令 dock band 立即以新单位重绘
    }
}
