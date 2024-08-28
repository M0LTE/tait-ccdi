using FluentAssertions;
using System.Diagnostics;
using tait_ccdi;

namespace tests;

public class CcdiCommandTests
{
    [Fact]
    public void ManualExample()
    {
        CcdiCommand.TryParse("s0D050800TESTHi!DA", out var ccdiCommand).Should().BeTrue();
        ccdiCommand.Ident.Should().Be('s');
        ccdiCommand.Size.Should().Be(13);
        ccdiCommand.Parameters.Should().Be("050800TESTHi!");
        ccdiCommand.Checksum.Should().Be("DA");
    }

    [Fact]
    public void PaTempQuery()
    {
        CcdiCommand.TryParse("q0450475B", out var ccdiCommand).Should().BeTrue();
        ccdiCommand.Ident.Should().Be('q');
        ccdiCommand.Size.Should().Be(4);
        ccdiCommand.Parameters.Should().Be("5047");
        ccdiCommand.Checksum.Should().Be("5B");
    }

    [Fact]
    public void PaTempResponse()
    {
        CcdiCommand.TryParse("j050472331", out var ccdiCommand).Should().BeTrue();
        ccdiCommand.Ident.Should().Be('j');
        ccdiCommand.Size.Should().Be(5);
        ccdiCommand.Parameters.Should().Be("04723");
        ccdiCommand.Checksum.Should().Be("31");

        var queryResults = ccdiCommand.AsQueryResponse();
        queryResults.Command.Should().Be("047");
        queryResults.Data.Should().Be("23");
    }

    [Fact]
    public void PaTempResponse2()
    {
        CcdiCommand.TryParse("j06047467F4", out var ccdiCommand).Should().BeTrue();
        ccdiCommand.Ident.Should().Be('j');
        ccdiCommand.Size.Should().Be(6);
        ccdiCommand.Parameters.Should().Be("047467");
        ccdiCommand.Checksum.Should().Be("F4");

        var queryResults = ccdiCommand.AsQueryResponse();
        queryResults.Command.Should().Be("047");
        queryResults.Data.Should().Be("467");
    }

    [Fact]
    public void PowerResponse()
    {
        CcdiCommand.TryParse("j06047481F8", out var ccdiCommand).Should().BeTrue();
        ccdiCommand.Ident.Should().Be('j');
        ccdiCommand.Size.Should().Be(6);
        ccdiCommand.Parameters.Should().Be("047481");
        ccdiCommand.Checksum.Should().Be("F8");

        var queryResults = ccdiCommand.AsQueryResponse();
        queryResults.Command.Should().Be("047");
        queryResults.Data.Should().Be("481");
    }

    [Fact]
    public void GenerateAllCommands()
    {
        foreach (var queryType in Enum.GetValues<QueryType>())
        {
            QueryCommand queryCommand = new(queryType);
            string cmd = queryCommand.ToCommand();
            Debug.WriteLine($"{queryType}: {cmd}");
        }
    }

    [Fact]
    public void ValidateWeirdPaTemp() => CcdiChecksum.Validate("j06047469F2").Should().BeTrue();

    [Fact]
    public void ValidateNormalPaTemp() => CcdiChecksum.Validate("j05047282C").Should().BeTrue();

    [Fact]
    public void Progress()
    {
        CcdiCommand.TryParse("p0205C9", out var command).Should().BeTrue();
        var progressMessage = command.AsProgressMessage();
        progressMessage.ProgressType.Should().Be(ProgressType.ReceiverBusy);
        progressMessage.Checksum.Should().Be("C9");
        progressMessage.Size.Should().Be(2);
        progressMessage.Para1.Should().BeNull();
        progressMessage.Para2.Should().BeNull();
    }

    [Fact]
    public void InhibitExample()
    {
        CcdiCommand.TryParse("p0202CC", out var command).Should().BeTrue();
        var progressMessage = command.AsProgressMessage();
        progressMessage.ProgressType.Should().Be(ProgressType.TxInhibited);
        progressMessage.Checksum.Should().Be("CC");
        progressMessage.Size.Should().Be(2);
        progressMessage.Para1.Should().BeNull();
        progressMessage.Para2.Should().BeNull();
    }

