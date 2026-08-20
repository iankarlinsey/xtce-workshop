using Xtce.Workshop.Cli;

namespace Xtce.Workshop.Cli.Tests;

public class FindCommandTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("xtce-cli-find-tests").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteDocument()
    {
        var path = Path.Combine(_tempDir, "doc.xml");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="UTF-8"?>
            <SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="Sat">
              <TelemetryMetaData>
                <ParameterTypeSet><IntegerParameterType name="Volt_Type"/></ParameterTypeSet>
                <ParameterSet>
                  <Parameter name="BattVoltage" parameterTypeRef="Volt_Type">
                    <AliasSet><Alias nameSpace="ops" alias="EPS_V_BATT"/></AliasSet>
                  </Parameter>
                </ParameterSet>
              </TelemetryMetaData>
            </SpaceSystem>
            """);
        return path;
    }

    [Test]
    public void Find_PrintsMatchesIncludingAliasHits_AndExitsZero()
    {
        var output = new StringWriter();

        var exitCode = FindCommand.Run(WriteDocument(), "EPS_V*", json: false, output, new StringWriter());
        var text = output.ToString();

        Assert.Equal(FindCommand.ExitFound, exitCode);
        Assert.Contains("Sat/BattVoltage", text);
        Assert.Contains("(alias: EPS_V_BATT)", text);
    }

    [Test]
    public void Find_NoMatches_ExitsOne()
    {
        var output = new StringWriter();

        var exitCode = FindCommand.Run(WriteDocument(), "NoSuchThing", json: false, output, new StringWriter());

        Assert.Equal(FindCommand.ExitNoMatches, exitCode);
        Assert.Contains("no matches", output.ToString());
    }
}
