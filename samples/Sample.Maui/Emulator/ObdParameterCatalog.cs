using System.Text;
using Shiny.Obd;
using Shiny.Obd.Commands;

namespace Sample.Maui.Emulator;

/// <summary>
/// Every command <c>Shiny.Obd</c> can issue, paired with an encoder that is the exact inverse of that
/// command's parser.
/// </summary>
/// <remarks>
/// The command instance is carried alongside each entry so <see cref="ObdParameter.Readback"/> can
/// decode the emulator's own bytes with the real parser. Keep the two halves together when adding a
/// PID: an encoder with no matching command is an encoder nothing checks.
/// </remarks>
public static class ObdParameterCatalog
{
    public static List<ObdParameter> Build(ObdEmulatorState state)
    {
        var items = new List<ObdParameter>
        {
            // ---- Mode 01: live data -------------------------------------------------------------
            Derived(
                "Monitor status since DTCs cleared", 0x01,
                state.EncodeMonitorStatus, StandardCommands.MonitorStatus,
                "Follows the MIL switch, the stored DTC count and the readiness toggle on the Faults tab."
            ),
            Hex(
                "Fuel system status", 0x03, "02 00", StandardCommands.FuelSystemStatus,
                "Byte A is system 1, byte B system 2. 01 open loop (cold), 02 closed loop, 04 open loop (load), 08 open loop (failure), 10 closed loop with fault."
            ),
            Num("Calculated engine load", 0x04, "%", 0, 100, 32.5, Percent255, StandardCommands.CalculatedEngineLoad),
            Num("Coolant temperature", 0x05, "°C", -40, 215, 88, TempOffset40, StandardCommands.CoolantTemperature),
            Num("Short term fuel trim bank 1", 0x06, "%", -100, 99.2, 1.6, Trim128, FuelTrimCommand.ShortTermBank1()),
            Num("Long term fuel trim bank 1", 0x07, "%", -100, 99.2, -2.3, Trim128, FuelTrimCommand.LongTermBank1()),
            Num("Short term fuel trim bank 2", 0x08, "%", -100, 99.2, 0.8, Trim128, FuelTrimCommand.ShortTermBank2()),
            Num("Long term fuel trim bank 2", 0x09, "%", -100, 99.2, -1.6, Trim128, FuelTrimCommand.LongTermBank2()),
            Num("Fuel pressure", 0x0A, "kPa", 0, 765, 300, v => U8(v / 3.0), StandardCommands.FuelPressure),
            Num("Intake manifold pressure", 0x0B, "kPa", 0, 255, 35, U8, StandardCommands.IntakeManifoldPressure),
            Num("Engine RPM", 0x0C, "rpm", 0, 16383, 850, v => U16(v * 4.0), StandardCommands.EngineRpm),
            Num("Vehicle speed", 0x0D, "km/h", 0, 255, 0, U8, StandardCommands.VehicleSpeed),
            Num("Timing advance", 0x0E, "°", -64, 63.5, 12, v => U8((v + 64.0) * 2.0), StandardCommands.TimingAdvance),
            Num("Intake air temperature", 0x0F, "°C", -40, 215, 30, TempOffset40, StandardCommands.IntakeAirTemperature),
            Num("Mass air flow", 0x10, "g/s", 0, 655.35, 4.2, v => U16(v * 100.0), StandardCommands.MassAirFlow),
            Num("Throttle position", 0x11, "%", 0, 100, 14, Percent255, StandardCommands.ThrottlePosition),
            Hex(
                "Oxygen sensors present (2 banks)", 0x13, "03", OxygenSensorsPresentCommand.TwoBanks(),
                "Bitmask, bit 0 is sensor 1 ascending. Two banks of four: 03 = B1S1 and B1S2."
            ),
            Num("OBD standards", 0x1C, "code", 0, 35, 6, U8, StandardCommands.ObdStandards, "1 CARB OBD-II, 6 EOBD, 7 EOBD+OBD-II, 10 JOBD."),
            Hex(
                "Oxygen sensors present (4 banks)", 0x1D, "05", OxygenSensorsPresentCommand.FourBanks(),
                "Bitmask, bit 0 is sensor 1 ascending. Four banks of two: 05 = B1S1 and B2S1."
            ),
            Num("Run time since engine start", 0x1F, "s", 0, 65535, 1260, U16, StandardCommands.RuntimeSinceStart),
            Num("Distance travelled with MIL on", 0x21, "km", 0, 65535, 0, U16, StandardCommands.DistanceWithMilOn),
            Num("Fuel rail pressure", 0x22, "kPa", 0, 5177, 380, v => U16(v / 0.079), StandardCommands.FuelRailPressure),
            Num("Fuel rail gauge pressure", 0x23, "kPa", 0, 655350, 25000, v => U16(v / 10.0), StandardCommands.FuelRailGaugePressure),
            Num("Commanded EGR", 0x2C, "%", 0, 100, 0, Percent255, StandardCommands.CommandedEgr),
            Num("EGR error", 0x2D, "%", -100, 99.2, 0, Trim128, StandardCommands.EgrError),
            Num("Commanded evaporative purge", 0x2E, "%", 0, 100, 0, Percent255, StandardCommands.CommandedEvaporativePurge),
            Num("Fuel tank level", 0x2F, "%", 0, 100, 62, Percent255, StandardCommands.FuelLevel),
            Num("Warm-ups since codes cleared", 0x30, "count", 0, 255, 12, U8, StandardCommands.WarmUpsSinceCodesCleared),
            Num("Distance since codes cleared", 0x31, "km", 0, 65535, 420, U16, StandardCommands.DistanceSinceCodesCleared),
            Num("Evap system vapor pressure", 0x32, "Pa", -8192, 8191.75, -25, v => S16(v * 4.0), StandardCommands.EvapVaporPressure),
            Num("Barometric pressure", 0x33, "kPa", 0, 255, 101, U8, StandardCommands.BarometricPressure),
            Derived(
                "Monitor status this drive cycle", 0x41,
                state.EncodeMonitorStatus, StandardCommands.MonitorStatusThisDriveCycle,
                "Shares the readiness state with PID 01."
            ),
            Num("Control module voltage", 0x42, "V", 0, 65.5, 14.1, v => U16(v * 1000.0), StandardCommands.ControlModuleVoltage),
            Num("Absolute load value", 0x43, "%", 0, 25700, 24, v => U16(v * 255.0 / 100.0), StandardCommands.AbsoluteLoadValue),
            Num("Commanded air-fuel ratio", 0x44, "λ", 0, 1.999, 1.0, Lambda, StandardCommands.CommandedAirFuelRatio),
            Num("Relative throttle position", 0x45, "%", 0, 100, 8, Percent255, StandardCommands.RelativeThrottlePosition),
            Num("Ambient air temperature", 0x46, "°C", -40, 215, 21, TempOffset40, StandardCommands.AmbientAirTemperature),
            Num("Absolute throttle position B", 0x47, "%", 0, 100, 18, Percent255, AbsoluteThrottlePositionCommand.B()),
            Num("Absolute throttle position C", 0x48, "%", 0, 100, 14, Percent255, AbsoluteThrottlePositionCommand.C()),
            Num("Accelerator pedal position D", 0x49, "%", 0, 100, 15, Percent255, AcceleratorPedalPositionCommand.D()),
            Num("Accelerator pedal position E", 0x4A, "%", 0, 100, 8, Percent255, AcceleratorPedalPositionCommand.E()),
            Num("Accelerator pedal position F", 0x4B, "%", 0, 100, 0, Percent255, AcceleratorPedalPositionCommand.F()),
            Num("Commanded throttle actuator", 0x4C, "%", 0, 100, 12, Percent255, StandardCommands.CommandedThrottleActuator),
            Num("Time run with MIL on", 0x4D, "min", 0, 65535, 0, U16, StandardCommands.TimeRunWithMilOn),
            Num("Time since codes cleared", 0x4E, "min", 0, 65535, 2400, U16, StandardCommands.TimeSinceCodesCleared),
            Num("Fuel type", 0x51, "code", 0, 23, 1, U8, StandardCommands.FuelType, "1 gasoline, 4 diesel, 8 electric, 17 hybrid gasoline."),
            Num("Ethanol fuel percent", 0x52, "%", 0, 100, 10, Percent255, StandardCommands.EthanolFuelPercent),
            Num("Absolute evap vapor pressure", 0x53, "kPa", 0, 327.675, 32, v => U16(v * 200.0), StandardCommands.AbsoluteEvapVaporPressure),
            Num("Evap vapor pressure (wide range)", 0x54, "Pa", -32768, 32767, -30, S16, StandardCommands.EvapVaporPressureWideRange),
            Num("Fuel rail absolute pressure", 0x59, "kPa", 0, 655350, 38000, v => U16(v / 10.0), StandardCommands.FuelRailAbsolutePressure),
            Num("Relative accelerator pedal position", 0x5A, "%", 0, 100, 12, Percent255, StandardCommands.RelativeAcceleratorPedalPosition),
            Num("Hybrid battery remaining life", 0x5B, "%", 0, 100, 0, Percent255, StandardCommands.HybridBatteryLife),
            Num("Engine oil temperature", 0x5C, "°C", -40, 215, 95, TempOffset40, StandardCommands.EngineOilTemperature),
            Num("Fuel injection timing", 0x5D, "°", -210, 301.99, 3.5, v => U16((v + 210.0) * 128.0), StandardCommands.FuelInjectionTiming),
            Num("Engine fuel rate", 0x5E, "L/h", 0, 3276.75, 2.4, v => U16(v * 20.0), StandardCommands.EngineFuelRate),
            Num("Driver's demand engine torque", 0x61, "%", -125, 130, 22, TorquePercent, StandardCommands.DriverDemandTorque),
            Num("Actual engine torque", 0x62, "%", -125, 130, 24, TorquePercent, StandardCommands.ActualEngineTorque),
            Num("Engine reference torque", 0x63, "N·m", 0, 65535, 320, U16, StandardCommands.ReferenceTorque),
            Hex(
                "Engine percent torque data", 0x64, "7D 8C 96 A0 AA", StandardCommands.EnginePercentTorqueData,
                "Five bytes: idle, then torque at points 1-4. Each is percent + 125, so 7D is 0%."
            ),
            Hex(
                "Engine run time", 0x7F, "07 00 00 04 EC 00 00 01 2C 00 00 00 00", StandardCommands.EngineRunTime,
                "Support mask, then three 32-bit second counters: total, idle, power take-off."
            ),
            Num("Odometer", 0xA6, "km", 0, 429496729, 84210.6, v => U32(v * 10.0), StandardCommands.Odometer),

            // ---- Mode 09: vehicle information ---------------------------------------------------
            Text(
                "VIN", 0x02, "1G1JC5444R7252367", v => WithCount(Ascii(v, 17)), StandardCommands.Vin,
                "17 characters. Long enough to force a multi-frame reply, which is the interesting case to test.",
                mode: 0x09
            ),
            Text(
                "Calibration ID", 0x04, "SHINY-OBD-CAL01", v => WithCount(Ascii(v, 16)), StandardCommands.CalibrationId,
                "Padded to a 16-byte block.",
                mode: 0x09
            ),
            Hex(
                "Calibration verification number", 0x06, "01 1A 2B 3C 4D", StandardCommands.CalibrationVerificationNumber,
                "Leading count byte, then one or more 4-byte CVN blocks.",
                mode: 0x09
            ),
            Hex(
                "In-use performance tracking (spark)", 0x08, InUseDefault(9), InUsePerformanceTrackingCommand.Spark(),
                "Count of 16-bit items, then monitoring conditions, ignition cycles, and a completion/condition pair per monitor.",
                mode: 0x09
            ),
            Text(
                "ECU name", 0x0A, "ECM-EngineControl", v => WithCount(Ascii(v, 20)), StandardCommands.EcuName,
                "Padded to a 20-byte field.",
                mode: 0x09
            ),
            Hex(
                "In-use performance tracking (compression)", 0x0B, InUseDefault(8), InUsePerformanceTrackingCommand.Compression(),
                "Same layout as PID 08, with the eight compression-ignition monitors.",
                mode: 0x09
            ),

            // ---- Mode 06: on-board monitoring test results --------------------------------------
            Mode06(
                "O2 sensor monitor B1S1", 0x01,
                "01 01 0B 00 64 00 32 00 96",
                "MID, test ID, unit/scaling ID, then value, minimum and maximum as 16-bit words."
            ),
            Mode06(
                "Catalyst monitor bank 1", 0x21,
                "21 80 0B 00 50 00 28 00 78",
                "Scaling 0B is volts x 0.001. Value must sit between min and max for OnBoardTestResult.Passed to be true."
            )
        };

        // The eight narrowband oxygen sensors and their two wideband families are identical but for
        // their PID, so they are generated rather than typed out sixteen more times.
        for (var sensor = 1; sensor <= 8; sensor++)
        {
            var position = $"B{((sensor - 1) / 4) + 1}S{((sensor - 1) % 4) + 1}";

            items.Add(Num(
                $"O2 sensor {position} voltage", (byte)(0x13 + sensor), "V", 0, 1.275, 0.45,
                v => [Clamp8(v * 200.0), 0xFF],
                OxygenSensorVoltageCommand.Sensor(sensor),
                "Trim byte is fixed at FF - the standard marker for a sensor not used in trim. Use the raw override to set it."
            ));

            items.Add(Num(
                $"O2 sensor {position} lambda (voltage)", (byte)(0x23 + sensor), "λ", 0, 1.999, 1.0,
                v => [.. Lambda(v), .. U16(0.5 * 65536.0 / 8.0)],
                OxygenSensorLambdaCommand.WithVoltage(sensor),
                "Second word is fixed at 0.5 V. Use the raw override to vary it."
            ));

            items.Add(Num(
                $"O2 sensor {position} lambda (current)", (byte)(0x33 + sensor), "λ", 0, 1.999, 1.0,
                v => [.. Lambda(v), .. U16(128.0 * 256.0)],
                OxygenSensorLambdaCommand.WithCurrent(sensor),
                "Second word is fixed at 0 mA. Use the raw override to vary it."
            ));
        }

        items.Sort((a, b) => a.Mode != b.Mode ? a.Mode.CompareTo(b.Mode) : a.Pid.CompareTo(b.Pid));
        return items;
    }

