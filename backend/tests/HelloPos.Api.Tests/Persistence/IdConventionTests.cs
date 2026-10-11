using HelloPos.Api.Features.Businesses;
using HelloPos.Api.Tests.Tenancy.Samples;

namespace HelloPos.Api.Tests.Persistence;

/// <summary>FR-018: ids are UUID version 7, so devices can create them offline and they sort by time.</summary>
public sealed class IdConventionTests
{
    [Fact]
    public void NewEntities_GetVersion7Ids()
    {
        Assert.Equal(7, new Business { Name = "x", CountryCode = "IN", CurrencyCode = "INR", TimeZone = "Asia/Kolkata" }.Id.Version);
        Assert.Equal(7, new SampleCategory { Name = "x" }.Id.Version);
        Assert.Equal(7, new SampleProduct { Name = "x" }.Id.Version);
    }

    [Fact]
    public async Task LaterIds_SortAfterEarlierOnes()
    {
        var earlier = Guid.CreateVersion7();
        await Task.Delay(5, TestContext.Current.CancellationToken);
        var later = Guid.CreateVersion7();

        Assert.True(string.CompareOrdinal(earlier.ToString(), later.ToString()) < 0);
    }
}
