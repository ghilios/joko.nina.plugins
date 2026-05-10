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
}
