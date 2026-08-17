using NINA.Astrometry;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    /// <summary>
    /// Shared, deterministic scene parameters for the compositor + capstone tests. Uses the smallest sensor
    /// (IMX533, 3008², 14-bit) and a modest refractor so a whole frame renders + detects fast. Stars are placed
    /// by <b>deprojecting</b> target pixels through the exact same <see cref="TanProjection"/> the compositor
    /// builds, so an injected star lands where the test says it does.
    /// </summary>
    internal static class SyntheticCameraTestScene {
        public const SonySensorModel Sensor = SonySensorModel.IMX533;
        public const SimulatorFilter Filter = SimulatorFilter.L;
        public const double PointingRaDeg = 90.0;
        public const double PointingDecDeg = 10.0;
        public const double ApertureMillimeters = 130.0;
        public const double FocalLengthMillimeters = 910.0;
        public const double OpticalThroughput = 0.85;
        public const int Gain = 100;
        public const int BiasPedestalAdu = 500;
        public const double SensorTemperatureCelsius = -10.0;
        public const double SkyBrightnessMagPerArcsec2 = 20.5;
        public const double SeeingArcsec = 2.5;
        public const int OptimalFocuserPosition = 10000;
        public const double FocuserStepSizeMicrons = 5.0;
        public const double LimitingMagnitude = 16.0;
        public const double ExposureSeconds = 2.0;
        public const int NoiseSeed = 12345;
        public const string AstapCatalogPath = "(fake)";

        public static SensorDefinition SensorDef => SensorRegistry.Get(Sensor);

        public static FilterDefinition FilterDef => FilterRegistry.Get(Filter);

        public static TanProjection Projection(double focalLengthMillimeters = FocalLengthMillimeters) {
            var s = SensorDef;
            return new TanProjection(PointingRaDeg, PointingDecDeg, focalLengthMillimeters, s.PixelSizeMicrons, 0.0, s.Width, s.Height);
        }

        /// <summary>Arcsec per pixel for this scene's sensor at <paramref name="focalLengthMillimeters"/>, including
        /// any camera binning. The same quantity <c>HocusFocusStarDetection.ApplyDetectionImageContext</c> derives
        /// from a captured frame, so tests can resolve detection binning exactly the way production does.</summary>
        public static double PixelScaleArcsecPerPixel(double focalLengthMillimeters, int cameraBinning = 1)
            => MathUtility.ArcsecPerPixel(SensorDef.PixelSizeMicrons, focalLengthMillimeters) * Math.Max(1, cameraBinning);

        public static RenderRequest Request(
                int focuserPosition,
                bool aberrationsEnabled = false,
                double tiltAngleDegrees = 0.0,
                double tiltAmountMicrons = 0.0,
                double backfocusErrorMicrons = 0.0,
                double exposureSeconds = ExposureSeconds,
                int noiseSeed = NoiseSeed,
                string astapCatalogPath = AstapCatalogPath,
                double focalLengthMillimeters = FocalLengthMillimeters,
                double apertureMillimeters = ApertureMillimeters,
                double limitingMagnitude = LimitingMagnitude,
                bool astigmatismEnabled = false,
                double astigmatismRatio = 0.0,
                double backfocusSpacingErrorMicrons = AberrationSurface.UnsetSpacingErrorMicrons) {
            return new RenderRequest {
                FocuserConnected = true,
                FocuserPosition = focuserPosition,
                TelescopeConnected = true,
                RaDegreesJ2000 = PointingRaDeg,
                DecDegreesJ2000 = PointingDecDeg,
                ApertureMillimeters = apertureMillimeters,
                FocalLengthMillimeters = focalLengthMillimeters,
                CentralObstructionEnabled = false,
                CentralObstructionFraction = 0.0,
                OpticalThroughput = OpticalThroughput,
                SensorModel = Sensor,
                Gain = Gain,
                BiasPedestalAdu = BiasPedestalAdu,
                SensorTemperatureCelsius = SensorTemperatureCelsius,
                Filter = Filter,
                SkyBrightnessMagPerArcsec2 = SkyBrightnessMagPerArcsec2,
                SeeingArcsec = SeeingArcsec,
                OptimalFocuserPosition = OptimalFocuserPosition,
                FocuserStepSizeMicrons = FocuserStepSizeMicrons,
                AstapCatalogPath = astapCatalogPath,
                LimitingMagnitude = limitingMagnitude,
                RotationDegrees = 0.0,
                NoiseSeed = noiseSeed,
                AberrationsEnabled = aberrationsEnabled,
                TiltAngleDegrees = tiltAngleDegrees,
                TiltAmountMicrons = tiltAmountMicrons,
                BackfocusErrorMicrons = backfocusErrorMicrons,
                AstigmatismEnabled = astigmatismEnabled,
                AstigmatismRatio = astigmatismRatio,
                BackfocusSpacingErrorMicrons = backfocusSpacingErrorMicrons,
                OpticalAxisOffsetXMicrons = 0.0,
                OpticalAxisOffsetYMicrons = 0.0,
                ExposureSeconds = exposureSeconds
            };
        }

        /// <summary>Places a catalog star that projects to (px, py) by deprojecting through the scene projection.</summary>
        public static CatalogStar StarAtPixel(TanProjection projection, double px, double py, double magnitude) {
            var (raDeg, decDeg) = projection.Deproject(px, py);
            return new CatalogStar(new Coordinates(raDeg, decDeg, Epoch.J2000, Coordinates.RAType.Degrees), magnitude, null);
        }
    }
}
