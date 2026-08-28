using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xtce.Workshop.Api.Tests;

public class XtcePositionIndexTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [OneTimeSetUp]
    public void CreateFactory() => _factory = new WebApplicationFactory<Program>();

    [OneTimeTearDown]
    public void DisposeFactory() => _factory.Dispose();

    private const string Document = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="T"/>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="P" parameterTypeRef="T"/>
            </ParameterSet>
            <ContainerSet>
              <SequenceContainer name="C">
                <EntryList/>
              </SequenceContainer>
            </ContainerSet>
            <MessageSet>
              <Message name="M">
                <ContainerRef containerRef="C"/>
              </Message>
            </MessageSet>
          </TelemetryMetaData>
          <CommandMetaData>
            <MetaCommandSet>
              <MetaCommand name="Cmd">
                <CommandContainer name="CmdC"/>
              </MetaCommand>
            </MetaCommandSet>
          </CommandMetaData>
          <SpaceSystem name="Bus"/>
        </SpaceSystem>
        """;

    [Test]
    public async Task Load_ResolvesIssuePositionsServerSide_WithAncestorFallback()
    {
        var client = _factory.CreateClient();
        // Line numbers matter: the dangling Parameter sits on line 6.
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="Dangling" parameterTypeRef="Missing"/>
                </ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """;

        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // #90 item 2: the per-element positions map is gone; every finding carries the
        // line/column of its cited location (or of the longest recorded ancestor).
        Assert.False(body.TryGetProperty("positions", out _));
        var issue = body.GetProperty("validationIssues").EnumerateArray()
            .First(i => i.GetProperty("location").GetString()!.EndsWith("Dangling"));
        Assert.Equal(6, issue.GetProperty("line").GetInt32());
        Assert.True(issue.GetProperty("column").GetInt32() > 0);
    }

    [Test]
    public async Task Load_SchemaErrorsAreStructuredWithPositions()
    {
        var client = _factory.CreateClient();

        // Message without its XSD-required MatchCriteria: schema-invalid, model-loadable.
        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml = Document });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var schemaErrors = body.GetProperty("schemaErrors").EnumerateArray().ToList();

        Assert.True(schemaErrors.Count > 0);
        var first = schemaErrors[0];
        Assert.False(string.IsNullOrEmpty(first.GetProperty("message").GetString()));
        Assert.True(first.GetProperty("line").GetInt32() > 0);
    }

    [Test]
    public async Task Load_SchemaValidationRunsEvenWhenTheModelLoadsCleanly()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml = Document });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Zero load diagnostics, yet the schema verdict is still present and non-empty.
        Assert.Equal(0, body.GetProperty("diagnostics").GetArrayLength());
        Assert.True(body.GetProperty("schemaErrors").GetArrayLength() > 0);
    }
}
