#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;
using System;
using System.Linq;
using System.Reflection;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

/// <summary>
/// docs/focuser-direction-convention-design.md §2.2, structural enforcement mechanism 1 — stated as a test
/// so it cannot erode quietly.
///
/// The display-only focuser convention k lives on IInspectorOptions. The pure math layer takes no options
/// object and must gain no k parameter on any computing function; the camera simulator reads only
/// ICameraSimulatorOptions. Any future attempt to thread k into one of those signatures fails here — which
/// is the whole point, because the design's guarantee ("a wrong k gives wrong labels, never wrong motion")
/// rests on it.
/// </summary>
[TestFixture]
public class FocuserDirectionFirewallTests {

    private static readonly Type[] MathLayerTypes = {
        typeof(TiltCalibrationCalculator),
        typeof(TiltScrewGeometry),
        typeof(TiltScrewTargets),
        typeof(SimulatedTiltInjection),
        typeof(SimulatedTiltAdapter),
    };

    [TestCaseSource(nameof(MathLayerTypes))]
    public void MathLayer_TakesNoInspectorOptions(Type type) {
        var offenders = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p => typeof(IInspectorOptions).IsAssignableFrom(p.ParameterType)))
            .Select(m => m.Name)
            .ToArray();

        Assert.That(offenders, Is.Empty,
            $"{type.Name} must not accept IInspectorOptions — the display-only focuser setting k lives there, " +
            "and the math layer is the one place it must never reach. Compose σ·sign(k) at the presentation " +
            "call site instead (design §2.2).");
    }

    [Test]
    public void PlannerTargetComputation_TakesOnlyTheModelAndTheAdapterOptions() {
        // Automation's target computation. It reads the fitted model and the adapter's own options and
        // nothing else, so there is no parameter through which k could reach a planned move. Widening this
        // signature is exactly the change the design forbids.
        var method = typeof(InspectorVM).GetMethod(
            nameof(InspectorVM.BuildPerScrewTargets), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "BuildPerScrewTargets moved or was renamed — re-point this guard at it");
        Assert.That(method.GetParameters().Select(p => p.ParameterType).ToArray(),
            Is.EqualTo(new[] { typeof(SensorParaboloidModel), typeof(ITiltAdapterOptions) }));
    }

    [Test]
    public void SimulatedTiltInjection_FoldTakesOnlyTheSimulatorOptionsAndTheDelta() {
        // The simulator is on the math side of the wall (design §4): the focuser convention cancels out of
        // both responses it folds, so it must not gain a knob.
        var method = typeof(SimulatedTiltInjection).GetMethod(
            nameof(SimulatedTiltInjection.Fold), BindingFlags.Public | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        Assert.That(method.GetParameters().Select(p => p.ParameterType).ToArray(),
            Is.EqualTo(new[] { typeof(ICameraSimulatorOptions), typeof(AberrationDelta), typeof(int) }));
    }
}
