using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;

namespace Shiny.Obd.Serial;

/// <summary>
/// Cross-platform serial port discovery for Windows, Linux and macOS.
/// </summary>
/// <remarks>
/// <see cref="SerialPort.GetPortNames"/> alone is not enough to drive an appliance from.
/// On Linux it enumerates every <c>/dev/tty*</c>, including the ~64 virtual consoles, and gives back
/// bare device names that are assigned in USB enumeration order - so the OBD adapter is
/// <c>ttyUSB0</c> today and <c>ttyUSB1</c> tomorrow because the GNSS puck happened to enumerate
/// first. On macOS it returns both the <c>tty.</c> and <c>cu.</c> node for every device plus the
/// Bluetooth serial ports.
///
/// This class filters that down to ports a USB OBD adapter could plausibly be on, and on Linux
/// prefers the <c>/dev/serial/by-id</c> symlinks, which are derived from the USB descriptor and
/// therefore stable across reboots.
/// </remarks>
public static class SerialPortEnumerator
{
    const string LinuxByIdDirectory = "/dev/serial/by-id";

    /// <summary>
    /// Substrings that identify an OBD adapter, or the USB-serial bridge chips they are built on.
    /// Matched case-insensitively against <see cref="SerialPortInfo.Description"/>.
    /// </summary>
    /// <remarks>
    /// The bridge chips are deliberately included even though they are not OBD-specific - a genuine
    /// OBDLink SX presents as a stock FTDI device with no OBD branding in its USB descriptor, so
    /// matching only on "OBD" would skip the best adapter on the list.
    /// </remarks>
    /// <remarks>
    /// Written without separators because they are matched against a normalized description - see
    /// <see cref="IsLikelyAdapter"/>.
    /// </remarks>
    static readonly string[] AdapterHints =
    [
        "obd", "elm327", "obdlink", "stn11", "stn21", "scantool", "vgate", "veepeak",
        "ftdi", "ft232", "ch340", "ch341", "cp210", "pl2303", "usbserial", "usbuart"
    ];

    /// <summary>
    /// Discover candidate serial ports, most likely adapter first.
    /// </summary>
    public static IReadOnlyList<SerialPortInfo> Discover()
    {
        var ports = Enumerate()
            .GroupBy(x => x.PortName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(x => x.IsLikelyAdapter)
            .ThenBy(x => x.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ports;
    }

    static IEnumerable<SerialPortInfo> Enumerate()
    {
        if (OperatingSystem.IsLinux())
            return EnumerateLinux();

        if (OperatingSystem.IsMacOS())
            return EnumerateMacOS();

        return EnumerateWindows();
    }

    static IEnumerable<SerialPortInfo> EnumerateLinux()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // by-id first: the link name is built from the USB descriptor, so it both survives a reboot
        // and tells us who made the thing.
        if (Directory.Exists(LinuxByIdDirectory))
        {
            foreach (var link in SafeEnumerateFiles(LinuxByIdDirectory))
            {
                var target = ResolveLink(link) ?? link;
                if (!seen.Add(target))
                    continue;

                var description = Path.GetFileName(link);
                yield return new SerialPortInfo(
                    target,
                    description,
                    link,
                    IsLikelyAdapter(description)
                );
            }
        }

        // ttyUSB/ttyACM are the USB bridges; ttyAMA0 and serial0 are the Pi's own header UART, which
        // is where a directly-wired adapter or a GNSS module lands.
        foreach (var pattern in new[] { "ttyUSB*", "ttyACM*", "ttyAMA*", "serial*" })
        {
            foreach (var dev in SafeEnumerateFiles("/dev", pattern))
            {
                var resolved = ResolveLink(dev) ?? dev;
                if (!seen.Add(resolved))
                    continue;

                yield return new SerialPortInfo(
                    resolved,
                    Path.GetFileName(dev),
                    null,
                    resolved.Contains("ttyUSB", StringComparison.Ordinal) ||
                    resolved.Contains("ttyACM", StringComparison.Ordinal)
                );
            }
        }
    }

    static IEnumerable<SerialPortInfo> EnumerateMacOS()
    {
        // Only the cu.* ("call-up") nodes. Opening the matching tty.* node blocks until the device
        // asserts carrier detect, which a USB-serial bridge never does - the open would just hang.
        foreach (var dev in SafeEnumerateFiles("/dev", "cu.*"))
        {
            var name = Path.GetFileName(dev);

            // Every Mac has these two and neither is ever an OBD adapter.
            if (name.Contains("Bluetooth-Incoming-Port", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("debug-console", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new SerialPortInfo(
                dev,
                name,
                null,
                IsLikelyAdapter(name) ||
                name.Contains("usbserial", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("usbmodem", StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    static IEnumerable<SerialPortInfo> EnumerateWindows()
    {
        // GetPortNames is backed by the SERIALCOMM device map here and is both accurate and cheap.
        foreach (var name in SafeGetPortNames())
            yield return new SerialPortInfo(name, name, null, true);
    }

    /// <summary>
    /// Whether the port description names an OBD adapter or a USB-serial bridge of the kind they use.
    /// </summary>
    /// <remarks>
    /// Matching is done on a letters-and-digits-only form of the description, because the same
    /// device is punctuated differently everywhere it appears: a CH340 is
    /// <c>usb-1a86_USB_Serial-if00-port0</c> in Linux's by-id, <c>cu.usbserial-1420</c> on macOS and
    /// "USB Serial Port" on Windows. Normalizing collapses all three onto one hint.
    /// </remarks>
    internal static bool IsLikelyAdapter(string description)
    {
        var normalized = Normalize(description);
        return AdapterHints.Any(h => normalized.Contains(h, StringComparison.Ordinal));
    }

    static string Normalize(string value)
    {
        var buffer = new char[value.Length];
        var length = 0;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                buffer[length++] = char.ToLowerInvariant(c);
        }

        return new string(buffer, 0, length);
    }

    static string? ResolveLink(string path)
    {
        try
        {
            // Follows a whole chain (by-id -> ../../ttyUSB0), not just one hop.
            var resolved = File.ResolveLinkTarget(path, true);
            return resolved?.FullName;
        }
        catch
        {
            // Not a link, or a dangling one. Either way the caller falls back to the literal path.
            return null;
        }
    }

    /// <summary>
    /// Enumeration must never be the thing that takes the service down: /dev entries come and go as
    /// USB devices are plugged and unplugged, and a container may not expose the directory at all.
    /// </summary>
    static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern = "*")
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.GetFileSystemEntries(directory, pattern)
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    static string[] SafeGetPortNames()
    {
        try
        {
            return SerialPort.GetPortNames();
        }
        catch (PlatformNotSupportedException)
        {
            return [];
        }
    }

    /// <summary>
    /// The platform's name for the situation, for log lines and error messages.
    /// </summary>
    internal static string PlatformHint => RuntimeInformation.OSDescription;
}
