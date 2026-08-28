using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Xtce.Workshop.Api.Tests;

/// <summary>
/// Server-held document sessions (#129): above the size threshold, loads answer with a
/// documentSessionId instead of document JSON, and the browser works item by item.
/// The threshold is forced to 1 byte here so ordinary fixtures exercise large mode.
/// </summary>
public class DocumentSessionEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Xtce:LargeDocumentThresholdBytes"] = "1",
            })));

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    private const string Document = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="Volt_Type" signed="false" sizeInBits="16"><UnitSet/></IntegerParameterType>
              <IntegerParameterType name="Mode_Type" signed="false" sizeInBits="8"><UnitSet/></IntegerParameterType>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="BusVoltage" parameterTypeRef="Volt_Type"/>
              <Parameter name="Mode" parameterTypeRef="Mode_Type"/>
            </ParameterSet>
          </TelemetryMetaData>
          <SpaceSystem name="Bus">
            <TelemetryMetaData>
              <ParameterTypeSet><IntegerParameterType name="Temp_Type" signed="false" sizeInBits="8"><UnitSet/></IntegerParameterType></ParameterTypeSet>
              <ParameterSet><Parameter name="Temp" parameterTypeRef="Temp_Type"/></ParameterSet>
            </TelemetryMetaData>
          </SpaceSystem>
        </SpaceSystem>
        """;

    private async Task<(HttpClient Client, string SessionId)> OpenSession()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml = Document });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("largeDocument").GetBoolean());
        Assert.False(body.TryGetProperty("document", out _)); // the whole point
        Assert.True(body.GetProperty("validationIssues").ValueKind == JsonValueKind.Array);
        return (client, body.GetProperty("documentSessionId").GetString()!);
    }

    [Test]
    public async Task LargeMode_ReturnsSessionInsteadOfDocument()
    {
        var (_, sessionId) = await OpenSession();
        Assert.False(string.IsNullOrEmpty(sessionId));
    }

    [Test]
    public async Task Node_SummarisesGroupsAndChildSystems()
    {
        var (client, sessionId) = await OpenSession();

        var root = await client.GetFromJsonAsync<JsonElement>($"/api/xtce/sessions/{sessionId}/node");
        Assert.Equal("Sat", root.GetProperty("name").GetString());
        Assert.Equal("Bus", root.GetProperty("childSystems")[0].GetString());
        Assert.Equal(2, root.GetProperty("groups").GetProperty("parameterType").GetInt32());
        Assert.Equal(2, root.GetProperty("groups").GetProperty("parameter").GetInt32());
        Assert.Equal(0, root.GetProperty("groups").GetProperty("metaCommand").GetInt32());

        var child = await client.GetFromJsonAsync<JsonElement>($"/api/xtce/sessions/{sessionId}/node?path=0");
        Assert.Equal("Bus", child.GetProperty("name").GetString());
        Assert.Equal(1, child.GetProperty("groups").GetProperty("parameter").GetInt32());
    }

    [Test]
    public async Task Items_PageThroughNames()
    {
        var (client, sessionId) = await OpenSession();

        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/xtce/sessions/{sessionId}/items?kind=parameterType&offset=1&limit=1");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("offset").GetInt32());
        Assert.Equal("Mode_Type", Assert.Single(page.GetProperty("names").EnumerateArray()).GetString());
    }

    [Test]
    public async Task Item_RoundTripsThroughPut_AndSaveReflectsTheEdit()
    {
        var (client, sessionId) = await OpenSession();

        var item = await client.GetFromJsonAsync<JsonElement>(
            $"/api/xtce/sessions/{sessionId}/item?path=0&kind=parameter&index=0");
        Assert.Equal("Temp", item.GetProperty("name").GetString());

        // Point the child-system parameter at a dangling type: the edit must land in the
        // held model, show up in validate, and serialize on save.
        var replacement = JsonSerializer.Deserialize<Dictionary<string, object?>>(item.GetRawText())!;
        replacement["parameterTypeRef"] = "NoSuchType";
        var put = await client.PutAsJsonAsync(
            $"/api/xtce/sessions/{sessionId}/item?path=0&kind=parameter&index=0", replacement);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var reread = await client.GetFromJsonAsync<JsonElement>(
            $"/api/xtce/sessions/{sessionId}/item?path=0&kind=parameter&index=0");
        Assert.Equal("NoSuchType", reread.GetProperty("parameterTypeRef").GetString());

        var validate = await client.PostAsync($"/api/xtce/sessions/{sessionId}/validate", null);
        var issues = (await validate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("validationIssues").EnumerateArray().ToList();
        Assert.Contains(issues, i => i.GetProperty("message").GetString()!.Contains("NoSuchType"));

        var save = await client.GetAsync($"/api/xtce/sessions/{sessionId}/save");
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var xml = await save.Content.ReadAsStringAsync();
        Assert.Contains("NoSuchType", xml);
        Assert.Contains("Volt_Type", xml); // the untouched parts survive
    }

    [Test]
    public async Task Search_FindsItemsInTheHeldModel()
    {
        var (client, sessionId) = await OpenSession();

        var result = await client.GetFromJsonAsync<JsonElement>(
            $"/api/xtce/sessions/{sessionId}/search?query=Temp*");
        var matches = result.GetProperty("matches").EnumerateArray().ToList();
        Assert.Contains(matches, m => m.GetProperty("name").GetString() == "Temp");
        Assert.Contains(matches, m => m.GetProperty("name").GetString() == "Temp_Type");
    }

    [Test]
    public async Task UnknownSessionsPathsAndKinds_AnswerHonestly()
    {
        var (client, sessionId) = await OpenSession();

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/xtce/sessions/nope/node")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/xtce/sessions/{sessionId}/node?path=9")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/xtce/sessions/{sessionId}/items?kind=mystery")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/xtce/sessions/{sessionId}/item?kind=parameter&index=99")).StatusCode);
    }

    [Test]
    public async Task Delete_DropsTheSession()
    {
        var (client, sessionId) = await OpenSession();

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/xtce/sessions/{sessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/xtce/sessions/{sessionId}/node")).StatusCode);
    }
}
