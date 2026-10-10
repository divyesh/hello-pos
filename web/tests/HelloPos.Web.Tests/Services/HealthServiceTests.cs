using System.Net;
using System.Text;
using HelloPos.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelloPos.Web.Tests.Services;

/// <summary>Every failure becomes "unavailable", never an unhandled error (FR-008, Principle IX).</summary>
public sealed class HealthServiceTests
{
    private const string ValidBody =
        """{"service":"hello-pos-api","version":"0.1.0-dev","status":"available","serverTimeUtc":"2026-10-10T14:03:22Z"}""";

    [Fact]
    public async Task Ok_WithValidBody_IsAvailable()
    {
        var result = await Check(_ => Json(HttpStatusCode.OK, ValidBody));

        var available = Assert.IsType<HealthCheckResult.Available>(result);
        Assert.Equal("available", available.Status);
        Assert.Equal("0.1.0-dev", available.Version);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"title":"boom"}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"service":"hello-pos-api"}""")]
    public async Task BadResponses_AreUnavailable(HttpStatusCode status, string body)
    {
        var result = await Check(_ => Json(status, body));

        Assert.IsType<HealthCheckResult.Unavailable>(result);
    }

    [Fact]
    public async Task NetworkError_IsUnavailable()
    {
        var result = await Check(_ => throw new HttpRequestException("connection refused"));

        Assert.IsType<HealthCheckResult.Unavailable>(result);
    }

    [Fact]
    public async Task SlowResponse_TimesOutAsUnavailable()
    {
        var result = await Check(
            async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                return Json(HttpStatusCode.OK, ValidBody);
            },
            timeout: TimeSpan.FromMilliseconds(200));

        Assert.IsType<HealthCheckResult.Unavailable>(result);
    }

    private static Task<HealthCheckResult> Check(Func<CancellationToken, HttpResponseMessage> respond) =>
        Check(token => Task.FromResult(respond(token)));

    private static async Task<HealthCheckResult> Check(
        Func<CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
    {
        using var http = new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://api.test/") };
        var service = new HealthService(http, NullLogger<HealthService>.Instance, timeout ?? HealthService.DefaultTimeout);
        return await service.CheckAsync(TestContext.Current.CancellationToken);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(cancellationToken);
    }
}
