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
}