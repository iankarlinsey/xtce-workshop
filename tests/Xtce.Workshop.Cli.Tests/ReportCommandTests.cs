using System.Text.Json;
using Xtce.Workshop.Cli;

namespace Xtce.Workshop.Cli.Tests;

public class ReportCommandTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("xtce-cli-report-tests").FullName;

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
    public void CleanFile_Prints109RowsWithNoFail_AndExitsZero()
    {
        var path = WriteTempFile("clean.xml", CleanDocument);
        var output = new StringWriter();

        var exitCode = ReportCommand.Run(path, json: false, output, new StringWriter());
        var text = output.ToString();

        Assert.Equal(ReportCommand.ExitClean, exitCode);
        Assert.Contains("Schema validation: VALID", text);
        for (var candidate = 1; candidate <= 109; candidate++)
        {
            Assert.Contains($"#{candidate} ", text);
        }
        Assert.DoesNotContain(" FAIL ", text);
        Assert.DoesNotContain("SCHEMA_FAIL", text);
    }

    [Test]
    public void FileWithDanglingRef_FailsCandidate91_AndExitsOne()
    {
        var path = WriteTempFile("broken.xml", DocumentWithFindings);
        var output = new StringWriter();

        var exitCode = ReportCommand.Run(path, json: true, output, new StringWriter());

        Assert.Equal(ReportCommand.ExitFindings, exitCode);
        using var report = JsonDocument.Parse(output.ToString());
        var candidates = report.RootElement.GetProperty("candidates");
        Assert.Equal(109, candidates.GetArrayLength());
        var row91 = candidates.EnumerateArray().Single(c => c.GetProperty("candidateNumber").GetInt32() == 91);
        Assert.Equal("Fail", row91.GetProperty("status").GetString());
        Assert.Equal(91, row91.GetProperty("findings")[0].GetProperty("candidateNumber").GetInt32());
    }

    [Test]
    public void OutFlag_WritesTheReportToDisk()
    {
        var path = WriteTempFile("clean.xml", CleanDocument);
        var outPath = Path.Combine(_tempDir, "report.txt");
        var output = new StringWriter();

        var exitCode = ReportCommand.Run(path, json: false, output, new StringWriter(), outPath);

        Assert.Equal(ReportCommand.ExitClean, exitCode);
        Assert.Contains($"wrote {outPath}", output.ToString());
        var text = File.ReadAllText(outPath);
        Assert.StartsWith("XTCE 1.2 conformance report: clean.xml", text);
        Assert.Contains("Generated: ", text);
        Assert.Contains("#109 ", text);
    }

    [Test]
    public void OutFlagWithJson_WritesJsonToDisk()
    {
        var path = WriteTempFile("clean.xml", CleanDocument);
        var outPath = Path.Combine(_tempDir, "report.json");

        var exitCode = ReportCommand.Run(path, json: true, new StringWriter(), new StringWriter(), outPath);

        Assert.Equal(ReportCommand.ExitClean, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.Equal(109, json.RootElement.GetProperty("candidates").GetArrayLength());
    }

    [Test]
    public void MissingFile_WritesErrorToStderrAndExitsTwo()
    {
        var errorOutput = new StringWriter();

        var exitCode = ReportCommand.Run(Path.Combine(_tempDir, "nope.xml"), json: false, new StringWriter(), errorOutput);

        Assert.Equal(ReportCommand.ExitError, exitCode);
        Assert.Contains("error:", errorOutput.ToString());
    }
}
