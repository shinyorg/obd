using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Shiny.Obd.Ble;

namespace Shiny.Obd.Tests;

/// <summary>
/// The next command waits out an adapter still answering one that was given up on. An ELM327 interrupted
/// mid-command answers STOPPED, and that reply lands on the command that interrupted it.
/// </summary>
public class PromptGateTests
{
    static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Wait_ReturnsAtOnce_WhenNothingWasAbandoned()
    {
        var gate = new PromptGate();
        var timer = Stopwatch.StartNew();

        Assert.True(await gate.WaitAsync(Grace, CancellationToken.None));
        Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task Wait_HoldsUntilTheLatePromptArrives()
    {
        var gate = new PromptGate();
        gate.Abandoned();

        var waiting = gate.WaitAsync(Grace, CancellationToken.None);
        await Task.Delay(50);
        Assert.False(waiting.IsCompleted);

        // The tail of the abandoned reply, then its prompt
        gate.Unclaimed("41 0D 3C\r");
        await Task.Delay(50);
        Assert.False(waiting.IsCompleted);

        gate.Unclaimed("\r>");
        Assert.True(await waiting);
        Assert.False(gate.IsClosed);
    }

    [Fact]
    public async Task Wait_GivesUpAfterTheGrace_AndOpensTheGate()
    {
        var gate = new PromptGate();
        gate.Abandoned();

        Assert.False(await gate.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None));
        Assert.False(gate.IsClosed);
    }

    [Fact]
    public async Task Open_ReleasesAWaiter_WhenTheLinkGoes()
    {
        var gate = new PromptGate();
        gate.Abandoned();

        var waiting = gate.WaitAsync(Grace, CancellationToken.None);
        gate.Open();

        Assert.True(await waiting);
    }

    [Fact]
    public async Task Wait_HonoursTheCallersToken()
    {
        var gate = new PromptGate();
        gate.Abandoned();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.WaitAsync(Grace, cts.Token));
    }

    [Fact]
    public void Unclaimed_WithoutAPrompt_LeavesTheGateClosed()
    {
        var gate = new PromptGate();
        gate.Abandoned();

        gate.Unclaimed("SEARCHING...\r");

        Assert.True(gate.IsClosed);
    }
}