    [Fact]
    public void PType21()
    {
        var command = CcdiCommand.FromParts('p', "21" + "9" + "01" + "2345");
        var progressMessage = command.AsProgressMessage();
        progressMessage.ProgressType.Should().Be(ProgressType.UserInitiatedChannelChange);
        progressMessage.Checksum.Should().Be("5C");
        progressMessage.Size.Should().Be(9);
        progressMessage.Para1.Should().Be("9");
        progressMessage.Para2.Should().Be("012345");
    }

    [Fact]
    public void PType21Short1()
    {
        CcdiCommand.TryParse("p 04 21 01 68", out var command).Should().BeTrue();
        var progressMessage = command.AsProgressMessage();
        progressMessage.ProgressType.Should().Be(ProgressType.UserInitiatedChannelChange);
        progressMessage.Checksum.Should().Be("68");
        progressMessage.Size.Should().Be(4);
        int.Parse(progressMessage.Para1).Should().Be(1);
        progressMessage.Para2.Should().BeNull();
    }

    [Fact]
    public void PType21Short2()
    {
        CcdiCommand.TryParse("p 04 21 02 67", out var command).Should().BeTrue();
        var progressMessage = command.AsProgressMessage();
        progressMessage.ProgressType.Should().Be(ProgressType.UserInitiatedChannelChange);
        progressMessage.Checksum.Should().Be("67");
        progressMessage.Size.Should().Be(4);
        int.Parse(progressMessage.Para1).Should().Be(2);
        progressMessage.Para2.Should().BeNull();
    }

    [Fact]
    public void PType21Short12()
    {
        var command = CcdiCommand.FromParts('p', "21 12");
        var progressMessage = command.AsProgressMessage();
        progressMessage.ProgressType.Should().Be(ProgressType.UserInitiatedChannelChange);
        progressMessage.Checksum.Should().Be("66");
        progressMessage.Size.Should().Be(4);
        int.Parse(progressMessage.Para1).Should().Be(12);
        progressMessage.Para2.Should().BeNull();
    }

    [Fact]
    public void PType22()
    {
        var command = CcdiCommand.FromParts('p', "22" + "EF" + "0D");
        var progressMessage = command.AsProgressMessage();
        progressMessage.ProgressType.Should().Be(ProgressType.TdmaChannelId);
        progressMessage.Checksum.Should().Be("C7");
        progressMessage.Size.Should().Be(6);
        progressMessage.Para1.Should().Be("EF");
        progressMessage.Para2.Should().Be("0D");
    }

    [Theory]
    [InlineData(ProgressType.SelcallAutoAcknowledge, "1", "88")]
    [InlineData(ProgressType.SdmAutoAcknowledge, "1", "87")]
    [InlineData(ProgressType.SdmGpsDataReceived, "1", "86")]
    [InlineData(ProgressType.RadioRestarted, "1", "85")]
    [InlineData(ProgressType.RadioRestarted, "3", "83")]
    public void PType1C_D_E_F(ProgressType progressType, string para1, string sum)
    {
        var ptype = Convert.ToHexString(new[] { (byte)progressType });
        var command = CcdiCommand.FromParts('p', ptype + para1);
        var progressMessage = command.AsProgressMessage();
        progressMessage.ProgressType.Should().Be(progressType);
        progressMessage.Checksum.Should().Be(sum);
        progressMessage.Size.Should().Be((ptype + para1).Length);
        progressMessage.Para1.Should().Be(para1);
        progressMessage.Para2.Should().BeNull();
    }

    [Theory]
    [InlineData(ProgressType.CallAnswered, "CE")]
    [InlineData(ProgressType.DeferredCalling, "CD")]
    [InlineData(ProgressType.TxInhibited, "CC")]
    [InlineData(ProgressType.EmergencyModeInitiated, "CB")]
    [InlineData(ProgressType.EmergencyModeTerminated, "CA")]
    [InlineData(ProgressType.ReceiverBusy, "C9")]
    [InlineData(ProgressType.ReceiverNotBusy, "C8")]
    [InlineData(ProgressType.PttMicActivated, "C7")]
    [InlineData(ProgressType.PttMicDeactivated, "C6")]
    [InlineData(ProgressType.SelcallRetry, "C7") ]
    [InlineData(ProgressType.RadioStunned, "C6")]
    [InlineData(ProgressType.RadioRevived, "C5")]
    [InlineData(ProgressType.FfskDataReceived, "C4")]
    [InlineData(ProgressType.SingleInBandToneReceived, "CC")]
    public void OtherPtypes(ProgressType progressType, string sum)
    {
        var command = CcdiCommand.FromParts('p', Convert.ToHexString(new[] { (byte)progressType }));
        var progressMessage = command.AsProgressMessage();
        progressMessage.ProgressType.Should().Be(progressType);
        progressMessage.Checksum.Should().Be(sum);
        progressMessage.Size.Should().Be(2);
        progressMessage.Para1.Should().BeNull();
        progressMessage.Para2.Should().BeNull();
    }

