using JPrime.Panel.App;
using JPrime.Panel.Processes;
using JPrime.Panel.Services;

namespace JPrime.Panel.UI.Panel;

/// <summary>Bottom pane: pick a log source (captured console or a file) and follow it.</summary>
public sealed class LogViewerControl : UserControl
{
    private const int MaxLines = 4000;

    private readonly ComboBox _source;
    private readonly CheckBox _follow;
    private readonly TextBox _text;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<(ServiceBase Service, LogSource Source)> _sources = new();
    private OutputSink? _activeSink;
    private FileTailer? _tailer;
    private readonly List<string> _lines = new();

    public LogViewerControl()
    {
        Dock = DockStyle.Fill;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "Log:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        _source.SelectedIndexChanged += (_, _) => Activate(_source!.SelectedIndex);
        bar.Controls.Add(_source);
        _follow = new CheckBox { Text = "Follow", Checked = true, AutoSize = true, Margin = new Padding(10, 5, 0, 0) };
        bar.Controls.Add(_follow);
        var clear = new Button { Text = "Clear", AutoSize = true, Margin = new Padding(10, 1, 0, 0) };
        clear.Click += (_, _) => { _lines.Clear(); _text!.Clear(); };
        bar.Controls.Add(clear);
        var open = new Button { Text = "Open logs folder", AutoSize = true, Margin = new Padding(10, 1, 0, 0) };
        open.Click += (_, _) => OpenFolder();
        bar.Controls.Add(open);

        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
            HideSelection = false,
        };

        Controls.Add(_text);
        Controls.Add(bar);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => PollFile();
        _timer.Start();
    }

    public void SetServices(IEnumerable<ServiceBase> services)
    {
        _sources.Clear();
        _source.Items.Clear();
        foreach (var s in services)
        {
            foreach (var src in s.LogSources)
            {
                _sources.Add((s, src));
                _source.Items.Add($"{s.DisplayName} - {src.Name}");
            }
        }
        if (_source.Items.Count > 0) _source.SelectedIndex = 0;
    }

    public void ShowService(ServiceBase service)
    {
        var idx = _sources.FindIndex(x => ReferenceEquals(x.Service, service));
        if (idx >= 0) _source.SelectedIndex = idx;
    }

    private void Activate(int index)
    {
        if (_activeSink is not null)
        {
            _activeSink.LinesAdded -= OnSinkLines;
            _activeSink = null;
        }
        _tailer = null;
        _lines.Clear();
        _text.Clear();
        if (index < 0 || index >= _sources.Count) return;
        var (_, src) = _sources[index];
        if (src.Sink is not null)
        {
            _activeSink = src.Sink;
            Append(src.Sink.Snapshot());
            _activeSink.LinesAdded += OnSinkLines;
        }
        else if (src.Directory is not null && src.FilePattern is not null)
        {
            _tailer = new FileTailer(src.Directory, src.FilePattern);
            PollFile();
        }
    }

    private void OnSinkLines(OutputSink sink, IReadOnlyList<string> lines)
    {
        if (!ReferenceEquals(sink, _activeSink)) return;
        UiThread.Post(() => Append(lines));
    }

    private void PollFile()
    {
        if (_tailer is null) return;
        var lines = _tailer.ReadNew();
        if (lines.Count > 0) Append(lines);
    }

    private void Append(IReadOnlyList<string> lines)
    {
        if (IsDisposed) return;
        _lines.AddRange(lines);
        if (_lines.Count > MaxLines)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
            _text.Text = string.Join(Environment.NewLine, _lines);
        }
        else
        {
            _text.AppendText((_text.TextLength > 0 ? Environment.NewLine : "") + string.Join(Environment.NewLine, lines));
        }
        if (_follow.Checked)
        {
            _text.SelectionStart = _text.TextLength;
            _text.ScrollToCaret();
        }
    }

    private void OpenFolder()
    {
        var idx = _source.SelectedIndex;
        string? dir = idx >= 0 && idx < _sources.Count ? _sources[idx].Source.Directory : null;
        dir ??= AppServices.IsInitialized ? AppServices.Current.Paths.LogsDir : null;
        if (dir is not null && Directory.Exists(dir)) PanelAppContext.OpenUrl(dir);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            if (_activeSink is not null) _activeSink.LinesAdded -= OnSinkLines;
        }
        base.Dispose(disposing);
    }
}
