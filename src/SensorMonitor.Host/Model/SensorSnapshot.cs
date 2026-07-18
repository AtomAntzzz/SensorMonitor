namespace SensorMonitor.Host.Model;

public sealed record SensorReading(
    string Id,        // LHM 传感器标识符，如 /intelcpu/0/temperature/8
    string Hardware,  // 所属硬件显示名，如 "Intel Core i7-14700K"
    string Name,      // 传感器名，如 "CPU Package"
    string Type,      // SensorType 枚举名：Temperature / Clock / Load / Fan / Power
    float Value,
    string Unit);     // °C / MHz / % / RPM / W

public sealed record SensorSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<SensorReading> Sensors);
