using System.Diagnostics;
using tait_ccdi;

var radio = new TaitRadio("COM3", 28800);

var stopwatch = Stopwatch.StartNew();

while (true)
{
    double rawRssi = radio.GetRawRssi();
    double forwardPower = radio.GetForwardPower();
    double reversePower = radio.GetReversePower();
    double paTemp = radio.GetPaTemperature();

    Console.WriteLine($"{stopwatch.ElapsedMilliseconds:000}ms   paTemp:{paTemp}C  rssi:{rawRssi:0.0}dBm   fwd:{forwardPower} rev:{reversePower}");

    stopwatch.Restart();
}