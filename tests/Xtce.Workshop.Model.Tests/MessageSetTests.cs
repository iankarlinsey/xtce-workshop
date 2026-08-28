using System.Text;

namespace Xtce.Workshop.Model.Tests;

public class MessageSetTests
{
    private static SpaceSystem LoadMessagesSample()
    {
        using var stream = File.OpenRead(TestPaths.MessagesSample);
        return XtceDocumentReader.Load(stream);
    }

    [Test]
    public void MessagesSampleFixture_IsItselfSchemaValid()
    {
        Assert.Empty(XsdValidation.Validate(File.ReadAllText(TestPaths.MessagesSample)));
    }

    [Test]
    public void Load_ParsesMessagesWithContainerRefsAndPreservesMatchCriteria()
    {
        var messageSet = LoadMessagesSample().TelemetryMetaData!.MessageSet!;

        Assert.Equal(
            ["EpsMessage", "AbstractMessage", "PieceMessage", "GhostMessage"],
            messageSet.Messages.Select(m => m.Name).ToList());

        var eps = messageSet.Messages[0];
        Assert.Equal("EpsPacket", eps.ContainerRef);
        // MatchCriteria is modeled since #108 — the comparison is inspectable.
        Assert.Null(eps.Preserved);
        Assert.Equal("101", eps.MatchCriteria!.Comparison!.Value);

        // The set-level name/shortDescription (OptionalNameDescriptionType) are preserved.
        var setAttributes = messageSet.PreservedAttributes!.ToDictionary(a => a.Name, a => a.Value);
        Assert.Equal("OperationalMessages", setAttributes["name"]);
    }

    [Test]
    public void RoundTrip_MessagesSample_IsLosslessAndSchemaValid()
    {
        var loaded = LoadMessagesSample();

        var xml = XtceDocumentWriter.Write(loaded);
        var reloaded = XtceDocumentReader.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(loaded, reloaded);
        var errors = XsdValidation.Validate(xml);
        Assert.True(errors.Count == 0, "Writer output failed XSD validation:\n" + string.Join("\n", errors));
    }

    [Test]
    public void Load_MessageWithoutContainerRef_Throws()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="S"><TelemetryMetaData><MessageSet><Message name="M"><MatchCriteria><Comparison parameterRef="P" value="1"/></MatchCriteria></Message></MessageSet></TelemetryMetaData></SpaceSystem>"""));

        var ex = Assert.Throws<XtceParseException>(() => XtceDocumentReader.Load(stream));
        Assert.Contains("ContainerRef", ex.Message);
    }
}
