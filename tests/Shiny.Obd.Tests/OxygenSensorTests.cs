using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class OxygenSensorsPresentTests
{
    [Fact]
    public void RawCommands_AreCorrect()
    {
        Assert.Equal("0113", OxygenSensorsPresentCommand.TwoBanks().RawCommand);
        Assert.Equal("011D", OxygenSensorsPresentCommand.FourBanks().RawCommand);
    }

    [Fact]
    public void TwoBankLayout_MapsSensorsFourPerBank()
    {
        // 0x03 == sensors 1 and 2 present
        var layout = OxygenSensorsPresentCommand.TwoBanks().Parse([0x41, 0x13, 0x03]);

        Assert.Equal(OxygenSensorBankLayout.TwoBanksOfFour, layout.Layout);
        Assert.Equal(2, layout.Sensors.Count);
        Assert.Equal("B1S1", layout.Sensors[0].ToString());
        Assert.Equal("B1S2", layout.Sensors[1].ToString());
    }

    [Fact]
    public void FourBankLayout_MapsSensorsTwoPerBank()
    {
        var layout = OxygenSensorsPresentCommand.FourBanks().Parse([0x41, 0x1D, 0x03]);

        Assert.Equal(OxygenSensorBankLayout.FourBanksOfTwo, layout.Layout);
        Assert.Equal("B1S1", layout.Sensors[0].ToString());
        Assert.Equal("B1S2", layout.Sensors[1].ToString());
    }

    [Fact]
    public void SamePid_MeansADifferentSensorUnderEachLayout()
    {
        // The trap this whole type exists for. Sensor index 3 (PID 0x16) is bank 1 sensor 3 on a
        // two-bank vehicle and bank 2 sensor 1 on a four-bank one. Label it from the wrong layout and
        // you send someone to the wrong side of the engine.
        var twoBank = OxygenSensorsPresentCommand.TwoBanks().Parse([0x41, 0x13, 0xFF]);
        var fourBank = OxygenSensorsPresentCommand.FourBanks().Parse([0x41, 0x1D, 0xFF]);

        Assert.Equal("B1S3", twoBank.Position(3).ToString());
        Assert.Equal("B2S1", fourBank.Position(3).ToString());
    }

    [Fact]
    public void AllSensorsPresent_ReportsEight()
    {
        var layout = OxygenSensorsPresentCommand.TwoBanks().Parse([0x41, 0x13, 0xFF]);

        Assert.Equal(8, layout.Sensors.Count);
        Assert.Equal("B2S4", layout.Sensors[7].ToString());
    }

    [Fact]
    public void BitOrder_IsLeastSignificantFirst()
    {
        // The opposite order to the supported-PID bitmask, which is MSB-first. Getting the two the
        // same way round reports a vehicle's sensors mirrored.
        var layout = OxygenSensorsPresentCommand.TwoBanks().Parse([0x41, 0x13, 0x80]);

        var only = Assert.Single(layout.Sensors);
        Assert.Equal(8, only.SensorIndex);
    }

    [Fact]
    public void IsPresent_TracksTheBitmask()
    {
        var layout = OxygenSensorsPresentCommand.TwoBanks().Parse([0x41, 0x13, 0x05]);

        Assert.True(layout.IsPresent(1));
        Assert.False(layout.IsPresent(2));
        Assert.True(layout.IsPresent(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Position_RejectsAnIndexOutsideTheSensorRange(int index)
    {
        var layout = OxygenSensorsPresentCommand.TwoBanks().Parse([0x41, 0x13, 0xFF]);

        Assert.Throws<ArgumentOutOfRangeException>(() => layout.Position(index));
    }
}

public class OxygenSensorVoltageTests
{
    [Fact]
    public void RawCommand_TracksTheSensorIndex()
    {
        Assert.Equal("0114", OxygenSensorVoltageCommand.Sensor(1).RawCommand);
        Assert.Equal("011B", OxygenSensorVoltageCommand.Sensor(8).RawCommand);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void Sensor_AcceptsTheWholeRange(int index)
        => Assert.Equal(index, OxygenSensorVoltageCommand.Sensor(index).SensorIndex);

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Sensor_RejectsAnIndexOutsideTheRange(int index)
        => Assert.Throws<ArgumentOutOfRangeException>(() => OxygenSensorVoltageCommand.Sensor(index));

    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0x80, 0.64)]
    [InlineData(0xFF, 1.275)]
    public void Parse_ScalesVoltage(byte a, double expected)
    {
        var result = OxygenSensorVoltageCommand.Sensor(1).Parse([0x41, 0x14, a, 0x80]);
        Assert.Equal(expected, result.Volts, 3);
    }

    [Fact]
    public void Parse_ScalesTheAssociatedTrim()
    {
        var result = OxygenSensorVoltageCommand.Sensor(1).Parse([0x41, 0x14, 0x80, 0x80]);
        Assert.Equal(0.0, result.ShortTermFuelTrim!.Value, 3);
    }

    [Fact]
    public void TrimOf0xFF_IsAnAbsenceNotAReading()
    {
        // 0xFF is the "not used in trim calculation" marker. It scales to +99.2%, which is a plausible
        // number and would sit on a graph looking like a wildly rich correction.
        var result = OxygenSensorVoltageCommand.Sensor(1).Parse([0x41, 0x14, 0x40, 0xFF]);

        Assert.Null(result.ShortTermFuelTrim);
        Assert.Equal(0.32, result.Volts, 3);
    }
}

public class OxygenSensorLambdaTests
{
    [Fact]
    public void RawCommands_AddressTheRightPidBlocks()
    {
        Assert.Equal("0124", OxygenSensorLambdaCommand.WithVoltage(1).RawCommand);
        Assert.Equal("012B", OxygenSensorLambdaCommand.WithVoltage(8).RawCommand);
        Assert.Equal("0134", OxygenSensorLambdaCommand.WithCurrent(1).RawCommand);
        Assert.Equal("013B", OxygenSensorLambdaCommand.WithCurrent(8).RawCommand);
    }

    [Fact]
    public void WithVoltage_ParsesLambdaAndVolts()
    {
        // 0x8000 == half of 65536, so lambda == 1.0
        var result = OxygenSensorLambdaCommand.WithVoltage(1).Parse([0x41, 0x24, 0x80, 0x00, 0x80, 0x00]);

        Assert.Equal(1.0, result.Lambda, 4);
        Assert.Equal(4.0, result.Volts!.Value, 4);
        Assert.Null(result.Milliamps);
    }

    [Fact]
    public void WithCurrent_ParsesLambdaAndCurrent()
    {
        // 0x8000 in the current field is 32768/256 - 128 == 0 mA, which is lambda 1 on a wideband
        var result = OxygenSensorLambdaCommand.WithCurrent(1).Parse([0x41, 0x34, 0x80, 0x00, 0x80, 0x00]);

        Assert.Equal(1.0, result.Lambda, 4);
        Assert.Equal(0.0, result.Milliamps!.Value, 4);
        Assert.Null(result.Volts);
    }

    [Fact]
    public void Current_GoesNegativeBelowMidScale()
    {
        var result = OxygenSensorLambdaCommand.WithCurrent(1).Parse([0x41, 0x34, 0x80, 0x00, 0x00, 0x00]);
        Assert.Equal(-128.0, result.Milliamps!.Value, 4);
    }

    [Fact]
    public void PetrolAirFuelRatio_ScalesLambda()
    {
        var result = OxygenSensorLambdaCommand.WithVoltage(1).Parse([0x41, 0x24, 0x80, 0x00, 0x00, 0x00]);
        Assert.Equal(14.7, result.PetrolAirFuelRatio, 3);
    }

    [Fact]
    public void CommandedAirFuelRatio_ParsesLambda()
    {
        var command = new CommandedAirFuelRatioCommand();

        Assert.Equal("0144", command.RawCommand);
        Assert.Equal(1.0, command.Parse([0x41, 0x44, 0x80, 0x00]), 4);
    }
}
