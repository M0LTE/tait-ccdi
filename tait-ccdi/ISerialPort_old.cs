using System.IO.Ports;

namespace tait_ccdi;

public interface ISerialPort_old : IDisposable
{
    int ReadTimeout { get; set; }
    void Open();
    int ReadByte();
    void DiscardInBuffer();
    void DiscardOutBuffer();
    void WriteLine(string modelAndCcdiVersion);
    string ReadTo(string value);
    string? ReadExisting();
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
    public void DiscardInBuffer() => serialPort.DiscardInBuffer();
    public void DiscardOutBuffer() => serialPort.DiscardOutBuffer();
    public override string ToString() => serialPort.PortName;
    public int ReadByte() => serialPort.ReadByte();
    public string? ReadExisting() => serialPort.ReadExisting();
    public string ReadTo(string value) => serialPort.ReadTo(value);
    public void WriteLine(string s) => serialPort.WriteLine(s);
    public void Dispose() => serialPort.Dispose();
}