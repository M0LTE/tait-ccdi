using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Ports;

namespace tait_ccdi;

public class TaitRadio
{
    public TaitRadio(string comPort, int baud, ILogger logger)
    {
        this.comPort = comPort;
        this.baud = baud;
        this.logger = logger;
        var thread = new Thread(RunSerialPort);
        thread.IsBackground = true;
        thread.Start();
    }

    public RadioState State { get; private set; }

    private readonly List<QueryResponse> queryResponses = new();
    private readonly string comPort;
    private readonly int baud;
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
            logger.LogInformation("Opening serial port " + comPort);

            using var serialPort = new SerialPort(comPort, baud, Parity.None, 8, StopBits.One);
            serialPort.NewLine = "\r";
            try
            {
                serialPort.Open();
            }
            catch (Exception)
            {
                logger.LogError("Failed to open serial port " + comPort);
                Thread.Sleep(5000);
                continue;
            }

            logger.LogInformation("Opened serial port " + comPort);

            string modelAndCcdiVersion;

            while (!TryDetectTait(serialPort, out modelAndCcdiVersion))
            {
                logger.LogInformation("Looking for a Tait radio...");
            }

            logger.LogInformation("Found a Tait radio: " + modelAndCcdiVersion);

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

    private bool TryDetectTait(SerialPort serialPort, out string result)
    {
        serialPort.DiscardInBuffer();
        serialPort.DiscardOutBuffer();

        while (true)
        {
            serialPort.WriteLine(QueryCommands.ModelAndCcdiVersion);

            if (!serialPort.TryReadTo("\r.", out var output, TimeSpan.FromSeconds(1)))
            {
                logger.LogWarning("Timeout reading from serial port");
                result = "";
                return false;
            }

            if (output.StartsWith(".m"))
            {
                serialPort.ReadExisting();
                result = output[1..];
                return true;
            }
        }
    }

    private void RunRadio(SerialPort serialPort)
    {
        var tokenSource = new CancellationTokenSource();
        
        Thread thread = new(() => HandleDataFromRadio(serialPort, tokenSource));
        thread.IsBackground = true;
        thread.Start();

        HandleCommandsToRadio(serialPort, tokenSource);
    }

    private void HandleCommandsToRadio(SerialPort serialPort, CancellationTokenSource tokenSource)
    {
        Stopwatch paTempLastQueried = new();
        Stopwatch swrLastQueried = new();
        var within = TimeSpan.FromSeconds(1);

        while (!tokenSource.IsCancellationRequested)
        {
            if (State == RadioState.ReceivingNoise || State == RadioState.ReceivingSignal)
            {
                serialPort.WriteLine(QueryCommands.Cctm_RawRssi);
                ExpectQueryResponse(rawRssiQueryResponseCode, within);
            }
            else if (State == RadioState.Transmitting)
            {
                if (!swrLastQueried.IsRunning || swrLastQueried.Elapsed > TimeSpan.FromSeconds(0.25))
                {
                    serialPort.WriteLine(QueryCommands.Cctm_ForwardPower);
                    ExpectQueryResponse(forwardPowerQueryResponseCode, within);
                    serialPort.WriteLine(QueryCommands.Cctm_ReversePower);
                    ExpectQueryResponse(reversePowerQueryResponseCode, within);
                    swrLastQueried.Restart();
                }
            }

            if (!paTempLastQueried.IsRunning || paTempLastQueried.Elapsed > TimeSpan.FromSeconds(10))
            {
                serialPort.WriteLine(QueryCommands.Cctm_PaTemperature);
                ExpectQueryResponse(paTempQueryResponseCode, within);
                paTempLastQueried.Restart();
            }
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
                    logger.LogInformation("Binning unexpected response {responseCode}, waiting...", queryResponse.Command);
                    Thread.Sleep(10);
                    continue;
                }
            }
        }

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

    private void HandleDataFromRadio(SerialPort serialPort, CancellationTokenSource tokenSource)
    { 
        logger.LogInformation("Waiting for data...");

        while (!tokenSource.IsCancellationRequested)
        {
            if (!serialPort.TryReadTo("\r.", out var radioOutput, TimeSpan.FromSeconds(10)))
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
            }

            if (string.IsNullOrWhiteSpace(radioOutput))
            {
                logger.LogWarning("Radio returned nothing");
                continue;
            }

            while (radioOutput.StartsWith('.'))
            {
                radioOutput = radioOutput[1..];
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
                logger.LogWarning("Radio returned error {error}", radioOutput);
                continue;
            }
            else
            {
                Debugger.Break();
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