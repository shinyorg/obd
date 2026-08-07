using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class OdometerCommandTests
{
    readonly OdometerCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("01A6", command.RawCommand);

    [Fact]
    public void Parse_ScalesTenthsOfAKilometre()
    {
        // 0x00061A80 = 400,000 -> 40,000.0 km
        byte[] data = [0x41, 0xA6, 0x00, 0x06, 0x1A, 0x80];
        Assert.Equal(40_000.0, command.Parse(data));
    }

    [Fact]
    public void Parse_HandlesTheTopOfTheRange()
    {
        // Would overflow a signed int if the bytes were combined as int rather than uint
        byte[] data = [0x41, 0xA6, 0xFF, 0xFF, 0xFF, 0xFF];
        Assert.Equal(429_496_729.5, command.Parse(data));
    }

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => command.Parse([0x41, 0xA6, 0x00, 0x06]));
}

public class DistanceSinceCodesClearedCommandTests
{
    readonly DistanceSinceCodesClearedCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0131", command.RawCommand);

    [Theory]
    [InlineData(0x00, 0x00, 0)]
    [InlineData(0x01, 0x2C, 300)]
    [InlineData(0xFF, 0xFF, 65535)]
    public void Parse_CombinesBothBytes(byte a, byte b, int expected)
        => Assert.Equal(expected, command.Parse([0x41, 0x31, a, b]));

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => command.Parse([0x41, 0x31, 0x01]));
}

public class ControlModuleVoltageCommandTests
{
    readonly ControlModuleVoltageCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0142", command.RawCommand);

    [Theory]
    [InlineData(0x36, 0xB0, 14.0)]
    [InlineData(0x00, 0x00, 0.0)]
    [InlineData(0x2F, 0x44, 12.1)]
    public void Parse_ScalesMillivolts(byte a, byte b, double expected)
        => Assert.Equal(expected, command.Parse([0x41, 0x42, a, b]), precision: 3);

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => command.Parse([0x41, 0x42, 0x36]));
}

public class MassAirFlowCommandTests
{
    readonly MassAirFlowCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0110", command.RawCommand);

    [Theory]
    [InlineData(0x00, 0x00, 0.0)]
    [InlineData(0x01, 0xF4, 5.0)]      // 500 / 100
    [InlineData(0xFF, 0xFF, 655.35)]
    public void Parse_ReturnsGramsPerSecond(byte a, byte b, double expected)
        => Assert.Equal(expected, command.Parse([0x41, 0x10, a, b]), precision: 2);

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => command.Parse([0x41, 0x10, 0x01]));
}

public class EngineFuelRateCommandTests
{
    readonly EngineFuelRateCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("015E", command.RawCommand);

    [Theory]
    [InlineData(0x00, 0x0C, 0.6)]      // 12 / 20 — a typical idle
    [InlineData(0x00, 0x64, 5.0)]      // 100 / 20
    [InlineData(0xFF, 0xFF, 3276.75)]
    public void Parse_ReturnsLitresPerHour(byte a, byte b, double expected)
        => Assert.Equal(expected, command.Parse([0x41, 0x5E, a, b]), precision: 2);

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => command.Parse([0x41, 0x5E, 0x00]));
}

public class FuelTrimCommandTests
{
    [Theory]
    [InlineData(0x06, "0106")]
    [InlineData(0x07, "0107")]
    [InlineData(0x08, "0108")]
    [InlineData(0x09, "0109")]
    public void RawCommand_UsesTheGivenPid(byte pid, string expected)
        => Assert.Equal(expected, new FuelTrimCommand(pid).RawCommand);

    [Fact]
    public void Factories_TargetTheDocumentedPids()
    {
        Assert.Equal("0106", FuelTrimCommand.ShortTermBank1().RawCommand);
        Assert.Equal("0107", FuelTrimCommand.LongTermBank1().RawCommand);
        Assert.Equal("0108", FuelTrimCommand.ShortTermBank2().RawCommand);
        Assert.Equal("0109", FuelTrimCommand.LongTermBank2().RawCommand);
    }

    [Theory]
    [InlineData(0x80, 0.0)]        // 128 is zero correction
    [InlineData(0x00, -100.0)]
    [InlineData(0xFF, 99.21875)]
    [InlineData(0x99, 19.53125)]   // adding fuel — the ECU reads the mixture as lean
    public void Parse_CentresOn128(byte value, double expected)
    {
        var command = FuelTrimCommand.ShortTermBank1();
        Assert.Equal(expected, command.Parse([0x41, 0x06, value]), precision: 5);
    }

