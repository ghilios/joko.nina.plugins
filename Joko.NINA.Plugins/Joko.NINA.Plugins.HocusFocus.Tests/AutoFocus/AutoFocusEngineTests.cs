using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel.AutoFocus;
using NSubstitute;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class AutoFocusEngineTests {

        [SetUp]
        public void ResetStaticGuardBefore() {
            // AutoFocusInProgress is a process-wide static; reset it so each test starts from a known state
            // regardless of execution order (F17).
            AutoFocusEngine.ResetAutoFocusInProgressForTests();
        }

        [TearDown]
        public void ResetStaticGuardAfter() {
            // Never leak the in-progress static to a later test if this one threw between claim and release.
            AutoFocusEngine.ResetAutoFocusInProgressForTests();
        }

        private static AutoFocusEngine Build(
            IProfileService profileService = null,
            IAutoFocusOptions autoFocusOptions = null,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector = null,
            IFocuserMediator focuserMediator = null,
            IFilterWheelMediator filterWheelMediator = null) {
            return new AutoFocusEngine(
                profileService: profileService ?? Substitute.For<IProfileService>(),
                cameraMediator: Substitute.For<ICameraMediator>(),
                filterWheelMediator: filterWheelMediator ?? Substitute.For<IFilterWheelMediator>(),
                focuserMediator: focuserMediator ?? Substitute.For<IFocuserMediator>(),
                guiderMediator: Substitute.For<IGuiderMediator>(),
                imagingMediator: Substitute.For<IImagingMediator>(),
                imageDataFactory: Substitute.For<IImageDataFactory>(),
                starDetectionSelector: starDetectionSelector ?? Substitute.For<IPluggableBehaviorSelector<IStarDetection>>(),
                starAnnotatorSelector: Substitute.For<IPluggableBehaviorSelector<IStarAnnotator>>(),
                autoFocusOptions: autoFocusOptions ?? Substitute.For<IAutoFocusOptions>(),
                starAnnotatorOptions: Substitute.For<IStarAnnotatorOptions>(),
                alglibAPI: new AlglibAPI());
        }

        // The live-display annotation gate. Every input must be true for the overlay to be drawn; each false case below
        // pins one reason the overlay is suppressed.
        [Test]
        public void ShouldAnnotateAutoFocusDisplay_AllConditionsMet_ReturnsTrue() {
            Assert.That(
                AutoFocusEngine.ShouldAnnotateAutoFocusDisplay(
                    annotateDuringAutoFocus: true, showAnnotations: true, isFullFrameRegion: true, isLiveRun: true, frameIsCurrentlyDisplayed: true),
                Is.True);
        }

        [TestCase(false, true, true, true, true, TestName = "ShouldAnnotateAutoFocusDisplay_OptionOff_ReturnsFalse")]
        [TestCase(true, false, true, true, true, TestName = "ShouldAnnotateAutoFocusDisplay_MasterSwitchOff_ReturnsFalse")]
        [TestCase(true, true, false, true, true, TestName = "ShouldAnnotateAutoFocusDisplay_MultiRegionRun_ReturnsFalse")]
        [TestCase(true, true, true, false, true, TestName = "ShouldAnnotateAutoFocusDisplay_ReplayRun_ReturnsFalse")]
        [TestCase(true, true, true, true, false, TestName = "ShouldAnnotateAutoFocusDisplay_StaleFrame_ReturnsFalse")]
        public void ShouldAnnotateAutoFocusDisplay_AnyConditionUnmet_ReturnsFalse(
            bool annotateDuringAutoFocus, bool showAnnotations, bool isFullFrameRegion, bool isLiveRun, bool frameIsCurrentlyDisplayed) {
            Assert.That(
                AutoFocusEngine.ShouldAnnotateAutoFocusDisplay(annotateDuringAutoFocus, showAnnotations, isFullFrameRegion, isLiveRun, frameIsCurrentlyDisplayed),
                Is.False);
        }

        [Test]
        public void GetOptions_PullsValuesFromProfileAndAutoFocusOptions() {
            var profileService = Substitute.For<IProfileService>();
            var autoFocusOptions = Substitute.For<IAutoFocusOptions>();
            profileService.ActiveProfile.FocuserSettings.AutoFocusUseBrightestStars.Returns(8);
            profileService.ActiveProfile.FocuserSettings.AutoFocusTotalNumberOfAttempts.Returns(2);
            profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps.Returns(5);
            profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize.Returns(25);
            profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint.Returns(3);
            profileService.ActiveProfile.FocuserSettings.AutoFocusMethod.Returns(AFMethodEnum.STARHFR);
            profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting.Returns(AFCurveFittingEnum.HYPERBOLIC);
            profileService.ActiveProfile.ImageSettings.DebayerImage.Returns(true);

            autoFocusOptions.MaxConcurrent.Returns(4);
            autoFocusOptions.AutoFocusTimeoutSeconds.Returns(120);
            autoFocusOptions.Save.Returns(true);
            autoFocusOptions.SavePath.Returns(@"C:\tmp");
            autoFocusOptions.MaxOutlierRejections.Returns(3);
            autoFocusOptions.OutlierRejectionConfidence.Returns(0.95);
            autoFocusOptions.WeightedHyperbolicFitEnabled.Returns(true);
            autoFocusOptions.HFRImprovementThreshold.Returns(0.1);
            autoFocusOptions.ValidateHfrImprovement.Returns(true);
            autoFocusOptions.FocuserOffset.Returns(10);

            var engine = Build(profileService, autoFocusOptions);
            var options = engine.GetOptions();

            Assert.Multiple(() => {
                Assert.That(options.NumberOfAFStars, Is.EqualTo(8));
                Assert.That(options.TotalNumberOfAttempts, Is.EqualTo(2));
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(5));
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(25));
                Assert.That(options.FramesPerPoint, Is.EqualTo(3));
                Assert.That(options.AutoFocusMethod, Is.EqualTo(AFMethodEnum.STARHFR));
                Assert.That(options.AutoFocusCurveFitting, Is.EqualTo(AFCurveFittingEnum.HYPERBOLIC));
                Assert.That(options.DebayerImage, Is.True);
                Assert.That(options.MaxConcurrent, Is.EqualTo(4));
                Assert.That(options.AutoFocusTimeout, Is.EqualTo(TimeSpan.FromSeconds(120)));
                Assert.That(options.Save, Is.True);
                Assert.That(options.SavePath, Is.EqualTo(@"C:\tmp"));
                Assert.That(options.MaxOutlierRejections, Is.EqualTo(3));
                Assert.That(options.OutlierRejectionConfidence, Is.EqualTo(0.95));
                Assert.That(options.WeightedHyperbolicFitEnabled, Is.True);
                Assert.That(options.HFRImprovementThreshold, Is.EqualTo(0.1));
                Assert.That(options.ValidateHfrImprovement, Is.True);
                Assert.That(options.FocuserOffset, Is.EqualTo(10));
            });
        }

        // ---- Fixed-sweep capture (non-convergent live optimization) --------------------------------------
        // ComputeSweepPositions is the load-bearing geometry for CaptureFixedSweepAsync: it maps the AF
        // step-size / offset-steps settings to the exact focuser positions the sweep visits. It must center on
        // the rough-focus position, be symmetric, descend (single-direction approach for backlash), and always
        // yield >= 3 distinct positions so the saved run clears the loader's 3-image / 3-position minimum.
        [Test]
        public void ComputeSweepPositions_CentersOnInitial_SymmetricDescending() {
            var positions = AutoFocusEngine.ComputeSweepPositions(initial: 1000, offsetSteps: 3, stepSize: 10);
            Assert.That(positions, Is.EqualTo(new[] { 1030, 1020, 1010, 1000, 990, 980, 970 }));
        }

        [TestCase(1, 3)]
        [TestCase(2, 5)]
        [TestCase(5, 11)]
        public void ComputeSweepPositions_ReturnsTwoNPlusOnePoints(int offsetSteps, int expectedCount) {
            var positions = AutoFocusEngine.ComputeSweepPositions(initial: 5000, offsetSteps: offsetSteps, stepSize: 25);
            Assert.Multiple(() => {
                Assert.That(positions, Has.Count.EqualTo(expectedCount));
                Assert.That(positions[0], Is.EqualTo(5000 + offsetSteps * 25), "first is the high extreme");
                Assert.That(positions[positions.Count - 1], Is.EqualTo(5000 - offsetSteps * 25), "last is the low extreme");
                Assert.That(positions, Does.Contain(5000), "the rough-focus center is captured");
                Assert.That(positions, Is.Ordered.Descending, "descend so every point is approached from the same direction");
            });
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ComputeSweepPositions_OffsetStepsBelowOne_Throws(int offsetSteps) {
            Assert.Throws<ArgumentOutOfRangeException>(() => AutoFocusEngine.ComputeSweepPositions(1000, offsetSteps, 10));
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void ComputeSweepPositions_NonPositiveStepSize_Throws(int stepSize) {
            Assert.Throws<ArgumentOutOfRangeException>(() => AutoFocusEngine.ComputeSweepPositions(1000, 3, stepSize));
        }

        // A sweep that can't save is useless: guard before touching the focuser so we don't move the focuser and
        // discard every frame. This is the engine-side belt to the wizard's UI suspenders.
        [Test]
        public async Task CaptureFixedSweepAsync_MissingSavePath_FailsWithoutMovingFocuser() {
            var focuserMediator = Substitute.For<IFocuserMediator>();
            var engine = Build(focuserMediator: focuserMediator);
            var options = new AutoFocusEngineOptions {
                Save = true,
                SavePath = "",
                AutoFocusInitialOffsetSteps = 3,
                AutoFocusStepSize = 10,
                AutoFocusTimeout = TimeSpan.FromMinutes(1)
            };

            var result = await engine.CaptureFixedSweepAsync(options, null, CancellationToken.None, null);

            Assert.Multiple(() => {
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.SaveFolder, Is.Null);
            });
            _ = focuserMediator.DidNotReceive().MoveFocuser(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public void GetOptions_MaxConcurrentZero_IsClampedToIntMaxValue() {
            var autoFocusOptions = Substitute.For<IAutoFocusOptions>();
            autoFocusOptions.MaxConcurrent.Returns(0);
            var engine = Build(autoFocusOptions: autoFocusOptions);

            var options = engine.GetOptions();

            Assert.That(options.MaxConcurrent, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void GetOptions_SavedAttemptStepSize_OverridesProfileStepSize() {
            var profileService = Substitute.For<IProfileService>();
            profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize.Returns(15);
            var engine = Build(profileService);

            var savedAttempt = new SavedAutoFocusAttempt { StepSize = 33 };
            var options = engine.GetOptions(savedAttempt);

            Assert.That(options.AutoFocusStepSize, Is.EqualTo(33));
        }

        [Test]
        public void GetOptions_SavedAttemptZeroStepSize_FallsBackToProfile() {
            var profileService = Substitute.For<IProfileService>();
            profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize.Returns(15);
            var engine = Build(profileService);

            var savedAttempt = new SavedAutoFocusAttempt { StepSize = 0 };
            var options = engine.GetOptions(savedAttempt);

            Assert.That(options.AutoFocusStepSize, Is.EqualTo(15));
        }

        [Test]
        public void RunWithRegions_NullRegions_Throws() {
            var engine = Build();
            Assert.That(async () => await engine.RunWithRegions(new AutoFocusEngineOptions(), null, null, default, null),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void RunWithRegions_EmptyRegions_Throws() {
            var engine = Build();
            Assert.That(async () => await engine.RunWithRegions(new AutoFocusEngineOptions(), null, new List<StarDetectionRegion>(), default, null),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void RerunWithRegions_NullRegions_Throws() {
            var engine = Build();
            Assert.That(async () => await engine.RerunWithRegions(new AutoFocusEngineOptions(), new SavedAutoFocusAttempt(), null, null, default, null),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void LoadSavedAutoFocusAttempt_NonAttemptFolderWithNoSubfolders_Throws() {
            using var tmp = new TempDir();
            var engine = Build();
            Assert.That(() => engine.LoadSavedAutoFocusAttempt(tmp.Path),
                Throws.InstanceOf<Exception>());
        }

        [Test]
        public void LoadSavedAutoFocusAttempt_AttemptFolderWithImages_ReturnsParsedAttempt() {
            using var tmp = new TempDir();
            var attemptDir = Directory.CreateDirectory(Path.Combine(tmp.Path, "attempt07"));
            // Filename pattern: <imageIndex>_Frame<frame>_BitDepth<bd>_Bayered<0|1>_Focuser<pos>(.fits)
            File.WriteAllBytes(Path.Combine(attemptDir.FullName, "0_Frame1_BitDepth16_Bayered0_Focuser5000.fits"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(attemptDir.FullName, "1_Frame1_BitDepth16_Bayered0_Focuser5025.fits"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(attemptDir.FullName, "2_Frame1_BitDepth16_Bayered0_Focuser5050.fits"), Array.Empty<byte>());
            // ignored file
            File.WriteAllText(Path.Combine(attemptDir.FullName, "notes.txt"), "irrelevant");

            var engine = Build();
            var attempt = engine.LoadSavedAutoFocusAttempt(attemptDir.FullName);

            Assert.Multiple(() => {
                Assert.That(attempt.Attempt, Is.EqualTo(7));
                Assert.That(attempt.SavedImages, Has.Count.EqualTo(3));
                Assert.That(attempt.StepSize, Is.EqualTo(25));
                Assert.That(attempt.FolderPath, Is.EqualTo(attemptDir.FullName));
            });
        }

        [Test]
        public void LoadSavedAutoFocusAttempt_TooFewImages_Throws() {
            using var tmp = new TempDir();
            var attemptDir = Directory.CreateDirectory(Path.Combine(tmp.Path, "attempt03"));
            // Only 2 images, minimum is 3 for the regular path.
            File.WriteAllBytes(Path.Combine(attemptDir.FullName, "0_Frame1_BitDepth16_Bayered0_Focuser5000.fits"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(attemptDir.FullName, "1_Frame1_BitDepth16_Bayered0_Focuser5050.fits"), Array.Empty<byte>());

            var engine = Build();
            Assert.That(() => engine.LoadSavedAutoFocusAttempt(attemptDir.FullName),
                Throws.InstanceOf<Exception>());
        }

        [Test]
        public void LoadSavedFinalAttempt_AcceptsAnyFolderName() {
            using var tmp = new TempDir();
            File.WriteAllBytes(Path.Combine(tmp.Path, "0_Frame1_BitDepth16_Bayered0_Focuser5000.fits"), Array.Empty<byte>());
            var engine = Build();

            var attempt = engine.LoadSavedFinalAttempt(tmp.Path);

            Assert.Multiple(() => {
                Assert.That(attempt.Attempt, Is.EqualTo(-1));
                Assert.That(attempt.SavedImages, Has.Count.EqualTo(1));
                Assert.That(attempt.StepSize, Is.EqualTo(0));
            });
        }

        [Test]
        public void TryCompleteFocuserPoint_DuplicatePosition_DoesNotThrowAndKeepsFirstCompletion() {
            var map = new Dictionary<int, MeasureAndError>();
            var firstFrames = new List<MeasureAndError> {
                new MeasureAndError { Measure = 2.0, Stdev = 0.1 },
                new MeasureAndError { Measure = 2.2, Stdev = 0.1 },
            };
            var secondFrames = new List<MeasureAndError> {
                new MeasureAndError { Measure = 9.0, Stdev = 0.5 },
            };

            var firstResult = AutoFocusEngine.TryCompleteFocuserPoint(map, 21209, firstFrames, out var firstPooled);

            // Reprocessing a saved run can map two measurement points to the same focuser position. The second
            // completion must not throw "An item with the same key has already been added. Key: 21209".
            bool secondResult = false;
            MeasureAndError duplicatePooled = default;
            Assert.DoesNotThrow(() => secondResult = AutoFocusEngine.TryCompleteFocuserPoint(map, 21209, secondFrames, out duplicatePooled));

            Assert.Multiple(() => {
                Assert.That(firstResult, Is.True);
                Assert.That(secondResult, Is.False);
                Assert.That(map, Has.Count.EqualTo(1));
                Assert.That(map[21209].Measure, Is.EqualTo(2.1).Within(1e-9));
                Assert.That(firstPooled.Measure, Is.EqualTo(2.1).Within(1e-9));
                Assert.That(duplicatePooled.Measure, Is.EqualTo(2.1).Within(1e-9));
            });
        }

        [Test]
        public void TryCompleteFocuserPoint_MultiFrame_OutputsPooledMeasurementForEvent() {
            // The MeasurementPointCompleted event must carry this pooled value — not the last
            // sub-frame — so charts, the NINA broadcast point, and saved reports agree with the fit
            // inputs when FramesPerPoint > 1.
            var map = new Dictionary<int, MeasureAndError>();
            var frames = new List<MeasureAndError> {
                new MeasureAndError { Measure = 2.0, Stdev = 0.4 },
                new MeasureAndError { Measure = 3.0, Stdev = 0.4 },
            };

            var completed = AutoFocusEngine.TryCompleteFocuserPoint(map, 100, frames, out var pooled);

            Assert.Multiple(() => {
                Assert.That(completed, Is.True);
                Assert.That(pooled.Measure, Is.EqualTo(2.5).Within(1e-12));
                Assert.That(pooled.Stdev, Is.EqualTo(0.4 / Math.Sqrt(2)).Within(1e-12));
                Assert.That(map[100].Measure, Is.EqualTo(pooled.Measure));
            });
        }

        [Test]
        public void SafeDisplayError_MapsNaNAndNegativeToZero_KeepsMeasuredValues() {
            Assert.Multiple(() => {
                Assert.That(AutoFocusEngine.SafeDisplayError(double.NaN), Is.EqualTo(0.0));
                Assert.That(AutoFocusEngine.SafeDisplayError(double.PositiveInfinity), Is.EqualTo(0.0));
                Assert.That(AutoFocusEngine.SafeDisplayError(-0.5), Is.EqualTo(0.0));
                Assert.That(AutoFocusEngine.SafeDisplayError(0.0), Is.EqualTo(0.0));
                // Below the old 0.001 fabrication threshold: must now pass through unmodified.
                Assert.That(AutoFocusEngine.SafeDisplayError(0.0004), Is.EqualTo(0.0004));
                Assert.That(AutoFocusEngine.SafeDisplayError(0.25), Is.EqualTo(0.25));
            });
        }

        [Test]
        public async Task Run_WithNullProgress_ClearsStaticInProgressGuard_EvenWhenAutoFocusFails() {
            // Regression: the Star Detection Optimizer's live attempt calls Run with a null progress. RunImpl's
            // finally used to call progress.Report(...) BEFORE clearing the static AutoFocusInProgress guard, so a
            // null progress threw an NRE that skipped the reset. The static flag stuck true and bricked every
            // subsequent AutoFocus ("Another AutoFocus is already in progress") app-wide until NINA was restarted.
            AutoFocusEngine.ResetAutoFocusInProgressForTests();
            try {
                var engine = Build();
                Assert.That(engine.AutoFocusInProgress, Is.False, "static guard must start clear (reset in SetUp)");

                try {
                    await engine.Run(new AutoFocusEngineOptions { AutoFocusTimeout = TimeSpan.FromMinutes(1) }, imagingFilter: null, token: default, progress: null);
                } catch {
                    // AutoFocus fails fast on the all-mocked equipment; we only care that the guard is released.
                }

                Assert.That(engine.AutoFocusInProgress, Is.False, "AutoFocusInProgress must be cleared even when the run fails with a null progress");
            } finally {
                AutoFocusEngine.ResetAutoFocusInProgressForTests();
            }
        }

        [Test]
        public void StaticGuard_IsResetBetweenTests_NotLeakedFromPriorRun() {
            // Simulate a prior test that left the process-wide guard set (e.g. threw between claim and release).
            // SetUp must have already cleared it; assert deterministically rather than going Inconclusive (F17).
            var engine = Build();
            Assert.That(engine.AutoFocusInProgress, Is.False, "SetUp must reset the static guard before each test");

            // F11 replaced the writable property with Interlocked claim/release helpers; claim to flip the guard true.
            Assert.That(AutoFocusEngine.TryClaimAutoFocusInProgress(), Is.True, "guard should be claimable after the SetUp reset");
            Assert.That(engine.AutoFocusInProgress, Is.True, "the claimed guard reads true through the public getter");
            // TearDown resets it so this claim cannot leak into a sibling test.
        }

        [Test]
        public async Task Run_WhenStartedSubscriberThrows_StillReleasesGuard() {
            // F11 leak-window regression: RunImpl claims the static AutoFocusInProgress guard at the gate, then calls
            // OnStarted() (which raises the public Started event synchronously into subscribers) BEFORE the inner
            // try/finally. If a Started subscriber throws, the buggy code left the guard claimed forever, bricking
            // every subsequent AutoFocus app-wide until NINA restarted. The release now lives in an outer finally
            // that covers OnStarted(), so the guard must be released even when a Started subscriber throws.
            AutoFocusEngine.ResetAutoFocusInProgressForTests();
            try {
                var engine = Build();
                Assert.That(engine.AutoFocusInProgress, Is.False, "static guard must start clear (reset in SetUp)");

                var subscriberThrew = false;
                engine.Started += (sender, args) => {
                    subscriberThrew = true;
                    throw new InvalidOperationException("Started subscriber failure");
                };

                try {
                    await engine.Run(new AutoFocusEngineOptions { AutoFocusTimeout = TimeSpan.FromMinutes(1) }, imagingFilter: null, token: default, progress: null);
                } catch {
                    // The throwing Started subscriber surfaces here; we only care that the guard is released.
                }

                Assert.Multiple(() => {
                    Assert.That(subscriberThrew, Is.True, "the throwing Started subscriber must have run (OnStarted reached)");
                    Assert.That(engine.AutoFocusInProgress, Is.False, "AutoFocusInProgress must be released even when a Started subscriber throws");
                    Assert.That(AutoFocusEngine.TryClaimAutoFocusInProgress(), Is.True, "the guard must be claimable again after the leaked-throw path");
                });
            } finally {
                AutoFocusEngine.ResetAutoFocusInProgressForTests();
            }
        }

        [Test]
        public void TryClaimAutoFocusInProgress_SecondClaimantIsRejectedUntilReleased() {
            // F11: the static AutoFocusInProgress guard must be claimed atomically so two RunImpl entrants
            // (e.g. a manual AF and the optimizer's live attempt) cannot both pass the gate and drive the focuser.
            AutoFocusEngine.ResetAutoFocusInProgressForTests();
            try {
                Assert.That(AutoFocusEngine.TryClaimAutoFocusInProgress(), Is.True, "first claim should succeed");
                Assert.That(AutoFocusEngine.TryClaimAutoFocusInProgress(), Is.False, "second claim must be rejected while held");

                AutoFocusEngine.ReleaseAutoFocusInProgress();
                Assert.That(AutoFocusEngine.TryClaimAutoFocusInProgress(), Is.True, "claim should succeed again after release");
            } finally {
                AutoFocusEngine.ResetAutoFocusInProgressForTests();
            }
        }

        // --- Auto-retry-from-calculated-point decision (Change 1) ---
        // The full sweep can't be driven through the all-mocked engine, so the retry predicate is unit-tested directly.

        [Test]
        public void ShouldRetryFromCalculatedPoint_HfrRegression_FirstTime_RetriesOnce() {
            // A "final HFR worse than original" failure with a usable calculated point should re-center and retry.
            Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                AutoFocusEngine.AutoFocusFailureMode.HfrRegression, calculatedPoint: 53929, currentSweepCenter: 54073, calculatedPointRetryUsed: false), Is.True);
        }

        [Test]
        public void ShouldRetryFromCalculatedPoint_OutOfBounds_FirstTime_RetriesOnce() {
            // A "focus point outside the swept range" failure (the user's saved run) should re-center and retry.
            Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                AutoFocusEngine.AutoFocusFailureMode.FinalPointOutOfBounds, calculatedPoint: 53214, currentSweepCenter: 54073, calculatedPointRetryUsed: false), Is.True);
        }

        [Test]
        public void ShouldRetryFromCalculatedPoint_SecondFailure_DoesNotRetryAgain() {
            // The calculated-point retry is single-shot: once used, a second a/c failure must NOT trigger it again.
            Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                AutoFocusEngine.AutoFocusFailureMode.HfrRegression, calculatedPoint: 53800, currentSweepCenter: 53929, calculatedPointRetryUsed: true), Is.False);
            Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                AutoFocusEngine.AutoFocusFailureMode.FinalPointOutOfBounds, calculatedPoint: 53800, currentSweepCenter: 53929, calculatedPointRetryUsed: true), Is.False);
        }

        [Test]
        public void ShouldRetryFromCalculatedPoint_NonRetryEligibleModes_DoNotRetry() {
            // Bad-data / bad-fit failures can't be fixed by re-centering, so they fall through to the normal budget.
            Assert.Multiple(() => {
                Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                    AutoFocusEngine.AutoFocusFailureMode.FitQuality, 53929, 54073, false), Is.False);
                Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                    AutoFocusEngine.AutoFocusFailureMode.InitialHfrFailed, 53929, 54073, false), Is.False);
                Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                    AutoFocusEngine.AutoFocusFailureMode.FinalHfrMissing, 53929, 54073, false), Is.False);
                Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                    AutoFocusEngine.AutoFocusFailureMode.None, 53929, 54073, false), Is.False);
            });
        }

        [Test]
        public void ShouldRetryFromCalculatedPoint_GuardsNoOpAndInvalidPoints() {
            // calc == current center (no-op re-sweep) and an invalid (<0) point must both be rejected.
            Assert.Multiple(() => {
                Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                    AutoFocusEngine.AutoFocusFailureMode.HfrRegression, calculatedPoint: 54073, currentSweepCenter: 54073, calculatedPointRetryUsed: false), Is.False);
                Assert.That(AutoFocusEngine.ShouldRetryFromCalculatedPoint(
                    AutoFocusEngine.AutoFocusFailureMode.FinalPointOutOfBounds, calculatedPoint: -1, currentSweepCenter: 54073, calculatedPointRetryUsed: false), Is.False);
            });
        }

        // --- Behavior A: symmetric-window helpers (final-fit exclusion) ---
        // The window/refit loop (ApplyFinalSymmetricWindow) needs a full region-state harness, but its geometry is
        // factored into these pure static helpers, unit-tested directly the same way ComputeSweepPositions is.

        private static ScatterErrorPoint SE(double x, double y) => new ScatterErrorPoint(x, y, 0, 0.1);

        [Test]
        public void IsWithinFocusWindow_StrictBoundaries_SymmetricMembership() {
            // Half-width = (offsetSteps + 0.5) * stepSize = 5.5 * 5 = 27.5, so the window is the OPEN interval (9972.5, 10027.5).
            const double min = 10000;
            Assert.Multiple(() => {
                Assert.That(AutoFocusEngine.IsWithinFocusWindow(min, min, 5, 5), Is.True, "the center is inside");
                Assert.That(AutoFocusEngine.IsWithinFocusWindow(min - 27, min, 5, 5), Is.True, "just inside the low edge");
                Assert.That(AutoFocusEngine.IsWithinFocusWindow(min + 27, min, 5, 5), Is.True, "just inside the high edge");
                Assert.That(AutoFocusEngine.IsWithinFocusWindow(min - 27.5, min, 5, 5), Is.False, "exactly on the low edge is excluded (strict)");
                Assert.That(AutoFocusEngine.IsWithinFocusWindow(min + 27.5, min, 5, 5), Is.False, "exactly on the high edge is excluded (strict)");
                Assert.That(AutoFocusEngine.IsWithinFocusWindow(min - 28, min, 5, 5), Is.False, "outside the low edge");
                Assert.That(AutoFocusEngine.IsWithinFocusWindow(min + 28, min, 5, 5), Is.False, "outside the high edge");
            });
        }

        [Test]
        public void PartitionByFocusWindow_LopsidedSplit_ExcludesFarPoints_IncludingSpuriousLowHfr() {
            // Window (9972.5, 10027.5) around the FITTED VERTEX (10000). Points cluster far to the right, plus a
            // spurious LOW-HFR point far to the left that a lowest-raw-point center would have chased. Because the
            // center is the vertex (not the lowest point), the spurious point simply lands outside the window and is
            // excluded rather than defining the minimum.
            var points = new List<ScatterErrorPoint> {
                SE(9500, 1.2),    // spurious low-HFR far left -> excluded
                SE(9980, 3.0),
                SE(10000, 2.0),
                SE(10020, 3.1),
                SE(10060, 6.0),   // far right -> excluded
                SE(10090, 8.0),   // far right -> excluded
            };
            var (included, excluded) = AutoFocusEngine.PartitionByFocusWindow(points, minimumX: 10000, offsetSteps: 5, stepSize: 5);
            Assert.Multiple(() => {
                Assert.That(included.Select(p => p.X), Is.EquivalentTo(new[] { 9980.0, 10000.0, 10020.0 }));
                Assert.That(excluded.Select(p => p.X), Is.EquivalentTo(new[] { 9500.0, 10060.0, 10090.0 }));
            });
        }

        [Test]
        public void PartitionByFocusWindow_AllWithinWindow_ExcludesNothing() {
            // No point falls outside the window, so the partition is a no-op and the caller keeps the full fit.
            var points = new List<ScatterErrorPoint> {
                SE(9980, 3.0), SE(9990, 2.5), SE(10000, 2.0), SE(10010, 2.6), SE(10020, 3.2)
            };
            var (included, excluded) = AutoFocusEngine.PartitionByFocusWindow(points, 10000, 5, 5);
            Assert.Multiple(() => {
                Assert.That(included, Has.Count.EqualTo(5));
                Assert.That(excluded, Is.Empty);
            });
        }

        [Test]
        public void PartitionByFocusWindow_WouldKeepFewerThanThree_IsDetectableSoCallerHoldsTheFloor() {
            // The window helper only splits; the >=3-kept floor lives in ApplyFinalSymmetricWindow, which refuses to
            // window (leaving the full fit intact) whenever fewer than 3 valid points would remain. Here only two
            // points fall inside the window, which the caller detects via included.Count(Y>0) and declines to apply.
            var points = new List<ScatterErrorPoint> {
                SE(9995, 2.0), SE(10005, 2.1),                 // the only two inside (9972.5, 10027.5)
                SE(10050, 5.0), SE(10080, 7.0), SE(10110, 9.0) // three far right
            };
            var (included, excluded) = AutoFocusEngine.PartitionByFocusWindow(points, 10000, 5, 5);
            Assert.Multiple(() => {
                Assert.That(included.Count(p => p.Y > 0.0), Is.EqualTo(2), "fewer than 3 kept -> caller must NOT window");
                Assert.That(excluded, Has.Count.EqualTo(3));
            });
        }

        // --- Behavior B: directional reversal decision ---
        // The full walk needs the sweep harness, but the reverse-or-not decision is a pure helper, tested directly.

        [TestCase(8, 8, false, false, true, TestName = "ShouldReverseDirection_AtCapWithoutBracket_Reverses")]
        [TestCase(7, 8, false, false, false, TestName = "ShouldReverseDirection_BelowCap_DoesNotReverse")]
        [TestCase(9, 8, true, false, false, TestName = "ShouldReverseDirection_WithTwoSidedBracket_DoesNotReverse")]
        [TestCase(20, 8, false, true, false, TestName = "ShouldReverseDirection_AlreadyReversed_LatchesAgainstOscillation")]
        public void ShouldReverseDirection_TruthTable(int stepOutsThisDir, int effectiveCap, bool bracketFormed, bool hasReversed, bool expected) {
            Assert.That(AutoFocusEngine.ShouldReverseDirection(stepOutsThisDir, effectiveCap, bracketFormed, hasReversed), Is.EqualTo(expected));
        }

        [Test]
        public void ShouldReverseDirection_DisabledSemantics_NeverReversesBelowTheEffectiveFloorCap() {
            // When Behavior B is disabled (configured cap 0) the walk gates the helper away entirely, but even the
            // effective floor cap (Math.Max(0, offsetSteps + 1)) is a valid, non-tightening bound: a walk that never
            // reaches that many step-outs in a direction cannot reverse.
            var effectiveCap = Math.Max(0, 5 + 1);
            Assert.Multiple(() => {
                Assert.That(AutoFocusEngine.ShouldReverseDirection(0, effectiveCap, bracketFormed: false, hasReversed: false), Is.False);
                Assert.That(AutoFocusEngine.ShouldReverseDirection(effectiveCap - 1, effectiveCap, bracketFormed: false, hasReversed: false), Is.False);
                Assert.That(AutoFocusEngine.ShouldReverseDirection(effectiveCap, effectiveCap, bracketFormed: false, hasReversed: false), Is.True);
            });
        }

        // --- metadata.json is written (and flagged) on the failure path (Change 2) ---

        [Test]
        public void WriteMetadataFile_WritesFailureFlaggedMetadata_ThatRoundTrips() {
            using var tmp = new TempDir();
            var metadata = new AutoFocusReplayMetadata() {
                SchemaVersion = AutoFocusReplayMetadata.CurrentSchemaVersion,
                StarDetection = new StarDetectionSettingsSnapshot(),
                AutoFocus = new AutoFocusOptionsSnapshot(),
                Succeeded = false,
                FailureReason = "Calculated focus point outside the swept range"
            };

            AutoFocusEngine.WriteMetadataFile(tmp.Path, metadata);

            var loaded = AutoFocusReplayMetadata.TryLoad(tmp.Path, out var restored, out var error);
            Assert.Multiple(() => {
                Assert.That(File.Exists(Path.Combine(tmp.Path, "metadata.json")), Is.True);
                Assert.That(loaded, Is.True);
                Assert.That(error, Is.Null);
                Assert.That(restored.Succeeded, Is.False);
                Assert.That(restored.FailureReason, Is.EqualTo("Calculated focus point outside the swept range"));
            });
        }

        // --- HFR-improvement gate (region-0-only validation) ---
        // The whole-run HFR-improvement check now gates on region 0 only; EvaluateHfrImprovement is the pure
        // decision for a single region, unit-tested here since the full sweep can't be driven through the mocks.

        [Test]
        public void EvaluateHfrImprovement_FinalHfrNull_ReturnsFinalHfrMissing() {
            Assert.That(AutoFocusEngine.EvaluateHfrImprovement(new MeasureAndError { Measure = 2.5 }, null, 0.1),
                Is.EqualTo(AutoFocusEngine.AutoFocusFailureMode.FinalHfrMissing));
        }

        [Test]
        public void EvaluateHfrImprovement_FinalHfrZero_ReturnsFinalHfrMissing() {
            Assert.That(AutoFocusEngine.EvaluateHfrImprovement(new MeasureAndError { Measure = 2.5 }, new MeasureAndError { Measure = 0.0 }, 0.1),
                Is.EqualTo(AutoFocusEngine.AutoFocusFailureMode.FinalHfrMissing));
        }

        [Test]
        public void EvaluateHfrImprovement_InitialHfrZero_FinalOk_ReturnsInitialHfrFailed() {
            // The incident's shape: a zero initial HFR with a healthy final HFR. Only fatal now when it's region 0.
            Assert.That(AutoFocusEngine.EvaluateHfrImprovement(new MeasureAndError { Measure = 0.0 }, new MeasureAndError { Measure = 2.5 }, 0.1),
                Is.EqualTo(AutoFocusEngine.AutoFocusFailureMode.InitialHfrFailed));
        }

        [Test]
        public void EvaluateHfrImprovement_InitialHfrNull_FinalOk_ReturnsInitialHfrFailed() {
            Assert.That(AutoFocusEngine.EvaluateHfrImprovement(null, new MeasureAndError { Measure = 2.5 }, 0.1),
                Is.EqualTo(AutoFocusEngine.AutoFocusFailureMode.InitialHfrFailed));
        }

        [Test]
        public void EvaluateHfrImprovement_FinalWorseBeyondThreshold_ReturnsHfrRegression() {
            // initial 3.0, threshold 0.1 => reject when final > 3.3.
            Assert.That(AutoFocusEngine.EvaluateHfrImprovement(new MeasureAndError { Measure = 3.0 }, new MeasureAndError { Measure = 3.4 }, 0.1),
                Is.EqualTo(AutoFocusEngine.AutoFocusFailureMode.HfrRegression));
        }

        [Test]
        public void EvaluateHfrImprovement_FinalWithinThreshold_ReturnsNone() {
            // 3.3 == 3.0 * 1.1 exactly => accepted (strictly-greater rejection).
            Assert.That(AutoFocusEngine.EvaluateHfrImprovement(new MeasureAndError { Measure = 3.0 }, new MeasureAndError { Measure = 3.3 }, 0.1),
                Is.EqualTo(AutoFocusEngine.AutoFocusFailureMode.None));
        }

        [Test]
        public void EvaluateHfrImprovement_FinalImproves_ReturnsNone() {
            Assert.That(AutoFocusEngine.EvaluateHfrImprovement(new MeasureAndError { Measure = 3.0 }, new MeasureAndError { Measure = 2.5 }, 0.1),
                Is.EqualTo(AutoFocusEngine.AutoFocusFailureMode.None));
        }

        // --- HFR-improvement validation failure diagnostics (expanded logging) ---
        // The generic "Failed assessing HFR at the initial position" named neither the region nor the reason.
        // DescribeHfrValidationFailure builds the enriched log line; verify it classifies the three causes and
        // echoes the region index so a user can cross-reference the detector's "Region: N" lines.

        [Test]
        public void DescribeHfrValidationFailure_ZeroHfrFiniteStdev_SaysNoStars() {
            // Genuine zero-star detection: EvaluateExposure returns {Measure: 0, Stdev: 0 (finite)}. This is the
            // real incident — region 3 had <=1 usable star in the initial frame.
            var subs = new List<MeasureAndError> { new MeasureAndError { Measure = 0.0, Stdev = 0.0 } };
            var msg = AutoFocusEngine.DescribeHfrValidationFailure("initial position", 3, new MeasureAndError { Measure = 0.0, Stdev = 0.0 }, subs, framesPerPoint: 1);

            Assert.Multiple(() => {
                Assert.That(msg, Does.Contain("initial position"));
                Assert.That(msg, Does.Contain("Region 3"));
                Assert.That(msg, Does.Contain("found no usable stars"));
                Assert.That(msg, Does.Contain("Measured HFR=0.00"));
                Assert.That(msg, Does.Contain("sub-frames 1/1"));
                Assert.That(msg, Does.Contain("Region: 3"), "must point the reader at the detector's Region line");
                Assert.That(msg, Does.Not.Contain("NaN"));
            });
        }

        [Test]
        public void DescribeHfrValidationFailure_ZeroHfrNaNStdev_SaysAnalysisError() {
            // AnalyzeExposure's catch injects {Measure: 0, Stdev: NaN} when detection threw for a sub-frame.
            var subs = new List<MeasureAndError> { new MeasureAndError { Measure = 0.0, Stdev = double.NaN } };
            var msg = AutoFocusEngine.DescribeHfrValidationFailure("final focus point", 2, new MeasureAndError { Measure = 0.0, Stdev = double.NaN }, subs, framesPerPoint: 1);

            Assert.Multiple(() => {
                Assert.That(msg, Does.Contain("final focus point"));
                Assert.That(msg, Does.Contain("Region 2"));
                Assert.That(msg, Does.Contain("analysis errored"));
                Assert.That(msg, Does.Contain("σ=NaN"));
            });
        }

        [Test]
        public void DescribeHfrValidationFailure_NullHfr_SaysMeasurementIncomplete() {
            // Null averaged HFR means the sub-frame loop never reached FramesPerPoint.
            var subs = new List<MeasureAndError> { new MeasureAndError { Measure = 2.5, Stdev = 0.3 } };
            var msg = AutoFocusEngine.DescribeHfrValidationFailure("initial position", 4, null, subs, framesPerPoint: 3);

            Assert.Multiple(() => {
                Assert.That(msg, Does.Contain("Region 4"));
                Assert.That(msg, Does.Contain("no averaged HFR was recorded"));
                Assert.That(msg, Does.Contain("1 of 3 sub-frame(s) completed"));
                Assert.That(msg, Does.Contain("Measured HFR=null"));
            });
        }

        [Test]
        public void DescribeHfrValidationFailure_MultiFrame_ListsEachSubMeasurement() {
            var subs = new List<MeasureAndError> {
                new MeasureAndError { Measure = 0.0, Stdev = 0.0 },
                new MeasureAndError { Measure = 0.0, Stdev = double.NaN },
            };
            var msg = AutoFocusEngine.DescribeHfrValidationFailure("initial position", 5, new MeasureAndError { Measure = 0.0, Stdev = 0.0 }, subs, framesPerPoint: 2);

            Assert.Multiple(() => {
                Assert.That(msg, Does.Contain("sub-frames 2/2"));
                Assert.That(msg, Does.Contain("0.00 (σ=0.00)"));
                Assert.That(msg, Does.Contain("0.00 (σ=NaN)"));
                // Any NaN sub-frame means an analysis error dominated the classification.
                Assert.That(msg, Does.Contain("analysis errored on 1 of 2"));
            });
        }

        // --- UseExactImagingFilter (per-filter wizard target sweeps) ---
        // SetAutofocusFilter substitutes the designated AF filter when UseFilterWheelOffsets is on. The
        // options-aware overload must skip that substitution — and move the wheel to the requested filter —
        // when UseExactImagingFilter is set, so a wizard target-filter sweep exposes through EXACTLY the
        // chosen filter. Default options must preserve the substitution byte-for-byte.

        private static IProfileService ProfileWithAfFilter(out FilterInfo afFilter, out FilterInfo targetFilter) {
            var profileService = Substitute.For<IProfileService>();
            afFilter = new FilterInfo("Lum", 0, 0) { AutoFocusFilter = true };
            targetFilter = new FilterInfo("Ha", 0, 1);
            profileService.ActiveProfile.FocuserSettings.UseFilterWheelOffsets.Returns(true);
            profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Returns(
                new ObserveAllCollection<FilterInfo>(new[] { afFilter, targetFilter }));
            return profileService;
        }

        private static IFilterWheelMediator EchoingFilterWheel() {
            var filterWheelMediator = Substitute.For<IFilterWheelMediator>();
            filterWheelMediator.ChangeFilter(Arg.Any<FilterInfo>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<ApplicationStatus>>())
                .Returns(ci => Task.FromResult(ci.Arg<FilterInfo>()));
            return filterWheelMediator;
        }

        [Test]
        public async Task SetAutofocusFilter_UseExactImagingFilter_MovesToExactFilterAndSkipsAfSubstitution() {
            var profileService = ProfileWithAfFilter(out var afFilter, out var targetFilter);
            var filterWheelMediator = EchoingFilterWheel();
            var engine = Build(profileService, filterWheelMediator: filterWheelMediator);
            var options = new AutoFocusEngineOptions { UseExactImagingFilter = true };

            var result = await engine.SetAutofocusFilter(options, targetFilter, CancellationToken.None, null);

            Assert.That(result, Is.SameAs(targetFilter), "the exact imaging filter is used, not the designated AF filter");
            _ = filterWheelMediator.Received(1).ChangeFilter(targetFilter, Arg.Any<CancellationToken>(), Arg.Any<IProgress<ApplicationStatus>>());
        }

        [Test]
        public async Task SetAutofocusFilter_DefaultOptions_StillSubstitutesDesignatedAfFilter() {
            var profileService = ProfileWithAfFilter(out var afFilter, out var targetFilter);
            var filterWheelMediator = EchoingFilterWheel();
            var engine = Build(profileService, filterWheelMediator: filterWheelMediator);
            var options = new AutoFocusEngineOptions(); // UseExactImagingFilter defaults to false

            var result = await engine.SetAutofocusFilter(options, targetFilter, CancellationToken.None, null);

            Assert.That(result, Is.SameAs(afFilter), "default behavior unchanged: the AF filter substitutes the imaging filter");
            _ = filterWheelMediator.Received(1).ChangeFilter(afFilter, Arg.Any<CancellationToken>(), Arg.Any<IProgress<ApplicationStatus>>());
        }

        [Test]
        public async Task SetAutofocusFilter_UseExactImagingFilter_NullImagingFilter_FallsBackToAfSubstitution() {
            var profileService = ProfileWithAfFilter(out var afFilter, out _);
            var filterWheelMediator = EchoingFilterWheel();
            var engine = Build(profileService, filterWheelMediator: filterWheelMediator);
            var options = new AutoFocusEngineOptions { UseExactImagingFilter = true };

            var result = await engine.SetAutofocusFilter(options, null, CancellationToken.None, null);

            Assert.That(result, Is.SameAs(afFilter), "no exact filter to honor: fall back to the designated AF filter");
        }

        private sealed class TempDir : IDisposable {
            public string Path { get; }
            public TempDir() {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hf-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }
            public void Dispose() {
                try { Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
            }
        }
    }
}
