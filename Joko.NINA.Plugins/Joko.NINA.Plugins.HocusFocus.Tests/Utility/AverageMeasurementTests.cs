#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.WPF.Base.ViewModel.AutoFocus;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class AverageMeasurementTests {

        private static MeasureAndError M(double measure, double stdev) =>
            new MeasureAndError() { Measure = measure, Stdev = stdev };

        [Test]
        public void AverageMeasurement_SingleFrame_PassesThrough() {
            var result = new List<MeasureAndError> { M(2.0, 0.5) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(2.0));
                Assert.That(result.Stdev, Is.EqualTo(0.5).Within(1e-12));
            });
        }

        [Test]
        public void AverageMeasurement_MultiFrame_StdevIsSemNotRms() {
            // The mean of n frames has SEM = σ/√n; the old code returned the RMS σ (no ÷√n),
            // so FramesPerPoint never reduced the reported σ (F6).
            var result = new List<MeasureAndError> { M(2.0, 0.4), M(3.0, 0.4) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(2.5));
                Assert.That(result.Stdev, Is.EqualTo(0.4 / Math.Sqrt(2)).Within(1e-12));
            });
        }

        [Test]
        public void AverageMeasurement_InvalidSigmaFrameFirst_LaterFramesStillPooled() {
            // Regression for the F5b latch: an invalid-σ frame previously stopped variance
            // accumulation for ALL later frames while the divisor still counted them.
            var result = new List<MeasureAndError> { M(2.0, 0.0), M(3.0, 0.3), M(4.0, 0.3) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(3.0));
                // Valid-σ pool RMS = 0.3; SEM over the 3 averaged frames = 0.3/√3.
                Assert.That(result.Stdev, Is.EqualTo(0.3 / Math.Sqrt(3)).Within(1e-12));
            });
        }

        [Test]
        public void AverageMeasurement_AllSigmasInvalid_StdevNaNMeasureIntact() {
            var result = new List<MeasureAndError> { M(2.0, 0.0), M(3.0, double.NaN) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(2.5));
                Assert.That(result.Stdev, Is.NaN);
            });
        }

        [Test]
        public void AverageMeasurement_NoPositiveMeasures_ZeroMeasureNaNStdev() {
            var result = new List<MeasureAndError> { M(0.0, 0.5) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(0.0));
                Assert.That(result.Stdev, Is.NaN);
            });
        }

        [Test]
        public void AverageMeasurement_ZeroMeasureFrame_ExcludedFromMeanAndPool() {
            var result = new List<MeasureAndError> { M(0.0, 0.5), M(2.0, 0.3) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(2.0));
                Assert.That(result.Stdev, Is.EqualTo(0.3).Within(1e-12));
            });
        }

        [Test]
        public void AverageMeasurement_EmptyList_ZeroMeasureNaNStdev() {
            var result = new List<MeasureAndError>().AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(0.0));
                Assert.That(result.Stdev, Is.NaN);
            });
        }

        [Test]
        public void AverageMeasurement_InfiniteAndNegativeSigma_ExcludedFromPool() {
            var result = new List<MeasureAndError> { M(2.0, double.PositiveInfinity), M(3.0, -0.2), M(4.0, 0.3) }.AverageMeasurement();
            Assert.Multiple(() => {
                Assert.That(result.Measure, Is.EqualTo(3.0));
                // Only the 0.3 frame enters the pool; SEM over the 3 averaged frames = 0.3/√3.
                Assert.That(result.Stdev, Is.EqualTo(0.3 / Math.Sqrt(3)).Within(1e-12));
            });
        }
    }
}