    [Fact]
    public void Parse_ShortResponse_Throws()
    {
        var command = FuelTrimCommand.ShortTermBank1();
        Assert.Throws<ObdException>(() => command.Parse([0x41, 0x06]));
    }
}

public class EngineOilTemperatureCommandTests
{
    readonly EngineOilTemperatureCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("015C", command.RawCommand);

    [Theory]
    [InlineData(0x00, -40)]
    [InlineData(0x28, 0)]      // 40 - 40
    [InlineData(0x82, 90)]     // 130 - 40
    [InlineData(0xFF, 215)]
    public void Parse_ReturnsCelsius(byte value, int expected)
        => Assert.Equal(expected, command.Parse([0x41, 0x5C, value]));

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => command.Parse([0x41, 0x5C]));
}

public class FuelTypeCommandTests
{
    readonly FuelTypeCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0151", command.RawCommand);

    [Fact]
    public void Parse_ReadsTheSingleByteCode()
        => Assert.Equal(0x04, command.Parse([0x41, 0x51, 0x04]));

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => command.Parse([0x41, 0x51]));
}

public class FuelTypesTests
{
    [Theory]
    [InlineData(1, "Gasoline")]
    [InlineData(4, "Diesel")]
    [InlineData(8, "Electric")]
    [InlineData(17, "Hybrid gasoline")]
    [InlineData(20, "Hybrid electric")]
    [InlineData(23, "Bi-fuel, running diesel")]
    public void Describe_ReadsTheJ1979Table(byte code, string expected)
        => Assert.Equal(expected, FuelTypes.Describe(code));

    /// <summary>
    /// A caller storing or showing this string has to be able to tell an absent answer from a claim
    /// about the vehicle, so an unlisted code answers null rather than "Unknown". Code 0 is the
    /// table's own "not available", so it answers the same way an out-of-range code does.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(255)]
    public void Describe_AnswersNullRatherThanGuessing(byte code)
        => Assert.Null(FuelTypes.Describe(code));

    [Fact]
    public void Describe_AnswersNullForAnUnreportedPid()
        => Assert.Null(FuelTypes.Describe((byte?)null));
}

public class HybridBatteryLifeCommandTests
{
    readonly HybridBatteryLifeCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("015B", command.RawCommand);

    [Theory]
    [InlineData(0xFF, 100.0)]
    [InlineData(0x80, 50.196)]
    [InlineData(0x00, 0.0)]
    public void Parse_ScalesToPercent(byte value, double expected)
        => Assert.Equal(expected, command.Parse([0x41, 0x5B, value]), precision: 3);

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => command.Parse([0x41, 0x5B]));
}

public class SupportedPidsCommandTests
{
    [Fact]
    public void RawCommand_UsesTheBlockPid()
        => Assert.Equal("01A0", new SupportedPidsCommand(0xA0).RawCommand);

    [Fact]
    public void Parse_MostSignificantBitIsTheFirstPidInTheBlock()
    {
        // 0x80000000 -> only bit 31 set -> PID 0x01
        var pids = new SupportedPidsCommand(0x00).Parse([0x41, 0x00, 0x80, 0x00, 0x00, 0x00]);

        Assert.Equal([(byte)0x01], pids);
    }

    [Fact]
    public void Parse_LeastSignificantBitIsTheLastPidInTheBlock()
    {
        // 0x00000001 -> only bit 0 set -> PID 0x20 (base + 32)
        var pids = new SupportedPidsCommand(0x00).Parse([0x41, 0x00, 0x00, 0x00, 0x00, 0x01]);

        Assert.Equal([(byte)0x20], pids);
    }

    [Fact]
    public void Parse_OffsetsByTheBlockBase()
    {
        // bit 30 -> base + 2 -> 0xA2
        var pids = new SupportedPidsCommand(0xA0).Parse([0x41, 0xA0, 0x40, 0x00, 0x00, 0x00]);

        Assert.Equal([(byte)0xA2], pids);
    }

    [Fact]
    public void Parse_DecodesAFullMask()
    {
        var pids = new SupportedPidsCommand(0x00).Parse([0x41, 0x00, 0xFF, 0xFF, 0xFF, 0xFF]);

        Assert.Equal(32, pids.Count);
        Assert.Equal(0x01, pids[0]);
        Assert.Equal(0x20, pids[31]);
    }

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(
            () => new SupportedPidsCommand(0x00).Parse([0x41, 0x00, 0xFF, 0xFF])
        );

    [Fact]
    public void BlockPids_CoverTheWholeModeOneRange()
        => Assert.Equal([(byte)0x00, 0x20, 0x40, 0x60, 0x80, 0xA0, 0xC0], SupportedPidsCommand.BlockPids);
}
