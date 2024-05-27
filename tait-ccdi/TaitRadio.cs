using System.Diagnostics;
using System.IO.Ports;
using System.Threading.Tasks.Dataflow;

namespace tait_ccdi;

public class TaitRadio
{
    private SerialPort serialPort;

    public TaitRadio(string comPort, int baud)
    {
        serialPort = new SerialPort(comPort, baud, Parity.None, 8, StopBits.One);
        serialPort.NewLine = "\r";
        serialPort.Open();
        queryResponseBlock = new ActionBlock<QueryResponse>(response => OnRawRssiResponse?.Invoke(response));
        _ = Task.Run(RunRadio);
    }

    private readonly ActionBlock<QueryResponse> queryResponseBlock;
    public Action<QueryResponse>? OnRawRssiResponse { get; set; }

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
                // radio signals the end of a message with a period
                var output = new string(buffer.ToArray());
                buffer.Clear();
                if (string.IsNullOrWhiteSpace(output))
                {
                    continue;
                }

                if (output.StartsWith('j') && CcdiCommand.TryParse(output, out var command))
                {
                    var response = command.AsQueryResponse();
                    if (!queryResponseBlock.Post(response))
                    {
                        throw new InvalidOperationException();
                    }
                }
                else if (output.StartsWith('p'))
                {
                    // ignore progress messages
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

    public void Send(string command)
    {
        serialPort.WriteLine(command);
    }
}
