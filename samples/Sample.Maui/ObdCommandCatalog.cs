using Shiny.Obd;
using Shiny.Obd.Commands;

namespace Sample.Maui;

/// <summary>
/// One command the sweep can issue, with what the page needs to show it and what the sweep needs to
/// decide whether asking is worthwhile.
/// </summary>
public sealed record ObdCommandEntry
{
    public required string Group { get; init; }
    public required string Name { get; init; }

    /// <summary>The request that goes on the wire, taken from the command rather than retyped.</summary>
    public required string Request { get; init; }

    /// <summary>
    /// The mode 01 PID to check against the support mask before asking, or null for a command that has
    /// no mask to check - the mask commands themselves, and modes 03, 04, 06, 07, 09 and 0A.
    /// </summary>
    public byte? SupportPid { get; init; }

    /// <summary>
    /// For a support-mask command, the block it covers. The sweep walks these first and stops at the
    /// first block the vehicle does not answer, which is what the masks are for.
    /// </summary>
    public byte? DiscoveryBlock { get; init; }

    public string? Note { get; init; }

    public required Func<IObdConnection, CancellationToken, Task<object?>> Execute { get; init; }
}

/// <summary>
/// The client-side counterpart to <see cref="Emulator.ObdParameterCatalog"/>: every command
/// <c>Shiny.Obd</c> can issue, as a caller issues it.
/// </summary>
/// <remarks>
/// The emulator catalog answers commands; this one asks them. Keep the two in step when adding a
/// command - one that appears in only the emulator is an answer nobody asks for, and one that appears
/// only here has nothing to answer it.
/// <para>
/// <see cref="ClearDtcCommand"/> is deliberately absent. It is the one command here that changes the
/// vehicle rather than reading it, and a sweep that runs everything would wipe fault memory as a side
/// effect of looking at it. The page exposes it as its own button.
/// </para>
/// </remarks>
public static class ObdCommandCatalog
{
    public const string SupportGroup = "Supported PIDs (mode 01)";
    public const string LiveGroup = "Live data (mode 01)";
    public const string FreezeFrameGroup = "Freeze frame (mode 02)";
    public const string DtcGroup = "Trouble codes (modes 03 / 07 / 0A)";
    public const string TestGroup = "On-board tests (mode 06)";
    public const string InfoGroup = "Vehicle info (mode 09)";

