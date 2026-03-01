using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class VehicleSpeedCommandTests
{
    readonly VehicleSpeedCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("010D", command.RawCommand);

    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x50, 80)]
    [InlineData(0xFF, 255)]
    public void Parse_ReturnsKmh(byte value, int expected)
    {
        byte[] data = [0x41, 0x0D, value];
        Assert.Equal(expected, command.Parse(data));
    }
}

public class EngineRpmCommandTests
{
    readonly EngineRpmCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("010C", command.RawCommand);

    [Theory]
    [InlineData(0x00, 0x00, 0)]
    [InlineData(0x1A, 0xF8, 1726)]    // ((0x1A*256)+0xF8)/4 = 6904/4
    [InlineData(0x0B, 0xB8, 750)]     // ((0x0B*256)+0xB8)/4 = 3000/4
    [InlineData(0xFF, 0xFF, 16383)]
    public void Parse_ReturnsRpm(byte a, byte b, int expected)
    {
        byte[] data = [0x41, 0x0C, a, b];
        Assert.Equal(expected, command.Parse(data));
    }
}

public class CoolantTemperatureCommandTests
{
    readonly CoolantTemperatureCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0105", command.RawCommand);

    [Theory]
    [InlineData(0x00, -40)]
    [InlineData(0x28, 0)]     // 40 - 40
    [InlineData(0x7B, 83)]    // 123 - 40
    [InlineData(0xFF, 215)]
    public void Parse_ReturnsCelsius(byte value, int expected)
    {
        byte[] data = [0x41, 0x05, value];
        Assert.Equal(expected, command.Parse(data));
    }
}

public class ThrottlePositionCommandTests
{
    readonly ThrottlePositionCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0111", command.RawCommand);

    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0xFF, 100.0)]
    public void Parse_ReturnsPercentage(byte value, double expected)
    {
        byte[] data = [0x41, 0x11, value];
        Assert.Equal(expected, command.Parse(data), precision: 2);
    }
}

public class FuelLevelCommandTests
{
    readonly FuelLevelCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("012F", command.RawCommand);

    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0x80, 50.20)]   // (128*100)/255
    [InlineData(0xFF, 100.0)]
    public void Parse_ReturnsPercentage(byte value, double expected)
    {
        byte[] data = [0x41, 0x2F, value];
        Assert.Equal(expected, command.Parse(data), precision: 1);
    }
}

public class CalculatedEngineLoadCommandTests
{
    readonly CalculatedEngineLoadCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0104", command.RawCommand);

    [Theory]
    [InlineData(0x00, 0.0)]
    [InlineData(0xFF, 100.0)]
    public void Parse_ReturnsPercentage(byte value, double expected)
    {
        byte[] data = [0x41, 0x04, value];
        Assert.Equal(expected, command.Parse(data), precision: 2);
    }
}

public class IntakeAirTemperatureCommandTests
{
    readonly IntakeAirTemperatureCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("010F", command.RawCommand);

    [Theory]
    [InlineData(0x00, -40)]
    [InlineData(0x46, 30)]    // 70 - 40
    [InlineData(0xFF, 215)]
    public void Parse_ReturnsCelsius(byte value, int expected)
    {
        byte[] data = [0x41, 0x0F, value];
        Assert.Equal(expected, command.Parse(data));
    }
}

public class RuntimeSinceStartCommandTests
{
    readonly RuntimeSinceStartCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("011F", command.RawCommand);

    [Theory]
    [InlineData(0x00, 0x00, 0)]
    [InlineData(0x00, 0x3C, 60)]       // 60 seconds
    [InlineData(0x0E, 0x10, 3600)]     // 1 hour
    [InlineData(0xFF, 0xFF, 65535)]
    public void Parse_ReturnsTimeSpan(byte a, byte b, int expectedSeconds)
    {
        byte[] data = [0x41, 0x1F, a, b];
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), command.Parse(data));
    }
}

public class VinCommandTests
{
    readonly VinCommand command = new();

    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0902", command.RawCommand);

    [Fact]
    public void Parse_ReturnsVinString()
    {
        // "WBA12345678901234"
        var vin = "WBA12345678901234";
        var vinBytes = System.Text.Encoding.ASCII.GetBytes(vin);

        // Response: mode echo (0x49), PID (0x02), count (0x01), then VIN bytes
        var data = new byte[2 + 1 + vinBytes.Length];
        data[0] = 0x49; // mode 09 + 0x40
        data[1] = 0x02;
        data[2] = 0x01; // data item count
        Array.Copy(vinBytes, 0, data, 3, vinBytes.Length);

        Assert.Equal(vin, command.Parse(data));
    }
}
