using System.Diagnostics;

namespace Xtce.Workshop.Model.Tests;

/// <summary>
/// Validates the streaming (XmlReader/XmlWriter, not DOM) architecture decision against
/// actual scale, empirically, rather than resting on the reasoning that motivated it.
/// This project's central stated constraint is performance on tens-of-MB XTCE files, and
/// every other fixture in this test suite is 1-3 nodes. Deliberately generates a WIDE,
/// not deep, tree — recursive-descent parsing has its own separate stack-depth risk at
/// extreme nesting depth, unrelated to what this test checks (streaming throughput), so
/// depth is kept trivial here on purpose. Not committed as a fixture file: a tens-of-MB
/// binary in the repo is bad hygiene, so it's generated at test time instead.
/// </summary>
public class LargeDocumentPerformanceTests
{

    [Test]
    public void RoundTrip_WideDocumentAtTensOfMegabytesScale_CompletesWithinReasonableTimeAndIsCorrect()
    {
        const int childCount = 300_000;
        var original = new SpaceSystem(
            "Root",
            Enumerable.Range(0, childCount)
                .Select(i => new SpaceSystem($"Node{i:D6}", []))
                .ToList());

        var writeStopwatch = Stopwatch.StartNew();
        using var stream = new MemoryStream();
        XtceDocumentWriter.Write(original, stream);
        writeStopwatch.Stop();

        var sizeInMegabytes = stream.Length / (1024.0 * 1024.0);
        TestContext.Out.WriteLine($"Serialized size: {sizeInMegabytes:F1} MB in {writeStopwatch.ElapsedMilliseconds} ms");
        Assert.True(sizeInMegabytes >= 10.0, $"Expected at least 10MB to actually exercise scale, got {sizeInMegabytes:F1}MB");

        stream.Position = 0;
        var readStopwatch = Stopwatch.StartNew();
        var reloaded = XtceDocumentReader.Load(stream);
        readStopwatch.Stop();
        TestContext.Out.WriteLine($"Loaded back in {readStopwatch.ElapsedMilliseconds} ms");

        // Reasonable, not scientific — this is a regression guard against something going
        // pathologically superlinear (e.g. accidental O(n^2) from repeated list copies),
        // not a tuned performance SLA. 10 seconds is generously loose for ~10MB of flat
        // sibling elements on any hardware this is likely to run on, CI included.
        Assert.True(writeStopwatch.ElapsedMilliseconds < 10_000,
            $"Write took {writeStopwatch.ElapsedMilliseconds} ms, expected well under 10000 ms");
        Assert.True(readStopwatch.ElapsedMilliseconds < 10_000,
            $"Read took {readStopwatch.ElapsedMilliseconds} ms, expected well under 10000 ms");

        // Correctness, not just "didn't throw" — spot-check structure, not full equality
        // (a full SequenceEqual-based Equals over 300,000 children is itself a needless
        // O(n) cost in the test; targeted checks are enough to prove the round-trip held).
        Assert.Equal("Root", reloaded.Name);
        Assert.Equal(childCount, reloaded.Children.Count);
        Assert.Equal("Node000000", reloaded.Children[0].Name);
        Assert.Equal($"Node{childCount - 1:D6}", reloaded.Children[^1].Name);
        Assert.Equal("Node150000", reloaded.Children[150_000].Name);
    }
}
