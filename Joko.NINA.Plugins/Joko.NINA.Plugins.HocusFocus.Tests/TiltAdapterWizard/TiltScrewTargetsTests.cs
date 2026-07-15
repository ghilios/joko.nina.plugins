#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

[TestFixture]
public class TiltScrewTargetsTests {

    // Representative fitted-paraboloid model params: nonzero Gx/Gy/Kx/Ky (astigmatic) and an
    // off-center vertex (X0/Y0 != 0) so every term in the formula participates.
    private const double Gx = 0.0012;
    private const double Gy = -0.0008;
    private const double Kx = 1.1e-5;
    private const double Ky = 1.3e-5;
    private const double X0 = 150.0;
    private const double Y0 = -80.0;
    private const double RadiusMicrons = 60000.0; // 60 mm

    private static readonly double[] Angles4 = { 0.0, 90.0, 180.0, 270.0 };
    private static readonly double[] Angles3 = { 15.0, 135.0, 255.0 };

    // Independent replication of the PRE-REFACTOR inline formula that used to live in
    // InspectorVM.FillNumericGuidance (foundation brief item 4), computed via separate calls
    // rather than through TiltScrewTargets.ComputePerScrewTargets, so comparing against it is not
    // a tautology:
    //   tiltSteps  = corr.TiltMicrons / unitMicrons
    //   backSteps  = resolvedSign * corr.BackfocusMicrons / unitMicrons
    //   totalSteps = TiltScrewGeometry.SignedTotalAdjustment(corr.TiltMicrons, corr.BackfocusMicrons, unitMicrons, resolvedSign)
    private static (double tiltSteps, double backSteps, double totalSteps) ExpectedTarget(
        double angleDegrees, double radiusMicrons, double unitMicrons, int curvatureSign) {
        int resolvedSign = curvatureSign == 0 ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign : curvatureSign;
        var corr = TiltScrewGeometry.ScrewCorrectionMicrons(Gx, Gy, Kx, Ky, X0, Y0, angleDegrees, radiusMicrons);
        double tiltSteps = corr.TiltMicrons / unitMicrons;
        double backSteps = resolvedSign * corr.BackfocusMicrons / unitMicrons;
        double totalSteps = TiltScrewGeometry.SignedTotalAdjustment(corr.TiltMicrons, corr.BackfocusMicrons, unitMicrons, resolvedSign);
        return (tiltSteps, backSteps, totalSteps);
    }

