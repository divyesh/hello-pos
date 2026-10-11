using HelloPos.Kernel.Staff;
using static HelloPos.Kernel.Staff.StaffAction;
using static HelloPos.Kernel.Staff.StaffRole;

namespace HelloPos.Kernel.Tests.Staff;

/// <summary>Feature 006 research R4: the role rule table, cell by cell (SC-003).</summary>
public sealed class StaffRulesTests
{
    public static TheoryData<StaffRole, StaffAction, StaffRole?, StaffRole?, bool> Table() => new()
    {
        // Add POS staff: the new person's role is the "new role".
        { Owner, AddPosStaff, null, Cashier, true },
        { Owner, AddPosStaff, null, Manager, true },
        { Owner, AddPosStaff, null, Owner, false },      // owners need an account: invitation only
        { Manager, AddPosStaff, null, Cashier, true },
        { Manager, AddPosStaff, null, Manager, false },
        { Manager, AddPosStaff, null, Owner, false },
        { Cashier, AddPosStaff, null, Cashier, false },

        // Invite (back-office access).
        { Owner, Invite, null, Owner, true },
        { Owner, Invite, null, Manager, true },
        { Owner, Invite, null, Cashier, false },
        { Manager, Invite, null, Manager, false },
        { Manager, Invite, null, Owner, false },
        { Cashier, Invite, null, Manager, false },

        // Change role: owners only.
        { Owner, ChangeRole, Cashier, Manager, true },
        { Owner, ChangeRole, Manager, Owner, true },
        { Owner, ChangeRole, Owner, Manager, true },
        { Owner, ChangeRole, Owner, Cashier, true },
        { Manager, ChangeRole, Cashier, Manager, false },
        { Manager, ChangeRole, Cashier, Cashier, false },
        { Manager, ChangeRole, Manager, Cashier, false },
        { Cashier, ChangeRole, Cashier, Manager, false },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void RoleTable(StaffRole actor, StaffAction action, StaffRole? target, StaffRole? newRole, bool allowed) =>
        Assert.Equal(allowed, StaffRules.Can(actor, action, target, newRole));

    [Theory]
    [InlineData(Rename)]
    [InlineData(Deactivate)]
    [InlineData(Reactivate)]
    [InlineData(SetPin)]
    public void ActionsOnAPerson_OwnersOnAnyone_ManagersOnCashiersOnly_CashiersNever(StaffAction action)
    {
        foreach (var target in Enum.GetValues<StaffRole>())
        {
            Assert.True(StaffRules.Can(Owner, action, target));
            Assert.Equal(target == Cashier, StaffRules.Can(Manager, action, target));
            Assert.False(StaffRules.Can(Cashier, action, target));
        }
    }

    [Fact]
    public void Lists_StaffForOwnersAndManagers_InvitationsForOwners()
    {
        Assert.True(StaffRules.Can(Owner, ListStaff));
        Assert.True(StaffRules.Can(Manager, ListStaff));
        Assert.False(StaffRules.Can(Cashier, ListStaff));
        Assert.True(StaffRules.Can(Owner, ListInvitations));
        Assert.False(StaffRules.Can(Manager, ListInvitations));
        Assert.False(StaffRules.Can(Cashier, ListInvitations));
    }

    [Fact]
    public void BackOfficeRoles_AreOwnerAndManager()
    {
        Assert.True(StaffRules.HasBackOffice(Owner));
        Assert.True(StaffRules.HasBackOffice(Manager));
        Assert.False(StaffRules.HasBackOffice(Cashier));
        Assert.True(StaffRules.RequiresAccount(Owner));
        Assert.False(StaffRules.RequiresAccount(Manager));
    }

    [Theory]
    [InlineData("ravi", true)]
    [InlineData("Ravi.K-2", true)]
    [InlineData("ab", false)]
    [InlineData("has space", false)]
    [InlineData("toolong_toolong_toolong_toolong_x", false)]
    [InlineData("", false)]
    public void PosUsername(string username, bool valid)
    {
        Assert.Equal(valid, StaffRules.ValidatePosUsername(username) is null);
        if (valid)
        {
            Assert.Equal(username.ToLowerInvariant(), StaffRules.NormalizePosUsername(username));
        }
    }

    [Theory]
    [InlineData("Ravi", true)]
    [InlineData("  ", false)]
    [InlineData("", false)]
    public void DisplayName(string name, bool valid) =>
        Assert.Equal(valid, StaffRules.ValidateDisplayName(name) is null);

    [Fact]
    public void DisplayName_LongerThan100_IsRefused() =>
        Assert.NotNull(StaffRules.ValidateDisplayName(new string('x', 101)));
}
