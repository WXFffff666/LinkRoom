using LinkRoom.Core;

namespace LinkRoom.Core.Tests;

/// <summary>
/// Verifies the thread-safety wrapper used for LogLines writes (BUG-2).
/// MainViewModel.L() funnels every log write through LogBuffer; its callback
/// marshals the ObservableCollection mutation onto the WPF Dispatcher.
/// This suite covers the pure-logic parts: concurrent add safety, head trimming,
/// and per-add callback delivery.
/// </summary>
public class UiThreadSafetyTests
{
    [Fact]
    public void LogBuffer_ConcurrentAdds_NoExceptionAndTrimmedToMax()
    {
        var buffer = new LogBuffer(300);
        const int threads = 8, perThread = 1000;

        Parallel.For(0, threads, t =>
        {
            for (int i = 0; i < perThread; i++) buffer.Add($"t{t}-{i}");
        });

        Assert.Equal(300, buffer.Count);
        Assert.Equal(300, buffer.Snapshot().Count);
    }

    [Fact]
    public void LogBuffer_TrimsFromHead_KeepsNewest()
    {
        var buffer = new LogBuffer(3);
        buffer.Add("a");
        buffer.Add("b");
        buffer.Add("c");
        buffer.Add("d");

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { "b", "c", "d" }, buffer.Snapshot());
    }

    [Fact]
    public void LogBuffer_OnLineAdded_CalledOncePerAdd()
    {
        var received = new List<string>();
        var buffer = new LogBuffer(10, received.Add);

        buffer.Add("x");
        buffer.Add("y");

        Assert.Equal(new[] { "x", "y" }, received);
    }

    [Fact]
    public void LogBuffer_ConcurrentAdds_CallbackInvokedOncePerAdd()
    {
        var callbackCount = 0;
        var buffer = new LogBuffer(1000, _ => Interlocked.Increment(ref callbackCount));
        const int threads = 8, perThread = 250;

        Parallel.For(0, threads, _ =>
        {
            for (int i = 0; i < perThread; i++) buffer.Add("line");
        });

        Assert.Equal(threads * perThread, callbackCount);
    }

    [Fact]
    public void LogBuffer_Clear_EmptiesBuffer()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("a");
        buffer.Add("b");
        buffer.Clear();

        Assert.Empty(buffer.Snapshot());
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void LogBuffer_InvalidMaxLines_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogBuffer(0));
    }
}
