using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xtce.Workshop.Api.Tests;

public class XtceNamespaceDetectionTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    [Test]
    public async Task Load_Xtce12Document_ReportsNamespaceAndVersion()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new
        {
            xml = """<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat"/>""",
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("http://www.omg.org/spec/XTCE/20180204", body.GetProperty("rootNamespace").GetString());
        Assert.Equal("1.2", body.GetProperty("detectedVersion").GetString());
    }

    [Test]
    public async Task Load_LegacyNamespaceDocument_StillLoadsAndSaysWhatItIs()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new
        {
            xml = """<SpaceSystem xmlns="http://www.omg.org/space/xtce" name="Sat"/>""",
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Local-name matching means legacy documents load; the response must still
        // lead with the declared version so the verifier can say so.
        Assert.Equal("Sat", body.GetProperty("name").GetString());
        Assert.Equal("http://www.omg.org/space/xtce", body.GetProperty("rootNamespace").GetString());
        Assert.Equal("1.0/1.1", body.GetProperty("detectedVersion").GetString());
    }

    [Test]
    public async Task Load_MalformedDocument_CarriesNullNamespaceWithTheDiagnostics()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml = "not xml <<<" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("rootNamespace").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("detectedVersion").ValueKind);
    }
}
