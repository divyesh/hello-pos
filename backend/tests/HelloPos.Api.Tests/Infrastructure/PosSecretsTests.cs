using HelloPos.Api.Infrastructure.Pos;

namespace HelloPos.Api.Tests.Infrastructure;

/// <summary>Feature 007 research R1, R2: device credentials, session tokens and pairing codes.</summary>
public sealed class PosSecretsTests
{
    [Theory]
    [InlineData(PosSecretKind.DeviceCredential, "dev_")]
    [InlineData(PosSecretKind.AccessToken, "pat_")]
    [InlineData(PosSecretKind.RefreshToken, "prt_")]
    public void Issued_SecretParsesBack_ToItsBusinessAndId_AndHashesToTheStoredValue(PosSecretKind kind, string prefix)
    {
        var business = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        var (secret, hash) = PosSecrets.Issue(kind, business, id);

        Assert.StartsWith(prefix, secret, StringComparison.Ordinal);
        var parsed = PosSecrets.Parse(kind, secret)!.Value;
        Assert.Equal(business, parsed.BusinessId);
        Assert.Equal(id, parsed.Id);
        Assert.True(PosSecrets.Matches(parsed, hash));
    }

    [Fact]
    public void Two_Issues_AreDifferent()
    {
        var business = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        Assert.NotEqual(PosSecrets.Issue(PosSecretKind.AccessToken, business, id).Secret,
            PosSecrets.Issue(PosSecretKind.AccessToken, business, id).Secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pat_")]
    [InlineData("pat_nothex.nothex.secret")]
    [InlineData("dev_0199d1c480007000800000000000000a.0199d1c480007000800000000000000b.c2hvcnQ")]
    public void Malformed_IsRejected(string value) => Assert.Null(PosSecrets.Parse(PosSecretKind.AccessToken, value));

    [Fact]
    public void AKindIsNotAnother()
    {
        var (secret, _) = PosSecrets.Issue(PosSecretKind.RefreshToken, Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Null(PosSecrets.Parse(PosSecretKind.AccessToken, secret));
    }

    [Fact]
    public void ChangingTheBusinessInATokenBreaksTheMatch()
    {
        var business = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        var (secret, hash) = PosSecrets.Issue(PosSecretKind.DeviceCredential, business, id);
        var forged = secret.Replace(business.ToString("N"), Guid.CreateVersion7().ToString("N"), StringComparison.Ordinal);

        var parsed = PosSecrets.Parse(PosSecretKind.DeviceCredential, forged)!.Value;

        Assert.NotEqual(business, parsed.BusinessId);
        Assert.True(PosSecrets.Matches(parsed, hash)); // the secret alone matches; the API also requires the row of that business
    }

    [Fact]
    public void PairingCodes_UseNoLookAlikes_AndNormalize()
    {
        for (var i = 0; i < 200; i++)
        {
            var (display, _) = PairingCodes.Issue();
            Assert.Matches("^[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}$", display);
        }

        var (code, hash) = PairingCodes.Issue();
        Assert.Equal(hash, PairingCodes.HashOf(code));
        Assert.Equal(hash, PairingCodes.HashOf(code.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant()));
        Assert.Equal(hash, PairingCodes.HashOf(" " + code.ToLowerInvariant() + " "));
        Assert.Null(PairingCodes.HashOf("ABCD-EFG"));
        Assert.Null(PairingCodes.HashOf("ABCD-EFG0"));
        Assert.Null(PairingCodes.HashOf(null));
    }
}