    public static List<ObdCommandEntry> Build()
    {
        var items = new List<ObdCommandEntry>();

        // ---- Mode 01 support masks ---------------------------------------------------------------
        // These run first and ungated: their answers are what gate everything in the live group.
        foreach (var block in SupportedPidsCommand.BlockPids)
        {
            items.Add(Entry(
                SupportGroup,
                $"PIDs {(byte)(block + 0x01):X2}-{(byte)(block + 0x20):X2}",
                new SupportedPidsCommand(block),
                "Each answered block says the next 32 PIDs are worth asking for. A vehicle that does not answer a block ends the walk.",
                gate: false
            ) with { DiscoveryBlock = block });
        }

        // ---- Mode 06 support masks ---------------------------------------------------------------
        foreach (var block in MonitorIds.BlockMids)
        {
            items.Add(Entry(
                TestGroup,
                $"Supported MIDs {(byte)(block + 0x01):X2}-{(byte)(block + 0x20):X2}",
                new OnBoardTestSupportedMidsCommand(block),
                "Same bitmask layout as the mode 01 mask. The monitors it names are added to this group as they are discovered."
            ) with { DiscoveryBlock = block });
        }

        // ---- Mode 01 live data, each paired with its mode 02 freeze frame -------------------------
        // Ordered by PID so this reads alongside ObdParameterCatalog.
        Live(items, "Monitor status since DTCs cleared", StandardCommands.MonitorStatus);
        Live(items, "Fuel system status", StandardCommands.FuelSystemStatus);
        Live(items, "Calculated engine load", StandardCommands.CalculatedEngineLoad);
        Live(items, "Coolant temperature", StandardCommands.CoolantTemperature);
        Live(items, "Short term fuel trim bank 1", FuelTrimCommand.ShortTermBank1());
        Live(items, "Long term fuel trim bank 1", FuelTrimCommand.LongTermBank1());
        Live(items, "Short term fuel trim bank 2", FuelTrimCommand.ShortTermBank2());
        Live(items, "Long term fuel trim bank 2", FuelTrimCommand.LongTermBank2());
        Live(items, "Fuel pressure", StandardCommands.FuelPressure);
        Live(items, "Intake manifold pressure", StandardCommands.IntakeManifoldPressure);
        Live(items, "Engine RPM", StandardCommands.EngineRpm);
        Live(items, "Vehicle speed", StandardCommands.VehicleSpeed);
        Live(items, "Timing advance", StandardCommands.TimingAdvance);
        Live(items, "Intake air temperature", StandardCommands.IntakeAirTemperature);
        Live(items, "Mass air flow", StandardCommands.MassAirFlow);
        Live(items, "Throttle position", StandardCommands.ThrottlePosition);
        Live(items, "Oxygen sensors present (2 banks)", OxygenSensorsPresentCommand.TwoBanks());

        for (var sensor = 1; sensor <= 8; sensor++)
            Live(items, $"O2 sensor {Position(sensor)} voltage", OxygenSensorVoltageCommand.Sensor(sensor));

        Live(items, "OBD standards", StandardCommands.ObdStandards);
        Live(items, "Oxygen sensors present (4 banks)", OxygenSensorsPresentCommand.FourBanks());
        Live(items, "Run time since engine start", StandardCommands.RuntimeSinceStart);
        Live(items, "Distance travelled with MIL on", StandardCommands.DistanceWithMilOn);
        Live(items, "Fuel rail pressure", StandardCommands.FuelRailPressure);
        Live(items, "Fuel rail gauge pressure", StandardCommands.FuelRailGaugePressure);

        for (var sensor = 1; sensor <= 8; sensor++)
            Live(items, $"O2 sensor {Position(sensor)} lambda (voltage)", OxygenSensorLambdaCommand.WithVoltage(sensor));

        Live(items, "Commanded EGR", StandardCommands.CommandedEgr);
        Live(items, "EGR error", StandardCommands.EgrError);
        Live(items, "Commanded evaporative purge", StandardCommands.CommandedEvaporativePurge);
        Live(items, "Fuel tank level", StandardCommands.FuelLevel);
        Live(items, "Warm-ups since codes cleared", StandardCommands.WarmUpsSinceCodesCleared);
        Live(items, "Distance since codes cleared", StandardCommands.DistanceSinceCodesCleared);
        Live(items, "Evap system vapor pressure", StandardCommands.EvapVaporPressure);
        Live(items, "Barometric pressure", StandardCommands.BarometricPressure);

        for (var sensor = 1; sensor <= 8; sensor++)
            Live(items, $"O2 sensor {Position(sensor)} lambda (current)", OxygenSensorLambdaCommand.WithCurrent(sensor));

        Live(items, "Monitor status this drive cycle", StandardCommands.MonitorStatusThisDriveCycle);
        Live(items, "Control module voltage", StandardCommands.ControlModuleVoltage);
        Live(items, "Absolute load value", StandardCommands.AbsoluteLoadValue);
        Live(items, "Commanded air-fuel ratio", StandardCommands.CommandedAirFuelRatio);
        Live(items, "Relative throttle position", StandardCommands.RelativeThrottlePosition);
        Live(items, "Ambient air temperature", StandardCommands.AmbientAirTemperature);
        Live(items, "Absolute throttle position B", AbsoluteThrottlePositionCommand.B());
        Live(items, "Absolute throttle position C", AbsoluteThrottlePositionCommand.C());
        Live(items, "Accelerator pedal position D", AcceleratorPedalPositionCommand.D());
        Live(items, "Accelerator pedal position E", AcceleratorPedalPositionCommand.E());
        Live(items, "Accelerator pedal position F", AcceleratorPedalPositionCommand.F());
        Live(items, "Commanded throttle actuator", StandardCommands.CommandedThrottleActuator);
        Live(items, "Time run with MIL on", StandardCommands.TimeRunWithMilOn);
        Live(items, "Time since codes cleared", StandardCommands.TimeSinceCodesCleared);
        Live(items, "Fuel type", StandardCommands.FuelType);
        Live(items, "Ethanol fuel percent", StandardCommands.EthanolFuelPercent);
        Live(items, "Absolute evap vapor pressure", StandardCommands.AbsoluteEvapVaporPressure);
        Live(items, "Evap vapor pressure (wide range)", StandardCommands.EvapVaporPressureWideRange);
        Live(items, "Fuel rail absolute pressure", StandardCommands.FuelRailAbsolutePressure);
        Live(items, "Relative accelerator pedal position", StandardCommands.RelativeAcceleratorPedalPosition);
        Live(items, "Hybrid battery remaining life", StandardCommands.HybridBatteryLife);
        Live(items, "Engine oil temperature", StandardCommands.EngineOilTemperature);
        Live(items, "Fuel injection timing", StandardCommands.FuelInjectionTiming);
        Live(items, "Engine fuel rate", StandardCommands.EngineFuelRate);
        Live(items, "Driver's demand engine torque", StandardCommands.DriverDemandTorque);
        Live(items, "Actual engine torque", StandardCommands.ActualEngineTorque);
        Live(items, "Engine reference torque", StandardCommands.ReferenceTorque);
        Live(items, "Engine percent torque data", StandardCommands.EnginePercentTorqueData);
        Live(items, "Engine run time", StandardCommands.EngineRunTime);
        Live(items, "Odometer", StandardCommands.Odometer);

        // ---- Mode 02: the code that caused the snapshot ------------------------------------------
        // Runs before the rest of its group. When it answers null there is no frame stored and every
        // other mode 02 reading is a zero-fill dressed up as a measurement, so the sweep stops there.
        items.Insert(
            IndexOfFirst(items, FreezeFrameGroup),
            Entry(
                FreezeFrameGroup,
                "Causal DTC",
                FreezeFrameCommands.CausalDtc(),
                "Ask this first. A null answer means there is no snapshot, and the rest of this group would read back a zero-filled frame as though it were data."
            )
        );

        // ---- Modes 03 / 07 / 0A: fault memory ----------------------------------------------------
        items.Add(Entry(DtcGroup, "Stored DTCs", DtcReadCommand.Stored, "Confirmed faults - the ones lighting the MIL."));
        items.Add(Entry(DtcGroup, "Pending DTCs", DtcReadCommand.Pending, "Seen once, not yet confirmed over a second drive cycle."));
        items.Add(Entry(DtcGroup, "Permanent DTCs", DtcReadCommand.Permanent, "Cannot be cleared by a scan tool - the vehicle erases them once it has re-run the monitor."));

        // ---- Mode 09: vehicle information --------------------------------------------------------
        items.Add(Entry(InfoGroup, "VIN", StandardCommands.Vin, "17 characters, so the reply arrives multi-frame."));
        items.Add(Entry(InfoGroup, "Calibration ID", StandardCommands.CalibrationId));
        items.Add(Entry(InfoGroup, "Calibration verification number", StandardCommands.CalibrationVerificationNumber));
        items.Add(Entry(InfoGroup, "In-use performance tracking (spark)", InUsePerformanceTrackingCommand.Spark()));
        items.Add(Entry(InfoGroup, "ECU name", StandardCommands.EcuName));
        items.Add(Entry(InfoGroup, "In-use performance tracking (compression)", InUsePerformanceTrackingCommand.Compression()));

        return items;
    }

