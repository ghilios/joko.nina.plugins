using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class TanProjectionTests {
    private const double FocalLengthMm = 800.0;
    private const double PixelMicrons = 3.76;

    [Test]
    public void PlateScale_MatchesFormula() {
        var proj = new TanProjection(83.8, -5.4, FocalLengthMm, PixelMicrons, 0.0, 3008, 3008);
        var expectedArcsecPerPx = 206.264806247096355 * PixelMicrons / FocalLengthMm;
        Assert.Multiple(() => {
            Assert.That(proj.ArcsecPerPixel, Is.EqualTo(expectedArcsecPerPx).Within(1e-9));
            Assert.That(proj.ArcsecPerPixel, Is.EqualTo(0.969).Within(1e-3), "design sanity value");
            Assert.That(proj.RadiansPerPixel, Is.EqualTo(PixelMicrons / (1000.0 * FocalLengthMm)).Within(1e-15));
        });
    }

    [TestCase(0.0)]
    [TestCase(13.7)]
    [TestCase(-47.0)]
    public void CenterStar_ProjectsToImageCenter(double rotationDeg) {
        const double raC = 200.25;
        const double decC = 42.5;
        var proj = new TanProjection(raC, decC, FocalLengthMm, PixelMicrons, rotationDeg, 3008, 3008);
        var (x, y) = proj.Project(raC, decC);
        Assert.Multiple(() => {
            Assert.That(x, Is.EqualTo(3008 / 2.0).Within(1e-9));
            Assert.That(y, Is.EqualTo(3008 / 2.0).Within(1e-9));
        });
    }

    private static IEnumerable<TestCaseData> RoundTripConfigs() {
        // center RA, center Dec, rotationDeg, width, height
        yield return new TestCaseData(83.8, -5.4, 0.0, 3008, 3008).SetName("IMX533_orion_rot0");
        yield return new TestCaseData(83.8, -5.4, 13.7, 3008, 3008).SetName("IMX533_orion_rot13");
        yield return new TestCaseData(200.25, 42.5, -47.0, 6248, 4176).SetName("IMX571_north_rotNeg47");
        yield return new TestCaseData(10.0, 70.0, 30.0, 6248, 4176).SetName("IMX571_highDec_rot30");
    }

    [TestCaseSource(nameof(RoundTripConfigs))]
    public void RoundTrip_PixelToRaDecToPixel_WithinHundredthPixel(double raC, double decC, double rotationDeg, int width, int height) {
        var proj = new TanProjection(raC, decC, FocalLengthMm, PixelMicrons, rotationDeg, width, height);

        // Grid over the whole frame including exact edges and corners.
        var xs = new[] { 0.0, 1.0, width * 0.25, width / 2.0, width * 0.75, width - 1.0, width };
        var ys = new[] { 0.0, 1.0, height * 0.25, height / 2.0, height * 0.75, height - 1.0, height };

        var maxErr = 0.0;
        foreach (var px in xs) {
            foreach (var py in ys) {
                var (ra, dec) = proj.Deproject(px, py);
                var (x2, y2) = proj.Project(ra, dec);
                maxErr = Math.Max(maxErr, Math.Sqrt((x2 - px) * (x2 - px) + (y2 - py) * (y2 - py)));
            }
        }
        Assert.That(maxErr, Is.LessThan(0.01), $"max round-trip error {maxErr:E3} px");
    }

    [TestCaseSource(nameof(RoundTripConfigs))]
    public void RoundTrip_RaDecToPixelToRaDec_WithinHundredthPixelEquivalent(double raC, double decC, double rotationDeg, int width, int height) {
        var proj = new TanProjection(raC, decC, FocalLengthMm, PixelMicrons, rotationDeg, width, height);
        var arcsecTolerance = 0.01 * proj.ArcsecPerPixel; // 0.01 px equivalent

        // Sample RA/Dec by deprojecting a grid of pixels, then test radec→pixel→radec.
        var xs = new[] { 0.0, width * 0.25, width / 2.0, width * 0.75, (double)width };
        var ys = new[] { 0.0, height * 0.25, height / 2.0, height * 0.75, (double)height };

        var maxErrArcsec = 0.0;
        foreach (var px in xs) {
            foreach (var py in ys) {
                var (ra, dec) = proj.Deproject(px, py);
                var (x, y) = proj.Project(ra, dec);
                var (ra2, dec2) = proj.Deproject(x, y);

                // Angular separation between (ra,dec) and (ra2,dec2), in arcsec.
                var dDec = (dec2 - dec) * 3600.0;
                var dRa = NormalizeDeltaDeg(ra2 - ra) * Math.Cos(dec * Math.PI / 180.0) * 3600.0;
                maxErrArcsec = Math.Max(maxErrArcsec, Math.Sqrt(dRa * dRa + dDec * dDec));
            }
        }
        Assert.That(maxErrArcsec, Is.LessThan(arcsecTolerance), $"max round-trip error {maxErrArcsec:E3} arcsec (tol {arcsecTolerance:E3})");
    }

    [Test]
    public void TryProject_RejectsStarWellOutsideFov() {
        const double raC = 83.8;
        const double decC = -5.4;
        var proj = new TanProjection(raC, decC, FocalLengthMm, PixelMicrons, 0.0, 3008, 3008);

        // 5 degrees away is far outside a ~0.8 degree FOV.
        var ok = proj.TryProject(raC + 5.0, decC, out var x, out var y);
        Assert.That(ok, Is.False, $"projected to ({x:F1},{y:F1}) unexpectedly in-frame");
    }

    [Test]
    public void TryProject_KeepsStarJustOutsideWhenMarginSupplied() {
        const double raC = 83.8;
        const double decC = -5.4;
        const int width = 3008;
        const int height = 3008;
        var proj = new TanProjection(raC, decC, FocalLengthMm, PixelMicrons, 0.0, width, height);

        // A pixel 3 px beyond the right edge, mapped back to sky, is a star whose wings spill onto the frame.
        var (ra, dec) = proj.Deproject(width + 3.0, height / 2.0);

        Assert.Multiple(() => {
            Assert.That(proj.TryProject(ra, dec, out _, out _, psfMarginPx: 0.0), Is.False, "margin 0 rejects");
            Assert.That(proj.TryProject(ra, dec, out var xk, out var yk, psfMarginPx: 5.0), Is.True, "margin 5 keeps");
            Assert.That(xk, Is.EqualTo(width + 3.0).Within(1e-6));
            Assert.That(yk, Is.EqualTo(height / 2.0).Within(1e-6));
        });
    }

    [Test]
    public void TryProject_KeepsStarInsideFrame() {
        var proj = new TanProjection(83.8, -5.4, FocalLengthMm, PixelMicrons, 0.0, 3008, 3008);
        var (ra, dec) = proj.Deproject(1000.0, 2000.0);
        var ok = proj.TryProject(ra, dec, out var x, out var y);
        Assert.Multiple(() => {
            Assert.That(ok, Is.True);
            Assert.That(x, Is.EqualTo(1000.0).Within(1e-6));
            Assert.That(y, Is.EqualTo(2000.0).Within(1e-6));
        });
    }

    [Test]
    public void EastWestSignConvention_IncreasingRaMovesPixelInPositiveX() {
        // Lock the (+RA → +x) convention at rotation 0. A pure RA change (dec fixed on the equator) must not
        // move y, and increasing/decreasing RA must move x in opposite, sign-definite directions.
        var proj = new TanProjection(100.0, 0.0, FocalLengthMm, PixelMicrons, 0.0, 3008, 3008);
        const double center = 3008 / 2.0;
        var (xEast, yEast) = proj.Project(100.1, 0.0); // higher RA (East)
        var (xWest, yWest) = proj.Project(99.9, 0.0);  // lower RA (West)
        Assert.Multiple(() => {
            Assert.That(xEast, Is.GreaterThan(center + 1.0), "increasing RA -> +x");
            Assert.That(xWest, Is.LessThan(center - 1.0), "decreasing RA -> -x");
            Assert.That(xEast - center, Is.EqualTo(center - xWest).Within(1e-6), "symmetric about center");
            Assert.That(yEast, Is.EqualTo(center).Within(1e-6), "no dec change keeps y at center");
            Assert.That(yWest, Is.EqualTo(center).Within(1e-6));
        });
    }

    [Test]
    public void NorthSouthSignConvention_IncreasingDecMovesPixelInNegativeY() {
        // Lock the (+Dec → −y, North up) convention at rotation 0.
        var proj = new TanProjection(100.0, 0.0, FocalLengthMm, PixelMicrons, 0.0, 3008, 3008);
        const double center = 3008 / 2.0;
        var (xNorth, yNorth) = proj.Project(100.0, 0.1); // higher Dec (North)
        Assert.Multiple(() => {
            Assert.That(yNorth, Is.LessThan(center - 1.0), "increasing Dec -> -y (up)");
            Assert.That(xNorth, Is.EqualTo(center).Within(1e-6), "no RA change keeps x at center");
        });
    }

    [Test]
    public void TryProject_RejectsStarOnOrBeyondTangentHemisphere() {
        var proj = new TanProjection(0.0, 0.0, FocalLengthMm, PixelMicrons, 0.0, 3008, 3008);
        // Center below the equator so the north pole is strictly 135° behind the tangent hemisphere.
        var projSouth = new TanProjection(0.0, -45.0, FocalLengthMm, PixelMicrons, 0.0, 3008, 3008);
        Assert.Multiple(() => {
            // Every star ≥ 90° from the center is rejected — either the denom ≤ 0 guard fires, or (at the
            // exactly-90° boundary, where floating-point rounds denom to a tiny positive) it projects to
            // infinity and is caught as off-frame.
            Assert.That(proj.TryProject(90.0, 0.0, out _, out _), Is.False, "exactly 90 deg away");
            Assert.That(proj.TryProject(120.0, 0.0, out _, out _), Is.False, "beyond 90 deg (would mirror)");
            Assert.That(proj.TryProject(0.0, 90.0, out _, out _), Is.False, "pole with equatorial center");
            Assert.That(projSouth.TryProject(0.0, 90.0, out _, out _), Is.False, "pole 135 deg behind center");

            // Where the star is strictly behind the hemisphere (denom < 0), Project signals NaN rather than
            // a plausible-but-wrong finite pixel.
            var (xBehind, yBehind) = proj.Project(120.0, 0.0);
            Assert.That(double.IsNaN(xBehind) && double.IsNaN(yBehind), Is.True, "behind-hemisphere -> NaN");
            var (xPole, yPole) = projSouth.Project(0.0, 90.0);
            Assert.That(double.IsNaN(xPole) && double.IsNaN(yPole), Is.True, "pole behind hemisphere -> NaN");
        });
    }

    private static double NormalizeDeltaDeg(double deltaDeg) {
        var d = deltaDeg % 360.0;
        if (d > 180.0) d -= 360.0;
        if (d < -180.0) d += 360.0;
        return d;
    }
}
