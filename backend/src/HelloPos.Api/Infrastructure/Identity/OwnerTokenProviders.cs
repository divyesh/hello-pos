using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace HelloPos.Api.Infrastructure.Identity;

/// <summary>
/// Email verification and password reset tokens (research R5). Like Identity's data-protection
/// provider (protected payload bound to user, purpose and security stamp), but reading the injected
/// <see cref="TimeProvider"/> so lifespans are testable. Renewing the security stamp invalidates every
/// outstanding token, which makes them single-use.
/// </summary>
public abstract class OwnerTokenProvider(IDataProtectionProvider dataProtection, TimeProvider clock)
    : IUserTwoFactorTokenProvider<OwnerAccount>
{
    private readonly IDataProtector protector = dataProtection.CreateProtector("HelloPos.OwnerTokens");

    protected abstract TimeSpan Lifespan { get; }

    public async Task<string> GenerateAsync(string purpose, UserManager<OwnerAccount> manager, OwnerAccount user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);
        using var buffer = new MemoryStream();
        await using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(clock.GetUtcNow().UtcTicks);
            writer.Write(user.Id.ToString());
            writer.Write(purpose);
            writer.Write(await manager.GetSecurityStampAsync(user).ConfigureAwait(false));
        }

        return WebEncoders.Base64UrlEncode(protector.Protect(buffer.ToArray()));
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<OwnerAccount> manager, OwnerAccount user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);
        try
        {
            using var reader = new BinaryReader(new MemoryStream(protector.Unprotect(WebEncoders.Base64UrlDecode(token))));
            var created = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            if (created + Lifespan < clock.GetUtcNow()
                || reader.ReadString() != user.Id.ToString()
                || reader.ReadString() != purpose)
            {
                return false;
            }

            var stamp = reader.ReadString();
            return reader.PeekChar() == -1
                && stamp == await manager.GetSecurityStampAsync(user).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or EndOfStreamException or IOException)
        {
            return false;
        }
    }

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<OwnerAccount> manager, OwnerAccount user) =>
        Task.FromResult(false);
}

/// <summary>Email verification links: 24 hours (FR-004).</summary>
public sealed class EmailConfirmationTokenProvider(IDataProtectionProvider dataProtection, TimeProvider clock)
    : OwnerTokenProvider(dataProtection, clock)
{
    public const string ProviderName = "EmailConfirmation";

    protected override TimeSpan Lifespan => TimeSpan.FromHours(24);
}

/// <summary>Password reset links: 1 hour (FR-004a).</summary>
public sealed class PasswordResetTokenProvider(IDataProtectionProvider dataProtection, TimeProvider clock)
    : OwnerTokenProvider(dataProtection, clock)
{
    public const string ProviderName = "PasswordReset";

    protected override TimeSpan Lifespan => TimeSpan.FromHours(1);
}
