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
/// These cover the geometry decision without constructing a WPF <c>Window</c>: fast, and immune to whatever
/// makes a testhost flaky on a slow runner. They are NOT sufficient on their own — mutant M-F76 (leave
/// <c>SizeToContent</c> active, which is exactly what shipped) passes all of them. The assertion that actually
/// catches the defect lives in <c>ClampWindowToWorkAreaTests</c>, which also runs in CI.
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
    public void TheMeasuredFailingRectangle_IsRepositioned_EvenThoughItsHeightAlreadyFits() {
        // THE regression test for F76, built from the rect actually measured on the failing window:
        //     T=444  B=1836  height=1392   against a work area of 0..1392
        // The height is ALREADY correct -- the Win32 hook caps it before this code runs -- and the position is the
        // entire defect. The first version of the fix asked "is it too tall", declined here, and shipped doing
        // nothing. Ask "does it FIT" instead.
        var work = new Rect(0, 0, 3440, 1392);

        var clamped = ClampWindowToWorkArea.TryComputeWorkAreaClamp(
            height: 1392, top: 444, work: work, newHeight: out var h, newTop: out var t);

        Assert.Multiple(() => {
            Assert.That(clamped, Is.True, "a window whose BOTTOM is outside the work area must be corrected");
            Assert.That(h, Is.EqualTo(1392), "the height already fitted and must not be shrunk");
            Assert.That(t, Is.EqualTo(0), "the top must be pulled up so the footer lands on screen");
            Assert.That(t + h, Is.LessThanOrEqualTo(work.Bottom));
        });
    }

    [Test]
    public void EffectiveHeight_PrefersTheRenderedHeight_WhenTheHeightPropertyLags() {
        // THE measured defect, from the failing window's own log line:
        //     F76 clamp: h=492 actual=817 top=123 stc=WidthAndHeight work=752@0 => declined
        // ActualHeight (rendered, 817) had already overflowed the 752 work area while Height (requested) lagged at
        // 492. The shipped clamp read Height, saw 492 <= 752, and declined on every single call.
        Assert.That(ClampWindowToWorkArea.EffectiveHeight(actualHeight: 817, heightProperty: 492), Is.EqualTo(817),
            "the RENDERED height is what puts the footer off the bottom of the screen");
    }

    [Test]
    public void EffectiveHeight_HandlesTheUnsetHeightProperty() {
        // A SizeToContent window often leaves Height as NaN; NaN must not poison the comparison.
        Assert.Multiple(() => {
            Assert.That(ClampWindowToWorkArea.EffectiveHeight(817, double.NaN), Is.EqualTo(817));
            // A pending larger request still counts: it would overflow on the next layout pass.
            Assert.That(ClampWindowToWorkArea.EffectiveHeight(400, 900), Is.EqualTo(900));
        });
    }

    [Test]
    public void TheMeasuredOverflowingWindow_IsBroughtFullyOnScreen() {
        // End to end on the real numbers: rendered 817 at top 123 in a 1280x752 work area (150% DPI, the owner's
        // screen). Bottom lands at 940 against 752 -- 188 DIPs, ~282 physical px, off the bottom. That is the
        // screenshot.
        var work = new Rect(0, 0, 1280, 752);
        var h0 = ClampWindowToWorkArea.EffectiveHeight(actualHeight: 817, heightProperty: 492);

        var clamped = ClampWindowToWorkArea.TryComputeWorkAreaClamp(h0, top: 123, work: work, newHeight: out var h, newTop: out var t);

        Assert.Multiple(() => {
            Assert.That(clamped, Is.True);
            Assert.That(h, Is.EqualTo(752), "height capped to the work area");
            Assert.That(t, Is.EqualTo(0), "pulled up so the footer is on screen");
            Assert.That(t + h, Is.LessThanOrEqualTo(work.Bottom), "Accept must be inside the work area");
        });
    }

    // ---- ChooseWindowHeight: min(everything rendered, work area) ------------------------------------------

    [Test]
    public void ChooseWindowHeight_UsesTheContentHeightWhenItFits() {
        // Content smaller than the screen: show all of it, do not stretch to fill.
        Assert.That(ClampWindowToWorkArea.ChooseWindowHeight(contentDesiredHeight: 500, chromeHeight: 30, workAreaHeight: 752),
            Is.EqualTo(530));
    }

    [Test]
    public void ChooseWindowHeight_CapsAtTheWorkAreaWhenTheContentIsTaller() {
        // Content taller than the screen: fill the work area exactly and let the body scroll the remainder.
        Assert.That(ClampWindowToWorkArea.ChooseWindowHeight(contentDesiredHeight: 1200, chromeHeight: 30, workAreaHeight: 752),
            Is.EqualTo(752));
    }

    [Test]
    public void ChooseWindowHeight_TheOwnersCase_FillsTheAvailableHeightInsteadOfLeavingItEmpty() {
        // The reported symptom: the window settled ~730 physical px tall with ~400 px of screen unused AND a
        // scrollbar showing. A ScrollViewer reports whatever height it is offered, so SizeToContent converged on a
        // window shorter than the content wanted. Measured against infinity the content asks for more than the work
        // area, so the answer is to fill it.
        var chosen = ClampWindowToWorkArea.ChooseWindowHeight(contentDesiredHeight: 900, chromeHeight: 28, workAreaHeight: 752);
        Assert.Multiple(() => {
            Assert.That(chosen, Is.EqualTo(752), "fill the work area rather than leaving screen unused");
            Assert.That(chosen, Is.GreaterThan(486), "must be taller than the ~486 DIP window that was reported");
        });
    }

    [Test]
    public void ChooseWindowHeight_IncludesTheChrome_SoTheContentIsNotCutByTheTitleBar() {
        // Forgetting the chrome would size the window to the content and then steal the title bar's height from it.
        Assert.That(ClampWindowToWorkArea.ChooseWindowHeight(400, 40, 752), Is.EqualTo(440));
        Assert.That(ClampWindowToWorkArea.ChooseWindowHeight(400, 0, 752), Is.EqualTo(400));
    }

    [Test]
    public void ChooseWindowHeight_DeclinesOnUnmeasurableOrDegenerateInput() {
        Assert.Multiple(() => {
            Assert.That(ClampWindowToWorkArea.ChooseWindowHeight(double.NaN, 30, 752), Is.NaN, "unmeasured content");
            Assert.That(ClampWindowToWorkArea.ChooseWindowHeight(0, 30, 752), Is.NaN, "zero content is not a size");
            Assert.That(ClampWindowToWorkArea.ChooseWindowHeight(500, 30, 0), Is.NaN, "degenerate work area");
            // A negative chrome (ActualHeight briefly below the content's) must not shrink the target.
            Assert.That(ClampWindowToWorkArea.ChooseWindowHeight(500, -50, 752), Is.EqualTo(500));
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
