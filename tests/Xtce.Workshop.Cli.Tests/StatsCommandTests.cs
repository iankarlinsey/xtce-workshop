using System.Text.Json;
using Xtce.Workshop.Cli;
using Xunit;

namespace Xtce.Workshop.Cli.Tests;

public class StatsCommandTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("xtce-cli-stats-tests").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteTempFile(string content)
    {
        var path = Path.Combine(_tempDir, "doc.xml");
        File.WriteAllText(path, content);
        return path;
    }

    private const string Document = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
          <TelemetryMetaData>
            <ParameterTypeSet><IntegerParameterType name="T"/></ParameterTypeSet>
            <ParameterSet>
              <Parameter name="A" parameterTypeRef="T"/>
              <Parameter name="B" parameterTypeRef="T"/>
            </ParameterSet>
          </TelemetryMetaData>
          <SpaceSystem name="Bus"/>
        </SpaceSystem>
        """;

    [Fact]
    public void Stats_PrintsPerSystemRowsAndTotals()
    {
        var output = new StringWriter();

        var exitCode = StatsCommand.Run(WriteTempFile(Document), json: false, output, new StringWriter());
        var text = output.ToString();

        Assert.Equal(StatsCommand.ExitOk, exitCode);
        Assert.Contains("Sat/Bus", text);
        Assert.Contains("2 parameter(s)", text);
        Assert.Contains("Types by kind: Integer=1", text);
    }

    [Fact]
    public void Stats_JsonShapeMatchesTheApi()
    {
        var output = new StringWriter();

        var exitCode = StatsCommand.Run(WriteTempFile(Document), json: true, output, new StringWriter());

        Assert.Equal(StatsCommand.ExitOk, exitCode);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(2, json.RootElement.GetProperty("systems").GetArrayLength());
        Assert.Equal(2, json.RootElement.GetProperty("totals").GetProperty("parameters").GetInt32());
    }

    [Fact]
    public void Stats_MissingFile_ExitsTwo()
    {
        var errorOutput = new StringWriter();

        var exitCode = StatsCommand.Run(Path.Combine(_tempDir, "nope.xml"), json: false, new StringWriter(), errorOutput);

        Assert.Equal(StatsCommand.ExitError, exitCode);
        Assert.Contains("error:", errorOutput.ToString());
    }
}
