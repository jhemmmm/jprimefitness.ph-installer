using System.Text;

namespace JPrime.Panel.Processes;

/// <summary>Captures a child's stdout/stderr into a bounded ring buffer and an append-only log file, and raises
/// batched line events (every 250 ms) so the UI is never flooded.</summary>
public sealed class OutputSink : IDisposable
{
    private const int Capacity = 2000;
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly LinkedList<string> _ring = new();
    private readonly List<string> _pending = new();
    private readonly string? _logFile;
    private readonly System.Threading.Timer _flushTimer;
    private StreamWriter? _writer;

    public OutputSink(string name, string? logsDir)
    {
        Name = name;
        if (logsDir is not null)
        {
            Directory.CreateDirectory(logsDir);
            _logFile = Path.Combine(logsDir, $"{name}.out.log");
        }
        _flushTimer = new System.Threading.Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Name { get; }

    /// <summary>Raised on a thread-pool thread with the lines appended since the last batch.</summary>
    public event Action<OutputSink, IReadOnlyList<string>>? LinesAdded;

    public void Append(string line, bool isError = false)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss} {(isError ? "! " : "  ")}{line}";
        lock (_gate)
        {
            _ring.AddLast(stamped);
            while (_ring.Count > Capacity) _ring.RemoveFirst();
            _pending.Add(stamped);
            if (_pending.Count == 1)
            {
                _flushTimer.Change(250, Timeout.Infinite);
            }
            WriteFile(stamped);
        }
    }

    public void AppendMarker(string text) => Append($"--- {text} ---");

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate) return _ring.ToList();
    }

    public IReadOnlyList<string> Tail(int count)
    {
        lock (_gate) return _ring.Reverse().Take(count).Reverse().ToList();
    }

    public void Clear()
    {
        lock (_gate) _ring.Clear();
    }

    private void Flush()
    {
        List<string> batch;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            batch = new List<string>(_pending);
            _pending.Clear();
        }
        try { LinesAdded?.Invoke(this, batch); }
        catch (Exception ex) { App.Log.Error("OutputSink listener failed", ex); }
    }

    private void WriteFile(string line)
    {
        if (_logFile is null) return;
        try
        {
            if (_writer is null)
            {
                RotateIfNeeded();
                _writer = new StreamWriter(new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false))
                {
                    AutoFlush = true,
                };
            }
            _writer.WriteLine(line);
            if (_writer.BaseStream.Length > MaxFileBytes)
            {
                _writer.Dispose();
                _writer = null;
                RotateIfNeeded();
            }
        }
        catch
        {
            _writer = null;
        }
    }

    private void RotateIfNeeded()
    {
        if (_logFile is null) return;
        var fi = new FileInfo(_logFile);
        if (fi.Exists && fi.Length > MaxFileBytes)
        {
            var rotated = _logFile + ".1";
            if (File.Exists(rotated)) File.Delete(rotated);
            File.Move(_logFile, rotated);
        }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
