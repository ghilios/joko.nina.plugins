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
/// Unit tests for the pure-logic bank cleanup classifier (linked from TestApp). The classifier is CONSERVATIVE:
/// it deletes only files matching a known stale-artifact denylist (regenerated per config) and KEEPS everything
/// else — AF sweep frames, autofocus_report_Region*.json (run config + region geometry + step size), the labels/
/// subtree, our *.golden.json reference sidecars, and run_meta.json — so re-running verification is non-destructive.
/// </summary>
[TestFixture]
public class BankCleanupTests {

    [Test]
    public void Keep_AutoFocusSweepFrame() {
        var d = BankCleanup.Classify("01_Frame00_BitDepth16_Bayered0_Focuser48645.fits");
        Assert.That(d.Keep, Is.True);
    }

    [Test]
    public void Keep_AutoFocusReportRegionJson() {
        var d = BankCleanup.Classify("autofocus_report_Region0.json");
        Assert.That(d.Keep, Is.True);
    }

    [Test]
    public void Keep_LabelsSubtree() {
        var d = BankCleanup.Classify("labels/3f2a-run.json");
        Assert.That(d.Keep, Is.True);
    }

    [Test]
    public void Keep_GoldenSidecar() {
        var d = BankCleanup.Classify("01_Frame00_BitDepth16_Bayered0_Focuser48645.fits.golden.json");
        Assert.That(d.Keep, Is.True);
    }

    [Test]
    public void Keep_RunMeta() {
        var d = BankCleanup.Classify("run_meta.json");
        Assert.That(d.Keep, Is.True);
    }

    [Test]
    public void Delete_StarDetectionResultJson() {
        var d = BankCleanup.Classify("01_Frame00_Region00_star_detection_result.json");
        Assert.That(d.Keep, Is.False);
        Assert.That(d.Reason, Is.Not.Empty);
    }

    [Test]
    public void Delete_OptimizedSettingsHandoff() {
        var d = BankCleanup.Classify("optimized_settings.json");
        Assert.That(d.Keep, Is.False);
    }

    [Test]
    public void Delete_DiagnosticPng() {
        var d = BankCleanup.Classify("optimize_extremes_Focuser5000_annotated.png");
        Assert.That(d.Keep, Is.False);
    }

    [Test]
    public void Delete_StrayOptimizeOutputs() {
        Assert.Multiple(() => {
            Assert.That(BankCleanup.Classify("optimize_summary.txt").Keep, Is.False);
            Assert.That(BankCleanup.Classify("optimize_result.csv").Keep, Is.False);
            Assert.That(BankCleanup.Classify("optimize_trajectory.csv").Keep, Is.False);
            Assert.That(BankCleanup.Classify("aggregate_summary.txt").Keep, Is.False);
        });
    }

    [Test]
    public void Delete_StrayGoldenEvalAndContaminationOutputs() {
        Assert.Multiple(() => {
            Assert.That(BankCleanup.Classify("golden_eval.txt").Keep, Is.False);
            Assert.That(BankCleanup.Classify("golden_eval_frames.csv").Keep, Is.False);
            Assert.That(BankCleanup.Classify("detected_f5000.csv").Keep, Is.False);
            Assert.That(BankCleanup.Classify("contamination_stars.csv").Keep, Is.False);
            Assert.That(BankCleanup.Classify("inspect_align.txt").Keep, Is.False);
        });
    }

    [Test]
    public void Keep_UnknownFileIsConservativelyRetained() {
        // Anything not on the denylist is KEPT — bank-clean must never delete data it doesn't recognize.
        var d = BankCleanup.Classify("some_user_notes.txt");
        Assert.That(d.Keep, Is.True);
    }

    [Test]
    public void Keep_LinearExportSidecar() {
        // export-linear's mono-FITS sidecars feed snr_ref; they are derived but cheap to keep and not clutter.
        var d = BankCleanup.Classify("01_Frame00_BitDepth16_Bayered0_Focuser48645.linear.fits");
        Assert.That(d.Keep, Is.True);
    }
}
