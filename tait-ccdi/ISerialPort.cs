using System.IO.Ports;

namespace tait_ccdi;

public interface ISerialPort : IDisposable
{
    int ReadByte();
    void Write(string line);
    string? ReadExisting();
    TimeSpan ReadTimeout { get; set; }
    void Close();
}

public class RealSerialPortWrapper : ISerialPort
{
    private readonly SerialPort serialPort;

    public RealSerialPortWrapper(SerialPort serialPort)
    {
        if (!serialPort.IsOpen)
        {
            serialPort.Open();
        }
        this.serialPort = serialPort;
    }

    public TimeSpan ReadTimeout { get => TimeSpan.FromMilliseconds(serialPort.ReadTimeout); set => serialPort.ReadTimeout = (int)value.TotalMilliseconds; }
    public int ReadByte() => serialPort.ReadByte();
    public string? ReadExisting() => serialPort.ReadExisting();
    public void Write(string s) => serialPort.Write(s);
    public void Close() => serialPort.Close();
    public void Dispose() => serialPort.Dispose();
}