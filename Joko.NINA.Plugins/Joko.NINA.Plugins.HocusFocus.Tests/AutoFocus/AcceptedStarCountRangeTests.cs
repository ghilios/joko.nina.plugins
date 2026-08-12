#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Interfaces;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel.AutoFocus;
using NSubstitute;
using NUnit.Framework;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

/// <summary>
/// The AutoFocus panel's "Stars detected (curve)" row. Nothing else on the panel says how much data the
/// result rests on: an eleven-point sweep whose best point found 40 stars and one whose worst found 400
/// produce the same R² and the same σ(focus) row, and only one of them is worth trusting.
///
/// <para>The load-bearing claim is the word CURVE. The row must describe the points the fit actually used,
/// so points the fit threw away — Grubbs outliers and the symmetric-window exclusions — must not be able to
/// set either end of the range.</para>
/// </summary>
[TestFixture]
public class AcceptedStarCountRangeTests {

    private static HocusFocusVM Build() {
        var focuserMediator = Substitute.For<IFocuserMediator>();
        focuserMediator.GetInfo().Returns(new FocuserInfo());
        return new HocusFocusVM(
            profileService: Substitute.For<IProfileService>(),
            focuserMediator: focuserMediator,
            autoFocusEngineFactory: Substitute.For<IAutoFocusEngineFactory>(),
            autoFocusOptions: Substitute.For<IAutoFocusOptions>(),
            starDetectionOptions: Substitute.For<IStarDetectionOptions>(),
            filterWheelMediator: Substitute.For<IFilterWheelMediator>(),
            applicationStatusMediator: Substitute.For<IApplicationStatusMediator>(),
            starDetectionSelector: Substitute.For<IPluggableBehaviorSelector<IStarDetection>>(),
            alglibAPI: new AlglibAPI(),
            applicationDispatcher: new SynchronousApplicationDispatcher());
    }

    private static StarDetectionResult Detection(int stars) => new StarDetectionResult { DetectedStars = stars };

    private static AutoFocusRegionPoint[] Points(params int[] focuserPositions) =>
        focuserPositions
            .Select(p => new AutoFocusRegionPoint { FocuserPosition = p, Measurement = new MeasureAndError { Measure = 3.0, Stdev = 0.1 } })
            .ToArray();

    /// <summary>Feeds one frame per position, in the order given, starting at focuser 1000 step 100.</summary>
    private static void Sweep(HocusFocusVM vm, params int[] starCounts) {
        for (var i = 0; i < starCounts.Length; i++) {
            vm.RecordFrameStarCount(1000 + i * 100, Detection(starCounts[i]));
        }
    }

    [Test]
    public void Range_IsTheMinAndMaxOverEveryMeasuredPoint_WhenNothingWasDiscarded() {
        var vm = Build();
        Sweep(vm, 412, 980, 1067, 733);

        vm.UpdateAcceptedStarCountRange(rejectedPoints: null, windowExcludedPoints: null);

        Assert.Multiple(() => {
            Assert.That(vm.AcceptedStarCountMin, Is.EqualTo(412));
            Assert.That(vm.AcceptedStarCountMax, Is.EqualTo(1067));
            Assert.That(vm.HasAcceptedStarCountRange, Is.True);
        });
    }

    [Test]
    public void RejectedPoint_CannotSetEitherEndOfTheRange() {
        // THE POINT OF THE ROW. A Grubbs-rejected wing is exactly where the star count is lowest, so a row
        // computed over all measured points would report the sweep as thinner than the curve actually was.
        // DISCRIMINATING: drop the rejected-set filter and the minimum becomes 12.
        var vm = Build();
        Sweep(vm, 12, 980, 1067, 733);

        vm.UpdateAcceptedStarCountRange(rejectedPoints: Points(1000), windowExcludedPoints: null);

        Assert.Multiple(() => {
            Assert.That(vm.AcceptedStarCountMin, Is.EqualTo(733));
            Assert.That(vm.AcceptedStarCountMax, Is.EqualTo(1067));
        });
    }

    [Test]
    public void WindowExcludedPoint_CannotSetEitherEndOfTheRange() {
        // The symmetric-window exclusions are decided only at finalization and are a SEPARATE set from the
        // Grubbs rejections; a filter that honoured only the latter would still report far-wing counts.
        // DISCRIMINATING: drop the window-excluded filter and the maximum becomes 4000.
        var vm = Build();
        Sweep(vm, 412, 980, 1067, 4000);

        vm.UpdateAcceptedStarCountRange(rejectedPoints: null, windowExcludedPoints: Points(1300));

        Assert.Multiple(() => {
            Assert.That(vm.AcceptedStarCountMin, Is.EqualTo(412));
            Assert.That(vm.AcceptedStarCountMax, Is.EqualTo(1067));
        });
    }

