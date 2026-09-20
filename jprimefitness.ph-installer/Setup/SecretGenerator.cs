using System.Security.Cryptography;

namespace JPrime.Panel.Setup;

public static class SecretGenerator
{
    /// <summary>URL-safe random token (default 32 bytes = 43 chars). Never contains <c>$</c>, <c>{</c>, quotes or spaces.</summary>
    public static string Token(int bytes = 32)
    {
        var buf = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buf).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Hex token (48 chars), same shape as the helper's existing ForwardToken.</summary>
    public static string Hex(int bytes = 24) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}

public static class PasswordPolicy
{
    /// <summary>Mirrors Laravel's Password::defaults() in this app: min 8, mixed case, at least one symbol.</summary>
    public static string? Validate(string password)
    {
        if (password.Length < 8) return "Password must be at least 8 characters.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower)) return "Password must contain both upper and lower case letters.";
        if (!password.Any(c => !char.IsLetterOrDigit(c))) return "Password must contain at least one symbol (for example ! or #).";
        return null;
    }
}

public static class TokenPolicy
{
    /// <summary>Shared tokens land in .env and appsettings.json: long enough to be unguessable, no <c>${</c> (env interpolation), quotes or whitespace.</summary>
    public static string? Validate(string token, string label)
    {
        if (token.Length < 16) return $"{label} must be at least 16 characters.";
        if (token.Contains("${") || token.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'')) return $"{label} must not contain spaces, quotes or ${{.";
        return null;
    }
}
