using System;
using System.IO;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class PathUtilityTests {

    private static string Combine(params string[] parts) =>
        string.Join(Path.DirectorySeparatorChar.ToString(), parts);

    [Test]
    public void GetRelativePath_ChildOfFromPath_ReturnsRelativeChild() {
        var from = @"C:\foo\bar\";
        var to = @"C:\foo\bar\baz.txt";
        var rel = PathUtility.GetRelativePath(from, to);
        Assert.That(rel, Is.EqualTo("baz.txt"));
    }

    [Test]
    public void GetRelativePath_SiblingDirectory_ReturnsDotDotPrefixed() {
        var from = @"C:\foo\bar\";
        var to = @"C:\foo\baz\qux.txt";
        var rel = PathUtility.GetRelativePath(from, to);
        Assert.That(rel, Is.EqualTo(Combine("..", "baz", "qux.txt")));
    }

    [Test]
    public void GetRelativePath_NullFromPath_Throws() {
        Assert.That(
            () => PathUtility.GetRelativePath(null, @"C:\foo"),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void GetRelativePath_EmptyFromPath_Throws() {
        Assert.That(
            () => PathUtility.GetRelativePath(string.Empty, @"C:\foo"),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void GetRelativePath_NullToPath_Throws() {
        Assert.That(
            () => PathUtility.GetRelativePath(@"C:\foo", null),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void WriteAllTextAtomic_WritesContent_AndLeavesNoTempFile() {
        var dir = Path.Combine(Path.GetTempPath(), "hf-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "2026-07-03--22-53-42.json");
            PathUtility.WriteAllTextAtomic(path, "{\"ok\":true}");

            Assert.Multiple(() => {
                Assert.That(File.ReadAllText(path), Is.EqualTo("{\"ok\":true}"));
                // The rename must clean up after itself: no leftover temp file, and the final file keeps its extension.
                Assert.That(Directory.GetFiles(dir, "*.tmp"), Is.Empty, "no temp file should remain after an atomic write");
                Assert.That(Directory.GetFiles(dir), Has.Length.EqualTo(1));
            });
        } finally {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Test]
    public void WriteAllTextAtomic_OverwritesExistingFile() {
        var dir = Path.Combine(Path.GetTempPath(), "hf-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "report.json");
            File.WriteAllText(path, "old");
            PathUtility.WriteAllTextAtomic(path, "new");

            Assert.That(File.ReadAllText(path), Is.EqualTo("new"));
        } finally {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Test]
    public void WriteAllTextAtomic_NullPath_Throws() {
        Assert.That(() => PathUtility.WriteAllTextAtomic(null, "x"), Throws.TypeOf<ArgumentNullException>());
    }
}
