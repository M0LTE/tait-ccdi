using System.IO.Ports;

namespace tait_ccdi;

public interface ISerialPort : IDisposable
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

public class RealSerialPortWrapper(string comPort, int baud) : ISerialPort
{
    private SerialPort _serialPort = new SerialPort(comPort, baud);
    public int ReadTimeout { get => _serialPort.ReadTimeout; set => _serialPort.ReadTimeout = value; }
    public void DiscardInBuffer() => _serialPort.DiscardInBuffer();
    public void DiscardOutBuffer() => _serialPort.DiscardOutBuffer();
    public override string ToString() => comPort;
    public int ReadByte() => _serialPort.ReadByte();
    public string? ReadExisting() => _serialPort.ReadExisting();
    public string ReadTo(string value) => _serialPort.ReadTo(value);
    public void WriteLine(string s) => _serialPort.WriteLine(s);
    public void Dispose() => _serialPort.Dispose();
    public void Open()
    {
        using var serialPort = new SerialPort(comPort, baud, Parity.None, 8, StopBits.One);
        serialPort.NewLine = "\r";
    }
}