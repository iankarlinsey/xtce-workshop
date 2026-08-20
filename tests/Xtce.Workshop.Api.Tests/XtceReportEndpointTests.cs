using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xtce.Workshop.Api.Tests;

public class XtceReportEndpointTests 
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    [Test]
    public async Task PostReport_MinimalDocument_Returns109CandidateRowsAndSchemaResult()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/report", new { name = "Root", children = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("schemaValid").GetBoolean());
        Assert.Equal(109, body.GetProperty("candidates").GetArrayLength());
        Assert.Equal(23, body.GetProperty("rules").GetArrayLength());
    }

    [Test]
    public async Task PostReportText_ReturnsRenderedPlainText()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/report/text", new { name = "Sat", children = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("XTCE 1.2 conformance report: Sat", text);
        Assert.Contains("Generated: ", text);
        Assert.Contains("#109 ", text);
        Assert.Contains("Summary: ", text);
    }

    [Test]
    public async Task PostReport_BadEnumInitialValue_FailsCandidate63WithTheFinding()
    {
        var client = _factory.CreateClient();
        var document = new
        {
            name = "Root",
            children = Array.Empty<object>(),
            telemetryMetaData = new
            {
                parameterTypeSet = new object[]
                {
                    new
                    {
                        name = "State_Type",
                        kind = "Enumerated",
                        initialValue = "NOT_A_LABEL",
                        enumerations = new object[] { new { value = 0, label = "OK" } },
                    },
                },
                parameterSet = Array.Empty<object>(),
            },
        };

        var response = await client.PostAsJsonAsync("/api/xtce/report", document);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row63 = body.GetProperty("candidates").EnumerateArray()
            .Single(c => c.GetProperty("candidateNumber").GetInt32() == 63);
        Assert.Equal("Fail", row63.GetProperty("status").GetString());
        var finding = row63.GetProperty("findings")[0];
        Assert.Equal("XTCE-1.2-R07-enum-initial-value-must-be-valid-label", finding.GetProperty("ruleId").GetString());
        Assert.Equal(63, finding.GetProperty("candidateNumber").GetInt32());
    }
}
