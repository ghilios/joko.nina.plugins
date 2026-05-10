using System.Linq;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class ExtensionsTests {

    [Test]
    public void Partition_ExactMultipleOfSize_ReturnsEqualChunks() {
        var src = new[] { 1, 2, 3, 4, 5, 6 };
        var chunks = src.Partition(2).Select(c => c.ToArray()).ToArray();
        Assert.That(chunks.Length, Is.EqualTo(3));
        Assert.That(chunks[0], Is.EqualTo(new[] { 1, 2 }));
        Assert.That(chunks[1], Is.EqualTo(new[] { 3, 4 }));
        Assert.That(chunks[2], Is.EqualTo(new[] { 5, 6 }));
    }

    [Test]
    public void Partition_NonMultiple_LastChunkIsShorter() {
        var src = new[] { 1, 2, 3, 4, 5 };
        var chunks = src.Partition(2).Select(c => c.ToArray()).ToArray();
        Assert.That(chunks.Length, Is.EqualTo(3));
        Assert.That(chunks[2], Is.EqualTo(new[] { 5 }));
    }

    [Test]
    public void Partition_EmptySource_YieldsNoChunks() {
        var chunks = System.Array.Empty<int>().Partition(3).ToArray();
        Assert.That(chunks, Is.Empty);
    }
}