    // ---- Factories --------------------------------------------------------------------------------

    static ObdParameter Num<T>(
        string name,
        byte pid,
        string? unit,
        double min,
        double max,
        double value,
        Func<double, byte[]> encode,
        IObdCommand<T> command,
        string? note = null,
        byte mode = 0x01
    ) => new()
    {
        Name = name, Mode = mode, Pid = pid, Unit = unit, Note = note,
        Kind = ObdValueKind.Number,
        Minimum = min, Maximum = max, Number = value,
        FromNumber = encode,
        Decode = Decoder(command)
    };

    static ObdParameter Text<T>(
        string name,
        byte pid,
        string value,
        Func<string, byte[]> encode,
        IObdCommand<T> command,
        string? note = null,
        byte mode = 0x01
    ) => new()
    {
        Name = name, Mode = mode, Pid = pid, Note = note,
        Kind = ObdValueKind.Text,
        Text = value,
        FromText = encode,
        Decode = Decoder(command)
    };

    static ObdParameter Hex<T>(
        string name,
        byte pid,
        string value,
        IObdCommand<T> command,
        string? note = null,
        byte mode = 0x01
    ) => new()
    {
        Name = name, Mode = mode, Pid = pid, Note = note,
        Kind = ObdValueKind.Hex,
        Hex = value,
        Decode = Decoder(command)
    };

