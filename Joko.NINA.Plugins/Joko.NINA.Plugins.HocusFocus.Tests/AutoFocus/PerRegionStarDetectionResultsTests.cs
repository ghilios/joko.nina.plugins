using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class PerRegionStarDetectionResultsTests {

        [Test]
        public void Set_ThenGet_ReturnsResultForThatRegion() {
            var store = new PerRegionStarDetectionResults();
            var resultA = new StarDetectionResult();
            var resultB = new StarDetectionResult();

            store.Set(0, resultA);
            store.Set(6, resultB);

            Assert.Multiple(() => {
                Assert.That(store.Get(0), Is.SameAs(resultA));
                Assert.That(store.Get(6), Is.SameAs(resultB));
                Assert.That(store.Get(3), Is.Null, "An unset region returns null");
            });
        }

        // Regression for the HFR-misattribution race: the 7 region-analysis tasks of a single frame run
        // concurrently against the same per-frame store. Each region must read back its OWN result and
        // never another region's. With the old single shared field this clobbered across regions.
        [Test]
        public void ConcurrentSetDistinctRegions_EachReadsBackItsOwnResult() {
            const int regionCount = 7;
            const int iterations = 2000;

            for (int iter = 0; iter < iterations; ++iter) {
                var store = new PerRegionStarDetectionResults();
                var expected = new StarDetectionResult[regionCount];
                for (int r = 0; r < regionCount; ++r) {
                    expected[r] = new StarDetectionResult();
                }

                var readBack = new StarDetectionResult[regionCount];
                var barrier = new Barrier(regionCount);
                var tasks = new List<Task>(regionCount);
                for (int r = 0; r < regionCount; ++r) {
                    int regionIndex = r;
                    tasks.Add(Task.Run(() => {
                        // Start the write/read as simultaneously as possible to maximize the chance of
                        // exposing a cross-region clobber if the store were not per-region.
                        barrier.SignalAndWait();
                        store.Set(regionIndex, expected[regionIndex]);
                        readBack[regionIndex] = store.Get(regionIndex);
                    }));
                }
                Task.WaitAll(tasks.ToArray());

                for (int r = 0; r < regionCount; ++r) {
                    Assert.That(readBack[r], Is.SameAs(expected[r]),
                        $"Region {r} read back a different region's result (iteration {iter})");
                }
            }
        }
    }
}
