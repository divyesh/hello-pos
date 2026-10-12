using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace HelloPos.Api.Infrastructure.Pos;

public enum PosSecretKind
{
    DeviceCredential,
    AccessToken,
    RefreshToken,
}

/// <summary>A parsed secret: the business and row it names, and the secret bytes to check.</summary>
public readonly record struct ParsedPosSecret(Guid BusinessId, Guid Id, byte[] Secret);

/// <summary>
/// Device credentials and session tokens (feature 007, research R1, R2):
/// <c>{prefix}{businessId:N}.{rowId:N}.{32 random bytes, base64url}</c>. The business id lets the API act
/// as that business to find the row; the request is accepted only when the secret's SHA-256 matches that
/// business's row, so the business still comes from an authenticated secret, never from request data.
/// </summary>
public static class PosSecrets
{
    private const int SecretBytes = 32;

    public static string Prefix(PosSecretKind kind) => kind switch
    {
        PosSecretKind.DeviceCredential => "dev_",
        PosSecretKind.AccessToken => "pat_",
        _ => "prt_",
    };

    /// <summary>A new secret and the hash to store.</summary>
    public static (string Secret, byte[] Hash) Issue(PosSecretKind kind, Guid businessId, Guid id)
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytes);
        var secret = $"{Prefix(kind)}{businessId:N}.{id:N}.{WebEncoders.Base64UrlEncode(bytes)}";
        return (secret, SHA256.HashData(bytes));
    }

    public static ParsedPosSecret? Parse(PosSecretKind kind, string? value)
    {
        var prefix = Prefix(kind);
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = value[prefix.Length..].Split('.');
        if (parts.Length != 3
            || !Guid.TryParseExact(parts[0], "N", out var business)
            || !Guid.TryParseExact(parts[1], "N", out var id))
        {
            return null;
        }

        try
        {
            var secret = WebEncoders.Base64UrlDecode(parts[2]);
            return secret.Length == SecretBytes ? new ParsedPosSecret(business, id, secret) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Constant-time comparison of the secret's hash with the stored one.</summary>
    public static bool Matches(ParsedPosSecret parsed, byte[]? storedHash) =>
        storedHash is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(parsed.Secret), storedHash);

    public static byte[] HashOf(ParsedPosSecret parsed) => SHA256.HashData(parsed.Secret);
}

/// <summary>
/// One-time device pairing codes (feature 007, FR-002): 8 characters from 31 without look-alikes (no
/// 0/O/1/I/L), shown as <c>ABCD-EFGH</c>; typed in any case, with or without the dash.
/// </summary>
public static class PairingCodes
{
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int Length = 8;

    public static (string Display, byte[] Hash) Issue()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var code = new string(chars);
        return ($"{code[..4]}-{code[4..]}", Hash(code));
    }

    /// <summary>The hash of a typed code, or <c>null</c> when it cannot be a code.</summary>
    public static byte[]? HashOf(string? typed)
    {
        if (typed is null)
        {
            return null;
        }

        var code = typed.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return code.Length == Length && code.All(c => Alphabet.Contains(c, StringComparison.Ordinal)) ? Hash(code) : null;
    }

    private static byte[] Hash(string code) => SHA256.HashData(Encoding.ASCII.GetBytes(code));
}
