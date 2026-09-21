using System.Text.Json;

namespace JPrime.Panel.Runtime.Updates;

/// <summary>One asset of a GitHub release. <see cref="Sha256Url"/> is the optional "&lt;asset&gt;.sha256" sidecar.</summary>
public sealed record GitHubRelease(string Tag, string AssetName, string AssetUrl, long Size, string? Sha256Url, DateTime PublishedAt);

/// <summary>releases/latest lookups for the three JPrime repositories (public repos, unauthenticated).</summary>
public static class GitHubReleases
{
    public const string AppRepo = "jhemmmm/jprimefitness.ph";
    public const string HelperRepo = "jhemmmm/jprimefitness.ph-hikvision";
    public const string PanelRepo = "jhemmmm/jprimefitness.ph-installer";

    public static Task<GitHubRelease?> LatestAppAsync(HttpClient http, CancellationToken ct) =>
        LatestAsync(http, AppRepo, n => n.StartsWith("jprimefitness.ph", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), ct);

    public static Task<GitHubRelease?> LatestHelperAsync(HttpClient http, CancellationToken ct) =>
        LatestAsync(http, HelperRepo, n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), ct);

    public static Task<GitHubRelease?> LatestPanelAsync(HttpClient http, CancellationToken ct) =>
        LatestAsync(http, PanelRepo, n => n.Equals("JPrimePanel.exe", StringComparison.OrdinalIgnoreCase), ct);

    /// <summary>Null when the repo has no release yet or none carries a matching asset.</summary>
    public static async Task<GitHubRelease?> LatestAsync(HttpClient http, string repo, Func<string, bool> assetMatch, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTime() : DateTime.MinValue;
        string? name = null, url = null, sha = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var n = asset.GetProperty("name").GetString() ?? "";
            var u = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (name is null && assetMatch(n))
            {
                name = n;
                url = u;
                size = asset.GetProperty("size").GetInt64();
            }
            else if (n.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            {
                sha = u;
            }
        }
        return name is null || url is null ? null : new GitHubRelease(tag, name, url, size, sha, published);
    }

    /// <summary>Reads the hex digest from a "&lt;hash&gt;  &lt;file&gt;" sidecar; null when unavailable.</summary>
    public static async Task<string?> ReadSha256Async(HttpClient http, string? sha256Url, CancellationToken ct)
    {
        if (sha256Url is null) return null;
        try
        {
            var txt = await http.GetStringAsync(sha256Url, ct).ConfigureAwait(false);
            var hex = txt.Split(' ', '\t', '\n', '\r')[0].Trim();
            return hex.Length == 64 ? hex : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"v1.2.3" &gt; "1.2.0"; unparsable versions count as newer when they differ (e.g. a "dev" bundle).</summary>
    public static bool IsNewer(string installed, string latest)
    {
        static string Norm(string v) => v.Trim().TrimStart('v', 'V');
        var a = Norm(installed);
        var b = Norm(latest);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) return vb > va;
        return true;
    }
}
