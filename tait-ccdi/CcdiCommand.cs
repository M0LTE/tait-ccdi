namespace tait_ccdi;

public record struct CcdiCommand
{
    public char Ident { get; set; }
    public int Size { get; set; }
    public string? Parameters { get; set; }
    public required string Checksum { get; set; }

    public static bool TryParse(string command, out CcdiCommand ccdiCommand)
    {
        if (command.Length < 5)
        {
            ccdiCommand = default;
            return false;
        }

        ccdiCommand = new CcdiCommand
        {
            Checksum = command[^2..],
            Ident = command[0],
            Size = Convert.ToInt32(command.Substring(1, 2), 16)
        };

        if (ccdiCommand.Size > 0)
        {
            ccdiCommand.Parameters = command[3..^2];
        }

        return true;
    }

    public const int Terminator = 0x0d;
}

public record struct QueryCommand
{
    public QueryCommand(QueryType queryType) : this()
    {
        if (queryType == QueryType.Cctm_RawRssi)
        {
            Ident = 'q';
            Command = "5";
            Data = "064";
        }
        else if (queryType == QueryType.Cctm_AveragedRssi)
        {
            Ident = 'q';
            Command = "5";
            Data = "063";
        }
        else
        {
            throw new NotSupportedException($"Not supported: {queryType}");
        }

        Size = Command.Length + Data.Length;
        var commandWithoutChecksum = $"{Ident}{Size:00}{Command}{Data}";
        Checksum = CcdiChecksum.Calculate(commandWithoutChecksum);
    }

    public char Ident { get; set; }
    public int Size { get; set; }
    public string Command { get; set; }
    public string Data { get; set; }
    public string Checksum { get; set; }

    public readonly string ToCommand() => $"{Ident}{Size:00}{Command}{Data}{Checksum}";
}

public record struct QueryResponse
{
    public char Ident { get; set; }
    public int Size { get; set; }
    public string Command { get; set; }
    public string Data { get; set; }
    public string Checksum { get; set; }

    public readonly string ToCommand() => $"{Ident}{Size:00}{Command}{Data}{Checksum}";
}

public static class CcdiCommandExtensions
{
    public static QueryResponse AsQueryResponse(this CcdiCommand ccdiCommand) => new()
    {
        Ident = ccdiCommand.Ident,
        Size = ccdiCommand.Size,
        Command = ccdiCommand.Parameters![..3],
        Data = ccdiCommand.Parameters![3..],
        Checksum = ccdiCommand.Checksum
    };
}

public enum QueryType
{
    ModelAndCcdiVersion,
    QuerySdm,
    Version,
    SerialNumber,
    Cctm_PaTemperature,
    Cctm_AveragedRssi,
    Cctm_RawRssi,
    Cctm_ForwardPower,
    Cctm_ReversePower,
    Gps,
    Display
}