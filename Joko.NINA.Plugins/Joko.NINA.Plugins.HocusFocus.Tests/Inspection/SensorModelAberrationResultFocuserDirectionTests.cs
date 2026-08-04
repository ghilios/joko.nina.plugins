#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Inspection;
using NUnit.Framework;
using System.Linq;
using DrawingSize = System.Drawing.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection;

/// <summary>
/// The spacer advice is caption site 5 of docs/focuser-direction-convention-design.md §3: the only thing the
/// display-only focuser convention k may change here is the REMOVING/ADDING word. The acceptable verdict and
/// the reported curvature magnitude are pure z-space and must be identical either way.
/// </summary>
[TestFixture]
public class SensorModelAberrationResultFocuserDirectionTests {

    private static SensorModelAnalysisResult CurvatureRowFor(int focuserSign, double k) {
        var result = new SensorModelAberrationResult();
        // A 1000×1000 px sensor at 4 µm/px; k > 0 puts the outer field above the centre (E_z > 0).
        var model = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: 0, gy: 0, k: k);
        result.Update(
            sensorModel: model,
            imageSize: new DrawingSize(1000, 1000),
            pixelSizeMicrons: 4.0,
            fRatio: 5.0,
            focuserStepSizeMicrons: 1.0,
            finalFocusPosition: 0.0,
            registeredStars: [],
            acceptableRSquaredMin: 0.05,
            focuserSign: focuserSign);
        return result.AnalysisResults.Single(r => r.Name == "Curvature");
    }

    [Test]
    public void SpacerAdvice_FlipsWithTheFocuserDirection_ButTheVerdictDoesNot() {
        var standard = CurvatureRowFor(focuserSign: +1, k: 1e-5);
        var reversed = CurvatureRowFor(focuserSign: -1, k: 1e-5);

        Assert.Multiple(() => {
            // E_z > 0 on a standard focuser means the sensor sits too far from the flattener.
            Assert.That(standard.Details, Does.Contain("REMOVING"));
            Assert.That(standard.Details, Does.Not.Contain("ADDING"));
            // The same z-space measurement is the opposite physical situation on a reversed focuser.
            Assert.That(reversed.Details, Does.Contain("ADDING"));
            Assert.That(reversed.Details, Does.Not.Contain("REMOVING"));

            // Everything else about the row is k-free.
            Assert.That(reversed.Value, Is.EqualTo(standard.Value), "the curvature radius is a fitted quantity");
            Assert.That(reversed.Acceptable, Is.EqualTo(standard.Acceptable), "the verdict is a magnitude test");
        });
    }

    [Test]
    public void SpacerAdvice_DefaultsToTheStandardFocuser() {
        // Omitting the parameter must be identical to the standard convention, so every existing caller
        // renders exactly as it did before k existed.
        var result = new SensorModelAberrationResult();
        var model = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: 0, gy: 0, k: 1e-5);
        result.Update(
            sensorModel: model,
            imageSize: new DrawingSize(1000, 1000),
            pixelSizeMicrons: 4.0,
            fRatio: 5.0,
            focuserStepSizeMicrons: 1.0,
            finalFocusPosition: 0.0,
            registeredStars: [],
            acceptableRSquaredMin: 0.05);

        Assert.That(result.AnalysisResults.Single(r => r.Name == "Curvature").Details,
            Is.EqualTo(CurvatureRowFor(focuserSign: +1, k: 1e-5).Details));
    }

    [Test]
    public void SpacerAdvice_NegativeCurvature_ReadsTheOtherWayRound() {
        var standard = CurvatureRowFor(focuserSign: +1, k: -1e-5);
        var reversed = CurvatureRowFor(focuserSign: -1, k: -1e-5);

        Assert.Multiple(() => {
            Assert.That(standard.Details, Does.Contain("ADDING"));
            Assert.That(reversed.Details, Does.Contain("REMOVING"));
        });
    }
}
