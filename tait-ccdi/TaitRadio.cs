using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;

namespace tait_ccdi;

public class TaitRadio
{
    public RadioState State { get; private set; }

    private readonly SerialPort serialPort;

    public TaitRadio(string comPort, int baud)
    {
        serialPort = new SerialPort(comPort, baud, Parity.None, 8, StopBits.One);
        serialPort.NewLine = "\r";
        serialPort.Open();
        _ = Task.Run(RunRadio);
    }

    private readonly BlockingCollection<QueryResponse> queryResponses = new(new ConcurrentQueue<QueryResponse>());

    public event EventHandler<ProgressMessageEventArgs>? ProgressMessageReceived;
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<RssiEventArgs>? RawRssiUpdated;
    public event EventHandler<VswrEventArgs>? VswrChanged;

    private void RunRadio()
    {
        List<char> buffer = [];

        while (true)
        {
            var i = serialPort.ReadByte();

            if (i == -1 || i == 254)
            {
                Console.WriteLine("Quitting");
                return;
            }

            byte b = (byte)i;

            if (b == '.')
            {
                // radio signals the end of a message with a period
                var radioOutput = new string(buffer.ToArray());
                buffer.Clear();
                if (string.IsNullOrWhiteSpace(radioOutput))
                {
                    continue;
                }

                if (radioOutput.StartsWith('j') && CcdiCommand.TryParse(radioOutput, out var queryResponseCommand))
                {
                    var queryResponse = queryResponseCommand.AsQueryResponse();
                    queryResponse.RadioOutput = radioOutput;
                    queryResponses.Add(queryResponse);
                }
                else if (radioOutput.StartsWith('p') && CcdiCommand.TryParse(radioOutput, out var progressCommand))
                {
                    var progressMessage = progressCommand.AsProgressMessage();
                    progressMessage.RadioOutput = radioOutput;
                    HandleProgressMessage(progressMessage);
                }
                else if (radioOutput.StartsWith('e'))
                {
                    // ignore error messages
                    continue;
                }
                else
                {
                    Debugger.Break();
                }
            }
            else
            {
                if (b >= 33 && b <= 126)
                {
                    // write printable ascii to the buffer
                    buffer.Add((char)b);
                }
                else if (b != 13) // radio sends CRs but we don't care
                {
                    // something we didn't anticipate
                    //Debugger.Break();
                }
            }
        }
    }

    private void HandleProgressMessage(ProgressMessage progressMessage)
    {
        var oldState = State;

        if (progressMessage.ProgressType == ProgressType.ReceiverBusy)
        {
            State = RadioState.HearingSignal;
        }
        else if (progressMessage.ProgressType == ProgressType.ReceiverNotBusy)
        {
            State = RadioState.HearingNoise;
        }
        else if (progressMessage.ProgressType == ProgressType.PttMicActivated)
        {
            State = RadioState.Transmitting;
        }
        else if (progressMessage.ProgressType == ProgressType.PttMicDeactivated)
        {
            State = RadioState.HearingNoise;
        }

        if (oldState != State)
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, State));
            if (State == RadioState.Transmitting)
            {
                StartGetVswr();
            }
            else if (oldState == RadioState.Transmitting && State != RadioState.Transmitting)
            {
                stopGettingVswr = true;
            }
        }

        ProgressMessageReceived?.Invoke(this, new ProgressMessageEventArgs(progressMessage));
    }

    bool stopGettingVswr;

    private void StartGetVswr()
    {
        _ = Task.Run(() =>
        {
            while (!stopGettingVswr)
            {
                var vswr = GetVswr();
                if (vswr != null && vswr > 1)
                {
                    VswrChanged?.Invoke(this, new(vswr.Value));
                }
            }

            stopGettingVswr = false;
        });
    }

    private QueryResponse? WaitForQueryResponse(string command, Func<string, bool>? validator = null)
    {
        while (!queryResponses.IsCompleted)
        {
            var response = queryResponses.Take();

            bool valid = validator == null || validator(response.Data);

            if (response.Command == command)
            {
                if (valid)
                {
                    return response;
                }
            }

            if (valid)
            {
                // put it back for the next guy
                queryResponses.Add(response);
            }
        }

        return null;
    }

    /// <summary>
    /// Raw received signal strength in dBm
    /// </summary>
    /// <returns></returns>
    public double GetRawRssi()
    {
        serialPort.WriteLine(QueryCommands.Cctm_RawRssi);
        var value = WaitForQueryResponse("064");
        return value == null ? default : double.Parse(value.Value.Data) / 10.0;
    }

    /// <summary>
    /// Averaged received signal strength in dBm
    /// Period unknown
    /// </summary>
    /// <returns></returns>
    public double GetAveragedRssi()
    {
        serialPort.WriteLine(QueryCommands.Cctm_AveragedRssi);
        var value = WaitForQueryResponse("063");
        return value == null ? default : double.Parse(value.Value.Data) / 10.0;
    }

    /// <summary>
    /// Forward power in mV, from 0 to 1200mV
    /// </summary>
    /// <returns></returns>
    public int GetForwardVoltage()
    {
        serialPort.WriteLine(QueryCommands.Cctm_ForwardPower);
        var value = WaitForQueryResponse("318");
        return value == null ? default : int.Parse(value.Value.Data);
    }

    /// <summary>
    /// Reverse power in mV, from 0 to 1200mV
    /// </summary>
    /// <returns></returns>
    public double GetReverseVoltage()
    {
        serialPort.WriteLine(QueryCommands.Cctm_ReversePower);
        var value = WaitForQueryResponse("319");
        return value == null ? default : int.Parse(value.Value.Data);
    }

    /// <summary>
    /// PA temperature in degrees C
    /// </summary>
    /// <returns></returns>
    public double GetPaTemperature()
    {
        serialPort.WriteLine(QueryCommands.Cctm_PaTemperature);
        var value = WaitForQueryResponse("047", result => double.TryParse(result, out var i) && i < 100);
        var result = value == null ? default : double.Parse(value.Value.Data);
        return result;
    }

    /// <summary>
    /// Get the VSWR from the forward and reverse voltage.
    /// </summary>
    /// <returns></returns>
    public double? GetVswr()
    {
        if (State != RadioState.Transmitting)
        {
            return default;
        }

        double forward = GetForwardVoltage();
        double reverse = GetReverseVoltage();
        double top = forward + reverse;
        double bottom = forward - reverse;
        double result = top / bottom;
        return result;
    }

    public void StartGetRawRssi()
    {
        _ = Task.Run(() =>
        {
            while (true)
            {
                var rssi = GetRawRssi();
                RawRssiUpdated?.Invoke(this, new RssiEventArgs(rssi));
            }
        });
    }
}

public enum RadioState
{
    HearingNoise, HearingSignal, Transmitting
}