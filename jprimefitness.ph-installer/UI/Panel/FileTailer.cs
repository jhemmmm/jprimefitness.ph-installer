using System.Text;

namespace JPrime.Panel.UI.Panel;

/// <summary>Follows the newest file matching a pattern in a directory (laravel-*.log, hikvision-helper-*.txt),
/// tolerating rotation and truncation. Call <see cref="ReadNew"/> from a timer.</summary>
public sealed class FileTailer
{
    private const int InitialTailBytes = 64 * 1024;

    private readonly string _directory;
    private readonly string _pattern;
    private string? _currentFile;
    private long _offset;
    private readonly StringBuilder _partial = new();

    public FileTailer(string directory, string pattern)
    {
        _directory = directory;
        _pattern = pattern;
    }

    public string? CurrentFile => _currentFile;

    /// <summary>Returns complete new lines since the last call (initially the last ~64 KB).</summary>
    public IReadOnlyList<string> ReadNew()
    {
        var lines = new List<string>();
        try
        {
            if (!Directory.Exists(_directory)) return lines;
            var newest = new DirectoryInfo(_directory).GetFiles(_pattern)
                .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name)
                .FirstOrDefault();
            if (newest is null) return lines;

            if (!string.Equals(newest.FullName, _currentFile, StringComparison.OrdinalIgnoreCase))
            {
                _currentFile = newest.FullName;
                _partial.Clear();
                _offset = Math.Max(0, newest.Length - InitialTailBytes);
                if (_offset > 0) lines.Add($"--- tailing {newest.Name} (last {InitialTailBytes / 1024} KB) ---");
                else lines.Add($"--- tailing {newest.Name} ---");
            }

            using var fs = new FileStream(_currentFile!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < _offset)
            {
                lines.Add("--- file truncated ---");
                _offset = 0;
                _partial.Clear();
            }
            if (fs.Length == _offset) return lines;

            fs.Seek(_offset, SeekOrigin.Begin);
            var buffer = new byte[Math.Min(fs.Length - _offset, 512 * 1024)];
            var read = fs.Read(buffer, 0, buffer.Length);
            _offset += read;
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            _partial.Append(text);
            var all = _partial.ToString();
            var lastNewline = all.LastIndexOf('\n');
            if (lastNewline < 0) return lines;
            var complete = all[..lastNewline];
            _partial.Clear();
            _partial.Append(all[(lastNewline + 1)..]);
            foreach (var line in complete.Split('\n'))
            {
                lines.Add(line.TrimEnd('\r'));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return lines;
    }

    public void Reset()
    {
        _currentFile = null;
        _offset = 0;
        _partial.Clear();
    }
}
