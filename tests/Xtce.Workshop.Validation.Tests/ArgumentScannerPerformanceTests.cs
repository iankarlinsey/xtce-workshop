using System.Diagnostics;
using System.Text;
using Xtce.Workshop.Model;

namespace Xtce.Workshop.Validation.Tests;

/// <summary>
/// Issue #94: ArgumentScanner re-parsed the preserved ArgumentTypeSet fragment on every
/// argument-type resolution, making R15 take minutes on command-heavy files. With
/// memoization the full rule pass over this repro runs in well under the bound; the
/// un-memoized code did not finish it in ten minutes.
/// </summary>
public class ArgumentScannerPerformanceTests
{
    [Test]
    public void RulePass_OnCommandHeavyDocument_StaysFast()
    {
        var xml = new StringBuilder();
        xml.Append("<SpaceSystem xmlns=\"http://www.omg.org/spec/XTCE/20180204\" name=\"CmdHeavy\"><CommandMetaData><ArgumentTypeSet>");
        for (var i = 0; i < 4000; i++)
        {
            xml.Append($"<IntegerArgumentType name=\"AT{i}\" sizeInBits=\"16\"><UnitSet/><IntegerDataEncoding sizeInBits=\"16\"/></IntegerArgumentType>");
        }
        xml.Append("</ArgumentTypeSet><MetaCommandSet>");
        xml.Append("<MetaCommand name=\"Base\" abstract=\"true\"><ArgumentList>");
        for (var j = 0; j < 5; j++)
        {
            xml.Append($"<Argument name=\"BA{j}\" argumentTypeRef=\"AT{j}\" initialValue=\"1\"/>");
        }
        xml.Append("</ArgumentList><CommandContainer name=\"BaseC\"/></MetaCommand>");
        for (var i = 0; i < 800; i++)
        {
            xml.Append($"<MetaCommand name=\"C{i}\"><BaseMetaCommand metaCommandRef=\"Base\"><ArgumentAssignmentList>");
            for (var j = 0; j < 5; j++)
            {
                xml.Append($"<ArgumentAssignment argumentName=\"BA{j}\" argumentValue=\"3\"/>");
            }
            xml.Append("</ArgumentAssignmentList></BaseMetaCommand><ArgumentList>");
            for (var j = 0; j < 5; j++)
            {
                xml.Append($"<Argument name=\"A{i}_{j}\" argumentTypeRef=\"AT{(i * 5 + j) % 4000}\" initialValue=\"2\"/>");
            }
            xml.Append("</ArgumentList><CommandContainer name=\"CC{i}\"/></MetaCommand>");
        }
        xml.Append("</MetaCommandSet></CommandMetaData></SpaceSystem>");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml.ToString()));
        var document = XtceDocumentReader.LoadWithRecovery(stream).Document!;

        var stopwatch = Stopwatch.StartNew();
        var issues = XtceValidator.Validate(document);
        stopwatch.Stop();

        Assert.Equal(0, issues.Count(i => i.RuleId.Contains("R15")));
        // Generous CI headroom; the pre-fix code measured in MINUTES on this shape.
        Assert.True(stopwatch.ElapsedMilliseconds < 20000,
            $"rule pass took {stopwatch.ElapsedMilliseconds} ms — argument-scan memoization has regressed");
    }
}
