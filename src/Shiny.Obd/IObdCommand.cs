namespace Shiny.Obd;

/// <summary>
/// Represents an OBD command that can be executed against a vehicle.
/// Implement this interface directly for custom/non-standard commands.
/// </summary>
/// <typeparam name="T">The type of the parsed result</typeparam>
public interface IObdCommand<T>
{
    /// <summary>
    /// The raw command string to send to the ELM327 adapter (e.g. "010D" for vehicle speed)
    /// </summary>
    string RawCommand { get; }

    /// <summary>
    /// Parse the response bytes into the expected result type.
    /// For standard OBD commands using <see cref="ObdCommand{T}"/>, this receives
    /// all response bytes including the mode+PID header.
    /// </summary>
    T Parse(byte[] data);
}
