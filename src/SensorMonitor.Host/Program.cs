using System.Text.Json;
using SensorMonitor.Host.Sensors;

if (args is ["--dump"])
{
    using var reader = new LhmSensorReader();
    Console.WriteLine(JsonSerializer.Serialize(reader.Read(),
        new JsonSerializerOptions { WriteIndented = true }));
    return;
}
