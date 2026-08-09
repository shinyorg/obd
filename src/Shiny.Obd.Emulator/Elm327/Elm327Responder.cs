using System.Globalization;
using System.Text;
using Shiny.Obd.Commands;

namespace Shiny.Obd.Emulator;

/// <summary>
/// Turns an ELM327 command line into the reply a simulated adapter would send. Transport-agnostic on
/// purpose - the BLE GATT server and the TCP server both hand their bytes to this and write back
/// whatever it returns.
/// </summary>
public sealed class Elm327Responder(ObdEmulatorState state)
{
    /// <summary>A CAN frame carries seven payload bytes; anything longer is split across frames.</summary>
    const int SingleFrameBytes = 7;

    public ObdExchange Respond(Elm327Session session, string raw)
    {
        var request = Normalise(raw);

        if (request.Length == 0)
            return new ObdExchange(request, [], "empty command");

        return request.StartsWith("AT", StringComparison.Ordinal)
            ? this.At(session, request)
            : this.Obd(session, request);
    }

    // ---- AT commands ------------------------------------------------------------------------------

    ObdExchange At(Elm327Session session, string request)
    {
        var command = request[2..];

        switch (command)
        {
            case "Z":
            case "WS":
            case "D":
                session.Reset();
                return Ack(request, [state.AdapterIdentifier], "reset");

            case "I":
                return Ack(request, [state.AdapterIdentifier], "identify");

            case "@1":
                return Ack(request, [state.DeviceDescription], "device description");

            case "RV":
                return Ack(request, [this.BatteryVoltage()], "battery voltage");

            case "DP":
                return Ack(request, ["AUTO, ISO 15765-4 (CAN 11/500)"], "describe protocol");

            case "DPN":
                return Ack(request, ["A6"], "describe protocol by number");
        }

        if (TryToggle(command, "E", out var echo))
        {
            session.Echo = echo;
            return Ack(request, ["OK"], $"echo {OnOff(echo)}");
        }

        if (TryToggle(command, "L", out var linefeeds))
        {
            session.Linefeeds = linefeeds;
            return Ack(request, ["OK"], $"linefeeds {OnOff(linefeeds)}");
        }

        if (TryToggle(command, "S", out var spaces))
        {
            session.Spaces = spaces;
            return Ack(request, ["OK"], $"spaces {OnOff(spaces)}");
        }

        if (TryToggle(command, "H", out var headers))
        {
            session.Headers = headers;
            return Ack(request, ["OK"], $"headers {OnOff(headers)}");
        }

        if (command.StartsWith("SP", StringComparison.Ordinal))
        {
            session.Protocol = command[2..];
            session.HasSearched = false;
            return Ack(request, ["OK"], $"protocol set to {session.Protocol}");
        }

        // Everything else - timings, filters, CAN formatting, low power. A real ELM327 answers '?' to
        // anything it does not know, but a simulator that rejects an unfamiliar init string just makes
        // the app under test look broken. Answer OK and surface it in the log instead.
        return Ack(request, ["OK"], "unhandled AT command, answered OK");
    }

    string BatteryVoltage()
    {
        var volts = state.Find(0x01, 0x42)?.Number ?? 12.6;
        return volts.ToString("0.0", CultureInfo.InvariantCulture) + "V";
    }

    // ---- OBD modes --------------------------------------------------------------------------------

    ObdExchange Obd(Elm327Session session, string request)
    {
        if (request.Length < 2 || !TryByte(request, 0, out var mode))
            return new ObdExchange(request, ["?"], "not a command this adapter understands");

        if (state.EngineOff)
            return new ObdExchange(request, ["UNABLE TO CONNECT"], "ignition is off");

        var prefix = new List<string>();
        if (!session.HasSearched)
        {
            // Exactly once per session, as a real adapter does while it negotiates the bus. Clients are
            // expected to skip this line - Shiny's response parser does.
            prefix.Add("SEARCHING...");
            session.HasSearched = true;
        }

        var (data, description) = mode switch
        {
            0x01 => this.LiveData(request),
            0x02 => this.FreezeFrame(request),
            0x03 or 0x07 or 0x0A => this.Dtcs(mode),
            0x04 => this.ClearDtcs(),
            0x06 => this.OnBoardTests(request),
            0x09 => this.VehicleInfo(request),
            _ => (null, $"mode {mode:X2} not supported")
        };

        if (data == null)
            return new ObdExchange(request, [.. prefix, "NO DATA"], description);

        return new ObdExchange(
            request,
            [.. prefix, .. this.Frame(session, data)],
            description,
            IsData: true
        );
    }

    (byte[]? Data, string Description) LiveData(string request)
    {
        if (!TryByte(request, 2, out var pid))
            return (null, "mode 01 with no PID");

        if (SupportedPidsCommand.BlockPids.Contains(pid))
        {
            var mask = state.SupportedMask(0x01, pid);
            return ([0x41, pid, .. mask], $"supported PIDs {pid:X2}01-{pid + 0x20:X2}");
        }

        var parameter = state.Find(0x01, pid);
        if (parameter is not { IsSupported: true })
            return (null, $"PID {pid:X2} not supported by this vehicle");

        return (parameter.Response(), $"{parameter.Name} = {parameter.Readback}");
    }

