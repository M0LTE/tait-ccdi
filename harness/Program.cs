using System.Diagnostics;
using tait_ccdi;

var radio = new TaitRadio("COM3", 115200);

var stopwatch = Stopwatch.StartNew();

radio.OnRawRssiResponse = response =>
{
    Console.WriteLine($"{1000/stopwatch.ElapsedMilliseconds:0}Hz   Data: {response.Data}");
    stopwatch.Restart();
    radio.Send(QueryCommands.Cctm_RawRssi);
};

radio.Send(QueryCommands.Cctm_RawRssi);

Thread.CurrentThread.Join();