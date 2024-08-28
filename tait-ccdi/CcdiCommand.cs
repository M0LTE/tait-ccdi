using System.ComponentModel.DataAnnotations;
using System;
using System.Net.NetworkInformation;
using System.Reflection.Metadata;
using System.Threading.Channels;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace tait_ccdi;

// see https://wiki.oarc.uk/_media/radios:tm8100-protocol-manual.pdf

public record struct CcdiCommand
{
    public char Ident { get; set; }
    public int Size { get; set; }
    /// <summary>
    /// Excludes checksum
    /// </summary>
    public string? Parameters { get; set; }
    public required string Checksum { get; set; }

    public override string ToString() => $"{Ident}{Size:00}{Parameters}{Checksum}";

    public static bool TryParse(string cmd, out CcdiCommand ccdiCommand)
    {
        var command = cmd.Replace(" ", "");

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

    public static CcdiCommand FromParts(char ident, string para)
    {
        var parameters = para.Replace(" ", "");
        var size = parameters.Length;
        var messageWithoutChecksum = $"{ident}{Hex(size)}{parameters}";
        var sum = CcdiChecksum.Calculate(messageWithoutChecksum);
        var withSum = $"{messageWithoutChecksum}{sum}";
        if (TryParse(withSum, out var ccdiCommand))
        {
            return ccdiCommand;
        }

        throw new Exception($"Internal error: failed to construct a valid CCDI command. Call: FromParts('{ident}', \"{parameters}\");");
    }

    private static string Hex(int size) => Convert.ToHexString(new[] { (byte)size });

    public static CcdiCommand SetCtcss(RxTx ab, decimal? hz)
    {
        var dhz = hz == null ? 0 : hz.Value * 10;
        var dHzstr = dhz.ToString("0000");

        if (hz > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(hz), "Invalid CTCSS frequency " + hz);
        }

        var cmd = FromParts((char)ab, dHzstr);
        
        return cmd;
    }

    public static CcdiCommand SetVolume(byte level)
    {
        var cmd = FromParts('J', level.ToString("000"));
        return cmd;
    }

    public static CcdiCommand SetPower(Power power)
    {
        var cmd = FromParts('P', ((int)power).ToString());
        return cmd;
    }

    public static CcdiCommand SetBandwidth(ChannelBandwidth bandwidth)
    {
        var cmd = FromParts('H', ((int)bandwidth).ToString());
        return cmd;
    }

    public static CcdiCommand SetMonitor(bool monitor)
    {
        var cmd = FromParts('M', (monitor ? 'D' : 'E').ToString());
        return cmd;
    }

    private static CcdiCommand SetFreq(char rt, int hz)
    {
        if (hz < 66000000 || hz > 520000000)
        {
            throw new ArgumentOutOfRangeException(nameof(hz), "Invalid frequency " + hz);
        }

        var freqStr = hz.ToString();
        var cmd = FromParts(rt, freqStr);
        return cmd;
    }

    public static CcdiCommand SetRxFreq(int hz) => SetFreq('R', hz);

    public static CcdiCommand SetTxFreq(int hz) => SetFreq('T', hz);

    /// <summary>
    /// The GO_TO_CHANNEL command tells the radio to change to another
    /// conventional mode channel.The specified channel can be assigned to a
    /// scan/vote group in the radio.A trunked radio must change to a conventional
    /// channel before executing this command.
    /// </summary>
    /// <param name="channel">The new channel number between 0 and 9999. This must
    /// be a valid channel for the radio.</param>
    /// <param name="zone">A two character string representing the new zone. When [ZONE] is omitted,
    /// the radio stays in the current zone. Optional for TM8200, not applicable for TM8100</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Zone is not null or two characters</exception>
    /// <exception cref="ArgumentOutOfRangeException">Channel is out of allowed range</exception>
    public static CcdiCommand GoToChannel(int channel, string? zone = null)
    {
        if (zone != null && zone.Length != 2)
        {
            throw new ArgumentException("Zone must be 2 characters", nameof(zone));
        }

        if (channel < 1 || channel > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid channel " + channel);
        }

        var formattedChannel = zone == null ? channel.ToString() : channel.ToString("0000");

        var cmd = FromParts('g', (zone ?? "") + formattedChannel);

        return cmd;
    }

    public static CcdiCommand Function(int function, int? subfunction, string? qualifier)
    {
        if (function < 0 || function > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(function), "Invalid function " + function);
        }

        if (subfunction != null && (subfunction < 0 || subfunction > 99))
        {
            throw new ArgumentOutOfRangeException(nameof(subfunction), "Invalid subfunction " + subfunction);
        }

        return FromParts('f', $"{function}{subfunction}{qualifier}");
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
    public int Size { get; set; }
    public string Command { get; set; }
    public string Data { get; set; }
    public string Checksum { get; set; }
    public string RadioOutput { get; set; }

    public readonly string ToCommand() => $"q{Size:00}{Command}{Data}{Checksum}";
}

public record struct ProgressMessage
{
    public int Size { get; set; }
    public ProgressType ProgressType { get; set; }
    public string Para1 { get; set; }
    /// <summary>
    /// Appended if PTYPE is 21, 22, or 23
    /// </summary>
    public string? Para2 { get; set; }
    public string Checksum { get; set; }
    public string RadioOutput { get; set; }

