using System;
using System.Drawing;
using NINA.Joko.Plugins.HocusFocus.Controls;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Controls;

[TestFixture]
public class PlotPoint3DTests {

    [Test]
    public void Constructor_StoresAllFields() {
        var p = new PlotPoint3D(1.0, 2.0, 3.0, Color.Red, 7.5f, "label");
        Assert.Multiple(() => {
            Assert.That(p.X, Is.EqualTo(1.0));
            Assert.That(p.Y, Is.EqualTo(2.0));
            Assert.That(p.Z, Is.EqualTo(3.0));
            Assert.That(p.Color, Is.EqualTo(Color.Red));
            Assert.That(p.Size, Is.EqualTo(7.5f));
            Assert.That(p.Label, Is.EqualTo("label"));
        });
    }

    [Test]
    public void Constructor_DefaultsSizeAndLabel() {
        var p = new PlotPoint3D(1, 2, 3, Color.Blue);
        Assert.That(p.Size, Is.EqualTo(5.0f));
        Assert.That(p.Label, Is.Null);
    }
}

[TestFixture]
public class PlotTickTests {

    [Test]
    public void Constructor_NoColor_LeavesLabelColorNull() {
        var t = new PlotTick(1.5, "tick");
        Assert.Multiple(() => {
            Assert.That(t.Value, Is.EqualTo(1.5));
            Assert.That(t.Label, Is.EqualTo("tick"));
            Assert.That(t.LabelColor, Is.Null);
        });
    }

    [Test]
    public void Constructor_WithColor_StoresColor() {
        var t = new PlotTick(2.0, "tick", Color.Magenta);
        Assert.That(t.LabelColor, Is.EqualTo(Color.Magenta));
    }
}

[TestFixture]
public class ScreenLabelTests {

    [Test]
    public void Constructor_StoresAllFields() {
        var l = new ScreenLabel("hello", 0.25f, 0.75f, Color.Cyan);
        Assert.Multiple(() => {
            Assert.That(l.Text, Is.EqualTo("hello"));
            Assert.That(l.XRatio, Is.EqualTo(0.25f));
            Assert.That(l.YRatio, Is.EqualTo(0.75f));
            Assert.That(l.Color, Is.EqualTo(Color.Cyan));
        });
    }
}

[TestFixture]
public class SurfaceColorMapTests {

    [Test]
    public void Constructor_RejectsNullStops() {
        Assert.Throws<ArgumentException>(() => new SurfaceColorMap(null));
    }

    [Test]
    public void Constructor_RejectsEmptyStops() {
        Assert.Throws<ArgumentException>(() => new SurfaceColorMap(Array.Empty<(double, Color)>()));
    }

    [Test]
    public void GetColor_SingleStop_ReturnsThatColor() {
        var map = new SurfaceColorMap((0.5, Color.Red));
        Assert.That(map.GetColor(0.0, 0.0, 1.0), Is.EqualTo(Color.Red));
        Assert.That(map.GetColor(1.0, 0.0, 1.0), Is.EqualTo(Color.Red));
    }

    [Test]
    public void GetColor_BelowFirstStop_ReturnsFirstStop() {
        var black = Color.FromArgb(255, 0, 0, 0);
        var white = Color.FromArgb(255, 255, 255, 255);
        var map = new SurfaceColorMap((0.0, black), (1.0, white));
        Assert.That(map.GetColor(0.0, 0.0, 1.0).ToArgb(), Is.EqualTo(black.ToArgb()));
    }

    [Test]
    public void GetColor_AboveLastStop_ReturnsLastStop() {
        var black = Color.FromArgb(255, 0, 0, 0);
        var white = Color.FromArgb(255, 255, 255, 255);
        var map = new SurfaceColorMap((0.0, black), (0.5, white));
        Assert.That(map.GetColor(1.0, 0.0, 1.0).ToArgb(), Is.EqualTo(white.ToArgb()));
    }

    [Test]
    public void GetColor_DegenerateRange_UsesMidpoint() {
        var map = new SurfaceColorMap((0.0, Color.Black), (1.0, Color.White));
        var c = map.GetColor(5.0, 1.0, 1.0);
        Assert.That(c.R, Is.EqualTo(127).Within(2));
    }

    [Test]
    public void GetColor_Midway_BlendsLinearly() {
        var map = new SurfaceColorMap((0.0, Color.FromArgb(0, 0, 0)), (1.0, Color.FromArgb(200, 100, 50)));
        var c = map.GetColor(0.5, 0.0, 1.0);
        Assert.Multiple(() => {
            Assert.That(c.R, Is.EqualTo(100).Within(2));
            Assert.That(c.G, Is.EqualTo(50).Within(2));
            Assert.That(c.B, Is.EqualTo(25).Within(2));
        });
    }

