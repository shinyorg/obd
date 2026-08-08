using System.Collections.ObjectModel;
using Shiny.Obd.Commands;

namespace Sample.Maui.Emulator;

/// <summary>
/// Everything the simulated vehicle knows about itself: which PIDs it answers, what each one reads,
/// the fault memory, and the adapter identity it reports to <c>ATI</c>.
/// </summary>
/// <remarks>
/// Registered as a singleton and shared by every front-end. A value edited on the Parameters tab is
/// live on the next request over BLE <i>and</i> TCP - there is one vehicle, not one per transport.
/// </remarks>
public partial class ObdEmulatorState : ObservableObject
{
    readonly Dictionary<int, ObdParameter> lookup;

    public ObdEmulatorState()
    {
        this.Parameters = ObdParameterCatalog.Build(this);
        this.lookup = this.Parameters.ToDictionary(Key);

        foreach (var code in new[] { "P0301", "P0420" })
            this.StoredDtcs.Add(code);

        this.PendingDtcs.Add("P0171");
    }

    public List<ObdParameter> Parameters { get; }

    public ObservableCollection<string> StoredDtcs { get; } = [];
    public ObservableCollection<string> PendingDtcs { get; } = [];
    public ObservableCollection<string> PermanentDtcs { get; } = [];

    /// <summary>What the adapter answers to ATI and AT@1. Say "STN" here to exercise the ObdLink profile.</summary>
    [ObservableProperty] string adapterIdentifier = "ELM327 v1.5";

    [ObservableProperty] string deviceDescription = "OBDII to RS232 Interpreter";

    /// <summary>Malfunction indicator lamp. Drives bit 7 of PIDs 01 and 41.</summary>
    [ObservableProperty] bool milOn = true;

    /// <summary>When false the readiness monitors report as still running, which is what fails an inspection.</summary>
    [ObservableProperty] bool monitorsComplete;

    /// <summary>Compression ignition changes which monitor set bytes C and D describe.</summary>
    [ObservableProperty] bool compressionIgnition;

    /// <summary>Whether a mode 02 snapshot exists. With this off, every freeze frame read answers NO DATA.</summary>
    [ObservableProperty] bool freezeFrameCaptured = true;

    /// <summary>The DTC that caused the stored freeze frame.</summary>
    [ObservableProperty] string freezeFrameDtc = "P0301";

    /// <summary>Answer every request with NO DATA, as an adapter does when the ignition is off.</summary>
    [ObservableProperty] bool engineOff;

    public ObdParameter? Find(byte mode, byte pid)
        => this.lookup.GetValueOrDefault((mode << 8) | pid);

    /// <summary>
    /// Whether the vehicle claims support for a PID in a supported-PID bitmask. A block PID (20, 40, …)
    /// is itself "supported" when anything above it is, which is how a client knows to keep walking.
    /// </summary>
    public bool IsSupported(byte mode, byte pid)
    {
        if (this.Find(mode, pid) is { IsSupported: true })
            return true;

        if (pid % 0x20 != 0 || pid == 0)
            return false;

        return this.Parameters.Any(x => x.Mode == mode && x.IsSupported && x.Pid > pid);
    }

    /// <summary>
    /// The four-byte bitmask for a supported-PID block. Bit 31 - the most significant bit of the first
    /// byte - is <paramref name="basePid"/> + 1, descending to + 32; getting that order backwards is
    /// the classic way to advertise a completely different set of PIDs than the one you support.
    /// </summary>
    public byte[] SupportedMask(byte mode, byte basePid)
    {
        var mask = new byte[4];
        for (var i = 0; i < 32; i++)
        {
            var pid = basePid + i + 1;
            if (pid > 0xFF)
                break;

            if (this.IsSupported(mode, (byte)pid))
                mask[i / 8] |= (byte)(0x80 >> (i % 8));
        }
        return mask;
    }

