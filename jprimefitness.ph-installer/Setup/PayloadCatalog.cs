using System.Text.Json;
using JPrime.Panel.App;

namespace JPrime.Panel.Setup;

public enum PayloadKind
{
    Zip,
    Exe,
    File,
}

/// <summary>One downloadable component.</summary>
public sealed record Payload(
    string Id,
    string Title,
    string Url,
    string FileName,
    PayloadKind Kind,
    string Version,
    bool StripTopLevel = false,
    string? Sha256 = null,
    long? ExpectedSize = null);

/// <summary>Pinned download sources. PHP and the app bundle are resolved live (releases.json / GitHub latest),
/// the rest are fixed URLs with a fallback to the pinned version.</summary>
public static class PayloadCatalog
{
    public const string PhpSeries = "8.4";
    public const string PhpPinned = "8.4.25";
    public const string NginxVersion = "1.28.3";
    public const string CloudflaredVersion = "2026.9.1";
    public const string HikVisionRepo = "jhemmmm/jprimefitness.ph-hikvision";
    public const string AppRepo = "jhemmmm/jprimefitness.ph";

    public static Payload Nginx => new("nginx", "nginx web server",
        $"https://nginx.org/download/nginx-{NginxVersion}.zip", $"nginx-{NginxVersion}.zip", PayloadKind.Zip, NginxVersion, StripTopLevel: true);

    public static Payload Cloudflared => new("cloudflared", "Cloudflare Tunnel connector",
        $"https://github.com/cloudflare/cloudflared/releases/download/{CloudflaredVersion}/cloudflared-windows-amd64.exe",
        $"cloudflared-{CloudflaredVersion}.exe", PayloadKind.Exe, CloudflaredVersion);

    public static Payload CaBundle => new("cacert", "CA certificate bundle",
        "https://curl.se/ca/cacert.pem", "cacert.pem", PayloadKind.File, "latest");

    public static Payload VcRedist => new("vcredist", "Visual C++ 2015-2022 Redistributable (x64)",
        "https://aka.ms/vs/17/release/vc_redist.x64.exe", "vc_redist.x64.exe", PayloadKind.Exe, "17");

    /// <summary>ASP.NET Core Runtime 8 (x64) installer: the "latest 8.0" alias redirects to the newest patch.</summary>
    public static Payload AspNetRuntime => new("aspnet8", "ASP.NET Core Runtime 8.0 (x64)",
        "https://aka.ms/dotnet/8.0/aspnetcore-runtime-win-x64.exe", "aspnetcore-runtime-8.0-win-x64.exe", PayloadKind.Exe, "8.0");

    public static Payload PhpPinnedPayload => new("php", $"PHP {PhpPinned} (NTS x64)",
        $"https://windows.php.net/downloads/releases/archives/php-{PhpPinned}-nts-Win32-vs17-x64.zip",
        $"php-{PhpPinned}-nts-Win32-vs17-x64.zip", PayloadKind.Zip, PhpPinned);

    /// <summary>Latest PHP 8.4 NTS x64 from releases.json (falls back to the pinned archive URL).</summary>
    public static async Task<Payload> ResolvePhpAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync("https://windows.php.net/downloads/releases/releases.json", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(PhpSeries, out var series))
            {
                var version = series.GetProperty("version").GetString() ?? PhpPinned;
                foreach (var prop in series.EnumerateObject())
                {
                    if (!prop.Name.StartsWith("nts-", StringComparison.OrdinalIgnoreCase) || !prop.Name.EndsWith("-x64", StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.Value.ValueKind != JsonValueKind.Object || !prop.Value.TryGetProperty("zip", out var zip)) continue;
                    var path = zip.GetProperty("path").GetString()!;
                    var sha = zip.TryGetProperty("sha256", out var s) ? s.GetString() : null;
                    long? size = zip.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : null;
                    return new Payload("php", $"PHP {version} (NTS x64)", "https://windows.php.net/downloads/releases/" + path, path, PayloadKind.Zip, version, Sha256: sha, ExpectedSize: size);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"PHP releases.json lookup failed, using pinned {PhpPinned}: {ex.Message}");
        }
        return PhpPinnedPayload;
    }

    public static async Task<Payload> ResolveGitHubLatestAsync(HttpClient http, string repo, string id, string title, Func<string, bool> assetMatch, string fileNamePrefix, CancellationToken ct)
    {
        var json = await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!assetMatch(name)) continue;
            var url = asset.GetProperty("browser_download_url").GetString()!;
            var size = asset.GetProperty("size").GetInt64();
            return new Payload(id, $"{title} {tag}", url, $"{fileNamePrefix}-{tag}.zip", PayloadKind.Zip, tag, ExpectedSize: size);
        }
        throw new InvalidOperationException($"No matching release asset found in {repo} {tag}.");
    }

    public static Task<Payload> ResolveHikVisionAsync(HttpClient http, CancellationToken ct) =>
        ResolveGitHubLatestAsync(http, HikVisionRepo, "hikvision", "Biometric helper", n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), "hikvision", ct);

    public static Task<Payload> ResolveAppAsync(HttpClient http, CancellationToken ct) =>
        ResolveGitHubLatestAsync(http, AppRepo, "app", "JPrime app", n => n.StartsWith("jprimefitness.ph", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), "jprimefitness.ph", ct);
}
