using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class EgrCommandTests
{
    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0xFF, 100.0)]
    [InlineData(0x80, 50.196)]
    public void CommandedEgr_ScalesToPercent(byte a, double expected)
        => Assert.Equal(expected, new CommandedEgrCommand().Parse([0x41, 0x2C, a]), 3);

    [Theory]
    [InlineData(0x80, 0.0)]      // 128 is zero error
    [InlineData(0x00, -100.0)]
    [InlineData(0xFF, 99.219)]
    public void EgrError_IsSignedAround128(byte a, double expected)
        => Assert.Equal(expected, new EgrErrorCommand().Parse([0x41, 0x2D, a]), 3);

    [Fact]
    public void RawCommands_AreCorrect()
    {
        Assert.Equal("012C", new CommandedEgrCommand().RawCommand);
        Assert.Equal("012D", new EgrErrorCommand().RawCommand);
    }
}

public class EvapCommandTests
{
    [Fact]
    public void CommandedPurge_ScalesToPercent()
        => Assert.Equal(100.0, new CommandedEvaporativePurgeCommand().Parse([0x41, 0x2E, 0xFF]), 3);

    [Theory]
    [InlineData(0x00, 0x00, 0.0)]
    [InlineData(0x00, 0x04, 1.0)]
    [InlineData(0x7F, 0xFF, 8191.75)]
    public void VaporPressure_ScalesToQuarterPascals(byte a, byte b, double expected)
        => Assert.Equal(expected, new EvapVaporPressureCommand().Parse([0x41, 0x32, a, b]), 3);

    [Fact]
    public void VaporPressure_GoesNegativeForVacuum()
    {
        // The entire point of the measurement is the vacuum a working system pulls. Reading these two
        // bytes unsigned turns -0.25 Pa into +16,383.75 Pa.
        var result = new EvapVaporPressureCommand().Parse([0x41, 0x32, 0xFF, 0xFF]);
        Assert.Equal(-0.25, result, 3);
    }

    [Fact]
    public void VaporPressure_HandlesTheFullNegativeRange()
        => Assert.Equal(-8192.0, new EvapVaporPressureCommand().Parse([0x41, 0x32, 0x80, 0x00]), 3);

    [Theory]
    [InlineData(0x00, 0x00, 0.0)]
    [InlineData(0x4E, 0x20, 100.0)]     // 20000 / 200
    public void AbsoluteVaporPressure_ScalesToKilopascals(byte a, byte b, double expected)
        => Assert.Equal(expected, new AbsoluteEvapVaporPressureCommand().Parse([0x41, 0x53, a, b]), 3);

    [Fact]
    public void WideRangeVaporPressure_IsSignedPascals()
    {
        var command = new EvapVaporPressureWideRangeCommand();

        Assert.Equal(1.0, command.Parse([0x41, 0x54, 0x00, 0x01]), 3);
        Assert.Equal(-1.0, command.Parse([0x41, 0x54, 0xFF, 0xFF]), 3);
        Assert.Equal(-32768.0, command.Parse([0x41, 0x54, 0x80, 0x00]), 3);
    }

    [Fact]
    public void TheThreePressurePids_AreNotInterchangeable()
    {
        // Same bytes, three commands, three different physical claims. Documented on the commands, and
        // asserted here so nobody "unifies" them later.
        byte[] bytes = [0x40, 0x00];

        var fine = new EvapVaporPressureCommand().Parse([0x41, 0x32, bytes[0], bytes[1]]);
        var absolute = new AbsoluteEvapVaporPressureCommand().Parse([0x41, 0x53, bytes[0], bytes[1]]);
        var wide = new EvapVaporPressureWideRangeCommand().Parse([0x41, 0x54, bytes[0], bytes[1]]);

        Assert.Equal(4096.0, fine, 3);       // Pa
        Assert.Equal(81.92, absolute, 3);    // kPa
        Assert.Equal(16384.0, wide, 3);      // Pa
    }
}

public class TorqueCommandTests
{
    [Theory]
    [InlineData(0x7D, 0)]       // 125 - 125
    [InlineData(0x00, -125)]
    [InlineData(0xFF, 130)]
    public void DriverDemandTorque_IsOffsetBy125(byte a, int expected)
        => Assert.Equal(expected, new DriverDemandTorqueCommand().Parse([0x41, 0x61, a]));

    [Fact]
    public void ActualTorque_GoesNegativeOnOverrun()
        => Assert.Equal(-25, new ActualEngineTorqueCommand().Parse([0x41, 0x62, 100]));

    [Fact]
    public void ReferenceTorque_IsNewtonMetres()
        => Assert.Equal(400, new ReferenceTorqueCommand().Parse([0x41, 0x63, 0x01, 0x90]));

    [Fact]
    public void PercentTorqueData_ReadsAllFivePoints()
    {
        var result = new EnginePercentTorqueDataCommand()
            .Parse([0x41, 0x64, 0x7D, 0x8C, 0x96, 0xA0, 0xAA]);

        Assert.Equal(0, result.Idle);
        Assert.Equal(15, result.Point1);
        Assert.Equal(25, result.Point2);
        Assert.Equal(35, result.Point3);
        Assert.Equal(45, result.Point4);
    }

    [Fact]
    public void EnginePower_ConvertsPercentToNewtonMetres()
        => Assert.Equal(200.0, EnginePower.TorqueNm(50, 400), 3);

    [Fact]
    public void EnginePower_ComputesKilowatts()
    {
        // 200 Nm at 3000 rpm == 200 * 3000 * 2pi / 60000 == 62.83 kW
        Assert.Equal(62.832, EnginePower.Kilowatts(50, 400, 3000), 3);
    }

    [Fact]
    public void EnginePower_DistinguishesMetricFromMechanicalHorsepower()
    {
        var metric = EnginePower.MetricHorsepower(50, 400, 3000);
        var mechanical = EnginePower.MechanicalHorsepower(50, 400, 3000);

        Assert.Equal(85.43, metric, 2);
        Assert.Equal(84.26, mechanical, 2);

        // ~1.4% apart: small enough to look like noise, large enough for two apps to disagree about
        // the same car. Hence both being offered rather than one being called "horsepower".
        Assert.True(metric > mechanical);
    }

    [Fact]
    public void EnginePower_HandlesOverrun()
        => Assert.True(EnginePower.Kilowatts(-10, 400, 2000) < 0);
}
