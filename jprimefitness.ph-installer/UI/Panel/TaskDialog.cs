using JPrime.Panel.App;

namespace JPrime.Panel.UI.Panel;

/// <summary>Runs a long operation with a live log and a Close button. Used for backup, update, re-provision.</summary>
public sealed class TaskDialog : Form
{
    private readonly TextBox _log;
    private readonly Button _close;
    private readonly ProgressBar _bar;
    private readonly CancellationTokenSource _cts = new();

    private TaskDialog(string title)
    {
        Text = title;
        Width = 760;
        Height = 480;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
        };
        _bar = new ProgressBar { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, Height = 14 };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        _close = new Button { Text = "Cancel", Width = 100 };
        _close.Click += (_, _) => { if (_close.Text == "Cancel") _cts.Cancel(); else Close(); };
        bottom.Controls.Add(_close);
        Controls.Add(_log);
        Controls.Add(_bar);
        Controls.Add(bottom);
        FormClosing += (_, e) => { if (_close.Text == "Cancel") { e.Cancel = true; _cts.Cancel(); } };
    }

    public void Append(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Append(line)); return; }
        _log.AppendText((_log.TextLength > 0 ? Environment.NewLine : "") + line);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    public void SetProgress(double? fraction)
    {
        if (InvokeRequired) { BeginInvoke(() => SetProgress(fraction)); return; }
        if (fraction is null) { _bar.Style = ProgressBarStyle.Marquee; return; }
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Value = Math.Clamp((int)(fraction.Value * 100), 0, 100);
    }

    /// <summary>Show modally and run <paramref name="work"/> on a background thread. Returns true when it finished without error.</summary>
    public static bool Run(IWin32Window owner, string title, Func<Action<string>, CancellationToken, Task> work)
    {
        using var dlg = new TaskDialog(title);
        var ok = false;
        dlg.Shown += async (_, _) =>
        {
            try
            {
                await Task.Run(() => work(dlg.Append, dlg._cts.Token));
                dlg.Append("Done.");
                ok = true;
            }
            catch (OperationCanceledException)
            {
                dlg.Append("Cancelled.");
            }
            catch (Exception ex)
            {
                Log.Error(title + " failed", ex);
                dlg.Append("ERROR: " + ex.Message);
            }
            finally
            {
                dlg.SetProgress(ok ? 1 : 0);
                dlg._bar.Style = ProgressBarStyle.Continuous;
                dlg._close.Text = "Close";
            }
        };
        dlg.ShowDialog(owner);
        return ok;
    }
}
