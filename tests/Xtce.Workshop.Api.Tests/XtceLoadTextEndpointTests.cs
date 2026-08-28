using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xtce.Workshop.Api.Tests;

public class XtceLoadTextEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    [Test]
    public async Task PostLoadText_ValidDocument_Returns200WithSameShapeAsFileLoad()
    {
        var client = _factory.CreateClient();
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet><Parameter name="P" parameterTypeRef="T"/></ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """;

        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sat", body.GetProperty("name").GetString());
        Assert.False(body.TryGetProperty("tree", out _)); // #90 item 1
        Assert.Equal("Sat", body.GetProperty("document").GetProperty("name").GetString());
        Assert.Equal(0, body.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal(0, body.GetProperty("validationIssues").GetArrayLength());
    }

    [Test]
    public async Task PostLoadText_MalformedXml_Returns400WithPositionedDiagnostics()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/load-text",
            new { xml = "<SpaceSystem name='X'>\n  <Unclosed>" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var diagnostic = Assert.Single(body.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("MalformedXml", diagnostic.GetProperty("kind").GetString());
        Assert.True(diagnostic.GetProperty("line").GetInt32() > 0);
        Assert.True(body.GetProperty("schemaErrors").GetArrayLength() > 0);
    }

    [Test]
    public async Task PostLoadText_BrokenModelElement_Returns200WithQuarantineAndDiagnostics()
    {
        var client = _factory.CreateClient();
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="Good" parameterTypeRef="T"/>
                  <Parameter name="NoTypeRef"/>
                </ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """;

        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var diagnostic = Assert.Single(body.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("ModelError", diagnostic.GetProperty("kind").GetString());
        Assert.Contains("Parameter[NoTypeRef]", diagnostic.GetProperty("path").GetString());
        Assert.Equal(1, body.GetProperty("document").GetProperty("telemetryMetaData").GetProperty("parameterSet").GetArrayLength());
    }

    [Test]
    public async Task PostLoadText_MissingOrEmptyXml_Returns400WithMessage()
    {
        var client = _factory.CreateClient();

        var emptyResponse = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml = "" });
        var absentResponse = await client.PostAsJsonAsync("/api/xtce/load-text", new { });

        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, absentResponse.StatusCode);
        var body = await emptyResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("xml", body.GetProperty("error").GetString()!);
    }

    [Test]
    public async Task PostLoadText_RoundTrip_SaveOutputReloadsIdentically()
    {
        var client = _factory.CreateClient();
        var repoRoot = FindRepoRoot();
        var original = await File.ReadAllTextAsync(Path.Combine(repoRoot, "samples", "preservation-1.2.xml"));

        // load-text -> document -> save -> load-text again: same document, no diagnostics.
        var first = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml = original });
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var saveResponse = await client.PostAsJsonAsync("/api/xtce/save",
            firstBody.GetProperty("document"));
        var savedXml = await saveResponse.Content.ReadAsStringAsync();
        var second = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml = savedXml });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, secondBody.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal(
            firstBody.GetProperty("document").GetRawText(),
            secondBody.GetProperty("document").GetRawText());
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