    static ObdParameter Derived<T>(
        string name,
        byte pid,
        Func<byte[]> encode,
        IObdCommand<T> command,
        string? note = null,
        byte mode = 0x01
    ) => new()
    {
        Name = name, Mode = mode, Pid = pid, Note = note,
        Kind = ObdValueKind.Derived,
        FromState = encode,
        Decode = Decoder(command)
    };

    static ObdParameter Mode06(string name, byte mid, string value, string note) => new()
    {
        Name = name, Mode = 0x06, Pid = mid, Note = note,
        Kind = ObdValueKind.Hex,
        Hex = value,

        // Mode 06 replies with the mode echo then whole 9-byte records; the MID lives inside the
        // record, so there is no PID echo byte of its own.
        EchoesPid = false,
        Decode = Decoder(new OnBoardTestCommand(mid))
    };

    // ---- Encoders ---------------------------------------------------------------------------------

    static byte[] Percent255(double v) => [Clamp8(v * 255.0 / 100.0)];
    static byte[] TempOffset40(double v) => [Clamp8(v + 40.0)];
    static byte[] Trim128(double v) => [Clamp8((v + 100.0) * 128.0 / 100.0)];
    static byte[] TorquePercent(double v) => [Clamp8(v + 125.0)];
    static byte[] Lambda(double v) => U16(v * 65536.0 / 2.0);

