using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Xtce.Workshop.Api.Tests;

public class XtceValidateEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public XtceValidateEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostValidate_DocumentWithNoIssues_ReturnsEmptyList()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/validate", new { name = "Root", children = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("validationIssues").GetArrayLength());
    }

    [Fact]
    public async Task PostValidate_DocumentWithBadEnumInitialValue_ReturnsIssue()
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

        var response = await client.PostAsJsonAsync("/api/xtce/validate", document);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var issues = body.GetProperty("validationIssues");
        Assert.Equal(1, issues.GetArrayLength());
        Assert.Equal(
            "XTCE-1.2-R07-enum-initial-value-must-be-valid-label",
            issues[0].GetProperty("ruleId").GetString());
    }
}
