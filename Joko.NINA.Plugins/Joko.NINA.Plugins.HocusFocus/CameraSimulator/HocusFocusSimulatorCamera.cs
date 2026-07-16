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
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Exceptions;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Equipment.Utility;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

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
        private readonly IExposureDataFactory exposureDataFactory;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly ICameraSimulatorOptions options;
        private readonly IStarFieldCompositor compositor;

        // WindowServiceFactory is not a MEF export (NINA exposes the concrete type with a default ctor only), so it
        // is instantiated directly here, mirroring HocusFocusPlugin/StarDetectionOptionsVM. Constructing the factory
        // is inert — the dispatcher capture happens in Create(), inside SetupDialog(); see the remarks there.
        private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();

        // These MUST be backed by a concrete List<T>, not an array. ICamera/IDevice declare them as IList<T>,
        // but NINA's binding layer hard-casts the bound value to List<T> — NINA.Core's
        // IntListToTextBlockListConverter does `((List<int>)value).Select(...)`, so an int[] (e.g.
        // Array.Empty<int>()) satisfies the interface yet throws InvalidCastException on the UI thread and
        // takes NINA down when the camera panel binds Gains. Keep the concrete type; see the tests that pin it.
        private readonly IList<string> supportedActions = new List<string>();
        private readonly IList<string> readoutModes = new List<string> { "Default" };
        // Empty = no discrete gain steps; NINA falls back to the GainMin..GainMax slider (the ASI/QHY convention).
        private readonly IList<int> gains = new List<int>();
        private readonly AsyncObservableCollection<BinningMode> binningModes =
            new AsyncObservableCollection<BinningMode> { new BinningMode(1, 1) };

        private readonly Lazy<SimulatedTiltAdapterVM> tiltAdapterVM;

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
            // profileService is part of the DI signature (HocusFocusSimulatorCameraProvider passes its MEF import)
            // but is no longer consumed by the camera itself: the telescope focal-length/focal-ratio fallback moved
            // into CameraSimulatorOptions, so the setup dialog's hint and the render resolve optics through one
            // shared property and cannot drift apart. Validated for fail-fast wiring, not stored.
            _ = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.exposureDataFactory = exposureDataFactory ?? throw new ArgumentNullException(nameof(exposureDataFactory));
            // imageDataFactory is part of the DI signature but not consumed by the camera itself (the ushort[]
            // path returns via IExposureDataFactory). Validated for fail-fast wiring, not stored.
            _ = imageDataFactory ?? throw new ArgumentNullException(nameof(imageDataFactory));
            this.telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            this.focuserMediator = focuserMediator ?? throw new ArgumentNullException(nameof(focuserMediator));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
            this.temperatureSetPoint = options.SensorTemperatureCelsius;
            tiltAdapterVM = new Lazy<SimulatedTiltAdapterVM>(() => new SimulatedTiltAdapterVM(this.options));
        }

        /// <summary>The datasheet definition for the currently-selected sensor. Re-resolved live so all
        /// sensor-derived capabilities change the moment the user picks a different sensor.</summary>
        private SensorDefinition SensorDef => SensorRegistry.Get(options.SensorModel);

        /// <summary>
        /// The simulator options, exposed so the setup dialog's DataTemplate can bind the rig fields. The dialog
        /// is shown with the camera itself as its content, so the template reaches the options through here.
        /// </summary>
        public ICameraSimulatorOptions Options => options;

        /// <summary>
        /// Backs the simulated tilt-adapter panel hosted by the setup dialog. The Imaging dockable hosts the same
        /// control but builds its own VM — the two share state through the options singleton, which is where the
        /// injected plane actually lives.
        /// </summary>
        /// <remarks>
        /// Built on first bind rather than in the constructor, deliberately. The equipment provider constructs a
        /// fresh camera on every rescan, whereas this panel is only needed once the user opens the setup dialog;
        /// and the VM subscribes to the plugin's process-lifetime options singletons for as long as it lives, so
        /// constructing one per rescan would retain every one of them. Lazy&lt;T&gt; rather than a bare ??=: the
        /// getter is a WPF binding evaluated on the dispatcher thread, but nothing in the type's contract promises
        /// that, and the default Lazy mode makes the question moot.
        /// </remarks>
        public SimulatedTiltAdapterVM TiltAdapterVM => tiltAdapterVM.Value;

        #region Identity (IDevice)

        /// <summary>
        /// True so NINA renders the gear button next to the camera in the Equipment &gt; Camera pane. The
        /// physical-rig options (sensor + optics) are edited only there — they render read-only in the plugin
        /// Options tab — so this is the sole entry point to them. NINA's <c>DeviceChooserVM.SetupDialog()</c>
        /// early-returns when this is false.
        /// </summary>
        public bool HasSetupDialog => true;

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
            exposureCounter = 0;
            return Task.FromResult(true);
        }

        public void Disconnect() {
            CancelPendingExposure();
            CameraState = CameraStates.NoState;
            Connected = false;
        }

        /// <summary>
        /// Shows the rig setup window. Rig settings live here rather than the plugin Options tab so they sit with
        /// the device they describe.
        /// </summary>
        /// <remarks>
        /// NINA invokes this on a dedicated STA thread that it then Joins (DeviceChooserVM.SetupDialog wraps the
        /// thread + Join in a Task.Run, so the Join blocks a pool thread and the UI thread stays free to pump —
        /// marshalling back to it here does not deadlock). We must NOT build WPF on that STA thread: it dies the
        /// moment this returns, and anything created on it would die with it. NINA's WindowService is the safe
        /// path — its constructor captures Application.Current.Dispatcher (the main dispatcher) regardless of the
        /// thread that builds it, and Show() marshals the window creation there. Show is non-modal, so it returns
        /// as soon as the window exists: the STA thread ends while the window stays up. This mirrors NINA's own
        /// SimulatorCamera, which does exactly the same thing.
        ///
        /// The service is created here rather than held as a field so the capture happens against a live
        /// Application (the camera itself is constructed during an equipment rescan, and in unit tests where
        /// Application.Current is null), matching the IWindowServiceFactory pattern used elsewhere in the plugin.
        /// </remarks>
        public void SetupDialog() {
            windowServiceFactory.Create().Show(
                this, "Hocus Focus Simulator Setup", ResizeMode.NoResize, WindowStyle.ToolWindow);
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

        /// <summary>
        /// One exposure's render snapshot: the request, the render started for it, and the source that cancels
        /// that render. These three MUST travel together — <see cref="DownloadExposure"/> sizes the frame from
        /// <see cref="Request"/> but fills it from <see cref="RenderTask"/>, so reading them as separate fields
        /// let a concurrent <see cref="StartExposure"/> land between the reads and pair one request's dimensions
        /// with another request's pixels. Publishing them as a single reference makes that unrepresentable.
        /// <see cref="RenderTask"/>/<see cref="Cts"/> are null exactly when the prefetch was skipped for a
        /// missing device — the case <see cref="DownloadExposure"/>'s guards reject.
        /// </summary>
        private sealed record PendingExposure(RenderRequest Request, Task<ushort[]> RenderTask, CancellationTokenSource Cts);

        private PendingExposure pending;
        private DateTime exposureStartTime;
        private double exposureLengthSeconds;

        // Advanced once per StartExposure so repeated exposures at one focuser position get their own noise.
        // Reset on Connect, so a fixed sequence of exposures taken after connecting replays identically.
        private int exposureCounter;

        public void StartExposure(CaptureSequence sequence) {
            if (!Connected) {
                throw new InvalidOperationException("Cannot start an exposure: the synthetic camera is not connected.");
            }

            CancelPendingExposure();
            exposureStartTime = DateTime.UtcNow;
            exposureLengthSeconds = sequence?.ExposureTime ?? 0.0;
            unchecked { ++exposureCounter; }
            var request = BuildRenderRequest(exposureLengthSeconds);

            // Render NOW rather than in DownloadExposure. BuildRenderRequest has already snapshotted everything
            // Render reads, and Render touches no ambient state, so the pixels are byte-identical either way —
            // this only moves the render out from after the exposure and under it. Skipped when a required device
            // is missing so DownloadExposure's descriptive guards still fire, unchanged, without burning cores on
            // a frame nobody can use.
            Task<ushort[]> renderTask = null;
            CancellationTokenSource cts = null;
            if (request.FocuserConnected && request.TelescopeConnected) {
                cts = new CancellationTokenSource();
                var token = cts.Token;
                renderTask = Task.Run(() => compositor.Render(request, token), token);
                // Observe the fault even if the exposure is aborted and nobody ever awaits this task, so a render
                // failure cannot resurface later as an unobserved TaskException.
                _ = renderTask.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            }

            // Published as one reference, only once the request/task/cts are all built, so a download can never
            // observe a half-built exposure or mix two of them.
            pending = new PendingExposure(request, renderTask, cts);

            CameraState = CameraStates.Exposing;
        }

        /// <summary>
        /// Drops the pending exposure and cancels its prefetched render. Clearing the snapshot and cancelling the
        /// render are one operation because they are one object — no call site can drop the request but forget the
        /// render, or vice versa.
        ///
        /// <para>The CTS is deliberately not disposed: disposing here would race the render task that is still
        /// observing its token (<c>ThrowIfCancellationRequested</c> on a disposed source throws
        /// <see cref="ObjectDisposedException"/>, which would surface as a spurious exposure failure). Letting the
        /// GC reclaim it is safe because it carries no timer, and no wait handle so long as no
        /// <see cref="IStarFieldCompositor.Render"/> implementation touches <c>token.WaitHandle</c> — a property of
        /// the compositors, not of the CTS itself. The production compositor does not.</para>
        /// </summary>
        private void CancelPendingExposure() {
            var snapshot = pending;
            pending = null;
            snapshot?.Cts?.Cancel();
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
            CancelPendingExposure();
            if (Connected) {
                CameraState = CameraStates.Idle;
            }
        }

        public void AbortExposure() {
            CancelPendingExposure();
            if (Connected) {
                CameraState = CameraStates.Idle;
            }
        }

        public async Task<IExposureData> DownloadExposure(CancellationToken token) {
            // Read the published snapshot exactly ONCE: the request that sizes the frame, the render that fills
            // it, and the source that cancels that render must all belong to the same exposure. Re-reading the
            // field would let a concurrent StartExposure swap it mid-method.
            var snapshot = pending
                ?? throw new InvalidOperationException("StartExposure must be called before DownloadExposure.");
            var request = snapshot.Request;

            // Descriptive failures: the synthetic camera cannot render without a focuser (defocus) or a mount
            // (pointing). These guards run BEFORE the compositor so a missing device produces a clear message
            // rather than a rendering error.
            //
            // These MUST be CameraExposureFailedException (NINA.Equipment.Exceptions), not a bare exception.
            // NINA's ImagingVM.CaptureImage switches on the exception type:
            //   catch (CameraExposureFailedException) -> Notification.ShowError(ex.Message)          // our text, verbatim
            //   catch (Exception)                     -> Notification.ShowError("Unexpected error" + ex.Message);
            //                                            cameraMediator.AbortExposure();
            // A bare exception therefore renders a user-actionable "connect a focuser" prompt as an *unexpected
            // plugin error* and triggers a spurious AbortExposure. The typed exception is the contract for
            // "this exposure cannot be taken, and here's why".
            if (!request.FocuserConnected) {
                CameraState = CameraStates.Error;
                throw new CameraExposureFailedException(
                    "Cannot render a synthetic exposure: no focuser is connected. The synthetic camera reads the " +
                    "focuser position to compute defocus — connect a focuser (e.g. the NINA simulator focuser).");
            }
            if (!request.TelescopeConnected) {
                CameraState = CameraStates.Error;
                throw new CameraExposureFailedException(
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
                // Running since StartExposure. RenderTask is non-null by construction here: StartExposure gates the
                // prefetch on the very FocuserConnected/TelescopeConnected fields the guards above just tested, on
                // this same snapshot's request — so a snapshot that reaches this line always carries its render.
                //
                // Route the download's cancellation into the render that is actually doing the work, rather than
                // just abandoning the await and leaving every core busy on a frame nobody wants.
                ushort[] pixels;
                using (token.Register(() => snapshot.Cts?.Cancel())) {
                    pixels = await snapshot.RenderTask.ConfigureAwait(false);
                }

                var metaData = new ImageMetaData();
                metaData.FromCamera(this);
                metaData.Image.SetExposureTimes(exposureStartTime, DateTime.UtcNow);
                var exposureData = exposureDataFactory.CreateImageArrayExposureData(
                    pixels, width, height, bitDepth, isBayered: false, metaData);
                // The frame now belongs to the caller, so stop rooting it: at 61 MP the array is ~122 MB, and
                // holding it until the next StartExposure keeps it alive for as long as the camera sits idle.
                // Conditional so a StartExposure that has already published the NEXT exposure is not wiped by this
                // download's cleanup.
                Interlocked.CompareExchange(ref pending, null, snapshot);
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

            return new RenderRequest {
                FocuserConnected = focuserConnected,
                FocuserPosition = focuserPosition,
                TelescopeConnected = telescopeConnected,
                RaDegreesJ2000 = raDeg,
                DecDegreesJ2000 = decDeg,
                // Resolved by the options object, which is also what the setup dialog's hint text displays —
                // so what the user is shown and what is rendered cannot drift apart.
                ApertureMillimeters = options.EffectiveApertureMillimeters,
                FocalLengthMillimeters = options.EffectiveFocalLengthMillimeters,
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
                // The option is the BASE seed, not the frame seed. Mixing in the focuser position and a
                // per-exposure counter gives every frame its own noise while keeping a fixed exposure sequence
                // reproducible from the base seed. It stays a property of the REQUEST rather than of the
                // compositor, so Render remains a pure function of its request — which is what lets the render
                // be started early (see StartExposure) without changing a single pixel.
                NoiseSeed = SeedMixer.Combine(options.NoiseSeed, focuserPosition, exposureCounter),
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