    static byte[] U8(double v) => [Clamp8(v)];

    static byte[] U16(double v)
    {
        var raw = (int)Math.Round(Math.Clamp(v, 0, 65535));
        return [(byte)(raw >> 8), (byte)raw];
    }

    static byte[] S16(double v)
    {
        var raw = (short)Math.Round(Math.Clamp(v, Int16.MinValue, Int16.MaxValue));
        return [(byte)(raw >> 8), (byte)raw];
    }

    static byte[] U32(double v)
    {
        var raw = (uint)Math.Round(Math.Clamp(v, 0, UInt32.MaxValue));
        return [(byte)(raw >> 24), (byte)(raw >> 16), (byte)(raw >> 8), (byte)raw];
    }

    static byte Clamp8(double v) => (byte)Math.Round(Math.Clamp(v, 0, 255));

    /// <summary>ASCII, truncated or null-padded to a fixed field width.</summary>
    static byte[] Ascii(string value, int length)
    {
        var buffer = new byte[length];
        var text = value ?? "";
        var count = Math.Min(text.Length, length);
        Encoding.ASCII.GetBytes(text.AsSpan(0, count), buffer);
        return buffer;
    }

    /// <summary>Mode 09 replies lead with a count of the data items that follow. It is always one here.</summary>
    static byte[] WithCount(byte[] payload) => [0x01, .. payload];

