using JPrime.Panel.App;
using JPrime.Panel.Runtime;
using JPrime.Panel.Runtime.Templates;

namespace JPrime.Panel.UI.Panel;

/// <summary>Key/value grid over the app's .env. Apply = save, clear + rebuild caches, rolling PHP restart.</summary>
public sealed class EnvEditorForm : Form
{
    private static readonly HashSet<string> Secret = new(StringComparer.OrdinalIgnoreCase)
    {
        "APP_KEY", "DB_PASSWORD", "MAIL_PASSWORD", "KIOSK_TOKEN", "BIOMETRIC_TOKEN", "LIVE_SYNC_TOKEN",
        "PAYMONGO_SECRET_KEY", "PAYMONGO_WEBHOOK_SECRET", "RECAPTCHA_SECRET_KEY", "PRODUCTION_SUPER_ADMIN_PASSWORD",
        "AWS_SECRET_ACCESS_KEY",
    };

    private static readonly HashSet<string> PanelManaged = new(StringComparer.OrdinalIgnoreCase)
    {
        "APP_URL", "DB_CONNECTION", "DB_DATABASE", "CACHE_STORE", "SESSION_DRIVER", "QUEUE_CONNECTION", "APP_ENV",
    };

    private readonly AppServices _ctx;
    private readonly DataGridView _grid;
    private readonly CheckBox _reveal;

    public EnvEditorForm(AppServices ctx)
    {
        _ctx = ctx;
        Text = "Edit .env";
        Width = 820;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Key", Name = "key", FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Name = "value", FillWeight = 65 });
        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != 1 || _reveal!.Checked) return;
            var key = _grid.Rows[e.RowIndex].Cells[0].Value as string ?? "";
            if (Secret.Contains(key) && e.Value is string s && s.Length > 0) { e.Value = new string('•', Math.Min(s.Length, 24)); e.FormattingApplied = true; }
        };
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var key = _grid.Rows[e.RowIndex].Cells[0].Value as string ?? "";
            if (PanelManaged.Contains(key)) e.ToolTipText = "Managed by the panel (Settings). Changing it here may be overwritten.";
        };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 6, 8, 0) };
        top.Controls.Add(new Label { Text = "Values are applied after clearing Laravel caches and restarting PHP workers one by one.", AutoSize = true, Margin = new Padding(0, 4, 20, 0), ForeColor = Color.DimGray });
        _reveal = new CheckBox { Text = "Show secrets", AutoSize = true };
        _reveal.CheckedChanged += (_, _) => _grid.Invalidate();
        top.Controls.Add(_reveal);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var apply = new Button { Text = "Apply", Width = 90 };
        apply.Click += (_, _) => Apply();
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(apply);

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(bottom);
        CancelButton = cancel;

        foreach (var (k, v) in EnvFile.Load(ctx.Paths.AppEnvFile).Entries()) _grid.Rows.Add(k, v);
    }

    private void Apply()
    {
        _grid.EndEdit();
        var env = EnvFile.Load(_ctx.Paths.AppEnvFile);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var key = (row.Cells[0].Value as string ?? "").Trim();
            var value = row.Cells[1].Value as string ?? "";
            if (key.Length == 0) continue;
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                MessageBox.Show(this, $"'{key}' is not a valid .env key.", "Edit .env", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            seen.Add(key);
            env.Set(key, value);
        }
        foreach (var k in env.Keys.ToList())
        {
            if (!seen.Contains(k)) env.Set(k, null);
        }
        env.Save(_ctx.Paths.AppEnvFile);

        var ok = TaskDialog.Run(this, "Applying .env", async (log, ct) =>
        {
            var php = new PhpRunner(_ctx.Paths, _ctx.Config.Web.MaxRequests);
            var artisan = new ArtisanRunner(_ctx.Paths, php);
            log("Clearing caches");
            ArtisanRunner.Require(await artisan.OptimizeClear(ct, log), "optimize:clear");
            log("Rebuilding caches");
            ArtisanRunner.Require(await artisan.Optimize(ct, log), "optimize");
            log("Restarting PHP workers");
            await _ctx.Services.Php.RollingRestartAsync(ct);
            if (_ctx.Services.Scheduler.Status.IsActive)
            {
                log("Restarting scheduler");
                await _ctx.Services.Scheduler.RestartAsync(ct);
            }
        });
        if (ok)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
