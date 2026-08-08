using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class FuelPressureCommandTests
{
    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x64, 300)]
    [InlineData(0xFF, 765)]
    public void FuelPressure_IsThreeKilopascalsPerBit(byte a, int expected)
        => Assert.Equal(expected, new FuelPressureCommand().Parse([0x41, 0x0A, a]));

    [Fact]
    public void FuelRailPressure_ScalesAgainstManifoldVacuum()
        => Assert.Equal(790.0, new FuelRailPressureCommand().Parse([0x41, 0x22, 0x27, 0x10]), 3);

    [Fact]
    public void FuelRailGaugePressure_ReachesCommonRailFigures()
    {
        // A common-rail diesel runs 25-250 MPa, which is why this PID is 10 kPa per bit
        var result = new FuelRailGaugePressureCommand().Parse([0x41, 0x23, 0x4E, 0x20]);

        Assert.Equal(200_000, result);      // kPa == 200 MPa
    }

    [Fact]
    public void FuelRailGaugePressure_CoversTheFullRange()
        => Assert.Equal(655_350, new FuelRailGaugePressureCommand().Parse([0x41, 0x23, 0xFF, 0xFF]));

    [Fact]
    public void FuelRailAbsolutePressure_UsesTheSameScale()
        => Assert.Equal(200_000, new FuelRailAbsolutePressureCommand().Parse([0x41, 0x59, 0x4E, 0x20]));
}

public class AdditionalMode01Tests
{
    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0xFF, 100.0)]
    public void EthanolFuelPercent_ScalesToPercent(byte a, double expected)
        => Assert.Equal(expected, new EthanolFuelPercentCommand().Parse([0x41, 0x52, a]), 3);

    [Fact]
    public void AbsoluteLoad_ExceedsOneHundredPercentOnBoost()
    {
        // Unlike calculated load, this is not capped at 100 - a boosted engine goes well above it,
        // which is what makes it the better axis to log boost against.
        var result = new AbsoluteLoadValueCommand().Parse([0x41, 0x43, 0x01, 0x90]);

        Assert.Equal(156.86, result, 2);
    }

    [Fact]
    public void WarmUpsSinceCodesCleared_IsACount()
        => Assert.Equal(42, new WarmUpsSinceCodesClearedCommand().Parse([0x41, 0x30, 42]));

    [Fact]
    public void RelativeThrottlePosition_ReadsZeroAtRest()
    {
        // The reason this exists alongside PID 0x11: absolute throttle carries a 12-18% closed floor,
        // so a UI built on it shows a throttle that is never shut.
        Assert.Equal(0.0, new RelativeThrottlePositionCommand().Parse([0x41, 0x45, 0x00]), 3);
        Assert.Equal(100.0, new RelativeThrottlePositionCommand().Parse([0x41, 0x45, 0xFF]), 3);
    }

    [Fact]
    public void AbsoluteThrottlePosition_AddressesBothSensors()
    {
        Assert.Equal("0147", AbsoluteThrottlePositionCommand.B().RawCommand);
        Assert.Equal("0148", AbsoluteThrottlePositionCommand.C().RawCommand);
        Assert.Equal(50.196, AbsoluteThrottlePositionCommand.B().Parse([0x41, 0x47, 0x80]), 3);
    }

    [Theory]
    [InlineData(0x69, 0x00, 0.0)]        // 26880/128 - 210
    [InlineData(0x00, 0x00, -210.0)]
    public void FuelInjectionTiming_IsDegreesAroundTdc(byte a, byte b, double expected)
        => Assert.Equal(expected, new FuelInjectionTimingCommand().Parse([0x41, 0x5D, a, b]), 3);

    [Fact]
    public void EngineRunTime_ReadsAllThreeCounters()
    {
        // A support byte, then three 4-byte second counters
        byte[] data = [
            0x41, 0x7F, 0x07,
            0x00, 0x00, 0x0E, 0x10,     // 3600 s total
            0x00, 0x00, 0x03, 0x84,     // 900 s idle
            0x00, 0x00, 0x00, 0x00      // no PTO
        ];

        var result = new EngineRunTimeCommand().Parse(data);

        Assert.Equal(TimeSpan.FromHours(1), result.Total);
        Assert.Equal(TimeSpan.FromMinutes(15), result.Idle);
        Assert.Equal(TimeSpan.Zero, result.PowerTakeOff);
        Assert.Equal(25.0, result.IdleFraction!.Value, 3);
    }

    [Fact]
    public void EngineRunTime_IdleFractionIsNullOnAnEngineThatNeverRan()
    {
        byte[] data = [0x41, 0x7F, 0x07, .. new byte[12]];

        Assert.Null(new EngineRunTimeCommand().Parse(data).IdleFraction);
    }

    [Fact]
    public void EngineRunTime_HandlesLargeCounters()
    {
        // Four bytes of seconds is ~136 years, so this must not overflow into a negative TimeSpan
        byte[] data = [
            0x41, 0x7F, 0x07,
            0xFF, 0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        ];

        Assert.Equal(TimeSpan.FromSeconds(4294967295), new EngineRunTimeCommand().Parse(data).Total);
    }

    [Fact]
    public void EngineRunTime_ShortResponseThrows()
        => Assert.Throws<ObdException>(() => new EngineRunTimeCommand().Parse([0x41, 0x7F, 0x07, 0x00]));
}

