using LibreHardwareMonitor.Hardware;
using SysPulse.Host.Model;

namespace SysPulse.Host.Sensors;

public static class SensorMapper
{
    public static bool IsRelevant(SensorType type) => type is
        SensorType.Temperature or SensorType.Clock or SensorType.Load
        or SensorType.Fan or SensorType.Power;

    public static string UnitOf(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Clock => "MHz",
        SensorType.Load => "%",
        SensorType.Fan => "RPM",
        SensorType.Power => "W",
        _ => "",
    };

    public static SensorReading ToReading(
        string id, string hardwareName, string sensorName, SensorType type, float value)
        => new(id, hardwareName, sensorName, type.ToString(), value, UnitOf(type));
}
