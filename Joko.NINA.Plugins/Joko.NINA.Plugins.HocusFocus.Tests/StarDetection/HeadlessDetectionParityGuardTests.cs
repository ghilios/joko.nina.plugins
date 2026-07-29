using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Source-level guards against the headless/live detection-parity bug coming back. Its shape was not "somebody
    /// wrote a wrong algorithm" — it was "a loader offered a way to produce the wrong image, and every caller took
    /// it by default". So the guard is structural: the ways to produce a detection input from a NINA-format frame
    /// are enumerated here, and adding a new one has to go through this file.
    ///
    /// <para>An "the argument must be explicit" test would not have helped: it still permits the WRONG argument.
    /// The arguments are gone instead, and these tests keep them gone.</para>
    ///
    /// <para>Same family as <see cref="Resources.ButtonPaddingGuardTests"/> and
    /// <see cref="CameraSimulator.XamlResourceResolutionTests"/>: a source scan for a failure the compiler cannot
    /// see. Here the compiler is perfectly happy — <c>Detect(Mat, …)</c> on a Bayer mosaic type-checks, runs, and
    /// silently produces numbers that do not describe what the app does.</para>
    /// </summary>
    [TestFixture]
    public class HeadlessDetectionParityGuardTests {

        /// <summary>The one file allowed to load a raw Mat from a NINA-format frame: it owns the .tif carve-out.</summary>
        private const string LoaderFile = "DiagnosticUtil.cs";

        /// <summary>The legacy WPF dev GUI. It reads a user-picked file straight through OpenCV, which cannot open
        /// XISF/FITS at all, so it has no way to reach a CFA frame and never touches the profile-aware loader.</summary>
        private const string LegacyGuiFile = "Program.cs";

        private static DirectoryInfo SolutionDirectory() {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && dir.GetDirectories("TestApp").Length == 0) {
                dir = dir.Parent;
            }
            Assert.That(dir, Is.Not.Null, "Could not locate the solution directory from the test binaries.");
            return dir;
        }

        private static FileInfo[] SourceFiles(string projectFolder) {
            var files = SolutionDirectory().GetDirectories(projectFolder).Single()
                .GetFiles("*.cs", SearchOption.AllDirectories)
                .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToArray();
            Assert.That(files, Is.Not.Empty, $"Found no sources under {projectFolder} — the path walk is wrong, not the code.");
            return files;
        }

        /// <summary>Strips // and /* */ comments so a doc comment naming a banned call is not a finding.</summary>
        private static string StripComments(string source) {
            var noBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\r\n]*", string.Empty);
        }

        private static string[] OffendingFiles(string projectFolder, string pattern, params string[] allowed) =>
            SourceFiles(projectFolder)
                .Where(f => !allowed.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
                .Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f.FullName)), pattern))
                .Select(f => f.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

        [Test]
        public void NoRunnerLoadsARawMatFromANinaFormatFrame() {
            // LoadFloatMat returns the frame EXACTLY as stored, which for a bayered frame is the Bayer mosaic — an
            // image the live app never detects on. Detecting runners must take LoadRenderedImage (so Detect applies
            // the CFA filter + debayer at their own params); rendering/reference surfaces take
            // LoadDebayeredFloatMat (same debayer, deliberately no CFA filter).
            var offenders = OffendingFiles("TestApp", @"\bLoadFloatMat\s*\(", LoaderFile);

            Assert.That(offenders, Is.Empty,
                $"{string.Join(", ", offenders)} call DiagnosticUtil.LoadFloatMat. Only {LoaderFile} may: it is the raw " +
                "route, kept for the .tif carve-out. Use LoadRenderedImage to detect, or LoadDebayeredFloatMat to render.");
        }

        [Test]
        public void NoRunnerBuildsADetectionMatFromImageDataDirectly() {
            // The other way to reach the mosaic: convert an IImageData/IRenderedImage yourself. ToOpenCVMat is the
            // right call INSIDE the detector (it is what Detect uses); it is the wrong call in a runner.
            var offenders = OffendingFiles("TestApp", @"\bToOpenCVMat\s*\(", LoaderFile, LegacyGuiFile);

            Assert.That(offenders, Is.Empty,
                $"{string.Join(", ", offenders)} convert image data to a Mat directly, which bypasses the shared " +
                "RenderedImageLoading seam and reproduces the mosaic-vs-luminance bug this work removed.");
        }

        [Test]
        public void LoadFloatMatTakesNoDebayerOrCfaHotpixelArguments() {
            // These parameters were the bug's surface: debayerToLuminance defaulted to false, so ~14 call sites
            // silently detected on the mosaic, and applyCfaHotpixel offered a load-time filter that would have
            // frozen two SEARCHED optimizer axes into no-ops. Deleted with their last caller; an argument that
            // cannot be passed cannot be passed wrongly.
            var source = File.ReadAllText(SourceFiles("TestApp").Single(f => f.Name == LoaderFile).FullName);
            var signature = Regex.Match(source, @"Task<Mat>\s+LoadFloatMat\s*\(([^)]*)\)");

            Assert.That(signature.Success, Is.True, $"Could not find LoadFloatMat's signature in {LoaderFile}.");
            Assert.Multiple(() => {
                Assert.That(signature.Groups[1].Value.Split(',').Length, Is.EqualTo(2),
                    $"LoadFloatMat must take only (path, profileService); found '{signature.Groups[1].Value.Trim()}'");
                foreach (var banned in new[] { "debayerToLuminance", "applyCfaHotpixel", "hotpixelThreshold" }) {
                    Assert.That(OffendingFiles("TestApp", $@"\b{banned}\b"), Is.Empty,
                        $"'{banned}' is back in the harness. Load-time debayer/CFA choices belong to Detect, at the " +
                        "caller's params — that is what keeps the hot-pixel axes real.");
                }
            });
        }

        [Test]
        public void TheHarnessDoesNotReimplementTheWizardsSplitFrameDetector() {
            // MatSplitFrameDetector + HarnessDetection.ToFrameDetectionResult were a copy of the wizard loader's
            // detector and of its FrameDetectionResult mapping. Two copies of one mapping is how the harness came
            // to score a different image AND aggregate it differently.
            var offenders = OffendingFiles("TestApp", @"ISplitFrameDetector\b");

            Assert.That(offenders, Is.Empty,
                $"{string.Join(", ", offenders)} implement or reference ISplitFrameDetector directly. The harness must " +
                "drive RunEvaluationLoader.HocusFocusSplitFrameDetector — the wizard's own — not a mirror of it.");
        }

        [Test]
        public void TheWizardsReviewStepLoadsThroughTheSharedSeam() {
            // This one ships to users: Review's labels are what "Optimize with feedback" trusts, so if Review goes
            // back to loading a bare Mat, an OSC user labels a Bayer checkerboard and the optimizer believes it.
            var wizard = SourceFiles("Joko.NINA.Plugins.HocusFocus")
                .Single(f => f.Name == "StarDetectionOptimizerWizardVM.cs");
            var source = StripComments(File.ReadAllText(wizard.FullName));

            Assert.Multiple(() => {
                Assert.That(source, Does.Contain("RenderedImageLoading.ForDetection"),
                    "the wizard's Review loader must build its detection input through the shared seam");
                Assert.That(Regex.IsMatch(source, @"XISF\.Load|FITS\.Load"), Is.False,
                    "the wizard must load through NINA's IImageDataFactory (as its optimizer step does), not by " +
                    "calling the format loaders itself with a hard-coded isBayered");
            });
        }
    }
}
