using System.Text.Json;

namespace JPrime.Panel.App;

/// <summary>Finds an existing installation. Order: <c>&lt;exe dir&gt;\..\config\panel.json</c> (installed layout),
/// then the per-user pointer file <c>%LocalAppData%\JPrime\install.json</c>. Returns null when nothing is installed.</summary>
public static class InstallLocator
{
    public static string PointerFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JPrime", "install.json");

    public static string? Find()
    {
        var exeDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var parent = Path.GetDirectoryName(exeDir);
        if (parent is not null && File.Exists(Path.Combine(parent, "config", "panel.json")))
        {
            return parent;
        }

        try
        {
            if (File.Exists(PointerFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(PointerFile));
                if (doc.RootElement.TryGetProperty("installRoot", out var root))
                {
                    var dir = root.GetString();
                    if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "config", "panel.json")))
                    {
                        return dir;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Ignoring unreadable pointer file {PointerFile}: {ex.Message}");
        }

        return null;
    }

    public static void WritePointer(string installRoot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PointerFile)!);
        var json = JsonSerializer.Serialize(new { installRoot }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(PointerFile, json);
    }

    public static void DeletePointer()
    {
        try { if (File.Exists(PointerFile)) File.Delete(PointerFile); } catch { }
    }
}
