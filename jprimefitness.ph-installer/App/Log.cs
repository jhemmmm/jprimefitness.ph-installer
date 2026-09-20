using System.Text;

namespace JPrime.Panel.App;

/// <summary>Tiny thread-safe file logger for the panel itself. Rotates at ~5 MB (keeps one .1 file).</summary>
public static class Log
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _file;
    private static readonly StringBuilder EarlyBuffer = new();

    public static event Action<string>? LineWritten;

    /// <summary>Until configured, lines are buffered in memory and flushed on first <see cref="Configure"/>.</summary>
    public static void Configure(string logsDir)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(logsDir);
            _file = Path.Combine(logsDir, "panel.log");
            if (EarlyBuffer.Length > 0)
            {
                AppendRaw(EarlyBuffer.ToString());
                EarlyBuffer.Clear();
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");
    public static void Debug(string message) => Write("DEBUG", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            if (_file is null)
            {
                EarlyBuffer.AppendLine(line);
            }
            else
            {
                AppendRaw(line + Environment.NewLine);
            }
        }
        try { LineWritten?.Invoke(line); } catch { }
    }

    private static void AppendRaw(string text)
    {
        try
        {
            if (_file is null) return;
            var fi = new FileInfo(_file);
            if (fi.Exists && fi.Length > MaxBytes)
            {
                var rotated = _file + ".1";
                if (File.Exists(rotated)) File.Delete(rotated);
                File.Move(_file, rotated);
            }
            File.AppendAllText(_file, text);
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
