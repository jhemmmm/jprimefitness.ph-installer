using System.Reflection;
using System.Text.RegularExpressions;

namespace JPrime.Panel.Runtime.Templates;

/// <summary><c>{{KEY}}</c> substitution over embedded resource templates. Unresolved keys throw.</summary>
public static partial class TemplateEngine
{
    [GeneratedRegex(@"\{\{([A-Z0-9_]+)\}\}")]
    private static partial Regex Placeholder();

    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var missing = new List<string>();
        var result = Placeholder().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            if (values.TryGetValue(key, out var v)) return v;
            missing.Add(key);
            return m.Value;
        });
        if (missing.Count > 0)
        {
            throw new InvalidOperationException("Template placeholders without values: " + string.Join(", ", missing.Distinct()));
        }
        return result;
    }

    public static string LoadResource(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase) || n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new FileNotFoundException($"Embedded resource {fileName} not found. Available: {string.Join(", ", asm.GetManifestResourceNames())}");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string RenderResource(string fileName, IReadOnlyDictionary<string, string> values) =>
        Render(LoadResource(fileName), values);
}
