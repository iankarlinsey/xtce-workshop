using System.Text.Json;
using Xtce.Workshop.Cli;

namespace Xtce.Workshop.Cli.Tests;

public class BuildInfoTests
{
    [Test]
    public void Version_IsNeverEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(Xtce.Workshop.Cli.BuildInfo.Version));
    }
}

public class ValidateCommandTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("xtce-cli-tests").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteTempFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string CleanDocument = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Clean">
          <TelemetryMetaData>
            <ParameterTypeSet>
              <IntegerParameterType name="Count_Type" signed="false" sizeInBits="8"/>
            </ParameterTypeSet>
            <ParameterSet>
              <Parameter name="Count" parameterTypeRef="Count_Type" initialValue="42"/>
            </ParameterSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    private const string DocumentWithFindings = """
        <?xml version="1.0" encoding="UTF-8"?>
        <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Broken">
          <TelemetryMetaData>
            <ParameterSet>
              <Parameter name="Ghost" parameterTypeRef="NoSuchType"/>
            </ParameterSet>
          </TelemetryMetaData>
        </SpaceSystem>
        """;

    [Test]
    public void CleanFile_PrintsNoFindingsAndExitsZero()
    {
        var path = WriteTempFile("clean.xml", CleanDocument);
        var output = new StringWriter();

        var exitCode = ValidateCommand.Run(path, json: false, output, new StringWriter());

        Assert.Equal(ValidateCommand.ExitValid, exitCode);
        Assert.Contains("no findings", output.ToString());
    }

    [Test]
    public void FileWithFindings_PrintsThemAndExitsOne()
    {
        var path = WriteTempFile("broken.xml", DocumentWithFindings);
        var output = new StringWriter();

        var exitCode = ValidateCommand.Run(path, json: false, output, new StringWriter());

        Assert.Equal(ValidateCommand.ExitFindings, exitCode);
        var text = output.ToString();
        Assert.Contains("error XTCE-1.2-R11-no-dangling-name-references @ Broken/ParameterSet/Ghost", text);
        Assert.Contains("NoSuchType", text);
        Assert.Contains("1 finding(s).", text);
    }

    [Test]
    public void UnloadableModel_PrintsAllDiagnosticsAndSchemaErrors()
    {
        var path = WriteTempFile("broken-model.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204">
              <TelemetryMetaData>
                <ParameterSet><Parameter name="NoTypeRef"/></ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);
        var errorOutput = new StringWriter();

        var exitCode = ValidateCommand.Run(path, json: false, new StringWriter(), errorOutput);
        var text = errorOutput.ToString();

        Assert.Equal(ValidateCommand.ExitError, exitCode);
        Assert.Contains("error:", text);
        Assert.Contains("model", text);        // positioned reader diagnostics
        Assert.Contains("schema:", text);      // full XSD error list for the raw input
    }

    [Test]
    public void MalformedXml_WritesErrorToStderrAndExitsTwo()
    {
        var path = WriteTempFile("malformed.xml", "<SpaceSystem name=\"Oops\"");
        var errorOutput = new StringWriter();

        var exitCode = ValidateCommand.Run(path, json: false, new StringWriter(), errorOutput);

        Assert.Equal(ValidateCommand.ExitError, exitCode);
        Assert.Contains("error:", errorOutput.ToString());
    }

    [Test]
    public void MissingFile_WritesErrorToStderrAndExitsTwo()
    {
        var errorOutput = new StringWriter();

        var exitCode = ValidateCommand.Run(
            Path.Combine(_tempDir, "does-not-exist.xml"), json: false, new StringWriter(), errorOutput);

        Assert.Equal(ValidateCommand.ExitError, exitCode);
        Assert.Contains("error:", errorOutput.ToString());
    }

    [Test]
    public void JsonFlag_ProducesParseableOutputMatchingTheApiShape()
    {
        var path = WriteTempFile("broken.xml", DocumentWithFindings);
        var output = new StringWriter();

        var exitCode = ValidateCommand.Run(path, json: true, output, new StringWriter());

        Assert.Equal(ValidateCommand.ExitFindings, exitCode);
        var parsed = JsonDocument.Parse(output.ToString());
        var issues = parsed.RootElement.GetProperty("validationIssues");
        Assert.Equal(1, issues.GetArrayLength());
        Assert.Equal("XTCE-1.2-R11-no-dangling-name-references", issues[0].GetProperty("ruleId").GetString());
        Assert.Equal("Error", issues[0].GetProperty("severity").GetString());
    }

    [Test]
    public void JsonFlag_CleanFileStillExitsZeroWithEmptyList()
    {
        var path = WriteTempFile("clean.xml", CleanDocument);
        var output = new StringWriter();

        var exitCode = ValidateCommand.Run(path, json: true, output, new StringWriter());

        Assert.Equal(ValidateCommand.ExitValid, exitCode);
        var parsed = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, parsed.RootElement.GetProperty("validationIssues").GetArrayLength());
    }
}
