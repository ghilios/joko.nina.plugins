#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Collections.Generic;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

[TestFixture]
public class SaturatedHfrExclusionTests {

    private const double SatThreshold = 1.0;

    private static Star Unsaturated(double hfr) => new Star { HFR = hfr, Background = 0.1, PeakBrightness = 0.5 };  // 0.6 < 1.0
    private static Star Saturated(double hfr) => new Star { HFR = hfr, Background = 0.1, PeakBrightness = 0.95 };   // 1.05 >= 1.0

    private static List<Star> Stars(int unsaturated, int saturated) {
        var list = new List<Star>();
        for (var i = 0; i < unsaturated; i++) {
            list.Add(Unsaturated(2.0));
        }
        for (var i = 0; i < saturated; i++) {
            list.Add(Saturated(6.0)); // bloated, biased-high HFR
        }
        return list;
    }

    [Test]
    public void Disabled_ReturnsAllStars() {
        var stars = Stars(unsaturated: 5, saturated: 1);
        var kept = HocusFocusStarDetection.StarsForHfrAggregation(stars, excludeSaturated: false, SatThreshold);
        Assert.That(kept.Count, Is.EqualTo(6));
    }

    [Test]
    public void ExcludesSaturated_WhenEnoughUnsaturatedRemain() {
        var stars = Stars(unsaturated: 5, saturated: 1);
        var kept = HocusFocusStarDetection.StarsForHfrAggregation(stars, excludeSaturated: true, SatThreshold);
        Assert.Multiple(() => {
            Assert.That(kept.Count, Is.EqualTo(5), "the saturated star is dropped from the HFR set");
            Assert.That(kept.All(s => s.Background + s.PeakBrightness < SatThreshold), Is.True, "no saturated star remains");
        });
    }

    [Test]
    public void KeepsAll_WhenTooFewUnsaturatedRemain() {
        // Only 2 unsaturated (< MinUnsaturatedStarsForHfr = 3): keep everything so the frame still yields an HFR.
        var stars = Stars(unsaturated: 2, saturated: 3);
        var kept = HocusFocusStarDetection.StarsForHfrAggregation(stars, excludeSaturated: true, SatThreshold);
        Assert.That(kept.Count, Is.EqualTo(5));
    }

    [Test]
    public void NoSaturatedStars_KeepsAll() {
        // Bit-identity for the common case: nothing saturated => the set is unchanged.
        var stars = Stars(unsaturated: 8, saturated: 0);
        var kept = HocusFocusStarDetection.StarsForHfrAggregation(stars, excludeSaturated: true, SatThreshold);
        Assert.That(kept.Count, Is.EqualTo(8));
    }
}
