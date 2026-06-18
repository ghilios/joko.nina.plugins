#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using NINA.Image.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// The wizard-side loader is heavily NINA-coupled (image factory + imaging mediator + real star detection),
/// so its end-to-end path is exercised in T6 against the user's real AF run folder. Here we only verify it
/// constructs and rejects null collaborators, so a wiring mistake is caught early.
/// </summary>
[TestFixture]
public class RunEvaluationLoaderTests {

    [Test]
    public void Constructor_WithAllCollaborators_DoesNotThrow() {
        Assert.DoesNotThrow(() => new RunEvaluationLoader(
            Substitute.For<IProfileService>(),
            Substitute.For<IImageDataFactory>(),
            Substitute.For<IImagingMediator>(),
            Substitute.For<IAutoFocusEngine>(),
            Substitute.For<IHocusFocusStarDetection>()));
    }

    [Test]
    public void Constructor_NullCollaborator_Throws() {
        Assert.Throws<ArgumentNullException>(() => new RunEvaluationLoader(
            null,
            Substitute.For<IImageDataFactory>(),
            Substitute.For<IImagingMediator>(),
            Substitute.For<IAutoFocusEngine>(),
            Substitute.For<IHocusFocusStarDetection>()));
    }

    [Test]
    public void LoadedRun_Baseline_FallsBackToSeed_WhenNotSet() {
        var seed = new StarDetectorParams { Sensitivity = 3.0 };
        var run = new LoadedRun { Seed = seed };
        Assert.That(run.Baseline, Is.SameAs(seed), "Baseline defaults to Seed when not provided");

        var baseline = new StarDetectorParams { Sensitivity = 9.0 };
        run.Baseline = baseline;
        Assert.Multiple(() => {
            Assert.That(run.Baseline, Is.SameAs(baseline), "an explicitly set Baseline is independent of Seed");
            Assert.That(run.Seed, Is.SameAs(seed), "setting Baseline does not change Seed");
        });
    }
}
