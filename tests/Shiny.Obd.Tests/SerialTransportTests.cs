using System.IO.Ports;
using Shiny.Obd;
using Shiny.Obd.Serial;

namespace Shiny.Obd.Tests;

public class SerialPortEnumeratorTests
{
    [Theory]
    [InlineData("usb-FTDI_FT232R_USB_UART_A50285BI-if00-port0", true)]
    [InlineData("usb-OBDLink_SX_STN1110-if00-port0", true)]
    [InlineData("usb-1a86_USB_Serial-if00-port0", true)]
    [InlineData("usb-Silicon_Labs_CP2102_USB_to_UART_Bridge_Controller-if00-port0", true)]
    [InlineData("Veepeak OBDCheck", true)]
    [InlineData("usb-Quectel_EG25-G-if02-port0", false)]
    [InlineData("ttyAMA0", false)]
    public void IsLikelyAdapter_MatchesKnownBridgesAndBrands(string description, bool expected)
        => Assert.Equal(expected, SerialPortEnumerator.IsLikelyAdapter(description));

    [Fact]
    public void Discover_DoesNotThrowOnAnyPlatform()
    {
        // Enumeration walks /dev (or the Windows device map) and must never be the thing that takes
        // the service down - a missing directory or a device unplugged mid-scan has to come back as
        // an empty list, not an exception.
        var ports = SerialPortEnumerator.Discover();
        Assert.NotNull(ports);
    }

    [Fact]
    public void Discover_ReturnsDistinctPortNames()
    {
        var ports = SerialPortEnumerator.Discover();
        var names = ports.Select(x => x.PortName).ToList();

        // On Linux a by-id link and the raw ttyUSB node are the same device reached two ways; on
        // macOS the same device appears under several patterns. Both must collapse to one entry, or
        // a caller iterating candidates tries to open the same busy port twice.
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Discover_OrdersLikelyAdaptersFirst()
    {
        var ports = SerialPortEnumerator.Discover();
        var firstUnlikely = ports.ToList().FindIndex(x => !x.IsLikelyAdapter);

        if (firstUnlikely < 0)
            return; // every port looked like an adapter, or there were none

        Assert.All(ports.Skip(firstUnlikely), x => Assert.False(x.IsLikelyAdapter));
    }
}

public class SerialObdConfigurationTests
{
    [Fact]
    public void Defaults_AreElm327Compatible()
    {
        var config = new SerialObdConfiguration();

        Assert.Equal(38400, config.BaudRate);
        Assert.Equal(8, config.DataBits);
        Assert.Equal(Parity.None, config.Parity);
        Assert.Equal(StopBits.One, config.StopBits);
        Assert.Equal(Handshake.None, config.Handshake);

        // USB-serial bridges hold the adapter in reset until the host raises DTR.
        Assert.True(config.DtrEnable);
    }

    [Fact]
    public void BaudCandidates_LeadWithTheElmDefault()
        => Assert.Equal(38400, new SerialObdConfiguration().BaudRateCandidates[0]);
}

public class SerialObdTransportTests
{
    [Fact]
    public void NotConnected_BeforeConnect()
    {
        var transport = new SerialObdTransport(new SerialObdConfiguration { PortName = "/dev/null" });

        Assert.False(transport.IsConnected);
        Assert.Null(transport.ConnectedPortName);
        Assert.Null(transport.ConnectedBaudRate);
    }

    [Fact]
    public async Task Send_ThrowsWhenNotConnected()
    {
        var transport = new SerialObdTransport(new SerialObdConfiguration { PortName = "/dev/null" });

        await Assert.ThrowsAsync<ObdException>(() => transport.Send("010D\r"));
    }

    [Fact]
    public async Task Connect_ToMissingPort_ThrowsObdException()
    {
        // A missing device has to surface as ObdException like every other transport failure -
        // callers retrying a connect should not have to catch IOException separately per transport.
        var transport = new SerialObdTransport(new SerialObdConfiguration
        {
            PortName = "/dev/obd-does-not-exist",
            AutoDetectBaudRate = false,
            OpenSettleDelay = TimeSpan.Zero
        });

        await Assert.ThrowsAsync<ObdException>(() => transport.Connect());
    }

    [Fact]
    public async Task Connect_WithUnmatchableFilter_ExplainsWhatWasFound()
    {
        var transport = new SerialObdTransport(new SerialObdConfiguration
        {
            PortName = null,
            PortNameFilter = "no-such-adapter-anywhere",
            AutoDetectBaudRate = false
        });

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());

        // The message is the whole value of this path: "connect failed" on a headless appliance with
        // no screen is not actionable, "found these three ports, none matched" is.
        Assert.True(
            ex.Message.Contains("No serial port matched", StringComparison.Ordinal) ||
            ex.Message.Contains("No serial ports found", StringComparison.Ordinal),
            $"Unexpected message: {ex.Message}"
        );
    }

    [Fact]
    public async Task Connect_OnAndroid_ExplainsThatSerialIsNotTheRoute()
    {
        // Android is the trap platform. System.IO.Ports is not marked unsupported there and its
        // native library ships for the android-* RIDs, so a net10.0-android project can reference
        // this package and compile cleanly - then fail here rather than with
        // PlatformNotSupportedException. The generic "add yourself to the dialout group" advice is
        // actively wrong on Android: /dev/ttyUSB* is root:usb and unreachable from an app at all.
        if (!OperatingSystem.IsAndroid())
            return;

        var transport = new SerialObdTransport(new SerialObdConfiguration
        {
            PortName = "/dev/ttyUSB0",
            AutoDetectBaudRate = false,
            OpenSettleDelay = TimeSpan.Zero
        });

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());

        Assert.Contains("Android", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("dialout", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeAsync_IsSafeBeforeConnect()
    {
        var transport = new SerialObdTransport(new SerialObdConfiguration { PortName = "/dev/null" });
        await transport.DisposeAsync();
    }
}

public class SerialObdDeviceScannerTests
{
    [Fact]
    public async Task Scan_CompletesOnCancellation()
    {
        var scanner = new SerialObdDeviceScanner();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await scanner.Scan(_ => { }, cts.Token);
    }

    [Fact]
    public async Task Scan_ReportsEachPortOnce()
    {
        var scanner = new SerialObdDeviceScanner();
        var found = new List<ObdDiscoveredDevice>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // The scan re-reads /dev on an interval so a UI can stay live; a port that is still present
        // on the second pass must not be reported again or the list grows without bound.
        await scanner.Scan(found.Add, cts.Token);

        var ids = found.Select(x => x.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Scan_SurfacesPortInfoAsNativeDevice()
    {
        var scanner = new SerialObdDeviceScanner();
        var found = new List<ObdDiscoveredDevice>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await scanner.Scan(found.Add, cts.Token);

        Assert.All(found, x => Assert.IsType<SerialPortInfo>(x.NativeDevice));
    }
}
