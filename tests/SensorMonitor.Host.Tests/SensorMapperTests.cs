using LibreHardwareMonitor.Hardware;
using SensorMonitor.Host.Sensors;
using Xunit;

public class SensorMapperTests
{
    [Theory]
    [InlineData(SensorType.Temperature, true, "°C")]
    [InlineData(SensorType.Clock, true, "MHz")]
    [InlineData(SensorType.Load, true, "%")]
    [InlineData(SensorType.Fan, true, "RPM")]
    [InlineData(SensorType.Power, true, "W")]
    [InlineData(SensorType.Data, false, "")]      // 不关心的类型被过滤
    [InlineData(SensorType.SmallData, false, "")]
    public void Relevance_And_Unit(SensorType type, bool relevant, string unit)
    {
        Assert.Equal(relevant, SensorMapper.IsRelevant(type));
        if (relevant)
            Assert.Equal(unit, SensorMapper.UnitOf(type));
    }

    [Fact]
    public void ToReading_Maps_All_Fields()
    {
        var reading = SensorMapper.ToReading(
            id: "/gpu-nvidia/0/temperature/0", hardwareName: "NVIDIA GeForce RTX 4080",
            sensorName: "GPU Core", type: SensorType.Temperature, value: 61.0f);

        Assert.Equal("/gpu-nvidia/0/temperature/0", reading.Id);
        Assert.Equal("NVIDIA GeForce RTX 4080", reading.Hardware);
        Assert.Equal("GPU Core", reading.Name);
        Assert.Equal("Temperature", reading.Type);
        Assert.Equal(61.0f, reading.Value);
        Assert.Equal("°C", reading.Unit);
    }
}
