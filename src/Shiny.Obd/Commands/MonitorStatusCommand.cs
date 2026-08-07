namespace Shiny.Obd.Commands;

/// <summary>
/// Monitor Status Since Codes Cleared (Mode 01, PID 0x01) - Returns the malfunction indicator lamp
/// state, the stored code count and the emissions monitor readiness flags
/// </summary>
/// <remarks>
/// The cheapest question worth asking a vehicle: it says whether there is anything to read modes
/// 03/07/0A for at all, and whether the car would pass an emissions inspection today. See
/// <see cref="MonitorStatusDecoder"/> for the bit layout.
/// </remarks>
public class MonitorStatusCommand() : ObdCommand<MonitorStatus>(0x01, 0x01)
{
    protected override MonitorStatus ParseData(byte[] data) => MonitorStatusDecoder.Decode(data);
}

/// <summary>
/// Monitor Status This Drive Cycle (Mode 01, PID 0x41) - Returns the emissions monitor readiness
/// flags for the current drive cycle only
/// </summary>
/// <remarks>
/// Same bit layout as <see cref="MonitorStatusCommand"/>, but byte A is reserved — so
/// <see cref="MonitorStatus.MilOn"/> and <see cref="MonitorStatus.DtcCount"/> always read false and
/// zero here, and only <see cref="MonitorStatus.Monitors"/> means anything. Use it to watch a
/// monitor complete during the drive you are on; use PID 0x01 for the state since codes were
/// cleared.
/// </remarks>
public class MonitorStatusThisDriveCycleCommand() : ObdCommand<MonitorStatus>(0x01, 0x41)
{
    protected override MonitorStatus ParseData(byte[] data) => MonitorStatusDecoder.Decode(data);
}
