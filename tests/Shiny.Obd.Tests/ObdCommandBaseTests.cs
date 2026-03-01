namespace Shiny.Obd.Tests;

public class ObdCommandBaseTests
{
    [Fact]
    public void Parse_ThrowsOnResponseTooShort()
    {
        var cmd = new Commands.VehicleSpeedCommand();
        Assert.Throws<ObdException>(() => cmd.Parse([0x41]));
    }

    [Fact]
    public void Parse_ThrowsOnWrongMode()
    {
        var cmd = new Commands.VehicleSpeedCommand();
        // Expected 0x41, given 0x42
        Assert.Throws<ObdException>(() => cmd.Parse([0x42, 0x0D, 0x50]));
    }

    [Fact]
    public void Parse_ThrowsOnWrongPid()
    {
        var cmd = new Commands.VehicleSpeedCommand();
        // Expected PID 0x0D, given 0x0E
        Assert.Throws<ObdException>(() => cmd.Parse([0x41, 0x0E, 0x50]));
    }
}
