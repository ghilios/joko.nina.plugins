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
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    /// <summary>
    /// Direct pins of the exponentially-scaled modified Bessel functions against high-precision reference
    /// values (Ĩ_n(x) = e^(−|x|)·I_n(x)), including points on each side of the A&amp;S 3.75 branch boundary and
    /// the large-x asymptote. These are independent of the PSF/Rice path, so a coefficient typo can't hide
    /// behind the Rice cross-check (which shares these functions on both sides).
    /// </summary>
    [TestFixture]
    public class ModifiedBesselTests {

        // Reference values: e^(−x)·I_n(x) from the series Σ (x/2)^(2k)/(k!)² etc. (double precision).
        [TestCase(1.0, 0.465759607594, 0.207910415350)] // small-x branch
        [TestCase(3.0, 0.243000354162, 0.196826713297)] // small-x branch, near the boundary
        [TestCase(3.7, 0.216049441673, 0.183837858027)] // just below the 3.75 boundary
        [TestCase(4.0, 0.207001921224, 0.178750839502)] // just above the 3.75 boundary
        public void ScaledBessel_MatchesReference(double x, double expectedI0, double expectedI1) {
            Assert.Multiple(() => {
                Assert.That(ModifiedBessel.ScaledI0(x), Is.EqualTo(expectedI0).Within(1e-6), $"ScaledI0({x})");
                Assert.That(ModifiedBessel.ScaledI1(x), Is.EqualTo(expectedI1).Within(1e-6), $"ScaledI1({x})");
            });
        }

        [Test]
        public void ScaledI0_LargeX_ApproachesAsymptote() {
            // Ĩ₀(x) → 1/√(2πx) as x→∞ (leading term of the A&S large-x expansion).
            const double x = 100.0;
            var asymptote = 1.0 / Math.Sqrt(2.0 * Math.PI * x);
            Assert.That(ModifiedBessel.ScaledI0(x), Is.EqualTo(asymptote).Within(0.005 * asymptote),
                "ScaledI0(100) within 0.5% of 1/√(2πx)");
        }

        [Test]
        public void ScaledI0_IsEven_ScaledI1_IsOdd() {
            Assert.Multiple(() => {
                Assert.That(ModifiedBessel.ScaledI0(-2.3), Is.EqualTo(ModifiedBessel.ScaledI0(2.3)).Within(1e-15));
                Assert.That(ModifiedBessel.ScaledI1(-2.3), Is.EqualTo(-ModifiedBessel.ScaledI1(2.3)).Within(1e-15));
            });
        }

        [Test]
        public void ScaledBessel_AtZero() {
            Assert.Multiple(() => {
                Assert.That(ModifiedBessel.ScaledI0(0.0), Is.EqualTo(1.0).Within(1e-12), "Ĩ₀(0)=I₀(0)=1");
                Assert.That(ModifiedBessel.ScaledI1(0.0), Is.EqualTo(0.0).Within(1e-12), "Ĩ₁(0)=I₁(0)=0");
            });
        }
    }
}
