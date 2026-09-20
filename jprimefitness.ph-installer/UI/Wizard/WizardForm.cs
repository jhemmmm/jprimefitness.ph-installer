using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.Runtime.Templates;
using JPrime.Panel.Setup;
using JPrime.Panel.UI.Wizard.Pages;

namespace JPrime.Panel.UI.Wizard;

/// <summary>Setup wizard host: header, step list, page area, Back/Next/Cancel.</summary>
public sealed class WizardForm : Form
{
    private readonly WizardState _state;
    private readonly List<WizardPage> _pages;
    private readonly System.Windows.Forms.Panel _content;
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly ListBox _steps;
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _cancel;
    private int _index = -1;
    private bool _busy;
    private CancellationTokenSource? _leaveCts;

    public HttpClient Http { get; } = AppServices.CreateHttpClient();

    public WizardForm(CommandLine cli)
    {
        _state = BuildState(cli);

        Text = "JPrime Fitness PH Setup";
        Width = 960;
        Height = 740;
        MinimumSize = new Size(900, 660);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        var header = new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.White, Padding = new Padding(24, 10, 24, 0) };
        _title = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
        _subtitle = new Label { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.DimGray };
        header.Controls.Add(_subtitle);
        header.Controls.Add(_title);

        _steps = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 210,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(245, 246, 248),
            SelectionMode = SelectionMode.None,
            ItemHeight = 26,
            DrawMode = DrawMode.OwnerDrawFixed,
            Font = new Font("Segoe UI", 9.5f),
        };
        _steps.DrawItem += DrawStep;

        _content = new System.Windows.Forms.Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        var footer = new System.Windows.Forms.Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(12) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        _back = new Button { Text = "< Back", Width = 100, Height = 30, Margin = new Padding(6, 0, 0, 0) };
        _next = new Button { Text = "Next >", Width = 110, Height = 30, Margin = new Padding(6, 0, 0, 0) };
        _cancel = new Button { Text = "Cancel", Width = 100, Height = 30, Margin = new Padding(18, 0, 0, 0) };
        _back.Click += (_, _) => Go(-1);
        _next.Click += async (_, _) => await NextAsync();
        _cancel.Click += (_, _) => Close();
        flow.Controls.AddRange(new Control[] { _back, _next, _cancel });
        footer.Controls.Add(flow);

        Controls.Add(_content);
        Controls.Add(_steps);
        Controls.Add(footer);
        Controls.Add(header);
        AcceptButton = _next;

        _pages = new List<WizardPage>
        {
            new WelcomePage(),
            new FolderPage(),
            new PrereqPage(this),
            new AppSettingsPage(),
            new DeploymentPage(this),
            new BiometricPage(),
            new TunnelPage(),
            new AdvancedPage(),
            new ReviewPage(),
            new DownloadPage(this),
            new InstallPage(this),
            new FinishPage(this),
        };
        foreach (var p in _pages) _steps.Items.Add(p.Title);

        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                _leaveCts?.Cancel();
                return;
            }
            if (!_state.InstallCompleted && _index > 0 && _index < _pages.Count - 1)
            {
                var r = MessageBox.Show(this, "Cancel setup? Nothing has been changed on this PC yet unless the install step already ran.", "Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) e.Cancel = true;
            }
        };

        Go(+1);
    }

    public WizardState State => _state;

    // Pages leave the control tree on navigation, so the form would not dispose them on its own.
    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var p in _pages) p.Dispose();
        base.Dispose(disposing);
    }

    private static WizardState BuildState(CommandLine cli)
    {
        var root = cli.InstallRootOverride;
        if (root is not null && File.Exists(Path.Combine(root, "config", "panel.json")))
        {
            try
            {
                var paths = new AppPaths(root);
                var config = new ConfigStore(paths.PanelConfigFile).Load();
                var env = File.Exists(paths.AppEnvFile) ? EnvFile.Load(paths.AppEnvFile) : null;
                var hik = HikAppSettings.TryLoad(paths.HikVisionAppSettings);
                var existing = WizardState.FromExisting(config, env, hik);
                existing.HasStoredTunnelToken = new SecretStore(paths.SecretsFile).Has(SecretStore.TunnelToken);
                return existing;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not pre-fill wizard from existing install: " + ex.Message);
            }
        }
        var s = new WizardState();
        if (root is not null) s.InstallRoot = root;
        return s;
    }

    private void DrawStep(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var current = e.Index == _index;
        var done = e.Index < _index;
        var color = current ? Color.FromArgb(20, 60, 140) : done ? Color.FromArgb(40, 120, 60) : Color.FromArgb(110, 110, 110);
        var g = e.Graphics;
        using var bg = new SolidBrush(current ? Color.FromArgb(225, 236, 255) : _steps.BackColor);
        g.FillRectangle(bg, e.Bounds);

        // Marks are drawn as shapes: DrawString has no font fallback, so glyphs like a check mark can show up as boxes.
        var box = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + (e.Bounds.Height - 10) / 2, 10, 10);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (done)
        {
            using var pen = new Pen(color, 2f);
            g.DrawLines(pen, new[] { new Point(box.Left, box.Top + 5), new Point(box.Left + 4, box.Bottom - 1), new Point(box.Right, box.Top) });
        }
        else if (current)
        {
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, box);
        }
        else
        {
            using var pen = new Pen(color, 1.5f);
            g.DrawEllipse(pen, box);
        }
        var textBounds = new Rectangle(e.Bounds.Left + 32, e.Bounds.Top, e.Bounds.Width - 32, e.Bounds.Height);
        TextRenderer.DrawText(g, (string)_steps.Items[e.Index], e.Font!, textBounds, color, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    private void Go(int delta)
    {
        var next = _index;
        do
        {
            next += delta;
            if (next < 0 || next >= _pages.Count) return;
        } while (!_pages[next].AppliesTo(_state));
        Show(next);
    }

    private void Show(int index)
    {
        _index = index;
        var page = _pages[index];
        _content.SuspendLayout();
        _content.Controls.Clear();
        page.OnEnter(_state);
        _content.Controls.Add(page);
        _content.ResumeLayout();
        _title.Text = page.Title;
        _subtitle.Text = page.Subtitle;
        _back.Visible = page.ShowBack && index > 0;
        _next.Text = page.NextText;
        _cancel.Enabled = page.CanCancel;
        _steps.Invalidate();
        page.Focus();
    }

    private async Task NextAsync()
    {
        if (_busy) return;
        var page = _pages[_index];
        var v = page.Validate(_state);
        if (!v.Ok)
        {
            MessageBox.Show(this, v.Message, page.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SetBusy(true);
        _leaveCts = new CancellationTokenSource();
        try
        {
            var ok = await page.OnLeaveAsync(_state, _leaveCts.Token);
            if (!ok) return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error(page.Title + " failed", ex);
            MessageBox.Show(this, ex.Message, page.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            SetBusy(false);
            _leaveCts?.Dispose();
            _leaveCts = null;
        }
        if (_index == _pages.Count - 1)
        {
            Close();
            return;
        }
        Go(+1);
    }

    /// <summary>Pages that run long work (download/install) call this to drive the buttons.</summary>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        _next.Enabled = !busy;
        _back.Enabled = !busy;
        UseWaitCursor = busy;
    }

    public void SetNextEnabled(bool enabled) => _next.Enabled = enabled && !_busy;
    public void SetNextText(string text) => _next.Text = text;

    /// <summary>Jump straight to the Next action (used by auto-advancing pages).</summary>
    public Task AdvanceAsync() => NextAsync();

    // Developer screenshot support: show any page regardless of AppliesTo, without running OnEnter side effects
    // that need the network (Prereq/Download/Install pages still call OnEnter; they tolerate failures).
    public IEnumerable<string> PageTitlesForScreenshots() => _pages.Select(p => p.Title);
    public void ShowPageForScreenshot(int index)
    {
        if (_pages[index] is Pages.DownloadPage or Pages.InstallPage)
        {
            // Do not kick off downloads/installs: render the page shell only.
            _index = index;
            _content.Controls.Clear();
            _content.Controls.Add(_pages[index]);
            _title.Text = _pages[index].Title;
            _subtitle.Text = _pages[index].Subtitle;
            _steps.Invalidate();
            return;
        }
        Show(index);
    }
}
