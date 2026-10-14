using System.Net;
using HelloPos.Api.Tests.Infrastructure.Database;
using HelloPos.Kernel.Staff;
using static HelloPos.Api.Tests.Features.Catalog.CatalogApi;
using static HelloPos.Api.Tests.Features.Owners.OwnerApi;
using static HelloPos.Api.Tests.Features.BusinessSettings.SettingsApi;

namespace HelloPos.Api.Tests.Features.BusinessSettings;

/// <summary>Feature #12 US1: payment types (FR-001–FR-005, clarification Q1: owners only).</summary>
[Collection(PostgresTests.Name)]
public sealed class PaymentTypeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ANewBusiness_HasCash_ThatGivesChangeAndOpensTheDrawer()
    {
        await using var shop = await ShopAsync(postgres);

        var types = (await JsonAsync(await AssertContractAsync(await ListPaymentTypesAsync(shop.Owner, shop.Id), HttpStatusCode.OK, "listPaymentTypes")))
            .AsArray();

        var cash = Assert.Single(types)!;
        Assert.Equal(("Cash", "cash", true, true, true, true), ((string)cash["name"]!, (string)cash["kind"]!, (bool)cash["builtIn"]!,
            (bool)cash["givesChange"]!, (bool)cash["opensDrawer"]!, (bool)cash["active"]!));
    }

    [Fact]
    public async Task AnOwner_AddsRecordOnlyTypes_WithSensibleDefaults()
    {
        await using var shop = await ShopAsync(postgres);

        var upi = await JsonAsync(await AssertContractAsync(
            await CreatePaymentTypeAsync(shop.Owner, shop.Id, PaymentTypeBody("UPI", "digitalWallet")), HttpStatusCode.Created, "createPaymentType"));
        var voucher = await JsonAsync(await CreatePaymentTypeAsync(shop.Owner, shop.Id, PaymentTypeBody("Gift voucher", "voucher", opensDrawer: true)));
        var cheque = await JsonAsync(await CreatePaymentTypeAsync(shop.Owner, shop.Id, PaymentTypeBody("Cheque", "cheque", asksReference: false)));

        Assert.Equal(("digitalWallet", false, true, false, false), ((string)upi["kind"]!, (bool)upi["givesChange"]!, (bool)upi["asksReference"]!,
            (bool)upi["opensDrawer"]!, (bool)upi["builtIn"]!));
        Assert.Equal((false, true), ((bool)voucher["asksReference"]!, (bool)voucher["opensDrawer"]!));
        Assert.False((bool)cheque["asksReference"]!);
        Assert.Equal(["Cash", "UPI", "Gift voucher", "Cheque"],
            (await JsonAsync(await ListPaymentTypesAsync(shop.Owner, shop.Id))).AsArray().Select(t => (string)t!["name"]!));
        Assert.Equal(["paymentType:created", "paymentType:created", "paymentType:created"], await SettingsHistoryAsync(shop.App));
    }

    [Fact]
    public async Task NamesAreUniqueIgnoringCase_AndCashIsNotAKindToChoose()
    {
        await using var shop = await ShopAsync(postgres);
        await PaymentTypeAsync(shop.Owner, shop.Id, "UPI");

        var duplicate = await JsonAsync(await AssertContractAsync(
            await CreatePaymentTypeAsync(shop.Owner, shop.Id, PaymentTypeBody(" upi ", "other")), HttpStatusCode.BadRequest, "createPaymentType"));
        var cash = await JsonAsync(await CreatePaymentTypeAsync(shop.Owner, shop.Id, PaymentTypeBody("Petty cash", "cash")));
        var cashName = await JsonAsync(await CreatePaymentTypeAsync(shop.Owner, shop.Id, PaymentTypeBody("CASH", "other")));

        Assert.NotNull(duplicate["errors"]!["name"]);
        Assert.NotNull(cash["errors"]!["kind"]);
        Assert.NotNull(cashName["errors"]!["name"]);
    }

    [Fact]
    public async Task Cash_CanBeRenamedAndMoved_ButNeverDeactivatedOrChangedInKind()
    {
        await using var shop = await ShopAsync(postgres);
        var cash = (await JsonAsync(await ListPaymentTypesAsync(shop.Owner, shop.Id)))[0]!;
        var id = Guid.Parse((string)cash["id"]!);

        await AssertContractAsync(await SetPaymentTypeActiveAsync(shop.Owner, shop.Id, id, false, 1), HttpStatusCode.Conflict, "deactivatePaymentType");
        var kind = await UpdatePaymentTypeAsync(shop.Owner, shop.Id, id, "Cash", "other", true, false, 1);
        var renamed = await JsonAsync(await AssertContractAsync(
            await UpdatePaymentTypeAsync(shop.Owner, shop.Id, id, "Cash (till)", "cash", true, false, 1), HttpStatusCode.OK, "updatePaymentType"));

        Assert.Equal(HttpStatusCode.BadRequest, kind.StatusCode);
        Assert.Equal(("Cash (till)", "cash", true, 2), ((string)renamed["name"]!, (string)renamed["kind"]!, (bool)renamed["active"]!, (int)renamed["version"]!));
    }

    [Fact]
    public async Task OtherTypes_AreChangedDeactivatedAndReactivated_WithTheirVersion()
    {
        await using var shop = await ShopAsync(postgres);
        var (id, version) = await PaymentTypeAsync(shop.Owner, shop.Id, "Card", "externalCard");

        var changed = await JsonAsync(await UpdatePaymentTypeAsync(shop.Owner, shop.Id, id, "Card (terminal)", "externalCard", true, true, version));
        await AssertContractAsync(await UpdatePaymentTypeAsync(shop.Owner, shop.Id, id, "Stale", "externalCard", true, true, version), HttpStatusCode.Conflict, "updatePaymentType");
        var off = await JsonAsync(await AssertContractAsync(await SetPaymentTypeActiveAsync(shop.Owner, shop.Id, id, false, 2), HttpStatusCode.OK, "deactivatePaymentType"));
        var on = await JsonAsync(await AssertContractAsync(await SetPaymentTypeActiveAsync(shop.Owner, shop.Id, id, true, 3), HttpStatusCode.OK, "reactivatePaymentType"));

        Assert.Equal(("Card (terminal)", true), ((string)changed["name"]!, (bool)changed["opensDrawer"]!));
        Assert.False((bool)off["active"]!);
        Assert.True((bool)on["active"]!);
        Assert.Equal(["paymentType:created", "paymentType:updated", "paymentType:deactivated", "paymentType:reactivated"], await SettingsHistoryAsync(shop.App));
        Assert.Equal(HttpStatusCode.NotFound, (await UpdatePaymentTypeAsync(shop.Owner, shop.Id, Guid.CreateVersion7(), "X", "other", false, false, 1)).StatusCode);
    }

    [Fact]
    public async Task Reordering_NeedsEveryTypeOnce_AndTheTillOrderFollows()
    {
        await using var shop = await ShopAsync(postgres);
        var cash = Guid.Parse((string)(await JsonAsync(await ListPaymentTypesAsync(shop.Owner, shop.Id)))[0]!["id"]!);
        var (upi, _) = await PaymentTypeAsync(shop.Owner, shop.Id, "UPI");
        var (card, _) = await PaymentTypeAsync(shop.Owner, shop.Id, "Card", "externalCard");

        var ordered = await JsonAsync(await AssertContractAsync(await ReorderPaymentTypesAsync(shop.Owner, shop.Id, upi, cash, card), HttpStatusCode.OK, "reorderPaymentTypes"));
        var missing = await ReorderPaymentTypesAsync(shop.Owner, shop.Id, upi, cash);
        var twice = await ReorderPaymentTypesAsync(shop.Owner, shop.Id, upi, cash, card, card);
        var unknown = await ReorderPaymentTypesAsync(shop.Owner, shop.Id, upi, cash, Guid.CreateVersion7());

        Assert.Equal(["UPI", "Cash", "Card"], ordered.AsArray().Select(t => (string)t!["name"]!));
        Assert.Equal(["UPI", "Cash", "Card"], (await JsonAsync(await ListPaymentTypesAsync(shop.Manager, shop.Id))).AsArray().Select(t => (string)t!["name"]!));
        Assert.All([missing, twice, unknown], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Contains("paymentType:reordered", await SettingsHistoryAsync(shop.App));
    }

    [Fact]
    public async Task AtMostTwentyTypes_AreActive()
    {
        await using var shop = await ShopAsync(postgres);
        for (var i = 1; i <= 19; i++)
        {
            await PaymentTypeAsync(shop.Owner, shop.Id, $"Voucher {i}", "voucher");
        }

        var tooMany = await CreatePaymentTypeAsync(shop.Owner, shop.Id, PaymentTypeBody("Voucher 20", "voucher"));

        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task Managers_SeeButCannotChange_AndCashiersCannotReach()
    {
        await using var shop = await ShopAsync(postgres);
        var (id, version) = await PaymentTypeAsync(shop.Owner, shop.Id, "UPI");

        Assert.Equal(HttpStatusCode.OK, (await ListPaymentTypesAsync(shop.Manager, shop.Id)).StatusCode);
        await AssertContractAsync(await CreatePaymentTypeAsync(shop.Manager, shop.Id, PaymentTypeBody("Cheque", "cheque")), HttpStatusCode.Forbidden, "createPaymentType");
        Assert.Equal(HttpStatusCode.Forbidden, (await UpdatePaymentTypeAsync(shop.Manager, shop.Id, id, "X", "other", false, false, version)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetPaymentTypeActiveAsync(shop.Manager, shop.Id, id, false, version)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReorderPaymentTypesAsync(shop.Manager, shop.Id, id)).StatusCode);

        using var cashier = await shop.App.SignUpVerifiedOwnerAsync("cashier@example.com");
        await shop.App.AddStaffAsync(shop.Id, StaffRole.Cashier, await HelloPos.Api.Tests.Tenancy.TenancyApp.AccountIdAsync(cashier), "Cash");
        Assert.Equal(HttpStatusCode.Unauthorized, (await ListPaymentTypesAsync(cashier, shop.Id)).StatusCode);
    }
}