    /// <summary>
    /// A plausible in-use performance tracking block: the item count in 16-bit words, the two
    /// standalone counters, then a completion/condition pair per monitor.
    /// </summary>
    static string InUseDefault(int monitors)
    {
        var words = new List<int> { 1200, 1500 };
        for (var i = 0; i < monitors; i++)
        {
            words.Add(300 + (i * 7));
            words.Add(1200);
        }

        var bytes = new List<byte> { (byte)words.Count };
        foreach (var word in words)
        {
            bytes.Add((byte)(word >> 8));
            bytes.Add((byte)word);
        }
        return Convert.ToHexString(bytes.ToArray());
    }

    // ---- Readback -----------------------------------------------------------------------------

    static Func<byte[], string> Decoder<T>(IObdCommand<T> command)
        => data => Format(command.Parse(data));

    static string Format(object? value) => value switch
    {
        null => "(none)",
        string s => s,
        double d => d.ToString("0.###"),
        float f => f.ToString("0.###"),
        TimeSpan t => t.ToString(),
        IEnumerable<byte> bytes => Convert.ToHexString(bytes.ToArray()),
        IEnumerable<string> list => Join(list),
        System.Collections.IEnumerable list => Join(list.Cast<object?>().Select(x => x?.ToString() ?? "")),
        _ => value.ToString() ?? ""
    };

    static string Join(IEnumerable<string> values)
    {
        var joined = String.Join(", ", values);
        return joined.Length == 0 ? "(none)" : joined;
    }
}
