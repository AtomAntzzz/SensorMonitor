using SysPulse.Host.Model;

namespace SysPulse.Host.Sensors;

public interface ISensorReader : IDisposable
{
    SensorSnapshot Read();
}
