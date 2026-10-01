namespace Shiny.Obd.Ble;

/// <summary>
/// Holds the next command back until an adapter that was left mid-answer has finished.
/// </summary>
/// <remarks>
/// <para>⚠️ <b>An ELM327 that receives anything while it is still working on a command abandons it.</b> It
/// prints <c>STOPPED</c> and a fresh <c>&gt;</c> prompt — and that reply then lands on whichever command was
/// just written. So a command the caller gave up on (its own timeout, or the caller's token) does not end when
/// the caller stops waiting; it ends when the adapter prints its prompt. Writing the next command before then
/// interrupts the adapter, loses that command, and hands it the wrong answer — and on a cheap clone a run of
/// those wedges the adapter until the session is dropped.</para>
/// <para><b>So an abandoned exchange closes this gate</b>, the late prompt (or the link going) opens it, and
/// the next send waits for one or the other — bounded, because an adapter that never prints a prompt again is
/// wedged, and waiting for ever would only move the hang.</para>
/// <para>A command whose write never completed does not close it: the adapter never got it, so there is no
/// reply to wait out.</para>
/// </remarks>
sealed class PromptGate
{
    readonly object sync = new();
    TaskCompletionSource? quiet;

    /// <summary>Whether an abandoned command may still be answering.</summary>
    public bool IsClosed
    {
        get
        {
            lock (this.sync)
                return this.quiet != null;
        }
    }

    /// <summary>A command that reached the adapter was given up on before its prompt arrived.</summary>
    public void Abandoned()
    {
        lock (this.sync)
            this.quiet ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Text that arrived with nobody waiting for it — the tail of an abandoned reply. Its prompt opens the gate.
    /// </summary>
    public void Unclaimed(string text)
    {
        if (text.Contains('>'))
            this.Open();
    }

    /// <summary>Opens the gate whatever is outstanding — the link went, so no reply is coming.</summary>
    public void Open()
    {
        TaskCompletionSource? waiting;
        lock (this.sync)
        {
            waiting = this.quiet;
            this.quiet = null;
        }

        waiting?.TrySetResult();
    }

    /// <summary>
    /// Waits for an abandoned reply to finish, for at most <paramref name="grace"/>, then opens the gate either
    /// way. Answers whether the adapter went quiet on its own.
    /// </summary>
    public async Task<bool> WaitAsync(TimeSpan grace, CancellationToken ct)
    {
        Task? waiting;
        lock (this.sync)
            waiting = this.quiet?.Task;

        if (waiting == null)
            return true;

        try
        {
            await waiting.WaitAsync(grace, ct).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            // Never printed its prompt. Carry on rather than hang every command behind it; the caller's own
            // timeouts are what decide an adapter that stays like this is dead.
            this.Open();
            return false;
        }
    }
}
