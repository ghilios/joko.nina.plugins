#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Equipment.Utility;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator {

    /// <summary>
    /// Synthetic monochrome star-field camera. On each exposure it snapshots the connected focuser (defocus)
    /// and mount (pointing) and hands the immutable <see cref="RenderRequest"/> to an
    /// <see cref="IStarFieldCompositor"/>. Sensor-derived capabilities (resolution, pixel size, bit depth,
    /// electrons-per-ADU, gain bounds) track the selected <see cref="SonySensorModel"/> via
    /// <see cref="SensorRegistry"/>. An exposure fails with a descriptive error if the focuser or mount is
    /// disconnected.
    /// </summary>
    /// <remarks>
    /// Sensor-derived capabilities read the selected sensor's definition live on each get, but the camera does
    /// not subscribe to <see cref="ICameraSimulatorOptions.PropertyChanged"/> and does not raise its own
    /// <c>PropertyChanged</c> when options change. NINA snapshots CameraInfo via those notifications, so the
    /// reported geometry is effectively latched at the values in force when the camera is connected — this is a
    /// configure-before-connect device: pick the sensor/gain first, then connect.
    /// </remarks>
    public class HocusFocusSimulatorCamera : BaseINPC, ICamera {
        private readonly IProfileService profileService;
        private readonly IExposureDataFactory exposureDataFactory;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly ICameraSimulatorOptions options;
        private readonly IStarFieldCompositor compositor;

        private readonly IList<string> supportedActions = Array.Empty<string>();
        private readonly IList<string> readoutModes = new List<string> { "Default" };
        private readonly IList<int> gains = Array.Empty<int>();
        private readonly AsyncObservableCollection<BinningMode> binningModes =
            new AsyncObservableCollection<BinningMode> { new BinningMode(1, 1) };

        public HocusFocusSimulatorCamera(
            IProfileService profileService,
            IExposureDataFactory exposureDataFactory,
            IImageDataFactory imageDataFactory,
            ITelescopeMediator telescopeMediator,
            IFocuserMediator focuserMediator,
            ICameraSimulatorOptions options)
            // The reader is built per exposure from the request's catalog-path snapshot (not latched here), so a
            // change to the option takes effect on the next exposure without reconnecting the camera.
            : this(profileService, exposureDataFactory, imageDataFactory, telescopeMediator, focuserMediator, options,
                  new StarFieldCompositor(path => new AstapCatalogReader(path ?? CameraSimulatorOptions.DefaultAstapCatalogPath))) {
        }

        internal HocusFocusSimulatorCamera(
            IProfileService profileService,
            IExposureDataFactory exposureDataFactory,
            IImageDataFactory imageDataFactory,
            ITelescopeMediator telescopeMediator,
            IFocuserMediator focuserMediator,
            ICameraSimulatorOptions options,
            IStarFieldCompositor compositor) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.exposureDataFactory = exposureDataFactory ?? throw new ArgumentNullException(nameof(exposureDataFactory));
            // imageDataFactory is part of the DI signature but not consumed by the camera itself (the ushort[]
            // path returns via IExposureDataFactory). Validated for fail-fast wiring, not stored.
            _ = imageDataFactory ?? throw new ArgumentNullException(nameof(imageDataFactory));
            this.telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            this.focuserMediator = focuserMediator ?? throw new ArgumentNullException(nameof(focuserMediator));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
            this.temperatureSetPoint = options.SensorTemperatureCelsius;
        }

        /// <summary>The datasheet definition for the currently-selected sensor. Re-resolved live so all
        /// sensor-derived capabilities change the moment the user picks a different sensor.</summary>
        private SensorDefinition SensorDef => SensorRegistry.Get(options.SensorModel);

        #region Identity (IDevice)

        public bool HasSetupDialog => false;
        public string Id => "HocusFocus_SimulatorCamera";
        public string Name => "Hocus Focus Simulator";
        public string DisplayName => Name;
        public string Category => "Hocus Focus";
        public string Description => "Synthetic star-field camera provided by the Hocus Focus plugin. Reads the connected focuser and mount to render physically-known frames.";
        public string DriverInfo => "Hocus Focus synthetic camera";
        public string DriverVersion => "1.0";
        public IList<string> SupportedActions => supportedActions;

        private bool connected;

        public bool Connected {
            get => connected;
            private set {
                if (connected != value) {
                    connected = value;
                    RaisePropertyChanged();
                }
            }
        }

        public Task<bool> Connect(CancellationToken token) {
            Connected = true;
            CameraState = CameraStates.Idle;
            return Task.FromResult(true);
        }

        public void Disconnect() {
            pendingRender = null;
            CameraState = CameraStates.NoState;
            Connected = false;
        }

        public void SetupDialog() {
        }

        public string Action(string actionName, string actionParameters) => throw new NotImplementedException();

        public string SendCommandString(string command, bool raw = true) => string.Empty;

        public bool SendCommandBool(string command, bool raw = true) => false;

        public void SendCommandBlind(string command, bool raw = true) {
        }

        #endregion Identity (IDevice)

        #region Sensor-derived capabilities

        public string SensorName => SensorDef.SensorName;
        public SensorType SensorType => SensorType.Monochrome;
        public short BayerOffsetX => 0;
        public short BayerOffsetY => 0;
        public int CameraXSize => SensorDef.Width;
        public int CameraYSize => SensorDef.Height;
        public double PixelSizeX => SensorDef.PixelSizeMicrons;
        public double PixelSizeY => SensorDef.PixelSizeMicrons;
        public int BitDepth => SensorDef.BitDepth;
        public double ElectronsPerADU => SensorDef.ElectronsPerAduAtGain(options.Gain);

        #endregion Sensor-derived capabilities

        #region Gain / offset / readout

        public bool CanGetGain => true;
        public bool CanSetGain => true;
        public int GainMin => 0;
        public int GainMax => SensorDef.MaxGain;

        public int Gain {
            get => options.Gain;
            set {
                var clamped = Math.Clamp(value, GainMin, GainMax);
                if (options.Gain != clamped) {
                    options.Gain = clamped;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(ElectronsPerADU));
                }
            }
        }

        public IList<int> Gains => gains;

        public bool CanSetOffset => false;
        private int offset;

        public int Offset {
            get => offset;
            set {
                if (offset != value) {
                    offset = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int OffsetMin => 0;
        public int OffsetMax => 0;

        public bool CanSetUSBLimit => false;
        private int usbLimit;

        public int USBLimit {
            get => usbLimit;
            set {
                if (usbLimit != value) {
                    usbLimit = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int USBLimitMin => 0;
        public int USBLimitMax => 0;
        public int USBLimitStep => 0;

        public IList<string> ReadoutModes => readoutModes;

        private short readoutMode;

        public short ReadoutMode {
            get => readoutMode;
            set {
                if (readoutMode != value) {
                    readoutMode = value;
                    RaisePropertyChanged();
                }
            }
        }

        private short readoutModeForSnapImages;

        public short ReadoutModeForSnapImages {
            get => readoutModeForSnapImages;
            set {
                if (readoutModeForSnapImages != value) {
                    readoutModeForSnapImages = value;
                    RaisePropertyChanged();
                }
            }
        }

        private short readoutModeForNormalImages;

        public short ReadoutModeForNormalImages {
            get => readoutModeForNormalImages;
            set {
                if (readoutModeForNormalImages != value) {
                    readoutModeForNormalImages = value;
                    RaisePropertyChanged();
                }
            }
        }

        #endregion Gain / offset / readout

        #region Binning

        private short binX = 1;

        public short BinX {
            get => binX;
            set {
                if (binX != value) {
                    binX = value;
                    RaisePropertyChanged();
                }
            }
        }

        private short binY = 1;

        public short BinY {
            get => binY;
            set {
                if (binY != value) {
                    binY = value;
                    RaisePropertyChanged();
                }
            }
        }

        public short MaxBinX => 1;
        public short MaxBinY => 1;
        public AsyncObservableCollection<BinningMode> BinningModes => binningModes;

        public void SetBinning(short x, short y) {
            BinX = x;
            BinY = y;
        }

        #endregion Binning

        #region Temperature / cooling

        public bool HasShutter => false;
        public double Temperature => options.SensorTemperatureCelsius;

        private double temperatureSetPoint;

        public double TemperatureSetPoint {
            get => temperatureSetPoint;
            set {
                if (temperatureSetPoint != value) {
                    temperatureSetPoint = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool CanSetTemperature => false;

        private bool coolerOn;

        public bool CoolerOn {
            get => coolerOn;
            set {
                if (coolerOn != value) {
                    coolerOn = value;
                    RaisePropertyChanged();
                }
            }
        }

        public double CoolerPower => 0.0;
        public bool HasDewHeater => false;

        private bool dewHeaterOn;

        public bool DewHeaterOn {
            get => dewHeaterOn;
            set {
                if (dewHeaterOn != value) {
                    dewHeaterOn = value;
                    RaisePropertyChanged();
                }
            }
        }

        #endregion Temperature / cooling

        #region Exposure limits / sub-sampling / misc capabilities

        public double ExposureMin => 0.0;
        public double ExposureMax => double.MaxValue;

        public bool CanSubSample => false;
        private bool enableSubSample;

        public bool EnableSubSample {
            get => enableSubSample;
            set {
                if (enableSubSample != value) {
                    enableSubSample = value;
                    RaisePropertyChanged();
                }
            }
        }

        private int subSampleX;
        public int SubSampleX { get => subSampleX; set { subSampleX = value; RaisePropertyChanged(); } }

        private int subSampleY;
        public int SubSampleY { get => subSampleY; set { subSampleY = value; RaisePropertyChanged(); } }

        private int subSampleWidth;
        public int SubSampleWidth { get => subSampleWidth; set { subSampleWidth = value; RaisePropertyChanged(); } }

        private int subSampleHeight;
        public int SubSampleHeight { get => subSampleHeight; set { subSampleHeight = value; RaisePropertyChanged(); } }

        public void UpdateSubSampleArea() {
        }

        public bool CanShowLiveView => false;
        public bool LiveViewEnabled => false;
        public bool HasBattery => false;
        public int BatteryLevel => -1;

        #endregion Exposure limits / sub-sampling / misc capabilities

        #region Exposure state machine

        private CameraStates cameraState = CameraStates.NoState;

        public CameraStates CameraState {
            get => cameraState;
            private set {
                if (cameraState != value) {
                    cameraState = value;
                    RaisePropertyChanged();
                }
            }
        }

        private RenderRequest pendingRender;
        private DateTime exposureStartTime;
        private double exposureLengthSeconds;

        public void StartExposure(CaptureSequence sequence) {
            if (!Connected) {
                throw new InvalidOperationException("Cannot start an exposure: the synthetic camera is not connected.");
            }

            exposureStartTime = DateTime.UtcNow;
            exposureLengthSeconds = sequence?.ExposureTime ?? 0.0;
            pendingRender = BuildRenderRequest(exposureLengthSeconds);
            CameraState = CameraStates.Exposing;
        }

        public async Task WaitUntilExposureIsReady(CancellationToken token) {
            if (CameraState != CameraStates.Exposing) {
                return;
            }

            var remaining = exposureLengthSeconds - (DateTime.UtcNow - exposureStartTime).TotalSeconds;
            if (remaining > 0) {
                CameraState = CameraStates.Waiting;
                // Let cancellation propagate: the caller is aborting the exposure, so we must NOT advance to
                // Reading. The state is left at Waiting for the aborting caller to reset via Stop/AbortExposure.
                await Task.Delay(TimeSpan.FromSeconds(remaining), token).ConfigureAwait(false);
            }

            CameraState = CameraStates.Reading;
        }

        public void StopExposure() {
            pendingRender = null;
            if (Connected) {
                CameraState = CameraStates.Idle;
            }
        }

        public void AbortExposure() {
            pendingRender = null;
            if (Connected) {
                CameraState = CameraStates.Idle;
            }
        }

        public async Task<IExposureData> DownloadExposure(CancellationToken token) {
            var request = pendingRender
                ?? throw new InvalidOperationException("StartExposure must be called before DownloadExposure.");

            // Descriptive failures: the synthetic camera cannot render without a focuser (defocus) or a mount
            // (pointing). These guards run BEFORE the compositor so a missing device produces a clear message
            // rather than a rendering error.
            if (!request.FocuserConnected) {
                CameraState = CameraStates.Error;
                throw new InvalidOperationException(
                    "Cannot render a synthetic exposure: no focuser is connected. The synthetic camera reads the " +
                    "focuser position to compute defocus — connect a focuser (e.g. the NINA simulator focuser).");
            }
            if (!request.TelescopeConnected) {
                CameraState = CameraStates.Error;
                throw new InvalidOperationException(
                    "Cannot render a synthetic exposure: no telescope/mount is connected. The synthetic camera reads " +
                    "the mount pointing to project catalog stars — connect a mount (e.g. the NINA simulator telescope).");
            }

            CameraState = CameraStates.Download;
            try {
                // Geometry MUST come from the render SNAPSHOT's sensor, not the live options. The user can switch
                // SensorModel between StartExposure and DownloadExposure; the rendered pixel array is sized from
                // request.SensorModel, so reading width/height/bit-depth from the live CameraXSize/CameraYSize/BitDepth
                // (which follow options.SensorModel) would desync the array from its declared dimensions.
                var snapshotSensor = SensorRegistry.Get(request.SensorModel);
                var width = snapshotSensor.Width;
                var height = snapshotSensor.Height;
                var bitDepth = snapshotSensor.BitDepth;
                var pixels = await Task.Run(() => compositor.Render(request, token), token).ConfigureAwait(false);

                var metaData = new ImageMetaData();
                metaData.FromCamera(this);
                metaData.Image.SetExposureTimes(exposureStartTime, DateTime.UtcNow);
                var exposureData = exposureDataFactory.CreateImageArrayExposureData(
                    pixels, width, height, bitDepth, isBayered: false, metaData);
                CameraState = CameraStates.Idle;
                return exposureData;
            } catch {
                // The render or the metadata/exposure-data build threw. Reset the state to Error — matching the
                // disconnected guards above — so the camera never reports Download while it is actually idle,
                // then propagate.
                CameraState = CameraStates.Error;
                throw;
            }
        }

        /// <summary>
        /// Build the immutable per-exposure snapshot from the live focuser/telescope state and the resolved
        /// option values. Only reads the NINA profile; never mutates it.
        /// </summary>
        private RenderRequest BuildRenderRequest(double exposureSeconds) {
            var focuserInfo = focuserMediator.GetInfo();
            var telescopeInfo = telescopeMediator.GetInfo();

            var focuserConnected = focuserInfo?.Connected ?? false;
            var focuserPosition = focuserInfo?.Position ?? 0;

            var telescopeConnected = telescopeInfo?.Connected ?? false;
            double raDeg = 0.0, decDeg = 0.0;
            if (telescopeConnected) {
                var coordinates = telescopeInfo.Coordinates;
                if (coordinates != null) {
                    var j2000 = coordinates.Transform(Epoch.J2000);
                    raDeg = j2000.RADegrees;
                    decDeg = j2000.Dec;
                } else {
                    // Fall back to the raw RA (hours → degrees) / Dec when Coordinates is not populated.
                    raDeg = telescopeInfo.RightAscension * 15.0;
                    decDeg = telescopeInfo.Declination;
                }
            }

            var focalLength = options.FocalLengthMillimeters > 0.0
                ? options.FocalLengthMillimeters
                : profileService.ActiveProfile.TelescopeSettings.FocalLength;

            return new RenderRequest {
                FocuserConnected = focuserConnected,
                FocuserPosition = focuserPosition,
                TelescopeConnected = telescopeConnected,
                RaDegreesJ2000 = raDeg,
                DecDegreesJ2000 = decDeg,
                ApertureMillimeters = options.ApertureMillimeters,
                FocalLengthMillimeters = focalLength,
                CentralObstructionEnabled = options.CentralObstructionEnabled,
                CentralObstructionFraction = options.CentralObstructionFraction,
                OpticalThroughput = options.OpticalThroughput,
                SensorModel = options.SensorModel,
                Gain = options.Gain,
                BiasPedestalAdu = options.BiasPedestalAdu,
                SensorTemperatureCelsius = options.SensorTemperatureCelsius,
                Filter = options.Filter,
                SkyBrightnessMagPerArcsec2 = options.SkyBrightnessMagPerArcsec2,
                SeeingArcsec = options.SeeingArcsec,
                OptimalFocuserPosition = options.OptimalFocuserPosition,
                FocuserStepSizeMicrons = options.FocuserStepSizeMicrons,
                AstapCatalogPath = options.AstapCatalogPath,
                LimitingMagnitude = options.LimitingMagnitude,
                RotationDegrees = options.RotationDegrees,
                NoiseSeed = options.NoiseSeed,
                AberrationsEnabled = options.EnableAberrations,
                TiltAngleDegrees = options.TiltAngleDegrees,
                TiltAmountMicrons = options.TiltAmountMicrons,
                BackfocusErrorMicrons = options.BackfocusErrorMicrons,
                OpticalAxisOffsetXMicrons = options.OpticalAxisOffsetXMicrons,
                OpticalAxisOffsetYMicrons = options.OpticalAxisOffsetYMicrons,
                ExposureSeconds = exposureSeconds
            };
        }

        #endregion Exposure state machine

        #region Live view (unsupported)

        public void StartLiveView(CaptureSequence sequence) => throw new NotSupportedException("The synthetic camera does not support live view.");

        public Task<IExposureData> DownloadLiveView(CancellationToken token) => throw new NotSupportedException("The synthetic camera does not support live view.");

        public void StopLiveView() {
        }

        #endregion Live view (unsupported)
    }
}
