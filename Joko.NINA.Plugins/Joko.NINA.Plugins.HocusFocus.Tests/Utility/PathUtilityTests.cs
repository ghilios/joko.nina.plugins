using System;
using System.IO;
using System.Threading;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class PathUtilityTests {

    private static string Combine(params string[] parts) =>
        string.Join(Path.DirectorySeparatorChar.ToString(), parts);

    private static string NewRoot() {
        var root = Path.Combine(Path.GetTempPath(), "hf-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root) {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

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
        var root = NewRoot();
        var dir = Path.Combine(root, "AutoFocus");
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
            Cleanup(root);
        }
    }

    [Test]
    public void WriteAllTextAtomic_OverwritesExistingFile() {
        var root = NewRoot();
        var dir = Path.Combine(root, "AutoFocus");
        Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "report.json");
            File.WriteAllText(path, "old");
            PathUtility.WriteAllTextAtomic(path, "new");

            Assert.That(File.ReadAllText(path), Is.EqualTo("new"));
        } finally {
            Cleanup(root);
        }
    }

    [Test]
    public void WriteAllTextAtomic_StagesTempFileOutsideTargetDirectory() {
        var root = NewRoot();
        var reportDir = Path.Combine(root, "AutoFocus");
        Directory.CreateDirectory(reportDir);
        try {
            PathUtility.WriteAllTextAtomic(Path.Combine(reportDir, "report.json"), "{\"ok\":true}");

            var expectedStaging = Path.Combine(root, "AutoFocus" + PathUtility.TempDirectorySuffix);
            Assert.Multiple(() => {
                Assert.That(
                    Directory.GetFiles(reportDir),
                    Has.Length.EqualTo(1),
                    "the watched directory must only ever contain the final report");
                Assert.That(
                    Directory.Exists(expectedStaging),
                    Is.True,
                    "the temp file must be staged in a sibling directory so the publish is a cross-directory rename");
                Assert.That(Directory.GetFiles(expectedStaging), Is.Empty, "staging directory should be left empty");
            });
        } finally {
            Cleanup(root);
        }
    }

    [Test]
    public void WriteAllTextAtomic_ExplicitTempDirectory_IsUsedAndLeftEmpty() {
        var root = NewRoot();
        var reportDir = Path.Combine(root, "AutoFocus");
        var stagingDir = Path.Combine(root, "staging");
        Directory.CreateDirectory(reportDir);
        try {
            var path = Path.Combine(reportDir, "report.json");
            PathUtility.WriteAllTextAtomic(path, "hello", stagingDir);

            Assert.Multiple(() => {
                Assert.That(File.ReadAllText(path), Is.EqualTo("hello"));
                Assert.That(Directory.Exists(stagingDir), Is.True, "explicit staging directory should be created on demand");
                Assert.That(Directory.GetFiles(stagingDir), Is.Empty, "staging directory should be left empty");
            });
        } finally {
            Cleanup(root);
        }
    }

    [Test]
    public void WriteAllTextAtomic_NullPath_Throws() {
        Assert.That(() => PathUtility.WriteAllTextAtomic(null, "x"), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void WriteAllTextAtomic_RaisesCreatedEvent_OnNinaStyleWatcher() {
        var root = NewRoot();
        var reportDir = Path.Combine(root, "AutoFocus");
        Directory.CreateDirectory(reportDir);
        try {
            using var createdSignal = new ManualResetEventSlim(false);
            string createdName = null;

            // Mirrors NINA core's AutoFocusToolVM.reportFileWatcher exactly.
            using (var watcher = new FileSystemWatcher {
                Path = reportDir,
                NotifyFilter = NotifyFilters.FileName,
                Filter = "*.json",
                IncludeSubdirectories = false
            }) {
                watcher.Created += (_, e) => { createdName = e.Name; createdSignal.Set(); };
                watcher.EnableRaisingEvents = true;

                PathUtility.WriteAllTextAtomic(Path.Combine(reportDir, "report.json"), "{\"ok\":true}");

                Assert.That(
                    createdSignal.Wait(TimeSpan.FromSeconds(10)),
                    Is.True,
                    "NINA's AutoFocusToolVM subscribes only to Created/Deleted. Staging the temp file inside the "
                    + "watched directory turns the publish into an intra-directory rename, which Windows reports as "
                    + "Renamed, so the AF chart dropdown never updates until restart.");
            }

            Assert.That(createdName, Is.EqualTo("report.json"));
        } finally {
            Cleanup(root);
        }
    }
}
