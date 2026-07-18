using System.Text.Json;
using SensorMonitor.Host.Model;
using Xunit;

public class SensorSnapshotTests
{
    [Fact]
    public void Snapshot_RoundTrips_Through_Json()
    {
        var snapshot = new SensorSnapshot(
            DateTimeOffset.Parse("2026-07-18T10:00:00+08:00"),
            [new SensorReading("/intelcpu/0/temperature/8", "CPU", "CPU Package", "Temperature", 65.5f, "°C")]);

        var json = JsonSerializer.Serialize(snapshot);
        var back = JsonSerializer.Deserialize<SensorSnapshot>(json);

        Assert.NotNull(back);
        Assert.Equal(snapshot.Timestamp, back!.Timestamp);
        Assert.Equal(snapshot.Sensors[0], back.Sensors[0]);
    }
}
