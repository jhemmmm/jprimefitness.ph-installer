using System.IO.Compression;

namespace JPrime.Panel.Setup;

public static class ZipExtractor
{
    /// <summary>Extract with overwrite. When <paramref name="stripTopLevel"/> is true and every entry shares one
    /// top folder (nginx-1.28.3/...), that folder is removed. Guards against zip-slip.</summary>
    public static void Extract(string zipPath, string destination, bool stripTopLevel, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destination);
        var destRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);

        string? top = null;
        if (stripTopLevel)
        {
            var firsts = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).Where(n => n.Length > 0).Select(n => n.Split('/')[0]).Distinct().ToList();
            if (firsts.Count == 1 && zip.Entries.All(e => e.FullName.Replace('\\', '/').StartsWith(firsts[0] + "/") || e.FullName.Replace('\\', '/') == firsts[0] + "/"))
            {
                top = firsts[0] + "/";
            }
        }

        var total = zip.Entries.Count;
        var done = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            if (top is not null && name.StartsWith(top)) name = name[top.Length..];
            if (name.Length == 0) { done++; continue; }

            var target = Path.GetFullPath(Path.Combine(destRoot, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Zip entry escapes the destination folder: {entry.FullName}");
            }

            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
            done++;
            if ((done & 63) == 0 || done == total) progress?.Report((done, total));
        }
    }

    public static bool ContainsFile(string zipPath, string fileName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Any(e => string.Equals(Path.GetFileName(e.FullName), fileName, StringComparison.OrdinalIgnoreCase));
    }
}
