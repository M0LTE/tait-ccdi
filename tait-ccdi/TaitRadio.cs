using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace tait_ccdi;


// see https://wiki.oarc.uk/_media/radios:tm8100-protocol-manual.pdf

public class TaitRadio
{
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<RssiEventArgs>? RawRssiUpdated;
    public event EventHandler<VswrEventArgs>? VswrChanged;
    public event EventHandler<PaTempEventArgs>? PaTempRead;

    private readonly ISerialPort serialPort;
    private readonly ILogger logger;
    private readonly object commandLock = new();
    private readonly List<int> paTempResponses = [];

    private double? rssi;
    private double? fwdPower, revPower;
    bool ready;
    private RadioState state;

    public TaitRadio(ISerialPort serialPort, ILogger logger)
    {
        this.serialPort = serialPort;
        this.logger = logger;

        var serialListenerThread = new Thread(SerialPortListener);
        serialListenerThread.IsBackground = true;
        serialListenerThread.Start();
    }

    /// <summary>
    /// Get the VSWR from the forward and reverse voltage.
    /// </summary>
    /// <returns></returns>
    private static double? GetVswr(double forward, double reverse)
    {
        double top = forward + reverse;
        double bottom = forward - reverse;
        double result = top / bottom;
        return result;
    }

    private void SendCommand(string command)
    {
        SpinWait.SpinUntil(() => ready);
        ready = false;
        serialPort.WriteLine(command);
    }

    static void DebugSerialPortOutputToConsole(ISerialPort serialPort)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("**");
        Console.ResetColor();

