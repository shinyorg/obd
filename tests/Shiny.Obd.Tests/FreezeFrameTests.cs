using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class FreezeFrameTests
{
    [Fact]
    public void AsFreezeFrame_SwitchesToModeTwoAndCarriesTheFrameNumber()
    {
        Assert.Equal("020C00", StandardCommands.EngineRpm.AsFreezeFrame().RawCommand);
        Assert.Equal("020C01", StandardCommands.EngineRpm.AsFreezeFrame(1).RawCommand);
    }

    /// <summary>
    /// Mode 02 scales identically to mode 01, so the same parsing is reused rather than duplicated
    /// per PID — the only difference is the extra frame byte in the header.
    /// </summary>
    [Fact]
    public void AsFreezeFrame_ParsesWithTheModeOneScaling()
    {
        var live = StandardCommands.EngineRpm.Parse([0x41, 0x0C, 0x1A, 0xF8]);
        var frozen = StandardCommands.EngineRpm.AsFreezeFrame().Parse([0x42, 0x0C, 0x00, 0x1A, 0xF8]);

        Assert.Equal(1726, frozen);
        Assert.Equal(live, frozen);
    }

    [Fact]
    public void AsFreezeFrame_WorksForEveryValueType()
    {
        Assert.Equal(83, StandardCommands.CoolantTemperature.AsFreezeFrame().Parse([0x42, 0x05, 0x00, 0x7B]));
        Assert.Equal(80, StandardCommands.VehicleSpeed.AsFreezeFrame().Parse([0x42, 0x0D, 0x00, 0x50]));
        Assert.Equal(
            100.0,
            StandardCommands.CalculatedEngineLoad.AsFreezeFrame().Parse([0x42, 0x04, 0x00, 0xFF]),
            precision: 2
        );
    }

    [Fact]
    public void AsFreezeFrame_RejectsTheLiveModeEcho()
    {
        var command = StandardCommands.EngineRpm.AsFreezeFrame();

        // 0x41 is a mode 01 reply — accepting it would silently read the frame byte as data
        Assert.Throws<ObdException>(() => command.Parse([0x41, 0x0C, 0x00, 0x1A, 0xF8]));
    }

    [Fact]
    public void AsFreezeFrame_RejectsAnotherPid()
        => Assert.Throws<ObdException>(
            () => StandardCommands.EngineRpm.AsFreezeFrame().Parse([0x42, 0x0D, 0x00, 0x1A, 0xF8])
        );

    [Fact]
    public void AsFreezeFrame_RejectsAnotherFrame()
        => Assert.Throws<ObdException>(
            () => StandardCommands.EngineRpm.AsFreezeFrame().Parse([0x42, 0x0C, 0x01, 0x1A, 0xF8])
        );

    [Fact]
    public void AsFreezeFrame_ShortResponse_Throws()
        => Assert.Throws<ObdException>(() => StandardCommands.EngineRpm.AsFreezeFrame().Parse([0x42, 0x0C]));

    /// <summary>Mode 09 identifiers are not sampled at a moment, so there is no frame to ask for.</summary>
    [Fact]
    public void AsFreezeFrame_RefusesANonModeOneCommand()
        => Assert.Throws<ObdException>(() => StandardCommands.Vin.AsFreezeFrame());
}

public class FreezeFrameCausalDtcTests
{
    [Fact]
    public void RawCommand_CarriesThePidAndTheFrame()
    {
        Assert.Equal("020200", FreezeFrameCommands.CausalDtc().RawCommand);
        Assert.Equal("020201", FreezeFrameCommands.CausalDtc(1).RawCommand);
    }

    [Fact]
    public void Parse_ReturnsTheCodeThatStoredTheFrame()
        => Assert.Equal("P0301", FreezeFrameCommands.CausalDtc().Parse([0x42, 0x02, 0x00, 0x03, 0x01]));

    /// <summary>
    /// Zero means no snapshot is stored, and everything else in mode 02 is then a zero-filled
    /// frame rather than a measurement — so this is the call that gates reading any of it.
    /// </summary>
    [Fact]
    public void Parse_AnswersNullWhenThereIsNoSnapshot()
        => Assert.Null(FreezeFrameCommands.CausalDtc().Parse([0x42, 0x02, 0x00, 0x00, 0x00]));

    [Fact]
    public void Parse_RejectsAMismatchedHeader()
    {
        var command = FreezeFrameCommands.CausalDtc();

        Assert.Throws<ObdException>(() => command.Parse([0x41, 0x02, 0x00, 0x03, 0x01]));
        Assert.Throws<ObdException>(() => command.Parse([0x42, 0x0C, 0x00, 0x03, 0x01]));
        Assert.Throws<ObdException>(() => command.Parse([0x42, 0x02, 0x01, 0x03, 0x01]));
        Assert.Throws<ObdException>(() => command.Parse([0x42, 0x02, 0x00, 0x03]));
    }
}
