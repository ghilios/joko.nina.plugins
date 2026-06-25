using NINA.Core.Enum;
using NINA.Core.Interfaces;
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
using System;
using System.Collections.Generic;
using System.IO;
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
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector = null) {
            return new AutoFocusEngine(
                profileService: profileService ?? Substitute.For<IProfileService>(),
                cameraMediator: Substitute.For<ICameraMediator>(),
                filterWheelMediator: Substitute.For<IFilterWheelMediator>(),
                focuserMediator: Substitute.For<IFocuserMediator>(),
                guiderMediator: Substitute.For<IGuiderMediator>(),
                imagingMediator: Substitute.For<IImagingMediator>(),
                imageDataFactory: Substitute.For<IImageDataFactory>(),
                starDetectionSelector: starDetectionSelector ?? Substitute.For<IPluggableBehaviorSelector<IStarDetection>>(),
                autoFocusOptions: autoFocusOptions ?? Substitute.For<IAutoFocusOptions>(),
                alglibAPI: new AlglibAPI());
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
