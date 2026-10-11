namespace HelloPos.Kernel.Staff;

/// <summary>The three roles (Constitution VIII). Every staff member has exactly one, in one business.</summary>
public enum StaffRole
{
    Owner,
    Manager,
    Cashier,
}

/// <summary>What one staff member may try to do to another (feature 006, research R4).</summary>
public enum StaffAction
{
    /// <summary>Add a person with a POS username and PIN; the role is the new person's role.</summary>
    AddPosStaff,

    /// <summary>Invite someone by email for back-office access; the role is the offered role.</summary>
    Invite,

    ChangeRole,
    Rename,
    Deactivate,
    Reactivate,

    /// <summary>Set or reset someone's POS username and PIN.</summary>
    SetPin,

    ListStaff,
    ListInvitations,
}
