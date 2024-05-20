using System.Diagnostics;
using System.IO.Ports;
using System.Text;

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

    private void RunRadio()
    {
        List<char> buffer = [];

        while (true)
        {
            var i = serialPort.ReadByte();

            if (i == -1)
            {
                Console.WriteLine("Quitting");
                return;
            }

            byte b = (byte)i;

            if (b == '.')
            {
                var sb = new StringBuilder();
                sb.Append(buffer.ToArray());
                var output = sb.ToString();
                buffer.Clear();
                if (string.IsNullOrWhiteSpace(output))
                {
                    continue;
                }

                if (output.StartsWith("j") && CcdiCommand.TryParse(output, out var command))
                {
                    var response = command.AsQueryResponse();

                    if (response.Command == "064")
                    {
                        rawRssi = int.Parse(response.Data) / 10.0;
                    }
                    else if (response.Command == "063")
                    {
                        averagedRssi = int.Parse(response.Data) / 10.0;
                    }
                }
                else if (output.StartsWith("p"))
                {
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
                    buffer.Add((char)b);
                }
                else if (b != 13)
                {
                    Debugger.Break();
                }
            }
        }
    }

    double? rawRssi;
    double? averagedRssi;

    public async Task<double> GetRawRssi()
    {
        var command = new QueryCommand(QueryType.Cctm_RawRssi);
        var cmd = command.ToCommand();
        serialPort.WriteLine(cmd);

        while (rawRssi == null)
        {
            await Task.Delay(1);
        }
        double val = rawRssi.Value;
        rawRssi = null;
        return val;
    }

    public async Task<double> GetAveragedRssi()
    {
        var command = new QueryCommand(QueryType.Cctm_AveragedRssi);
        var cmd = command.ToCommand();
        serialPort.WriteLine(cmd);

        while (averagedRssi == null)
        {
            await Task.Delay(1);
        }
        double val = averagedRssi.Value;
        averagedRssi = null;
        return val;
    }
}