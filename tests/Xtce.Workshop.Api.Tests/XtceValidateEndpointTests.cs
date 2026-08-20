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

    [Fact]
    public async Task PostLayout_ComputesOffsetsForAKnownContainer()
    {
        var client = _factory.CreateClient();
        var request = new
        {
            containerName = "Frame",
            systemPath = Array.Empty<int>(),
            document = new
            {
                name = "S",
                children = Array.Empty<object>(),
                telemetryMetaData = new
                {
                    parameterTypeSet = new object[]
                    {
                        new
                        {
                            name = "U16",
                            kind = "Integer",
                            preserved = new object[]
                            {
                                new { elementName = "IntegerDataEncoding", outerXml = """<IntegerDataEncoding sizeInBits="16" xmlns="http://www.omg.org/spec/XTCE/20180204"/>""" },
                            },
                        },
                    },
                    parameterSet = new object[] { new { name = "P", parameterTypeRef = "U16" } },
                    containerSet = new object[]
                    {
                        new { name = "Frame", entryList = new object[] { new { kind = "ParameterRef", @ref = "P" } } },
                    },
                },
            },
        };

        var response = await client.PostAsJsonAsync("/api/xtce/layout", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(16, body.GetProperty("totalSizeInBits").GetInt64());
        Assert.Equal("P", body.GetProperty("rows")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task PostLayout_UnknownContainer_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/layout", new
        {
            containerName = "Nowhere",
            document = new { name = "S", children = Array.Empty<object>() },
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
