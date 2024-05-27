using System.Diagnostics;
using tait_ccdi;
using MathNet.Numerics.Statistics;

var radio = new TaitRadio("COM3", 28800);
radio.ProgressMessageReceived += (sender, args) =>
{
    if (args.ProgressMessage.ProgressType != ProgressType.FfskDataReceived)
    {
        Console.WriteLine(args.ProgressMessage);
    }
};

var stopwatch = Stopwatch.StartNew();

var nf = new List<double>();

while (true)
{
    if (radio.State == RadioState.Transmitting)
    {
        var vswr = radio.GetVswr();
        var paTemp = radio.GetPaTemperature();
        Console.WriteLine($"{stopwatch.ElapsedMilliseconds:000}ms   vswr:{vswr:0.0}:1   paTemp:{paTemp}C");
    }
    else
    {
        double rawRssi = radio.GetRawRssi();
        if (radio.State == RadioState.HearingSignal)
        {
            var averageNoiseFloor = nf.Median();
            Console.WriteLine($"{stopwatch.ElapsedMilliseconds:000}ms   rssi:{rawRssi:0.0}dBm   nf:{averageNoiseFloor:0.0}dBm   snr:{rawRssi - averageNoiseFloor:0.0}dB");
        }
        else
        {
            // measure the noise floor while idle
            nf.Add(rawRssi);
            if (nf.Count > 100)
            {
                nf.RemoveAt(0);
            }
        }
    }

    stopwatch.Restart();
}
