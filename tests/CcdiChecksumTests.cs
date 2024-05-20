using FluentAssertions;
using tait_ccdi;

namespace tests;

public class CcdiChecksumTests
{
    [Theory]
    [InlineData("s0D050800TESTHi!", "DA")]
    [InlineData("q010", "FE")]
    [InlineData("q00", "2F")]
    [InlineData("q011", "FD")]
    [InlineData("q013", "FB")]
    public void Calculate(string commandWithoutChecksum, string checksum)
        => CcdiChecksum.Calculate(commandWithoutChecksum).Should().Be(checksum);

    [Fact]
    public void Validate() => CcdiChecksum.Validate("q002F").Should().BeTrue();

    [Fact]
    public void Validate_Lowercase() => CcdiChecksum.Validate("q002f").Should().BeTrue();

    [Fact]
    public void Validate_False() => CcdiChecksum.Validate("q002E").Should().BeFalse();
}
