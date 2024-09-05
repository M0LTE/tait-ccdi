using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;
using System.IO.Ports;

namespace tait_ccdi;


// see https://wiki.oarc.uk/_media/radios:tm8100-protocol-manual.pdf

public class TaitRadio
{
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<RssiEventArgs>? RawRssiUpdated;
    public event EventHandler<VswrEventArgs>? VswrChanged;
    public event EventHandler<PaTempEventArgs>? PaTempRead;

    private readonly ISerialPort serialPort;
    private readonly ILogger? logger;
    private readonly object commandLock = new();
    private readonly List<int> paTempResponses = [];

    private double? rssi;
    private double? fwdPower, revPower;
    bool ready;
    private RadioState state;

    public TaitRadio(ISerialPort serialPort, ILogger? logger)
    {
        this.serialPort = serialPort;
        this.logger = logger;

        var serialListenerThread = new Thread(SerialPortListener);
        serialListenerThread.IsBackground = true;
        serialListenerThread.Start();
    }

    public TaitRadio(string radioComPort, int radioComBaud, ILogger? logger = null)
        : this(CreateSerialPort(radioComPort, radioComBaud), logger)
    {
    }

    private static ISerialPort CreateSerialPort(string radioPort, int radioBaud)
    {
        var serialPort = new SerialPort(radioPort, radioBaud);
        var wrapper = new RealSerialPortWrapper(serialPort);
        return wrapper;
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

    private void SendCommand(string command) => SendCommand(command, CancellationToken.None);
    private void SendCommand(string command, CancellationToken cancellationToken)
    {
        if (!IsCcrMode)
        { 
            SpinWait.SpinUntil(() => ready || disconnectSignalled || cancellationToken.IsCancellationRequested);
            ready = false;
        }
        
        serialPort.Write(command + '\r');

        if (IsCcrMode)
        {
            logger?.LogInformation("Sent command: {command}", command);
        }
    }

    private void SendCommand(CcdiCommand ccdiCommand) => SendCommand(ccdiCommand, CancellationToken.None);
    private void SendCommand(CcdiCommand ccdiCommand, CancellationToken cancellationToken)
    {
        var command = ccdiCommand.ToString();
        SendCommand(command, cancellationToken);
    }

    static void DebugSerialPortOutputToConsole(ISerialPort serialPort)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("**");
        Console.ResetColor();

        while (true)
        {
            var b = serialPort.ReadByte();
            var c = (char)b;
            if (c.IsPrintable())
            {
                Console.Write(c);
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

    private bool disconnectSignalled;

    private void WaitForRadio()
    {
        while (!disconnectSignalled)
        {
            try
            {
                serialPort.ReadExisting();
                serialPort.Write(QueryCommands.ModelAndCcdiVersion + '\r');
                var response = ReadResponseTypeAndResponse('m', TimeSpan.FromSeconds(1));
                if (string.IsNullOrWhiteSpace(response))
                {
                    logger?.LogInformation("Looking for radio...");
                }
                else
                {
                    logger?.LogInformation("Found radio in CCDI mode; model/version: {response}", response);
                    break;
                }

                serialPort.ReadExisting();
                serialPort.Write("Q01PFE\r");
                var pingResponse = ReadResponseTypeAndResponse('Q', TimeSpan.FromSeconds(1));

                if (pingResponse != null && pingResponse.StartsWith("Q"))
                {
                    IsCcrMode = true;
                    if (pingResponse == "QD0A")
                    {
                        logger?.LogInformation("Found radio in CCR mode without config (receive frequency needs setting)");
                        break;
                    }
                    else if (pingResponse == "QPFE")
                    {
                        logger?.LogInformation("Found radio in CCR mode with required config");
                        break;
                    }
                    else
                    {
                        logger?.LogWarning("Unrecognised ping response {pingResponse}", pingResponse);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                if (disconnectSignalled)
                {
                    return;
                }
            }
        }
    }

    private void SerialPortListener()
    {
        WaitForRadio();

        var dataGathererThread = new Thread(GatherDataBasedOnState);
        dataGathererThread.IsBackground = true;
        dataGathererThread.Start();

        using Timer timer = new(SendGetTemperatureQuery, null, 2000, 10000);

        while (!disconnectSignalled)
        {
            char readChar;

            if (IsCcrMode)
            {
                readChar = (char)serialPort.ReadByte(); //= serialPort.ReadPrintableChars(1)[0];
            }
            else
            {
                if (!serialPort.TryReadChar(TimeSpan.FromSeconds(10), out readChar))
                {
                    logger?.LogInformation("No data read for 10s");
                    continue;
                }
            }

            ready = readChar == '.';

            if (readChar == '.')
            {
                continue;
            }
            else if ("pejM+-".Contains(readChar))
            {
                var messageBody = ReadResponse(TimeSpan.FromSeconds(1));
                if (messageBody == null)
                {
                    logger?.LogError("Failed to read message {message} in 5s", readChar);
                }
                else
                {
                    var message = readChar + messageBody;
                    if (!CcdiChecksum.Validate(message))
                    {
                        logger?.LogError(message + " has invalid checksum");
                        continue;
                    }

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
                                logger?.LogInformation($"query response: command:{response.Command} data:{response.Data}");
                            }
                        }
                        else if (command.Ident == 'p')
                        {
                            var progress = command.AsProgressMessage();
                            HandleProgress(progress);
                        }
                        else if (command.Ident == 'e')
                        {
                            var error = command.AsErrorMessage();
                            logger?.LogWarning($"error: {error.Category}");
                        }
                        else if (command.Ident == 'M' && command.Parameters == "R") // M 01 R 00
                        {
                            IsCcrMode = true;
                            logger?.LogInformation("Radio has entered CCR mode ({message})", message);
                        }
                        else if (command.Ident == '+')
                        {
                            if (!CcdiChecksum.Validate(message))
                            {
                                logger?.LogWarning("Invalid checksum in {message}", message);
                            }

                            if (command.Parameters == "E")
                            {
                                logger?.LogInformation("Rebooting");
                            }
                            else if (command.Parameters == "R")
                            {
                                logger?.LogInformation("Receive frequency set");
                            }
                            else if (command.Parameters == "T")
                            {
                                logger?.LogInformation("Transmit frequency set");
                            }
                            else if (command.Parameters == "A")
                            {
                                logger?.LogInformation("CTCSS set for receive");
                            }
                            else if (command.Parameters == "B")
                            {
                                logger?.LogInformation("CTCSS set for transmit");
                            }
                            else if (command.Parameters == "M")
                            {
                                logger?.LogInformation("Monitor set");
                            }
                            else if (command.Parameters == "J")
                            {
                                logger?.LogInformation("Volume set");
                            }
                            else if (command.Parameters == "H")
                            {
                                logger?.LogInformation("Bandwidth set");
                            }
                            else if (command.Parameters == "P")
                            {
                                logger?.LogInformation("Power set");
                            }
                            else if (command.Parameters == "Q")
                            {
                                //logger?.LogInformation("Q"); // unknown response to ping
                            }
                            else
                            {
                                logger?.LogWarning("Unhandled CCR response: {message}", message);

                                if (Debugger.IsAttached)
                                {
                                    Debugger.Break();
                                }
                            }
                        }
                        else if (command.Ident == '-')
                        {
                            if (command.Parameters == "02C")
                            {
                                logger?.LogInformation("Radio has entered CCR mode ({message})", message);
                            }
                            else if (command.Parameters == "03R")
                            {
                                logger?.LogInformation("Invalid receive frequency");
                            }
                            else if (command.Parameters == "03T")
                            {
                                logger?.LogInformation("Invalid transmit frequency");
                            }
                            else if (command.Parameters == "03A")
                            {
                                logger?.LogInformation("Invalid CTCSS for receive");
                            }
                            else if (command.Parameters == "03B")
                            {
                                logger?.LogInformation("Invalid CTCSS for transmit");
                            }
                            else if (command.Parameters == "01Q")
                            {
                                logger?.LogInformation("Unknown CCR failure {parameters}}", command.Parameters);
                            }
                            else
                            {
                                logger?.LogWarning("Unhandled CCR error: {message}", message);

                                if (Debugger.IsAttached)
                                {
                                    Debugger.Break();
                                }
                            }
                        }
                        else
                        {
                            logger?.LogWarning("Unhandled command: {message}", message);
                        }
                    }
                    else
                    {
                        logger?.LogWarning("Could not parse {message} as CcdiCommand", message);
                    }
                }
            }
            else if (readChar == 0x9c)
            {
                var oldTimeout = serialPort.ReadTimeout;
                serialPort.ReadTimeout = TimeSpan.FromSeconds(5);
                try
                {
                    if (serialPort.ReadByte() == 0xfe && serialPort.ReadByte() == 0x0d)
                    {
                        logger?.LogInformation("Radio has booted");
                    }
                }
                catch (Exception)
                {
                    logger?.LogWarning("Lost track of what radio is doing");
                }
                finally
                {
                    serialPort.ReadTimeout = oldTimeout;
                }
            }
            else
            {
                logger?.LogWarning("Unexpected character read from radio: {readChar}", readChar.IsPrintable() ? readChar : ("0x" + ((int)readChar).ToHex()));
            }
        }
    }

    private void HandleProgress(ProgressMessage progressMessage)
    {
        var oldState = state;
        var progressType = progressMessage.ProgressType;
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
        else if (progressType == ProgressType.UserInitiatedChannelChange)
        {
            channelUpdate = int.Parse(progressMessage.Para1);
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
            var lengthChars = serialPort.ReadPrintableChars(2);
            var length = int.Parse(lengthChars);
            var data = new string(serialPort.ReadPrintableChars(length));
            var checksum = new string(serialPort.ReadPrintableChars(2));
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
            logger?.LogError(ex, "Error reading response");
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

            var lengthChars = serialPort.ReadPrintableChars(2);
            var length = int.Parse(lengthChars);
            var data = new string(serialPort.ReadPrintableChars(length));
            var checksum = new string(serialPort.ReadPrintableChars(2));
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
            logger?.LogError(ex, "Error reading response");
            return null;
        }
        finally
        {
            serialPort.ReadTimeout = oldTimeout;
        }
    }

    private void SendGetTemperatureQuery(object? _)
    {
        if (disconnectSignalled || IsCcrMode)
        {
            return;
        }

        lock (commandLock)
        {
            SendCommand(QueryCommands.Cctm_PaTemperature);

            // on the 8100 the PA temp query results in two completely separate responses
            // first one is temp in C
            // second one is ADC value in mV (0 to 1200)

            PaTempEventArgs? ea = null;

            if (SpinWait.SpinUntil(() => paTempResponses.Count == 2, TimeSpan.FromMilliseconds(100)))
            {
                ea = new PaTempEventArgs(paTempResponses[1]);
            }
            else if (paTempResponses.Count == 1)
            {
                ea = new PaTempEventArgs(paTempResponses[0]);
            }

            paTempResponses.Clear();

            if (ea != null && ea.TempC <= 100)
            {
                PaTempRead?.Invoke(this, ea);
            }
        }
    }

    private void GatherDataBasedOnState()
    {
        while (!disconnectSignalled)
        {
            if (IsCcrMode)
            {
                Thread.Sleep(1000);
                continue;
            }

            if (state == RadioState.ReceivingNoise || state == RadioState.ReceivingSignal)
            {
                lock (commandLock)
                {
                    SendCommand(QueryCommands.Cctm_RawRssi);
                    if (SpinWait.SpinUntil(() => rssi != null || disconnectSignalled, TimeSpan.FromSeconds(0.1)))
                    {
                        if (disconnectSignalled)
                        {
                            return;
                        }
                        RawRssiUpdated?.Invoke(this, new RssiEventArgs(rssi!.Value));
                        rssi = null;
                    }
                    else
                    {
                        if (!IsCcrMode)
                        {
                            logger?.LogDebug("Failed to get RSSI");
                        }
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
                    if (SpinWait.SpinUntil(() => fwdPower != null || disconnectSignalled, TimeSpan.FromSeconds(1)))
                    {
                        fwd = fwdPower;
                        fwdPower = null;
                    }
                    else
                    {
                        if (!IsCcrMode)
                        {
                            logger?.LogWarning("Failed to get fwd power in 1s");
                        }
                    }
                }

                lock (commandLock)
                {
                    SendCommand(QueryCommands.Cctm_ReversePower);
                    if (SpinWait.SpinUntil(() => revPower != null || disconnectSignalled, TimeSpan.FromSeconds(1)))
                    {
                        rev = revPower;
                        revPower = null;
                    }
                    else
                    {
                        if (!IsCcrMode)
                        {
                            logger?.LogWarning("Failed to get rev power in 1s");
                        }
                    }
                }

                if (fwd != null && rev != null && transmittingFor.ElapsedMilliseconds > 300 && state == RadioState.Transmitting)
                {
                    var vswr = GetVswr(fwd.Value, rev.Value);
                    if (!disconnectSignalled && vswr.HasValue && !double.IsNaN(vswr.Value) && vswr > 0)
                    {
                        VswrChanged?.Invoke(this, new VswrEventArgs(vswr.Value));
                    }
                }
            }
        }
    }

    public void Disconnect()
    {
        disconnectSignalled = true;
        serialPort.Close();
        serialPort.Dispose();
    }

    /// <summary>
    /// Radio will return M01R00
    /// </summary>
    public void EnterCcrMode()
    {
        if (!IsCcrMode)
        {
            SendCommand("f0200D8");
        }
    }

    /// <summary>
    /// Radio will reboot
    /// </summary>
    public void ExitCcrMode()
    {
        if (IsCcrMode)
        {
            SendCommand("E005B");
            IsCcrMode = false;
        }
    }

    public void SetTxFrequency(int hz) => ExecuteCcrModeCommand(() => SendCommand(CcdiCommand.SetTxFreq(hz)));
    public void SetRxFrequency(int hz) => ExecuteCcrModeCommand(() => SendCommand(CcdiCommand.SetRxFreq(hz)));
    public void SetBandwidth(ChannelBandwidth bandwidth) => ExecuteCcrModeCommand(() => SendCommand(CcdiCommand.SetBandwidth(bandwidth)));
    public void SetPower(Power power) => ExecuteCcrModeCommand(() => SendCommand(CcdiCommand.SetPower(power)));
    public void SetRxCtcss(decimal hz) => ExecuteCcrModeCommand(() => SendCommand(CcdiCommand.SetCtcss(RxTx.Rx, hz)));
    public void SetTxCtcss(decimal hz) => ExecuteCcrModeCommand(() => SendCommand(CcdiCommand.SetCtcss(RxTx.Tx, hz)));
    public void SetVolume(byte level) => ExecuteCcrModeCommand(() => SendCommand(CcdiCommand.SetVolume(level)));
    public void SetMonitor(bool monitor) => ExecuteCcrModeCommand(() => SendCommand(CcdiCommand.SetMonitor(monitor)));

    private void ExecuteCcrModeCommand(Action action)
    {
        if (!IsCcrMode)
        {
            throw new InvalidOperationException("Radio must be in CCR mode to do this");
        }

        action();
    }

    private readonly Stopwatch transmittingFor = new();

    public bool IsCcrMode { get; private set; }

    public bool GoToChannel(int channel, string? zone = null)
    {
        if (IsCcrMode)
        {
            throw new InvalidOperationException("Radio must not be in CCR mode to do this");
        }

        lock (commandLock)
        {
            SendCommand(CcdiCommand.GoToChannel(channel, zone));

            if (!SpinWait.SpinUntil(() => GetCurrentChannel() == channel, TimeSpan.FromSeconds(1)))
            {
                return false;
            }
        }

        return true;
    }

    private int? channelUpdate;

    public int GetCurrentChannel() => GetCurrentChannel(CancellationToken.None);
    public int GetCurrentChannel(CancellationToken cancellationToken)
    {
        lock (commandLock)
        {
            SendFunctionCommand(0, 5, "2", cancellationToken);
            
            if (SpinWait.SpinUntil(() => channelUpdate != null || disconnectSignalled, TimeSpan.FromSeconds(0.1)))
            {
                int result = channelUpdate!.Value;
                channelUpdate = null;
                return result;
            }
        }

        logger?.LogError("Radio failed to return channel");
        return -1;
    }

    private void SendFunctionCommand(int function, int? subfunction, string? qualifier, CancellationToken cancellationToken)
    {
        if (IsCcrMode)
        {
            throw new InvalidOperationException("Radio must be not in CCR mode to do this");
        }
        
        SendCommand(CcdiCommand.Function(function, subfunction, qualifier), cancellationToken);
    }
}

public enum RxTx
{
    Rx = 'A', Tx = 'B'
}

public enum Power
{
    VeryLow = 1, Low, Medium, High
}

public enum ChannelBandwidth
{
    Narrow = 1, Medium, Wide
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

public class PaTempEventArgs(int readAdcValue) : EventArgs
{
    public double TempC
    {
        get
        {
            /*if (readTempC.HasValue)
            {
                return readTempC.Value;
            }
            else
            {*/
                return CalculateDegrees(readAdcValue);
            //}
        }
    }

    private static double CalculateDegrees(int milliVolts)
    {
        // set of actual values read from a TM8110:
        // 28, 468
        // 29, 467
        // 30, 464
        // 33, 458
        // 37, 450
        // 37, 448
        // 36, 452
        // 34, 455
        
        // m and c values from a linear regression of the above data
        var result = -0.463 * milliVolts + 244.81;
        return Math.Round(result, 1, MidpointRounding.AwayFromZero);
    }
}