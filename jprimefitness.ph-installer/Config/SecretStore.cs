using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JPrime.Panel.Config;

/// <summary>DPAPI (current user) protected key/value store for secrets the panel needs at runtime
/// (currently the Cloudflare tunnel token). File: <c>config\secrets.json</c>.</summary>
public sealed class SecretStore
{
    public const string TunnelToken = "tunnelToken";

    private readonly string _file;
    private readonly object _gate = new();

    public SecretStore(string file)
    {
        _file = file;
    }

    public string? Get(string key)
    {
        lock (_gate)
        {
            var map = ReadMap();
            if (!map.TryGetValue(key, out var b64) || string.IsNullOrEmpty(b64)) return null;
            try
            {
                var plain = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                // Written by a different Windows user; treat as missing.
                return null;
            }
        }
    }

    public void Set(string key, string? value)
    {
        lock (_gate)
        {
            var map = ReadMap();
            if (string.IsNullOrEmpty(value))
            {
                map.Remove(key);
            }
            else
            {
                var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
                map[key] = Convert.ToBase64String(cipher);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public bool Has(string key) => !string.IsNullOrEmpty(Get(key));

    private Dictionary<string, string> ReadMap()
    {
        if (!File.Exists(_file)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_file)) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