    /// <summary>
    /// PID 01/41: MIL state, stored DTC count, and the readiness monitors, laid out the way
    /// <see cref="MonitorStatusDecoder"/> reads them back.
    /// </summary>
    public byte[] EncodeMonitorStatus()
    {
        var a = (byte)((this.MilOn ? 0x80 : 0x00) | Math.Min(this.StoredDtcs.Count, 0x7F));

        // Bits 0-2 are the three common monitors, all supported. Bits 4-6 are set while the matching
        // test is still running, so an incomplete monitor is a *set* bit - the inverse of "ready".
        var b = (byte)(0x07 | (this.CompressionIgnition ? 0x08 : 0x00));
        if (!this.MonitorsComplete)
            b |= 0x70;

        // Bits 7/6/5 are EGR-VVT, O2 heater and O2 sensor on spark ignition; bit 0 is the catalyst.
        // The compression set skips bits 4 and 2, which are reserved there.
        var supported = (byte)(this.CompressionIgnition ? 0xEB : 0xE1);
        var incomplete = this.MonitorsComplete ? (byte)0x00 : supported;

        return [a, b, supported, incomplete];
    }

    /// <summary>Mode 03/07/0A payload: the response mode, a code count, then one pair per code.</summary>
    public byte[] EncodeDtcs(byte responseMode, IReadOnlyList<string> codes)
    {
        var bytes = new List<byte> { responseMode, (byte)codes.Count };
        foreach (var code in codes)
        {
            if (TryEncodeDtc(code, out var a, out var b))
                bytes.AddRange([a, b]);
        }
        return bytes.ToArray();
    }

    public IReadOnlyList<string> DtcsFor(byte mode) => mode switch
    {
        0x03 => this.StoredDtcs,
        0x07 => this.PendingDtcs,
        0x0A => this.PermanentDtcs,
        _ => []
    };

    /// <summary>Mode 04. Clears stored and pending codes and drops the lamp; permanent codes survive, as on a real vehicle.</summary>
    public void ClearDtcs()
    {
        this.StoredDtcs.Clear();
        this.PendingDtcs.Clear();
        this.MilOn = false;
        this.FreezeFrameCaptured = false;
        this.RefreshDerived();
    }

    /// <summary>Nudges the derived parameters so the Parameters tab reflects a fault-state change.</summary>
    public void RefreshDerived()
    {
        foreach (var parameter in this.Parameters)
        {
            if (parameter.Kind == ObdValueKind.Derived)
                parameter.RefreshReadback();
        }
    }

    /// <summary>The inverse of <see cref="DtcDecoder.DecodePair"/>: "P0301" back into its two bytes.</summary>
    public static bool TryEncodeDtc(string? code, out byte a, out byte b)
    {
        a = 0;
        b = 0;

        if (code is not { Length: 5 })
            return false;

        var system = "PCBU".IndexOf(Char.ToUpperInvariant(code[0]));
        if (system < 0)
            return false;

        Span<int> digits = stackalloc int[4];
        for (var i = 0; i < 4; i++)
        {
            var c = code[i + 1];
            if (!Uri.IsHexDigit(c))
                return false;

            digits[i] = Convert.ToInt32(Char.ToUpperInvariant(c).ToString(), 16);
        }

        // The first digit after the letter only has two bits to live in, so P4xxx is not encodable.
        if (digits[0] > 3)
            return false;

        a = (byte)((system << 6) | (digits[0] << 4) | digits[1]);
        b = (byte)((digits[2] << 4) | digits[3]);
        return true;
    }

    static int Key(ObdParameter parameter) => (parameter.Mode << 8) | parameter.Pid;

    partial void OnMilOnChanged(bool value) => this.RefreshDerived();
    partial void OnMonitorsCompleteChanged(bool value) => this.RefreshDerived();
    partial void OnCompressionIgnitionChanged(bool value) => this.RefreshDerived();
}
