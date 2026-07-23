using LibreHardwareMonitor.Hardware;
using SysPulse.Host.Model;

namespace SysPulse.Host.Sensors;

public sealed class LhmSensorReader : ISensorReader
{
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    private readonly Computer _computer;

    public LhmSensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
        };
        _computer.Open();
    }

    public SensorSnapshot Read()
    {
        _computer.Accept(new UpdateVisitor());
        var readings = new List<SensorReading>();
        foreach (var hw in _computer.Hardware)
            Collect(hw, readings);
        return new SensorSnapshot(DateTimeOffset.Now, readings);
    }

    private static void Collect(IHardware hw, List<SensorReading> into)
    {
        foreach (var sensor in hw.Sensors)
        {
            if (SensorMapper.IsRelevant(sensor.SensorType) && sensor.Value is float v)
                into.Add(SensorMapper.ToReading(
                    sensor.Identifier.ToString(), hw.Name, sensor.Name, sensor.SensorType, v));
        }
        foreach (var sub in hw.SubHardware)
            Collect(sub, into);
    }

    public void Dispose() => _computer.Close();
}
