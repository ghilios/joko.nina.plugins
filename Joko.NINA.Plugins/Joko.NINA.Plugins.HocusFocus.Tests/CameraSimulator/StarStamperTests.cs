#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    [TestFixture]
    public class StarStamperTests {

        private static DefocusModel BuildSanityVector() {
            return new DefocusModel(
                apertureMillimeters: 100.0,
                focalLengthMillimeters: 800.0,
                centralObstructionFraction: 0.3,
                pixelSizeMicrons: 3.76,
                seeingArcsec: 2.5,
                wavelengthNm: 550.0,
                focuserStepSizeMicrons: 0.49,
                optimalFocuserPosition: 0);
        }

        [Test]
        public void Stamp_ConservesFlux_WhenFullyOnSensor() {
            var model = BuildSanityVector();
            var kernel = PsfKernelGenerator.Generate(model, 200.0);
            const int w = 80, h = 80;
            var acc = new float[w * h];
            const double flux = 12345.0;
            StarStamper.Stamp(acc, w, h, 40.3, 40.7, kernel, flux);

            double sum = 0.0;
            foreach (var v in acc) sum += v;
            Assert.That(sum, Is.EqualTo(flux).Within(1e-3 * flux), "all flux deposited on-sensor");
        }

        [Test]
        public void Stamp_SubPixelPhase_PreservesCentroid() {
            var model = BuildSanityVector();
            var kernel = PsfKernelGenerator.Generate(model, 200.0);
            const int w = 60, h = 60;
            var acc = new float[w * h];
            const double cx = 30.5, cy = 20.3;
            StarStamper.Stamp(acc, w, h, cx, cy, kernel, 1000.0);

            double sum = 0.0, sx = 0.0, sy = 0.0;
            for (var y = 0; y < h; ++y) {
                for (var x = 0; x < w; ++x) {
                    var v = acc[y * w + x];
                    sum += v;
                    sx += v * x;
                    sy += v * y;
                }
            }
            var centroidX = sx / sum;
            var centroidY = sy / sum;
            Assert.Multiple(() => {
                Assert.That(centroidX, Is.EqualTo(cx).Within(0.06), "centroid x within ~0.05 px");
                Assert.That(centroidY, Is.EqualTo(cy).Within(0.06), "centroid y within ~0.05 px");
            });
        }

        [Test]
        public void Stamp_ClipsContributionsOutsideSensor() {
            var model = BuildSanityVector();
            var kernel = PsfKernelGenerator.Generate(model, 200.0);
            const int w = 40, h = 40;
            var acc = new float[w * h];
            // Star centred on the corner: only ~1/4 of the flux lands on-sensor, nothing throws / writes OOB.
            StarStamper.Stamp(acc, w, h, 0.0, 0.0, kernel, 1000.0);

            double sum = 0.0;
            foreach (var v in acc) sum += v;
            Assert.Multiple(() => {
                Assert.That(sum, Is.GreaterThan(0.0), "some flux lands on-sensor");
                Assert.That(sum, Is.LessThan(1000.0), "clipped flux is less than the total");
            });
        }
    }
}
