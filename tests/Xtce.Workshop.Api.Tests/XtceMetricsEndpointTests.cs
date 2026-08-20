using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xtce.Workshop.Api.Tests;

public class XtceMetricsEndpointTests 
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    [Test]
    public async Task PostMetrics_ReturnsPerSystemAndTotalCounts()
    {
        var client = _factory.CreateClient();
        var document = new
        {
            name = "Root",
            children = new object[]
            {
                new
                {
                    name = "Bus",
                    children = Array.Empty<object>(),
                    telemetryMetaData = new
                    {
                        parameterTypeSet = new object[] { new { name = "T", kind = "Integer" } },
                        parameterSet = new object[] { new { name = "P", parameterTypeRef = "T" } },
                    },
                },
            },
        };

        var response = await client.PostAsJsonAsync("/api/xtce/metrics", document);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("systems").GetArrayLength());
        Assert.Equal(1, body.GetProperty("totals").GetProperty("parameters").GetInt32());
        var bus = body.GetProperty("systems").EnumerateArray().Single(s => s.GetProperty("systemPath").GetString() == "Root/Bus");
        Assert.Equal(1, bus.GetProperty("local").GetProperty("parameterTypes").GetInt32());
    }
}
