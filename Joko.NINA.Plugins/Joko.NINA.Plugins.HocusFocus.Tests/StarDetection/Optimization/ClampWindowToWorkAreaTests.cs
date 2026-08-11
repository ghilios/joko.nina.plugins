#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Threading;
using System.Windows;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// F76. The optimizer wizard opened taller than the screen, putting its footer — Back / Review frames /
/// Continue optimizing / <b>Accept</b> / Close — off the bottom, with no way to apply a completed run except
/// the title-bar X, which discards it.
///
/// The cause was NOT a missing ScrollViewer and NOT a missing y-clamp; both were already shipped. It was that
/// <see cref="ClampWindowToWorkArea"/> clamped only at the Win32 level while <c>SizeToContent</c> stayed
/// ACTIVE, so WPF recomputed the content height on the next layout pass and overwrote the clamp. The tell was
/// that resizing the window by hand fixed it — WPF sets <c>SizeToContent = Manual</c> automatically when the
/// USER resizes.
///
/// These tests assert the PLACED RECTANGLE and the SizeToContent state against a simulated work area. No
/// ViewModel test can reach this: all 180 StarDetectionOptimizerWizardVMTests passed while the defect shipped,
/// correctly, because the ViewModel and the XAML were both fine.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public class ClampWindowToWorkAreaTests {

    // A screen small enough that the wizard's summary greatly exceeds it — the owner's repro condition. The
    // controller's 3440x1440 monitor masked the bug because the window happened to land at exactly the work
    // area height there.
    private static readonly Rect SmallWorkArea = new Rect(0, 0, 1920, 1000);

    [Test]
    public void TallerThanWorkArea_TurnsSizeToContentOff_AndPinsHeightAndTop() {
        var window = new Window {
            SizeToContent = SizeToContent.WidthAndHeight,
            Top = 444,
            Height = 2000,
        };

        var clamped = ClampWindowToWorkArea.ApplyWorkAreaLimit(window, SmallWorkArea);

        Assert.Multiple(() => {
            Assert.That(clamped, Is.True, "a window taller than the work area must be clamped");
            // The heart of F76: leaving SizeToContent on is what let WPF re-grow the window over the Win32 clamp.
            Assert.That(window.SizeToContent, Is.EqualTo(SizeToContent.Manual),
                "SizeToContent must be turned OFF, or WPF re-asserts the content height on the next layout pass");
            Assert.That(window.Height, Is.EqualTo(SmallWorkArea.Height),
                "the window must not be taller than the work area");
            Assert.That(window.Top, Is.EqualTo(SmallWorkArea.Top),
                "the window must be moved back inside the work area, not merely resized");
            // The property that actually matters to a user: the footer is on screen.
            Assert.That(window.Top + window.Height, Is.LessThanOrEqualTo(SmallWorkArea.Bottom),
                "the bottom of the window -- where the Accept button lives -- must be inside the work area");
        });
    }

    [Test]
    public void ShorterThanWorkArea_LeavesTheWindowAlone() {
        var window = new Window {
            SizeToContent = SizeToContent.WidthAndHeight,
            Top = 100,
            Height = 500,
        };

        var clamped = ClampWindowToWorkArea.ApplyWorkAreaLimit(window, SmallWorkArea);

        Assert.Multiple(() => {
            Assert.That(clamped, Is.False, "a window that fits must not be touched");
            // Ordinary per-step growth must keep working on a screen with room for it.
            Assert.That(window.SizeToContent, Is.EqualTo(SizeToContent.WidthAndHeight),
                "SizeToContent must survive when the window fits, or the wizard stops sizing itself to each step");
            Assert.That(window.Height, Is.EqualTo(500));
            Assert.That(window.Top, Is.EqualTo(100));
        });
    }

    [Test]
    public void ClampingIsIdempotent_SoTheSizeChangedHandlerCannotLoop() {
        var window = new Window {
            SizeToContent = SizeToContent.WidthAndHeight,
            Top = 444,
            Height = 2000,
        };

        Assert.That(ClampWindowToWorkArea.ApplyWorkAreaLimit(window, SmallWorkArea), Is.True, "first pass clamps");
        // ApplyWorkAreaLimit runs from SizeChanged, and setting Height raises SizeChanged again. The second pass
        // must be a no-op or the handler recurses.
        Assert.That(ClampWindowToWorkArea.ApplyWorkAreaLimit(window, SmallWorkArea), Is.False,
            "second pass must be a no-op, otherwise the SizeChanged handler recurses");
    }

    [Test]
    public void AWorkAreaTallerThanTheWindow_IsNotTreatedAsAClamp() {
        // Guards the comparison direction: an easy sign error here would clamp every window on a large monitor
        // and permanently disable SizeToContent for users who never had the problem.
        var window = new Window {
            SizeToContent = SizeToContent.WidthAndHeight,
            Top = 0,
            Height = 1392,
        };

        var large = new Rect(0, 0, 3440, 1392);

        Assert.That(ClampWindowToWorkArea.ApplyWorkAreaLimit(window, large), Is.False,
            "a window exactly the height of the work area fits and must not be clamped");
        Assert.That(window.SizeToContent, Is.EqualTo(SizeToContent.WidthAndHeight));
    }
}
