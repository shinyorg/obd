using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class MonitorStatusDecoderTests
{
    static MonitorStatus Decode(byte a, byte b, byte c, byte d)
        => MonitorStatusDecoder.Decode([a, b, c, d]);

    static bool? Complete(MonitorStatus status, EmissionMonitor monitor)
        => status.Monitors.Where(x => x.Monitor == monitor).Select(x => (bool?)x.Complete).FirstOrDefault();

    [Fact]
    public void ByteA_CarriesTheLampAndTheCount()
    {
        // 0x83 = MIL on (bit 7) with 3 stored codes (low 7 bits)
        var status = Decode(0x83, 0x00, 0x00, 0x00);

        Assert.True(status.MilOn);
        Assert.Equal(3, status.DtcCount);
    }

    [Fact]
    public void ByteA_MilOffLeavesCountIntact()
    {
        var status = Decode(0x02, 0x00, 0x00, 0x00);

        Assert.False(status.MilOn);
        Assert.Equal(2, status.DtcCount);
    }

    /// <summary>
    /// Byte B bit 0 is misfire, bit 1 fuel system, bit 2 components — and bits 4-6 are the matching
    /// *incomplete* flags, so a set bit means the test is still running. Getting the polarity or
    /// the order wrong here would report a car as inspection-ready when it is not, so it is pinned
    /// one monitor at a time.
    /// </summary>
    [Theory]
    [InlineData(0x01, EmissionMonitor.Misfire)]
    [InlineData(0x02, EmissionMonitor.FuelSystem)]
    [InlineData(0x04, EmissionMonitor.Components)]
    public void ByteB_SupportBitsMapToTheCommonMonitors(byte supportBit, EmissionMonitor expected)
    {
        var status = Decode(0x00, supportBit, 0x00, 0x00);

        var only = Assert.Single(status.Monitors);
        Assert.Equal(expected, only.Monitor);
        Assert.True(only.Complete);
    }

    [Theory]
    [InlineData(0x01, 0x10, EmissionMonitor.Misfire)]
    [InlineData(0x02, 0x20, EmissionMonitor.FuelSystem)]
    [InlineData(0x04, 0x40, EmissionMonitor.Components)]
    public void ByteB_IncompleteBitIsInverted(byte supportBit, byte incompleteBit, EmissionMonitor expected)
    {
        var status = Decode(0x00, (byte)(supportBit | incompleteBit), 0x00, 0x00);

        var only = Assert.Single(status.Monitors);
        Assert.Equal(expected, only.Monitor);
        Assert.False(only.Complete);
    }

    [Fact]
    public void UnsupportedMonitorsAreLeftOutRatherThanReportedIncomplete()
    {
        // Only misfire is supported; the other two support bits are clear, and their incomplete
        // bits are set. Reporting them at all would put monitors on the screen that do not exist
        // on this vehicle, and IsReadyForInspection would never come true.
        var status = Decode(0x00, 0x61, 0x00, 0x00);

        var only = Assert.Single(status.Monitors);
        Assert.Equal(EmissionMonitor.Misfire, only.Monitor);
        Assert.True(status.IsReadyForInspection);
    }

    /// <summary>
    /// A struct's default is always reachable, so it has to mean something rather than throw. This
    /// is not hypothetical: a fake or stubbed connection hands back <c>default(MonitorStatus)</c>
    /// for every command, and dereferencing a null list there took down a caller's whole poll loop.
    /// </summary>
    [Fact]
    public void DefaultInstance_IsSafeToRead()
    {
        var status = default(MonitorStatus);

        Assert.Empty(status.Monitors);
        Assert.Empty(status.Incomplete);
        Assert.Null(status.IsReadyForInspection);
    }

    /// <summary>
    /// "Every monitor in an empty list has completed" is vacuously true, so a plain bool would
    /// report a vehicle as inspection-ready on the strength of bytes that never arrived.
    /// </summary>
    [Fact]
    public void IsReadyForInspection_IsNullWhenNoMonitorsWereReported()
    {
        Assert.Null(MonitorStatusDecoder.Decode([0x81]).IsReadyForInspection);
        Assert.Null(Decode(0x00, 0x00, 0x00, 0x00).IsReadyForInspection);
    }

    [Fact]
    public void ByteB_Bit3SelectsCompressionIgnition()
    {
        Assert.Equal(IgnitionType.Spark, Decode(0x00, 0x00, 0x00, 0x00).Ignition);
        Assert.Equal(IgnitionType.Compression, Decode(0x00, 0x08, 0x00, 0x00).Ignition);
    }

    /// <summary>Bytes C/D run MSB first: bit 7 is EGR/VVT down to bit 0 for the catalyst.</summary>
    [Theory]
    [InlineData(0x80, EmissionMonitor.EgrOrVvtSystem)]
    [InlineData(0x40, EmissionMonitor.OxygenSensorHeater)]
    [InlineData(0x20, EmissionMonitor.OxygenSensor)]
    [InlineData(0x10, EmissionMonitor.GasolineParticulateFilter)]
    [InlineData(0x08, EmissionMonitor.SecondaryAirSystem)]
    [InlineData(0x04, EmissionMonitor.EvaporativeSystem)]
    [InlineData(0x02, EmissionMonitor.HeatedCatalyst)]
    [InlineData(0x01, EmissionMonitor.Catalyst)]
    public void SparkMonitors_MapFromBytesCAndD(byte bit, EmissionMonitor expected)
    {
        var complete = Decode(0x00, 0x00, bit, 0x00);
        var running = Decode(0x00, 0x00, bit, bit);

        Assert.Equal(expected, Assert.Single(complete.Monitors).Monitor);
        Assert.True(Complete(complete, expected));
        Assert.False(Complete(running, expected));
    }

    [Theory]
    [InlineData(0x80, EmissionMonitor.EgrOrVvtSystem)]
    [InlineData(0x40, EmissionMonitor.ParticulateFilter)]
    [InlineData(0x20, EmissionMonitor.ExhaustGasSensor)]
    [InlineData(0x08, EmissionMonitor.BoostPressure)]
    [InlineData(0x02, EmissionMonitor.NoxOrScrAftertreatment)]
    [InlineData(0x01, EmissionMonitor.NmhcCatalyst)]
    public void CompressionMonitors_MapFromBytesCAndD(byte bit, EmissionMonitor expected)
    {
        var complete = Decode(0x00, 0x08, bit, 0x00);
        var running = Decode(0x00, 0x08, bit, bit);

        Assert.Equal(expected, Assert.Single(complete.Monitors).Monitor);
        Assert.True(Complete(complete, expected));
        Assert.False(Complete(running, expected));
    }

    /// <summary>Bits 4 and 2 of C/D are reserved on a diesel and must not become a monitor.</summary>
    [Theory]
    [InlineData(0x10)]
    [InlineData(0x04)]
    public void CompressionReservedBitsAreIgnored(byte bit)
        => Assert.Empty(Decode(0x00, 0x08, bit, 0x00).Monitors);

    [Fact]
    public void SparkAndCompressionDisagreeAboutTheSameBit()
    {
        // Bit 6 of C is the oxygen sensor heater on a petrol car and the particulate filter on a
        // diesel — the only thing that separates them is bit 3 of byte B
        Assert.Equal(EmissionMonitor.OxygenSensorHeater, Assert.Single(Decode(0x00, 0x00, 0x40, 0x00).Monitors).Monitor);
        Assert.Equal(EmissionMonitor.ParticulateFilter, Assert.Single(Decode(0x00, 0x08, 0x40, 0x00).Monitors).Monitor);
    }

    [Fact]
    public void IsReadyForInspection_RequiresEverySupportedMonitor()
    {
        // Misfire, fuel and components supported; catalyst and EVAP supported, EVAP still running
        var status = Decode(0x00, 0x07, 0x05, 0x04);

        Assert.False(status.IsReadyForInspection);
        Assert.Equal(EmissionMonitor.EvaporativeSystem, Assert.Single(status.Incomplete).Monitor);
    }

    [Fact]
    public void IsReadyForInspection_IsTrueWhenEverySupportedMonitorHasRun()
    {
        var status = Decode(0x00, 0x07, 0xFF, 0x00);

        Assert.True(status.IsReadyForInspection);
        Assert.Empty(status.Incomplete);
        Assert.Equal(11, status.Monitors.Count);
    }

    /// <summary>
    /// Some adapters truncate the reply to byte A. The lamp is the more important half, so that
    /// still decodes rather than throwing — with an empty monitor list rather than a fabricated one.
    /// </summary>
    [Fact]
    public void TruncatedResponse_StillReadsTheLamp()
    {
        var status = MonitorStatusDecoder.Decode([0x81]);

        Assert.True(status.MilOn);
        Assert.Equal(1, status.DtcCount);
        Assert.Empty(status.Monitors);
    }

    [Fact]
    public void EmptyResponse_Throws()
        => Assert.Throws<ObdException>(() => MonitorStatusDecoder.Decode([]));
}

