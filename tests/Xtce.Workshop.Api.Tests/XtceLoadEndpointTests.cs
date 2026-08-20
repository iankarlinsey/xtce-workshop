using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Api.Tests;

public class XtceLoadEndpointTests 
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
    public async Task PostLoad_ValidFile_ReturnsEmptyValidationIssues()
    {
        var client = _factory.CreateClient();
        var repoRoot = FindRepoRoot();
        var samplePath = Path.Combine(repoRoot, "samples", "telemetry-1.2.xml");

        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(samplePath);
        content.Add(new StreamContent(fileStream), "file", "telemetry-1.2.xml");

        var response = await client.PostAsync("/api/xtce/load", content);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("validationIssues").GetArrayLength());
    }

    [Test]
    public async Task PostLoad_FileWithIssues_ReturnsValidationIssues()
    {
        var client = _factory.CreateClient();
        var repoRoot = FindRepoRoot();
        var samplePath = Path.Combine(repoRoot, "samples", "telemetry-with-issues-1.2.xml");

        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(samplePath);
        content.Add(new StreamContent(fileStream), "file", "telemetry-with-issues-1.2.xml");

        var response = await client.PostAsync("/api/xtce/load", content);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var issues = body.GetProperty("validationIssues");
        Assert.Equal(2, issues.GetArrayLength());

        var ruleIds = issues.EnumerateArray().Select(i => i.GetProperty("ruleId").GetString()).ToList();
        Assert.Contains("XTCE-1.2-R07-enum-initial-value-must-be-valid-label", ruleIds);
        Assert.Contains("XTCE-1.2-R15-typed-value-valid-for-type", ruleIds);

        var severity = issues[0].GetProperty("severity").GetString();
        Assert.Equal("Error", severity);
    }

    [Test]
    public async Task PostLoad_BrokenModelElements_LoadsPartiallyWithDiagnosticsAndSchemaErrors()
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

        var response = await PostFile(client, xml);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var diagnostic = Assert.Single(body.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("ModelError", diagnostic.GetProperty("kind").GetString());
        Assert.Contains("Parameter[NoTypeRef]", diagnostic.GetProperty("path").GetString());
        Assert.True(diagnostic.GetProperty("line").GetInt32() > 0);
        Assert.True(body.GetProperty("schemaErrors").GetArrayLength() > 0);
        // The good parameter loaded; the broken one is quarantined, not dropped.
        Assert.Equal(1, body.GetProperty("document").GetProperty("telemetryMetaData").GetProperty("parameterSet").GetArrayLength());
    }

    [Test]
    public async Task PostLoad_MalformedXml_Returns400WithPositionedDiagnostics()
    {
        var client = _factory.CreateClient();

        var response = await PostFile(client, "<SpaceSystem name='X'><Unclosed>");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var diagnostic = Assert.Single(body.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("MalformedXml", diagnostic.GetProperty("kind").GetString());
        Assert.True(diagnostic.GetProperty("line").GetInt32() > 0);
    }

    private static async Task<HttpResponseMessage> PostFile(HttpClient client, string xml)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(xml)), "file", "upload.xml" },
        };
        return await client.PostAsync("/api/xtce/load", content);
    }

    [Test]
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