    [Test]
    public void GetColor_OrdersStopsByPosition() {
        // Constructor should sort by position even if passed reversed.
        var black = Color.FromArgb(255, 0, 0, 0);
        var white = Color.FromArgb(255, 255, 255, 255);
        var map = new SurfaceColorMap((1.0, white), (0.0, black));
        Assert.That(map.GetColor(0.0, 0.0, 1.0).ToArgb(), Is.EqualTo(black.ToArgb()));
        Assert.That(map.GetColor(1.0, 0.0, 1.0).ToArgb(), Is.EqualTo(white.ToArgb()));
    }

    [Test]
    public void Blend_AtZero_ReturnsLeft() {
        var c = SurfaceColorMap.Blend(Color.FromArgb(255, 10, 20, 30), Color.FromArgb(0, 200, 200, 200), 0.0);
        Assert.Multiple(() => {
            Assert.That(c.A, Is.EqualTo(255));
            Assert.That(c.R, Is.EqualTo(10));
            Assert.That(c.G, Is.EqualTo(20));
            Assert.That(c.B, Is.EqualTo(30));
        });
    }

    [Test]
    public void Blend_AtOne_ReturnsRight() {
        var c = SurfaceColorMap.Blend(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(100, 50, 60, 70), 1.0);
        Assert.Multiple(() => {
            Assert.That(c.A, Is.EqualTo(100));
            Assert.That(c.R, Is.EqualTo(50));
            Assert.That(c.G, Is.EqualTo(60));
            Assert.That(c.B, Is.EqualTo(70));
        });
    }

    [Test]
    public void Blend_ClampsRatio() {
        var black = Color.FromArgb(255, 0, 0, 0);
        var white = Color.FromArgb(255, 255, 255, 255);
        var c1 = SurfaceColorMap.Blend(black, white, -1.0);
        var c2 = SurfaceColorMap.Blend(black, white, 2.0);
        Assert.That(c1.ToArgb(), Is.EqualTo(black.ToArgb()));
        Assert.That(c2.ToArgb(), Is.EqualTo(white.ToArgb()));
    }
}

[TestFixture]
public class SurfacePlotModelDataTests {

    [Test]
    public void Defaults_AreReasonable() {
        var m = new SurfacePlotModel();
        Assert.Multiple(() => {
            Assert.That(m.RotationXDegrees, Is.EqualTo(45.0));
            Assert.That(m.RotationZDegrees, Is.EqualTo(25.0));
            Assert.That(m.VerticalScale, Is.EqualTo(0.8));
            Assert.That(m.ShowSurface, Is.True);
            Assert.That(m.ContourCount, Is.EqualTo(10));
            Assert.That(m.ContourLabelFormat, Is.EqualTo("0.0"));
            Assert.That(m.BackgroundColor, Is.EqualTo(Color.Black));
            Assert.That(m.TextColor, Is.EqualTo(Color.White));
            Assert.That(m.Projection, Is.EqualTo(ProjectionType.RotationBased));
            Assert.That(m.ObliqueYAngleDegrees, Is.EqualTo(30.0));
            Assert.That(m.ObliqueYScale, Is.EqualTo(0.5));
            Assert.That(m.ShowAxes, Is.True);
        });
    }

    [Test]
    public void Lists_AreInitializedNonNull() {
        var m = new SurfacePlotModel();
        Assert.Multiple(() => {
            Assert.That(m.XTicks, Is.Empty);
            Assert.That(m.YTicks, Is.Empty);
            Assert.That(m.ZTicks, Is.Empty);
            Assert.That(m.Points, Is.Empty);
            Assert.That(m.ScreenLabels, Is.Empty);
        });
    }
}

[TestFixture]
public class SurfacePlotRendererTests {

    [Test]
    public void Constructor_RejectsNullModel() {
        Assert.Throws<ArgumentNullException>(() => new SurfacePlotRenderer(null, 10, 10));
    }

    [Test]
    public void Constructor_ClampsDimensionsToAtLeastOne() {
        // Should not throw.
        var renderer = new SurfacePlotRenderer(new SurfacePlotModel(), 0, -5);
        Assert.That(renderer, Is.Not.Null);
    }

    [Test]
    public void Render_ThrowsWhenZIsMissing() {
        var renderer = new SurfacePlotRenderer(new SurfacePlotModel(), 16, 16);
        Assert.Throws<ArgumentException>(() => renderer.Render());
    }

    [Test]
    public void Render_ProducesBitmapWhenZIsProvided() {
        var z = new double[3, 3];
        for (int j = 0; j < 3; j++) {
            for (int i = 0; i < 3; i++) {
                z[j, i] = i + j;
            }
        }
        var model = new SurfacePlotModel { Z = z };
        using var bmp = new SurfacePlotRenderer(model, 32, 32).Render();
        Assert.Multiple(() => {
            Assert.That(bmp.Width, Is.EqualTo(32));
            Assert.That(bmp.Height, Is.EqualTo(32));
        });
    }
}