        while (true)
        {
            var b = serialPort.ReadByte();

            if (b >= 32 && b < 127)
            {
                Console.Write((char)b);
            }
            else if (b == '\r')
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("\\r");
                Console.ResetColor();
                Console.WriteLine();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                
                Console.Write("{" + b.ToHex() + "}");
                Console.ResetColor();
            }
        }
    }

    private void WaitForRadio()
    {
        while (true)
        {
            serialPort.ReadExisting();
            serialPort.WriteLine(QueryCommands.ModelAndCcdiVersion);
            var response = ReadResponseTypeAndResponse('m', TimeSpan.FromSeconds(1));
            if (string.IsNullOrWhiteSpace(response))
            {
                logger.LogInformation("Looking for radio...");
            }
            else
            {
                logger.LogInformation("Found radio; model/version: {response}", response);
                break;
            }
        }
    }

    private void SerialPortListener()
    {
        WaitForRadio();

        var dataGathererThread = new Thread(GatherDataBasedOnState);
        dataGathererThread.IsBackground = true;
        dataGathererThread.Start();

        using Timer timer = new(SendGetTemperatureQuery, null, 0, 60000);

        while (true)
        {
            if (!serialPort.TryReadChar(TimeSpan.FromSeconds(10), out var readChar))
            {
                logger.LogInformation("No data read");
                continue;
            }

            ready = readChar == '.';

            if (readChar == '.')
            {
                continue;
            }
            else if (readChar == 'p' || readChar == 'e' || readChar == 'j')
            {
                var messageBody = ReadResponse(TimeSpan.FromSeconds(1));
                if (messageBody == null)
                {
                    logger.LogError("Failed to read message {message} in 5s", readChar);
                }
                else
                {
                    var message = readChar + messageBody;
                    /*if (!CcdiChecksum.Validate(msg))
                    {
                        logger.LogError(msg + " has invalid checksum");
                        continue;
                    }*/

                    if (CcdiCommand.TryParse(message, out var command))
                    {
                        if (command.Ident == 'j')
                        {
                            var response = command.AsQueryResponse();

                            if (response.Command == "047")
                            {
                                // this may result in two separate responses depending on radio model
                                paTempResponses.Add(int.Parse(response.Data));
                            }
                            else if (response.Command == "064")
                            {
                                rssi = double.Parse(response.Data) / 10.0;
                            }
                            else if (response.Command == "318")
                            {
                                fwdPower = double.Parse(response.Data);
                            }
                            else if (response.Command == "319")
                            {
                                revPower = double.Parse(response.Data);
                            }
                            else
                            {
                                logger.LogInformation($"query response: command:{response.Command} data:{response.Data}");
                            }
                        }
                        else if (command.Ident == 'p')
                        {
                            var progress = command.AsProgressMessage();
                            HandleProgress(progress.ProgressType);
                        }
                        else if (command.Ident == 'e')
                        {
                            var error = command.AsErrorMessage();
                            HandleError(error);
                        }
                    }
                    else
                    {
                        logger.LogWarning("Could not parse {message} as CcdiCommand", message);
                    }
                }
            }
            else
            {
                logger.LogWarning("Unexpected character read from radio: 0x{readChar}", ((int)readChar).ToHex());
            }
        }
    }

    private void HandleError(ErrorMessage error)
    {
        logger.LogWarning($"error: {error.Category}");
    }

    private void HandleProgress(ProgressType progressType)
    {
        var oldState = state;

        if (progressType == ProgressType.PttMicActivated)
        {
            state = RadioState.Transmitting;
            if (oldState != RadioState.Transmitting)
            {
                transmittingFor.Restart();
            }
        }
        else if (progressType == ProgressType.PttMicDeactivated)
        {
            state = RadioState.ReceivingNoise;
            if (oldState == RadioState.Transmitting)
            {
                transmittingFor.Stop();
            }
        }
        else if (progressType == ProgressType.ReceiverBusy)
        {
            state = RadioState.ReceivingSignal;
        }
        else if (progressType == ProgressType.ReceiverNotBusy)
        {
            state = RadioState.ReceivingNoise;
        }

        if (oldState != state)
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, state));
        }
    }

    private string? ReadResponse(TimeSpan timeout)
    {
        var oldTimeout = serialPort.ReadTimeout;
        serialPort.ReadTimeout = timeout;
        try
        {
            var lengthChars = serialPort.ReadChars(2);
            var length = int.Parse(lengthChars);
            var data = new string(serialPort.ReadChars(length));
            var checksum = new string(serialPort.ReadChars(2));
            var cr = serialPort.ReadByte();
            if (cr != '\r')
            {
                throw new Exception($"Expected CR after checksum, got '{cr.ToHex()}'");
            }

            if (data.Length != length)
            {
                throw new Exception($"Expected {length} characters, got {data.Length}");
            }

            if (checksum.Length != 2)
            {
                throw new Exception($"Expected 2 checksum characters, got {checksum.Length}");
            }

            return $"{lengthChars[0]}{lengthChars[1]}{data}{checksum}";
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading response");
            return null;
        }
        finally
        {
            serialPort.ReadTimeout = oldTimeout;
        }
    }

    private string? ReadResponseTypeAndResponse(char expectedResponseType, TimeSpan timeout)
    {
        var oldTimeout = serialPort.ReadTimeout;
        serialPort.ReadTimeout = timeout;
        var sw = Stopwatch.StartNew();

        try
        {
            while (serialPort.ReadByte() != expectedResponseType)
            {
                if (sw.Elapsed > timeout)
                {
                    return null;
                }
            }

            var lengthChars = serialPort.ReadChars(2);
            var length = int.Parse(lengthChars);
            var data = new string(serialPort.ReadChars(length));
            var checksum = new string(serialPort.ReadChars(2));
            var cr = serialPort.ReadByte();
            if (cr != '\r')
            {
                throw new Exception($"Expected CR after checksum, got '{cr.ToHex()}'");
            }

            return expectedResponseType + data + checksum;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading response");
            return null;
        }
        finally
        {
            serialPort.ReadTimeout = oldTimeout;
        }
    }

    private void SendGetTemperatureQuery(object? _)
    {
        lock (commandLock)
        {
            SendCommand(QueryCommands.Cctm_PaTemperature);

            // on the 8100 the PA temp query results in two completely separate responses
            // first one is temp in C
            // second one is ADC value in mV (0 to 1200)

            PaTempEventArgs? ea = null;

            if (SpinWait.SpinUntil(() => paTempResponses.Count == 2, TimeSpan.FromMilliseconds(100)))
            {
                ea = new PaTempEventArgs(paTempResponses[0], paTempResponses[1]);
            }
            else if (paTempResponses.Count == 1)
            {
                ea = new PaTempEventArgs(null, paTempResponses[0]);
            }

            paTempResponses.Clear();

            if (ea != null && (ea.TempC == null || ea.TempC <= 100))
            {
                PaTempRead?.Invoke(this, ea);
            }
        }
    }

    private void GatherDataBasedOnState()
    {
        while (true)
        {
            if (state == RadioState.ReceivingNoise || state == RadioState.ReceivingSignal)
            {
                lock (commandLock)
                {
                    SendCommand(QueryCommands.Cctm_RawRssi);
                    if (SpinWait.SpinUntil(() => rssi != null, TimeSpan.FromSeconds(1)))
                    {
                        RawRssiUpdated?.Invoke(this, new RssiEventArgs(rssi!.Value));
                        rssi = null;
                    }
                    else
                    {
                        logger.LogWarning("Failed to get RSSI in 1s");
                    }
                }
            }
            else if (state == RadioState.Transmitting)
            {
                double? fwd = null;
                double? rev = null;

                lock (commandLock)
                {
                    SendCommand(QueryCommands.Cctm_ForwardPower);
                    if (SpinWait.SpinUntil(() => fwdPower != null, TimeSpan.FromSeconds(1)))
                    {
                        fwd = fwdPower;
                        fwdPower = null;
                    }
                    else
                    {
                        logger.LogWarning("Failed to get fwd power in 1s");
                    }
                }

                lock (commandLock)
                {
                    SendCommand(QueryCommands.Cctm_ReversePower);
                    if (SpinWait.SpinUntil(() => revPower != null, TimeSpan.FromSeconds(1)))
                    {
                        rev = revPower;
                        revPower = null;
                    }
                    else
                    {
                        logger.LogWarning("Failed to get rev power in 1s");
                    }
                }

                if (fwd != null && rev != null && transmittingFor.ElapsedMilliseconds > 300 && state == RadioState.Transmitting)
                {
                    var vswr = GetVswr(fwd.Value, rev.Value);
                    if (vswr.HasValue && !double.IsNaN(vswr.Value) && vswr > 0)
                    {
                        VswrChanged?.Invoke(this, new VswrEventArgs(vswr.Value));
                    }
                }
            }
        }
    }

    private readonly Stopwatch transmittingFor = new();
}

public enum RadioState
{
    ReceivingNoise, ReceivingSignal, Transmitting
}


public class StateChangedEventArgs(RadioState from, RadioState to) : EventArgs
{
    public RadioState From { get; } = from;
    public RadioState To { get; } = to;
}

public class RssiEventArgs(double rssi) : EventArgs
{
    public double Rssi { get; } = rssi;
}

public class VswrEventArgs(double vswr) : EventArgs
{
    public double Vswr { get; } = vswr;
}

public class PaTempEventArgs(int? readTempC, int readAdcValue) : EventArgs
{
    public int? TempC { get; } = readTempC;
    public int AdcValue { get; } = readAdcValue;
}