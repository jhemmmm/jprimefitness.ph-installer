using System.Text;
using System.Text.RegularExpressions;

namespace JPrime.Panel.Runtime.Templates;

/// <summary>Order- and comment-preserving Laravel <c>.env</c> editor.</summary>
public sealed class EnvFile
{
    private static readonly Regex KeyLine = new(@"^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$", RegexOptions.Compiled);

    private readonly List<string> _lines;

    public EnvFile(string text)
    {
        _lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (_lines.Count > 0 && _lines[^1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
    }

    public static EnvFile Load(string path) => new(File.ReadAllText(path));

    public static EnvFile Empty() => new("");

    public IEnumerable<string> Keys => _lines.Select(l => KeyLine.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value);

    public bool Has(string key) => IndexOf(key) >= 0;

    public string? Get(string key)
    {
        var i = IndexOf(key);
        if (i < 0) return null;
        return Unquote(KeyLine.Match(_lines[i]).Groups[2].Value.Trim());
    }

    /// <summary>Set (replace in place, else append). Null removes the key.</summary>
    public EnvFile Set(string key, string? value)
    {
        var i = IndexOf(key);
        if (value is null)
        {
            if (i >= 0) _lines.RemoveAt(i);
            return this;
        }
        var line = $"{key}={Quote(value)}";
        if (i >= 0) _lines[i] = line;
        else _lines.Add(line);
        return this;
    }

    public EnvFile SetAll(IEnumerable<KeyValuePair<string, string?>> values)
    {
        foreach (var (k, v) in values) Set(k, v);
        return this;
    }

    public IReadOnlyList<KeyValuePair<string, string>> Entries()
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (var l in _lines)
        {
            var m = KeyLine.Match(l);
            if (m.Success) list.Add(new(m.Groups[1].Value, Unquote(m.Groups[2].Value.Trim())));
        }
        return list;
    }

    private int IndexOf(string key)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var m = KeyLine.Match(_lines[i]);
            if (m.Success && m.Groups[1].Value == key) return i;
        }
        return -1;
    }

    /// <summary>Quote only when needed. Double quotes let Laravel expand <c>${VAR}</c>, so <c>$</c> is escaped.</summary>
    public static string Quote(string value)
    {
        if (value.Length == 0) return "";
        var needs = value.Any(c => char.IsWhiteSpace(c) || c is '#' or '"' or '\'' or '$' or '\\' or '=' or '`');
        if (!needs) return value;
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '$': sb.Append("\\$"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static string Unquote(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            var inner = raw[1..^1];
            return inner.Replace("\\\"", "\"").Replace("\\$", "$").Replace("\\\\", "\\").Replace("\\n", "\n");
        }
        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
        {
            return raw[1..^1];
        }
        // Strip trailing inline comment on unquoted values.
        var hash = raw.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? raw[..hash].TrimEnd() : raw;
    }

    public string Build()
    {
        var sb = new StringBuilder();
        foreach (var l in _lines) sb.Append(l).Append('\n');
        return sb.ToString();
    }

    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Build(), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
        else File.Move(tmp, path);
    }
}