    public readonly string ToCommand() => $"q{Size:00}{Convert.ToHexString(new[] { (byte)ProgressType })}{Para1}{Para2}{Checksum}";
    public override readonly string ToString() => $"{ProgressType} {Para1} {Para2}   [{RadioOutput}]";
}

public record struct ErrorMessage
{
    public ErrorCategory Category { get; set; }
    public TransactionError? TransactionError { get; set; }
}

public enum TransactionError
{
    UnsupportedCommand = 1,
    ChecksumError = 2,
    ParameterError = 3,
    NotReadyError = 5,
    CommandError = 6
}

public enum ErrorCategory
{
    TransactionError = 0, SystemError = 1
}

public enum ProgressType
{
    CallAnswered = 0x00,
    DeferredCalling = 0x01,
    TxInhibited = 0x02,
    EmergencyModeInitiated = 0x03,
    EmergencyModeTerminated = 0x04,
    /// <summary>
    /// Signal is being received
    /// </summary>
    ReceiverBusy = 0x05,
    /// <summary>
    /// No signal is being received
    /// </summary>
    ReceiverNotBusy = 0x06,
    /// <summary>
    /// Radio has started transmit
    /// </summary>
    PttMicActivated = 0x07,
    /// <summary>
    /// Radio has ended transmit
    /// </summary>
    PttMicDeactivated = 0x08,
    SelcallRetry = 0x16,
    RadioStunned = 0x17,
    RadioRevived = 0x18,
    FfskDataReceived = 0x19,
    SelcallAutoAcknowledge = 0x1c,
    SdmAutoAcknowledge = 0x1d,
    SdmGpsDataReceived = 0x1e,
    RadioRestarted = 0x1f,
    SingleInBandToneReceived=0x20,
    UserInitiatedChannelChange=0x21,
    TdmaChannelId = 0x22,
    KeyCode = 0x23
}

public static class CcdiCommandExtensions
{
    public static QueryResponse AsQueryResponse(this CcdiCommand ccdiCommand) => new()
    {
        Size = ccdiCommand.Size,
        Command = ccdiCommand.Parameters![..3],
        Data = ccdiCommand.Parameters![3..],
        Checksum = ccdiCommand.Checksum
    };

    public static ErrorMessage AsErrorMessage(this CcdiCommand ccdiCommand)
    {
        var obj = new ErrorMessage
        {
            Category = (ErrorCategory)int.Parse(ccdiCommand.Parameters![..1]),
        };

        if (obj.Category == ErrorCategory.TransactionError && ccdiCommand.Parameters!.Length > 1)
        {
            obj.TransactionError = (TransactionError)int.Parse(ccdiCommand.Parameters![1..]);
        }

        return obj;
    }

    public static ProgressMessage AsProgressMessage(this CcdiCommand ccdiCommand)
    {
        var result = new ProgressMessage
        {
            Size = ccdiCommand.Size,
            Checksum = ccdiCommand.Checksum,
            ProgressType = (ProgressType)Convert.FromHexString(ccdiCommand.Parameters![..2])[0]
        };

        if (result.ProgressType == ProgressType.SelcallAutoAcknowledge
            || result.ProgressType == ProgressType.SdmAutoAcknowledge
            || result.ProgressType == ProgressType.SdmGpsDataReceived
            || result.ProgressType == ProgressType.RadioRestarted)
        {
            result.Para1 = ccdiCommand.Parameters![2..];
        }
        else if (result.ProgressType == ProgressType.UserInitiatedChannelChange)
        {
            if (ccdiCommand.Parameters!.Length == 4)
            {
                result.Para1 = ccdiCommand.Parameters![2..];
            }
            else
            {
                // 0 is single channel, 1 is scan/vote group of channels, 2 is a channel captured within a scan/vote group,
                // 3 is a temporary channel, e.g. one used for GPS, 9 is the channel is not available or invalid
                result.Para1 = ccdiCommand.Parameters!.Substring(ccdiCommand.Parameters!.Length - 7, 1);
                // [PARA2] is a fixed length field of 6-digits which indicate zone (2 digits) and the channel
                // or scan/ vote group ID(4 digits).
                result.Para2 = ccdiCommand.Parameters!.Substring(ccdiCommand.Parameters!.Length - 6, 6);
            }
        }
        else if (result.ProgressType == ProgressType.TdmaChannelId)
        {
            result.Para1 = ccdiCommand.Parameters!.Substring(ccdiCommand.Parameters!.Length - 4, 2);
            // [PARA2]: 2-digit hexadecimal number with various TDMA states
            result.Para2 = ccdiCommand.Parameters!.Substring(ccdiCommand.Parameters!.Length - 2, 2);
        }
        else if (result.ProgressType == ProgressType.KeyCode)
        {
            result.Para1 = ccdiCommand.Parameters!.Substring(ccdiCommand.Parameters!.Length - 3, 2);
            // [PARA2]: [PARA2]: 0 = key down action, 1 = key up action, 2 = short keypress, 3 = long keypress
            result.Para2 = ccdiCommand.Parameters!.Substring(ccdiCommand.Parameters!.Length - 1, 1);
        }

        return result;
    }
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