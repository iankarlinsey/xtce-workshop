using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xtce.Workshop.Api.Tests;

public class LoadJobEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    private static async Task<JsonElement> PollUntilTerminal(HttpClient client, string jobId, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        for (;;)
        {
            var snapshot = await client.GetFromJsonAsync<JsonElement>($"/api/xtce/jobs/{jobId}");
            var state = snapshot.GetProperty("state").GetString();
            if (state is "done" or "failed" or "cancelled")
            {
                return snapshot;
            }
            Assert.True(DateTime.UtcNow < deadline, $"job did not finish within {limit.TotalSeconds}s (state {state})");
            await Task.Delay(50);
        }
    }

    [Test]
    public async Task JobFlow_ProducesTheSameResultShapeAsTheSynchronousLoad()
    {
        var client = _factory.CreateClient();
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="Good" parameterTypeRef="T"/>
                  <Parameter name="Dangling" parameterTypeRef="Missing"/>
                </ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """;

        var start = await client.PostAsJsonAsync("/api/xtce/jobs/text", new { xml });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var jobId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;

        var final = await PollUntilTerminal(client, jobId, TimeSpan.FromSeconds(15));
        Assert.Equal("done", final.GetProperty("state").GetString());

        var result = await client.GetAsync($"/api/xtce/jobs/{jobId}/result");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sat", body.GetProperty("name").GetString());
        Assert.Equal("1.2", body.GetProperty("detectedVersion").GetString());
        Assert.True(body.GetProperty("validationIssues").GetArrayLength() > 0);
        // #90 items 1+2: findings carry their own line/column; the response ships
        // neither the per-element positions map nor the redundant tree.
        var danglingIssue = body.GetProperty("validationIssues").EnumerateArray()
            .First(i => i.GetProperty("location").GetString()!.EndsWith("Dangling"));
        Assert.True(danglingIssue.GetProperty("line").GetInt32() > 0);
        Assert.False(body.TryGetProperty("positions", out _));
        Assert.False(body.TryGetProperty("tree", out _));

        // The result stays re-fetchable — a browser that could not hold it comes back
        // for the session form; converting to a session is what evicts the job.
        var again = await client.GetAsync($"/api/xtce/jobs/{jobId}/result");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var asSession = await client.GetAsync($"/api/xtce/jobs/{jobId}/result?as=session");
        Assert.Equal(HttpStatusCode.OK, asSession.StatusCode);
        var sessionBody = await asSession.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(sessionBody.GetProperty("largeDocument").GetBoolean());
        Assert.False(sessionBody.TryGetProperty("document", out _));
        var sessionId = sessionBody.GetProperty("documentSessionId").GetString()!;
        // Findings keep their server-resolved positions in the session form too.
        var sessionIssue = sessionBody.GetProperty("validationIssues").EnumerateArray()
            .First(i => i.GetProperty("location").GetString()!.EndsWith("Dangling"));
        Assert.True(sessionIssue.GetProperty("line").GetInt32() > 0);

        // The session is live and the job copy is gone.
        var node = await client.GetAsync($"/api/xtce/sessions/{sessionId}/node");
        Assert.Equal(HttpStatusCode.OK, node.StatusCode);
        var gone = await client.GetAsync($"/api/xtce/jobs/{jobId}/result");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Test]
    public async Task JobFlow_MalformedDocument_ResultCarriesTheStandard400Shape()
    {
        var client = _factory.CreateClient();
        var start = await client.PostAsJsonAsync("/api/xtce/jobs/text", new { xml = "<SpaceSystem name='X'>\n<Unclosed>" });
        var jobId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;
        var final = await PollUntilTerminal(client, jobId, TimeSpan.FromSeconds(15));
        Assert.Equal("done", final.GetProperty("state").GetString());

        var result = await client.GetAsync($"/api/xtce/jobs/{jobId}/result");

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var body = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MalformedXml",
            body.GetProperty("diagnostics").EnumerateArray().First().GetProperty("kind").GetString());
    }

    [Test]
    public async Task JobFlow_CancelStopsTheServerSidePipeline()
    {
        var client = _factory.CreateClient();
        // Big enough to still be parsing when the cancel lands.
        var bulk = new StringBuilder();
        bulk.Append("""<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Big"><TelemetryMetaData><ParameterTypeSet>""");
        for (var i = 0; i < 120000; i++)
        {
            bulk.Append($"<IntegerParameterType name=\"T{i}\"><UnitSet/><IntegerDataEncoding sizeInBits=\"16\"/></IntegerParameterType>");
        }
        bulk.Append("</ParameterTypeSet></TelemetryMetaData></SpaceSystem>");

        var start = await client.PostAsJsonAsync("/api/xtce/jobs/text", new { xml = bulk.ToString() });
        var jobId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString()!;

        var cancel = await client.DeleteAsync($"/api/xtce/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        var final = await PollUntilTerminal(client, jobId, TimeSpan.FromSeconds(15));
        Assert.Equal("cancelled", final.GetProperty("state").GetString());

        var result = await client.GetAsync($"/api/xtce/jobs/{jobId}/result");
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
    }

    [Test]
    public async Task JobFlow_UnknownJob_Is404Everywhere()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/xtce/jobs/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/xtce/jobs/nope/result")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/xtce/jobs/nope")).StatusCode);
    }
}
