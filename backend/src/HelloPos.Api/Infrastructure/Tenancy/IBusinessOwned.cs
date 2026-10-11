namespace HelloPos.Api.Infrastructure.Tenancy;

/// <summary>
/// A record that belongs to exactly one business (Constitution, Principle III). Its table has key
/// <c>(business_id, id)</c>, links to other business-owned tables include <c>business_id</c>, every
/// query is scoped to the current business, and row-level security applies. <see cref="BusinessId"/>
/// is stamped on save from the current business; code never sets it.
/// </summary>
public interface IBusinessOwned
{
    Guid BusinessId { get; set; }

    /// <summary>UUIDv7: generatable offline by a device, time-ordered.</summary>
    Guid Id { get; }
}
