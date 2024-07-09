using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;

namespace tait_ccdi;


// see https://wiki.oarc.uk/_media/radios:tm8100-protocol-manual.pdf

public interface ISerialPort
{
    int ReadByte();
    void WriteLine(string line);
    string ReadTo(string value);
    string? ReadExisting();
    TimeSpan ReadTimeout { get; set; }
}

public class TaitRadio
{
    public event EventHandler<ProgressMessageEventArgs>? ProgressMessageReceived;
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<RssiEventArgs>? RawRssiUpdated;
    public event EventHandler<VswrEventArgs>? VswrChanged;
    public event EventHandler<PaTempEventArgs>? PaTempRead;

    private readonly ISerialPort serialPort;
    private readonly ILogger logger;

    public TaitRadio(ISerialPort serialPort, ILogger logger)
    {
        this.serialPort = serialPort;
        this.logger = logger;
        var thread = new Thread(RunSerialPort);
        thread.IsBackground = true;
        thread.Start();
    }

    private enum ReadState
    {
        WaitingForPrompt,
        WaitingForDataOrCommand
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
            var response = ReadResponse('m', serialPort, TimeSpan.FromSeconds(1));
            if (string.IsNullOrWhiteSpace(response))
            {
                logger.LogInformation("Looking for radio...");
            }
            else
            {
                logger.LogInformation("Radio responded with {response}", response);
                break;
            }
        }
    }

    bool ready;

    private void RunSerialPort()
    {
        WaitForRadio();

        using Timer timer = new(SendGetTemperatureQuery, null, 0, 60000);

        while (true)
        {
            if (!serialPort.TryReadChar(TimeSpan.FromSeconds(10), out var c))
            {
                logger.LogInformation("No data read");
                continue;
            }

            ready = c == '.';

            if (c == '.')
            {
                continue;
            }
            else if (c == 'p' || c == 'e' || c == 'j')
            {
                var message = ReadResponse(serialPort, TimeSpan.FromSeconds(5));
                if (message == null)
                {
                    logger.LogError("Failed to read message {message} in 5s", c);
                }
                else
                {
                    var msg = c + message;
                    if (CcdiChecksum.Validate(msg))
                    {
                        logger.LogInformation("Got message {msg}", msg);
                    }
                    else
                    {
                        logger.LogError(msg + " has invalid checksum");
                        continue;
                    }

                    if (CcdiCommand.TryParse(msg, out var command))
                    {
                        if (command.Ident == 'j')
                        {
                            var response = command.AsQueryResponse();

                            if (response.Command == "047")
                            {
                                // this may result in two separate responses depending on radio model
                                paTempResponses.Add(int.Parse(response.Data));
                            }
                            else
                            {
                                logger.LogInformation($"query response: {response.Data}");
                            }
                        }
                        else if (command.Ident == 'p')
                        {
                            var progress = command.AsProgressMessage();
                            logger.LogInformation($"progress: {progress.ProgressType}");
                            HandleProgress(progress.ProgressType);
                        }
                        else if (command.Ident == 'e')
                        {
                            var error = command.AsErrorMessage();
                            logger.LogWarning($"error: {error.Category}");
                            HandleError(error);
                        }
                    }
                    else
                    {
                        logger.LogWarning("Could not parse {message} as CcdiCommand", msg);
                    }
                }
            }
            else
            {
                Debugger.Break();
            }
        }
    }

    private void HandleError(ErrorMessage error)
    {
    }

    private void HandleProgress(ProgressType progressType)
    {
    }

    List<int> paTempResponses = new();

    private static string? ReadResponse(ISerialPort serialPort, TimeSpan timeout)
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
        finally
        {
            serialPort.ReadTimeout = oldTimeout;
        }
    }

    private static string? ReadResponse(char expectedResponseType, ISerialPort serialPort, TimeSpan timeout)
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
        finally
        {
            serialPort.ReadTimeout = oldTimeout;
        }
    } 

    private object commandLock = new();

    private void SendGetTemperatureQuery(object? _)
    {
        lock (commandLock)
        {
            SpinWait.SpinUntil(() => ready);
            ready = false;
            serialPort.WriteLine(QueryCommands.Cctm_PaTemperature);
            // on the 8100 the PA temp query results in two completely separate responses
            // first one is temp in C
            // second one is ADC value in mV (0 to 1200)

            if (SpinWait.SpinUntil(() => paTempResponses.Count == 2, TimeSpan.FromMilliseconds(100)))
            {
                PaTempRead?.Invoke(this, new PaTempEventArgs(paTempResponses[0], paTempResponses[1]));
            }
            else if (paTempResponses.Count == 1)
            {
                PaTempRead?.Invoke(this, new PaTempEventArgs(null, paTempResponses[0]));
            }

            paTempResponses.Clear();
        }
    }
}

