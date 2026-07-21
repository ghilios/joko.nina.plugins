using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using NSubstitute;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class PerFilterDetectionResolutionTests {

        private static IRenderedImage MakeImage(string filterName) {
            var metaData = new ImageMetaData();
            metaData.FilterWheel.Filter = filterName;
            var imageData = Substitute.For<IImageData>();
            imageData.MetaData.Returns(metaData);
            var image = Substitute.For<IRenderedImage>();
            image.RawImageData.Returns(imageData);
            return image;
        }

        private static (HocusFocusStarDetection detection, IPerFilterStarDetectionStore store) Build(IStarDetectionOptions liveOptions) {
            var profileService = Substitute.For<IProfileService>();
            profileService.ActiveProfile.CameraSettings.PixelSize.Returns(3.76);
            profileService.ActiveProfile.TelescopeSettings.FocalLength.Returns(1000d);
            profileService.ActiveProfile.ApplicationSettings.SelectedPluggableBehaviors
                .Returns(new AsyncObservableCollection<KeyValuePair<string, string>>());
            var focuserMediator = Substitute.For<IFocuserMediator>();
            focuserMediator.GetInfo().Returns(new FocuserInfo());
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            var detection = new HocusFocusStarDetection(
                imageStatisticsVM: Substitute.For<IImageStatisticsVM>(),
                profileService: profileService,
                focuserMediator: focuserMediator,
                starDetectionOptions: liveOptions,
                alglibAPI: new AlglibAPI(),
                perFilterStore: store);
            return (detection, store);
        }

        [Test]
        public void FeatureOff_ParamsBitIdenticalToDirectBuild() {
            var profile = Substitute.For<IProfileService>();
            var accessor = new InMemoryPluginOptionsAccessor();
            var options = new StarDetectionOptions(profile, accessor);
            options.UseAdvanced = true;
            options.NoiseClippingMultiplier = 5.5;
            options.StructureLayers = 6;

            var (detection, store) = Build(options);
            store.Enabled.Returns(false);

            var expected = HocusFocusStarDetection.BuildStarDetectorParams(options);
            expected.PixelScale = MathUtility.ArcsecPerPixel(3.76, 1000);
            expected.Region = StarDetectionRegion.Full;

            var actual = detection.GetStarDetectorParams(MakeImage("Ha"), StarDetectionRegion.Full, isAutoFocus: false);

            Assert.Multiple(() => {
                // The cache key canonicalizes every output-affecting param (region + pixel scale included),
                // so key equality is the codebase's own bit-identity check for detector params.
                Assert.That(StarDetector.ComputeCacheKey(actual), Is.EqualTo(StarDetector.ComputeCacheKey(expected)));
                // The two denylisted (output-neutral) fields are outside the key; compare them directly.
                Assert.That(actual.StoreStructureMap, Is.EqualTo(expected.StoreStructureMap));
                Assert.That(actual.SaveIntermediateFilesPath, Is.EqualTo(expected.SaveIntermediateFilesPath));
                store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
            });
        }

        [Test]
        public void FeatureOn_UsesFilterSnapshot_MachineLocalFromLiveOptions() {
            var liveOptions = new StarDetectionSettingsSnapshot {
                NoiseClippingMultiplier = 4.0,
                DebugMode = true,
                SaveIntermediateImages = true,
                IntermediateSavePath = @"C:\hf-debug",
                PSFParallelPartitionSize = 250
            };
            var snapshot = new StarDetectionSettingsSnapshot {
                NoiseClippingMultiplier = 7.5, // distinctive per-filter knob
                DebugMode = false,             // stored snapshots hold scrubbed machine-local values
                SaveIntermediateImages = false,
                IntermediateSavePath = "",
                PSFParallelPartitionSize = 100,
                PixelSampleSize = 1.0
            };
            var (detection, store) = Build(liveOptions);
            store.Enabled.Returns(true);
            store.GetOrSeedSnapshot("Ha").Returns(snapshot);

            var actual = detection.GetStarDetectorParams(MakeImage("Ha"), StarDetectionRegion.Full, isAutoFocus: false);

            Assert.Multiple(() => {
                Assert.That(actual.NoiseClippingMultiplier, Is.EqualTo(7.5));             // filter snapshot knob
                Assert.That(actual.StoreStructureMap, Is.True);                           // DebugMode from live options
                Assert.That(actual.SaveIntermediateFilesPath, Is.EqualTo(@"C:\hf-debug"));// Save* from live options
                Assert.That(actual.PSFParallelPartitionSize, Is.EqualTo(250));            // machine-local from live options
                Assert.That(liveOptions.SaveIntermediateImages, Is.False);                // one-shot reset targets the live options
            });
        }

        [Test]
        public void FeatureOn_EmptyFilterName_GetStarDetectorParamsThrows() {
            var (detection, store) = Build(new StarDetectionSettingsSnapshot());
            store.Enabled.Returns(true);

            var ex = Assert.Throws<PerFilterSettingsUnavailableException>(
                () => detection.GetStarDetectorParams(MakeImage(""), StarDetectionRegion.Full, isAutoFocus: false));

            Assert.Multiple(() => {
                Assert.That(ex.Message, Is.EqualTo("Per-filter star detection is enabled but the active filter is unknown - connect a filter wheel."));
                store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
            });
        }

        [Test]
        public void FeatureOn_OptionsOverrideWins_NoStoreConsult() {
            var (detection, store) = Build(new StarDetectionSettingsSnapshot());
            store.Enabled.Returns(true);
            var overrideSnapshot = new StarDetectionSettingsSnapshot { NoiseClippingMultiplier = 9.25, PixelSampleSize = 1.0 };

            // Even with an indeterminate filter, an explicit override wins verbatim — replay never consults the store.
            var actual = detection.GetStarDetectorParams(MakeImage(""), StarDetectionRegion.Full, isAutoFocus: false, optionsOverride: overrideSnapshot);

            Assert.Multiple(() => {
                Assert.That(actual.NoiseClippingMultiplier, Is.EqualTo(9.25));
                store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
            });
        }

        [Test]
        public async Task FeatureOn_EmptyFilterName_InterfaceDetectSoftFailsWithEmptyResult() {
            var (detection, store) = Build(new StarDetectionSettingsSnapshot());
            store.Enabled.Returns(true);
            var p = new StarDetectionParams { IsAutoFocus = false };

            // The NINA-facing entry point must NEVER throw into the imaging pipeline: it returns a valid
            // zero-star result. (Notification.ShowWarning is a headless no-op under test — NINA's manager is
            // null without Application.Current — so the observable contract here is the returned result.)
            var result = await detection.Detect(MakeImage(""), PixelFormats.Gray16, p, progress: null, token: CancellationToken.None);

            Assert.Multiple(() => {
                Assert.That(result, Is.InstanceOf<HocusFocusStarDetectionResult>());
                Assert.That(result.DetectedStars, Is.EqualTo(0));
                Assert.That(result.StarList, Is.Empty);
                Assert.That(result.Params, Is.SameAs(p));
            });
        }
    }
}
