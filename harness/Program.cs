using harness;
using Microsoft.Extensions.Logging;
using System.IO.Ports;
using tait_ccdi;

var logger = ConsoleWritelineLogger.Instance;

var sp = new SerialPort("COM2", 28800);
sp.Open();
var radio = new TaitRadio(new RealSerialPortWrapper(sp), logger);

object lockObj = new();
Console.CursorVisible = false;

radio.StateChanged += (sender, e) =>
{
    lock (lockObj)
    {
        logger.LogInformation($"Radio state changed to {e.To}");
    }
};

radio.RawRssiUpdated += (sender, e) =>
{
    lock (lockObj)
    {
        var (Left, Top) = Console.GetCursorPosition();
        Console.SetCursorPosition(Console.WindowWidth - 16, 0);
        Console.Write($"RSSI: {e.Rssi,6:0.0} dBm");
        Console.SetCursorPosition(Left, Top);
    }
};

radio.VswrChanged += (sender, e) =>
{
    lock (lockObj)
    {
        logger.LogInformation($"VSWR: {e.Vswr:0.0} : 1");
    }
};

radio.PaTempRead += (sender, e) =>
{
    lock (lockObj)
    {
        logger.LogInformation($"PA Temp: {e.TempC} °C, ADC value: {e.AdcValue}");
    }
};

Thread.CurrentThread.Join();
