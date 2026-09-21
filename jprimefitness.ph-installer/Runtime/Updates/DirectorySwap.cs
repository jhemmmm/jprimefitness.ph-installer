namespace JPrime.Panel.Runtime.Updates;

/// <summary>Folder replacement that survives the folder being held open (an Explorer window in it is enough to make a
/// plain rename fail): entries are moved one by one, with a short retry for files an antivirus scanner still holds.</summary>
public static class DirectorySwap
{
    public static void MoveContents(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(entry));
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (Directory.Exists(entry)) Directory.Move(entry, dest);
                    else File.Move(entry, dest, overwrite: true);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 8)
                {
                    Thread.Sleep(250 * attempt);
                }
            }
        }
    }

    public static string InUseMessage(string what, Exception ex) =>
        $"A file or folder in the {what} folder is in use by another program (an Explorer window, editor or terminal open in that folder is enough). "
        + "Close it and run the update again; the current version was left unchanged. Detail: " + ex.Message;

    /// <summary>current → stash, incoming → current. Restores the original layout when either half fails.</summary>
    public static void Replace(string current, string incoming, string stash, string what)
    {
        if (Directory.Exists(stash)) Directory.Delete(stash, true);
        try
        {
            MoveContents(current, stash);
        }
        catch (Exception ex)
        {
            MoveContents(stash, current);
            throw new InvalidOperationException(InUseMessage(what, ex), ex);
        }
        try
        {
            MoveContents(incoming, current);
            Directory.Delete(incoming);
        }
        catch (Exception ex)
        {
            MoveContents(current, incoming);
            MoveContents(stash, current);
            throw new InvalidOperationException(InUseMessage(what, ex), ex);
        }
    }
}
