using System.Linq;
using System.Threading.Tasks;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class MultiStopWatchTests {

    [Test]
    public void GenerateString_NoEntries_ContainsOnlyTotalElapsed() {
        var sw = MultiStopWatch.Measure();

        var output = sw.GenerateString();

        // No per-entry segments, just the trailing total elapsed.
        Assert.That(output, Does.StartWith("; Elapsed: "));
        Assert.That(output, Does.Not.Contain(", "));
    }

    [Test]
    public void GenerateString_WithEntries_IncludesEachEntryAndTotalElapsed() {
        var sw = MultiStopWatch.Measure();

        sw.RecordEntry("alpha");
        sw.RecordEntry("beta");
        sw.RecordEntry("gamma");

        var output = sw.GenerateString();

        Assert.Multiple(() => {
            Assert.That(output, Does.Contain("alpha: "));
            Assert.That(output, Does.Contain("beta: "));
            Assert.That(output, Does.Contain("gamma: "));
            Assert.That(output, Does.Contain("; Elapsed: "));
            // Entries are comma-separated.
            Assert.That(output.Split(", ").Length, Is.GreaterThanOrEqualTo(3));
        });
    }

    [Test]
    public void RecordEntry_ConcurrentCalls_AllEntriesAppearInOutput() {
        var sw = MultiStopWatch.Measure();
        const int count = 64;

        Parallel.For(0, count, i => sw.RecordEntry($"e{i}"));

        var output = sw.GenerateString();
        var matched = Enumerable.Range(0, count).Count(i => output.Contains($"e{i}: "));
        Assert.That(matched, Is.EqualTo(count));
    }
}
