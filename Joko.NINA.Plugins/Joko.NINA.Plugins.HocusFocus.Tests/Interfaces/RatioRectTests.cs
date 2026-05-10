using System;
using System.Drawing;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Interfaces;

[TestFixture]
public class RatioRectTests {

    [Test]
    public void Constructor_StoresCoordinates() {
        var r = new RatioRect(0.1, 0.2, 0.3, 0.4);
        Assert.Multiple(() => {
            Assert.That(r.StartX, Is.EqualTo(0.1));
            Assert.That(r.StartY, Is.EqualTo(0.2));
            Assert.That(r.Width, Is.EqualTo(0.3));
            Assert.That(r.Height, Is.EqualTo(0.4));
        });
    }

    [Test]
    public void Constructor_ClampsWidthAndHeightToBounds() {
        var r = new RatioRect(0.6, 0.7, 1.0, 1.0);
        Assert.That(r.Width, Is.EqualTo(0.4).Within(1e-12));
        Assert.That(r.Height, Is.EqualTo(0.3).Within(1e-12));
    }

    [TestCase(-0.1, 0.0, 0.5, 0.5)]
    [TestCase(0.0, -0.1, 0.5, 0.5)]
    [TestCase(1.0, 0.0, 0.5, 0.5)]
    [TestCase(0.0, 1.0, 0.5, 0.5)]
    [TestCase(0.1, 0.1, 0.0, 0.5)]
    [TestCase(0.1, 0.1, 0.5, 0.0)]
    [TestCase(0.1, 0.1, -0.5, 0.5)]
    [TestCase(0.1, 0.1, 0.5, -0.5)]
    public void Constructor_RejectsInvalidArgs(double sx, double sy, double w, double h) {
        Assert.Throws<ArgumentException>(() => new RatioRect(sx, sy, w, h));
    }

    [Test]
    public void Full_IsFullCoverage() {
        Assert.That(RatioRect.Full.IsFull(), Is.True);
        Assert.That(RatioRect.Full.StartX, Is.EqualTo(0.0));
        Assert.That(RatioRect.Full.StartY, Is.EqualTo(0.0));
        Assert.That(RatioRect.Full.Width, Is.EqualTo(1.0));
        Assert.That(RatioRect.Full.Height, Is.EqualTo(1.0));
    }

    [Test]
    public void EndExclusive_AddsStartAndExtent() {
        var r = new RatioRect(0.1, 0.2, 0.3, 0.4);
        Assert.That(r.EndExclusiveX(), Is.EqualTo(0.4).Within(1e-12));
        Assert.That(r.EndExclusiveY(), Is.EqualTo(0.6).Within(1e-12));
    }

    [Test]
    public void Contains_ReturnsTrueForInteriorRect() {
        var outer = new RatioRect(0.0, 0.0, 1.0, 1.0);
        var inner = new RatioRect(0.1, 0.1, 0.5, 0.5);
        Assert.That(outer.Contains(inner), Is.True);
    }

    [Test]
    public void Contains_ReturnsFalseWhenInnerStartsBeforeOuter() {
        var outer = new RatioRect(0.5, 0.5, 0.4, 0.4);
        var inner = new RatioRect(0.1, 0.6, 0.2, 0.2);
        Assert.That(outer.Contains(inner), Is.False);
    }

    [Test]
    public void Contains_ReturnsFalseWhenInnerExtendsBeyondOuter() {
        var outer = new RatioRect(0.0, 0.0, 0.5, 0.5);
        var inner = new RatioRect(0.1, 0.1, 0.7, 0.2);
        Assert.That(outer.Contains(inner), Is.False);
    }

    [Test]
    public void Contains_RectangleOverload_DelegatesToFromRectangle() {
        var outer = new RatioRect(0.0, 0.0, 1.0, 1.0);
        var rect = new Rectangle(10, 10, 50, 50);
        Assert.That(outer.Contains(rect, new Size(100, 100)), Is.True);
    }

