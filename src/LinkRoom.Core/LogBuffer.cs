namespace LinkRoom.Core;

/// <summary>
/// Thread-safe bounded in-memory log buffer (pure logic, no WPF dependency).
/// Lines are trimmed from the head once <see cref="MaxLines"/> is exceeded.
/// The optional callback runs after each successful add, OUTSIDE the lock, so
/// consumers may marshal onto a UI thread (e.g. WPF Dispatcher) without deadlock.
/// </summary>
public sealed class LogBuffer
{
    readonly List<string> _lines = new();
    readonly object _lock = new();
    readonly Action<string>? _onLineAdded;
    readonly int _maxLines;

    public LogBuffer(int maxLines, Action<string>? onLineAdded = null)
    {
        if (maxLines <= 0) throw new ArgumentOutOfRangeException(nameof(maxLines));
        _maxLines = maxLines;
        _onLineAdded = onLineAdded;
    }

    public int MaxLines => _maxLines;

    public int Count
    {
        get { lock (_lock) return _lines.Count; }
    }

    /// <summary>Appends a line, trimming old entries beyond <see cref="MaxLines"/>. Thread-safe.</summary>
    public void Add(string line)
    {
        Action<string>? notify;
        lock (_lock)
        {
            _lines.Add(line);
            while (_lines.Count > _maxLines) _lines.RemoveAt(0);
            notify = _onLineAdded;
        }
        notify?.Invoke(line);
    }

    /// <summary>Returns a copy of the buffered lines. Thread-safe.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock) return _lines.ToArray();
    }

    public void Clear()
    {
        lock (_lock) _lines.Clear();
    }
}
