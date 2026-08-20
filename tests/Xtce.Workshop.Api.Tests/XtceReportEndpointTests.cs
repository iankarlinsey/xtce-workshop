using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Xtce.Workshop.Api.Tests;

public class XtceReportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public XtceReportEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
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

    [Fact]
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
