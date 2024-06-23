using harness;
using Microsoft.Extensions.Logging;
using System.IO.Ports;
using tait_ccdi;

/*var sp = new SerialPort("COM2", 28800);
sp.Open();
sp.NewLine = "\r";
sp.WriteLine("q010FE");
sp.ReadTimeout = 5000;

var output = sp.ReadTo("\r.");

List<byte> bytes = new();
while (true)
{
    try
    {
        var b = sp.ReadByte();
        bytes.Add((byte)b);
        Console.Write("0x" + b.ToString("X").ToLower() + " ");
    }
    catch (TimeoutException ex)
    {
        break;
    }
}

using var fs = File.OpenWrite("response.bin");
fs.Write(bytes.ToArray());
return;*/

var logger = ConsoleWritelineLogger.Instance;

var radio = new TaitRadio("COM2", 28800, logger);

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

Thread.CurrentThread.Join();