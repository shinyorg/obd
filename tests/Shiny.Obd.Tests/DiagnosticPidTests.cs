using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class FuelSystemStatusCommandTests
{
    readonly FuelSystemStatusCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0103", command.RawCommand);

    [Theory]
    [InlineData(0x00, FuelSystemState.Off)]
    [InlineData(0x01, FuelSystemState.OpenLoopEngineCold)]
    [InlineData(0x02, FuelSystemState.ClosedLoop)]
    [InlineData(0x04, FuelSystemState.OpenLoopLoadOrDeceleration)]
    [InlineData(0x08, FuelSystemState.OpenLoopSystemFailure)]
    [InlineData(0x10, FuelSystemState.ClosedLoopWithFault)]
    public void Parse_ReadsTheEncodedState(byte value, FuelSystemState expected)
        => Assert.Equal(expected, command.Parse([0x41, 0x03, value, 0x00]).System1);

    [Theory]
    [InlineData(0x03)]
    [InlineData(0x20)]
    [InlineData(0xFF)]
    public void Parse_AnswersNullForAValueOutsideTheStandardSet(byte value)
        => Assert.Null(command.Parse([0x41, 0x03, value, 0x00]).System1);

    /// <summary>
    /// A vehicle with one fuel system reports zero in byte B, which must not be read the way a zero
    /// in byte A is — there, zero means the engine is off.
    /// </summary>
    [Fact]
    public void SecondSystem_ZeroMeansThereIsNoSecondSystem()
    {
        var status = command.Parse([0x41, 0x03, 0x02, 0x00]);

        Assert.Equal(FuelSystemState.ClosedLoop, status.System1);
        Assert.Null(status.System2);
    }

    [Fact]
    public void SecondSystem_IsReadWhenPresent()
    {
        var status = command.Parse([0x41, 0x03, 0x02, 0x02]);

        Assert.Equal(FuelSystemState.ClosedLoop, status.System2);
    }

    [Fact]
    public void SecondSystem_IsNullWhenTheByteIsAbsent()
        => Assert.Null(command.Parse([0x41, 0x03, 0x02]).System2);

    /// <summary>
    /// The question a caller actually asks before trusting a fuel trim reading.
    /// </summary>
    [Theory]
    [InlineData(0x02, true)]
    [InlineData(0x10, true)]
    [InlineData(0x01, false)]
    [InlineData(0x04, false)]
    [InlineData(0x00, false)]
    public void IsClosedLoop_CoversBothClosedLoopStates(byte value, bool expected)
        => Assert.Equal(expected, command.Parse([0x41, 0x03, value, 0x00]).IsClosedLoop);
}

public class IntakeManifoldPressureCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("010B", StandardCommands.IntakeManifoldPressure.RawCommand);

    [Theory]
    [InlineData(0x1E, 30)]     // a warm idle at sea level
    [InlineData(0x65, 101)]    // near ambient — wide open throttle
    [InlineData(0xFF, 255)]
    public void Parse_ReturnsKilopascals(byte value, int expected)
        => Assert.Equal(expected, StandardCommands.IntakeManifoldPressure.Parse([0x41, 0x0B, value]));

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => StandardCommands.IntakeManifoldPressure.Parse([0x41, 0x0B]));
}

public class BarometricPressureCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0133", StandardCommands.BarometricPressure.RawCommand);

    [Theory]
    [InlineData(0x65, 101)]    // sea level
    [InlineData(0x54, 84)]     // roughly 1,500 m
    public void Parse_ReturnsKilopascals(byte value, int expected)
        => Assert.Equal(expected, StandardCommands.BarometricPressure.Parse([0x41, 0x33, value]));

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => StandardCommands.BarometricPressure.Parse([0x41, 0x33]));
}

public class TimingAdvanceCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("010E", StandardCommands.TimingAdvance.RawCommand);

    [Theory]
    [InlineData(0x80, 0.0)]      // 128/2 - 64
    [InlineData(0xA0, 16.0)]
    [InlineData(0x00, -64.0)]    // retard is negative and must survive the offset
    [InlineData(0xFF, 63.5)]
    public void Parse_ReturnsDegreesBeforeTopDeadCentre(byte value, double expected)
        => Assert.Equal(expected, StandardCommands.TimingAdvance.Parse([0x41, 0x0E, value]), precision: 2);

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => StandardCommands.TimingAdvance.Parse([0x41, 0x0E]));
}

public class AmbientAirTemperatureCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0146", StandardCommands.AmbientAirTemperature.RawCommand);

    [Theory]
    [InlineData(0x00, -40)]
    [InlineData(0x28, 0)]
    [InlineData(0x3C, 20)]
    [InlineData(0xFF, 215)]
    public void Parse_ReturnsCelsius(byte value, int expected)
        => Assert.Equal(expected, StandardCommands.AmbientAirTemperature.Parse([0x41, 0x46, value]));

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => StandardCommands.AmbientAirTemperature.Parse([0x41, 0x46]));
}

