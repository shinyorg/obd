using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class UnitAndScalingTests
{
    [Fact]
    public void KnownIdentifier_ScalesAndOffsets()
    {
        // 0x16 is 0.1 C per bit with a -40 offset
        var scaling = UnitAndScaling.Lookup(0x16)!.Value;

        Assert.Equal("°C", scaling.Unit);
        Assert.Equal(-40.0, scaling.Apply(0), 3);
        Assert.Equal(60.0, scaling.Apply(1000), 3);
    }

    [Fact]
    public void SignedIdentifier_ReadsTwosComplement()
    {
        // 0x8C is signed volts at 0.01/bit. Read unsigned, -0.01 V becomes +655.35 V.
        var signed = UnitAndScaling.Lookup(0x8C)!.Value;
        var unsigned = UnitAndScaling.Lookup(0x0C)!.Value;

        Assert.True(signed.Signed);
        Assert.False(unsigned.Signed);
        Assert.Equal(-0.01, signed.Apply(0xFFFF), 4);
        Assert.Equal(655.35, unsigned.Apply(0xFFFF), 4);
    }

    [Fact]
    public void UnknownIdentifier_IsNullRatherThanAGuess()
    {
        Assert.Null(UnitAndScaling.Lookup(0x00));
        Assert.Null(UnitAndScaling.Lookup(0x50));
        Assert.Null(UnitAndScaling.Lookup(0xFF));
    }

    [Fact]
    public void RpmIdentifier_IsQuarterRpmPerBit()
        => Assert.Equal(1000.0, UnitAndScaling.Lookup(0x07)!.Value.Apply(4000), 3);
}

public class MonitorIdTests
{
    [Theory]
    [InlineData(0x01, "Oxygen sensor monitor B1S1")]
    [InlineData(0x05, "Oxygen sensor monitor B2S1")]
    [InlineData(0x10, "Oxygen sensor monitor B4S4")]
    [InlineData(0x21, "Catalyst monitor bank 1")]
    [InlineData(0x24, "Catalyst monitor bank 4")]
    [InlineData(0x31, "EGR monitor bank 1")]
    [InlineData(0x35, "VVT monitor bank 1")]
    [InlineData(0x3D, "Purge flow monitor")]
    [InlineData(0x41, "Oxygen sensor heater monitor B1S1")]
    [InlineData(0x61, "Heated catalyst monitor bank 1")]
    [InlineData(0x71, "Secondary air monitor 1")]
    [InlineData(0x81, "Fuel system monitor bank 1")]
    [InlineData(0x85, "Boost pressure monitor bank 1")]
    [InlineData(0x90, "NOx adsorber monitor bank 1")]
    [InlineData(0x98, "NOx catalyst monitor bank 1")]
    [InlineData(0xA1, "Misfire monitor (general)")]
    [InlineData(0xA2, "Misfire monitor cylinder 1")]
    [InlineData(0xAD, "Misfire monitor cylinder 12")]
    [InlineData(0xB0, "PM filter monitor bank 1")]
    public void Describe_NamesTheStandardMonitors(byte mid, string expected)
        => Assert.Equal(expected, MonitorIds.Describe(mid));

    [Fact]
    public void EvapMonitors_NameTheLeakSizeTheyTestFor()
    {
        // The 0.020" test is the one that catches a loose filler cap, and the distinction between the
        // four is the whole diagnostic value of these MIDs.
        Assert.Equal("EVAP monitor (cap off / 0.150\")", MonitorIds.Describe(0x39));
        Assert.Equal("EVAP monitor (0.020\")", MonitorIds.Describe(0x3C));
    }

    [Theory]
    [InlineData(0x00)]    // the supported-MID block, not a monitor
    [InlineData(0x11)]    // reserved
    [InlineData(0xE0)]    // manufacturer-defined
    [InlineData(0xFF)]
    public void Describe_IsNullForAnythingNotStandardised(byte mid)
        => Assert.Null(MonitorIds.Describe(mid));
}

public class OnBoardTestCommandTests
{
    [Fact]
    public void RawCommand_CarriesTheMid()
        => Assert.Equal("0621", new OnBoardTestCommand(0x21).RawCommand);