    (byte[]? Data, string Description) FreezeFrame(string request)
    {
        if (!TryByte(request, 2, out var pid))
            return (null, "mode 02 with no PID");

        // The frame number is optional on the wire; frame 0 is the only one vehicles store in practice.
        var frame = TryByte(request, 4, out var parsed) ? parsed : (byte)0x00;

        if (!state.FreezeFrameCaptured)
            return (null, "no freeze frame stored");

        if (frame != 0x00)
            return (null, $"freeze frame {frame:X2} not stored");

        if (pid == 0x02)
        {
            if (!ObdEmulatorState.TryEncodeDtc(state.FreezeFrameDtc, out var a, out var b))
                return ([0x42, 0x02, frame, 0x00, 0x00], "freeze frame DTC is empty");

            return ([0x42, 0x02, frame, a, b], $"freeze frame caused by {state.FreezeFrameDtc}");
        }

        var parameter = state.Find(0x01, pid);
        if (parameter is not { IsSupported: true })
            return (null, $"PID {pid:X2} has no freeze frame");

        // Mode 02 reuses the mode 01 payload and scaling; only the header differs - it carries the
        // frame number as a third byte.
        return ([0x42, pid, frame, .. parameter.Payload()], $"freeze frame {parameter.Name} = {parameter.Readback}");
    }

    (byte[]? Data, string Description) Dtcs(byte mode)
    {
        var codes = state.DtcsFor(mode);
        var label = mode switch
        {
            0x03 => "stored",
            0x07 => "pending",
            _ => "permanent"
        };

        var response = (byte)(mode + 0x40);
        return (state.EncodeDtcs(response, codes), $"{codes.Count} {label} DTC(s)");
    }

    (byte[]? Data, string Description) ClearDtcs()
    {
        state.ClearDtcs();
        return ([0x44], "cleared stored and pending DTCs, MIL off");
    }

    (byte[]? Data, string Description) OnBoardTests(string request)
    {
        if (!TryByte(request, 2, out var mid))
            return (null, "mode 06 with no MID");

        if (MonitorIds.BlockMids.Contains(mid))
        {
            var mask = state.SupportedMask(0x06, mid);
            return ([0x46, mid, .. mask], $"supported MIDs {mid:X2}01-{mid + 0x20:X2}");
        }

        var parameter = state.Find(0x06, mid);
        if (parameter is not { IsSupported: true })
            return (null, $"MID {mid:X2} not supported");

        return (parameter.Response(), $"{parameter.Name}: {parameter.Readback}");
    }

    (byte[]? Data, string Description) VehicleInfo(string request)
    {
        if (!TryByte(request, 2, out var pid))
            return (null, "mode 09 with no PID");

        if (pid == 0x00)
        {
            var mask = state.SupportedMask(0x09, pid);
            return ([0x49, 0x00, .. mask], "supported mode 09 PIDs 01-20");
        }

        var parameter = state.Find(0x09, pid);
        if (parameter is not { IsSupported: true })
            return (null, $"mode 09 PID {pid:X2} not supported");

        return (parameter.Response(), $"{parameter.Name} = {parameter.Readback}");
    }

    // ---- Framing ----------------------------------------------------------------------------------

    /// <summary>
    /// Lays bytes out the way an ELM327 prints them. Anything over one CAN frame gets the byte-count
    /// line and numbered frames that a multi-frame reply really carries - the shape Shiny's response
    /// parser has to strip back off, and the reason a VIN read is worth testing against this.
    /// </summary>
    IReadOnlyList<string> Frame(Elm327Session session, byte[] data)
    {
        if (data.Length <= SingleFrameBytes)
            return [Hex(session, data)];

        var lines = new List<string> { data.Length.ToString("X3", CultureInfo.InvariantCulture) };

        // The first frame gives up a byte to the length field, so it carries six; the rest carry seven.
        var offset = 0;
        var index = 0;
        while (offset < data.Length)
        {
            var take = Math.Min(index == 0 ? SingleFrameBytes - 1 : SingleFrameBytes, data.Length - offset);
            lines.Add($"{index:X}: {Hex(session, data.AsSpan(offset, take))}");

            offset += take;
            index = (index + 1) & 0x0F;
        }

        return lines;
    }

    static string Hex(Elm327Session session, ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length * 3);
        for (var i = 0; i < data.Length; i++)
        {
            if (session.Spaces && i > 0)
                sb.Append(' ');

            sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    // ---- Parsing ----------------------------------------------------------------------------------

    /// <summary>
    /// Strips the whitespace an ELM327 ignores and upper-cases the rest. Clients send "01 0C" and
    /// "010c" interchangeably, and adapters accept both.
    /// </summary>
    static string Normalise(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (!Char.IsWhiteSpace(c) && c != '>')
                sb.Append(Char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    static bool TryByte(string request, int offset, out byte value)
    {
        value = 0;
        if (offset + 2 > request.Length)
            return false;

        return Byte.TryParse(
            request.AsSpan(offset, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    /// <summary>Matches ATx0 / ATx1 style switches.</summary>
    static bool TryToggle(string command, string letter, out bool on)
    {
        on = false;
        if (command.Length != letter.Length + 1 || !command.StartsWith(letter, StringComparison.Ordinal))
            return false;

        var digit = command[^1];
        if (digit is not ('0' or '1'))
            return false;

        on = digit == '1';
        return true;
    }

    static string OnOff(bool value) => value ? "on" : "off";

    static ObdExchange Ack(string request, IReadOnlyList<string> lines, string description)
        => new(request, lines, description);
}
