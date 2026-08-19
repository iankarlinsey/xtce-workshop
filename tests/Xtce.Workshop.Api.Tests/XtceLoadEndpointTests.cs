using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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
    public async Task PostLoad_MinimalValidFile_Returns200WithName()
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