public class MonitorStatusCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0101", StandardCommands.MonitorStatus.RawCommand);

    [Fact]
    public void Parse_ReadsThroughTheDecoder()
    {
        var status = StandardCommands.MonitorStatus.Parse([0x41, 0x01, 0x83, 0x07, 0x05, 0x04]);

        Assert.True(status.MilOn);
        Assert.Equal(3, status.DtcCount);
        Assert.False(status.IsReadyForInspection);
    }
}

public class MonitorStatusThisDriveCycleCommandTests
{
    [Fact]
    public void RawCommand_IsCorrect()
        => Assert.Equal("0141", StandardCommands.MonitorStatusThisDriveCycle.RawCommand);

    /// <summary>Byte A is reserved on this PID, so the lamp and count read empty by construction.</summary>
    [Fact]
    public void Parse_ReportsMonitorsWithoutALamp()
    {
        var status = StandardCommands.MonitorStatusThisDriveCycle.Parse([0x41, 0x41, 0x00, 0x07, 0x01, 0x01]);

        Assert.False(status.MilOn);
        Assert.Equal(0, status.DtcCount);
        Assert.Equal(4, status.Monitors.Count);
        Assert.Equal(EmissionMonitor.Catalyst, Assert.Single(status.Incomplete).Monitor);
    }
}
