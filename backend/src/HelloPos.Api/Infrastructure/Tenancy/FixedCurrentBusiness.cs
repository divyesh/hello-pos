namespace HelloPos.Api.Infrastructure.Tenancy;

/// <summary>
/// Acting as one specific business, chosen by server code, never by request data: creating a business
/// (the server-generated id, research R6) and, later, background work for one business.
/// </summary>
public sealed class FixedCurrentBusiness(Guid businessId) : ICurrentBusiness
{
    public Guid? BusinessId { get; } = businessId;
}
