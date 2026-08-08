namespace Shiny.Obd.Commands;

/// <summary>
/// On-Board Monitoring Test Results (Mode 06) - Returns the measured value and pass/fail limits for
/// each test a monitor ran
/// </summary>
/// <remarks>
/// The deepest data OBD-II exposes, and the only mode that answers "how close is this to failing".
/// Everything else reports a state: a code is set or it is not, a monitor is complete or it is not.
/// Mode 06 reports the actual measurement the monitor took and the limits it was judged against, so a
/// catalyst at 92% of its failure threshold looks entirely healthy to
/// <see cref="MonitorStatusCommand"/> and is visibly on its way out here.
///
/// <para>
/// Discover what a vehicle supports with <see cref="OnBoardTestSupportedMidsCommand"/> rather than
/// walking every MID - an unsupported one returns NO DATA, and there are 224 of them.
/// </para>
///
/// <para>
/// Mode 06 is defined for CAN (ISO 15765-4) vehicles. Pre-CAN protocols used a different and largely
/// manufacturer-specific format that this does not attempt to decode; on such a vehicle expect an
/// <see cref="ObdException"/> rather than wrong numbers.
/// </para>
///
/// <para>
/// A single MID commonly answers with several records - one per test the monitor runs - which is why
/// the result is a list rather than a single reading.
/// </para>
/// </remarks>
public class OnBoardTestCommand(byte mid) : IObdCommand<IReadOnlyList<OnBoardTestResult>>
{
    /// <summary>Each record is MID, test ID, unit/scaling ID, then three 16-bit values.</summary>
    const int RecordLength = 9;

    /// <summary>The monitor being queried.</summary>
    public byte Mid { get; } = mid;

    public string RawCommand => $"06{this.Mid:X2}";

    public IReadOnlyList<OnBoardTestResult> Parse(byte[] data)
    {
        if (data.Length < 1 + RecordLength)
            throw new ObdException("On-board test response requires at least 10 data bytes");

        if (data[0] != 0x46)
            throw new ObdException($"Unexpected mode response: 0x{data[0]:X2}, expected 0x46");

        var payload = data.AsSpan(1);
        if (payload.Length % RecordLength != 0)
        {
            throw new ObdException(
                $"On-board test response is {payload.Length} bytes, which is not a whole number of 9-byte records. This vehicle may be answering mode 06 in a pre-CAN format."
            );
        }

        var results = new List<OnBoardTestResult>(payload.Length / RecordLength);
        for (var offset = 0; offset + RecordLength <= payload.Length; offset += RecordLength)
        {
            var record = payload.Slice(offset, RecordLength);

            results.Add(new OnBoardTestResult(
                Mid: record[0],
                TestId: record[1],
                UnitAndScalingId: record[2],
                RawValue: (record[3] << 8) | record[4],
                RawMinimum: (record[5] << 8) | record[6],
                RawMaximum: (record[7] << 8) | record[8]
            ));
        }

        return results;
    }
}

/// <summary>
/// Supported Monitor IDs (Mode 06, MIDs 0x00/0x20/0x40/0x60/0x80/0xA0) - Returns the MIDs the vehicle
/// answers in the 32-MID block following the one queried
/// </summary>
/// <remarks>
/// The mode 06 counterpart of <see cref="SupportedPidsCommand"/>, with the same bitmask layout. Walk
/// <see cref="MonitorIds.BlockMids"/> and stop at the first block the vehicle does not answer.
/// </remarks>
public class OnBoardTestSupportedMidsCommand(byte baseMid) : IObdCommand<IReadOnlyList<byte>>
{
    /// <summary>The block being queried.</summary>
    public byte BaseMid { get; } = baseMid;

    public string RawCommand => $"06{this.BaseMid:X2}";

    public IReadOnlyList<byte> Parse(byte[] data)
    {
        // 0x46, the MID echo, then the four bitmask bytes.
        if (data.Length < 6)
            throw new ObdException("Supported-MID response requires 6 data bytes");

        if (data[0] != 0x46)
            throw new ObdException($"Unexpected mode response: 0x{data[0]:X2}, expected 0x46");

        var supported = new List<byte>(32);
        for (var i = 0; i < 32; i++)
        {
            // MSB-first, matching the mode 01 supported-PID bitmask.
            var isSet = (data[2 + (i / 8)] & (0x80 >> (i % 8))) != 0;
            if (isSet)
                supported.Add((byte)(this.BaseMid + i + 1));
        }
        return supported;
    }
}

/// <summary>One test result from <see cref="OnBoardTestCommand"/>.</summary>
/// <param name="Mid">The monitor that ran the test.</param>
/// <param name="TestId">Which of that monitor's tests this is. Largely manufacturer-defined.</param>
/// <param name="UnitAndScalingId">How to interpret the raw values - see <see cref="UnitAndScaling"/>.</param>
/// <param name="RawValue">The measurement, unscaled.</param>
/// <param name="RawMinimum">The lower limit, unscaled.</param>
/// <param name="RawMaximum">The upper limit, unscaled.</param>
public readonly record struct OnBoardTestResult(
    byte Mid,
    byte TestId,
    byte UnitAndScalingId,
    int RawValue,
    int RawMinimum,
    int RawMaximum
)
{
    /// <summary>The monitor's name, or null when the MID is manufacturer-defined.</summary>
    public string? Monitor => MonitorIds.Describe(this.Mid);

    /// <summary>How this record's values are scaled, or null when the identifier is not in the standard table.</summary>
    public UnitScaling? Scaling => UnitAndScaling.Lookup(this.UnitAndScalingId);

    /// <summary>The measurement in its real unit, or null when the scaling is unknown.</summary>
    public double? Value => this.Scaling?.Apply(this.RawValue);

    /// <summary>The lower limit in its real unit, or null when the scaling is unknown.</summary>
    public double? Minimum => this.Scaling?.Apply(this.RawMinimum);

    /// <summary>The upper limit in its real unit, or null when the scaling is unknown.</summary>
    public double? Maximum => this.Scaling?.Apply(this.RawMaximum);

    /// <summary>The unit the scaled values are in, or null when the scaling is unknown.</summary>
    public string? Unit => this.Scaling?.Unit;

    /// <summary>
    /// Whether the measurement sits inside its limits, or null when the scaling is unknown.
    /// </summary>
    /// <remarks>
    /// Null rather than a raw comparison, because without the scaling there is no way to know whether
    /// the value is signed - and comparing a two's complement negative as unsigned turns a comfortably
    /// passing test into a dramatic failure.
    /// </remarks>
    public bool? Passed => this.Scaling is { } scaling &&
        this.Value is { } value &&
        this.Minimum is { } min &&
        this.Maximum is { } max
            ? value >= min && value <= max
            : null;

    /// <summary>
    /// How far through its pass band the measurement sits, 0 at the lower limit and 1 at the upper -
    /// or null when the scaling is unknown or the limits leave no band.
    /// </summary>
    /// <remarks>
    /// This is the number mode 06 exists for. A result that passes tells you nothing about trend; a
    /// result sitting at 0.95 of its band, compared against the same reading six months ago, is a
    /// component you can schedule rather than wait to fail.
    /// </remarks>
    public double? BandPosition
    {
        get
        {
            if (this.Value is not { } value || this.Minimum is not { } min || this.Maximum is not { } max)
                return null;

            var span = max - min;
            return span > 0 ? (value - min) / span : null;
        }
    }
}
