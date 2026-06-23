#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NUnit.Framework;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Bank;

/// <summary>
/// Unit tests for the refined donut-aware decision. The cwhite dry-run showed the donut/peak fraction ALONE
/// over-flags mildly-defocused runs (cwhite frac≈1.53 but donuts marginal, and donut-aware loosened its sensor
/// fit). The fix: require BOTH a high donut/peak fraction AND heavy defocus on the extreme frames (high extreme
/// median HFR OR a large donut bbox) before setting donutAware=true.
/// </summary>
[TestFixture]
public class DonutHeuristicTests {

    private static DonutHeuristic.Signals Sig(double frac, double extremeHfr, double donutBboxPx) =>
        new DonutHeuristic.Signals { DonutPeakFracMax = frac, ExtremeFrameMedianHFR = extremeHfr, ExtremeDonutBBoxMedianPx = donutBboxPx };

    [Test]
    public void DonutAware_When_HighFrac_And_HeavyDefocusHfr() {
        var d = DonutHeuristic.Decide(Sig(DonutHeuristic.FracThreshold + 0.5, DonutHeuristic.HeavyDefocusHFR + 3.0, 0));
        Assert.That(d.DonutAware, Is.True);
    }

    [Test]
    public void DonutAware_When_HighFrac_And_LargeDonutBBox_EvenIfHfrModest() {
        var d = DonutHeuristic.Decide(Sig(DonutHeuristic.FracThreshold + 0.5, DonutHeuristic.HeavyDefocusHFR - 2.0, DonutHeuristic.LargeDonutBBoxPx + 6.0));
        Assert.That(d.DonutAware, Is.True);
    }

    [Test]
    public void NotDonutAware_When_HighFrac_But_MildDefocus_TheCwhiteOverflagFix() {
        // High frac but mild defocus (low extreme HFR, small donut bbox) — exactly the cwhite case that must NOT flag.
        var d = DonutHeuristic.Decide(Sig(1.53, DonutHeuristic.HeavyDefocusHFR - 3.0, DonutHeuristic.LargeDonutBBoxPx - 8.0));
        Assert.That(d.DonutAware, Is.False);
    }

    [Test]
    public void NotDonutAware_When_HeavyDefocus_But_LowFrac() {
        var d = DonutHeuristic.Decide(Sig(DonutHeuristic.FracThreshold - 0.3, DonutHeuristic.HeavyDefocusHFR + 5.0, DonutHeuristic.LargeDonutBBoxPx + 10.0));
        Assert.That(d.DonutAware, Is.False);
    }

    [Test]
    public void Decision_AlwaysHasReason() {
        Assert.That(DonutHeuristic.Decide(Sig(2.0, 14.0, 30.0)).Reason, Is.Not.Empty);
        Assert.That(DonutHeuristic.Decide(Sig(0.2, 3.0, 0.0)).Reason, Is.Not.Empty);
    }
}
