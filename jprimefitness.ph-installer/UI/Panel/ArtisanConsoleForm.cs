using JPrime.Panel.App;
using JPrime.Panel.Runtime;

namespace JPrime.Panel.UI.Panel;

/// <summary>Run <c>php artisan ...</c> with captured output. Common commands in a dropdown.</summary>
public sealed class ArtisanConsoleForm : Form
{
    private readonly AppServices _ctx;
    private readonly ComboBox _command;
    private readonly TextBox _output;
    private readonly Button _run;
    private CancellationTokenSource? _cts;

    public ArtisanConsoleForm(AppServices ctx)
    {
        _ctx = ctx;
        Text = "Artisan console";
        Width = 860;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 3, Padding = new Padding(8, 6, 8, 0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        top.Controls.Add(new Label { Text = "php artisan", AutoSize = true, Margin = new Padding(0, 6, 0, 0), Font = new Font("Consolas", 9.5f) }, 0, 0);
        _command = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, Font = new Font("Consolas", 9.5f) };
        _command.Items.AddRange(new object[]
        {
            "about", "migrate --force", "migrate:status", "optimize", "optimize:clear", "route:list --path=api",
            "db:seed --class=ProductionSeeder --force", "sync:issue-token", "sync:heartbeat", "sync:push", "sync:pull", "sync:bootstrap",
            "panel:expire-memberships", "panel:send-expiring-membership-notifications", "schedule:list", "queue:failed",
        });
        _command.Text = "about";
        top.Controls.Add(_command, 1, 0);
        _run = new Button { Text = "Run", Dock = DockStyle.Fill };
        _run.Click += (_, _) => _ = RunAsync();
        top.Controls.Add(_run, 2, 0);

        _output = new TextBox
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
        Controls.Add(_output);
        Controls.Add(top);
        AcceptButton = _run;
        FormClosing += (_, _) => _cts?.Cancel();
    }

    private async Task RunAsync()
    {
        var text = _command.Text.Trim();
        if (text.Length == 0) return;
        if (text.StartsWith("php ", StringComparison.OrdinalIgnoreCase)) text = text[4..].Trim();
        if (text.StartsWith("artisan ", StringComparison.OrdinalIgnoreCase)) text = text[8..].Trim();
        var args = SplitArgs(text);
        if (args.Length == 0) return;
        if (args[0] is "db:seed" && !args.Contains("--class=ProductionSeeder"))
        {
            var r = MessageBox.Show(this, "Running db:seed without --class=ProductionSeeder loads DEMO data into the live database. Continue?", "Artisan", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
        }
        if (args[0] is "migrate:fresh" or "migrate:reset" or "db:wipe")
        {
            var r = MessageBox.Show(this, $"{args[0]} DESTROYS all data in the database. Make a backup first. Continue?", "Artisan", MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
        }

        _run.Enabled = false;
        _cts = new CancellationTokenSource();
        _output.AppendText($"> php artisan {text}{Environment.NewLine}");
        try
        {
            var php = new PhpRunner(_ctx.Paths, _ctx.Config.Web.MaxRequests);
            var artisan = new ArtisanRunner(_ctx.Paths, php);
            var result = await artisan.RunAsync(_cts.Token, line => BeginInvoke(() => _output.AppendText(line + Environment.NewLine)), args);
            _output.AppendText($"[exit {result.ExitCode}]{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            _output.AppendText("ERROR: " + ex.Message + Environment.NewLine);
        }
        finally
        {
            _run.Enabled = true;
            _output.SelectionStart = _output.TextLength;
            _output.ScrollToCaret();
        }
    }

    private static string[] SplitArgs(string text)
    {
        var list = new List<string>();
        var cur = new System.Text.StringBuilder();
        var inQuote = false;
        foreach (var ch in text)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); }
                continue;
            }
            cur.Append(ch);
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list.ToArray();
    }
}
