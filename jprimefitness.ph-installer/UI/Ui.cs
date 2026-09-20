using JPrime.Panel.App;

namespace JPrime.Panel.UI;

/// <summary>Small UI utilities shared by the wizard and the panel.</summary>
public static class Ui
{
    /// <summary>Copy to the clipboard; another app holding the clipboard makes SetText throw, which is not worth a dialog.</summary>
    public static void TryCopy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch (Exception ex) { Log.Warn("Clipboard copy failed: " + ex.Message); }
    }
}
