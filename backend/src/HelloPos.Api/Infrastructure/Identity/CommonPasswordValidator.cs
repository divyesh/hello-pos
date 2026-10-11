using System.Collections.Frozen;
using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Infrastructure.Identity;

/// <summary>
/// Password rules beyond length (research R2, NIST 800-63B): at most 128 characters, and not one of
/// the 10,000 most common passwords (bundled list, no network call). No composition rules.
/// </summary>
public sealed class CommonPasswordValidator : IPasswordValidator<OwnerAccount>
{
    public const int MaxLength = 128;
    public const string TooLongCode = "PasswordTooLong";
    public const string TooCommonCode = "PasswordTooCommon";

    private static readonly Lazy<FrozenSet<string>> Common = new(Load);

    public Task<IdentityResult> ValidateAsync(UserManager<OwnerAccount> manager, OwnerAccount user, string? password)
    {
        if (password is null)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        if (password.Length > MaxLength)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = TooLongCode,
                Description = $"Use at most {MaxLength} characters.",
            }));
        }

        return Task.FromResult(IsCommon(password)
            ? IdentityResult.Failed(new IdentityError
            {
                Code = TooCommonCode,
                Description = "This password is too common. Choose one that is harder to guess.",
            })
            : IdentityResult.Success);
    }

    public static bool IsCommon(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return Common.Value.Contains(password.ToLowerInvariant());
    }

    private static FrozenSet<string> Load()
    {
        using var stream = typeof(CommonPasswordValidator).Assembly
            .GetManifestResourceStream("HelloPos.Api.Infrastructure.Identity.common-passwords.txt")
            ?? throw new InvalidOperationException("The bundled common password list is missing.");
        using var reader = new StreamReader(stream);
        var passwords = new List<string>(10_000);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                passwords.Add(line);
            }
        }

        return passwords.ToFrozenSet(StringComparer.Ordinal);
    }
}
