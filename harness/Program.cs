using System.Diagnostics;
using tait_ccdi;

var radio = new TaitRadio("COM3", 28800);

var stopwatch = Stopwatch.StartNew();

while (true)
{
    double rawRssi = radio.GetRawRssi();
    var swr = radio.GetVswr();
    double paTemp = radio.GetPaTemperature();

    Console.WriteLine($"{stopwatch.ElapsedMilliseconds:000}ms   paTemp:{paTemp}C  rssi:{rawRssi:0.0}dBm   swr:{(swr == null ? "rx" : $"{swr:0.0}:1")}");

    stopwatch.Restart();
}