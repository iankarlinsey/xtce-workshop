using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xtce.Workshop.Model;
using Xunit;

namespace Xtce.Workshop.Api.Tests;

public class XtceLoadEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public XtceLoadEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostLoad_MinimalValidFile_Returns200WithNameAndTree()
    {
        var client = _factory.CreateClient();
        var repoRoot = FindRepoRoot();
        var samplePath = Path.Combine(repoRoot, "samples", "minimal-1.2.xml");

        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(samplePath);
        content.Add(new StreamContent(fileStream), "file", "minimal-1.2.xml");

        var response = await client.PostAsync("/api/xtce/load", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Minimal", body.GetProperty("name").GetString());

        var tree = body.GetProperty("tree");
        Assert.Equal("Minimal", tree.GetProperty("label").GetString());
        Assert.Equal("SpaceSystem", tree.GetProperty("nodeType").GetString());
        Assert.Equal(0, tree.GetProperty("children").GetArrayLength());

        var document = body.GetProperty("document");
        Assert.Equal("Minimal", document.GetProperty("name").GetString());
        Assert.Equal(0, document.GetProperty("children").GetArrayLength());
    }

    [Fact]
    public async Task PostLoad_Document_CanBeFedDirectlyToSaveAndRoundTrips()
    {
        var client = _factory.CreateClient();
        var repoRoot = FindRepoRoot();
        var samplePath = Path.Combine(repoRoot, "samples", "nested-1.2.xml");

        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(samplePath);
        content.Add(new StreamContent(fileStream), "file", "nested-1.2.xml");

        var loadResponse = await client.PostAsync("/api/xtce/load", content);
        var loadBody = await loadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var document = loadBody.GetProperty("document").Deserialize<SpaceSystem>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var saveResponse = await client.PostAsJsonAsync("/api/xtce/save", document);

        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        var xml = await saveResponse.Content.ReadAsStringAsync();
        Assert.Contains("Mission", xml);
        Assert.Contains("Bus", xml);
        Assert.Contains("Payload", xml);
    }

    [Fact]
    public async Task PostLoad_NestedValidFile_ReturnsFullTreeStructure()
    {
        var client = _factory.CreateClient();
        var repoRoot = FindRepoRoot();
        var samplePath = Path.Combine(repoRoot, "samples", "nested-1.2.xml");

        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(samplePath);
        content.Add(new StreamContent(fileStream), "file", "nested-1.2.xml");

        var response = await client.PostAsync("/api/xtce/load", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var tree = body.GetProperty("tree");
        Assert.Equal("Mission", tree.GetProperty("label").GetString());
        var children = tree.GetProperty("children");
        Assert.Equal(2, children.GetArrayLength());
        Assert.Equal("Bus", children[0].GetProperty("label").GetString());
        Assert.Equal(2, children[0].GetProperty("children").GetArrayLength());
        Assert.Equal("Payload", children[1].GetProperty("label").GetString());
    }

    [Fact]
    public async Task PostLoad_MalformedFile_Returns400()
    {
        var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var malformedBytes = Encoding.UTF8.GetBytes("<SpaceSystem name=\"Broken\"");
        content.Add(new ByteArrayContent(malformedBytes), "file", "broken.xml");

        var response = await client.PostAsync("/api/xtce/load", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (global.json) from " + AppContext.BaseDirectory);
    }
}