    [Test]
    public void FramesPerPointGreaterThanOne_PoolsThatPositionByMean() {
        // The fit sees ONE point per focuser position (TryCompleteFocuserPoint averages the sub-measurements),
        // so the row must too. DISCRIMINATING against treating each frame as its own point, which would report
        // 100 – 900 rather than the 500 the curve was fitted on.
        var vm = Build();
        vm.RecordFrameStarCount(1000, Detection(100));
        vm.RecordFrameStarCount(1000, Detection(500));
        vm.RecordFrameStarCount(1000, Detection(900));
        vm.RecordFrameStarCount(1100, Detection(700));

        vm.UpdateAcceptedStarCountRange(null, null);

        Assert.Multiple(() => {
            Assert.That(vm.AcceptedStarCountMin, Is.EqualTo(500));
            Assert.That(vm.AcceptedStarCountMax, Is.EqualTo(700));
        });
    }

    [Test]
    public void EveryPointDiscarded_CollapsesTheRowRatherThanReportingZero() {
        // GUARD. 0 – 0 reads as "the run found no stars", which is a different and alarming claim from "this
        // is not known". The row is hidden instead.
        var vm = Build();
        Sweep(vm, 412, 980);

        vm.UpdateAcceptedStarCountRange(rejectedPoints: Points(1000, 1100), windowExcludedPoints: null);

        Assert.Multiple(() => {
            Assert.That(vm.HasAcceptedStarCountRange, Is.False);
            Assert.That(vm.AcceptedStarCountMin, Is.EqualTo(-1));
            Assert.That(vm.AcceptedStarCountRangeText, Is.Empty);
        });
    }

    [Test]
    public void NoStarDetectionBehindTheMeasurement_LeavesTheRowCollapsed() {
        // GUARD: contrast-detection AF produces measurements with no StarDetectionResult at all. Counting
        // those as zero-star points would put a fabricated "0" on the panel for every contrast run.
        var vm = Build();
        vm.RecordFrameStarCount(1000, null);
        vm.RecordFrameStarCount(1100, null);

        vm.UpdateAcceptedStarCountRange(null, null);

        Assert.That(vm.HasAcceptedStarCountRange, Is.False);
    }

    [Test]
    public void RangeText_RendersASinglePointAsOneNumber_AndASpanAsAnEnDashPair() {
        var single = Build();
        single.RecordFrameStarCount(1000, Detection(1234));
        single.UpdateAcceptedStarCountRange(null, null);

        var span = Build();
        Sweep(span, 412, 1067);
        span.UpdateAcceptedStarCountRange(null, null);

        Assert.Multiple(() => {
            Assert.That(single.AcceptedStarCountRangeText, Does.Not.Contain("–"),
                "a degenerate range must not be rendered as \"1,234 – 1,234\"");
            Assert.That(single.AcceptedStarCountRangeText, Does.Contain("1"));
            Assert.That(span.AcceptedStarCountRangeText, Does.Contain("–"));
        });
    }

    [Test]
    public void Report_CarriesTheRange_AndAnUnrecordedRangeRoundTripsAsTheCollapsedSentinel() {
        // The row has to survive a chart reload, and core's LoadChart hands the plugin nothing but a
        // timestamp — so the report is the only place it can come from.
        var withRange = HocusFocusReport.GenerateReport(
            Substitute.For<IProfileService>(), Substitute.For<IStarDetection>(),
            new System.Collections.Generic.List<OxyPlot.Series.ScatterErrorPoint>(),
            initialFocusPosition: 1000, initialHFR: 4.0, finalHFR: 2.0,
            focusPoint: new OxyPlot.DataPoint(1000, 2.0), fittings: new AutoFocusFitting(),
            lastFocusPoint: null, temperature: 10.0, filter: "L",
            region: StarDetectionRegion.Full, hocusFocusStarDetectionOptions: null,
            hocusFocusAutoFocusOptions: null, duration: System.TimeSpan.FromMinutes(1),
            acceptedStarCountMin: 412, acceptedStarCountMax: 1067);

        var withoutRange = HocusFocusReport.GenerateReport(
            Substitute.For<IProfileService>(), Substitute.For<IStarDetection>(),
            new System.Collections.Generic.List<OxyPlot.Series.ScatterErrorPoint>(),
            initialFocusPosition: 1000, initialHFR: 4.0, finalHFR: 2.0,
            focusPoint: new OxyPlot.DataPoint(1000, 2.0), fittings: new AutoFocusFitting(),
            lastFocusPoint: null, temperature: 10.0, filter: "L",
            region: StarDetectionRegion.Full, hocusFocusStarDetectionOptions: null,
            hocusFocusAutoFocusOptions: null, duration: System.TimeSpan.FromMinutes(1));

        Assert.Multiple(() => {
            Assert.That(withRange.AcceptedStarCountMin, Is.EqualTo(412));
            Assert.That(withRange.AcceptedStarCountMax, Is.EqualTo(1067));
            // DISCRIMINATING against defaulting to 0, which the panel cannot tell from a measured zero.
            Assert.That(withoutRange.AcceptedStarCountMin, Is.EqualTo(-1));
            Assert.That(withoutRange.AcceptedStarCountMax, Is.EqualTo(-1));
        });
    }
}
