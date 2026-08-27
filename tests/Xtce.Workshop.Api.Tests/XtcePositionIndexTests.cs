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
    public async Task Load_ReturnsPositionsKeyedByValidatorLocationGrammar()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/xtce/load-text", new { xml = Document });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var positions = body.GetProperty("positions");

        // Every location the validator can cite resolves to the exact source line.
        Assert.Equal(2, positions.GetProperty("Sat").GetProperty("line").GetInt32());
        Assert.Equal(5, positions.GetProperty("Sat/ParameterTypeSet/T").GetProperty("line").GetInt32());
        Assert.Equal(8, positions.GetProperty("Sat/ParameterSet/P").GetProperty("line").GetInt32());
        Assert.Equal(11, positions.GetProperty("Sat/ContainerSet/C").GetProperty("line").GetInt32());
        Assert.Equal(16, positions.GetProperty("Sat/MessageSet/M").GetProperty("line").GetInt32());
        Assert.Equal(23, positions.GetProperty("Sat/CommandMetaData/MetaCommandSet/Cmd").GetProperty("line").GetInt32());
        Assert.Equal(24, positions.GetProperty("Sat/CommandMetaData/MetaCommandSet/Cmd/CommandContainer").GetProperty("line").GetInt32());
        Assert.Equal(28, positions.GetProperty("Sat/Bus").GetProperty("line").GetInt32());
        Assert.True(positions.GetProperty("Sat/ParameterSet/P").GetProperty("column").GetInt32() > 0);
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
