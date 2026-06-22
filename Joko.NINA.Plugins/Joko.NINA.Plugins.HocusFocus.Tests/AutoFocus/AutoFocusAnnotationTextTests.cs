using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;
using System;
using System.Globalization;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class AutoFocusAnnotationTextTests {

        private static AutoFocusReviewStar WithPsf() => new AutoFocusReviewStar {
            Hfr = 2.0,
            Background = 12.5,
            HasPsf = true,
            FwhmArcsec = 4.2426406,
            FwhmPixels = 2.8284271,
            FwhmX = 4.0,
            FwhmY = 2.0,
            Eccentricity = 0.8660254,
            PsfThetaDegrees = 90.0,
            PsfBackground = 56.0,
            PsfPeak = 1234.0,
            MoffatBeta = 4.0,
        };

        private static AutoFocusReviewStar NoPsf() => new AutoFocusReviewStar {
            Hfr = 2.0,
            Background = 12.5,
            HasPsf = false,
            FwhmArcsec = double.NaN,
            FwhmPixels = double.NaN,
            FwhmX = double.NaN,
            FwhmY = double.NaN,
            Eccentricity = double.NaN,
            PsfThetaDegrees = double.NaN,
            PsfBackground = double.NaN,
            PsfPeak = double.NaN,
            MoffatBeta = double.NaN,
        };

        private static string F(double v, string fmt) => v.ToString(fmt, CultureInfo.CurrentCulture);

        [Test]
        public void Format_WithPsf_MatchesAnnotatorFormatsForEveryType() {
            var s = WithPsf();
            Assert.Multiple(() => {
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.None), Is.EqualTo(string.Empty));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.HFR), Is.EqualTo(F(2.0, "#0.00")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.Background), Is.EqualTo(F(12.5, "#.0000")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.FWHM), Is.EqualTo(F(4.2426406, "#0.00")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.FWHMPixels), Is.EqualTo(F(2.8284271, "#0.00")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.FWHM_X), Is.EqualTo(F(4.0, "#0.00")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.FWHM_Y), Is.EqualTo(F(2.0, "#0.00")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.Eccentricity), Is.EqualTo(F(0.8660254, "#.00")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.PSFTheta), Is.EqualTo(Math.Round(90.0).ToString("##0", CultureInfo.CurrentCulture) + "°"));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.PSFBackground), Is.EqualTo(F(56.0, "#.0000")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.PSFPeak), Is.EqualTo(F(1234.0, "#.0000")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.MoffatBeta), Is.EqualTo(F(4.0, "0.##")));
            });
        }

        [Test]
        public void Format_WithoutPsf_PsfOnlyTypesAreEmptyButHfrAndBackgroundStillRender() {
            var s = NoPsf();
            Assert.Multiple(() => {
                // Non-PSF values still render.
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.HFR), Is.EqualTo(F(2.0, "#0.00")));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.Background), Is.EqualTo(F(12.5, "#.0000")));
                // PSF-only values draw nothing (matching the annotator's `psf != null` guards).
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.FWHM), Is.EqualTo(string.Empty));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.FWHMPixels), Is.EqualTo(string.Empty));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.FWHM_X), Is.EqualTo(string.Empty));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.FWHM_Y), Is.EqualTo(string.Empty));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.Eccentricity), Is.EqualTo(string.Empty));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.PSFTheta), Is.EqualTo(string.Empty));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.PSFBackground), Is.EqualTo(string.Empty));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.PSFPeak), Is.EqualTo(string.Empty));
                Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.MoffatBeta), Is.EqualTo(string.Empty));
            });
        }

        [Test]
        public void Format_MoffatBetaNaN_IsEmptyEvenWithPsf() {
            var s = WithPsf();
            s = new AutoFocusReviewStar {
                HasPsf = true,
                MoffatBeta = double.NaN,
            };
            Assert.That(AutoFocusAnnotationText.Format(s, ShowAnnotationTypeEnum.MoffatBeta), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Format_NullStar_IsEmpty() {
            Assert.That(AutoFocusAnnotationText.Format(null, ShowAnnotationTypeEnum.HFR), Is.EqualTo(string.Empty));
        }
    }
}
