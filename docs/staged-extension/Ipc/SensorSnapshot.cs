namespace SensorMonitorExtension.Ipc;

// Host 侧 Model/SensorSnapshot.cs 的拷贝，仅命名空间不同（DTO 双拷贝，见 architecture.md D5）。
// 协议 v1 的 6 个字段必须与 Host 完全一致，否则 JSON 反序列化字段错位。

public sealed record SensorReading(
    string Id,
    string Hardware,
    string Name,
    string Type,
    float Value,
    string Unit);

public sealed record SensorSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<SensorReading> Sensors);
