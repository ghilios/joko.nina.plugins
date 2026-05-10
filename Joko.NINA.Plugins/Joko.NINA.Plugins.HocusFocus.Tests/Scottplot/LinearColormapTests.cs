using NINA.Joko.Plugins.HocusFocus.Scottplot;
using NUnit.Framework;
using System.Drawing;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Scottplot {

    [TestFixture]
    public class LinearColormapTests {

        [Test]
        public void GetRGB_AtZero_ReturnsLowColor() {
            var lo = Color.FromArgb(10, 20, 30);
            var mid = Color.FromArgb(100, 110, 120);
            var hi = Color.FromArgb(200, 210, 220);
            var cmap = new LinearColormap("test", lo, mid, hi);

            var (r, g, b) = cmap.GetRGB(0);

            Assert.Multiple(() => {
                Assert.That(r, Is.EqualTo(lo.R));
                Assert.That(g, Is.EqualTo(lo.G));
                Assert.That(b, Is.EqualTo(lo.B));
            });
        }

        [Test]
        public void GetRGB_AtMidpoint_IsBetweenLowAndMid() {
            var lo = Color.FromArgb(0, 0, 0);
            var mid = Color.FromArgb(128, 128, 128);
            var hi = Color.FromArgb(255, 255, 255);
            var cmap = new LinearColormap("test", lo, mid, hi);

            var atMid = cmap.GetRGB(128);

            // value 128 / 255 = 0.502 — this falls into the second half (>= 0.5),
            // so the constructor takes the "highColor branch". colorRatio = 0.004
            // so r ≈ mid.R + 0.004 * (hi.R - mid.R) ≈ 128 (rounds to mid)
            Assert.Multiple(() => {
                Assert.That(atMid.r, Is.EqualTo((byte)128).Within(2));
                Assert.That(atMid.g, Is.EqualTo((byte)128).Within(2));
                Assert.That(atMid.b, Is.EqualTo((byte)128).Within(2));
            });
        }

        [Test]
        public void GetRGB_AtMaxValue_ReturnsHighColor() {
            var lo = Color.FromArgb(10, 20, 30);
            var mid = Color.FromArgb(100, 110, 120);
            var hi = Color.FromArgb(200, 210, 220);
            var cmap = new LinearColormap("test", lo, mid, hi);

            var (r, g, b) = cmap.GetRGB(255);

            Assert.Multiple(() => {
                Assert.That(r, Is.EqualTo(hi.R));
                Assert.That(g, Is.EqualTo(hi.G));
                Assert.That(b, Is.EqualTo(hi.B));
            });
        }

        [Test]
        public void GetRGB_QuarterValue_InterpolatesLowToMid() {
            var lo = Color.FromArgb(0, 0, 0);
            var mid = Color.FromArgb(200, 200, 200);
            var hi = Color.FromArgb(255, 255, 255);
            var cmap = new LinearColormap("test", lo, mid, hi);

            // ratio = 64/255 ≈ 0.251, in lower half. colorRatio = 0.502
            // r ≈ 0 + 0.502 * 200 ≈ 100
            var (r, g, b) = cmap.GetRGB(64);

            Assert.Multiple(() => {
                Assert.That(r, Is.InRange((byte)95, (byte)105));
                Assert.That(g, Is.InRange((byte)95, (byte)105));
                Assert.That(b, Is.InRange((byte)95, (byte)105));
            });
        }

        [Test]
        public void GetRGB_ThreeQuarterValue_InterpolatesMidToHigh() {
            var lo = Color.FromArgb(0, 0, 0);
            var mid = Color.FromArgb(100, 100, 100);
            var hi = Color.FromArgb(200, 200, 200);
            var cmap = new LinearColormap("test", lo, mid, hi);

            // ratio = 192/255 ≈ 0.753, in upper half. colorRatio = (0.753-0.5)*2 ≈ 0.506
            // r ≈ 100 + 0.506 * (200 - 100) ≈ 150
            var (r, g, b) = cmap.GetRGB(192);

            Assert.Multiple(() => {
                Assert.That(r, Is.InRange((byte)145, (byte)155));
                Assert.That(g, Is.InRange((byte)145, (byte)155));
                Assert.That(b, Is.InRange((byte)145, (byte)155));
            });
        }

        [Test]
        public void Name_PreservedFromConstructor() {
            var cmap = new LinearColormap("hocus", Color.Black, Color.Gray, Color.White);
            Assert.That(cmap.Name, Is.EqualTo("hocus"));
        }
    }
}