public class AcceleratorPedalPositionCommandTests
{
    [Fact]
    public void Factories_TargetTheDocumentedSensors()
    {
        Assert.Equal("0149", AcceleratorPedalPositionCommand.D().RawCommand);
        Assert.Equal("014A", AcceleratorPedalPositionCommand.E().RawCommand);
        Assert.Equal("014B", AcceleratorPedalPositionCommand.F().RawCommand);
    }

    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0x80, 50.196)]
    [InlineData(0xFF, 100.0)]
    public void Parse_ReturnsPercentage(byte value, double expected)
        => Assert.Equal(expected, AcceleratorPedalPositionCommand.D().Parse([0x41, 0x49, value]), precision: 3);

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => AcceleratorPedalPositionCommand.D().Parse([0x41, 0x49]));
}

public class RelativeAcceleratorPedalPositionCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("015A", StandardCommands.RelativeAcceleratorPedalPosition.RawCommand);

    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0xFF, 100.0)]
    public void Parse_ReturnsPercentage(byte value, double expected)
        => Assert.Equal(expected, StandardCommands.RelativeAcceleratorPedalPosition.Parse([0x41, 0x5A, value]), precision: 2);
}

public class CommandedThrottleActuatorCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("014C", StandardCommands.CommandedThrottleActuator.RawCommand);

    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0xFF, 100.0)]
    public void Parse_ReturnsPercentage(byte value, double expected)
        => Assert.Equal(expected, StandardCommands.CommandedThrottleActuator.Parse([0x41, 0x4C, value]), precision: 2);
}

public class DistanceWithMilOnCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0121", StandardCommands.DistanceWithMilOn.RawCommand);

    [Theory]
    [InlineData(0x00, 0x00, 0)]
    [InlineData(0x01, 0x2C, 300)]
    [InlineData(0xFF, 0xFF, 65535)]
    public void Parse_CombinesBothBytes(byte a, byte b, int expected)
        => Assert.Equal(expected, StandardCommands.DistanceWithMilOn.Parse([0x41, 0x21, a, b]));

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => StandardCommands.DistanceWithMilOn.Parse([0x41, 0x21, 0x01]));
}

public class MilAndClearedTimerTests
{
    /// <summary>
    /// These two are whole minutes, unlike RuntimeSinceStart's seconds — reading them the same way
    /// would under-report by a factor of sixty.
    /// </summary>
    [Fact]
    public void TimeRunWithMilOn_IsMinutes()
    {
        Assert.Equal("014D", StandardCommands.TimeRunWithMilOn.RawCommand);
        Assert.Equal(
            TimeSpan.FromMinutes(300),
            StandardCommands.TimeRunWithMilOn.Parse([0x41, 0x4D, 0x01, 0x2C])
        );
    }

    [Fact]
    public void TimeSinceCodesCleared_IsMinutes()
    {
        Assert.Equal("014E", StandardCommands.TimeSinceCodesCleared.RawCommand);
        Assert.Equal(
            TimeSpan.FromMinutes(65535),
            StandardCommands.TimeSinceCodesCleared.Parse([0x41, 0x4E, 0xFF, 0xFF])
        );
    }

    [Fact]
    public void ShortResponse_Throws()
    {
        Assert.Throws<ObdException>(() => StandardCommands.TimeRunWithMilOn.Parse([0x41, 0x4D, 0x01]));
        Assert.Throws<ObdException>(() => StandardCommands.TimeSinceCodesCleared.Parse([0x41, 0x4E, 0x01]));
    }
}

public class CalibrationIdCommandTests
{
    static byte[] Response(params string[] ids)
    {
        var data = new List<byte> { 0x49, 0x04, (byte)ids.Length };
        foreach (var id in ids)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(id);
            data.AddRange(bytes);
            data.AddRange(new byte[16 - bytes.Length]);   // unused bytes are reported as nulls
        }
        return data.ToArray();
    }

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0904", StandardCommands.CalibrationId.RawCommand);

    [Fact]
    public void Parse_ReadsASingleId()
        => Assert.Equal(["JMB*36761500"], StandardCommands.CalibrationId.Parse(Response("JMB*36761500")));

    [Fact]
    public void Parse_ReadsSeveralIds()
        => Assert.Equal(
            ["JMB*36761500", "JMB*36861500"],
            StandardCommands.CalibrationId.Parse(Response("JMB*36761500", "JMB*36861500"))
        );

    [Fact]
    public void Parse_KeepsAFullSixteenCharacterId()
        => Assert.Equal(["ABCDEFGHIJKLMNOP"], StandardCommands.CalibrationId.Parse(Response("ABCDEFGHIJKLMNOP")));

    /// <summary>
    /// Not every ECU sends the leading count byte, so the block remainder decides whether one is
    /// there — the same parity reasoning DtcDecoder uses.
    /// </summary>
    [Fact]
    public void Parse_HandlesAResponseWithNoCountByte()
    {
        var withCount = Response("JMB*36761500");
        var withoutCount = withCount[..2].Concat(withCount[3..]).ToArray();

        Assert.Equal(["JMB*36761500"], StandardCommands.CalibrationId.Parse(withoutCount));
    }

    [Fact]
    public void Parse_DropsABlockThatIsAllPadding()
    {
        var data = new List<byte> { 0x49, 0x04, 0x02 };
        data.AddRange(System.Text.Encoding.ASCII.GetBytes("JMB*36761500"));
        data.AddRange(new byte[4]);
        data.AddRange(new byte[16]);

        Assert.Equal(["JMB*36761500"], StandardCommands.CalibrationId.Parse(data.ToArray()));
    }

    [Fact]
    public void Parse_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => StandardCommands.CalibrationId.Parse([0x49, 0x04, 0x01, 0x41]));
}
