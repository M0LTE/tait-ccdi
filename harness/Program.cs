using harness;
using Microsoft.Extensions.Logging;
using tait_ccdi;

Dictionary<RadioState, string> displayStates = new() {
    { RadioState.Transmitting, "TX " },
    { RadioState.ReceivingSignal, "SIG" },
    { RadioState.ReceivingNoise, "RX " },
};

var logger = ConsoleWritelineLogger.Instance;

var radio = TaitRadio.Create("COM6", 28800, logger);

object cursorLock = new();
Console.WriteLine();

radio.StateChanged += (sender, e) =>
{
    lock (cursorLock)
    {
        var (Left, Top) = Console.GetCursorPosition();
        Console.SetCursorPosition(Console.WindowWidth - 45, 0);
        Console.Write($"State: {displayStates[e.To]}");
        Console.SetCursorPosition(Left, Top);
    }
};

radio.RawRssiUpdated += (sender, e) =>
{
    lock (cursorLock)
    {
        var (Left, Top) = Console.GetCursorPosition();
        Console.SetCursorPosition(Console.WindowWidth - 16, 0);
        Console.Write($"RSSI: {e.Rssi,6:0.0} dBm");
        Console.SetCursorPosition(Left, Top);
    }
};

radio.VswrChanged += (sender, e) =>
{
    lock (cursorLock)
    {
        var (Left, Top) = Console.GetCursorPosition();
        Console.SetCursorPosition(Console.WindowWidth - 33, 0);
        Console.Write($"VSWR: {e.Vswr,3:0.0}:1");
        Console.SetCursorPosition(Left, Top);
    }
};

radio.PaTempRead += (sender, e) =>
{
    lock (cursorLock)
    {
        var (Left, Top) = Console.GetCursorPosition();
        Console.SetCursorPosition(Console.WindowWidth - 60, 0);
        Console.Write($"Temp: {e.TempC,3:0.0}°C");
        Console.SetCursorPosition(Left, Top);
        //logger.LogInformation(e.TempC + " " + e.AdcValue);
    }
};

while (true)
{
    Console.Write(" >");
    var cmd = Console.ReadLine();

    try
    {
        if (cmd == "exit")
        {
            radio.Disconnect();
            break;
        }
        else if (cmd == "ccr")
        {
            radio.EnterCcrMode();
        }
        else if (cmd == "exitccr")
        {
            radio.ExitCcrMode();
        }
        else if (cmd!.StartsWith("txf "))
        {
            var freq = int.Parse(cmd[4..]);
            radio.SetTxFrequency(freq);
        }
        else if (cmd.StartsWith("rxf "))
        {
            var freq = int.Parse(cmd[4..]);
            radio.SetRxFrequency(freq);
        }
        else if (cmd.StartsWith("ctcss "))
        {
            var ctcss = decimal.Parse(cmd.Substring(6));
            radio.SetTxCtcss(ctcss);
            radio.SetRxCtcss(ctcss);
        }
        else if (cmd.StartsWith("mon "))
        {
            radio.SetMonitor(cmd[4..] == "on");
        }
        else if (cmd.StartsWith("vol "))
        {
            radio.SetVolume((byte)int.Parse(cmd[4..]));
        }
        else if (cmd.StartsWith("bw "))
        {
            radio.SetBandwidth(Enum.Parse<ChannelBandwidth>(cmd[3..]));
        }
        else if (cmd.StartsWith("pwr "))
        {
            radio.SetPower(Enum.Parse<Power>(cmd[3..]));
        }
        else if (cmd.StartsWith("g "))
        {
            radio.GoToChannel(int.Parse(cmd[2..]));
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex.Message);
    }
}