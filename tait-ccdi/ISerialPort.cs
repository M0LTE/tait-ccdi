using System.IO.Ports;

namespace tait_ccdi;

public interface ISerialPort
{
    int ReadByte();
    void WriteLine(string line);
    string? ReadExisting();
    TimeSpan ReadTimeout { get; set; }
}

public class RealSerialPortWrapper : ISerialPort
{
    private readonly SerialPort serialPort;

    public RealSerialPortWrapper(SerialPort serialPort)
    {
        serialPort.NewLine = "\r";
        this.serialPort = serialPort;
    }

    public TimeSpan ReadTimeout { get => TimeSpan.FromMilliseconds(serialPort.ReadTimeout); set => serialPort.ReadTimeout = (int)value.TotalMilliseconds; }
    public int ReadByte() => serialPort.ReadByte();
    public string? ReadExisting() => serialPort.ReadExisting();
    public void WriteLine(string s) => serialPort.WriteLine(s);
}