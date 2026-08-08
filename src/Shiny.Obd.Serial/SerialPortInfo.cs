namespace Shiny.Obd.Serial;

/// <summary>
/// A serial port discovered by <see cref="SerialPortEnumerator"/>.
/// </summary>
/// <param name="PortName">The path/name to hand to <c>SerialPort</c> (e.g. <c>/dev/ttyUSB0</c>, <c>COM3</c>).</param>
/// <param name="Description">
/// A human-readable identity for the port. On Linux this is the <c>/dev/serial/by-id</c> link name,
/// which carries the vendor, product and USB serial number; elsewhere it falls back to the port name.
/// </param>
/// <param name="StablePath">
/// A path that survives re-enumeration, when the platform offers one (Linux <c>by-id</c>). Null when
/// only the volatile name is available.
/// </param>
/// <param name="IsLikelyAdapter">
/// Whether the description matches a known OBD adapter or a USB-serial bridge of the kind they use.
/// Discovery does not connect to anything, so this is a hint for ordering candidates, not a promise.
/// </param>
public record SerialPortInfo(
    string PortName,
    string Description,
    string? StablePath,
    bool IsLikelyAdapter
)
{
    public override string ToString()
        => this.PortName == this.Description
            ? this.PortName
            : $"{this.PortName} ({this.Description})";
}
