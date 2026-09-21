using JPrime.Panel.App;

namespace JPrime.Panel.Runtime.Updates;

public enum UpdateTarget { App, Helper, Panel }

/// <summary>Result of one releases/latest lookup for a component.</summary>
public sealed record UpdateInfo(UpdateTarget Target, string Title, string Installed, GitHubRelease? Latest, string? Error)
{
    public bool Available => Latest is not null && GitHubReleases.IsNewer(Installed, Latest.Tag);
}

/// <summary>Queries GitHub for the three components and remembers the last answer.</summary>
public sealed class UpdateChecker
{
    private readonly AppServices _ctx;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateChecker(AppServices ctx)
    {
        _ctx = ctx;
    }

    public IReadOnlyList<UpdateInfo> Last { get; private set; } = Array.Empty<UpdateInfo>();
    public IEnumerable<UpdateInfo> Available => Last.Where(u => u.Available);

    /// <summary>Raised (on a worker thread) after every completed check.</summary>
    public event Action<IReadOnlyList<UpdateInfo>>? Checked;

    public string InstalledVersion(UpdateTarget t) => t switch
    {
        UpdateTarget.App => _ctx.Config.Versions.App,
        UpdateTarget.Helper => _ctx.Config.Versions.HikVision,
        _ => PanelUpdater.InstalledVersion,
    };

    /// <summary>The helper is only checked when it is installed; the app and the panel always.</summary>
    public IEnumerable<UpdateTarget> Targets()
    {
        yield return UpdateTarget.App;
        if (_ctx.Config.Services.HikVision.Enabled) yield return UpdateTarget.Helper;
        yield return UpdateTarget.Panel;
    }

    public async Task<IReadOnlyList<UpdateInfo>> CheckAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = new List<UpdateInfo>();
            foreach (var t in Targets()) list.Add(await CheckOneAsync(t, ct).ConfigureAwait(false));
            Last = list;
            _ctx.Config.Updates.LastCheckUtc = DateTime.UtcNow;
            _ctx.SaveConfig();
            Log.Info("Update check: " + string.Join("; ", list.Select(u => $"{u.Title} {u.Installed} -> {u.Latest?.Tag ?? "n/a"}{(u.Available ? " (update)" : "")}{(u.Error is null ? "" : " ERROR " + u.Error)}")));
            Checked?.Invoke(list);
            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UpdateInfo> CheckOneAsync(UpdateTarget t, CancellationToken ct)
    {
        var title = Title(t);
        var installed = InstalledVersion(t);
        try
        {
            var rel = t switch
            {
                UpdateTarget.App => await GitHubReleases.LatestAppAsync(_ctx.Http, ct).ConfigureAwait(false),
                UpdateTarget.Helper => await GitHubReleases.LatestHelperAsync(_ctx.Http, ct).ConfigureAwait(false),
                _ => await GitHubReleases.LatestPanelAsync(_ctx.Http, ct).ConfigureAwait(false),
            };
            return new UpdateInfo(t, title, installed, rel, null);
        }
        catch (Exception ex)
        {
            return new UpdateInfo(t, title, installed, null, ex.Message);
        }
    }

    public static string Title(UpdateTarget t) => t switch
    {
        UpdateTarget.App => "JPrime app",
        UpdateTarget.Helper => "Biometric helper",
        _ => "Control panel",
    };
}
