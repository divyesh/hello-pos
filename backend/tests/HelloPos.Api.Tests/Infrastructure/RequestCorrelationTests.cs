using System.Net;
using System.Text.Json;
using HelloPos.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Sinks.InMemory;

namespace HelloPos.Api.Tests.Infrastructure;

/// <summary>Constitution, Principle IX: one structured summary per request, correlated by trace id.</summary>
public sealed class RequestCorrelationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Request_LogsOneSummaryEvent_WithTraceIdMatchingProblemDetails()
    {
        var sink = new InMemorySink();
        using var client = factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)))
            .CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var traceId = body.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrEmpty(traceId));

        // The summary is written when the pipeline unwinds, which can be after the client already has the response.
        var summaries = await SummariesAsync(sink, "/does-not-exist");
        var summary = Assert.Single(summaries);

        Assert.Equal("\"GET\"", summary.Properties["RequestMethod"].ToString());
        Assert.Equal("404", summary.Properties["StatusCode"].ToString());
        Assert.True(summary.Properties.ContainsKey("Elapsed"));
        Assert.Equal(traceId, summary.TraceId?.ToHexString());
    }

    [Fact]
    public void Seq_IsEnabledOnlyWhenServerUrlIsConfigured()
    {
        var without = new ConfigurationBuilder().Build();
        var with = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seq:ServerUrl"] = "http://seq:5341" })
            .Build();

        Assert.False(Logging.IsSeqEnabled(without));
        Assert.True(Logging.IsSeqEnabled(with));
    }

    [Fact]
    public async Task Api_StartsAndServes_WithoutSeqConfigured()
    {
        using var client = factory
            .WithWebHostBuilder(b => b.UseSetting("Seq:ServerUrl", string.Empty))
            .CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Waits (up to 5 s) for the request summary events of a path; returns what arrived.</summary>
    private static async Task<List<Serilog.Events.LogEvent>> SummariesAsync(InMemorySink sink, string path)
    {
        var quoted = $"\"{path}\"";
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var summaries = sink.LogEvents
                .Where(e => e.Properties.TryGetValue("RequestPath", out var value)
                            && value.ToString() == quoted
                            && e.Properties.ContainsKey("StatusCode"))
                .ToList();
            if (summaries.Count > 0 || DateTime.UtcNow > deadline)
            {
                return summaries;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
