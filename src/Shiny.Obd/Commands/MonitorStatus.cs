namespace Shiny.Obd.Commands;

/// <summary>The result of <see cref="MonitorStatusCommand"/> and <see cref="MonitorStatusThisDriveCycleCommand"/>.</summary>
/// <param name="MilOn">Whether the check-engine light is currently commanded on.</param>
/// <param name="DtcCount">Number of confirmed emissions-related codes stored by the ECU.</param>
/// <param name="Ignition">Which set of monitors the vehicle reports, spark or compression.</param>
/// <param name="Monitors">
/// The emissions monitors this vehicle supports, and whether each has finished running. Monitors
/// the vehicle does not support are left out entirely rather than reported as incomplete — a
/// monitor that does not exist on a car has no readiness state to show.
/// </param>
/// <remarks>
/// <see cref="MilOn"/> and <see cref="DtcCount"/> come from byte A, which
/// <see cref="MonitorStatusThisDriveCycleCommand"/> (PID 0x41) reserves — that command always
/// reports the lamp off and no codes, and only its <see cref="Monitors"/> mean anything.
/// </remarks>
public readonly record struct MonitorStatus(
    bool MilOn,
    int DtcCount,
    IgnitionType Ignition,
    IReadOnlyList<MonitorReadiness> Monitors
)
{
    readonly IReadOnlyList<MonitorReadiness>? monitors = Monitors;

    /// <summary>
    /// The emissions monitors this vehicle supports. Never null.
    /// </summary>
    /// <remarks>
    /// Read through a backing field rather than as a plain positional property because this is a
    /// <b>struct</b>: <c>default(MonitorStatus)</c> bypasses every constructor, so a field
    /// initializer cannot save it and any caller touching the list would take a
    /// <see cref="NullReferenceException"/>. A default is always reachable for a value type, so it
    /// has to mean something — here it means "no monitors reported".
    /// </remarks>
    public IReadOnlyList<MonitorReadiness> Monitors
    {
        get => this.monitors ?? [];
        init => this.monitors = value;
    }

    /// <summary>
    /// Whether every monitor this vehicle supports has finished running, which is the question an
    /// emissions inspection actually asks — or <b>null when no monitors were reported at all</b>.
    /// </summary>
    /// <remarks>
    /// The null case is load-bearing rather than defensive. An adapter that truncates the reply to
    /// byte A leaves the list empty, and "every monitor in an empty list has completed" is
    /// vacuously true — so a plain <c>bool</c> would answer <c>true</c> and report a vehicle as
    /// inspection-ready on the strength of bytes that never arrived. Null says the question was not
    /// answered, which is the same distinction the whole of OBD-II draws between absent and zero.
    /// <para>
    /// A vehicle whose codes were recently cleared honestly reads false here for several drive
    /// cycles with nothing wrong with it — pair it with
    /// <see cref="TimeSinceCodesClearedCommand"/> (PID 0x4E) before reporting a problem.
    /// </para>
    /// </remarks>
    public bool? IsReadyForInspection => this.Monitors.Count == 0 ? null : this.Monitors.All(x => x.Complete);

    /// <summary>The supported monitors that have not finished running yet.</summary>
    public IEnumerable<MonitorReadiness> Incomplete => this.Monitors.Where(x => !x.Complete);
}

/// <summary>One emissions monitor the vehicle supports, and whether it has finished running.</summary>
/// <param name="Monitor">Which monitor this is.</param>
/// <param name="Complete">Whether it has run to completion since codes were last cleared.</param>
public readonly record struct MonitorReadiness(EmissionMonitor Monitor, bool Complete);

/// <summary>Which set of emissions monitors a vehicle reports (bit 3 of byte B).</summary>
public enum IgnitionType
{
    /// <summary>Petrol/gasoline.</summary>
    Spark,

    /// <summary>Diesel.</summary>
    Compression
}

/// <summary>
/// An emissions monitor reported by mode 01 PIDs 0x01 and 0x41.
/// </summary>
/// <remarks>
/// The first three are common to both ignition types; the rest are specific to one, and which set
/// the bytes carry is decided by <see cref="MonitorStatus.Ignition"/>.
/// </remarks>
public enum EmissionMonitor
{
    /// <summary>Misfire (common to both ignition types).</summary>
    Misfire,

    /// <summary>Fuel system (common to both ignition types).</summary>
    FuelSystem,

    /// <summary>Comprehensive components (common to both ignition types).</summary>
    Components,

    /// <summary>EGR and/or VVT system. The one type-specific monitor both sets share.</summary>
    EgrOrVvtSystem,

    /// <summary>Catalyst (spark ignition).</summary>
    Catalyst,

    /// <summary>Heated catalyst (spark ignition).</summary>
    HeatedCatalyst,

    /// <summary>Evaporative system (spark ignition).</summary>
    EvaporativeSystem,

    /// <summary>Secondary air system (spark ignition).</summary>
    SecondaryAirSystem,

    /// <summary>
    /// Gasoline particulate filter (spark ignition). ISO 15031-5:2015 and later; earlier revisions
    /// of the standard defined this same bit as A/C refrigerant monitoring, so on an older vehicle
    /// that is what it means. Neither is widely reported.
    /// </summary>
    GasolineParticulateFilter,

    /// <summary>Oxygen sensor (spark ignition).</summary>
    OxygenSensor,

    /// <summary>Oxygen sensor heater (spark ignition).</summary>
    OxygenSensorHeater,

    /// <summary>NMHC catalyst (compression ignition).</summary>
    NmhcCatalyst,

    /// <summary>NOx/SCR aftertreatment (compression ignition).</summary>
    NoxOrScrAftertreatment,

    /// <summary>Boost pressure (compression ignition).</summary>
    BoostPressure,

    /// <summary>Exhaust gas sensor (compression ignition).</summary>
    ExhaustGasSensor,

    /// <summary>Particulate filter (compression ignition).</summary>
    ParticulateFilter
}
