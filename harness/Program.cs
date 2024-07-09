using harness;
using tait_ccdi;

Dictionary<RadioState, string> displayStates = new() {
    { RadioState.Transmitting, "TX " },
    { RadioState.ReceivingSignal, "SIG" },
    { RadioState.ReceivingNoise, "RX " },
};

var logger = ConsoleWritelineLogger.Instance;

var radio = TaitRadio.Create("COM2", 28800, logger);

object cursorLock = new();
Console.CursorVisible = false;
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

Thread.CurrentThread.Join();
