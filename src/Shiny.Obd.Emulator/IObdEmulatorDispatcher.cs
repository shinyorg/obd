namespace Shiny.Obd.Emulator;

/// <summary>
/// Marshals emulator state changes onto the thread a UI can safely observe them from.
/// </summary>
/// <remarks>
/// <para>
/// The emulator's state objects raise <c>PropertyChanged</c> straight into whatever is bound to them,
/// and both the transports and the driving scenarios write to that state from background threads - a
/// socket accept loop, a GATT callback, a timer tick. A UI framework will throw, or corrupt its
/// layout, if that reaches a binding off the UI thread.
/// </para>
/// <para>
/// This exists instead of a direct dependency on a UI framework's dispatcher so the emulator can run
/// anywhere: <see cref="AddObdEmulator"/> captures the <see cref="SynchronizationContext"/> in force
/// where you register it, which is the UI context in MAUI, WPF and WinForms, and null in a console or
/// test host - where every call simply runs inline.
/// </para>
/// </remarks>
public interface IObdEmulatorDispatcher
{
    /// <summary>Runs the action on the captured context, inline if already there or if there is none.</summary>
    void Invoke(Action action);
}

/// <summary>
/// The default <see cref="IObdEmulatorDispatcher"/>: posts to a <see cref="SynchronizationContext"/>
/// captured when the emulator was registered.
/// </summary>
/// <param name="context">The context to post to, or null to always run inline.</param>
public sealed class SynchronizationContextDispatcher(SynchronizationContext? context) : IObdEmulatorDispatcher
{
    /// <summary>Captures the context in force on the calling thread.</summary>
    public SynchronizationContextDispatcher() : this(SynchronizationContext.Current) { }

    /// <inheritdoc/>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Posting when already on the context would defer work that could have run now, which turns a
        // synchronous Stop() into something the caller can observe half-finished.
        if (context == null || SynchronizationContext.Current == context)
            action();
        else
            context.Post(_ => action(), null);
    }
}
