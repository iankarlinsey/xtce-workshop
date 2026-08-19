using System.Net;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xtce.Workshop.Model;
using Xunit;

namespace Xtce.Workshop.Api.Tests;

public class XtceSaveEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public XtceSaveEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostSave_ChildlessDocument_ReturnsXmlLoadableByLoadEndpoint()
    {
        var client = _factory.CreateClient();
        var document = new SpaceSystem("Minimal", []);

        var saveResponse = await client.PostAsJsonAsync("/api/xtce/save", document);
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        var xml = await saveResponse.Content.ReadAsStringAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(xml), "file", "roundtrip.xml");
        var loadResponse = await client.PostAsync("/api/xtce/load", content);

        Assert.Equal(HttpStatusCode.OK, loadResponse.StatusCode);
        var body = await loadResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Minimal", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task PostSave_NestedDocument_ReturnsXmlLoadableByLoadEndpoint()
    {
        var client = _factory.CreateClient();
        var document = new SpaceSystem("Mission", [
            new SpaceSystem("Bus", [
                new SpaceSystem("Power", []),
            ]),
        ]);

        var saveResponse = await client.PostAsJsonAsync("/api/xtce/save", document);
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        var xml = await saveResponse.Content.ReadAsStringAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(xml), "file", "roundtrip.xml");
        var loadResponse = await client.PostAsync("/api/xtce/load", content);

        Assert.Equal(HttpStatusCode.OK, loadResponse.StatusCode);
        var body = await loadResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Mission", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task PostSave_DocumentWithOmittedCollections_IsNormalizedNot500()
    {
        // {"name":"M"} binds Children (and nested collections) to null — the normalizer
        // must absorb that instead of letting the writer NRE into a 500.
        var client = _factory.CreateClient();
        var json = """{"name":"M","telemetryMetaData":{"parameterTypeSet":null}}""";

        var response = await client.PostAsync("/api/xtce/save",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"M\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostValidate_DocumentWithOmittedCollections_IsNormalizedNot500()
    {
        var client = _factory.CreateClient();
        var json = """{"name":"M"}""";

        var response = await client.PostAsync("/api/xtce/validate",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
