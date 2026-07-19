using NINA.Core.Interfaces;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NSubstitute;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;

internal sealed class MediatorBundle {
    public IProfileService ProfileService { get; } = Substitute.For<IProfileService>();
    public IApplicationStatusMediator ApplicationStatusMediator { get; } = Substitute.For<IApplicationStatusMediator>();
    public IImagingMediator ImagingMediator { get; } = Substitute.For<IImagingMediator>();
    public ICameraMediator CameraMediator { get; } = Substitute.For<ICameraMediator>();
    public IFocuserMediator FocuserMediator { get; } = Substitute.For<IFocuserMediator>();
    public IFilterWheelMediator FilterWheelMediator { get; } = Substitute.For<IFilterWheelMediator>();
    public ITelescopeMediator TelescopeMediator { get; } = Substitute.For<ITelescopeMediator>();
    public IGuiderMediator GuiderMediator { get; } = Substitute.For<IGuiderMediator>();
    public IImageDataFactory ImageDataFactory { get; } = Substitute.For<IImageDataFactory>();

    public IStarDetectionOptions StarDetectionOptions { get; } = Substitute.For<IStarDetectionOptions>();
    public IStarAnnotatorOptions StarAnnotatorOptions { get; } = Substitute.For<IStarAnnotatorOptions>();
    public IInspectorOptions InspectorOptions { get; } = Substitute.For<IInspectorOptions>();
    public IAutoFocusOptions AutoFocusOptions { get; } = Substitute.For<IAutoFocusOptions>();
    public ITiltAdapterOptions TiltAdapterOptions { get; } = Substitute.For<ITiltAdapterOptions>();
    public IPerFilterStarDetectionStore PerFilterStarDetectionStore { get; } = Substitute.For<IPerFilterStarDetectionStore>();

    public IAutoFocusEngineFactory AutoFocusEngineFactory { get; } = Substitute.For<IAutoFocusEngineFactory>();
    public IPluggableBehaviorSelector<IStarDetection> StarDetectionSelector { get; } = Substitute.For<IPluggableBehaviorSelector<IStarDetection>>();
    public IPluggableBehaviorSelector<IStarAnnotator> StarAnnotatorSelector { get; } = Substitute.For<IPluggableBehaviorSelector<IStarAnnotator>>();
    public IApplicationDispatcher ApplicationDispatcher { get; } = new SynchronousApplicationDispatcher();
    public IAlglibAPI AlglibAPI { get; } = new AlglibAPI();

    public MediatorBundle WithCameraConnected(bool connected = true) {
        CameraMediator.GetInfo().Returns(new CameraInfo { Connected = connected });
        return this;
    }

    public MediatorBundle WithFocuserConnected(bool connected = true, int position = 0) {
        FocuserMediator.GetInfo().Returns(new FocuserInfo { Connected = connected, Position = position });
        return this;
    }

    public MediatorBundle WithTelescopeConnected(bool connected = true) {
        TelescopeMediator.GetInfo().Returns(new TelescopeInfo { Connected = connected });
        return this;
    }

    public MediatorBundle WithFilterWheelConnected(bool connected = true) {
        FilterWheelMediator.GetInfo().Returns(new FilterWheelInfo { Connected = connected });
        return this;
    }

    public MediatorBundle WithGuiderConnected(bool connected = true) {
        GuiderMediator.GetInfo().Returns(new GuiderInfo { Connected = connected });
        return this;
    }

    public MediatorBundle WithPerFilterStarDetectionEnabled(bool enabled = true) {
        PerFilterStarDetectionStore.Enabled.Returns(enabled);
        return this;
    }

    public InspectorVM BuildInspectorVM(
        TiltDeviceConnectionService tiltDeviceConnectionService = null,
        Func<string, string, Task<bool>> confirmPromptAsync = null,
        Func<Func<bool, bool, TiltDevicePlanPreview>, bool, string, bool, double, Task<TiltDeviceAdjustmentChoice>> showAdjustmentPromptAsync = null,
        Func<CancellationToken, Task<bool>> reRunAnalysisAsync = null) {
        return new InspectorVM(
            profileService: ProfileService,
            applicationStatusMediator: ApplicationStatusMediator,
            imagingMediator: ImagingMediator,
            cameraMediator: CameraMediator,
            focuserMediator: FocuserMediator,
            filterWheelMediator: FilterWheelMediator,
            telescopeMediator: TelescopeMediator,
            starDetectionOptions: StarDetectionOptions,
            starAnnotatorOptions: StarAnnotatorOptions,
            inspectorOptions: InspectorOptions,
            autoFocusOptions: AutoFocusOptions,
            autoFocusEngineFactory: AutoFocusEngineFactory,
            imageDataFactory: ImageDataFactory,
            starDetectionSelector: StarDetectionSelector,
            starAnnotatorSelector: StarAnnotatorSelector,
            applicationDispatcher: ApplicationDispatcher,
            alglibAPI: AlglibAPI,
            tiltAdapterOptions: TiltAdapterOptions,
            tiltDeviceConnectionService: tiltDeviceConnectionService,
            confirmPromptAsync: confirmPromptAsync,
            showAdjustmentPromptAsync: showAdjustmentPromptAsync,
            reRunAnalysisAsync: reRunAnalysisAsync);
    }

    public HocusFocusVM BuildHocusFocusVM() {
        return new HocusFocusVM(
            profileService: ProfileService,
            focuserMediator: FocuserMediator,
            autoFocusEngineFactory: AutoFocusEngineFactory,
            autoFocusOptions: AutoFocusOptions,
            starDetectionOptions: StarDetectionOptions,
            filterWheelMediator: FilterWheelMediator,
            applicationStatusMediator: ApplicationStatusMediator,
            starDetectionSelector: StarDetectionSelector,
            alglibAPI: AlglibAPI,
            applicationDispatcher: ApplicationDispatcher,
            perFilterStore: PerFilterStarDetectionStore);
    }
}
