using JPrime.Panel.Setup;

namespace JPrime.Panel.UI.Wizard;

public sealed record ValidationResult(bool Ok, string? Message = null)
{
    public static readonly ValidationResult Valid = new(true);
    public static ValidationResult Error(string message) => new(false, message);
}

/// <summary>Base for wizard pages: a UserControl with enter/validate/leave hooks.</summary>
public abstract class WizardPage : UserControl
{
    /// <summary>Readability cap for prose and wide inputs. Content wraps/shrinks to the page width below this, so nothing
    /// depends on the window size; above it lines just stop growing.</summary>
    protected const int ContentWidth = 760;
    protected const int LabelWidth = 200;
    private static readonly Font HeadingFont = new("Segoe UI", 10.5f, FontStyle.Bold);

    /// <summary>One tooltip window per page (AddRow tooltips share it).</summary>
    protected readonly ToolTip Tip = new();

    protected WizardPage()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(28, 20, 28, 12);
        AutoScroll = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Tip.Dispose();
        base.Dispose(disposing);
    }

    public abstract string Title { get; }
    public virtual string Subtitle => "";
    public virtual bool ShowBack => true;
    public virtual string NextText => "Next >";
    public virtual bool CanCancel => true;

    /// <summary>Whether the page applies given the current state (e.g. Biometric page only when the helper is selected).</summary>
    public virtual bool AppliesTo(WizardState s) => true;

    public virtual void OnEnter(WizardState s) { }
    public virtual ValidationResult Validate(WizardState s) => ValidationResult.Valid;

    /// <summary>Called after Validate; return false to stay on the page. Long work belongs here.</summary>
    public virtual Task<bool> OnLeaveAsync(WizardState s, CancellationToken ct) => Task.FromResult(true);

    // Layout helpers shared by pages. Everything is width-driven by the page: Stack() is a one-column table whose
    // children stretch to it, so labels wrap and inputs shrink instead of overflowing when the window is small.

    protected static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Fill,
        Font = HeadingFont,
        Margin = new Padding(0, 10, 0, 2),
    };

    protected static Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Fill,
        MaximumSize = new Size(ContentWidth, 0),
        ForeColor = Color.FromArgb(90, 90, 90),
        Margin = new Padding(0, 2, 0, 6),
    };

    protected static TableLayoutPanel FormGrid(int labelWidth = LabelWidth)
    {
        var g = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Top };
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return g;
    }

    protected void AddRow(TableLayoutPanel grid, string label, Control control, string? tooltip = null)
    {
        var l = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 8, 0), Anchor = AnchorStyles.Left | AnchorStyles.Top };
        control.Margin = new Padding(0, 3, 0, 3);
        grid.Controls.Add(l);
        grid.Controls.Add(control);
        if (tooltip is not null) Tip.SetToolTip(control, tooltip);
    }

    protected static void AddSpan(TableLayoutPanel grid, Control control)
    {
        grid.Controls.Add(control);
        grid.SetColumnSpan(control, 2);
    }

    /// <summary>Text box that fills its column up to <paramref name="width"/>. The small fixed Width is the floor the
    /// table may shrink it to; the cap is the MaximumSize.</summary>
    protected static TextBox Input(string value = "", int width = 360, bool password = false) => new()
    {
        Text = value,
        Width = 120,
        MaximumSize = new Size(width, 0),
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        UseSystemPasswordChar = password,
    };

    protected static Button Btn(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        b.Click += (_, _) => onClick();
        return b;
    }

    protected static Button CopyButton(Func<string> value, string text = "Copy token") => Btn(text, () => Ui.TryCopy(value()));

    /// <summary>One horizontal row. Controls anchored Left|Right (every <see cref="Input"/>) share the remaining width;
    /// the others keep their natural size, so a text box plus its buttons never overflows the column.</summary>
    protected static TableLayoutPanel Row(params Control[] items)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = items.Length,
            RowCount = 1,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(ContentWidth, 0),
        };
        foreach (var c in items)
        {
            var stretch = c.Dock == DockStyle.Fill || (c.Anchor & (AnchorStyles.Left | AnchorStyles.Right)) == (AnchorStyles.Left | AnchorStyles.Right);
            row.ColumnStyles.Add(stretch ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            if (stretch) c.MaximumSize = Size.Empty;
            else c.Anchor = AnchorStyles.Left;
            if (c == items[0]) c.Margin = new Padding(0);
            row.Controls.Add(c);
        }
        return row;
    }

    protected static TableLayoutPanel Stack()
    {
        var s = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
        };
        s.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return s;
    }
}