    [Test]
    public void IsFull_FalseForPartialRect() {
        Assert.That(new RatioRect(0.0, 0.0, 0.5, 1.0).IsFull(), Is.False);
        Assert.That(new RatioRect(0.0, 0.0, 1.0, 0.5).IsFull(), Is.False);
    }

    [Test]
    public void ToString_ContainsAllFields() {
        var r = new RatioRect(0.1, 0.2, 0.3, 0.4);
        var s = r.ToString();
        Assert.Multiple(() => {
            Assert.That(s, Does.Contain("StartX="));
            Assert.That(s, Does.Contain("StartY="));
            Assert.That(s, Does.Contain("Width="));
            Assert.That(s, Does.Contain("Height="));
        });
    }

    [Test]
    public void FromCenterROI_ProducesCenteredRect() {
        var r = RatioRect.FromCenterROI(0.5);
        Assert.Multiple(() => {
            Assert.That(r.StartX, Is.EqualTo(0.25).Within(1e-12));
            Assert.That(r.StartY, Is.EqualTo(0.25).Within(1e-12));
            Assert.That(r.Width, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(r.Height, Is.EqualTo(0.5).Within(1e-12));
        });
    }

    [Test]
    public void FromRectangle_ScalesByFullSize() {
        var rect = new Rectangle(50, 100, 200, 300);
        var r = RatioRect.FromRectangle(rect, new Size(1000, 2000));
        Assert.Multiple(() => {
            Assert.That(r.StartX, Is.EqualTo(0.05).Within(1e-12));
            Assert.That(r.StartY, Is.EqualTo(0.05).Within(1e-12));
            Assert.That(r.Width, Is.EqualTo(0.2).Within(1e-12));
            Assert.That(r.Height, Is.EqualTo(0.15).Within(1e-12));
        });
    }

    [Test]
    public void ToRectangle_RoundsToPixelCoords() {
        var r = new RatioRect(0.1, 0.1, 0.5, 0.5);
        var rect = r.ToRectangle(new Size(100, 200));
        Assert.Multiple(() => {
            Assert.That(rect.X, Is.EqualTo(10));
            Assert.That(rect.Y, Is.EqualTo(20));
            Assert.That(rect.Width, Is.EqualTo(50));
            Assert.That(rect.Height, Is.EqualTo(100));
        });
    }

    [Test]
    public void ToRect2D_AndToInt32Rect_AlsoRound() {
        var r = new RatioRect(0.1, 0.1, 0.5, 0.5);
        var size = new Size(100, 200);
        var rect2d = r.ToRect2D(size);
        var int32Rect = r.ToInt32Rect(size);
        Assert.Multiple(() => {
            Assert.That(rect2d.X, Is.EqualTo(10));
            Assert.That(rect2d.Y, Is.EqualTo(20));
            Assert.That(rect2d.Width, Is.EqualTo(50));
            Assert.That(rect2d.Height, Is.EqualTo(100));
            Assert.That(int32Rect.X, Is.EqualTo(10));
            Assert.That(int32Rect.Y, Is.EqualTo(20));
            Assert.That(int32Rect.Width, Is.EqualTo(50));
            Assert.That(int32Rect.Height, Is.EqualTo(100));
        });
    }

    [Test]
    public void RoundTrip_FromRectangleAndBack_PreservesGeometryWithinRounding() {
        var size = new Size(1024, 768);
        var original = new Rectangle(100, 200, 300, 400);
        var r = RatioRect.FromRectangle(original, size);
        var roundTrip = r.ToRectangle(size);
        Assert.Multiple(() => {
            Assert.That(roundTrip.X, Is.EqualTo(original.X));
            Assert.That(roundTrip.Y, Is.EqualTo(original.Y));
            Assert.That(roundTrip.Width, Is.EqualTo(original.Width));
            Assert.That(roundTrip.Height, Is.EqualTo(original.Height));
        });
    }
}
