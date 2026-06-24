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
            "count=48\r\n" +
            "star 287.9972 31.9821 2.922775 0.6275\r\n" +
            "star 416.0106 31.9896 2.978072 0.6182\r\n" +
            "star 31.9569 32.0005 2.975680 0.6446\r\n" +
            "star 95.9995 32.0016 2.961249 0.6265\r\n" +
            "star 224.0156 32.0038 2.995798 0.6313\r\n" +
            "star 351.9757 32.0112 2.969016 0.6338\r\n" +
            "star 159.9824 32.0204 2.959315 0.6261\r\n" +
            "star 159.9989 95.9470 2.983049 0.6376\r\n" +
            "star 287.9948 95.9772 2.951876 0.6225\r\n" +
            "star 352.0297 96.0010 2.968764 0.6337\r\n" +
            "star 223.9891 96.0049 2.931570 0.6162\r\n" +
            "star 415.9753 96.0537 2.929834 0.6174\r\n" +
            "star 32.0072 96.0594 2.931031 0.6291\r\n" +
            "star 95.9784 96.0779 2.982615 0.6452\r\n" +
            "star 159.9794 159.9584 2.943640 0.6379\r\n" +
            "star 352.0284 159.9584 2.979470 0.6243\r\n" +
            "star 288.0065 159.9673 2.999183 0.6222\r\n" +
            "star 96.0164 160.0169 2.987839 0.6242\r\n" +
            "star 31.9602 160.0209 2.984810 0.6363\r\n" +
            "star 416.0099 160.0209 3.009649 0.6255\r\n" +
            "star 223.9890 160.0931 3.007711 0.6203\r\n" +
            "star 351.9410 223.9616 2.977955 0.6229\r\n" +
            "star 415.9785 223.9637 2.968713 0.6199\r\n" +
            "star 31.9741 223.9648 2.964981 0.6084\r\n" +
            "star 160.0263 223.9678 2.973133 0.6359\r\n" +
            "star 95.9922 223.9775 2.933265 0.6276\r\n" +
            "star 288.0021 224.0188 2.984899 0.6188\r\n" +
            "star 223.9720 224.0810 3.013252 0.6335\r\n" +
            "star 96.0783 287.9810 3.023550 0.6139\r\n" +
            "star 32.0302 287.9854 2.970971 0.6179\r\n" +
            "star 224.0644 287.9914 2.968096 0.6074\r\n" +
            "star 416.0320 287.9921 2.933165 0.6216\r\n" +
            "star 352.0084 288.0032 2.973213 0.6260\r\n" +
            "star 288.0202 288.0342 3.011008 0.6478\r\n" +
            "star 160.0504 288.0428 2.990959 0.6279\r\n" +
            "star 159.9877 351.9534 2.934105 0.6129\r\n" +
            "star 223.9996 351.9638 2.917287 0.6135\r\n" +
            "star 351.9702 351.9913 2.959108 0.6194\r\n" +
            "star 95.9751 351.9957 2.961774 0.6114\r\n" +
            "star 31.9451 352.0148 2.951521 0.6362\r\n" +
            "star 288.0278 352.0217 2.985469 0.6421\r\n" +
            "star 415.9739 352.0502 2.970900 0.6225\r\n" +
            "star 32.0036 415.9661 2.960674 0.5957\r\n" +
            "star 224.0057 415.9805 2.978703 0.6119\r\n" +
            "star 416.0661 415.9900 2.975281 0.6346\r\n" +
            "star 352.0376 415.9997 2.996079 0.6369\r\n" +
            "star 288.0005 416.0141 3.004318 0.5996\r\n" +
            "star 96.0302 416.0249 2.996646 0.6180\r\n" +
            "StructureCandidates=49\r\n" +
            "TotalDetected=48\r\n" +
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
            "ContaminatedBounds=[(154,410,14,13)]\r\n";

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
