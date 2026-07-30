#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NUnit.Framework;
using System.IO;
using System.Linq;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Golden {

    [TestFixture]
    public class GoldenStarSetTests {

        private static GoldenFrame Sample() {
            return new GoldenFrame {
                ImageFile = "05_Frame00_BitDepth16_Bayered0_Focuser2701.fits",
                FocuserPosition = 2701,
                CoveredTiles = new System.Collections.Generic.List<string> { "f2701/tileA.png", "f2701/tileB.png" },
                Stars = {
                    new GoldenStarBox { X = 100, Y = 200, W = 12, H = 10, Confidence = GoldenConfidence.High },
                    new GoldenStarBox { X = 500, Y = 600, W = 30, H = 28, Confidence = GoldenConfidence.Low }
                }
            };
        }

        [Test]
        public void Serialize_RoundTrips_BoxesConfidenceAndCoverage() {
            var frame = Sample();
            var json = GoldenStarSetStore.Serialize(frame);
            var back = GoldenStarSetStore.Deserialize(json);

            Assert.Multiple(() => {
                Assert.That(back.ImageFile, Is.EqualTo("05_Frame00_BitDepth16_Bayered0_Focuser2701.fits"));
                Assert.That(back.FocuserPosition, Is.EqualTo(2701));
                Assert.That(back.Stars, Has.Count.EqualTo(2));
                Assert.That(back.Stars[0].X, Is.EqualTo(100));
                Assert.That(back.Stars[0].CenterX, Is.EqualTo(106));
                Assert.That(back.Stars[0].CenterY, Is.EqualTo(205));
                Assert.That(back.Stars[0].Confidence, Is.EqualTo("high"));
                Assert.That(back.Stars[1].Confidence, Is.EqualTo("low"));
                Assert.That(back.CoveredTiles, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void LoadForImage_ReturnsNull_WhenFileAbsent() {
            var dir = Path.Combine(Path.GetTempPath(), "golden-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                Assert.That(GoldenStarSetStore.LoadForImage(Path.Combine(dir, "nope.fits")), Is.Null);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void SaveForImage_Then_LoadForImage_RoundTrips_WithSidecarNaming() {
            var dir = Path.Combine(Path.GetTempPath(), "golden-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                var imagePath = Path.Combine(dir, "05_Frame00_BitDepth16_Bayered0_Focuser2701.fits");
                var path = GoldenStarSetStore.SaveForImage(imagePath, Sample());
                Assert.That(File.Exists(path), Is.True);
                Assert.That(Path.GetFileName(path), Is.EqualTo("05_Frame00_BitDepth16_Bayered0_Focuser2701.fits.golden.json"));
                var back = GoldenStarSetStore.LoadForImage(imagePath);
                Assert.That(back.Stars, Has.Count.EqualTo(2));
                Assert.That(back.FocuserPosition, Is.EqualTo(2701));
            } finally {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void Deserialize_SchemaV1_LeavesUnresolvedNull() {
            // Backward compatibility is load-bearing: 13 untouched runs must score bit-identically.
            var json = @"{""imageFile"":""a.fits"",""focuserPosition"":100,""schemaVersion"":1,
                          ""stars"":[{""x"":1,""y"":2,""w"":3,""h"":4,""confidence"":""high""}]}";
            var frame = GoldenStarSetStore.Deserialize(json);
            Assert.Multiple(() => {
                Assert.That(frame.SchemaVersion, Is.EqualTo(1));
                Assert.That(frame.Stars, Has.Count.EqualTo(1));
                Assert.That(frame.Unresolved, Is.Null);
                Assert.That(frame.Coverage, Is.Null);
            });
        }

        [Test]
        public void Deserialize_SchemaV2_ReadsUnresolvedAndCoverage() {
            var json = @"{""imageFile"":""a.fits"",""focuserPosition"":100,""schemaVersion"":2,
                          ""stars"":[{""x"":1,""y"":2,""w"":3,""h"":4,""confidence"":""high""}],
                          ""unresolved"":[{""x"":9,""y"":9,""w"":36,""h"":36,""confidence"":""medium""}],
                          ""coverage"":{""high"":{""examined"":10,""total"":20}},
                          ""qaVotes"":3,""qaVersion"":""sonnet/golden-qa-v2""}";
            var frame = GoldenStarSetStore.Deserialize(json);
            Assert.Multiple(() => {
                Assert.That(frame.SchemaVersion, Is.EqualTo(2));
                Assert.That(frame.Unresolved, Has.Count.EqualTo(1));
                Assert.That(frame.Unresolved[0].W, Is.EqualTo(36.0));
                Assert.That(frame.Coverage["high"].Examined, Is.EqualTo(10));
                Assert.That(frame.Coverage["high"].Fraction, Is.EqualTo(0.5));
                Assert.That(frame.QaVotes, Is.EqualTo(3));
                Assert.That(frame.QaVersion, Is.EqualTo("sonnet/golden-qa-v2"));
            });
        }

        [Test]
        public void Serialize_OmitsUnresolvedWhenAbsent() {
            var frame = new GoldenFrame { ImageFile = "a.fits", FocuserPosition = 100 };
            Assert.That(GoldenStarSetStore.Serialize(frame), Does.Not.Contain("unresolved"));
        }

        [Test]
        public void Confidence_Rank_OrdersHighMedLow_AndDefaultsToHigh() {
            Assert.Multiple(() => {
                Assert.That(GoldenConfidence.Rank("high"), Is.EqualTo(3));
                Assert.That(GoldenConfidence.Rank("medium"), Is.EqualTo(2));
                Assert.That(GoldenConfidence.Rank("low"), Is.EqualTo(1));
                Assert.That(GoldenConfidence.Rank(null), Is.EqualTo(3));
                Assert.That(GoldenConfidence.Rank(""), Is.EqualTo(3));
                Assert.That(GoldenConfidence.Rank("garbage"), Is.EqualTo(0));
            });
        }
    }
}