    [Fact]
    public void Volume0() => CcdiCommand.SetVolume(0).ToString().Should().Be("J03000C3");

    [Fact]
    public void Volume104() => CcdiCommand.SetVolume(104).ToString().Should().Be("J03104BE");

    [Fact]
    public void SetCtcssRx67() => CcdiCommand.SetCtcss(RxTx.Rx, 67).ToString().Should().Be("A0406708E");

    [Fact]
    public void SetCtcssRx0() => CcdiCommand.SetCtcss(RxTx.Rx, 0).ToString().Should().Be("A0400009B");

    [Fact]
    public void SetCtcssRx0Null() => CcdiCommand.SetCtcss(RxTx.Rx, null).ToString().Should().Be("A0400009B");

    [Fact]
    public void SetCtcssTx67() => CcdiCommand.SetCtcss(RxTx.Tx, 67).ToString().Should().Be("B0406708D");

    [Fact]
    public void SetCtcssTx0() => CcdiCommand.SetCtcss(RxTx.Tx, 0).ToString().Should().Be("B0400009A");

    [Fact]
    public void SetCtcssTx0Null() => CcdiCommand.SetCtcss(RxTx.Tx, null).ToString().Should().Be("B0400009A");

    [Fact]
    public void PowerVeryLow() => CcdiCommand.SetPower(Power.VeryLow).ToString().Should().Be("P0111E");

    [Fact]
    public void PowerHigh() => CcdiCommand.SetPower(Power.High).ToString().Should().Be("P0141B");

    [Fact]
    public void BandwidthWide() => CcdiCommand.SetBandwidth(ChannelBandwidth.Wide).ToString().Should().Be("H01324");

    [Fact]
    public void BandwidthNarrow() => CcdiCommand.SetBandwidth(ChannelBandwidth.Narrow).ToString().Should().Be("H01126");

    [Fact]
    public void SetRxFreq() => CcdiCommand.SetRxFreq(453200000).ToString().Should().Be("R0945320000087");

    [Fact]
    public void SetTxFreq() => CcdiCommand.SetTxFreq(453200000).ToString().Should().Be("T0945320000085");

    [Fact]
    public void MonitorOn() => CcdiCommand.SetMonitor(true).ToString().Should().Be("M01D0E");

    [Fact]
    public void MonitorOff() => CcdiCommand.SetMonitor(false).ToString().Should().Be("M01E0D");

    [Theory]
    [InlineData(null, 23, "g0223D2")]
    [InlineData(null, 1499, "g0414995E")]
    [InlineData("01", 12, "g060100120F")]
    public void GoToChannelAndZone(string? zone, int channel, string command)
    {
        CcdiCommand.GoToChannel(channel, zone).ToString().Should().Be(command);
    }

    [Fact]
    public void GoToChannelParses()
    {
        CcdiCommand.TryParse("g0223D2", out var command).Should().BeTrue();
        command.Ident.Should().Be('g');
        command.Size.Should().Be(2);
        command.Parameters.Should().Be("23");
        command.Checksum.Should().Be("D2");
    }

    [Theory]
    [InlineData(9, null, "1", "f0291CE")]
    [InlineData(5, null, "0", "f0250D3")]
    [InlineData(0, 2, "25", "f0402256D")]
    [InlineData(0, 5, "1", "f03051A1")]
    [InlineData(0, 5, "2", "f03052A0")]
    public void Functions(int command, int? subCommand, string? qualifier, string expected) 
        => CcdiCommand.Function(command, subCommand, qualifier).ToString().Should().Be(expected);

}