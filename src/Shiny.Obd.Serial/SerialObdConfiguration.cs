using System;
using System.IO.Ports;

namespace Shiny.Obd.Serial;

/// <summary>
/// Configuration for the serial (USB/UART) OBD transport.
/// </summary>
/// <remarks>
/// The defaults suit the common USB adapters (OBDLink SX, ELM327 clones on CH340/FTDI/CP210x) as
/// well as the Raspberry Pi's own UART. Only <see cref="PortName"/> normally needs setting, and even
/// that can be left null to let <see cref="SerialObdDeviceScanner"/> pick.
/// </remarks>
public class SerialObdConfiguration
{
    /// <summary>
    /// The serial port to open - <c>/dev/ttyUSB0</c> or a <c>/dev/serial/by-id/...</c> path on Linux,
    /// <c>/dev/cu.usbserial-XXXX</c> on macOS, <c>COM3</c> on Windows.
    /// </summary>
    /// <remarks>
    /// Null means "discover one" - the transport asks <see cref="SerialObdDeviceScanner"/> for the
    /// first likely adapter at connect time. Prefer a <c>by-id</c> path over <c>ttyUSB0</c> on Linux:
    /// the numbered device names are assigned in enumeration order, so a second USB serial device
    /// (a GNSS puck, a cellular modem) can take <c>ttyUSB0</c> out from under you across a reboot.
    /// </remarks>
    public string? PortName { get; set; }

    /// <summary>
    /// Baud rate. 38400 is the ELM327 default; OBDLink/STN adapters run happily at 115200 and
    /// upwards, which is worth having when you are polling more than a handful of PIDs.
    /// </summary>
    public int BaudRate { get; set; } = 38400;

    /// <summary>
    /// Baud rates to try, in order, when <see cref="AutoDetectBaudRate"/> is on.
    /// </summary>
    public int[] BaudRateCandidates { get; set; } = [38400, 115200, 9600, 500000];

    /// <summary>
    /// Probe <see cref="BaudRateCandidates"/> at connect time rather than trusting
    /// <see cref="BaudRate"/>. Costs a second or two on connect and removes an entire category of
    /// "it returns garbage" support question.
    /// </summary>
    public bool AutoDetectBaudRate { get; set; } = true;

    public Parity Parity { get; set; } = Parity.None;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>
    /// Assert DTR on open. Required by most USB-serial bridges, which hold the adapter in reset
    /// until the host raises it.
    /// </summary>
    public bool DtrEnable { get; set; } = true;

    /// <summary>
    /// Assert RTS on open.
    /// </summary>
    public bool RtsEnable { get; set; } = true;

    /// <summary>
    /// Timeout for a single OBD command response.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long to wait for the adapter to settle after opening the port before sending anything.
    /// USB-serial bridges that reset on DTR need this or the first command is swallowed.
    /// </summary>
    public TimeSpan OpenSettleDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Optional substring the discovered port name must contain when <see cref="PortName"/> is null.
    /// Handy when several USB serial devices are attached - "OBDLink" or "FTDI" will pin it down on
    /// Linux, where <c>/dev/serial/by-id</c> names carry the manufacturer and serial number.
    /// </summary>
    public string? PortNameFilter { get; set; }
}