    [TestCase(1.8, true, 1)]     // stepper unit (e.g. ASG EAT), σ = +1
    [TestCase(1.8, true, -1)]    // stepper unit, σ = -1
    [TestCase(212.0, false, 1)]  // screw thread-pitch unit (e.g. ASG Photon Cage), σ = +1
    public void ComputePerScrewTargets_FourScrews_MatchesHandReplicatedInlineFormula(
        double unitMicrons, bool steps, int curvatureSign) {
        var targets = TiltScrewTargets.ComputePerScrewTargets(
            Gx, Gy, Kx, Ky, X0, Y0, Angles4, RadiusMicrons, unitMicrons, curvatureSign);

        Assert.That(targets.Count, Is.EqualTo(4));
        Assert.Multiple(() => {
            for (int i = 0; i < Angles4.Length; i++) {
                var expected = ExpectedTarget(Angles4[i], RadiusMicrons, unitMicrons, curvatureSign);

                Assert.That(targets[i].TiltSteps, Is.EqualTo(expected.tiltSteps).Within(1e-9), $"screw {i} tilt steps");
                Assert.That(targets[i].BackfocusSteps, Is.EqualTo(expected.backSteps).Within(1e-9), $"screw {i} backfocus steps");
                Assert.That(targets[i].TotalSteps, Is.EqualTo(expected.totalSteps).Within(1e-9), $"screw {i} total steps");

                // Regression lock: the strings the pre-refactor inline formula would have formatted.
                string expectedTiltText = TiltAdapterGuidanceVM.FormatAmount(expected.tiltSteps, steps, TiltGuidanceAngleUnit.Turns);
                string expectedBackText = TiltAdapterGuidanceVM.FormatAmount(expected.backSteps, steps, TiltGuidanceAngleUnit.Turns);
                string expectedTotalText = TiltAdapterGuidanceVM.FormatAmount(expected.totalSteps, steps, TiltGuidanceAngleUnit.Turns);

                Assert.That(TiltAdapterGuidanceVM.FormatAmount(targets[i].TiltSteps, steps, TiltGuidanceAngleUnit.Turns), Is.EqualTo(expectedTiltText), $"screw {i} tilt text");
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(targets[i].BackfocusSteps, steps, TiltGuidanceAngleUnit.Turns), Is.EqualTo(expectedBackText), $"screw {i} backfocus text");
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(targets[i].TotalSteps, steps, TiltGuidanceAngleUnit.Turns), Is.EqualTo(expectedTotalText), $"screw {i} total text");
            }
        });
    }

    [Test]
    public void ComputePerScrewTargets_ThreeScrews_MatchesHandReplicatedInlineFormula() {
        const double unitMicrons = 400.0; // e.g. Neumann CTU XT48 thread pitch
        const int curvatureSign = -1;
        var targets = TiltScrewTargets.ComputePerScrewTargets(
            Gx, Gy, Kx, Ky, X0, Y0, Angles3, RadiusMicrons, unitMicrons, curvatureSign);

        Assert.That(targets.Count, Is.EqualTo(3));
        Assert.Multiple(() => {
            for (int i = 0; i < Angles3.Length; i++) {
                var expected = ExpectedTarget(Angles3[i], RadiusMicrons, unitMicrons, curvatureSign);
                Assert.That(targets[i].TiltSteps, Is.EqualTo(expected.tiltSteps).Within(1e-9), $"screw {i} tilt steps");
                Assert.That(targets[i].BackfocusSteps, Is.EqualTo(expected.backSteps).Within(1e-9), $"screw {i} backfocus steps");
                Assert.That(targets[i].TotalSteps, Is.EqualTo(expected.totalSteps).Within(1e-9), $"screw {i} total steps");
            }
        });
    }

    [Test]
    public void ComputePerScrewTargets_SigmaFlip_NegatesBackfocusOnly_TiltUnchanged() {
        const double unitMicrons = 1.8;
        var plus = TiltScrewTargets.ComputePerScrewTargets(Gx, Gy, Kx, Ky, X0, Y0, Angles4, RadiusMicrons, unitMicrons, curvatureSign: 1);
        var minus = TiltScrewTargets.ComputePerScrewTargets(Gx, Gy, Kx, Ky, X0, Y0, Angles4, RadiusMicrons, unitMicrons, curvatureSign: -1);

        Assert.Multiple(() => {
            for (int i = 0; i < Angles4.Length; i++) {
                // σ multiplies BACKFOCUS ONLY: the tilt component must be bit-for-bit identical.
                Assert.That(minus[i].TiltSteps, Is.EqualTo(plus[i].TiltSteps).Within(1e-12), $"screw {i} tilt unaffected by σ");
                // Flipping σ negates the backfocus component.
                Assert.That(minus[i].BackfocusSteps, Is.EqualTo(-plus[i].BackfocusSteps).Within(1e-9), $"screw {i} backfocus negates");
                // Total therefore shifts by 2·backfocus(σ=+1)/unit (tilt term is common, backfocus term flips sign).
                Assert.That(minus[i].TotalSteps, Is.EqualTo(plus[i].TotalSteps - 2.0 * plus[i].BackfocusSteps).Within(1e-9),
                    $"screw {i} total shifts by 2*backfocus/unit");
            }
        });
    }

    [Test]
    public void ComputePerScrewTargets_ZeroCurvatureSign_ResolvesToDefault() {
        const double unitMicrons = 1.8;
        var zero = TiltScrewTargets.ComputePerScrewTargets(Gx, Gy, Kx, Ky, X0, Y0, Angles4, RadiusMicrons, unitMicrons, curvatureSign: 0);
        var defaultSign = TiltScrewTargets.ComputePerScrewTargets(
            Gx, Gy, Kx, Ky, X0, Y0, Angles4, RadiusMicrons, unitMicrons, curvatureSign: TiltScrewGeometry.DefaultScrewInwardCurvatureSign);

        Assert.Multiple(() => {
            for (int i = 0; i < Angles4.Length; i++) {
                Assert.That(zero[i].TiltSteps, Is.EqualTo(defaultSign[i].TiltSteps).Within(1e-12));
                Assert.That(zero[i].BackfocusSteps, Is.EqualTo(defaultSign[i].BackfocusSteps).Within(1e-12));
                Assert.That(zero[i].TotalSteps, Is.EqualTo(defaultSign[i].TotalSteps).Within(1e-12));
            }
        });
    }

    [Test]
    public void ComputePerScrewTargets_TotalSign_MatchesWizardPlusConvention() {
        // Pure curvature (no tilt gradient), isotropic (Kx == Ky) and centered (X0 = Y0 = 0): every
        // screw sits at the same radius so BackfocusMicrons is identical at every angle, TiltSteps is
        // exactly 0, and TotalSteps == BackfocusSteps. This isolates the sign convention that
        // TiltAdapterGuidanceVM.FormatAmount renders as "+"/⟳ (signedAmount >= 0) vs "−"/⟲
        // (signedAmount < 0) -- the same "CW/+ positive" convention TiltScrewGeometry.SignedTotalAdjustment
        // documents and the wizard's StepInstructionsText "Turn screw N CLOCKWISE..." prompts use for a
        // positive applied amount.
        const double k = 1e-5;
        const double radius = 10000.0;
        const double unitMicrons = 1.8;

        var plus = TiltScrewTargets.ComputePerScrewTargets(0, 0, k, k, 0, 0, Angles4, radius, unitMicrons, curvatureSign: 1);
        var minus = TiltScrewTargets.ComputePerScrewTargets(0, 0, k, k, 0, 0, Angles4, radius, unitMicrons, curvatureSign: -1);

        double expectedBackfocusMicrons = -(k * radius * radius); // -(kx*cx^2 + ky*cy^2), cx^2+cy^2 == R^2 on every screw
        Assert.Multiple(() => {
            for (int i = 0; i < Angles4.Length; i++) {
                Assert.That(plus[i].TiltSteps, Is.EqualTo(0.0).Within(1e-12), $"screw {i}: no tilt gradient -> zero tilt steps");

                Assert.That(plus[i].TotalSteps, Is.EqualTo(expectedBackfocusMicrons / unitMicrons).Within(1e-9), $"screw {i} σ=+1 total");
                Assert.That(plus[i].TotalSteps, Is.LessThan(0.0), $"screw {i}: σ=+1 total must be negative here");
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(plus[i].TotalSteps, steps: true, TiltGuidanceAngleUnit.Turns), Does.StartWith("−"), $"screw {i} negative formats with a minus sign");

                Assert.That(minus[i].TotalSteps, Is.EqualTo(-expectedBackfocusMicrons / unitMicrons).Within(1e-9), $"screw {i} σ=-1 total");
                Assert.That(minus[i].TotalSteps, Is.GreaterThan(0.0), $"screw {i}: σ=-1 total must be positive here");
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(minus[i].TotalSteps, steps: true, TiltGuidanceAngleUnit.Turns), Does.StartWith("+"), $"screw {i} positive formats with a plus sign (wizard '+' convention)");
            }
        });
    }
}
