#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.ImageData;
using NUnit.Framework;
using System.Collections.Generic;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// The harness settings store. These cover the two properties the store exists to guarantee — that a run's
/// settings come from a file rather than mutable profile state, and that PixelScale comes from the DATA rather
/// than from whichever rig the local profile happens to describe.
/// </summary>
[TestFixture]
public class HarnessSettingsStoreTests {

    private static HarnessSettingsStore.FileOptionsAccessor Accessor(params (string Key, string Value)[] pairs) {
        var bag = new Dictionary<string, string>();
        foreach (var (k, v) in pairs) {
            bag[k] = v;
        }
        return new HarnessSettingsStore.FileOptionsAccessor(bag);
    }

    // ---- The accessor -------------------------------------------------------------------------------------

    [Test]
    public void Accessor_ReadsTypedValuesBackFromTheFileBag() {
        var a = Accessor(("BrightnessSensitivity", "33.3333"), ("StructureLayers", "5"),
                         ("DefocusAwareDonutDetection", "True"), ("MeasurementAverage", "Median"));
        Assert.Multiple(() => {
            Assert.That(a.GetValueDouble("BrightnessSensitivity", 10.0), Is.EqualTo(33.3333).Within(1e-9));
            Assert.That(a.GetValueInt32("StructureLayers", 4), Is.EqualTo(5));
            Assert.That(a.GetValueBoolean("DefocusAwareDonutDetection", false), Is.True);
        });
    }

    [Test]
    public void Accessor_FallsBackToTheDefault_ForAMissingOrUnparseableKey() {
        // A harness must never throw on a key it does not care about, and a corrupt value must not be silently
        // read as some other number — both degrade to the caller's stated default.
        var a = Accessor(("BrightnessSensitivity", "not-a-number"));
        Assert.Multiple(() => {
            Assert.That(a.GetValueDouble("BrightnessSensitivity", 10.0), Is.EqualTo(10.0));
            Assert.That(a.GetValueDouble("NeverWritten", 7.5), Is.EqualTo(7.5));
        });
    }

    [Test]
    public void Accessor_WritesStayInTheBag_AndSuppressWritesBlocksThem() {
        // Writes must never reach the user's profile as a side effect of a harness run.
        var bag = new Dictionary<string, string>();
        var a = new HarnessSettingsStore.FileOptionsAccessor(bag);
        a.SetValueDouble("BrightnessSensitivity", 12.5);
        Assert.That(bag["BrightnessSensitivity"], Is.EqualTo("12.5"));

        a.SuppressWrites = true;
        a.SetValueDouble("BrightnessSensitivity", 99.0);
        Assert.That(bag["BrightnessSensitivity"], Is.EqualTo("12.5"), "SuppressWrites must block the write");
    }

    [Test]
    public void Accessor_UsesInvariantCulture() {
        // A machine with a comma decimal separator must not read 33.3333 as 333333.
        var a = Accessor(("BrightnessSensitivity", "33.3333"));
        Assert.That(a.GetValueDouble("BrightnessSensitivity", 0.0), Is.EqualTo(33.3333).Within(1e-9));
    }

    // ---- PixelScale from the frame, not the profile -------------------------------------------------------

    private static ImageMetaData Meta(double pixelSize, double focalLength, int binX = 1) {
        var m = new ImageMetaData();
        m.Camera.PixelSize = pixelSize;
        m.Camera.BinX = binX;
        m.Telescope.FocalLength = focalLength;
        return m;
    }

    [Test]
    public void PixelScale_ComesFromTheFrameHeader_NotTheSettingsFile() {
        // The measured 3800mm case: the frame says 3.76um / 3800mm = 0.204 arcsec/px, while the local profile
        // described a 703mm rig at 1.10. Using the profile value under-reported the scale by 5x on this run, and
        // across the bank the true scales span 0.74-5.97 arcsec/px.
        var settings = new HarnessSettingsStore.Resolved { PixelSizeMicrons = 3.76, FocalLengthMm = 703.0, Path = "f.json" };
        var scale = HarnessSettingsStore.PixelScaleForFrame(Meta(3.76, 3800.0), settings, out var source);
        Assert.Multiple(() => {
            Assert.That(scale, Is.EqualTo(0.2041).Within(0.001));
            Assert.That(source, Is.EqualTo("frame header"));
        });
    }

    [Test]
    public void PixelScale_ScalesWithBinning() {
        var scale = HarnessSettingsStore.PixelScaleForFrame(Meta(3.76, 3800.0, binX: 2), null, out _);
        Assert.That(scale, Is.EqualTo(0.4082).Within(0.001));
    }

    [Test]
    public void PixelScale_FallsBackToTheSettingsFile_WhenTheHeaderHasNone() {
        var settings = new HarnessSettingsStore.Resolved { PixelSizeMicrons = 3.76, FocalLengthMm = 585.0, Path = "f.json" };
        var scale = HarnessSettingsStore.PixelScaleForFrame(Meta(double.NaN, double.NaN), settings, out var source);
        Assert.Multiple(() => {
            Assert.That(scale, Is.EqualTo(1.3257).Within(0.001));
            Assert.That(source, Does.Contain("settings file"));
        });
    }

    [Test]
    public void PixelScale_IsNaN_WhenNeitherSourceHasIt_RatherThanFabricated() {
        // A frame with no scale information must not be handed an invented one: NaN is what the previous
        // profile path produced too, and PixelScale-dependent gates already handle it.
        var scale = HarnessSettingsStore.PixelScaleForFrame(Meta(double.NaN, double.NaN), null, out var source);
        Assert.Multiple(() => {
            Assert.That(double.IsNaN(scale), Is.True);
            Assert.That(source, Does.Contain("unavailable"));
        });
    }
}
