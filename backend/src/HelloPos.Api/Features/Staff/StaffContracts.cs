using System.Text.Json.Serialization;
using HelloPos.Kernel.Staff;

namespace HelloPos.Api.Features.Staff;

/// <summary>Contract <c>StaffMember</c>: never a PIN, only whether one is set.</summary>
public sealed record StaffMemberResponse(
    Guid Id,
    string DisplayName,
    string Role,
    bool Active,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PosUsername,
    bool HasPin,
    bool HasBackOfficeAccess,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static StaffMemberResponse From(StaffMember staff)
    {
        ArgumentNullException.ThrowIfNull(staff);
        return new StaffMemberResponse(
            staff.Id, staff.DisplayName, StaffRoleNames.ToWire(staff.Role), staff.Active, staff.PosUsername,
            staff.PinHash is not null, staff.AccountId is not null, staff.Version, staff.CreatedAt, staff.UpdatedAt);
    }
}

/// <summary>Contract <c>NewStaffMember</c>; loosely typed so every bad value becomes a field error.</summary>
public sealed record NewStaffMemberRequest(string? DisplayName, string? Role, string? PosUsername, string? Pin);

public static class StaffValidation
{
    /// <summary>Kernel rules for a person's name, POS username and PIN, as API field errors.</summary>
    public static void ValidatePosFields(string? displayName, string? posUsername, string? pin, Dictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (StaffRules.ValidateDisplayName(displayName) is { } nameError)
        {
            errors["displayName"] = [nameError];
        }

        if (StaffRules.ValidatePosUsername(posUsername) is { } usernameError)
        {
            errors["posUsername"] = [usernameError];
        }

        if (PinRules.Validate(pin) is { } pinError)
        {
            errors["pin"] = [pinError];
        }
    }

    public const string UsernameTaken = "Another person in this business already uses this username.";
}