    [Fact]
    public void Parse_ReadsANineByteRecord()
    {
        // MID 0x21 (catalyst bank 1), TID 0x80, UASID 0x0A (0.122 mV/bit), value 100, limits 50-200
        var results = new OnBoardTestCommand(0x21).Parse([
            0x46,
            0x21, 0x80, 0x0A, 0x00, 0x64, 0x00, 0x32, 0x00, 0xC8
        ]);

        var result = Assert.Single(results);
        Assert.Equal(0x21, result.Mid);
        Assert.Equal(0x80, result.TestId);
        Assert.Equal("Catalyst monitor bank 1", result.Monitor);
        Assert.Equal(100, result.RawValue);
        Assert.Equal(12.2, result.Value!.Value, 3);
        Assert.Equal("mV", result.Unit);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Parse_ReadsSeveralRecordsFromOneMonitor()
    {
        // A monitor commonly runs more than one test, which is why the result is a list.
        var results = new OnBoardTestCommand(0x01).Parse([
            0x46,
            0x01, 0x80, 0x01, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x64,
            0x01, 0x81, 0x01, 0x00, 0x14, 0x00, 0x00, 0x00, 0x64
        ]);

        Assert.Equal(2, results.Count);
        Assert.Equal(0x80, results[0].TestId);
        Assert.Equal(0x81, results[1].TestId);
    }

    [Fact]
    public void FailingTest_IsReportedAsFailing()
    {
        var results = new OnBoardTestCommand(0x21).Parse([
            0x46,
            0x21, 0x80, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x64   // 256, limits 0-100
        ]);

        Assert.False(results[0].Passed);
    }

    [Fact]
    public void SignedScaling_KeepsANegativeValuePassing()
    {
        // The failure this guards: a small negative measurement read as unsigned becomes ~65,535 and
        // turns a comfortably passing test into a dramatic failure.
        var results = new OnBoardTestCommand(0x01).Parse([
            0x46,
            0x01, 0x80, 0x8C, 0xFF, 0xFF, 0xFF, 0x9C, 0x00, 0x64   // -0.01 V, limits -1.00 to +1.00 V
        ]);

        var result = results[0];
        Assert.Equal(-0.01, result.Value!.Value, 4);
        Assert.True(result.Passed);
    }

    [Fact]
    public void UnknownScaling_LeavesRawDataAndNoClaims()
    {
        var results = new OnBoardTestCommand(0x01).Parse([
            0x46,
            0x01, 0x80, 0x50, 0x00, 0x64, 0x00, 0x00, 0x00, 0xC8   // 0x50 is not in the table
        ]);

        var result = results[0];
        Assert.Equal(100, result.RawValue);     // still usable
        Assert.Null(result.Value);
        Assert.Null(result.Unit);
        Assert.Null(result.Passed);             // signedness unknown, so no comparison is safe
        Assert.Null(result.BandPosition);
    }

    [Fact]
    public void BandPosition_ShowsHowCloseToFailingAResultIs()
    {
        // The number mode 06 exists for: this test passes, and it is 90% of the way to not passing.
        var results = new OnBoardTestCommand(0x21).Parse([
            0x46,
            0x21, 0x80, 0x01, 0x00, 0x5A, 0x00, 0x00, 0x00, 0x64   // 90, limits 0-100
        ]);

        Assert.True(results[0].Passed);
        Assert.Equal(0.9, results[0].BandPosition!.Value, 3);
    }

    [Fact]
    public void BandPosition_IsNullWhenTheLimitsLeaveNoBand()
    {
        var results = new OnBoardTestCommand(0x21).Parse([
            0x46,
            0x21, 0x80, 0x01, 0x00, 0x64, 0x00, 0x64, 0x00, 0x64
        ]);

        Assert.Null(results[0].BandPosition);
    }

    [Fact]
    public void WrongModeEcho_Throws()
    {
        var ex = Assert.Throws<ObdException>(() => new OnBoardTestCommand(0x21).Parse([
            0x41, 0x21, 0x80, 0x01, 0x00, 0x64, 0x00, 0x00, 0x00, 0xC8
        ]));

        Assert.Contains("0x46", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PartialRecord_SaysWhatIsProbablyWrong()
    {
        // A pre-CAN vehicle answers mode 06 in a different, largely manufacturer-specific format. That
        // has to fail loudly rather than decode into confident nonsense.
        var ex = Assert.Throws<ObdException>(() => new OnBoardTestCommand(0x21).Parse([
            0x46, 0x21, 0x80, 0x01, 0x00, 0x64, 0x00, 0x00, 0x00, 0xC8, 0x00
        ]));

        Assert.Contains("pre-CAN", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => new OnBoardTestCommand(0x21).Parse([0x46, 0x21]));
}

public class OnBoardTestSupportedMidsTests
{
    [Fact]
    public void BlockMids_CoverTheStandardRange()
        => Assert.Equal([0x00, 0x20, 0x40, 0x60, 0x80, 0xA0], MonitorIds.BlockMids);

    [Fact]
    public void Parse_ReadsTheBitmaskMsbFirst()
    {
        // 0x80 in the first byte is the MID immediately after the block base
        var supported = new OnBoardTestSupportedMidsCommand(0x00)
            .Parse([0x46, 0x00, 0x80, 0x00, 0x00, 0x00]);

        Assert.Equal([(byte)0x01], supported);
    }

    [Fact]
    public void Parse_MapsTheWholeBlock()
    {
        var supported = new OnBoardTestSupportedMidsCommand(0x20)
            .Parse([0x46, 0x20, 0xFF, 0xFF, 0xFF, 0xFF]);

        Assert.Equal(32, supported.Count);
        Assert.Equal(0x21, supported[0]);
        Assert.Equal(0x40, supported[31]);
    }

    [Fact]
    public void Parse_ReturnsNothingForAnEmptyBlock()
        => Assert.Empty(new OnBoardTestSupportedMidsCommand(0x40).Parse([0x46, 0x40, 0x00, 0x00, 0x00, 0x00]));

    [Fact]
    public void ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => new OnBoardTestSupportedMidsCommand(0x00).Parse([0x46, 0x00, 0xFF]));
}
