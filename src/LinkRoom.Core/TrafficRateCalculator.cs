namespace LinkRoom.Core;

/// <summary>
/// Pure helpers for rx/tx traffic rate computation from two cumulative byte
/// samples (spike: SPIKE-TRAFFIC.md). Kept dependency-free for unit testing.
/// </summary>
public static class TrafficRateCalculator
{
    /// <summary>
    /// Computes bytes/s from two cumulative byte counters and the elapsed
    /// seconds between samples. First sample (prev == null), invalid elapsed,
    /// or a counter reset (cur &lt; prev — e.g. easytier-core restarted) all
    /// yield 0 instead of a bogus spike.
    /// </summary>
    public static double ComputeRate(ulong? prevBytes, ulong? curBytes, double elapsedSeconds)
    {
        if (prevBytes == null || curBytes == null || elapsedSeconds <= 0)
            return 0;
        if (curBytes.Value < prevBytes.Value)
            return 0;
        return (curBytes.Value - prevBytes.Value) / elapsedSeconds;
    }

    /// <summary>
    /// Formats bytes/s for the quality panel: "512 B/s" below 1024, otherwise
    /// "1.5 KB/s" (one decimal).
    /// </summary>
    public static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond:F0} B/s";
        return $"{bytesPerSecond / 1024.0:F1} KB/s";
    }
}
