using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Determinism guard and pre-change baseline for the star detector.
    ///
    /// <list type="bullet">
    ///   <item><term>Determinism</term><description>
    ///     Running <c>Detect</c> twice on the same small synthetic field must produce the
    ///     identical <see cref="StarDetectorEquivalence.Signature"/>.
    ///   </description></item>
    ///   <item><term>Baseline</term><description>
    ///     The current detector must reproduce the committed golden signature captured on the
    ///     <c>ghilios/af-star-detection-parallelization</c> branch before any production changes.
    ///     Later parallelization tasks must still match this baseline to prove results are unchanged.
    ///   </description></item>
    /// </list>
    ///
    /// The field is deliberately small (512×512, 49 stars) so this test runs in a few seconds in the
    /// normal suite (it is NOT <c>[Explicit]</c>).
    /// </summary>
    [TestFixture]
    public class StarDetectorEquivalenceTests {

        // ── Golden baseline signature ─────────────────────────────────────────────────────────────────
        // Captured by running Detect_SmallField_MatchesGoldenBaseline with PrintGolden = true
        // on the ghilios/af-star-detection-parallelization branch before any production changes.
        // DO NOT edit this constant unless you are intentionally changing detection behavior and
        // re-baselining — this is the "before" fingerprint that parallelization tasks must match.
        //
        // If this test breaks after a production change, re-run with PrintGolden = true,
        // verify the new signature is expected, and update this constant in a separate commit.
        private const string GoldenSignature =
            "count=49\r\n" +
            "star 287.9880 31.9798 2.918343 0.6269\r\n" +
            "star 416.0067 31.9906 2.975633 0.6179\r\n" +
            "star 223.9896 31.9970 2.936451 0.6298\r\n" +
            "star 31.9437 32.0002 2.966611 0.6434\r\n" +
            "star 95.9951 32.0034 2.958378 0.6260\r\n" +
            "star 351.9692 32.0095 2.964885 0.6331\r\n" +
            "star 159.9772 32.0195 2.954695 0.6255\r\n" +
            "star 160.0002 95.9441 3.003604 0.6376\r\n" +
            "star 287.9874 95.9786 2.947651 0.6218\r\n" +
            "star 352.0325 95.9964 2.951912 0.6340\r\n" +
            "star 223.9871 96.0050 2.930782 0.6160\r\n" +
            "star 415.9737 96.0544 2.928518 0.6172\r\n" +
            "star 32.0002 96.0622 2.926781 0.6285\r\n" +
            "star 95.9757 96.0784 2.980587 0.6449\r\n" +
            "star 159.9758 159.9596 2.940594 0.6374\r\n" +
            "star 352.0176 159.9597 2.969314 0.6233\r\n" +
            "star 288.0035 159.9694 2.996794 0.6219\r\n" +
            "star 415.9966 160.0170 2.976688 0.6245\r\n" +
            "star 96.0052 160.0175 2.978243 0.6232\r\n" +
            "star 31.9547 160.0230 2.977881 0.6354\r\n" +
            "star 223.9815 160.0904 3.002249 0.6195\r\n" +
            "star 351.9324 223.9616 2.973073 0.6221\r\n" +
            "star 415.9722 223.9621 2.963116 0.6193\r\n" +
            "star 31.9656 223.9625 2.961965 0.6078\r\n" +
            "star 160.0285 223.9657 2.973884 0.6360\r\n" +
            "star 95.9809 223.9789 2.928255 0.6268\r\n" +
            "star 288.0003 224.0185 2.983022 0.6185\r\n" +
            "star 223.9637 224.0819 3.008111 0.6328\r\n" +
            "star 96.0685 287.9831 3.016291 0.6129\r\n" +
            "star 32.0167 287.9924 2.962024 0.6168\r\n" +
            "star 416.0250 287.9927 2.927678 0.6209\r\n" +
            "star 224.0620 287.9939 2.966105 0.6072\r\n" +
            "star 352.0145 288.0020 2.974695 0.6262\r\n" +
            "star 288.0088 288.0324 3.002000 0.6467\r\n" +
            "star 160.0438 288.0435 2.985785 0.6273\r\n" +
            "star 159.9815 351.9555 2.931431 0.6125\r\n" +
            "star 223.9899 351.9646 2.910670 0.6124\r\n" +
            "star 95.9703 351.9926 2.959507 0.6110\r\n" +
            "star 351.9700 351.9937 2.958390 0.6193\r\n" +
            "star 31.9405 352.0138 2.947560 0.6358\r\n" +
            "star 288.0262 352.0212 2.983956 0.6419\r\n" +
            "star 415.9632 352.0474 2.962843 0.6215\r\n" +
            "star 31.9965 415.9686 2.934056 0.5950\r\n" +
            "star 224.0065 415.9818 2.979502 0.6120\r\n" +
            "star 416.0547 415.9842 2.968162 0.6338\r\n" +
            "star 352.0337 416.0018 3.010850 0.6366\r\n" +
            "star 287.9948 416.0148 2.979401 0.5989\r\n" +
            "star 159.9947 416.0178 2.977280 0.6323\r\n" +
            "star 96.0184 416.0283 2.988837 0.6169\r\n" +
            "StructureCandidates=49\r\n" +
            "TotalDetected=49\r\n" +
            "TooSmall=0\r\n" +
            "OnBorder=0\r\n" +
            "TooLowHFR=0\r\n" +
            "HFRAnalysisFailed=0\r\n" +
            "PSFFitFailed=0\r\n" +
            "OutsideROI=0\r\n" +
            "SaturatedPixelCount=0\r\n" +
            "HotpixelCount=221643\r\n" +
            "RelaxationAdmittedCount=0\r\n" +
            "TooDistortedBounds=[]\r\n" +
            "DegenerateBounds=[]\r\n" +
            "SaturatedBounds=[]\r\n" +
            "LowSensitivityBounds=[]\r\n" +
            "NotCenteredBounds=[]\r\n" +
            "TooFlatBounds=[]\r\n" +
            "TooElongatedBounds=[]\r\n" +
            "BloomSuppressedBounds=[]\r\n" +
            "ContaminatedBounds=[]\r\n";

        // Set to true locally to print the signature (then copy it into GoldenSignature above).
        // Must be false when committed.
        private const bool PrintGolden = false;

        [Test]
        public async Task Detect_SmallField_IsDeterministic() {
            var p = StarDetectorEquivalence.StandardParams();

            using var field1 = StarDetectorEquivalence.BuildSmallField();
            var result1 = await StarDetectorEquivalence.RunDetect(field1, p);
            var sig1 = StarDetectorEquivalence.Signature(result1);

            using var field2 = StarDetectorEquivalence.BuildSmallField();
            var result2 = await StarDetectorEquivalence.RunDetect(field2, p);
            var sig2 = StarDetectorEquivalence.Signature(result2);

            TestContext.Progress.WriteLine($"[determinism] detected={result1.DetectedStars.Count}");
            Assert.That(sig2, Is.EqualTo(sig1),
                "Detect must be deterministic: two runs on the same field must yield identical signatures");
        }

        [Test]
        public async Task Detect_SmallField_MatchesGoldenBaseline() {
            var p = StarDetectorEquivalence.StandardParams();
            using var field = StarDetectorEquivalence.BuildSmallField();
            var result = await StarDetectorEquivalence.RunDetect(field, p);
            var sig = StarDetectorEquivalence.Signature(result);

            if (PrintGolden) {
                // Emit the full signature to the test output so it can be copied into GoldenSignature.
                TestContext.Progress.WriteLine("=== GOLDEN SIGNATURE (copy into GoldenSignature const) ===");
                TestContext.Progress.WriteLine(sig);
                TestContext.Progress.WriteLine("=== END GOLDEN ===");
                // Skip assertion when printing — the point is to capture the value.
                Assert.Ignore("PrintGolden=true: golden signature printed above; set PrintGolden=false and paste the value into GoldenSignature.");
                return;
            }

            Assert.That(sig, Is.EqualTo(GoldenSignature),
                "Detected stars and metrics must exactly match the committed pre-change baseline. " +
                "If this fails after a parallelization change, the change has altered results — " +
                "investigate before updating the baseline.");
        }

        /// <summary>
        /// Bit-identical gate for the spatially-adaptive binarization rollout: with
        /// <see cref="StarDetectorParams.LocallyAdaptiveBinarization"/> OFF the binarization seam runs the exact
        /// legacy scalar path (the new coarse grids are never even computed), so the full detected-star + metrics
        /// signature MUST equal the committed pre-change baseline. This is the guarantee that lets the option's
        /// default be flipped ON safely while keeping the boolean as an off-switch.
        /// </summary>
        [Test]
        public async Task Detect_AdaptiveBinarizationOff_MatchesLegacyBaseline() {
            var p = StarDetectorEquivalence.StandardParams();
            p.LocallyAdaptiveBinarization = false;
            using var field = StarDetectorEquivalence.BuildSmallField();
            var result = await StarDetectorEquivalence.RunDetect(field, p);
            var sig = StarDetectorEquivalence.Signature(result);

            Assert.That(sig, Is.EqualTo(GoldenSignature),
                "LocallyAdaptiveBinarization=false must be byte-for-byte identical to the legacy baseline.");
        }

        /// <summary>
        /// End-to-end smoke test for the ON path: on a spatially-uniform field the local-median + NC·local-σ surface
        /// degenerates to ~the global threshold, so adaptive binarization must execute and recover the same bright
        /// stars as the legacy path. This is a behavioural check (same stars, within a fraction of a pixel), NOT a
        /// bit-identical one — the estimators differ (block median / 1.4826·MAD vs histogram-median / kappa-sigma),
        /// so a candidate at the pixel margin may differ. The real ON-path validation is the AF-bank audit (Step 6).
        /// </summary>
        [Test]
        public async Task Detect_AdaptiveBinarizationOn_RecoversSameStarsOnUniformField() {
            var pOff = StarDetectorEquivalence.StandardParams();
            var pOn = StarDetectorEquivalence.StandardParams();
            pOn.LocallyAdaptiveBinarization = true;
            pOn.AdaptiveNoiseBlockSize = 128;

            using var field1 = StarDetectorEquivalence.BuildSmallField();
            var off = await StarDetectorEquivalence.RunDetect(field1, pOff);
            using var field2 = StarDetectorEquivalence.BuildSmallField();
            var on = await StarDetectorEquivalence.RunDetect(field2, pOn);

            TestContext.Progress.WriteLine($"[adaptive] off={off.DetectedStars.Count} on={on.DetectedStars.Count}");
            Assert.That(on.DetectedStars.Count, Is.EqualTo(off.DetectedStars.Count).Within(1),
                "adaptive ON should recover essentially the same star count on a uniform field");
            foreach (var s in off.DetectedStars) {
                Assert.That(
                    on.DetectedStars.Any(t => Math.Abs(t.Center.X - s.Center.X) < 0.5 && Math.Abs(t.Center.Y - s.Center.Y) < 0.5),
                    Is.True, $"adaptive ON dropped a star near ({s.Center.X:F1},{s.Center.Y:F1})");
            }
        }
    }
}
