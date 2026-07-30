using NINA.Core.Enum;
using NINA.Image.ImageData;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NSubstitute;
using NUnit.Framework;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Pins the representation every detection path — the live app, the in-NINA wizard's Review step, and every
    /// headless runner — must agree on, which is what <see cref="RenderedImageLoading"/> exists to produce.
    ///
    /// <para><b>The bug these guard.</b> Headless runners (and the wizard's Review step) used to detect on the raw
    /// Bayer MOSAIC for an OSC run while the live optimizer detected on CFA-filtered luminance. On
    /// <c>D:\Autofocus Bank\bobp</c> that moved the landed Sensitivity from the wizard's <c>10.000</c> (27–31 min
    /// stars) to <c>0.0</c> at the search floor (52) — the harness could drive the gate to its floor and harvest
    /// unfiltered hot pixels as faint stars, an incentive that does not exist on the image the app detects on.
    /// The bank-level numbers are verified by running the harness (see <c>.claude/docs/testapp-cli.md</c>); what
    /// is unit-testable is the mechanism underneath them, which is what this fixture holds down:</para>
    /// <list type="bullet">
    /// <item>mono frames are byte-identical to the legacy raw-Mat route (18 of 22 bank runs are mono);</item>
    /// <item>a bayered frame reaches detection as an <c>IDebayeredImage</c> with <c>SaveLumChannel == false</c>,
    ///   so <c>Detect</c> — not the loader — decides whether to CFA-filter and debayer;</item>
    /// <item><c>HotpixelThreshold</c> therefore still changes detection output, i.e. it is still a real searched
    ///   optimizer axis rather than a silent no-op;</item>
    /// <item>the debayer is gated on the profile exactly as <c>ImageControlVM.PrepareImage</c> gates it, and the
    ///   CFA pattern resolves with live's precedence or fails loudly.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class HeadlessDetectionParityTests {

        private const int Width = 512;
        private const int Height = 512;
        private const int BitDepth = 16;

        // ── Fixtures ──────────────────────────────────────────────────────────────────────────────────

        private static IProfileService BuildProfileService(bool debayerImage = true, BayerPatternEnum pattern = BayerPatternEnum.RGGB) {
            var profileService = Substitute.For<IProfileService>();
            profileService.ActiveProfile.ImageSettings.DebayerImage.Returns(debayerImage);
            profileService.ActiveProfile.CameraSettings.BayerPattern.Returns(pattern);
            return profileService;
        }

        private static IImageData BuildImageData(ushort[] pixels, bool isBayered, IProfileService profileService, SensorType metadataSensorType = SensorType.Monochrome) {
            var starDetection = Substitute.For<IStarDetection>();
            starDetection.CreateAnalysis().Returns(_ => new StarDetectionAnalysis());
            var metaData = new ImageMetaData();
            metaData.Camera.SensorType = metadataSensorType;
            return new BaseImageData(pixels, Width, Height, BitDepth, isBayered, metaData,
                profileService, starDetection, Substitute.For<IStarAnnotator>());
        }

        /// <summary>A deterministic star field as raw 16-bit samples (the same field the equivalence oracle uses).</summary>
        private static ushort[] BuildStarFieldPixels() {
            using var field = StarDetectorEquivalence.BuildSmallField();
            var pixels = new ushort[Width * Height];
            unsafe {
                var src = (float*)field.DataPointer;
                for (var i = 0; i < pixels.Length; ++i) {
                    pixels[i] = (ushort)Math.Clamp(Math.Round(src[i] * 65536.0), 0, ushort.MaxValue);
                }
            }
            return pixels;
        }

        /// <summary>
        /// The same field sampled through an RGGB colour filter array: each pixel scaled by its filter's
        /// throughput, so the raw frame is a checkerboard the debayer has to undo. This is the property that
        /// makes mosaic-vs-luminance detection genuinely different rather than a rounding difference.
        /// </summary>
        private static ushort[] BuildBayerMosaicPixels(ushort[] luminance) {
            var mosaic = new ushort[luminance.Length];
            for (var y = 0; y < Height; ++y) {
                for (var x = 0; x < Width; ++x) {
                    // RGGB: R at (even, even), G at (odd, even)/(even, odd), B at (odd, odd).
                    var isEvenRow = (y % 2) == 0;
                    var isEvenCol = (x % 2) == 0;
                    var gain = isEvenRow
                        ? (isEvenCol ? 0.55 : 1.0)   // R, G
                        : (isEvenCol ? 1.0 : 0.40);  // G, B
                    var i = y * Width + x;
                    mosaic[i] = (ushort)Math.Clamp(Math.Round(luminance[i] * gain), 0, ushort.MaxValue);
                }
            }
            return mosaic;
        }

        /// <summary>
        /// Plants isolated single-pixel spikes — what the CFA hotpixel filter exists to remove. The level is chosen
        /// to sit clearly ABOVE the default 0.001 threshold (65 ADU over the local same-colour median) and clearly
        /// BELOW <see cref="PermissiveHotpixelThreshold"/>, so the two thresholds bracket it and the axis test is
        /// exercising the threshold rather than the noise.
        /// </summary>
        private const ushort PlantedHotPixelLevel = 20000;

        private const double PermissiveHotpixelThreshold = 0.5;   // 32768 ADU: above every planted spike's excess

        private static void PlantHotPixels(ushort[] pixels, int count = 40) {
            var rng = new Random(4242);
            for (var i = 0; i < count; ++i) {
                var x = 8 + rng.Next(Width - 16);
                var y = 8 + rng.Next(Height - 16);
                pixels[y * Width + x] = PlantedHotPixelLevel;
            }
        }

        private static StarDetectorParams Params() => StarDetectorEquivalence.StandardParams();

        private static Task<HocusFocusStarDetectorResult> Detect(IRenderedImage image, StarDetectorParams p) =>
            new StarDetector(new AlglibAPI()).Detect(image, p, null, CancellationToken.None);

        // ── Mono byte-identity ────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Mono_IsByteIdenticalToTheLegacyRawMatRoute() {
            var profileService = BuildProfileService();
            var imageData = BuildImageData(BuildStarFieldPixels(), isBayered: false, profileService);

            var rendered = RenderedImageLoading.ForDetection(imageData, profileService);
            Assert.That(rendered, Is.Not.InstanceOf<IDebayeredImage>(),
                "a non-bayered frame must not be debayered — that is what makes mono byte-identical by construction");

            var p = Params();
            var throughRendered = StarDetectorEquivalence.Signature(await Detect(rendered, p));
            using var legacyMat = CvImageUtility.ToOpenCVMat(imageData);
            var throughMat = StarDetectorEquivalence.Signature(await StarDetectorEquivalence.RunDetect(legacyMat, p));

            Assert.That(throughRendered, Is.EqualTo(throughMat),
                "mono detection through the rendered-image path must be byte-identical to the legacy Mat route: " +
                "18 of the 22 bank runs are mono, so a difference here is a bank-wide regression");
        }

        [Test]
        public void Mono_RenderedImageCarriesTheSamePixelsAsTheRawFrame() {
            var profileService = BuildProfileService();
            var imageData = BuildImageData(BuildStarFieldPixels(), isBayered: false, profileService);

            using var fromRendered = CvImageUtility.ToOpenCVMat(RenderedImageLoading.ForDetection(imageData, profileService));
            using var fromRaw = CvImageUtility.ToOpenCVMat(imageData);
            using var diff = new Mat();
            Cv2.Absdiff(fromRendered, fromRaw, diff);

            Assert.That(Cv2.CountNonZero(diff.Reshape(1)), Is.Zero, "the rendered mono frame must be the raw frame, pixel for pixel");
        }

        // ── Bayered: the loader hands Detect the decision, and the decision matters ────────────────────

        [Test]
        public void Bayered_ProducesADebayeredImageWithSaveLumChannelFalse() {
            var profileService = BuildProfileService();
            var imageData = BuildImageData(BuildBayerMosaicPixels(BuildStarFieldPixels()), isBayered: true, profileService);

            var rendered = RenderedImageLoading.ForDetection(imageData, profileService);

            Assert.Multiple(() => {
                Assert.That(rendered, Is.InstanceOf<IDebayeredImage>(), "a bayered frame must reach Detect as an IDebayeredImage");
                var debayered = (IDebayeredImage)rendered;
                Assert.That(debayered.SaveLumChannel, Is.False,
                    "saveLumChannel MUST stay false: live computes it as DebayeredHFR && detectStars and every HocusFocus " +
                    "caller passes detectStars:false, so true here would flip ToOpenCVMat's guard and make the " +
                    "hotpixel-filtering-OFF branch read luminance where live reads the mosaic");
                Assert.That(debayered.BayerPattern, Is.EqualTo(SensorType.RGGB));
            });
        }

        [Test]
        public void Bayered_WithHotpixelFilteringOff_DetectionSourceIsStillTheMosaic() {
            // The other half of the saveLumChannel:false contract. Live has exactly two outcomes for a bayered
            // frame: CFA filter + debayer (both hotpixel flags on), or the raw mosaic. There is no live state that
            // debayers without filtering, which is precisely why the loader must not decide.
            var profileService = BuildProfileService();
            var mosaic = BuildBayerMosaicPixels(BuildStarFieldPixels());
            var imageData = BuildImageData(mosaic, isBayered: true, profileService);

            using var detectionSource = CvImageUtility.ToOpenCVMat(RenderedImageLoading.ForDetection(imageData, profileService));
            using var rawMosaic = CvImageUtility.ToOpenCVMat(imageData);
            using var diff = new Mat();
            Cv2.Absdiff(detectionSource, rawMosaic, diff);

            Assert.That(Cv2.CountNonZero(diff.Reshape(1)), Is.Zero,
                "with hotpixel filtering off, ToOpenCVMat must still read the mosaic — the debayer belongs to Detect");
        }

        [Test]
        public async Task Bayered_DetectingOnTheMosaicDiffersFromTheLiveRepresentation() {
            var profileService = BuildProfileService();
            var mosaic = BuildBayerMosaicPixels(BuildStarFieldPixels());
            PlantHotPixels(mosaic);
            var imageData = BuildImageData(mosaic, isBayered: true, profileService);

            var p = Params();
            var live = await Detect(RenderedImageLoading.ForDetection(imageData, profileService), p);
            using var mosaicMat = CvImageUtility.ToOpenCVMat(imageData);
            var onMosaic = await StarDetectorEquivalence.RunDetect(mosaicMat, p);

            Assert.Multiple(() => {
                Assert.That(live.Metrics.HotpixelCount, Is.GreaterThan(0),
                    "the CFA hotpixel filter must have run inside Detect at the caller's params");
                Assert.That(StarDetectorEquivalence.Signature(live), Is.Not.EqualTo(StarDetectorEquivalence.Signature(onMosaic)),
                    "detecting a bayered frame on its raw mosaic must NOT agree with detecting it the way the app does — " +
                    "if these ever match, the fixture stopped exercising the difference the whole change is about");
            });
        }

        // ── The hot-pixel axes still bite ─────────────────────────────────────────────────────────────

        [Test]
        public async Task Bayered_HotpixelThresholdStillChangesDetection() {
            // The reason the CFA filter lives inside Detect rather than in the loader: HotpixelThreshold and
            // HotpixelThresholdingEnabled are SEARCHED optimizer axes. Filtering once at load time froze them into
            // silent no-ops — the probe watched the optimizer keep searching 0.0005 -> 0.0015 against an image it
            // could no longer affect.
            var profileService = BuildProfileService();
            var mosaic = BuildBayerMosaicPixels(BuildStarFieldPixels());
            PlantHotPixels(mosaic);
            var imageData = BuildImageData(mosaic, isBayered: true, profileService);
            var rendered = RenderedImageLoading.ForDetection(imageData, profileService);

            var aggressive = Params();
            aggressive.HotpixelFiltering = true;
            aggressive.HotpixelThresholdingEnabled = true;
            aggressive.HotpixelThreshold = 0.001;   // 65 ADU at 16 bits: catches the planted spikes

            var permissive = Params();
            permissive.HotpixelFiltering = true;
            permissive.HotpixelThresholdingEnabled = true;
            permissive.HotpixelThreshold = PermissiveHotpixelThreshold;

            var withAggressive = await Detect(rendered, aggressive);
            var withPermissive = await Detect(rendered, permissive);

            Assert.Multiple(() => {
                Assert.That(withAggressive.Metrics.HotpixelCount, Is.GreaterThan(withPermissive.Metrics.HotpixelCount),
                    "a lower HotpixelThreshold must filter more CFA hot pixels");
                Assert.That(withPermissive.Metrics.HotpixelCount, Is.Zero,
                    $"{PermissiveHotpixelThreshold} (32768 ADU) must be above every planted spike's excess over its neighbours");
                Assert.That(StarDetectorEquivalence.Signature(withAggressive), Is.Not.EqualTo(StarDetectorEquivalence.Signature(withPermissive)),
                    "HotpixelThreshold must still change detection OUTPUT, not just a counter — otherwise the optimizer " +
                    "is searching an axis that no longer affects the image");
            });
        }

        [Test]
        public async Task Bayered_HotpixelFilteringOff_TakesTheMosaicBranch() {
            var profileService = BuildProfileService();
            var mosaic = BuildBayerMosaicPixels(BuildStarFieldPixels());
            PlantHotPixels(mosaic);
            var imageData = BuildImageData(mosaic, isBayered: true, profileService);
            var rendered = RenderedImageLoading.ForDetection(imageData, profileService);

            var filteringOff = Params();
            filteringOff.HotpixelThresholdingEnabled = false;

            var result = await Detect(rendered, filteringOff);
            using var mosaicMat = CvImageUtility.ToOpenCVMat(imageData);
            var onMosaic = await StarDetectorEquivalence.RunDetect(mosaicMat, filteringOff);

            Assert.That(StarDetectorEquivalence.Signature(result), Is.EqualTo(StarDetectorEquivalence.Signature(onMosaic)),
                "with either hotpixel flag off, live detects on the raw mosaic — the representation is params-dependent, " +
                "which is exactly why no loader may decide it");
        }

        // ── The debayer decision mirrors ImageControlVM.PrepareImage ──────────────────────────────────

        [Test]
        public void Bayered_ProfileDebayerImageOff_LeavesTheMosaicAlone() {
            var profileService = BuildProfileService(debayerImage: false);
            var imageData = BuildImageData(BuildBayerMosaicPixels(BuildStarFieldPixels()), isBayered: true, profileService);

            Assert.That(RenderedImageLoading.ForDetection(imageData, profileService), Is.Not.InstanceOf<IDebayeredImage>(),
                "live gates the whole debayer on ImageSettings.DebayerImage; with it off, detection runs on the mosaic");
        }

        [Test]
        public void BayerPattern_ProfileWinsOverFrameMetadata() {
            var profileService = BuildProfileService(pattern: BayerPatternEnum.GBRG);
            var imageData = BuildImageData(BuildBayerMosaicPixels(BuildStarFieldPixels()), isBayered: true, profileService, metadataSensorType: SensorType.RGGB);

            var debayered = (IDebayeredImage)RenderedImageLoading.ForDetection(imageData, profileService);

            Assert.That(debayered.BayerPattern, Is.EqualTo(SensorType.GBRG),
                "ImageControlVM.PrepareImage lets an explicit profile pattern override the frame's metadata");
        }

        [Test]
        public void BayerPattern_FallsBackToFrameMetadata_ThenToTheCamera() {
            var profileService = BuildProfileService(pattern: BayerPatternEnum.Auto);
            var fromMetadata = BuildImageData(BuildBayerMosaicPixels(BuildStarFieldPixels()), isBayered: true, profileService, metadataSensorType: SensorType.BGGR);
            var noMetadata = BuildImageData(BuildBayerMosaicPixels(BuildStarFieldPixels()), isBayered: true, profileService, metadataSensorType: SensorType.Monochrome);

            Assert.Multiple(() => {
                Assert.That(RenderedImageLoading.ResolveBayerPattern(fromMetadata, profileService), Is.EqualTo(SensorType.BGGR),
                    "with the profile on Auto, the frame's own BAYERPAT/SensorType is next");
                Assert.That(RenderedImageLoading.ResolveBayerPattern(fromMetadata, profileService, cameraSensorType: SensorType.GRBG), Is.EqualTo(SensorType.BGGR),
                    "the frame's concrete pattern still outranks the camera's");
                Assert.That(RenderedImageLoading.ResolveBayerPattern(noMetadata, profileService, cameraSensorType: SensorType.GRBG), Is.EqualTo(SensorType.GRBG),
                    "Monochrome/Color are placeholder categories, not patterns — the connected camera is the last resort");
            });
        }

        [Test]
        public void BayerPattern_Unresolvable_ThrowsRatherThanGuessingRggb() {
            var profileService = BuildProfileService(pattern: BayerPatternEnum.Auto);
            var imageData = BuildImageData(BuildBayerMosaicPixels(BuildStarFieldPixels()), isBayered: true, profileService, metadataSensorType: SensorType.Monochrome);

            var ex = Assert.Throws<InvalidOperationException>(() => RenderedImageLoading.ForDetection(imageData, profileService));
            Assert.That(ex.Message, Does.Contain("Bayer pattern"),
                "debayering at a guessed phase would quietly bias every number downstream; failing loudly is the contract");
        }
    }
}
