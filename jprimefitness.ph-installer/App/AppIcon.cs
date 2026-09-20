using System.Reflection;

namespace JPrime.Panel.App;

/// <summary>The JPrime logo as a multi-size icon (embedded jprime.ico); used by every window and the tray.</summary>
public static class AppIcon
{
    private static Icon? _main;

    public static Icon Main => _main ??= Load();

    private static Icon Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("jprime.ico", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        return new Icon(stream);
    }

    /// <summary>Logo rendered at the given pixel size (picks the nearest embedded size, then scales).</summary>
    public static Bitmap Bitmap(int size)
    {
        using var sized = new Icon(Main, size, size);
        return sized.ToBitmap();
    }
}
