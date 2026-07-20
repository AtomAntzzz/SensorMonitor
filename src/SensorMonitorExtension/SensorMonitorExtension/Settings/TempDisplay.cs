namespace SensorMonitorExtension.Settings;

/// <summary>
/// 温度显示单位转换（纯函数）。Fahrenheit 由 SettingsManager 依设置写入。
/// 按 Unit=="°C" 判温度（Host 仅温度输出 °C，SensorMapper.UnitOf）：
/// 非 °C 原样透传；°C 按当前单位换算（°F = °C·9/5+32）。三处显示位统一调用。
/// </summary>
internal static class TempDisplay
{
    /// <summary>仅 SettingsManager 写；显示位读。bool 读写原子，无需锁。</summary>
    public static bool Fahrenheit;

    public static (double Value, string Unit) Format(double value, string unit)
        => unit == "°C"
            ? (Fahrenheit ? value * 9 / 5 + 32 : value, Fahrenheit ? "°F" : "°C")
            : (value, unit);
}
