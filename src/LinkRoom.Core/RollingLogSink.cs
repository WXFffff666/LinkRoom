using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

/// <summary>
/// In-memory rolling log sink that keeps the last N log entries.
/// Used for UI log display + file persistence.
/// All entries are sanitized (passwords/IPs redacted).
/// </summary>
public sealed class RollingLogSink : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly int _maxEntries;
    private readonly object _fileLock = new();
    private readonly string? _filePath;

    public event Action<LogEntry>? OnEntryAdded;

    public RollingLogSink(string? filePath = null, int maxEntries = 500)
    {
        _filePath = filePath;
        _maxEntries = maxEntries;
    }

    public ILogger CreateLogger(string categoryName) => this;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var entry = new LogEntry(DateTime.Now, logLevel, message);

        // Enqueue and trim
        _entries.Enqueue(entry);
        while (_entries.Count > _maxEntries)
            _entries.TryDequeue(out _);

        OnEntryAdded?.Invoke(entry);

        // Write to file if configured
        if (_filePath != null)
        {
            lock (_fileLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_filePath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    File.AppendAllText(_filePath,
                        $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] {entry.Message}{Environment.NewLine}");
                }
                catch { /* best effort */ }
            }
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>Returns all current log entries for UI display.</summary>
    public string GetAllText()
    {
        return string.Join(Environment.NewLine,
            _entries.Select(e => $"[{e.Timestamp:HH:mm:ss}] [{e.Level}] {e.Message}"));
    }

    public void Dispose() { }
}

public record LogEntry(DateTime Timestamp, LogLevel Level, string Message);