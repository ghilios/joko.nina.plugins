using NUnit.Framework;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Harness;

/// <summary>
/// The headless tilt validator's pixel-pitch decision. The stakes are the whole point: this number is only
/// ever multiplied by a frame DIMENSION to get the sensor's physical extent, so getting it wrong scales every
/// recovered gradient — and with it the reported thread pitch / stepper step size — by exactly the error.
/// </summary>
[TestFixture]
public class EffectivePixelSizeTests {

    // A 3.76 µm camera, the pitch NINA's readers hand back for any binning (both the FITS and XISF readers
    // divide the stored XPIXSZ back out by XBINNING), so the binning has to be re-applied here.
    private const double NativeMicrons = 3.76;

    [Test]
    public void FromFrameHeader_Unbinned_IsTheNativePitch() {
        var resolution = EffectivePixelSize.FromFrameHeader(NativeMicrons, binX: 1, metadataPixelSizeMicrons: NativeMicrons, "f.fits");

        Assert.Multiple(() => {
            Assert.That(resolution.Microns, Is.EqualTo(3.76).Within(1e-12));
            Assert.That(resolution.FromFrameHeader, Is.True);
            Assert.That(resolution.Provenance, Does.Contain("unbinned"));
        });
    }

    [TestCase(2, 7.52)]
    [TestCase(3, 11.28)]
    [TestCase(4, 15.04)]
    public void FromFrameHeader_Binned_ScalesTheNativePitchByTheBinningFactor(int binning, double expected) {
        // THE regression. Returning the native pitch here understates the sensor by `binning` and inflates the
        // recovered hardware by exactly the same factor — the bug the tilt wizard shipped with.
        var resolution = EffectivePixelSize.FromFrameHeader(NativeMicrons, binning, metadataPixelSizeMicrons: NativeMicrons, "f.fits");

        Assert.Multiple(() => {
            Assert.That(resolution.Microns, Is.EqualTo(expected).Within(1e-12));
            Assert.That(resolution.Provenance, Does.Contain($"{binning}x{binning} binning"));
        });
    }

    [Test]
    public void FromFrameHeader_BeatsAMetadataValueThatDisagrees() {
        // A run saved before metadata schema 4 at 2x2 stored the NATIVE pitch. Trusting it is precisely the
        // old inflation, so the header has to win — and the disagreement has to be reportable.
        var resolution = EffectivePixelSize.FromFrameHeader(NativeMicrons, binX: 2, metadataPixelSizeMicrons: NativeMicrons, "f.fits");

        Assert.Multiple(() => {
            Assert.That(resolution.Microns, Is.EqualTo(7.52).Within(1e-12));
            Assert.That(EffectivePixelSize.DisagreesWithMetadata(resolution, NativeMicrons), Is.True);
        });
    }

    [Test]
    public void FromFrameHeader_AgreesWithSchema4MetadataForTheSameRun() {
        // Schema 4 stores the effective pitch, so a binned run written by the current wizard must NOT be
        // reported as a disagreement — otherwise every modern run prints a scary note about itself.
        var resolution = EffectivePixelSize.FromFrameHeader(NativeMicrons, binX: 2, metadataPixelSizeMicrons: 7.52, "f.fits");

        Assert.That(EffectivePixelSize.DisagreesWithMetadata(resolution, 7.52), Is.False);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void FromFrameHeader_NonsensicalBinning_IsClampedToOne(int binX) {
        // A zero factor would scale the pitch — and the whole sensor extent — to zero, turning every gradient
        // into an infinity rather than failing loudly. Clamp instead.
        var resolution = EffectivePixelSize.FromFrameHeader(NativeMicrons, binX, metadataPixelSizeMicrons: NativeMicrons, "f.fits");

        Assert.That(resolution.Microns, Is.EqualTo(3.76).Within(1e-12));
    }

    [TestCase(double.NaN)]
    [TestCase(0.0)]
    [TestCase(-3.76)]
    public void FromFrameHeader_NoUsablePixelSize_FallsBackToMetadataAndSaysSo(double headerPixelSize) {
        var resolution = EffectivePixelSize.FromFrameHeader(headerPixelSize, binX: 2, metadataPixelSizeMicrons: 4.63, "frame.fits");

        Assert.Multiple(() => {
            Assert.That(resolution.Microns, Is.EqualTo(4.63).Within(1e-12));
            Assert.That(resolution.FromFrameHeader, Is.False);
            Assert.That(resolution.Provenance, Does.StartWith("metadata (").And.Contain("frame.fits"));
        });
    }

    [Test]
    public void FromMetadata_CarriesTheReasonIntoTheProvenance() {
        // The reason matters: on a pre-schema-4 binned run this fallback IS the inflated number, and the
        // report has to admit the harness had to trust it rather than quoting it as measured.
        var resolution = EffectivePixelSize.FromMetadata(3.76, "no frames to read a header from");

        Assert.Multiple(() => {
            Assert.That(resolution.Microns, Is.EqualTo(3.76).Within(1e-12));
            Assert.That(resolution.FromFrameHeader, Is.False);
            Assert.That(resolution.Provenance, Is.EqualTo("metadata (no frames to read a header from)"));
        });
    }

    [Test]
    public void DisagreesWithMetadata_UnsetMetadataIsNotADisagreement() {
        var resolution = EffectivePixelSize.FromFrameHeader(NativeMicrons, binX: 1, metadataPixelSizeMicrons: 0, "f.fits");

        Assert.That(EffectivePixelSize.DisagreesWithMetadata(resolution, 0), Is.False);
    }
}