public class TaitRadio_old
{
    public TaitRadio_old(ISerialPort_old serialPort, ILogger logger)
    {
        this.serialPort = serialPort;
        this.logger = logger;
        var thread = new Thread(RunSerialPort);
        thread.IsBackground = true;
        thread.Start();
    }

    public RadioState State { get; private set; }

    private readonly List<QueryResponse> queryResponses = new();
    private readonly ISerialPort_old serialPort;
    private readonly ILogger logger;
    
    public event EventHandler<ProgressMessageEventArgs>? ProgressMessageReceived;
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<RssiEventArgs>? RawRssiUpdated;
    public event EventHandler<VswrEventArgs>? VswrChanged;

    private StreamWriter? logWriter;

    private void RunSerialPort()
    {
        if (Directory.Exists("radiolog"))
        {
            var key = Guid.NewGuid().ToString()[..4];
            var binaryFile = $"radiolog-{key}.bin";
            var textFile = $"radiolog-{key}.txt";
            var log = File.Open(Path.Combine("radiolog", textFile), FileMode.Create);
            logWriter = new StreamWriter(log);
            logWriter.AutoFlush = true;
            logger.LogInformation($"Writing radio bytes to {binaryFile} / {textFile}");
        }

        while (true)
        {
            logger.LogInformation("Opening serial port " + serialPort);

            try
            {
                serialPort.Open();
            }
            catch (Exception)
            {
                logger.LogError("Failed to open serial port " + serialPort);
                Thread.Sleep(5000);
                continue;
            }

            logger.LogInformation("Opened serial port " + serialPort);

            string modelAndCcdiVersion;

            try
            {
                RunRadio(serialPort);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"{ex.GetType().Name} in RunRadio()");
                Thread.Sleep(5000);
                continue;
            }
        }
    }

    private void RunRadio(ISerialPort_old serialPort)
    {
        var tokenSource = new CancellationTokenSource();
        
        Thread thread = new(() => HandleDataFromRadio(serialPort, tokenSource));
        thread.IsBackground = true;
        thread.Start();

        HandleCommandsToRadio(serialPort, tokenSource);
    }

    private void HandleCommandsToRadio(ISerialPort_old serialPort, CancellationTokenSource tokenSource)
    {
        Stopwatch paTempLastQueried = new();
        Stopwatch swrLastQueried = new();
        var within = TimeSpan.FromSeconds(1);

        while (!tokenSource.IsCancellationRequested)
        {
            if (State == RadioState.ReceivingNoise || State == RadioState.ReceivingSignal)
            {
                WaitUntilReady();
                lock (serialPort)
                {
                    isReady = false;
                    serialPort.WriteLine(QueryCommands.Cctm_RawRssi);
                    ExpectQueryResponse(rawRssiQueryResponseCode, within);
                }
            }
            else if (State == RadioState.Transmitting)
            {
                if (!swrLastQueried.IsRunning || swrLastQueried.Elapsed > TimeSpan.FromSeconds(0.25))
                {
                    WaitUntilReady();
                    lock (serialPort)
                    {
                        isReady = false;
                        serialPort.WriteLine(QueryCommands.Cctm_ForwardPower);
                        ExpectQueryResponse(forwardPowerQueryResponseCode, within);
                    }
                    WaitUntilReady();
                    lock (serialPort)
                    {
                        isReady = false;
                        serialPort.WriteLine(QueryCommands.Cctm_ReversePower);
                        ExpectQueryResponse(reversePowerQueryResponseCode, within);
                    }
                    swrLastQueried.Restart();
                }
            }

            if (!paTempLastQueried.IsRunning || paTempLastQueried.Elapsed > TimeSpan.FromSeconds(10))
            {
                WaitUntilReady();
                lock (serialPort)
                {
                    isReady = false;
                    serialPort.WriteLine(QueryCommands.Cctm_PaTemperature);
                    ExpectQueryResponse(paTempQueryResponseCode, within);
                }
                paTempLastQueried.Restart();
            }
        }
    }

    private void WaitUntilReady()
    {
        while (!isReady)
        {
            Thread.Sleep(5);
        }
    }

    const string paTempQueryResponseCode = "047";
    const string rawRssiQueryResponseCode = "064";
    const string forwardPowerQueryResponseCode = "318";
    const string reversePowerQueryResponseCode = "319";

    private bool ExpectQueryResponse(string responseCode, TimeSpan within)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < within)
        {
            lock (queryResponses)
            {
                var matchingResponses = queryResponses.Where(x => x.Command == responseCode);

                if (!matchingResponses.Any())
                {
                    Thread.Sleep(10);
                    continue;
                }

                var queryResponse = matchingResponses.First();

                if (queryResponse.Command == responseCode)
                {
                    queryResponses.Remove(queryResponse);
                    ProcessQueryResponse(queryResponse);
                    return true;
                }
                else
                {
                    logger.LogInformation("Unexpected response {responseCode}, waiting...", queryResponse.Command);
                    Thread.Sleep(10);
                    continue;
                }
            }
        }

        logger.LogWarning("Timed out waiting for response {responseCode}", responseCode);
        return false;
    }

    private void ProcessQueryResponse(QueryResponse queryResponse)
    {
        if (queryResponse.Command == paTempQueryResponseCode)
        {
            logger.LogInformation("PA temperature: {paTemp}", queryResponse.Data);
        }
        else if (queryResponse.Command == rawRssiQueryResponseCode)
        {
            var rssi = double.Parse(queryResponse.Data) / 10.0;
            RawRssiUpdated?.Invoke(this, new RssiEventArgs(rssi));
        }
        else if (queryResponse.Command == forwardPowerQueryResponseCode)
        {
            lastFwdPower = double.Parse(queryResponse.Data);
        }
        else if (queryResponse.Command == reversePowerQueryResponseCode)
        {
            var swr = GetVswr(lastFwdPower, double.Parse(queryResponse.Data));
            if (swr.HasValue && !double.IsNaN(swr.Value))
            {
                VswrChanged?.Invoke(this, new VswrEventArgs(swr.Value));
            }
        }
    }

    double lastFwdPower;
    bool isReady;

    private void HandleDataFromRadio(ISerialPort_old serialPort, CancellationTokenSource tokenSource)
    { 
        logger.LogInformation("Waiting for data...");

        while (!tokenSource.IsCancellationRequested)
        {
            /*if (!serialPort.TryReadTo("\r", out var radioOutput, TimeSpan.FromSeconds(10)))
            {
                if (TryDetectTait(serialPort, out _))
                {
                    logger.LogInformation("Radio is still there");
                    continue;
                }
                else
                {
                    logger.LogWarning("Radio has gone away");
                    tokenSource.Cancel();
                    return;
                }
            }*/

            string radioOutput = "";

            if (string.IsNullOrWhiteSpace(radioOutput))
            {
                logger.LogWarning("Radio returned nothing");
                continue;
            }

            while (radioOutput.StartsWith('.'))
            {
                radioOutput = radioOutput[1..];
                isReady = true;
            }

            if (radioOutput.StartsWith('j') && CcdiCommand.TryParse(radioOutput, out var queryResponseCommand))
            {
                var queryResponse = queryResponseCommand.AsQueryResponse();
                queryResponse.RadioOutput = radioOutput;
                lock (queryResponses)
                {
                    queryResponses.Add(queryResponse);
                }
            }
            else if (radioOutput.StartsWith('p') && CcdiCommand.TryParse(radioOutput, out var progressCommand))
            {
                var progressMessage = progressCommand.AsProgressMessage();
                progressMessage.RadioOutput = radioOutput;
                SetState(progressMessage.ProgressType);
            }
            else if (radioOutput.StartsWith('e'))
            {
                if (CcdiCommand.TryParse(radioOutput, out var errorCommand))
                {
                    var errorMessage = errorCommand.AsErrorMessage();
                    if (errorMessage.Category == ErrorCategory.TransactionError)
                    {
                        logger.LogWarning("Radio returned transaction error {transactionError}",
                            errorMessage.TransactionError.ToString());
                    }
                    else
                    {
                        logger.LogWarning("Radio returned system error: {radioOutput}", radioOutput);
                    }
                }
                else
                {
                    logger.LogWarning("Radio returned unknown error {error}", radioOutput);
                }
            }
            else
            {
                logger.LogError("Unexpected radio output: {radioOutput}", radioOutput);
            }
        }
    }

    private void SetState(ProgressType type)
    {
        var oldState = State;

        if (type == ProgressType.PttMicActivated)
        {
            State = RadioState.Transmitting;
        }
        else if (type == ProgressType.PttMicDeactivated)
        {
            State = RadioState.ReceivingNoise;
        }
        else if (type == ProgressType.ReceiverBusy)
        {
            State = RadioState.ReceivingSignal;
        }
        else if (type == ProgressType.ReceiverNotBusy)
        {
            State = RadioState.ReceivingNoise;
        }

        if (oldState != State)
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, State));
        }
    }

    // raw rssi 064
    // averaged rssi 063
    // forward power 318
    // reverse power 319
    // pa temperature 047

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
}

public enum RadioState
{
    ReceivingNoise, ReceivingSignal, Transmitting
}

internal static class ExtensionMethods
{
    public static string ToHex(this int b)
    {
        var hex = b.ToString("X").ToLower();
        if (hex.Length == 1)
        {
            hex = "0" + hex;
        }
        return hex;
    }

    public static bool TryReadChar(this ISerialPort serialPort, TimeSpan timeout, out char value)
    {
        var oldTimeout = serialPort.ReadTimeout;
        serialPort.ReadTimeout = timeout;
        try
        {
            value = (char)serialPort.ReadByte();
            return true; 
        }
        catch (TimeoutException)
        {
            value = default;
            return false;
        }
        finally
        {
            serialPort.ReadTimeout = oldTimeout;
        }
    }
}