using System.Text.Json;
using Xtce.Workshop.Cli;
using Xunit;

namespace Xtce.Workshop.Cli.Tests;

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

    [Fact]
    public void CleanFile_PrintsNoFindingsAndExitsZero()
    {
        var path = WriteTempFile("clean.xml", CleanDocument);
        var output = new StringWriter();

        var exitCode = ValidateCommand.Run(path, json: false, output, new StringWriter());

        Assert.Equal(ValidateCommand.ExitValid, exitCode);
        Assert.Contains("no findings", output.ToString());
    }

    [Fact]
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

    [Fact]
    public void MalformedXml_WritesErrorToStderrAndExitsTwo()
    {
        var path = WriteTempFile("malformed.xml", "<SpaceSystem name=\"Oops\"");
        var errorOutput = new StringWriter();

        var exitCode = ValidateCommand.Run(path, json: false, new StringWriter(), errorOutput);

        Assert.Equal(ValidateCommand.ExitError, exitCode);
        Assert.Contains("error:", errorOutput.ToString());
    }

    [Fact]
    public void MissingFile_WritesErrorToStderrAndExitsTwo()
    {
        var errorOutput = new StringWriter();

        var exitCode = ValidateCommand.Run(
            Path.Combine(_tempDir, "does-not-exist.xml"), json: false, new StringWriter(), errorOutput);

        Assert.Equal(ValidateCommand.ExitError, exitCode);
        Assert.Contains("error:", errorOutput.ToString());
    }

    [Fact]
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

    [Fact]
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
