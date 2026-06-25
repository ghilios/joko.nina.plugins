using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Guards the optimizer's log-flood suppression flag
    /// (<see cref="StarDetectorParams.SuppressInfoLogging"/>). The optimizer wizard runs star detection
    /// thousands of times; left unguarded, the per-region "Average HFR / Detected Stars" INFO summary in
    /// <c>HocusFocusStarDetection.BuildStarDetectionResult</c> floods the NINA log with ~10k+ lines per run.
    /// Setting this flag on the optimizer's seed/baseline params skips that one INFO line — and must change
    /// NOTHING else.
    ///
    /// Therefore the flag must be (1) OFF by default so normal autofocus keeps the summary, (2) excluded from the
    /// detection cache key (it is a pure side channel), and (3) output-neutral (no detected-star/metric change).
    /// </summary>
    [TestFixture]
    public class SuppressInfoLoggingTests {

        [Test]
        public void SuppressInfoLogging_DefaultsToFalse() {
            Assert.That(new StarDetectorParams().SuppressInfoLogging, Is.False,
                "Normal autofocus must keep the per-region HFR INFO summary; only the optimizer opts in.");
        }

        [Test]
        public void SuppressInfoLogging_FlagExcludedFromCacheKey() {
            var pOff = StarDetectorEquivalence.StandardParams();
            var pOn = StarDetectorEquivalence.StandardParams();
            pOn.SuppressInfoLogging = true;

            // Logging-only side channel: it must NOT change the canonical cache string, else the optimizer's
            // early-context cache could treat suppress-on and suppress-off as different detections and miss.
            Assert.That(pOn.ToCanonicalCacheString(), Is.EqualTo(pOff.ToCanonicalCacheString()),
                "SuppressInfoLogging must be excluded from the detection cache key.");
        }

        [Test]
        public async Task SuppressInfoLogging_OffVsOn_DetectionBitIdentical() {
            var pOff = StarDetectorEquivalence.StandardParams();
            var pOn = StarDetectorEquivalence.StandardParams();
            pOn.SuppressInfoLogging = true;

            using var field1 = StarDetectorEquivalence.BuildSmallField();
            var resultOff = await StarDetectorEquivalence.RunDetect(field1, pOff);

            using var field2 = StarDetectorEquivalence.BuildSmallField();
            var resultOn = await StarDetectorEquivalence.RunDetect(field2, pOn);

            Assert.That(StarDetectorEquivalence.Signature(resultOn), Is.EqualTo(StarDetectorEquivalence.Signature(resultOff)),
                "Suppressing the INFO summary must not change any detected star or metric.");
        }
    }
}
