using FluentAssertions;
using tait_ccdi;

namespace tests;

public class PaTempTests
{
    [Theory]
    [InlineData(28.1, 468)]
    [InlineData(28.6, 467)]
    [InlineData(30.0, 464)]
    [InlineData(32.8, 458)]
    [InlineData(36.5, 450)]
    [InlineData(37.4, 448)]
    [InlineData(35.5, 452)]
    [InlineData(34.1, 455)]
    public void UsingFitValues(double degrees, int adcMillivolts)
    {
        // set of actual values read from a TM8110:
        // 28, 468
        // 29, 467
        // 30, 464
        // 33, 458
        // 37, 450
        // 37, 448
        // 36, 452
        // 34, 455

        PaTempEventArgs args = new(adcMillivolts);
        args.TempC.Should().Be(degrees);
    }

    [Theory]
    [InlineData(28, 468)]
    [InlineData(29, 467)]
    [InlineData(30, 464)]
    [InlineData(33, 458)]
    [InlineData(37, 450)]
    [InlineData(37, 448)]
    [InlineData(36, 452)]
    [InlineData(34, 455)]
    public void UsingRealValues(double degrees, int adcMillivolts)
    {
        PaTempEventArgs args = new(adcMillivolts);
        Math.Round(args.TempC, 0, MidpointRounding.AwayFromZero).Should().Be(degrees);
    }
}
