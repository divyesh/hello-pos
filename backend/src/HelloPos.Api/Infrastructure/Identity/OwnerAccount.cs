using Microsoft.AspNetCore.Identity;

namespace HelloPos.Api.Infrastructure.Identity;

/// <summary>
/// A person who signs up and owns businesses (feature 005, FR-001). Platform data in schema
/// <c>identity</c>, not business-owned: sign-in must find the account before any business is known.
/// </summary>
public sealed class OwnerAccount : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public OwnerAccount()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
