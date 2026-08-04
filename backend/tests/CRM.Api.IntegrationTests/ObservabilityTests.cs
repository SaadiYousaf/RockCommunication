using System.Linq;
using System.Net;

namespace CRM.Api.IntegrationTests;

/// <summary>Locks in the enterprise observability/ops surface: liveness vs readiness health and per-request correlation ids.</summary>
public class ObservabilityTests : IClassFixture<CrmWebAppFactory>
{
    private readonly CrmWebAppFactory _factory;
    public ObservabilityTests(CrmWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Liveness_health_is_up()
    {
        var resp = await _factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Readiness_health_reports_database_healthy()
    {
        var resp = await _factory.CreateClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("database", body);
        Assert.Contains("Healthy", body);
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id()
    {
        var resp = await _factory.CreateClient().GetAsync("/health");
        Assert.True(resp.Headers.Contains("X-Correlation-ID"));
        Assert.False(string.IsNullOrWhiteSpace(resp.Headers.GetValues("X-Correlation-ID").First()));
    }

    [Fact]
    public async Task Inbound_correlation_id_is_echoed_back()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add("X-Correlation-ID", "corr-test-123");
        var resp = await _factory.CreateClient().SendAsync(req);
        Assert.Equal("corr-test-123", resp.Headers.GetValues("X-Correlation-ID").First());
    }
}
