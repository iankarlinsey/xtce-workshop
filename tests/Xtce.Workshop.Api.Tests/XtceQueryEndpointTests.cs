using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Xtce.Workshop.Api.Tests;

public class XtceQueryEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public XtceQueryEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static object SampleDocument() => new
    {
        name = "Root",
        children = Array.Empty<object>(),
        telemetryMetaData = new
        {
            parameterTypeSet = new object[] { new { name = "T", kind = "Integer" } },
            parameterSet = new object[] { new { name = "BattVoltage", parameterTypeRef = "T" } },
            containerSet = new object[]
            {
                new
                {
                    name = "Hk",
                    entryList = new object[] { new { kind = "ParameterRef", @ref = "BattVoltage" } },
                },
            },
        },
    };

    [Fact]
    public async Task PostSearch_ReturnsMatches()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/search", new { document = SampleDocument(), query = "batt" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var match = Assert.Single(body.GetProperty("matches").EnumerateArray());
        Assert.Equal("BattVoltage", match.GetProperty("name").GetString());
        Assert.Equal("Root", match.GetProperty("systemPath").GetString());
    }

    [Fact]
    public async Task PostUsages_ReturnsReferencesToTheParameter()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/usages",
            new { document = SampleDocument(), systemPath = "Root", parameterName = "BattVoltage" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var usage = Assert.Single(body.GetProperty("usages").EnumerateArray());
        Assert.Equal("ParameterRefEntry", usage.GetProperty("kind").GetString());
        Assert.Equal("Root/ContainerSet/Hk", usage.GetProperty("location").GetString());
    }
}
