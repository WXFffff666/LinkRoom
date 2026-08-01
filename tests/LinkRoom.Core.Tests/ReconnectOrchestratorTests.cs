using LinkRoom.Core;

namespace LinkRoom.Core.Tests;

/// <summary>
/// BUG-5 suite for the single reconnect orchestrator.
/// Guarantees: at most one reconnect executes at any time; requests that arrive
/// while one is running coalesce (skip when the process is already restored);
/// a failed reconnect does not suppress later retries.
/// </summary>
public class ReconnectOrchestratorTests
{
    [Fact]
    public async Task RunOnceAsync_ConcurrentBurst_ExecutesExactlyOnceWhenRecovered()
    {
        // Models "guardian fires while AutoReconnect is mid-reconnect": 10 requests
        // burst in, the first one restores the process, the other 9 must be skipped.
        var orch = new ReconnectOrchestrator();
        var healthy = false;
        var executed = 0;
        var release = new TaskCompletionSource();

        Task Reconnect()
        {
            Interlocked.Increment(ref executed);
            healthy = true; // the first reconnect restores the process
            return release.Task; // keep it running while the stragglers queue up
        }

        var calls = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => orch.RunOnceAsync(Reconnect, () => healthy)))
            .ToArray();

        await Task.Delay(50); // let every request reach the gate while one is running
        release.SetResult();
        await Task.WhenAll(calls);

        Assert.Equal(1, executed);
    }

    [Fact]
    public async Task RunOnceAsync_ConcurrentBurst_NeverRunsTwoAtOnce()
    {
        var orch = new ReconnectOrchestrator();
        var active = 0;
        var peak = 0;
        var release = new TaskCompletionSource();

        async Task Reconnect()
        {
            var now = Interlocked.Increment(ref active);
            UpdateMax(ref peak, now);
            try { await release.Task; }
            finally { Interlocked.Decrement(ref active); }
        }

        var calls = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => orch.RunOnceAsync(Reconnect, () => false)))
            .ToArray();

        await Task.Delay(50);
        release.SetResult();
        await Task.WhenAll(calls);

        Assert.Equal(1, peak); // serialized: never two reconnects in flight
    }

    static void UpdateMax(ref int location, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref location)))
        {
            if (Interlocked.CompareExchange(ref location, value, current) == current) return;
        }
    }

    [Fact]
    public async Task RunOnceAsync_HealthyProcess_SkipsReconnect()
    {
        var orch = new ReconnectOrchestrator();
        var executed = 0;

        await orch.RunOnceAsync(() => { executed++; return Task.CompletedTask; }, () => true);

        Assert.Equal(0, executed);
    }

    [Fact]
    public async Task RunOnceAsync_SequentialCalls_BothRun()
    {
        // Ordinary sequential use (e.g. backoff-separated loop attempts):
        // each request is a fresh burst and must execute.
        var orch = new ReconnectOrchestrator();
        var executed = 0;

        Task Reconnect() { executed++; return Task.CompletedTask; }

        await orch.RunOnceAsync(Reconnect, () => false);
        await orch.RunOnceAsync(Reconnect, () => false);

        Assert.Equal(2, executed);
    }

    [Fact]
    public async Task RunOnceAsync_AfterFailedReconnect_NextRequestRetries()
    {
        var orch = new ReconnectOrchestrator();
        var healthy = false;
        var executed = 0;

        Task Reconnect()
        {
            if (Interlocked.Increment(ref executed) == 1)
                throw new InvalidOperationException("first attempt fails");
            healthy = true;
            return Task.CompletedTask;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orch.RunOnceAsync(Reconnect, () => healthy));
        await orch.RunOnceAsync(Reconnect, () => healthy); // queued retry succeeds

        Assert.Equal(2, executed);
        Assert.True(healthy);
    }
}
