using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization {

    /// <summary>
    /// G3: the wizard saves labels keyed by the ABSOLUTE run path, but offline discovery keys a run by its
    /// relative/leaf folder name. <see cref="StarReviewLabelStore.Load"/> must still match them (trailing-segment
    /// fallback) so wizard labels load against a path-relocated copy of the run, while exact matches keep working.
    /// </summary>
    [TestFixture]
    public class LabelRunIdMatchTests {

        private static string WriteLabel(string dir, string fileName, string embeddedRunId) {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            File.WriteAllText(path,
                "{\"runId\":\"" + embeddedRunId.Replace("\\", "\\\\") + "\",\"radiusPx\":6.0,\"positions\":[" +
                "{\"focuserPosition\":5000,\"wronglyRejected\":[{\"x\":10,\"y\":20,\"w\":8,\"h\":8}]}]}");
            return path;
        }

        [Test]
        public void Load_AbsolutePathEmbeddedRunId_MatchesLeafRunId() {
            var dir = Path.Combine(Path.GetTempPath(), "hf-g3-" + System.Guid.NewGuid().ToString("N"));
            try {
                // Mimics the wizard: filename + embedded runId are the sanitized absolute path.
                WriteLabel(dir, "E__WorkshopData_sensitivity_example1_attempt01.json", @"E:\WorkshopData\sensitivity_example1\attempt01");

                // Offline discovery hands us the leaf runId.
                var labels = StarReviewLabelStore.Load(dir, "attempt01", out var error);

                Assert.Multiple(() => {
                    Assert.That(error, Is.Null);
                    Assert.That(labels.Positions, Has.Count.EqualTo(1));
                    Assert.That(labels.Positions[0].FocuserPosition, Is.EqualTo(5000));
                    Assert.That(labels.Positions[0].WronglyRejected, Has.Count.EqualTo(1));
                });
            } finally {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [Test]
        public void Load_ExactFilenameMatch_StillWins() {
            var dir = Path.Combine(Path.GetTempPath(), "hf-g3-" + System.Guid.NewGuid().ToString("N"));
            try {
                WriteLabel(dir, "attempt01.json", "attempt01");
                var labels = StarReviewLabelStore.Load(dir, "attempt01", out var error);
                Assert.Multiple(() => {
                    Assert.That(error, Is.Null);
                    Assert.That(labels.Positions, Has.Count.EqualTo(1));
                });
            } finally {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [Test]
        public void LastSegment_HandlesBothSeparators() {
            Assert.Multiple(() => {
                Assert.That(StarReviewLabelStore.LastSegment(@"E:\a\b\attempt01"), Is.EqualTo("attempt01"));
                Assert.That(StarReviewLabelStore.LastSegment("toml999/attempt01"), Is.EqualTo("attempt01"));
                Assert.That(StarReviewLabelStore.LastSegment("attempt01"), Is.EqualTo("attempt01"));
                Assert.That(StarReviewLabelStore.LastSegment(""), Is.Null);
            });
        }
    }
}
