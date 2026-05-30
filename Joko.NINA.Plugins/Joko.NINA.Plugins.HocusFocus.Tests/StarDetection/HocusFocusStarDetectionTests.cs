using NINA.Core.Enum;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using NSubstitute;
using NUnit.Framework;
using System.Drawing;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class HocusFocusStarDetectionTests {

        private static HocusFocusStarDetection Build() {
            var profileService = Substitute.For<IProfileService>();
            var focuserMediator = Substitute.For<IFocuserMediator>();
            focuserMediator.GetInfo().Returns(new FocuserInfo());
            return new HocusFocusStarDetection(
                imageStatisticsVM: Substitute.For<IImageStatisticsVM>(),
                profileService: profileService,
                focuserMediator: focuserMediator,
                starDetectionOptions: Substitute.For<IStarDetectionOptions>(),
                alglibAPI: new AlglibAPI());
        }

        [Test]
        public void Name_IsHocusFocus() {
            Assert.That(Build().Name, Is.EqualTo("Hocus Focus"));
        }

        [Test]
        public void ContentId_IsFullyQualifiedTypeName() {
            Assert.That(Build().ContentId, Is.EqualTo(typeof(HocusFocusStarDetection).FullName));
        }

        [Test]
        public void ToHocusFocusParams_NormalSensitivity_HighSigmaIs3() {
            var det = Build();
            var p = new StarDetectionParams { Sensitivity = StarSensitivityEnum.Normal, NumberOfAFStars = 7, IsAutoFocus = true };
            var hp = det.ToHocusFocusParams(p);

            Assert.Multiple(() => {
                Assert.That(hp.HighSigmaOutlierRejection, Is.EqualTo(3.0).Within(1e-12));
                Assert.That(hp.LowSigmaOutlierRejection, Is.EqualTo(3.0).Within(1e-12));
                Assert.That(hp.NumberOfAFStars, Is.EqualTo(7));
                Assert.That(hp.IsAutoFocus, Is.True);
            });
        }

        [Test]
        public void ToHocusFocusParams_HighSensitivity_HighSigmaIs4() {
            var det = Build();
            var p = new StarDetectionParams { Sensitivity = StarSensitivityEnum.High };
            var hp = det.ToHocusFocusParams(p);

            Assert.That(hp.HighSigmaOutlierRejection, Is.EqualTo(4.0).Within(1e-12));
        }

        [Test]
        public void CreateAnalysis_ReturnsHocusFocusStarDetectionAnalysis() {
            var det = Build();
            var analysis = det.CreateAnalysis();
            Assert.That(analysis, Is.InstanceOf<HocusFocusStarDetectionAnalysis>());
        }

        [Test]
        public void UpdateAnalysis_CopiesFieldsFromResultOntoAnalysis() {
            var det = Build();
            var analysis = (HocusFocusStarDetectionAnalysis)det.CreateAnalysis();
            var result = new HocusFocusStarDetectionResult {
                AverageHFR = 2.5,
                HFRStdDev = 0.3,
                DetectedStars = 42,
                Metrics = new StarDetectorMetrics(),
                PSFType = StarDetectorPSFFitType.Gaussian,
                PSFRSquared = 0.95,
                Sigma = 1.7,
                FWHM = 4.0,
                FWHMMAD = 0.4,
                Eccentricity = 0.2,
                EccentricityMAD = 0.05,
                MeasurementAverage = MeasurementAverageEnum.Median,
                PixelScale = 1.25
            };

            det.UpdateAnalysis(analysis, new StarDetectionParams(), result);

            Assert.Multiple(() => {
                Assert.That(analysis.HFR, Is.EqualTo(2.5));
                Assert.That(analysis.HFRStDev, Is.EqualTo(0.3));
                Assert.That(analysis.DetectedStars, Is.EqualTo(42));
                Assert.That(analysis.PSFType, Is.EqualTo(StarDetectorPSFFitType.Gaussian));
                Assert.That(analysis.PSFRSquared, Is.EqualTo(0.95));
                Assert.That(analysis.Sigma, Is.EqualTo(1.7));
                Assert.That(analysis.FWHM, Is.EqualTo(4.0));
                Assert.That(analysis.FWHMMAD, Is.EqualTo(0.4));
                Assert.That(analysis.Eccentricity, Is.EqualTo(0.2));
                Assert.That(analysis.EccentricityMAD, Is.EqualTo(0.05));
                Assert.That(analysis.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.Median));
                Assert.That(analysis.PixelScale, Is.EqualTo(1.25));
            });
        }

        [Test]
        public void HocusFocusStarDetectionAnalysis_RaisesPropertyChangedOnSetters() {
            var a = new HocusFocusStarDetectionAnalysis();
            int changes = 0;
            a.PropertyChanged += (_, _) => changes++;

            a.FWHM = 1.0;
            a.FWHM = 1.0; // unchanged
            a.Eccentricity = 0.1;
            a.Sigma = 2.0;
            a.PSFRSquared = 0.99;

            Assert.That(changes, Is.EqualTo(4));
        }

        [Test]
        public void HocusFocusDetectedStar_ToString_IsNotEmpty() {
            var s = new HocusFocusDetectedStar {
                HFR = 1.0,
                Position = new Accord.Point(1.0f, 2.0f),
                AverageBrightness = 100.0,
                MaxBrightness = 200.0,
                Background = 10.0,
                BoundingBox = new Rectangle(0, 0, 5, 5)
            };
            Assert.That(s.ToString(), Is.Not.Empty);
        }

        [Test]
        public void ToDetectedStar_CopiesContaminationFlag([Values(true, false)] bool contaminated) {
            var star = new Star {
                Center = new OpenCvSharp.Point2d(10.0, 20.0),
                StarBoundingBox = new OpenCvSharp.Rect(5, 15, 10, 10),
                HFR = 2.0,
                MeanBrightness = 100.0,
                PeakBrightness = 200.0,
                Background = 10.0,
                StarContaminationSuspected = contaminated
            };

            var detected = HocusFocusStarDetection.ToDetectedStar(star) as HocusFocusDetectedStar;

            Assert.That(detected, Is.Not.Null);
            Assert.That(detected.StarContaminationSuspected, Is.EqualTo(contaminated));
        }

        [Test]
        public void HocusFocusStarDetectionResult_DefaultsAreNaN() {
            var r = new HocusFocusStarDetectionResult();
            Assert.Multiple(() => {
                Assert.That(double.IsNaN(r.PSFRSquared), Is.True);
                Assert.That(double.IsNaN(r.Sigma), Is.True);
                Assert.That(double.IsNaN(r.FWHM), Is.True);
                Assert.That(double.IsNaN(r.FWHMMAD), Is.True);
                Assert.That(double.IsNaN(r.Eccentricity), Is.True);
                Assert.That(double.IsNaN(r.EccentricityMAD), Is.True);
                Assert.That(double.IsNaN(r.PixelSize), Is.True);
                Assert.That(double.IsNaN(r.PixelScale), Is.True);
                Assert.That(r.PSFType, Is.EqualTo(StarDetectorPSFFitType.Moffat_40));
                Assert.That(r.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.Median));
            });
        }
    }
}
