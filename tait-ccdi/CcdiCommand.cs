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

/// <summary>
/// While we can construct each command every time, the values are always the same, so there is no need.
/// </summary>
public static class QueryCommands
{
    public const string ModelAndCcdiVersion = "q010FE";
    public const string QuerySdm = "q011FD";
    public const string Version = "q013FB";
    public const string SerialNumber = "q014FA";
    public const string Cctm_PaTemperature = "q0450475B";
    public const string Cctm_AveragedRssi = "q0450635D";
    public const string Cctm_RawRssi = "q0450645C";
    public const string Cctm_ForwardPower = "q0453185A";
    public const string Cctm_ReversePower = "q04531959";
    public const string Gps = "q016F8";
    public const string Display = "q0270C6";
}

public record struct QueryCommand
{
    public QueryCommand(QueryType queryType) : this()
    {
        Ident = 'q';

        if (queryType == QueryType.Cctm_RawRssi)
        {
            Command = "5";
            Data = "064";
        }
        else if (queryType == QueryType.Cctm_AveragedRssi)
        {
            Command = "5";
            Data = "063";
        }
        else if (queryType == QueryType.Cctm_PaTemperature)
        {
            Command = "5";
            Data = "047";
        }
        else if (queryType == QueryType.Cctm_ForwardPower)
        {
            Command = "5";
            Data = "318";
        }
        else if (queryType == QueryType.Cctm_ReversePower)
        {
            Command = "5";
            Data = "319";
        }
        else if (queryType == QueryType.ModelAndCcdiVersion)
        {
            Command = "0";
            Data = "";
            // returns a MODEL message
        }
        else if (queryType == QueryType.QuerySdm)
        {
            Command = "1";
            Data = "";
            // returns a GET_SDM message
        }
        else if (queryType == QueryType.Version)
        {
            Command = "3";
            Data = "";
            // returns a RADIO_VERSION message
        }
        else if (queryType == QueryType.SerialNumber)
        {
            Command = "4";
            Data = "";
            // returns a RADIO_SERIAL message
        }
        else if (queryType == QueryType.Gps)
        {
            Command = "6";
            Data = "";
        }
        else if (queryType == QueryType.Display)
        {
            Command = "7";
            Data = "0";
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
    /// <summary>
    /// Query GPS. GPS data is returned packetised as though the TM8100/TM8200 is a polling radio.
    /// TM8100: from v2.10
    /// TM8200: No
    /// </summary>
    Gps,
    /// <summary>
    /// Returns the text of the entire display. Non-ASCII text is ignored.
    /// TM8100: No
    /// TM8200: from v3.03
    /// </summary>
    Display
}