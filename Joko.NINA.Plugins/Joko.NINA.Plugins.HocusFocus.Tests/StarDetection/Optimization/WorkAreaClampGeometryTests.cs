#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Windows;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// F76, CI-side. The optimizer wizard opened taller than the screen, putting its footer — Back / Review frames /
/// Continue optimizing / <b>Accept</b> / Close — off the bottom, so a completed run could not be applied at all
/// except via the title-bar X, which discards it.
///
/// These cover the geometry decision without constructing a WPF <c>Window</c>, because a Window-constructing
/// <c>[Apartment(STA)]</c> fixture makes the CI testhost fail to connect for the whole assembly. The Window-level
/// adapter — above all the assertion that <c>SizeToContent</c> is switched to Manual, which is the actual defect —
/// lives in <c>ClampWindowToWorkAreaTests</c> and is <c>[Explicit]</c>, so it runs on demand and NOT here. That
/// split is recorded rather than hidden: the single most important assertion about this fix is not enforced by CI.
/// </summary>
[TestFixture]
public class WorkAreaClampGeometryTests {

    // The owner's repro condition: a screen small enough that the wizard's summary greatly exceeds it. A
    // 3440x1440 monitor masks the bug, which is why it survived a full session of testing on one.
    private static readonly Rect SmallWorkArea = new Rect(0, 0, 1920, 1000);

    [Test]
    public void TallerThanWorkArea_ClampsHeightAndPullsTheWindowBackInside() {
        var clamped = ClampWindowToWorkArea.TryComputeWorkAreaClamp(
            height: 2000, top: 444, work: SmallWorkArea, newHeight: out var h, newTop: out var t);

        Assert.Multiple(() => {
            Assert.That(clamped, Is.True, "a window taller than the work area must be clamped");
            Assert.That(h, Is.EqualTo(SmallWorkArea.Height), "height must be capped at the work area");
            Assert.That(t, Is.EqualTo(SmallWorkArea.Top), "the window must be moved, not merely resized");
            // The property a user actually feels: the footer, and therefore Accept, is on screen.
            Assert.That(t + h, Is.LessThanOrEqualTo(SmallWorkArea.Bottom),
                "the bottom of the window -- where Accept lives -- must land inside the work area");
        });
    }

    [Test]
    public void ShorterThanWorkArea_IsLeftAlone() {
        var clamped = ClampWindowToWorkArea.TryComputeWorkAreaClamp(
            height: 500, top: 100, work: SmallWorkArea, newHeight: out var h, newTop: out var t);

        Assert.Multiple(() => {
            Assert.That(clamped, Is.False, "a window that fits must not be touched");
            Assert.That(h, Is.EqualTo(500));
            Assert.That(t, Is.EqualTo(100));
        });
    }

    [Test]
    public void ExactlyTheWorkAreaHeight_Fits_AndIsNotClamped() {
        // Guards the comparison direction. A sign error here would clamp every window on a large monitor and
        // permanently disable SizeToContent for users who never had the problem. 1392 is the height the
        // controller measured on a 3440x1440 display, where the window happened to land exactly on the boundary.
        var large = new Rect(0, 0, 3440, 1392);

        var clamped = ClampWindowToWorkArea.TryComputeWorkAreaClamp(
            height: 1392, top: 0, work: large, newHeight: out _, newTop: out _);

        Assert.That(clamped, Is.False, "a window exactly the height of the work area fits");
    }

    [Test]
    public void ClampingIsIdempotent_SoTheSizeChangedHandlerCannotRecurse() {
        ClampWindowToWorkArea.TryComputeWorkAreaClamp(2000, 444, SmallWorkArea, out var h1, out var t1);
        // ApplyWorkAreaLimit runs from SizeChanged, and setting Height raises SizeChanged again. The second pass
        // must decline, or the handler recurses.
        var again = ClampWindowToWorkArea.TryComputeWorkAreaClamp(h1, t1, SmallWorkArea, out _, out _);

        Assert.That(again, Is.False, "the second pass must decline, otherwise the SizeChanged handler recurses");
    }

    [Test]
    public void AlreadyInsideButTooTall_KeepsItsTopWhenTheClampedHeightStillFits() {
        // Height must be capped without needlessly teleporting a window the user has positioned deliberately.
        var clamped = ClampWindowToWorkArea.TryComputeWorkAreaClamp(
            height: 1200, top: 0, work: SmallWorkArea, newHeight: out var h, newTop: out var t);

        Assert.Multiple(() => {
            Assert.That(clamped, Is.True);
            Assert.That(h, Is.EqualTo(1000));
            Assert.That(t, Is.EqualTo(0), "top 0 already satisfies top + clamped height <= work.Bottom");
        });
    }

    [Test]
    public void ADegenerateWorkArea_IsDeclined_RatherThanCollapsingTheWindow() {
        // A zero-height work area has been observed from monitor APIs during display changes. Resizing a window
        // to zero would be worse than leaving it alone.
        var clamped = ClampWindowToWorkArea.TryComputeWorkAreaClamp(
            height: 2000, top: 100, work: new Rect(0, 0, 1920, 0), newHeight: out var h, newTop: out var t);

        Assert.Multiple(() => {
            Assert.That(clamped, Is.False, "a degenerate work area must be declined");
            Assert.That(h, Is.EqualTo(2000), "the window must be left untouched");
            Assert.That(t, Is.EqualTo(100));
        });
    }
}
