using System.Text.RegularExpressions;

namespace JPrime.Panel.Setup;

/// <summary>A Cloudflare connector token is a long base64 blob starting with <c>eyJ</c>. The dashboard shows it inside a
/// "cloudflared service install &lt;token&gt;" command that people paste whole, so we pick the token out by shape.</summary>
public static partial class TunnelToken
{
    [GeneratedRegex(@"eyJ[A-Za-z0-9+/=_\-.]{37,}")]
    private static partial Regex Shape();

    public static bool TryExtract(string pasted, out string token)
    {
        var matches = Shape().Matches(pasted);
        token = matches.Count > 0 ? matches[^1].Value.TrimEnd('.') : "";
        return token.Length > 0;
    }
}
