# tait-ccdi

This is a .NET 8 library for the Tait Communications Common Channel Digital Interface (CCDI) protocol. It was built and tested with the Tait TM8100 transceiver, but may work with other Tait 8000-series radios that support the CCDI protocol.

If you find this useful, please feel free to [![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/Y8Y8KFHA0), but no pressure of course.

## Installation

```
dotnet add package m0lte.tait-ccdi
```

## Radio Configuration

On the Data tab of the radio programming application, ensure the radio has Data Port set to Aux (15-pin accessory connector), Mic (front panel RJ45), or Internal Options as required. Set the Command Mode Baud Rate to 28800 (or the rate you specify when opening the serial port).

## Usage

```csharp
var radio = new TaitRadio("COM2", 28800);

radio.StateChanged += (sender, e) =>
{
    Console.WriteLine($"State: {e.From} {e.To}");
};

radio.RawRssiUpdated += (sender, e) =>
{
    Console.WriteLine($"RSSI: {e.Rssi,6:0.0} dBm");
};

radio.VswrChanged += (sender, e) =>
{
    Console.WriteLine($"VSWR: {e.Vswr,3:0.0}:1");
};

radio.PaTempRead += (sender, e) =>
{
    Console.WriteLine($"Temp: {e.TempC,3:0.0}°C");
};

// the above will monitor a radio in CCDI mode. To get direct control of the radio at a lower level:

radio.EnterCcrMode();

// the events above will stop, but you can now send commands to the radio:

radio.SetTxFrequency(144950000);
radio.SetRxFrequency(144950000); // note, required for the radio to start receiving anything; CCR mode seems to drop the radio out of memory mode
radio.SetTxCtcss(69.3);
radio.SetRxCtcss(0);
radio.SetBandwidth(ChannelBandwidth.Wide));
radio.SetPower(Power.VeryLow);
radio.SetVolume(0x50);

// and you can reboot the radio into CCDI mode again:

radio.ExitCcrMode();
```

Unfortunately it's not possible for the radio to be in both modes at the same time; it's one subset of features or the other.