public class ObdStandardsTests
{
    [Fact]
    public void Parse_ReturnsTheRawCode()
        => Assert.Equal(6, new ObdStandardsCommand().Parse([0x41, 0x1C, 6]));

    [Theory]
    [InlineData(1, "OBD-II as defined by the CARB")]
    [InlineData(5, "Not OBD compliant")]
    [InlineData(6, "EOBD (Europe)")]
    [InlineData(10, "JOBD (Japan)")]
    [InlineData(20, "Heavy Duty On-Board Diagnostics (HD OBD)")]
    [InlineData(30, "Korean OBD (KOBD)")]
    [InlineData(35, "Brazil OBD Phase 3 (OBDBr-3)")]
    public void Describe_NamesTheStandard(byte code, string expected)
        => Assert.Equal(expected, ObdStandards.Describe(code));

    [Theory]
    [InlineData(0)]
    [InlineData(16)]     // reserved
    [InlineData(22)]     // reserved
    [InlineData(200)]
    [InlineData(255)]
    public void Describe_IsNullForReservedAndUnassignedCodes(byte code)
        => Assert.Null(ObdStandards.Describe(code));

    [Theory]
    [InlineData(20, true)]
    [InlineData(33, true)]
    [InlineData(1, false)]
    [InlineData(6, false)]
    public void IsHeavyDuty_IdentifiesTheCommercialStandards(byte code, bool expected)
        => Assert.Equal(expected, ObdStandards.IsHeavyDuty(code));
}

public class EcuIdentityTests
{
    [Fact]
    public void Cvn_IsReturnedAsHexNotANumber()
    {
        // A CVN is only ever compared for equality. Rendering it as an integer invites someone to sort
        // or subtract it.
        var result = new CalibrationVerificationNumberCommand()
            .Parse([0x49, 0x06, 0x01, 0x12, 0x34, 0x56, 0x78]);

        Assert.Equal("12345678", Assert.Single(result));
    }

    [Fact]
    public void Cvn_ReadsSeveralBlocks()
    {
        var result = new CalibrationVerificationNumberCommand()
            .Parse([0x49, 0x06, 0x12, 0x34, 0x56, 0x78, 0xAB, 0xCD, 0xEF, 0x01]);

        Assert.Equal(2, result.Count);
        Assert.Equal("ABCDEF01", result[1]);
    }

    [Fact]
    public void Cvn_PreservesLeadingZeroes()
    {
        // The exact failure that returning an integer would cause
        var result = new CalibrationVerificationNumberCommand()
            .Parse([0x49, 0x06, 0x00, 0x00, 0x12, 0x34]);

        Assert.Equal("00001234", Assert.Single(result));
    }

