using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class DtcReadCommandTests
{
    [Theory]
    [InlineData(0x03, "03")]
    [InlineData(0x07, "07")]
    [InlineData(0x0A, "0A")]
    public void RawCommand_IsTwoDigitUppercaseHex(byte mode, string expected)
        => Assert.Equal(expected, new DtcReadCommand(mode).RawCommand);

    [Fact]
    public void Stored_ParsesAgainstMode43Echo()
        => Assert.Equal(["P0301"], DtcReadCommand.Stored.Parse([0x43, 0x01, 0x03, 0x01]));

    [Fact]
    public void Pending_ParsesAgainstMode47Echo()
        => Assert.Equal(["P0171"], DtcReadCommand.Pending.Parse([0x47, 0x01, 0x01, 0x71]));

    [Fact]
    public void Permanent_ParsesAgainstMode4AEcho()
        => Assert.Equal(["P0420"], DtcReadCommand.Permanent.Parse([0x4A, 0x01, 0x04, 0x20]));

    [Fact]
    public void SharedInstances_CarryTheirOwnMode()
    {
        Assert.Equal(0x03, DtcReadCommand.Stored.Mode);
        Assert.Equal(0x07, DtcReadCommand.Pending.Mode);
        Assert.Equal(0x0A, DtcReadCommand.Permanent.Mode);
    }
}

public class ClearDtcCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("04", ClearDtcCommand.Instance.RawCommand);

    [Fact]
    public void Parse_AcceptsThe44Acknowledgement()
        => Assert.True(ClearDtcCommand.Instance.Parse([0x44]));

    [Fact]
    public void Parse_RejectsANegativeResponse()
        => Assert.False(ClearDtcCommand.Instance.Parse([0x7F, 0x04, 0x12]));

    [Fact]
    public void Parse_RejectsAnEmptyResponse()
        => Assert.False(ClearDtcCommand.Instance.Parse([]));
}

public class DtcDecoderTests
{
    [Theory]
    [InlineData(0x03, 0x01, "P0301")] // powertrain, cylinder 1 misfire
    [InlineData(0x04, 0x20, "P0420")] // catalyst efficiency
    [InlineData(0xC1, 0x00, "U0100")] // network, lost comms with ECM
    [InlineData(0x92, 0x34, "B1234")] // body, manufacturer-specific
    [InlineData(0x51, 0x55, "C1155")] // chassis, manufacturer-specific
    public void DecodePair_ProducesJ2012Code(byte a, byte b, string expected)
        => Assert.Equal(expected, DtcDecoder.DecodePair(a, b));

    [Fact]
    public void DecodePair_AllZeroes_IsPaddingAndReturnsNull()
        => Assert.Null(DtcDecoder.DecodePair(0x00, 0x00));

    [Fact]
    public void DecodePair_UsesHexDigitsAboveNine()
        => Assert.Equal("P0FAB", DtcDecoder.DecodePair(0x0F, 0xAB));

    [Fact]
    public void Decode_CanResponse_SkipsTheCountByte()
    {
        // CAN replies `43 <count> <pairs...>` — payload length after the mode echo is odd
        byte[] response = [0x43, 0x02, 0x03, 0x01, 0x04, 0x20];

        Assert.Equal(["P0301", "P0420"], DtcDecoder.Decode(response, 0x43));
    }

    [Fact]
    public void Decode_NonCanResponse_HasNoCountByte()
    {
        // Older protocols reply `43 <pairs...>` — payload length after the mode echo is even
        byte[] response = [0x43, 0x03, 0x01, 0x04, 0x20, 0x00, 0x00];

        Assert.Equal(["P0301", "P0420"], DtcDecoder.Decode(response, 0x43));
    }

    [Fact]
    public void Decode_DropsZeroPadding()
    {
        byte[] response = [0x43, 0x03, 0x01, 0x00, 0x00, 0x00, 0x00];

        Assert.Equal(["P0301"], DtcDecoder.Decode(response, 0x43));
    }

    [Fact]
    public void Decode_CanResponseWithNoCodes_ReturnsEmpty()
        => Assert.Empty(DtcDecoder.Decode([0x43, 0x00], 0x43));

    [Fact]
    public void Decode_EmptyResponse_ReturnsEmpty()
        => Assert.Empty(DtcDecoder.Decode([], 0x43));

    [Fact]
    public void Decode_WithoutModeEcho_StillDecodesPairs()
    {
        byte[] response = [0x03, 0x01, 0x04, 0x20];

        Assert.Equal(["P0301", "P0420"], DtcDecoder.Decode(response, 0x43));
    }

    [Fact]
    public void Decode_PendingModeUsesItsOwnEcho()
        => Assert.Equal(["P0171"], DtcDecoder.Decode([0x47, 0x01, 0x01, 0x71], 0x47));

    [Fact]
    public void Decode_PermanentModeUsesItsOwnEcho()
        => Assert.Equal(["P0420"], DtcDecoder.Decode([0x4A, 0x01, 0x04, 0x20], 0x4A));
}
