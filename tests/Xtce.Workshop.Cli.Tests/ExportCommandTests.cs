using Xtce.Workshop.Cli;

namespace Xtce.Workshop.Cli.Tests;

public class ExportCommandTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("xtce-cli-export-tests").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteDocument()
    {
        var path = Path.Combine(_tempDir, "doc.xml");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <ParameterTypeSet>
                  <IntegerParameterType name="U8" signed="false" sizeInBits="8">
                    <IntegerDataEncoding sizeInBits="8"/>
                  </IntegerParameterType>
                </ParameterTypeSet>
                <ParameterSet><Parameter name="Batt" parameterTypeRef="U8"/></ParameterSet>
                <ContainerSet><SequenceContainer name="Frame"><EntryList>
                  <ParameterRefEntry parameterRef="Batt"/>
                </EntryList></SequenceContainer></ContainerSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);
        return path;
    }

    [Test]
    public void ExportParameters_ToStdout()
    {
        var output = new StringWriter();

        var exitCode = ExportCommand.Run(WriteDocument(), "--parameters", null, output, new StringWriter());

        Assert.Equal(ExportCommand.ExitOk, exitCode);
        Assert.Contains("SystemPath,Name,ParameterTypeRef", output.ToString());
        Assert.Contains("Sat,Batt,U8,Integer,8", output.ToString());
    }

    [Test]
    public void ExportContainers_ToFile()
    {
        var outPath = Path.Combine(_tempDir, "containers.csv");

        var exitCode = ExportCommand.Run(WriteDocument(), "--containers", outPath, new StringWriter(), new StringWriter());

        Assert.Equal(ExportCommand.ExitOk, exitCode);
        var csv = File.ReadAllText(outPath);
        Assert.Contains("Sat,Frame,Batt,parameter,Frame,0,8,", csv);
    }

    [Test]
    public void MissingSelector_ExitsTwo()
    {
        var errorOutput = new StringWriter();

        var exitCode = ExportCommand.Run(WriteDocument(), "", null, new StringWriter(), errorOutput);

        Assert.Equal(ExportCommand.ExitError, exitCode);
        Assert.Contains("--parameters or --containers", errorOutput.ToString());
    }
}