    [Fact]
    public void EcuName_TrimsThePadding()
    {
        byte[] name = "ECM-EngineControl"u8.ToArray();
        byte[] data = [0x49, 0x0A, .. name, .. new byte[20 - name.Length]];

        Assert.Equal("ECM-EngineControl", new EcuNameCommand().Parse(data));
    }

    [Fact]
    public void EcuName_HandlesALeadingCountByte()
    {
        byte[] name = "ECM"u8.ToArray();
        byte[] data = [0x49, 0x0A, 0x01, .. name, .. new byte[20 - name.Length]];

        Assert.Equal("ECM", new EcuNameCommand().Parse(data));
    }
}

public class InUsePerformanceTrackingTests
{
    [Fact]
    public void RawCommands_SelectTheEngineType()
    {
        Assert.Equal("0908", InUsePerformanceTrackingCommand.Spark().RawCommand);
        Assert.Equal("090B", InUsePerformanceTrackingCommand.Compression().RawCommand);
    }

    [Fact]
    public void Spark_ReadsCountersThenMonitorPairs()
    {
        byte[] data = [
            0x49, 0x08, 0x08,           // count of 16-bit items
            0x00, 0x64,                 // OBDCOND  = 100
            0x00, 0xC8,                 // IGNCNTR  = 200
            0x00, 0x0A, 0x00, 0x14,     // catalyst bank 1: 10 / 20
            0x00, 0x00, 0x00, 0x1E,     // catalyst bank 2:  0 / 30
            0x00, 0x05, 0x00, 0x00      // O2 bank 1:        5 / 0
        ];

        var result = InUsePerformanceTrackingCommand.Spark().Parse(data);

        Assert.Equal(100, result.MonitoringConditions);
        Assert.Equal(200, result.IgnitionCycles);
        Assert.Equal(3, result.Monitors.Count);

        Assert.Equal("Catalyst bank 1", result.Monitors[0].Monitor);
        Assert.Equal(0.5, result.Monitors[0].Ratio!.Value, 3);

        // Ran zero times out of thirty opportunities — a real finding
        Assert.Equal(0.0, result.Monitors[1].Ratio!.Value, 3);
    }

    [Fact]
    public void ZeroDenominator_IsNullNotZero()
    {
        // "Never had the opportunity" and "had the opportunity and never ran" are different findings,
        // and only the second is a problem with the vehicle.
        byte[] data = [
            0x49, 0x08, 0x03,
            0x00, 0x64, 0x00, 0xC8,
            0x00, 0x00, 0x00, 0x00
        ];

        var result = InUsePerformanceTrackingCommand.Spark().Parse(data);

        Assert.Null(result.Monitors[0].Ratio);
        Assert.Equal(0, result.Monitors[0].Conditions);
    }

    [Fact]
    public void Compression_UsesTheDieselMonitorNames()
    {
        byte[] data = [
            0x49, 0x0B, 0x03,
            0x00, 0x64, 0x00, 0xC8,
            0x00, 0x0A, 0x00, 0x14
        ];

        var result = InUsePerformanceTrackingCommand.Compression().Parse(data);

        Assert.Equal("NMHC catalyst", result.Monitors[0].Monitor);
    }

    [Fact]
    public void ExtraMonitors_AreUnnamedRatherThanMislabelled()
    {
        // A vehicle reporting more items than the standard names must not have the last known name
        // stretched over them.
        byte[] words = new byte[2 + (2 * 22)];
        byte[] data = [0x49, 0x08, 0x16, .. words[2..]];

        var result = InUsePerformanceTrackingCommand.Spark().Parse(data);

        Assert.True(result.Monitors.Count > 9);
        Assert.All(result.Monitors.Skip(9), x => Assert.Null(x.Monitor));
    }

    [Fact]
    public void ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => InUsePerformanceTrackingCommand.Spark().Parse([0x49, 0x08, 0x01]));
}
