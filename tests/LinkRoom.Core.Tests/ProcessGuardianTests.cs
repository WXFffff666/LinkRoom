using LinkRoom.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinkRoom.Core.Tests;

/// <summary>
/// BUG-5 regression suite for ProcessGuardian.
/// The old dead-check (`!_proc.IsRunning &amp;&amp; _proc.ProcessId == null`) could never
/// be true — an exited process keeps a stale non-null PID — so recovery never fired.
/// These tests lock the fixed condition (`ProcessId == null || !IsRunning`) and the
/// per-dead-episode debounce.
/// </summary>
public class ProcessGuardianTests
{
    sealed class FakeProcessHealth : IProcessHealth
    {
        public bool IsRunning { get; set; }
        public int? ProcessId { get; set; }
    }

    static ProcessGuardian CreateGuardian(IProcessHealth proc)
        => new(proc, NullLogger<ProcessGuardian>.Instance, TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task Watch_ExitedProcessWithStalePid_TriggersRecovery()
    {
        // BUG-5 regression: an exited process keeps its stale PID (ProcessId == 42)
        // while IsRunning is false — under the old condition this state was never
        // detected, so recovery never fired.
        var proc = new FakeProcessHealth { IsRunning = false, ProcessId = 42 };
        var recovered = new TaskCompletionSource();
        var guardian = CreateGuardian(proc);

        guardian.Start(() => { recovered.SetResult(); return Task.CompletedTask; });
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        guardian.Stop();
    }

    [Fact]
    public async Task Watch_MissingProcessObject_TriggersRecovery()
    {
        // Process never started / already stopped: ProcessId is null.
        var proc = new FakeProcessHealth { IsRunning = false, ProcessId = null };
        var recovered = new TaskCompletionSource();
        var guardian = CreateGuardian(proc);

        guardian.Start(() => { recovered.SetResult(); return Task.CompletedTask; });
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        guardian.Stop();
    }

    [Fact]
    public async Task Watch_HealthyProcess_DoesNotTrigger()
    {
        var proc = new FakeProcessHealth { IsRunning = true, ProcessId = 7 };
        var triggered = 0;
        var guardian = CreateGuardian(proc);

        guardian.Start(() => { Interlocked.Increment(ref triggered); return Task.CompletedTask; });
        await Task.Delay(100); // ~10 ticks
        guardian.Stop();

        Assert.Equal(0, triggered);
    }

    [Fact]
    public async Task Watch_StaysDead_TriggersOnlyOnce()
    {
        var proc = new FakeProcessHealth { IsRunning = false, ProcessId = 42 };
        var triggered = 0;
        var guardian = CreateGuardian(proc);

        guardian.Start(() => { Interlocked.Increment(ref triggered); return Task.CompletedTask; });
        await Task.Delay(200); // ~20 ticks while still dead
        guardian.Stop();

        Assert.Equal(1, triggered);
    }

    [Fact]
    public async Task Watch_RevivedThenDeadAgain_TriggersAgain()
    {
        var proc = new FakeProcessHealth { IsRunning = false, ProcessId = 42 };
        var triggered = 0;
        var guardian = CreateGuardian(proc);

        guardian.Start(() => { Interlocked.Increment(ref triggered); return Task.CompletedTask; });
        await Task.Delay(50);        // first dead episode fires once
        proc.IsRunning = true;       // process revived
        await Task.Delay(50);        // debounce resets on a healthy observation
        proc.IsRunning = false;      // dies again → new episode
        await Task.Delay(50);
        guardian.Stop();

        Assert.Equal(2, triggered);
    }

    [Fact]
    public async Task Watch_DeadProcess_RecoveryThrows_DoesNotReTrigger()
    {
        // Even when the recovery callback fails, the dead episode is consumed —
        // the guardian must not fire again on every subsequent tick.
        var proc = new FakeProcessHealth { IsRunning = false, ProcessId = 42 };
        var triggered = 0;
        var guardian = CreateGuardian(proc);

        guardian.Start(() =>
        {
            Interlocked.Increment(ref triggered);
            throw new InvalidOperationException("recovery failed");
        });
        await Task.Delay(150); // ~15 ticks, first one throws inside the loop
        guardian.Stop();

        Assert.Equal(1, triggered);
    }
}
