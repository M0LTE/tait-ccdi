using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;

namespace tait_ccdi;

public class TaitRadio
{
    private SerialPort serialPort;

    public TaitRadio(string comPort, int baud)
    {
        serialPort = new SerialPort(comPort, baud, Parity.None, 8, StopBits.One);
        serialPort.NewLine = "\r";
        serialPort.Open();
        _ = Task.Run(RunRadio);
    }

    private readonly BlockingCollection<QueryResponse> responses = new(new ConcurrentQueue<QueryResponse>());

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

                if (radioOutput.StartsWith('j') && CcdiCommand.TryParse(radioOutput, out var command))
                {
                    var response = command.AsQueryResponse();
                    response.RadioOutput = radioOutput;
                    responses.Add(response);
                }
                else if (radioOutput.StartsWith('p'))
                {
                    // ignore progress messages
                    continue;
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
                    Debugger.Break();
                }
            }
        }
    }

    private QueryResponse? WaitForResponse(char ident, string command, Func<string, bool>? validator = null)
    {
        while (!responses.IsCompleted)
        {
            var response = responses.Take();

            bool valid = validator == null || validator(response.Data);

            if (response.Ident == ident)
            {
                if (response.Command == command)
                {
                    if (valid)
                    {
                        return response;
                    }
                }
            }

            if (valid)
            {
                // put it back for the next guy
                responses.Add(response);
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
        var value = WaitForResponse('j', "064");
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
        var value = WaitForResponse('j', "063");
        return value == null ? default : double.Parse(value.Value.Data) / 10.0;
    }

    /// <summary>
    /// Forward power in ADC value out of 3000
    /// </summary>
    /// <returns></returns>
    public int GetForwardPower()
    {
        serialPort.WriteLine(QueryCommands.Cctm_ForwardPower);
        var value = WaitForResponse('j', "318");
        return value == null ? default : int.Parse(value.Value.Data);
    }

    /// <summary>
    /// Reverse power in ADC value out of 3000
    /// </summary>
    /// <returns></returns>
    public double GetReversePower()
    {
        serialPort.WriteLine(QueryCommands.Cctm_ReversePower);
        var value = WaitForResponse('j', "319");
        return value == null ? default : int.Parse(value.Value.Data);
    }

    /// <summary>
    /// PA temperature in degrees C
    /// </summary>
    /// <returns></returns>
    public double GetPaTemperature()
    {
        serialPort.WriteLine(QueryCommands.Cctm_PaTemperature);
        var value = WaitForResponse('j', "047", result => double.TryParse(result, out var i) && i < 400);
        var result = value == null ? default : double.Parse(value.Value.Data);
        return result;
    }

    /// <summary>
    /// Get the VSWR from the forward and reverse power. Assumes a linear relationship between the ADC value and actual power.
    /// </summary>
    /// <returns></returns>
    public double? GetVswr()
    {
        double forward = GetForwardPower();
        if (forward < 200)
        {
            // probably in receive mode. We could use progress messages to detect.
            return null;
        }
        double reverse = GetReversePower();
        double top = 1 + Math.Sqrt(reverse / forward);
        double bottom = 1 - Math.Sqrt(reverse / forward);
        double result = top / bottom;
        return result;
    }
}
