using System.Text.RegularExpressions;

namespace HelloPos.Kernel.Staff;

/// <summary>
/// The role rule table (feature 006, research R4), used by the API (authoritative) and the web (to
/// offer only allowed actions). Data rules that need the database (the last active owner, an owner
/// needing an account) are checked by the API on top of these.
/// </summary>
public static partial class StaffRules
{
    public const int DisplayNameMaxLength = 100;

    /// <summary>
    /// Whether <paramref name="actor"/> may perform <paramref name="action"/> on a person whose role is
    /// <paramref name="target"/>; <paramref name="newRole"/> is the role being given (add, invite, change).
    /// </summary>
    public static bool Can(StaffRole actor, StaffAction action, StaffRole? target = null, StaffRole? newRole = null) =>
        (actor, action) switch
        {
            (StaffRole.Owner, StaffAction.AddPosStaff) => newRole is StaffRole.Cashier or StaffRole.Manager,
            (StaffRole.Manager, StaffAction.AddPosStaff) => newRole is StaffRole.Cashier,
            (StaffRole.Owner, StaffAction.Invite) => newRole is StaffRole.Owner or StaffRole.Manager,
            (StaffRole.Owner, StaffAction.ChangeRole) => target is not null && newRole is not null,
            (StaffRole.Owner, StaffAction.Rename or StaffAction.Deactivate or StaffAction.Reactivate or StaffAction.SetPin) => true,
            (StaffRole.Manager, StaffAction.Rename or StaffAction.Deactivate or StaffAction.Reactivate or StaffAction.SetPin) =>
                target is StaffRole.Cashier,
            (StaffRole.Owner or StaffRole.Manager, StaffAction.ListStaff) => true,
            (StaffRole.Owner, StaffAction.ListInvitations) => true,
            _ => false,
        };

    /// <summary>Owners and managers use the back office; cashiers only the POS.</summary>
    public static bool HasBackOffice(StaffRole role) => role is StaffRole.Owner or StaffRole.Manager;

    /// <summary>An owner always has a back-office account (to sign in and receive security emails).</summary>
    public static bool RequiresAccount(StaffRole role) => role == StaffRole.Owner;

    public static string? ValidateDisplayName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > DisplayNameMaxLength
            ? $"Enter a name of 1 to {DisplayNameMaxLength} characters."
            : null;
    }

    public static string? ValidatePosUsername(string? username) =>
        username is not null && PosUsername().IsMatch(username.ToLowerInvariant())
            ? null
            : "Use 3 to 32 letters, digits, dots, dashes or underscores.";

    /// <summary>Usernames are case-insensitive: stored and compared lower-case.</summary>
    public static string NormalizePosUsername(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        return username.Trim().ToLowerInvariant();
    }

    [GeneratedRegex("^[a-z0-9._-]{3,32}$")]
    private static partial Regex PosUsername();
}