    /// <summary>
    /// A mode 06 monitor, built once the supported-MID walk has said the vehicle has it.
    /// </summary>
    public static ObdCommandEntry OnBoardTest(byte mid) => Entry(
        TestGroup,
        MonitorIds.Describe(mid) is { } name ? name : $"Monitor {mid:X2} (manufacturer-defined)",
        new OnBoardTestCommand(mid)
    );

    /// <summary>
    /// Clearing fault memory. Kept out of <see cref="Build"/> on purpose - see the type remarks.
    /// </summary>
    public static ObdCommandEntry ClearDtcs() => Entry(
        DtcGroup,
        "Clear DTCs",
        new ClearDtcCommand(),
        "Erases stored and pending codes, the freeze frame and monitor readiness. Permanent codes survive."
    );

    // ---- Factories ----------------------------------------------------------------------------

    /// <summary>
    /// A mode 01 reading and the mode 02 snapshot of the same PID. Both gate on the same support
    /// mask: a PID the vehicle does not report live is not one it stored in a freeze frame either.
    /// </summary>
    static void Live<T>(List<ObdCommandEntry> items, string name, ObdCommand<T> command, string? note = null)
    {
        items.Add(Entry(LiveGroup, name, command, note));
        items.Add(Entry(FreezeFrameGroup, name, command.AsFreezeFrame(), note) with { SupportPid = command.Pid });
    }

    static ObdCommandEntry Entry<T>(
        string group,
        string name,
        IObdCommand<T> command,
        string? note = null,
        bool gate = true
    )
    {
        // Every RawCommand leads with the mode, so the mode and PID are read back off the command
        // rather than restated here - one fewer place for the two to drift apart.
        var raw = command.RawCommand;
        var mode = Convert.ToByte(raw[..2], 16);

        return new ObdCommandEntry
        {
            Group = group,
            Name = name,
            Request = raw,
            Note = note,
            SupportPid = gate && mode == 0x01 && raw.Length >= 4
                ? Convert.ToByte(raw.Substring(2, 2), 16)
                : null,
            // No ConfigureAwait(false): the sweep updates an ObservableCollection bound to a
            // CollectionView, so every continuation has to come back to the UI thread.
            Execute = async (connection, ct) => await connection.Execute(command, ct)
        };
    }

    static int IndexOfFirst(List<ObdCommandEntry> items, string group)
    {
        var index = items.FindIndex(x => x.Group == group);
        return index < 0 ? items.Count : index;
    }

    /// <summary>Bank and sensor for a 1-based sensor index, in banks of four.</summary>
    static string Position(int sensor) => $"B{((sensor - 1) / 4) + 1}S{((sensor - 1) % 4) + 1}";
}
