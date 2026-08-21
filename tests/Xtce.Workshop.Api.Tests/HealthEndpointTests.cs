using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xtce.Workshop.Api.Tests;

public class HealthEndpointTests 
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    [Test]
    public async Task GetHealth_CarriesTheBuildVersion()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        // "dev" outside stamped image builds; never absent or empty.
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
    }

    [Test]
    public async Task GetHealth_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
