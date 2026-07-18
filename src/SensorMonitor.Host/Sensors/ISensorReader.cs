using SensorMonitor.Host.Model;

namespace SensorMonitor.Host.Sensors;

public interface ISensorReader : IDisposable
{
    SensorSnapshot Read();
}
