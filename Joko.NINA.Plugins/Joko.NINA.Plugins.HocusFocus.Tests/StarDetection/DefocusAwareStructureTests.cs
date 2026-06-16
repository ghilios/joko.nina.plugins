using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Phase 5: the opt-in, default-OFF defocus-aware structure-detection setting (DefocusAwareStructure +
    /// StructureLayerBoost). When OFF, candidate formation must be bit-identical regardless of the boost value;
    /// when ON, the coarser wavelet residual must still detect the field (and is a genuine EARLY param).
    /// </summary>
    [TestFixture]
    public class DefocusAwareStructureTests {

        [Test]
        public async Task FlagOff_IgnoresBoost_BitIdentical() {
            var pOff0 = StarDetectorEquivalence.StandardParams();
            var pOff7 = StarDetectorEquivalence.StandardParams();
            pOff7.StructureLayerBoost = 7; // boost set, but flag OFF ⇒ must be ignored

            using var f1 = StarDetectorEquivalence.BuildSmallField();
            var r1 = await StarDetectorEquivalence.RunDetect(f1, pOff0);

            using var f2 = StarDetectorEquivalence.BuildSmallField();
            var r2 = await StarDetectorEquivalence.RunDetect(f2, pOff7);

            Assert.That(StarDetectorEquivalence.Signature(r2), Is.EqualTo(StarDetectorEquivalence.Signature(r1)),
                "With DefocusAwareStructure OFF, StructureLayerBoost must not change detection at all.");
        }

        [Test]
        public async Task FlagOn_DetectsField_AndIsAnEarlyParam() {
            var pOn = StarDetectorEquivalence.StandardParams();
            pOn.DefocusAwareStructure = true;
            pOn.StructureLayerBoost = 2;

            using var field = StarDetectorEquivalence.BuildSmallField();
            var result = await StarDetectorEquivalence.RunDetect(field, pOn);

            Assert.Multiple(() => {
                Assert.That(result.DetectedStars, Is.Not.Null);
                Assert.That(result.DetectedStars.Count, Is.GreaterThan(0), "coarser residual must still detect the star field");
                // The setting changes candidate formation, so it must participate in the EARLY cache key.
                Assert.That(StarDetector.IsEarlyCacheKeyParameter(nameof(StarDetectorParams.DefocusAwareStructure)), Is.True);
                Assert.That(StarDetector.IsEarlyCacheKeyParameter(nameof(StarDetectorParams.StructureLayerBoost)), Is.True);
            });
        }
    }
}
