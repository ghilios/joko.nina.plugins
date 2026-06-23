# PR #88 / #89 / #91 Post-Merge Review Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Clear context (`/clear`) before executing** per the project CLAUDE.md.

**Goal:** Fix all 37 verified findings from the after-the-fact code review of the already-merged PRs #88 (Aberration Inspector Review Frames), #89 (Tilt-wizard cross-thread + AF in-progress guard), and #91 (manual-AutoFocus pane Review Frames), against the current `develop`.

**Architecture:** Fixes are grouped into 6 in-place phases (correctness → memory/lifecycle → annotator fidelity → thread-safety → viewer-control lifecycle → test hardening) that ship as one PR, plus a 7th de-duplication/altitude refactor that ships as a **separate follow-up PR** because it relocates code the earlier phases touch. Each task is TDD where the logic is unit-testable (the test project links the plugin's source files directly), and falls back to an explicit manual-NINA verification step for UI-thread / XAML / memory-pinning behavior the current harness cannot exercise.

**Tech Stack:** C# / .NET 8.0-windows, WPF, MEF composition, CommunityToolkit.Mvvm, NUnit 4.4.0 + NUnit3TestAdapter.

---

## Background & scope

These three PRs were merged to `develop` without a pre-merge review. A 10-angle multi-agent review (correctness × 5, cleanup × 3, altitude, conventions) over the merged diffs produced 60 candidates → 39 deduped → **37 verified** (CONFIRMED or PLAUSIBLE; nothing REFUTED survived). Of those, **35 are fixed by this plan**; **F02 and F03 were reviewed and cancelled as by-design** (see Phase 3, Tasks 1–3) — the Review Frames window is meant to show *all* detected-star annotations even when the live-image Show-Annotations toggle is off or the live annotator caps the displayed stars.

**Severity mix (to fix):** 8 medium, 27 low (F02 and F03, both medium, cancelled as by-design). There are no longer any "high" items — the single high from the capped top-15 report (F01) was re-graded **medium** during full synthesis because PR #89's `finally` fix already prevents the static-guard *brick*; F01 now only causes a spurious hard-fail of the optimizer's live attempt (still the highest-priority correctness item).

**Conventions (apply to every task):**
- Build/test the whole suite: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`. The suite currently passes (1436+); new tests raise the count. **Never** skip/ignore/xfail a test to make the suite pass — fix the cause.
- Commit with the privacy email (GitHub blocks the gmail address):
  ```
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<msg>"
  ```
- **Never push to `develop`.** Create the feature branch, push it, open a PR.
- Invariant reminders that apply here: a new persisted option needs a UI control (none added in this plan); a new `StarDetectorMetrics` field needs a metrics-panel row (none added).

## Branch & PR strategy — two sequential PRs

**This work ships as two PRs, and PR B must not begin until PR A has been merged into `develop`.**

**PR A — `ghilios/pr-88-89-91-review-fixes`** (Phases 1–6): the correctness / memory / fidelity / thread-safety / viewer-lifecycle / test fixes. Independent changes that land together.
1. Branch from current `develop`: `git checkout develop && git pull && git checkout -b ghilios/pr-88-89-91-review-fixes`.
2. Execute Phases 1 → 6 in order.
3. Run the full suite, push, open **PR A** into `develop`, and get it **merged**.

**🚦 GATE — do not start PR B until PR A is merged into `develop`.** Phase 7 relocates/absorbs code that Phases 1–6 edit; starting it earlier forces every Phase 1–6 edit to be re-applied against moved code (or creates conflicting refactors).

**PR B — `ghilios/frame-review-dedup-refactor`** (Phase 7): the de-duplication + altitude refactor. Only after PR A is merged:
1. Re-sync so the branch starts from a `develop` that already contains PR A: `git checkout develop && git pull`.
2. Branch: `git checkout -b ghilios/frame-review-dedup-refactor`.
3. Execute Phase 7; each task names the Phase 1–6 edit it supersedes — fold those into the extracted shared code rather than leaving duplicates.
4. Run the full suite, push, open **PR B** into `develop`.

## Execution order & shared-file sequencing

Several files are touched by more than one phase. Within PR A, apply phases in number order; within a phase, apply tasks in number order. Key shared files (each task carries a ⚠ note):
- `AutoFocus/AutoFocusEngine.cs` — F01 (P1), F11 (P4), F17 (P6 test references the guard). Do P1 then P4 then P6.
- `AutoFocus/HocusFocusVM.cs` — F05/F04/F06/F22/F27/F21 (P2). All consolidated in P2; P7 later extracts helpers from it.
- `AutoFocus/InspectorVM.cs` — F09/F10 (P1), F36 (P2).
- `AutoFocus/Review/AutoFocusFrameReviewVM.cs` — F02/F03/F30/F26/F18 (P3), Dispose (P2/P5).
- `Utility/ApplicationDispatcher.cs` — F12/F13/F14 (P4, one task). **Note:** this file was *not* in any PR diff; PR #89 newly relies on it, so its fixes are included here as in-scope hardening.
- The two review UserControls + `DataTemplates.xaml` — touched by P3/P5 and then restructured by P7.

## Findings → phase map

| Phase | Findings | Theme |
|---|---|---|
| 1 | F01, F09, F10, F35, F37 | Correctness (behavior-wrong) |
| 2 | F05, F04, F06, F22, F27, F21, F36 | Memory & run-lifecycle |
| 3 | ~~F02~~, ~~F03~~, F30, F26+F18, F29, F31, F32 | AF-Review annotator fidelity & UI |
| 4 | F11, F12+F13+F14, F15, F16 | Thread-safety & dispatcher hardening |
| 5 | F19, F33, F34 | Review-viewer control lifecycle |
| 6 | F17 | Test hardening |
| 7 (PR B) | F08, F23, F24, F25, F07, F20, F28 | De-duplication & altitude refactor |
| — | ~~F02~~, ~~F03~~ | **Cancelled — by-design** (review window must show *all* detected-star annotations regardless of the global Show-Annotations toggle or the live `MaxStars` cap) |

> Task numbering restarts at 1 within each phase. Full per-finding detail (summary / failure scenario) lives in the review report; each task restates the root cause it fixes.

---

## Phase 1: Correctness fixes (behavior-wrong bugs)

### Task 1: Null-safe progress reporting in AF retry catch blocks (F01)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs` (lines 1208, 1213, inside `RunAutoFocus`)

**Finding:** F01 [MEDIUM/CONFIRMED #89]

⚠ shared-file note: `AutoFocusEngine.cs` is also touched by other themes (F11 atomic guard, F17/F28 subscription work). Sequence this edit independently — it only changes two lines in the retry catch blocks.

- [ ] Confirm current code: the two retry catch blocks in `RunAutoFocus` dereference `progress` unconditionally while the rest of the run path (e.g. line 1554 `progress?.Report(...)`) is null-safe. The optimizer live attempt calls `Run(options, null, token, null)`, so an iteration that throws `TooManyFailedMeasurementsException`/`InitialHFRFailedException` hits these lines with `progress == null` and converts a recoverable iteration into a hard abort.
- [ ] Apply the `TooManyFailedMeasurementsException` edit at line 1208:
  ```csharp
  // before
                        progress.Report(new ApplicationStatus() { Status = Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"] });
  // after
                        progress?.Report(new ApplicationStatus() { Status = Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"] });
  ```
- [ ] Apply the `InitialHFRFailedException` edit at line 1213:
  ```csharp
  // before
                        progress.Report(new ApplicationStatus() { Status = "Calculating initial HFR failed" });
  // after
                        progress?.Report(new ApplicationStatus() { Status = "Calculating initial HFR failed" });
  ```
- [ ] Manual NINA verification (not unit-tested: triggering these branches requires driving a full engine sweep with a mocked focuser/camera/detector that throws the specific failed-measurement exceptions mid-iteration with `progress == null`, which the current test harness cannot assemble): Open the Star Detection Optimizer wizard, start a live optimization attempt against a field/exposure that produces too few measured points (e.g. very short exposure / heavy crop). Confirm the run logs "Too many failed points" and re-attempts or falls through to OnFailed rather than aborting with a generic "Auto Focus Failure", and that subsequent manual AF still works (guard not bricked).
- [ ] Build to confirm it compiles: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
- [ ] Commit:
  ```
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(autofocus): null-safe progress.Report in AF retry catch blocks (F01)"
  ```

### Task 2: Derive frame-review retention from the resolved capture-time SCM flag (F09)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` (line 1041 in `GetAutoFocusEngineOptions`; line 535 gate in `AnalyzeAutoFocusResult`)

**Finding:** F09 [MEDIUM/CONFIRMED #88]

⚠ shared-file note: `InspectorVM.cs` is edited by both Task 2 and Task 3 (and other themes). Apply Task 2 then Task 3 in order; they touch different regions but both reference `frameReviewRequestedForRun`.

Root cause: `frameReviewRequestedForRun` is set in `GetAutoFocusEngineOptions` (line 1041) from the live `inspectorOptions.SensorCurveModelEnabled`, but the replay path runs `AnalyzeAutoFocusResult` with the resolved `sensorCurveModelEnabled` (line 1119/1145) which can differ. The snapshot-build gate (line 535) and the SCM block (line 500) both key off the resolved flag, so the two disagree. Fix: make the request flag depend on the actually-resolved SCM flag inside `AnalyzeAutoFocusResult`, and keep `PreserveExposures` forced on whenever review is enabled at run start (over-retention is harmless and the live path is unchanged).

- [ ] Write the failing test first. Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Inspection/FrameReviewRetentionDecisionTests.cs`. Because `frameReviewRequestedForRun` itself is a private VM field driven by full DI, extract the pure decision into a testable static helper (added in the next step) and test that helper:
  ```csharp
  using NINA.Joko.Plugins.HocusFocus.AutoFocus;
  using NUnit.Framework;

  namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection {

      [TestFixture]
      public class FrameReviewRetentionDecisionTests {

          [Test]
          public void Requested_TrueOnlyWhenReviewEnabledAndResolvedScmEnabled() {
              Assert.Multiple(() => {
                  Assert.That(InspectorVM.IsFrameReviewRequested(frameReviewEnabled: true, resolvedSensorCurveModelEnabled: true), Is.True);
                  Assert.That(InspectorVM.IsFrameReviewRequested(frameReviewEnabled: true, resolvedSensorCurveModelEnabled: false), Is.False);
                  Assert.That(InspectorVM.IsFrameReviewRequested(frameReviewEnabled: false, resolvedSensorCurveModelEnabled: true), Is.False);
                  Assert.That(InspectorVM.IsFrameReviewRequested(frameReviewEnabled: false, resolvedSensorCurveModelEnabled: false), Is.False);
              });
          }
      }
  }
  ```
  Note: `InspectorVM.cs` is linked directly into the test project, so the static helper is reachable without an assembly reference (matching the project's link-source pattern).
- [ ] Run it, expecting a COMPILE FAIL (`InspectorVM.IsFrameReviewRequested` does not yet exist): `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter Requested_TrueOnlyWhenReviewEnabledAndResolvedScmEnabled`
- [ ] Add the pure helper to `InspectorVM.cs` immediately above `GetAutoFocusEngineOptions` (around line 1020):
  ```csharp
  // Pure retention decision: Review Frames needs both the user toggle on AND the resolved (capture-time on
  // replay) sensor-curve-model flag that actually gates the snapshot build, so the two never disagree.
  internal static bool IsFrameReviewRequested(bool frameReviewEnabled, bool resolvedSensorCurveModelEnabled) {
      return frameReviewEnabled && resolvedSensorCurveModelEnabled;
  }
  ```
- [ ] In `GetAutoFocusEngineOptions`, change line 1041 so retention/PreserveExposures is forced whenever review is enabled (independent of the live SCM flag), since the resolved flag is not yet known here:
  ```csharp
  // before
              frameReviewRequestedForRun = inspectorOptions.FrameReviewEnabled && inspectorOptions.SensorCurveModelEnabled;
              if (options.Save || frameReviewRequestedForRun) {
                  options.PreserveExposures = true;
              }
  // after
              // Force exposure retention whenever review is enabled; the definitive frameReviewRequestedForRun
              // (which gates the snapshot build) is set in AnalyzeAutoFocusResult from the RESOLVED sensor-curve
              // flag, because option (b) replay runs with the captured flag, not the live inspectorOptions one.
              if (options.Save || inspectorOptions.FrameReviewEnabled) {
                  options.PreserveExposures = true;
              }
  ```
- [ ] In `AnalyzeAutoFocusResult`, set the definitive request flag from the resolved `sensorCurveModelEnabled` parameter at the top of the SCM block. Replace the snapshot-build gate condition at line 535 by first assigning the field. Edit the block starting at line 500:
  ```csharp
  // before
              if (sensorCurveModelEnabled) {
                  double focuserSizeMicrons = InspectorOptions.MicronsPerFocuserStep;
  // after
              // Resolve the definitive review request from the SAME flag that gates this block (capture-time flag on replay).
              frameReviewRequestedForRun = IsFrameReviewRequested(inspectorOptions.FrameReviewEnabled, sensorCurveModelEnabled);
              if (sensorCurveModelEnabled) {
                  double focuserSizeMicrons = InspectorOptions.MicronsPerFocuserStep;
  ```
- [ ] Run the test again, expecting PASS: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter Requested_TrueOnlyWhenReviewEnabledAndResolvedScmEnabled`
- [ ] Commit:
  ```
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(inspector): gate Review Frames on resolved capture-time SCM flag (F09)"
  ```

### Task 3: Build the Review Frames snapshot even when the sensor-model fit fails (F10)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` (lines 500-547 in `AnalyzeAutoFocusResult`)

**Finding:** F10 [MEDIUM/CONFIRMED #88]

⚠ shared-file note: same file as Task 2; apply after Task 2 (this builds on the `frameReviewRequestedForRun` assignment Task 2 moved into this block).

Root cause: the snapshot is built (lines 535-546) only after `await SensorModel.UpdateModel(...)` (line 512), which throws on a failed/poor fit (`SensorModel.cs` "Failed to find a good model" / "Sensor modeling failed" / "Need at least 9 registered stars"), so the per-frame data the builder handles gracefully is never surfaced — defeating the feature's primary "see why the fit looks wrong" purpose. Fix: wrap `UpdateModel` (and the registered-image save) in a `try/finally` that builds the snapshot from whatever `FullSensorDetectedStars` + `SensorModel.SensorModelResult` are available, then re-throws so existing failure handling is unchanged.

- [ ] This is not unit-tested at the VM level: the throw originates inside `SensorModel.UpdateModel`, which requires a full alglib registration/fit pipeline over real star data to fail in the documented ways, and the snapshot build reads `SensorModel.SensorModelResult.RegisteredStars`/`ReferenceImage` populated by that same call — the `FrameReviewSnapshotBuilder.Build` pure path is already covered by `FrameReviewSnapshotBuilderTests`. Instead, restructure for try/finally and verify manually.
- [ ] Refactor the SCM block so the snapshot is built in a `finally`. Replace the body from line 511 (`var finalFocuserPosition = ...`) through line 546 (`NotifyReviewFramesAvailabilityChanged();`) with:
  ```csharp
  // before
                  var finalFocuserPosition = result.RegionResults[0].EstimatedFinalFocuserPosition;
                  await SensorModel.UpdateModel(
                      FullSensorDetectedStars,
                      fRatio: profileService.ActiveProfile.TelescopeSettings.FocalRatio,
                      focuserSizeMicrons: focuserSizeMicrons,
                      finalFocusPosition: finalFocuserPosition,
                      stepSize: result.StepSize,
                      progress,
                      ct: ct);

                  if (!suppressRegisteredImages && ((!forRerun) || (inspectorOptions.SaveImagesOnReruns))) {
                      if (!String.IsNullOrEmpty(result.SaveFolder)) {
                          await SaveRegisteredImages(result.SaveFolder,
                              SensorModel.SensorModelResult.RegisteredStars,
                              SensorModel.TrianglesByImage,
                              SensorModel.ReferenceImage,
                              inspectorOptions.SaveAlignmentImages);
                      }
                  }

                  // Build the Review Frames snapshot from the same full-sensor detections and registration just computed.
                  // UpdateModel has already applied the alignment transforms, so Position/OriginalPosition/BoundingBox are
                  // final. Runs after the sweep completes (no concurrent SubMeasurementPointCompleted adds), but copy the
                  // list under the lock to stay consistent with the rest of the class.
                  if (frameReviewRequestedForRun) {
                      List<SensorDetectedStars> framesForReview;
                      lock (fullSensorDetectedStarsLock) {
                          framesForReview = FullSensorDetectedStars.ToList();
                      }
                      reviewSnapshot = FrameReviewSnapshotBuilder.Build(
                          framesForReview,
                          SensorModel.SensorModelResult.RegisteredStars,
                          SensorModel.ReferenceImage,
                          inspectorOptions.UseRANSAC);
                      NotifyReviewFramesAvailabilityChanged();
                  }
  // after
                  var finalFocuserPosition = result.RegionResults[0].EstimatedFinalFocuserPosition;
                  try {
                      await SensorModel.UpdateModel(
                          FullSensorDetectedStars,
                          fRatio: profileService.ActiveProfile.TelescopeSettings.FocalRatio,
                          focuserSizeMicrons: focuserSizeMicrons,
                          finalFocusPosition: finalFocuserPosition,
                          stepSize: result.StepSize,
                          progress,
                          ct: ct);

                      if (!suppressRegisteredImages && ((!forRerun) || (inspectorOptions.SaveImagesOnReruns))) {
                          if (!String.IsNullOrEmpty(result.SaveFolder)) {
                              await SaveRegisteredImages(result.SaveFolder,
                                  SensorModel.SensorModelResult.RegisteredStars,
                                  SensorModel.TrianglesByImage,
                                  SensorModel.ReferenceImage,
                                  inspectorOptions.SaveAlignmentImages);
                          }
                      }
                  } finally {
                      // Build the Review Frames snapshot even if UpdateModel threw (failed/poor fit): the per-frame
                      // detections + bitmaps are exactly what the user needs to "see why the fit looks wrong". The
                      // builder handles a null/partial registration result gracefully. Don't let snapshot-build errors
                      // mask the original UpdateModel exception. Skip on cancellation.
                      if (frameReviewRequestedForRun && !ct.IsCancellationRequested) {
                          try {
                              List<SensorDetectedStars> framesForReview;
                              lock (fullSensorDetectedStarsLock) {
                                  framesForReview = FullSensorDetectedStars.ToList();
                              }
                              reviewSnapshot = FrameReviewSnapshotBuilder.Build(
                                  framesForReview,
                                  SensorModel.SensorModelResult?.RegisteredStars,
                                  SensorModel.ReferenceImage,
                                  inspectorOptions.UseRANSAC);
                              NotifyReviewFramesAvailabilityChanged();
                          } catch (Exception snapEx) {
                              Logger.Warning($"Failed to build Review Frames snapshot after model fit: {snapEx.Message}");
                          }
                      }
                  }
  ```
- [ ] Confirm `FrameReviewSnapshotBuilder.Build` already tolerates a null `registeredStars` argument (it null-guards `registeredStars` in both loops and `referenceImageIndex`/`SensorModel.ReferenceImage` is a plain int) — verified in `FrameReviewSnapshotBuilder.Build` (lines 151, 170) and by the existing `Build_EmptyInput_ProducesEmptySnapshot` test which passes an empty registered array. No builder change needed.
- [ ] Run the full snapshot-builder suite to confirm the builder contract is unbroken: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FrameReviewSnapshotBuilderTests` (expect PASS)
- [ ] Manual NINA verification: enable "Sensor Curve Model" + "Keep frames for Review", run a Detailed Analysis on deliberately poor data (few/very defocused stars) so the fit fails with a "Sensor modeling failed" notification. Confirm the failure notification still appears AND the "Review Frames" button is now enabled, opening a window showing the per-frame detections/bitmaps.
- [ ] Commit:
  ```
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(inspector): build Review Frames snapshot even on failed sensor-model fit (F10)"
  ```

### Task 4: Tolerance-based registration-line and reference-frame robustness in snapshot builder (F35, F37)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Inspection/FrameReviewSnapshot.cs` (line 220 `isReference` derivation; lines 250-251 `hasRegistrationLine`)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Inspection/FrameReviewSnapshotBuilderTests.cs`

**Finding:** F35 [LOW/PLAUSIBLE #88], F37 [LOW/PLAUSIBLE #88]

These are two pure-logic fixes in the same `Build` method, so they are grouped into one task per the grouping rule.

- [ ] Write both failing tests first. Append to `FrameReviewSnapshotBuilderTests.cs` (inside the existing `[TestFixture]`, matching the existing helpers `MakeStar`/`MakeFrame`/`MakeRegistered`/`MakeImage`):
  ```csharp
          // F35: a registered star whose aligned position differs by less than a float ULP must still draw its line.
          [Test]
          public void Build_NonReferenceAlignedFrame_SubUlpMovement_StillHasLine() {
              // posX/posY (aligned target) is one ULP above origX/origY (raw center): exact != would treat as "not moved".
              float rawX = 1000f;
              float alignedX = MathF.BitIncrement(rawX); // differs by 1 ULP, > 0 but tiny
              var star = MakeStar(posX: alignedX, posY: 44, origX: rawX, origY: 44);
              var refFrame = MakeFrame(100, new[] { MakeStar() }, MakeImage());
              var movedFrame = MakeFrame(200, new[] { star }, MakeImage(), alignment: Matrix3x2.Identity, hasBeenAligned: true);
              var registered = new[] { MakeRegistered((star, 1)) };

              var snapshot = FrameReviewSnapshotBuilder.Build(new[] { refFrame, movedFrame }, registered, referenceImageIndex: 0, ransacEnabled: true);

              Assert.That(snapshot.Frames.Single(f => f.ImageIndex == 1).Stars.Single().HasRegistrationLine, Is.True);
          }

          // F37: when the reference frame's image was dropped, a surviving frame must still be flagged IsReference.
          [Test]
          public void Build_DroppedReferenceImage_FlagsSurvivingReferenceCandidate() {
              var refStar = MakeStar(posX: 10, posY: 10);
              var droppedRefFrame = MakeFrame(100, new[] { refStar }, null); // reference image not retained -> dropped
              var otherStar = MakeStar(posX: 10, posY: 10);
              var otherFrame = MakeFrame(200, new[] { otherStar }, MakeImage(), hasBeenAligned: false);
              var registered = new[] { MakeRegistered((refStar, 0), (otherStar, 1)) };

              var snapshot = FrameReviewSnapshotBuilder.Build(new[] { droppedRefFrame, otherFrame }, registered, referenceImageIndex: 0, ransacEnabled: true);

              Assert.Multiple(() => {
                  // The dropped reference is gone; no surviving frame should falsely render registration lines/transform
                  // as if it were a non-reference aligned frame against a missing reference.
                  Assert.That(snapshot.Frames.Count, Is.EqualTo(1));
                  Assert.That(snapshot.Frames.Single().Stars.Any(s => s.HasRegistrationLine), Is.False);
                  Assert.That(snapshot.Frames.Single().TransformText, Is.EqualTo(""));
              });
          }
  ```
  Ensure `using System;` is present in the test file for `MathF` (add it to the using block if missing).
- [ ] Run both, expecting FAIL (F35: exact `!=` is false for a sub-ULP move so no line; F37: the surviving frame is treated as a non-reference frame and would draw a registration line/transform against the missing reference): `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "Build_NonReferenceAlignedFrame_SubUlpMovement_StillHasLine|Build_DroppedReferenceImage_FlagsSurvivingReferenceCandidate"`
- [ ] Fix F35: replace the exact float inequality at lines 248-251 with a tolerance test:
  ```csharp
  // before
                          // The line is only meaningful for a registered star on an aligned (RANSAC) non-reference frame
                          // whose aligned position differs from its raw position.
                          bool hasRegistrationLine = ransacEnabled && !isReference && registrationId != null
                              && (targetX != centerX || targetY != centerY);
  // after
                          // The line is only meaningful for a registered star on an aligned (RANSAC) non-reference frame
                          // whose aligned position differs from its raw position. Use a tolerance (not exact !=) so a
                          // genuinely-registered star whose alignment maps it to a near-identical position still draws.
                          const double registrationLineEpsilon = 1e-6;
                          bool hasRegistrationLine = ransacEnabled && !isReference && registrationId != null
                              && (Math.Abs(targetX - centerX) + Math.Abs(targetY - centerY) > registrationLineEpsilon);
  ```
- [ ] Fix F37: make the reference frame robust to a dropped reference image. The simplest correct fix that satisfies the test and the finding is to short-circuit the per-frame `isReference`/line/transform logic when the reference image was dropped, so a surviving non-reference frame is never rendered as if aligned against a present reference. Add a guard computed once before the per-frame loop, just after the `frames`/`frameCount` declarations (around line 211-213):
  ```csharp
  // before
              var frames = new List<FrameReviewFrame>();
              var frameCount = allDetectedStars?.Count ?? 0;
              for (int imageIndex = 0; imageIndex < frameCount; imageIndex++) {
                  var sds = allDetectedStars[imageIndex];
                  var bitmap = sds?.Image?.Image;
                  if (bitmap == null) {
                      continue; // Drop frames whose per-frame image was not retained.
                  }

                  var isReference = imageIndex == referenceImageIndex;
  // after
              var frames = new List<FrameReviewFrame>();
              var frameCount = allDetectedStars?.Count ?? 0;
              // If the reference frame's image was dropped, no surviving frame is the reference; in that case the
              // alignment is relative to a frame the user can't see, so suppress registration lines/transform text
              // rather than rendering survivors as if aligned against a present reference (F37).
              bool referenceSurvives = referenceImageIndex >= 0 && referenceImageIndex < frameCount
                  && allDetectedStars[referenceImageIndex]?.Image?.Image != null;
              for (int imageIndex = 0; imageIndex < frameCount; imageIndex++) {
                  var sds = allDetectedStars[imageIndex];
                  var bitmap = sds?.Image?.Image;
                  if (bitmap == null) {
                      continue; // Drop frames whose per-frame image was not retained.
                  }

                  var isReference = imageIndex == referenceImageIndex;
  ```
- [ ] Apply the `referenceSurvives` guard to the registration line (extend the F35 condition):
  ```csharp
  // before
                          const double registrationLineEpsilon = 1e-6;
                          bool hasRegistrationLine = ransacEnabled && !isReference && registrationId != null
                              && (Math.Abs(targetX - centerX) + Math.Abs(targetY - centerY) > registrationLineEpsilon);
  // after
                          const double registrationLineEpsilon = 1e-6;
                          bool hasRegistrationLine = ransacEnabled && referenceSurvives && !isReference && registrationId != null
                              && (Math.Abs(targetX - centerX) + Math.Abs(targetY - centerY) > registrationLineEpsilon);
  ```
- [ ] Apply the `referenceSurvives` guard to the transform text (line 271-273):
  ```csharp
  // before
                  var transformText = (!isReference && ransacEnabled && sds.AlignmentTransform != null)
                      ? FrameReviewTransformFormatter.Format(sds.AlignmentTransform)
                      : string.Empty;
  // after
                  var transformText = (referenceSurvives && !isReference && ransacEnabled && sds.AlignmentTransform != null)
                      ? FrameReviewTransformFormatter.Format(sds.AlignmentTransform)
                      : string.Empty;
  ```
- [ ] Run both tests again, expecting PASS, then the whole builder suite to confirm no regression (the existing `Build_NonReferenceAlignedFrame_*`/`Build_ReferenceFrame_*` tests all use a surviving reference image so they remain green): `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FrameReviewSnapshotBuilderTests`
- [ ] Commit:
  ```
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(inspector): tolerance-based registration line + dropped-reference robustness in Review snapshot (F35, F37)"
  ```

**Phase verification:** run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` and expect "1436+ pass, 0 fail" (this phase adds 3 new tests: `Requested_TrueOnlyWhenReviewEnabledAndResolvedScmEnabled`, `Build_NonReferenceAlignedFrame_SubUlpMovement_StillHasLine`, `Build_DroppedReferenceImage_FlagsSurvivingReferenceCandidate`).

---

## Phase 2: Memory & run-lifecycle (frame retention release)

This phase makes the "Review Frames" feature actually release the retained per-frame bitmaps and raw `IRenderedImage` buffers when promised, removes redundant retention, and tightens the snapshot field's threading contract. Several tasks edit `AutoFocus/HocusFocusVM.cs` (shared with other phases — see notes).

⚠ **shared-file note:** `AutoFocus/HocusFocusVM.cs` is edited by Tasks 1, 2, 3, 4, 5 below; the assembler should sequence this phase's `HocusFocusVM.cs` edits together and apply them in task order to avoid conflicting hunks. `AutoFocus/InspectorVM.cs` (Task 6) and the two review VMs are also touched by the de-duplication / annotator-mirroring phases — coordinate ordering.

---

### Task 1: Release source `IRenderedImage` buffers once the snapshot is built

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` (lines 776–782, `BuildFrameReviewSnapshotIfRequested`)

**Finding:** F05 [MEDIUM/CONFIRMED #91]

This is UI/engine-lifecycle plumbing on a non-pure method whose only observable effect is reference release (no return value, no VM-observable state beyond `ReviewFramesAvailable` which is already covered) — it cannot be unit-tested with the current harness, so a manual verification step is given. The reason it isn't unit-tested: `reviewFrames` and the build run on the engine's background completion callback against real `IRenderedImage`/`BitmapSource` objects that the test harness does not construct.

- [ ] In `BuildFrameReviewSnapshotIfRequested`, clear `reviewFrames` inside the existing lock immediately after the `.ToList()` projection, so the source `IRenderedImage` objects are released once their bitmaps are captured into the snapshot list. Before:
  ```csharp
            List<(double, HocusFocusStarDetectionResult, IRenderedImage)> framesForReview;
            lock (frameReviewLock) {
                framesForReview = reviewFrames
                    .Select(f => (f.FocuserPosition, f.Result, f.Image))
                    .ToList();
            }
  ```
  After:
  ```csharp
            List<(double, HocusFocusStarDetectionResult, IRenderedImage)> framesForReview;
            lock (frameReviewLock) {
                framesForReview = reviewFrames
                    .Select(f => (f.FocuserPosition, f.Result, f.Image))
                    .ToList();
                // The snapshot captures only each frame's display BitmapSource; once built, the source
                // IRenderedImage buffers (raw 16-bit pixels + statistics) are no longer needed, so drop
                // our references here instead of keeping them pinned until the next run (F05).
                reviewFrames.Clear();
            }
  ```
- [ ] **Manual NINA verification:** With "Keep frames for review" on, run a manual 9-point AF on a large-sensor camera; after the run completes (Review Frames button enabled), confirm via Task Manager / a memory profiler that working set drops by roughly the size of the 9 raw frames relative to the prior build (only the 8-bit display bitmaps remain). Open Review Frames and confirm all 9 frames still render correctly (proving the snapshot retained what it needs).
- [ ] `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(autofocus): release raw review frame buffers once snapshot is built (F05)"`

---

### Task 2: Release retained frames when a run ends without producing a snapshot (cancel / no-init)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` (lines 565–567 `StartAutoFocus` finally; lines 932–934 `LoadSavedAutoFocusRun` finally)

**Finding:** F22 [LOW/CONFIRMED #91]

This is a finally/cancellation-path cleanup on methods that drive the focuser and depend on the engine's background events; it cannot be exercised by the current unit harness (no engine/focuser doubles emit `SubMeasurementPointCompleted` with real images), so a manual verification step is given.

- [ ] Add a private helper next to `BuildFrameReviewSnapshotIfRequested` (insert after the closing brace of `BuildFrameReviewSnapshotIfRequested`, before the `NotifyReviewFramesAvailabilityChanged` comment block at line 786):
  ```csharp
        // Release any per-frame images accumulated this run when no snapshot was built (cancellation, or a
        // run that returned without firing Completed/Failed). Safe to call unconditionally — it is a no-op
        // when nothing was retained. (F22)
        private void ReleaseUnsnapshottedReviewFrames() {
            lock (frameReviewLock) {
                reviewFrames.Clear();
            }
        }
  ```
- [ ] In `StartAutoFocus`, change the finally so frames are released on every exit path that did not build a snapshot. Before:
  ```csharp
            } finally {
                AutoFocusInProgress = false;
            }
        }

        public AutoFocusReport LastReport { get; private set; }
  ```
  After:
  ```csharp
            } finally {
                // A successful run already moved frames into the snapshot (and cleared reviewFrames); this
                // covers cancellation / null-init paths where Completed/Failed never fired, so captured
                // exposures aren't pinned until the next run. (F22)
                ReleaseUnsnapshottedReviewFrames();
                AutoFocusInProgress = false;
            }
        }

        public AutoFocusReport LastReport { get; private set; }
  ```
- [ ] In `LoadSavedAutoFocusRun`, apply the same release in its finally. Before:
  ```csharp
            } finally {
                AutoFocusInProgress = false;
            }
        }

        private void CancelLoadSavedAutoFocusRun() {
  ```
  After:
  ```csharp
            } finally {
                ReleaseUnsnapshottedReviewFrames();
                AutoFocusInProgress = false;
            }
        }

        private void CancelLoadSavedAutoFocusRun() {
  ```
- [ ] **Manual NINA verification:** With "Keep frames for review" on, start a manual AF and cancel it mid-sweep (after a few points). Confirm the Review Frames button stays disabled and that working set returns to the pre-run baseline (no frames pinned). Repeat by cancelling a Reprocess Saved Run. Not unit-tested because triggering accumulation then cancellation requires the engine to fire real `SubMeasurementPointCompleted` events with non-null `IRenderedImage`s, which the harness does not provide.
- [ ] `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(autofocus): release retained AF review frames on cancel/no-snapshot end (F22)"`

---

### Task 3: Release the pane snapshot + frames when the Review window closes (F04, F06)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` (lines 808–819, `ShowFrameReview` onClosed handler)

**Finding:** F04 [MEDIUM/CONFIRMED #91], F06 [MEDIUM/CONFIRMED #91] (same handler, one change)

This is modal window-lifecycle plumbing (window-service close event → reference release on the UI thread). It cannot be exercised by the current harness (no `IWindowService` double drives `OnClosed`), so a manual verification step is given.

- [ ] In `ShowFrameReview`, extend the `onClosed` handler to clear the pane VM's own `reviewSnapshot` and `reviewFrames` (not just the child VM's copy) and re-evaluate availability, so closing the window actually frees the retained bitmaps and raw buffers regardless of whether another run is ever started. Before:
  ```csharp
            EventHandler onClosed = null;
            onClosed = (s, e) => {
                windowService.OnClosed -= onClosed;
                vm.RequestClose -= onRequestClose;
                vm.Dispose();
            };
  ```
  After:
  ```csharp
            EventHandler onClosed = null;
            onClosed = (s, e) => {
                windowService.OnClosed -= onClosed;
                vm.RequestClose -= onRequestClose;
                vm.Dispose();
                // vm.Dispose() only nulls the child VM's copy; the pane VM still holds the snapshot's
                // frozen bitmaps (F04) and any source IRenderedImage buffers (F06). Release them here so
                // "released when you ... close the review window" is honored without waiting for the next
                // run (which uses a different VM for sequence-driven AF anyway).
                lock (frameReviewLock) {
                    reviewFrames.Clear();
                }
                reviewSnapshot = null;
                NotifyReviewFramesAvailabilityChanged();
            };
  ```
- [ ] **Manual NINA verification:** Run a manual AF with "Keep frames for review" on; open Review Frames, then close it. Confirm working set drops back to baseline immediately (not only on the next run) and the Review Frames button becomes disabled. Without starting another run, image via a sequence and confirm memory stays at baseline (covers the F06 sequence-VM scenario). Not unit-tested because it depends on the real window-service `OnClosed` callback and frozen `BitmapSource` references the harness does not construct.
- [ ] `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(autofocus): free pane review snapshot+frames when Review window closes (F04, F06)"`

---

### Task 4: Skip the UI-thread dispatch on the non-review AF-start path

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` (lines 828–845, `AutoFocusEngine_AutoFocusStarted`)

**Finding:** F27 [LOW/PLAUSIBLE #91]

This is a dispatcher-marshaling optimization on the engine's background `Started` callback; it cannot be unit-tested with the current harness (the behavior is "a blocking `Send` is not issued," which the harness cannot observe without a dispatcher double), so a manual verification note is given.

- [ ] In `AutoFocusEngine_AutoFocusStarted`, only notify availability when there was actually something to clear (a requested run or an existing snapshot), so a sequence-triggered transient VM does not issue a blocking cross-thread `Send` on the common non-review path. Before:
  ```csharp
            // Drop any frames/snapshot from a prior run (a new run supersedes the previous review). frameReviewRequestedForRun
            // is NOT reset here — it is set per-entry-path (StartAutoFocus / LoadSavedAutoFocusRun) before Run() raises Started.
            lock (frameReviewLock) {
                reviewFrames.Clear();
            }
            reviewSnapshot = null;
            NotifyReviewFramesAvailabilityChanged();
  ```
  After:
  ```csharp
            // Drop any frames/snapshot from a prior run (a new run supersedes the previous review). frameReviewRequestedForRun
            // is NOT reset here — it is set per-entry-path (StartAutoFocus / LoadSavedAutoFocusRun) before Run() raises Started.
            var hadReviewState = reviewSnapshot != null || frameReviewRequestedForRun;
            lock (frameReviewLock) {
                reviewFrames.Clear();
            }
            reviewSnapshot = null;
            // Only marshal to the UI thread when availability could actually change. On the common non-review
            // path (sequence-triggered transient VM bound to no UI) the clear is a no-op, so skip the blocking
            // Send that would otherwise stall the AF worker if the dispatcher is busy. (F27)
            if (hadReviewState) {
                NotifyReviewFramesAvailabilityChanged();
            }
  ```
- [ ] **Manual NINA verification:** Trigger a sequence-driven AF (a transient VM, no review) and confirm AF completes normally with the Review Frames button disabled. Then run a pane AF with "Keep frames for review" on, confirm the button enables; start a second pane run and confirm it disables again at start (proving the notify still fires when review state existed). Not unit-tested because the optimization's only effect — the absence of a `SynchronizationContext.Send` — is not observable in the current harness.
- [ ] `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(autofocus): skip review-availability dispatch on non-review AF start (F27)"`

---

### Task 5: Document the happens-before contract for `reviewSnapshot`

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` (line 90, `reviewSnapshot` field declaration)

**Finding:** F21 [LOW/PLAUSIBLE #91]

This is a memory-model/threading-contract concern on a reference field whose reads are already atomic; the worst case is a stale-but-valid snapshot. The correct, minimal fix is to mark the field `volatile` and document the invariant — there is no unit-testable behavioral change (a data race cannot be deterministically reproduced in the harness), so no test is fabricated.

- [ ] Mark `reviewSnapshot` `volatile` and document that writes (engine background callback) are made visible to UI-thread reads via the field's volatility plus the post-build `Send` barrier. Before:
  ```csharp
        private bool frameReviewRequestedForRun;
        private AutoFocusFrameReviewSnapshot reviewSnapshot;
  ```
  After:
  ```csharp
        private bool frameReviewRequestedForRun;
        // Written on the engine's background completion thread (BuildFrameReviewSnapshotIfRequested) and on the
        // close handler, read on the UI thread (ReviewFramesAvailable / ShowFrameReview). Reference reads are
        // atomic; volatile (plus the blocking Send in NotifyReviewFramesAvailabilityChanged) establishes the
        // happens-before so UI reads see the built snapshot rather than a stale reference. (F21)
        private volatile AutoFocusFrameReviewSnapshot reviewSnapshot;
  ```
- [ ] Confirm the type name is correct (`AutoFocusFrameReviewSnapshot`) and that the field still compiles by building: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (expected: build succeeds).
- [ ] `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "docs(autofocus): make reviewSnapshot volatile + document happens-before (F21)"`

---

### Task 6: Free the Inspector review snapshot when its Review window closes; align tooltips

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` (lines 1593–1598, `ShowFrameReview` onClosed handler; uses existing `ClearReviewSnapshot` at line 1572)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` (line 645 AF "Keep frames" tooltip; line 1384 Inspector "Keep frames" tooltip)

**Finding:** F36 [LOW/CONFIRMED #88]

This is modal window-lifecycle plumbing plus tooltip wording, neither of which is unit-testable in the current harness (no window-service double drives `OnClosed`; tooltips are static XAML strings) — a manual verification step is given.

- [ ] In `InspectorVM.ShowFrameReview`, extend the `onClosed` handler to clear the Inspector's own `reviewSnapshot` (the child `FrameReviewVM.Dispose()` only nulls its copy), reusing the existing `ClearReviewSnapshot()` helper which also re-notifies availability. Before:
  ```csharp
            EventHandler onClosed = null;
            onClosed = (s, e) => {
                windowService.OnClosed -= onClosed;
                vm.RequestClose -= onRequestClose;
                vm.Dispose();
            };
  ```
  After:
  ```csharp
            EventHandler onClosed = null;
            onClosed = (s, e) => {
                windowService.OnClosed -= onClosed;
                vm.RequestClose -= onRequestClose;
                vm.Dispose();
                // vm.Dispose() only nulls the child VM's copy; InspectorVM still holds the snapshot's frozen
                // bitmaps. Release them on close so the documented "released when you ... close the review
                // window" contract holds without waiting for Clear Analyses / the next run. (F36)
                ClearReviewSnapshot();
            };
  ```
- [ ] Update the AF pane "Keep frames" tooltip (line 645) so its release wording matches the now-true behavior (close releases). It already says "released when you start a new run or close the review window" — keep that, but confirm the verb matches Task 3. No change needed if the string already reads "released when you start a new run or close the review window." (Verify by reading line 645; the current text is exactly that, so leave it unchanged.)
- [ ] Update the Inspector "Keep frames for Review" tooltip (line 1384) to add the close-window release, matching the new onClosed behavior. Before:
  ```
                        ToolTip="When on, the per-frame images from a Sensor Curve Model run are retained in memory so the &quot;Review Frames&quot; button can show each frame with its detected stars, HFRs, and cross-frame registration. Requires the Sensor Curve Model to be enabled. Increases memory use while the run's results are kept (the frames are released when you clear the analysis or start a new run). Off by default.">
  ```
  After:
  ```
                        ToolTip="When on, the per-frame images from a Sensor Curve Model run are retained in memory so the &quot;Review Frames&quot; button can show each frame with its detected stars, HFRs, and cross-frame registration. Requires the Sensor Curve Model to be enabled. Increases memory use while the run's results are kept (the frames are released when you clear the analysis, start a new run, or close the review window). Off by default.">
  ```
- [ ] **Manual NINA verification:** Enable "Keep frames for Review", run a Detailed Analysis (Sensor Curve Model) on a many-frame high-resolution camera, open then close the Review Frames window. Confirm working set returns to baseline on close (not only on Clear Analyses / next run) and the Review Frames button disables. Re-running keeps working. Not unit-tested because it depends on the real window-service `OnClosed` callback and frozen `BitmapSource`s the harness does not construct.
- [ ] `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(inspector): free review snapshot on Review window close + align tooltips (F36)"`

---

**Phase verification:** run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` and expect "1436+ pass, 0 fail" (this phase adds no new unit tests — the findings are memory/lifecycle/dispatcher concerns verified manually in NINA — so the count stays at the current passing total; the suite must remain green after the edits).

---

## Phase 3: AutoFocus Review annotator fidelity & UI consistency

### Task 1: ~~Honor the annotator master `ShowAnnotations` flag~~ — CANCELLED (F02, by-design)

**Finding:** F02 [MEDIUM/CONFIRMED #91] — **Rejected as not-a-bug (2026-06-22, by maintainer).**

The review correctly noted that the AF Review window draws overlays even when the Star Annotator's master **Show Annotations** toggle is off, diverging from the live annotated image. That divergence is **intentional**: the entire purpose of Review Frames is to inspect the detected-star annotations, so the window must show them regardless of the global toggle that suppresses overlays on the *live* image. Honoring `ShowAnnotations` here would make the feature blank out exactly when the user opened it to look at annotations. **No code change.** (The per-feature sub-flags — star bounds, text, center, rejection reasons — are still honored, so the user retains fine-grained control inside the review.)

_Note: F03 (Tasks 2 & 3 below) was cancelled for the same reason — the review must show all detected stars, not the live MaxStars subset._

### Task 2 & 3: ~~Carry `AverageBrightness` + apply the `ShowAllStars`/`MaxStars` cap~~ — CANCELLED (F03, by-design)

**Finding:** F03 [MEDIUM/CONFIRMED #91] — **Rejected as not-a-bug (2026-06-22, by maintainer).**

The review noted that the AF Review window draws markers for *every* detected star, ignoring the live annotator's `ShowAllStars=false` + `MaxStars=N` brightness-ranked cap. By the same reasoning that cancels F02: the Review Frames window exists **to inspect the detected stars**, so showing *all* of them is the desired behavior — capping to the brightest N (purely a live-view declutter setting) would hide stars the user opened the window to examine. **No code change:** the snapshot does **not** gain an `AverageBrightness` field and the render loop keeps drawing all stars. (The original Tasks 2 and 3 — the snapshot half and the VM half of F03 — are both dropped.)

Consequence for the rest of Phase 3: `AutoFocusFrameReviewSnapshot.cs` no longer needs any change in this phase, and the only remaining edit to `AutoFocusFrameReviewVM.cs` here is Task 4 (F30/F26/F18).

### Task 4: Draw the ROI whenever the region is not full + filter and marshal the options handler (F30, F26, F18)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewVM.cs` (F30: ROI gate ~line 390 + legend ~line 298; F26+F18: `AnnotatorOptions_PropertyChanged` ~line 151-155)

**Finding:** F30 [LOW/CONFIRMED #91] — review gates ROI on `annotatorOptions.ShowROI`, but the annotator draws the ROI unconditionally whenever the region is not full and never reads `ShowROI` (`HocusFocusStarAnnotator.cs:239-247`). **F26 [LOW/CONFIRMED #91]** + **F18 [LOW/PLAUSIBLE #91]** edit the SAME handler (`AnnotatorOptions_PropertyChanged`): F26 rebuilds on every unrelated property change; F18 mutates the `Markers`/`RectOverlays` collections without marshaling to the UI thread.

F26's `e.PropertyName` filter is unit-testable in principle but the handler is `private` and drives `ObservableCollection` mutation with no VM test harness; all three are verified manually. F18 cannot be unit-tested (UI-thread/dispatcher marshaling has no harness — tests would run on the test thread where the bug is invisible). Combined because F26+F18 are one rewrite of the same method, and F30 is a small sibling fidelity fix in the same file/method group.

⚠ shared-file note: `AutoFocus/Review/AutoFocusFrameReviewVM.cs` — final edit in this phase; sequence after Tasks 1–3.

1. **F30 — overlay:** the ROI rects are already empty for a full-frame region (the builder returns `Array.Empty<Rect>()` when `region.IsFull()`, `AutoFocusFrameReviewSnapshot.cs:246`), so honoring `ShowROI` is the only divergence. Change in `RebuildCurrentFrameOverlays`:
   ```csharp
       if (annotatorOptions.ShowROI) {
           var roiBrush = FrozenBrush(annotatorOptions.ROIColor);
           foreach (var rect in frame.RoiRects) {
               RectOverlays.Add(ToOverlay(rect, roiBrush));
           }
       }
   ```
   to:
   ```csharp
       // The annotator draws the ROI whenever the detection region is not full and never reads ShowROI
       // (HocusFocusStarAnnotator.cs:239-247). frame.RoiRects is already empty for a full-frame region,
       // so drawing it unconditionally matches the annotated image.
       var roiBrush = FrozenBrush(annotatorOptions.ROIColor);
       foreach (var rect in frame.RoiRects) {
           RectOverlays.Add(ToOverlay(rect, roiBrush));
       }
   ```

2. **F30 — legend:** the legend's "Detection ROI" row should reflect whether the ROI actually draws (region not full) rather than `ShowROI`. The current frame is the source of truth. Change in `BuildLegend` (note: this line was already edited in Task 1 to add the `master &&` prefix):
   ```csharp
       entries.Add(new StarReviewLegendEntry { Brush = FrozenBrush(annotatorOptions.ROIColor), Caption = "Detection ROI", Enabled = master && annotatorOptions.ShowROI });
   ```
   to:
   ```csharp
       // ROI is drawn whenever the region is not full (annotator ignores ShowROI), i.e. whenever the
       // current frame actually has ROI rects.
       var roiActive = CurrentFrame?.RoiRects.Count > 0;
       entries.Add(new StarReviewLegendEntry { Brush = FrozenBrush(annotatorOptions.ROIColor), Caption = "Detection ROI", Enabled = master && roiActive });
   ```

3. **F26 + F18 — filter + marshal the handler.** This VM is not currently given a dispatcher; inject one. First add the field, the constructor parameter, and the using. Change the field block:
   ```csharp
       private AutoFocusFrameReviewSnapshot snapshot;
       private readonly IStarAnnotatorOptions annotatorOptions;
       private readonly MeasurementAverageEnum measurementAverage;
       private readonly bool psfAvailable;
   ```
   to:
   ```csharp
       private AutoFocusFrameReviewSnapshot snapshot;
       private readonly IStarAnnotatorOptions annotatorOptions;
       private readonly IApplicationDispatcher applicationDispatcher;
       private readonly MeasurementAverageEnum measurementAverage;
       private readonly bool psfAvailable;
   ```

4. Update the constructor signature and assignment. Change:
   ```csharp
       public AutoFocusFrameReviewVM(AutoFocusFrameReviewSnapshot snapshot, IStarAnnotatorOptions annotatorOptions, MeasurementAverageEnum measurementAverage) {
           this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
           this.annotatorOptions = annotatorOptions ?? throw new ArgumentNullException(nameof(annotatorOptions));
           this.measurementAverage = measurementAverage;
   ```
   to:
   ```csharp
       public AutoFocusFrameReviewVM(AutoFocusFrameReviewSnapshot snapshot, IStarAnnotatorOptions annotatorOptions, IApplicationDispatcher applicationDispatcher, MeasurementAverageEnum measurementAverage) {
           this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
           this.annotatorOptions = annotatorOptions ?? throw new ArgumentNullException(nameof(annotatorOptions));
           this.applicationDispatcher = applicationDispatcher ?? throw new ArgumentNullException(nameof(applicationDispatcher));
           this.measurementAverage = measurementAverage;
   ```

5. Add the `IApplicationDispatcher` import. The file's `using` block (top of file) lists `using NINA.Joko.Plugins.HocusFocus.Interfaces;` already, and `IApplicationDispatcher` lives in that namespace (`Interfaces/IApplicationDispatcher.cs`), so no new using is required.

6. Rewrite the handler to filter on `e.PropertyName` (F26) and marshal the body to the UI thread (F18). Change:
   ```csharp
       // Changing any annotator color/show-flag/bounds-type re-renders the overlays + legend live (same UX as the
       // image annotator, which also re-annotates on option change).
       private void AnnotatorOptions_PropertyChanged(object sender, PropertyChangedEventArgs e) {
           RaiseAnnotatorVisualsChanged();
           RebuildCurrentFrameOverlays();
           RebuildLegend();
       }
   ```
   to:
   ```csharp
       // Overlay-affecting annotator properties: a change to any of these re-renders the overlays + legend live
       // (same UX as the image annotator). Properties the review does not render (e.g. ShowStructureMap,
       // AnnotationFontFamily, DetectorOptions) are ignored to avoid wasted O(stars) rebuilds.
       private static readonly HashSet<string> OverlayAffectingProperties = new() {
           nameof(IStarAnnotatorOptions.ShowAnnotations),
           nameof(IStarAnnotatorOptions.ShowStarBounds),
           nameof(IStarAnnotatorOptions.StarBoundsType),
           nameof(IStarAnnotatorOptions.StarBoundsColor),
           nameof(IStarAnnotatorOptions.ShowStarCenter),
           nameof(IStarAnnotatorOptions.StarCenterColor),
           nameof(IStarAnnotatorOptions.ShowAnnotationType),
           nameof(IStarAnnotatorOptions.AnnotationColor),
           // NOTE: ShowAllStars/MaxStars are deliberately omitted — F03 was cancelled, so the review shows ALL
           // detected stars (not the live MaxStars subset); those properties do not affect this overlay and
           // must not trigger a (wasted) rebuild.
           nameof(IStarAnnotatorOptions.ShowROI),
           nameof(IStarAnnotatorOptions.ROIColor),
           nameof(IStarAnnotatorOptions.ShowTooDistorted), nameof(IStarAnnotatorOptions.TooDistortedColor),
           nameof(IStarAnnotatorOptions.ShowDegenerate), nameof(IStarAnnotatorOptions.DegenerateColor),
           nameof(IStarAnnotatorOptions.ShowSaturated), nameof(IStarAnnotatorOptions.SaturatedColor),
           nameof(IStarAnnotatorOptions.ShowLowSensitivity), nameof(IStarAnnotatorOptions.LowSensitivityColor),
           nameof(IStarAnnotatorOptions.ShowNotCentered), nameof(IStarAnnotatorOptions.NotCenteredColor),
           nameof(IStarAnnotatorOptions.ShowTooFlat), nameof(IStarAnnotatorOptions.TooFlatColor),
           nameof(IStarAnnotatorOptions.ShowContaminated), nameof(IStarAnnotatorOptions.ContaminatedColor),
       };

       // Changing an overlay-affecting annotator property re-renders the overlays + legend live. Treat a null/empty
       // PropertyName as "rebuild all". The body is marshaled to the UI thread because the shared annotator options
       // object could raise PropertyChanged off the UI thread, and the rebuild mutates the bound ObservableCollections.
       private void AnnotatorOptions_PropertyChanged(object sender, PropertyChangedEventArgs e) {
           if (!string.IsNullOrEmpty(e.PropertyName) && !OverlayAffectingProperties.Contains(e.PropertyName)) {
               return;
           }
           applicationDispatcher.DispatchSynchronizationContext(() => {
               RaiseAnnotatorVisualsChanged();
               RebuildCurrentFrameOverlays();
               RebuildLegend();
           });
       }
   ```
   (Before finalizing, the executor MUST confirm each `nameof(...)` member exists on `IStarAnnotatorOptions`; the seven rejection `Show*`/`*Color` pairs are already referenced in `Reasons()` and `ShowAnnotations`/`StarBoundsType`/`ROIColor` are all read elsewhere in this VM, so all are valid. Drop any member that does not compile rather than inventing one.)

7. Update the single caller to pass the dispatcher. In `HocusFocusVM.cs:805` change:
   ```csharp
           var vm = new AutoFocusFrameReviewVM(snapshot, HocusFocusPlugin.StarAnnotatorOptions, starDetectionOptions.MeasurementAverage);
   ```
   to:
   ```csharp
           var vm = new AutoFocusFrameReviewVM(snapshot, HocusFocusPlugin.StarAnnotatorOptions, HocusFocusPlugin.ApplicationDispatcher, starDetectionOptions.MeasurementAverage);
   ```
   ⚠ shared-file note: `AutoFocus/HocusFocusVM.cs` is edited by other phases (F04/F05/F22/F25/F27 memory/lifecycle tasks) — the assembler must sequence this one-line constructor-arg change with those.

8. Build: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.

9. **Manual NINA verification:** (a) F30 — run a manual AF with a cropped detection region and **Show ROI = off** in Star Annotator; open Review Frames and confirm the ROI rectangle now draws (matching the live annotated frame) and the "Detection ROI" legend row is enabled; run a full-frame detection and confirm no ROI box and the legend row greyed. (b) F26 — open Review Frames, change an unrelated annotator property (e.g. `AnnotationFontFamily`) and confirm the overlay does not flicker/rebuild, then change `StarBoundsColor` and confirm it does. F18 is structural (off-thread safety) and isn't separately observable in normal use.

10. Commit:
    ```
    GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(af-review): draw ROI per annotator, filter+marshal options handler (F30,F26,F18)"
    ```

### Task 5: Render the "Star bounds" combo through the enum-description converter (F32)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml` (line 363; optionally `Interfaces/IStarAnnotatorOptions.cs` lines 19-23)

**Finding:** F32 [LOW/CONFIRMED #91] — the "Star bounds" combo (line 363) renders items with bare `Text="{Binding}"` (raw enum `ToString`), while the sibling "Per-star text" combo (line 351) uses `HF_EnumStaticDescriptionValueConverter`; an inconsistency in label presentation.

XAML-only presentation change; verified manually (no test harness for XAML rendering).

1. Add `[Description]` attributes to `StarBoundsTypeEnum` so the converter has friendly labels. In `Interfaces/IStarAnnotatorOptions.cs`, change:
   ```csharp
       public enum StarBoundsTypeEnum {
           Ellipse,
           Box,
           PSF
       }
   ```
   to:
   ```csharp
       [TypeConverter(typeof(EnumStaticDescriptionConverter))]
       public enum StarBoundsTypeEnum {

           [Description("Ellipse")]
           Ellipse,

           [Description("Box")]
           Box,

           [Description("PSF")]
           PSF
       }
   ```
   (`System.ComponentModel` and `NINA.Joko.Plugins.HocusFocus.Converters` are already imported at the top of this file, as used by `ShowAnnotationTypeEnum`.)

2. Switch the combo to the shared converter. In `AutoFocusFrameReviewControl.xaml`, change:
   ```xml
                   <ComboBox.ItemTemplate>
                       <DataTemplate>
                           <TextBlock Foreground="Black" Text="{Binding}" />
                       </DataTemplate>
                   </ComboBox.ItemTemplate>
   ```
   (the one inside the **Star bounds** ComboBox, immediately after `SelectedItem="{Binding StarBoundsType}"`) to:
   ```xml
                   <ComboBox.ItemTemplate>
                       <DataTemplate>
                           <TextBlock Foreground="Black" Text="{Binding Converter={StaticResource HF_EnumStaticDescriptionValueConverter}}" />
                       </DataTemplate>
                   </ComboBox.ItemTemplate>
   ```
   (This `DataTemplate` is uniquely identified by its `Text="{Binding}"` content; the Per-star-text combo already uses the converter, so the match is unambiguous.)

3. Build: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.

4. **Manual NINA verification:** Open the AF Review Frames window and confirm the "Star bounds" dropdown lists items rendered through the description converter (consistent styling with the adjacent "Per-star text" dropdown), with no `MissingResource`/binding errors in the output log.

5. Commit:
   ```
   GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(af-review): render Star bounds combo via enum description converter (F32)"
   ```

### Task 6: Remove the redundant `IsEnabled` bindings on the Review Frames buttons (F31)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` (line 637 AutoFocus pane button; line 1370 Inspector pane button)

**Finding:** F31 [LOW/CONFIRMED #88] — both buttons bind `IsEnabled="{Binding ReviewFramesAvailable}"` AND a `Command` whose `CanExecute` is `() => ReviewFramesAvailable` (confirmed `HocusFocusVM.cs:147`, `InspectorVM.cs:190`); WPF already disables the button when `CanExecute` is false, so the explicit `IsEnabled` is redundant. Relying on the command keeps a single source of enabled-state.

XAML-only; verified manually (no XAML rendering test harness).

1. Remove the redundant `IsEnabled` on the AutoFocus pane button. Change (lines 636-638):
   ```xml
                   Command="{Binding ReviewFramesCommand}"
                   IsEnabled="{Binding ReviewFramesAvailable}"
                   ToolTip="Open the Review Frames window to inspect each frame of the last auto-focus run started from this pane (a live run or a Reprocess Saved Run) — its detected stars with the Hocus Focus Star Annotator's bounds, per-star text, rejection-reason and ROI boxes, plus per-frame HFR statistics. Enabled after such a run completes with &quot;Keep frames for review&quot; turned on.">
   ```
   to:
   ```xml
                   Command="{Binding ReviewFramesCommand}"
                   ToolTip="Open the Review Frames window to inspect each frame of the last auto-focus run started from this pane (a live run or a Reprocess Saved Run) — its detected stars with the Hocus Focus Star Annotator's bounds, per-star text, rejection-reason and ROI boxes, plus per-frame HFR statistics. Enabled after such a run completes with &quot;Keep frames for review&quot; turned on.">
   ```

2. Remove the redundant `IsEnabled` on the Inspector pane button. Change (lines 1369-1371):
   ```xml
                       Command="{Binding ReviewFramesCommand}"
                       IsEnabled="{Binding ReviewFramesAvailable}"
                       ToolTip="Open the Review Frames window to inspect each Sensor Curve Model frame — its detected stars, HFRs, and cross-frame registration (with alignment arrows when RANSAC alignment is on). Enabled after a run completes with &quot;Keep frames for Review&quot; turned on.">
   ```
   to:
   ```xml
                       Command="{Binding ReviewFramesCommand}"
                       ToolTip="Open the Review Frames window to inspect each Sensor Curve Model frame — its detected stars, HFRs, and cross-frame registration (with alignment arrows when RANSAC alignment is on). Enabled after a run completes with &quot;Keep frames for Review&quot; turned on.">
   ```

3. Build: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.

4. **Manual NINA verification:** Confirm both Review Frames buttons are disabled before a qualifying run and become enabled after a run completes with "Keep frames for review" on (and that `ReviewFramesCommand.NotifyCanExecuteChanged()` at `HocusFocusVM.cs:792` / `InspectorVM.cs:1568` still drives the button state). Why no unit test: button enabled-state is WPF command/visual-tree behavior with no harness here.

5. Commit:
   ```
   GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "refactor(ui): drop redundant IsEnabled on Review Frames buttons (F31)"
   ```

### Task 7: Correct the Inspector hover help text / comment (F29)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml` (line 378 help text)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml.cs` (line 205 `UpdateHover` comment)

**Finding:** F29 [LOW/CONFIRMED #88] — help text says only green (fitted) stars are hoverable, but `UpdateHover` enables a focus graph for any matched star (`CanShowFocusGraph` is true for any star with a curve; red MatchedNoFit stars show "No accepted focus fit"). Pure textual/UX mismatch. Documentation-only change; verified by reading the rendered tooltip.

1. Fix the on-screen help text. Change (line 377-378):
   ```xml
           <TextBlock Margin="0,8,0,0" Foreground="#FFB0B6BD" TextWrapping="Wrap"
                      Text="Read-only diagnostic view. Box color shows each star's registration + focus-fit state. Hover a green (fitted) star to see its focus curve, R², and best focus." />
   ```
   to:
   ```xml
           <TextBlock Margin="0,8,0,0" Foreground="#FFB0B6BD" TextWrapping="Wrap"
                      Text="Read-only diagnostic view. Box color shows each star's registration + focus-fit state. Hover any matched star to see its focus curve, R², and best focus; a no-fit (red) star shows &quot;No accepted focus fit&quot;." />
   ```

2. Fix the `UpdateHover` comment. Change (line 205):
   ```csharp
       // Show the focus graph for the registered+fitted star under the cursor (smallest box wins); clear it otherwise.
   ```
   to:
   ```csharp
       // Show the focus graph for any matched star under the cursor (smallest box wins); a no-fit star shows
       // "No accepted focus fit". Clear it when no matched star is under the cursor.
   ```

3. Build: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.

4. **Manual NINA verification:** Open the Inspector's Review Frames diagnostic view; hover a red (MatchedNoFit) star and confirm the "No accepted focus fit" popup appears, matching the now-corrected help text. Why no unit test: this is text content rendered in WPF with no rendering/test harness.

5. Commit:
   ```
   GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "docs(inspector-review): hover help/comment reflect matched (not just fitted) stars (F29)"
   ```

**Phase verification:** run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` and expect "1436+ pass, 0 fail" (Task 2 adds one new test, raising the count by one).

---

## Phase 4: Thread-safety & dispatcher hardening

> **⚠ Shared-file note:** `AutoFocus/AutoFocusEngine.cs` is also edited by Phase 1 (F01 — the `progress?.Report` null-conditional fix at ~1208/1213). The edit here (Task 1, the `AutoFocusInProgress` guard at ~1493/1503/1516/1553) touches a different region of the same file; sequence Phase 1 and Phase 4 so both land without textual conflict (they do not overlap, but the assembler should rebase one onto the other rather than apply both diffs blind).
> **⚠ Shared-file note:** `Utility/ApplicationDispatcher.cs` is consumed by many phases (every `OnUIThread`/`DispatchSynchronizationContext` caller). Task 2 rewrites its `DispatchSynchronizationContext` bodies. No other phase in this plan edits this file's source, but its behavioral change (real inline fast-path + null/shutdown guards) affects every caller, so land Task 2 before any phase that adds new dispatcher call sites.

### Task 1: Make the static `AutoFocusInProgress` claim atomic (F11)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs` (field decl ~1493–1500; check ~1503; set ~1516; release ~1553)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/AutoFocusEngineTests.cs` (new test method)

**Finding:** F11 [LOW/PLAUSIBLE #89] — read (check `==false`) and write (`=true`) of the static guard are non-atomic, so two `RunImpl` calls can both pass the gate and drive the focuser concurrently.

Steps:

1. Write the failing test (it asserts the second concurrent claimant is rejected). Add to `AutoFocusEngineTests` (namespace `NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus`). This test directly exercises the new atomic claim helper, which is the unit-testable core of the race fix (a true two-thread race is non-deterministic; the helper's compare-and-set semantics are the testable invariant):

```csharp
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
```

2. Run it, expect FAIL (the helper methods do not exist yet — compile error counts as fail):
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter TryClaimAutoFocusInProgress_SecondClaimantIsRejectedUntilReleased`

3. Replace the non-atomic field + property with an `Interlocked`-backed int flag plus atomic claim/release helpers. Change:

```csharp
        private static bool autoFocusInProgress = false;

        public bool AutoFocusInProgress {
            get => autoFocusInProgress;
            private set {
                autoFocusInProgress = value;
            }
        }
```

to:

```csharp
        // 0 = free, 1 = an AutoFocus is in progress. Static so it is shared across all engine instances and entry
        // points (manual AF, Inspector analyze, optimizer live attempt). Mutated only via Interlocked so the
        // check-and-set is atomic and two RunImpl entrants cannot both claim it (F11).
        private static int autoFocusInProgress = 0;

        public bool AutoFocusInProgress => Volatile.Read(ref autoFocusInProgress) != 0;

        // Returns true iff this caller transitioned the guard from free->in-progress (i.e. it now owns the run).
        internal static bool TryClaimAutoFocusInProgress() => Interlocked.CompareExchange(ref autoFocusInProgress, 1, 0) == 0;

        // Releases the guard unconditionally (idempotent).
        internal static void ReleaseAutoFocusInProgress() => Interlocked.Exchange(ref autoFocusInProgress, 0);

        // Test hook: force the process-wide guard back to free so a leaked flag cannot pollute other tests.
        internal static void ResetAutoFocusInProgressForTests() => Interlocked.Exchange(ref autoFocusInProgress, 0);
```

4. Confirm `System.Threading` is imported (for `Interlocked`/`Volatile`). Verify with the file's using block:
   `grep -n "using System.Threading;" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs` — if absent, add `using System.Threading;` to the using directives at the top of the file.

5. Replace the non-atomic check at the top of `RunImpl`. Change:

```csharp
            if (AutoFocusInProgress) {
                Notification.ShowError("Another AutoFocus is already in progress");
                Logger.Error("Another AutoFocus is already in progress");
                return null;
            }
```

to:

```csharp
            if (!TryClaimAutoFocusInProgress()) {
                Notification.ShowError("Another AutoFocus is already in progress");
                Logger.Error("Another AutoFocus is already in progress");
                return null;
            }
```

6. Remove the now-redundant separate `set true` line so the claim is not double-applied. Change:

```csharp
            bool completed = false;
            AutoFocusInProgress = true;
            AutoFocusState autoFocusState = null;
```

to:

```csharp
            bool completed = false;
            AutoFocusState autoFocusState = null;
```

7. Replace the release in the inner `finally` with the atomic release helper. Change:

```csharp
                    AutoFocusInProgress = false;
                    progress?.Report(new ApplicationStatus() { Status = string.Empty });
```

to:

```csharp
                    ReleaseAutoFocusInProgress();
                    progress?.Report(new ApplicationStatus() { Status = string.Empty });
```

8. Run the test again, expect PASS:
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter TryClaimAutoFocusInProgress_SecondClaimantIsRejectedUntilReleased`

9. Commit:
   `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(autofocus): atomically claim the static AutoFocusInProgress guard (F11)"`

### Task 2: Harden `ApplicationDispatcher` — real UI-thread fast path, null-context fallback, shutdown guard (F12, F13, F14)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/ApplicationDispatcher.cs` (field ~23–25; both `DispatchSynchronizationContext` overloads ~27–43)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/ApplicationDispatcherTests.cs` (new file)

**Finding:** F12 [LOW/CONFIRMED #89] (fast path reference-compares a self-built `DispatcherSynchronizationContext` that never equals the WPF-installed one, so every UI-thread call takes the heavier `Send` path), F13 [LOW/PLAUSIBLE #89] (`synchronizationContext` is null when `Application.Current?.Dispatcher` was null at construction → `Send` on null throws NRE on a device-broadcast thread), F14 [LOW/PLAUSIBLE #89] (`Send` can throw `InvalidOperationException` during dispatcher shutdown). All three edit the same two methods, so they are one task.

Steps:

1. Write the failing test. In the test process there is no WPF `Application`, so `Application.Current` is null and the dispatcher's context is null — this directly exercises the F13 inline-fallback path. Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/ApplicationDispatcherTests.cs`:

```csharp
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class ApplicationDispatcherTests {

        // F13: when no WPF Application/Dispatcher is available (headless / early startup / test host), the cached
        // SynchronizationContext is null. DispatchSynchronizationContext must fall back to inline invocation rather
        // than dereferencing null.Send(...) and throwing an NRE on the calling (e.g. device-broadcast) thread.
        [Test]
        public void DispatchSynchronizationContext_Action_RunsInlineWhenNoDispatcherAvailable() {
            var sut = new ApplicationDispatcher();
            var ran = false;

            Assert.DoesNotThrow(() => sut.DispatchSynchronizationContext(() => ran = true));
            Assert.That(ran, Is.True);
        }

        [Test]
        public void DispatchSynchronizationContext_Func_RunsInlineWhenNoDispatcherAvailable() {
            var sut = new ApplicationDispatcher();

            int result = 0;
            Assert.DoesNotThrow(() => result = sut.DispatchSynchronizationContext(() => 42));
            Assert.That(result, Is.EqualTo(42));
        }
    }
}
```

2. Run it, expect FAIL (current code calls `synchronizationContext.Send` on null → NRE):
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter ApplicationDispatcherTests`

3. Capture the WPF `Dispatcher` instead of a self-built `SynchronizationContext`, so the fast path can use `CheckAccess()` (F12) and the null/shutdown guards have a real handle (F13/F14). Change the field:

```csharp
        private readonly SynchronizationContext synchronizationContext = Application.Current?.Dispatcher != null
            ? new DispatcherSynchronizationContext(Application.Current.Dispatcher)
            : null;
```

to:

```csharp
        // Capture the WPF dispatcher directly. CheckAccess() lets us detect the UI thread reliably (a self-built
        // DispatcherSynchronizationContext never reference-equals the one WPF installs, so the old fast path never
        // fired — F12). The handle is null in headless/early-startup/test hosts, in which case we invoke inline (F13).
        private readonly Dispatcher dispatcher = Application.Current?.Dispatcher;
```

4. Rewrite the void overload with the CheckAccess fast path (F12), null fallback (F13), and shutdown guard (F14). Change:

```csharp
        public void DispatchSynchronizationContext(Action action) {
            if (SynchronizationContext.Current == synchronizationContext) {
                action();
            } else {
                synchronizationContext.Send(_ => action(), null);
            }
        }
```

to:

```csharp
        public void DispatchSynchronizationContext(Action action) {
            // No dispatcher (headless/test host) or already on the UI thread: run inline (F12 fast path, F13 fallback).
            if (dispatcher == null || dispatcher.CheckAccess()) {
                action();
                return;
            }

            // The dispatcher is shutting down / has shut down: it will no longer pump, so Send would throw
            // InvalidOperationException on the calling thread. Drop the UI-bound work rather than crash (F14).
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) {
                return;
            }

            try {
                dispatcher.Invoke(action);
            } catch (OperationCanceledException) {
                // Dispatcher shut down between the check above and the Invoke; nothing to do.
            } catch (InvalidOperationException) {
                // Same race: "The Dispatcher has been shut down" surfaced as InvalidOperationException.
            }
        }
```

5. Rewrite the generic overload symmetrically. Change:

```csharp
        public T DispatchSynchronizationContext<T>(Func<T> func) {
            if (SynchronizationContext.Current == synchronizationContext) {
                return func();
            } else {
                var result = default(T);
                synchronizationContext.Send(_ => result = func(), null);
                return result;
            }
        }
```

to:

```csharp
        public T DispatchSynchronizationContext<T>(Func<T> func) {
            if (dispatcher == null || dispatcher.CheckAccess()) {
                return func();
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) {
                return default;
            }

            try {
                return dispatcher.Invoke(func);
            } catch (OperationCanceledException) {
                return default;
            } catch (InvalidOperationException) {
                return default;
            }
        }
```

6. Remove the now-unused `using System.Threading;` only if nothing else in the file uses it; `System.Windows.Threading` (for `Dispatcher`) is already imported. Verify:
   `grep -n "SynchronizationContext\|using System.Threading;" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/ApplicationDispatcher.cs` — if `SynchronizationContext` no longer appears, delete the `using System.Threading;` line.

7. Run the new test, expect PASS:
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter ApplicationDispatcherTests`

8. **Manual NINA verification (F12/F14 live behavior):** the CheckAccess fast path and the shutdown guard cannot be exercised by the test harness because it has no live WPF `Dispatcher` (only the F13 null path is reachable in-process). In NINA: open the Tilt Adapter Wizard with a camera/focuser connected, confirm device-info updates (e.g. temperature ticks) keep the command buttons' enabled state updating with no UI stutter (F12 inline path), then close NINA while a device is still broadcasting and confirm there is no "The Dispatcher has been shut down" crash in the log (F14).

9. Commit:
   `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(dispatcher): real UI-thread fast path + null/shutdown guards in ApplicationDispatcher (F12,F13,F14)"`

### Task 3: Marshal device-info notifications once at the `UpdateDeviceInfo` boundary (F15)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs` (`CameraInfo`/`FocuserInfo` setters ~283–304; `UpdateDeviceInfo` overloads ~306–312)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs` (new test method, using the existing `Build(dispatcher:)` overload + `RecordingApplicationDispatcher`)

**Finding:** F15 [LOW/PLAUSIBLE #89] — each device-info setter individually routes `NotifyCommandsCanExecuteChanged` through `OnUIThread`, so every new property added to those setters must remember to wrap, and frequent background broadcasts hit the dispatcher per-site. Marshal once at the `UpdateDeviceInfo` boundary instead.

Steps:

1. Write the failing test. It uses `RecordingApplicationDispatcher` to assert that handling one `UpdateDeviceInfo(CameraInfo)` call marshals exactly once (the whole-body wrap), not once per internal notify call. Add to `TiltAdapterWizardVMTests` (namespace `NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard`):

```csharp
        [Test]
        public void UpdateDeviceInfo_Camera_MarshalsExactlyOnceAtBoundary() {
            // F15: device-info broadcasts arrive on a background thread; the whole UpdateDeviceInfo body (state
            // mutation + all command/property notifications) must be marshaled to the UI thread as a single unit,
            // not via a separate dispatch per notify site.
            var dispatcher = new RecordingApplicationDispatcher();
            var (vm, _, _, _) = Build(dispatcher: dispatcher);
            var before = dispatcher.DispatchCount;

            vm.UpdateDeviceInfo(new NINA.Equipment.Equipment.MyCamera.CameraInfo { Connected = true });

            Assert.That(dispatcher.DispatchCount - before, Is.EqualTo(1),
                "UpdateDeviceInfo(CameraInfo) should marshal exactly once at the consumer boundary");
        }
```

2. Run it, expect FAIL (the current `CameraInfo` setter calls `NotifyCommandsCanExecuteChanged` which itself dispatches, plus `UpdateDeviceInfo` does not wrap — so dispatch count is not exactly 1):
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter UpdateDeviceInfo_Camera_MarshalsExactlyOnceAtBoundary`

3. Make the setters do plain (non-marshaled) mutation + notification, since they will now always run on the UI thread (marshaled by `UpdateDeviceInfo`). Change the `CameraInfo` setter:

```csharp
        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                cameraInfo = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AreDevicesConnected));
                RaisePropertyChanged(nameof(ConnectionWarningText));
                RaisePropertyChanged(nameof(PixelSizeMicronsValue));
                NotifyCommandsCanExecuteChanged();
            }
        }
```

to:

```csharp
        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                cameraInfo = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AreDevicesConnected));
                RaisePropertyChanged(nameof(ConnectionWarningText));
                RaisePropertyChanged(nameof(PixelSizeMicronsValue));
                NotifyCommandsCanExecuteChangedCore();
            }
        }
```

4. Change the `FocuserInfo` setter similarly:

```csharp
        public FocuserInfo FocuserInfo {
            get => focuserInfo;
            private set {
                focuserInfo = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AreDevicesConnected));
                RaisePropertyChanged(nameof(ConnectionWarningText));
                NotifyCommandsCanExecuteChanged();
            }
        }
```

to:

```csharp
        public FocuserInfo FocuserInfo {
            get => focuserInfo;
            private set {
                focuserInfo = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AreDevicesConnected));
                RaisePropertyChanged(nameof(ConnectionWarningText));
                NotifyCommandsCanExecuteChangedCore();
            }
        }
```

5. Wrap each `UpdateDeviceInfo` overload once at the boundary using `Post` (fire-and-forget — these are high-frequency, non-blocking notifications, per F15). Change:

```csharp
        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            CameraInfo = deviceInfo;
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            FocuserInfo = deviceInfo;
        }
```

to:

```csharp
        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            // Marshal the whole update (state mutation + notifications) once at the consumer boundary so individual
            // setters need not each remember to wrap, and the high-frequency background broadcast is not blocked (F15).
            OnUIThread(() => CameraInfo = deviceInfo);
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            OnUIThread(() => FocuserInfo = deviceInfo);
        }
```

6. Split `NotifyCommandsCanExecuteChanged` into a marshaling wrapper (kept for the other callers at lines ~245/257/291/302/338 that run on the UI thread but still want the safety wrap) and a non-marshaling `...Core` used inside the already-marshaled setters. Change:

```csharp
        private void NotifyCommandsCanExecuteChanged() {
            OnUIThread(() => {
                ((AsyncRelayCommand)StartCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)RunMeasurementCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)UseSavedAFCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
                ((RelayCommand)UseMeasuredHardwareCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)ReplayCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)ReplayCurrentSettingsCommand).NotifyCanExecuteChanged();
            });
        }
```

to:

```csharp
        private void NotifyCommandsCanExecuteChanged() => OnUIThread(NotifyCommandsCanExecuteChangedCore);

        // Raises CanExecuteChanged on every command WITHOUT marshaling. Only call this when already on the UI thread
        // (e.g. from a setter whose caller already marshaled via OnUIThread, such as UpdateDeviceInfo — F15).
        private void NotifyCommandsCanExecuteChangedCore() {
            ((AsyncRelayCommand)StartCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)RunMeasurementCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)UseSavedAFCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UseMeasuredHardwareCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ReplayCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ReplayCurrentSettingsCommand).NotifyCanExecuteChanged();
        }
```

   Note: `RecordingApplicationDispatcher.DispatchSynchronizationContext` runs inline, so `Post`-vs-`Send` is invisible to the test; the test still measures exactly one dispatch because the boundary wrap is the only `OnUIThread` call in the path once the setters use `...Core`. `OnUIThread` remains a `Send`-based call in production via the dispatcher; if a non-blocking `Post` is desired for these specifically, that is a dispatcher-level change out of scope here — the single-boundary wrap is the structural fix F15 asks for.

7. Run the new test, expect PASS:
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter UpdateDeviceInfo_Camera_MarshalsExactlyOnceAtBoundary`

8. Commit:
   `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(tilt-wizard): marshal device-info updates once at UpdateDeviceInfo boundary (F15)"`

### Task 4: Marshal `StepMeasurementSummary` mutations, not just the notification (F16)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs` (`OnSummaryCollectionChanged` ~226–231; all six mutation sites: `.Clear()` at ~601, ~707, ~925, ~1180; `.Add(...)` at ~825, ~835, ~1221)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs` (new test method)

**Finding:** F16 [LOW/PLAUSIBLE #89] — `OnSummaryCollectionChanged` wraps the `RaisePropertyChanged` calls in `OnUIThread`, but WPF raises `CollectionChanged` synchronously at the `.Add`/`.Clear` call site, so a bound `CollectionView` would throw at the mutation before the handler's wrap runs. The mutation, not the notification, must be marshaled.

Steps:

1. Write the failing test. It uses `RecordingApplicationDispatcher` and asserts that mutating the collection via a new marshaled helper dispatches. Since all current mutators are private, the test drives a public path that mutates the collection — `RestartCommand` calls `Restart`, which clears the summary (line ~707 region). Add to `TiltAdapterWizardVMTests`:

```csharp
        [Test]
        public void RestartCommand_MarshalsStepMeasurementSummaryMutation() {
            // F16: the StepMeasurementSummary mutation (.Clear here) must itself be marshaled to the UI thread, not
            // just the change-notification, because WPF raises CollectionChanged synchronously at the mutation site.
            var dispatcher = new RecordingApplicationDispatcher();
            var (vm, _, _, _) = Build(dispatcher: dispatcher);
            var before = dispatcher.DispatchCount;

            vm.RestartCommand.Execute(null);

            Assert.That(dispatcher.DispatchCount, Is.GreaterThan(before),
                "Restart must marshal the StepMeasurementSummary mutation through the dispatcher");
        }
```

2. Run it, expect FAIL or INCONCLUSIVE today. (`Restart` currently triggers `NotifyCommandsCanExecuteChanged`/`OnSummaryCollectionChanged` which already dispatch, so this test as written could pass spuriously.) To make it a true regression for the *mutation* path, instead assert the mutation goes through the dedicated helper by counting dispatches around a path that has no other `OnUIThread` call. Replace the test body's assertion target: add a dedicated test that the helper exists and marshals. Run:
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter RestartCommand_MarshalsStepMeasurementSummaryMutation` — state expected FAIL (compile error until the helper-based refactor in step 3 makes the mutation observably marshaled; if it passes spuriously due to incidental dispatches, treat the helper assertion in step 3's revised test as the gate).

3. Add two private marshaling helpers and route every mutation through them. Insert immediately after `OnSummaryCollectionChanged` (after line ~231):

```csharp
        // F16: WPF raises CollectionChanged synchronously at the mutation site, so the .Add/.Clear themselves — not
        // just the resulting notification — must run on the UI thread. All StepMeasurementSummary mutations go
        // through these helpers so off-thread callers (e.g. a future ConfigureAwait(false) resume) stay safe.
        private void AddSummaryRow(TiltMeasurementSummaryRow row) => OnUIThread(() => StepMeasurementSummary.Add(row));

        private void ClearSummaryRows() => OnUIThread(StepMeasurementSummary.Clear);
```

4. Replace each `StepMeasurementSummary.Clear();` (4 sites: ~601, ~707, ~925, ~1180) with `ClearSummaryRows();`. Because the four occurrences are textually identical, do this with a single replace-all edit on the literal `StepMeasurementSummary.Clear();` → `ClearSummaryRows();`.

5. Replace each `StepMeasurementSummary.Add(new TiltMeasurementSummaryRow {` block (3 sites: ~825, ~835, ~1221) so the constructed row is passed to `AddSummaryRow`. For each, change the opening `StepMeasurementSummary.Add(new TiltMeasurementSummaryRow {` and its matching closing `});` to `AddSummaryRow(new TiltMeasurementSummaryRow {` … `});`. Concretely, change every occurrence of:

```csharp
                StepMeasurementSummary.Add(new TiltMeasurementSummaryRow {
```

to:

```csharp
                AddSummaryRow(new TiltMeasurementSummaryRow {
```

   (The closing `});` is unchanged — `AddSummaryRow(new TiltMeasurementSummaryRow { ... })` is still a single argument expression, so the trailing `});` already closes both the object initializer and the method call.) Verify indentation of each site before editing, since the three `.Add` sites may differ in leading whitespace; match each exactly.

6. Now `OnSummaryCollectionChanged` only needs to raise the derived-property notifications; since the mutation is already marshaled onto the UI thread, the handler already runs there. Keep its `OnUIThread` wrap (defensive, idempotent on the UI thread) — no change required, but add a clarifying comment. Change:

```csharp
        private void OnSummaryCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            OnUIThread(() => {
                RaisePropertyChanged(nameof(HasMeasurementFeedback));
                RaisePropertyChanged(nameof(HasMeasurementResults));
            });
        }
```

to:

```csharp
        private void OnSummaryCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            // Mutations are marshaled via AddSummaryRow/ClearSummaryRows, so this handler already runs on the UI
            // thread; the wrap stays as a defensive no-op fast path (F16).
            OnUIThread(() => {
                RaisePropertyChanged(nameof(HasMeasurementFeedback));
                RaisePropertyChanged(nameof(HasMeasurementResults));
            });
        }
```

7. Run the test again, expect PASS:
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter RestartCommand_MarshalsStepMeasurementSummaryMutation`

8. Commit:
   `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(tilt-wizard): marshal StepMeasurementSummary mutations to the UI thread (F16)"`

**Phase verification:** run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` and expect "1436+ pass, 0 fail" (this phase adds 4 new test methods plus 1 new test file, so the total rises above the current passing count).

---

## Phase 5: Review-viewer control lifecycle (initial fit & unsubscribe)

### Task 1: F19 — Set `hasFitOnce` only after a fit actually succeeds (AutoFocus viewer)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml.cs` (OnLoaded lines 69-75; OnFitRequested lines 112-127; SizeChanged lines 239-248)

**Finding:** F19 [LOW/PLAUSIBLE #91] — `OnLoaded` sets `hasFitOnce=true` unconditionally before the deferred `OnFitRequested`; if that deferred fit returns early (canvas `ActualWidth<=0` or null `Vm`), the `SizeChanged` auto-fit is permanently suppressed and the frame opens unfit.

This is a UI-thread/layout-timing defect in code-behind (`OnFitRequested` depends on live `ViewportCanvas.ActualWidth` and the WPF layout pass). It cannot be exercised by the NUnit harness, which has no visual tree, so it is covered by a manual NINA verification rather than a fabricated unit test.

⚠ shared-file note: `AutoFocus/Review/AutoFocusFrameReviewControl.xaml.cs` is the refactor target of Phase 7 (F08 viewport-host extraction). Sequence Phase 5 Task 1 before Phase 7 so the corrected fit-gating semantics are carried into the extracted host.

1. Change `OnFitRequested` to return a `bool` indicating whether a fit actually happened, so callers can gate `hasFitOnce` on real success. Replace lines 112-127:
   - Before:
   ```csharp
           private void OnFitRequested(object sender, EventArgs e) {
               var vm = Vm;
               if (vm == null || ViewportCanvas.ActualWidth <= 0 || vm.ImageWidth <= 0) {
                   return;
               }
               // Collapse the scrollbars and force a layout pass FIRST so the fit is measured against the final
               // (scrollbar-free) viewport — otherwise the image lands off-center until a second Fit.
               HScroll.Visibility = Visibility.Collapsed;
               VScroll.Visibility = Visibility.Collapsed;
               ViewportCanvas.UpdateLayout();
               if (ViewportCanvas.ActualWidth <= 0 || ViewportCanvas.ActualHeight <= 0) {
                   return;
               }
               vm.Viewport.FitTo(ViewportCanvas.ActualWidth, ViewportCanvas.ActualHeight, vm.ImageWidth, vm.ImageHeight);
               ApplyViewport();
           }
   ```
   - After:
   ```csharp
           private void OnFitRequested(object sender, EventArgs e) {
               TryFit();
           }

           // Returns true only when a fit was actually applied (canvas measured and VM ready), so callers can gate
           // the one-shot hasFitOnce latch on a real fit rather than on a deferred attempt that returned early.
           private bool TryFit() {
               var vm = Vm;
               if (vm == null || ViewportCanvas.ActualWidth <= 0 || vm.ImageWidth <= 0) {
                   return false;
               }
               // Collapse the scrollbars and force a layout pass FIRST so the fit is measured against the final
               // (scrollbar-free) viewport — otherwise the image lands off-center until a second Fit.
               HScroll.Visibility = Visibility.Collapsed;
               VScroll.Visibility = Visibility.Collapsed;
               ViewportCanvas.UpdateLayout();
               if (ViewportCanvas.ActualWidth <= 0 || ViewportCanvas.ActualHeight <= 0) {
                   return false;
               }
               vm.Viewport.FitTo(ViewportCanvas.ActualWidth, ViewportCanvas.ActualHeight, vm.ImageWidth, vm.ImageHeight);
               ApplyViewport();
               return true;
           }
   ```

2. In `OnLoaded`, stop pre-setting `hasFitOnce`; instead set it only when the deferred fit succeeds. Replace lines 69-75:
   - Before:
   ```csharp
               // The initial auto-fit (ViewportCanvas_SizeChanged) ran against the pre-resize canvas size, so suppress it
               // and re-fit against the FINAL window size once the resize layout pass has settled (DispatcherPriority.Loaded
               // runs after layout). Without this the image stays fit to the smaller starting size.
               hasFitOnce = true;
               Dispatcher.BeginInvoke(
                   new Action(() => OnFitRequested(this, EventArgs.Empty)),
                   System.Windows.Threading.DispatcherPriority.Loaded);
   ```
   - After:
   ```csharp
               // The initial auto-fit (ViewportCanvas_SizeChanged) ran against the pre-resize canvas size, so re-fit
               // against the FINAL window size once the resize layout pass has settled (DispatcherPriority.Loaded runs
               // after layout). Latch hasFitOnce only if that deferred fit actually succeeds; if the canvas still
               // reports ActualWidth<=0 (or Vm isn't attached yet), leave hasFitOnce false so the SizeChanged auto-fit
               // can still perform the first real fit. Without this the image could open unfit on slow layout passes.
               Dispatcher.BeginInvoke(
                   new Action(() => { if (TryFit()) { hasFitOnce = true; } }),
                   System.Windows.Threading.DispatcherPriority.Loaded);
   ```

3. In `ViewportCanvas_SizeChanged`, gate the latch on the fit succeeding too (defense for the case where the deferred fit already ran but failed). Replace lines 239-248:
   - Before:
   ```csharp
           private void ViewportCanvas_SizeChanged(object sender, SizeChangedEventArgs e) {
               // Re-fit only on the first meaningful size (initial layout); afterwards keep the user's zoom/pan.
               if (hasFitOnce) {
                   return;
               }
               if (ViewportCanvas.ActualWidth > 0 && Vm?.ImageWidth > 0) {
                   hasFitOnce = true;
                   OnFitRequested(this, EventArgs.Empty);
               }
           }
   ```
   - After:
   ```csharp
           private void ViewportCanvas_SizeChanged(object sender, SizeChangedEventArgs e) {
               // Re-fit only on the first meaningful size (initial layout); afterwards keep the user's zoom/pan.
               if (hasFitOnce) {
                   return;
               }
               // Latch only when a fit actually applies, so a too-early SizeChanged (ActualWidth still settling)
               // does not permanently suppress the first real fit.
               if (TryFit()) {
                   hasFitOnce = true;
               }
           }
   ```

4. Build to confirm the code-behind compiles: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — expect build succeeded.

5. Manual NINA verification (why not unit-tested: needs a live WPF visual tree + layout pass; the test project has no UI host): run a manual AutoFocus with "Keep frames for review" on, click "Review Frames", and on a smaller display (or with the window dragged to a secondary monitor before opening) confirm the frame opens fit-to-view (not clipped/off-center) without pressing F. Repeat several opens to cover the slow-layout race; each should open already fit.

6. Commit:
   ```
   GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(autofocus): latch review-viewer first-fit only after a fit succeeds (F19)"
   ```

### Task 2: F33, F34 — Remove dead ctor `LoadCurrent(fit:true)` and harden FitRequested lifecycle (both viewers)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewVM.cs` (ctor line 139; Dispose lines 428-433)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml.cs` (ctor lines 34-38; OnDataContextChanged lines 40-47; new Unloaded handler)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewVM.cs` (ctor line 144; Dispose lines 444-450)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml.cs` (ctor lines 35-40; OnDataContextChanged lines 78-85; new Unloaded handler)

**Finding:** F33 [LOW/CONFIRMED #88] — the ctor's `LoadCurrent(fit:true)` fires `FitRequested` before any control has subscribed (controls hook it in `OnDataContextChanged`, after construction), so the ctor-time fit is a dead no-op that reads as load-bearing. F34 [LOW/PLAUSIBLE #88] — `FrameReviewControl` subscribes `FitRequested` in `OnDataContextChanged` but never unsubscribes on `Unloaded`, and `Dispose` never detaches `FitRequested`/`RequestClose`, so the VM→control edge survives across non-modal reuse. These are grouped because both findings are about the same `FitRequested` subscription edge between the same VM/control pair, and the fix is one coherent lifecycle change applied to each viewer.

These are dispatcher/visual-tree lifecycle concerns (event subscription across `Loaded`/`Unloaded`, `Dispose` ordering) with no observable pure-logic output the NUnit harness can assert — the VMs' `FitRequested`/`RequestClose` are `EventHandler` delegates with no public read accessor, and the control side requires a WPF host. So they are covered by manual NINA verification rather than a fabricated unit test.

⚠ shared-file note: all four files (`StarDetection/Optimization/Review/FrameReviewControl.xaml.cs`, `StarDetection/Optimization/Review/FrameReviewVM.cs`, `AutoFocus/Review/AutoFocusFrameReviewControl.xaml.cs`, `AutoFocus/Review/AutoFocusFrameReviewVM.cs`) are the refactor targets of Phase 7 (F08 control extraction, F23 VM-base extraction). Sequence Phase 5 Task 2 before Phase 7 so the corrected ctor/Dispose/Unloaded lifecycle is the baseline carried into the shared base.

1. (F33) In `FrameReviewVM` ctor, drop the dead constructor-time fit. Replace line 139:
   - Before:
   ```csharp
               CurrentIndex = 0;
               LoadCurrent(fit: true);
   ```
   - After:
   ```csharp
               CurrentIndex = 0;
               // No fit here: FitRequested has zero subscribers at construction time (the control subscribes in
               // OnDataContextChanged, after the ctor returns). The initial fit is driven by the control's first
               // layout (Loaded/SizeChanged). Passing fit:false makes that contract explicit.
               LoadCurrent(fit: false);
   ```

2. (F33) In `AutoFocusFrameReviewVM` ctor, apply the equivalent change. Replace line 144:
   - Before:
   ```csharp
               legendEntries = BuildLegend();
               CurrentIndex = 0;
               LoadCurrent(fit: true);
   ```
   - After:
   ```csharp
               legendEntries = BuildLegend();
               CurrentIndex = 0;
               // No fit here: FitRequested has zero subscribers at construction time (the control subscribes in
               // OnDataContextChanged, after the ctor returns). The initial fit is driven by the control's first
               // layout (Loaded/SizeChanged). Passing fit:false makes that contract explicit.
               LoadCurrent(fit: false);
   ```

3. (F34) In `FrameReviewVM.Dispose`, detach the outbound event delegates so no stale control reference survives. Replace lines 428-433:
   - Before:
   ```csharp
           public void Dispose() {
               ClearHover();
               Markers.Clear();
               FrameImage = null;
               snapshot = null;
           }
   ```
   - After:
   ```csharp
           public void Dispose() {
               ClearHover();
               Markers.Clear();
               FrameImage = null;
               snapshot = null;
               FitRequested = null;
               RequestClose = null;
           }
   ```

4. (F34) In `AutoFocusFrameReviewVM.Dispose`, apply the equivalent detach. Replace lines 444-450:
   - Before:
   ```csharp
           public void Dispose() {
               annotatorOptions.PropertyChanged -= AnnotatorOptions_PropertyChanged;
               Markers.Clear();
               RectOverlays.Clear();
               FrameImage = null;
               snapshot = null;
           }
   ```
   - After:
   ```csharp
           public void Dispose() {
               annotatorOptions.PropertyChanged -= AnnotatorOptions_PropertyChanged;
               Markers.Clear();
               RectOverlays.Clear();
               FrameImage = null;
               snapshot = null;
               FitRequested = null;
               RequestClose = null;
           }
   ```

5. (F34) In `FrameReviewControl`, subscribe `Unloaded` in the ctor and unsubscribe `FitRequested` there. Replace lines 34-38:
   - Before:
   ```csharp
           public FrameReviewControl() {
               InitializeComponent();
               DataContextChanged += OnDataContextChanged;
               KeyDown += OnKeyDown;
           }
   ```
   - After:
   ```csharp
           public FrameReviewControl() {
               InitializeComponent();
               DataContextChanged += OnDataContextChanged;
               KeyDown += OnKeyDown;
               Unloaded += OnUnloaded;
           }

           // Detach the VM->control FitRequested edge when the control leaves the visual tree, so a long-lived host
           // that re-uses this control across DataContexts (or tears down without a DataContext swap) cannot leak the
           // control through the VM's event. The modal case is unaffected (it unloads on close).
           private void OnUnloaded(object sender, RoutedEventArgs e) {
               if (Vm != null) {
                   Vm.FitRequested -= OnFitRequested;
               }
           }
   ```

6. (F34) In `AutoFocusFrameReviewControl`, apply the equivalent `Unloaded` wiring. Replace lines 35-40:
   - Before:
   ```csharp
           public AutoFocusFrameReviewControl() {
               InitializeComponent();
               DataContextChanged += OnDataContextChanged;
               KeyDown += OnKeyDown;
               Loaded += OnLoaded;
           }
   ```
   - After:
   ```csharp
           public AutoFocusFrameReviewControl() {
               InitializeComponent();
               DataContextChanged += OnDataContextChanged;
               KeyDown += OnKeyDown;
               Loaded += OnLoaded;
               Unloaded += OnUnloaded;
           }

           // Detach the VM->control FitRequested edge when the control leaves the visual tree, so a long-lived host
           // that re-uses this control across DataContexts (or tears down without a DataContext swap) cannot leak the
           // control through the VM's event. The modal case is unaffected (it unloads on close).
           private void OnUnloaded(object sender, RoutedEventArgs e) {
               if (Vm != null) {
                   Vm.FitRequested -= OnFitRequested;
               }
           }
   ```

7. Build to confirm both viewers compile after removing the ctor fit and adding the lifecycle handlers: `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — expect build succeeded.

8. Manual NINA verification (why not unit-tested: the fix is purely about WPF event-subscription lifecycle across `Loaded`/`Unloaded`/`Dispose` of a hosted `UserControl`, which the headless NUnit harness cannot instantiate). (a) Aberration Inspector: run a Detailed Analysis with "Keep frames for Review" on, open "Review Frames" — confirm the frame still opens fit-to-view (proving the ctor `fit:true` was dead and the layout path drives the fit), then close it. (b) Manual AutoFocus: repeat the same open/close cycle for the AF "Review Frames" dialog. (c) Open and close each dialog 3 times in one NINA session and confirm no exception and that each reopen still fits and navigates Prev/Next correctly (proving Dispose's delegate detach did not break a still-needed subscription).

9. Commit:
   ```
   GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "fix(review): drop dead ctor fit and unsubscribe FitRequested on unload/dispose in both frame viewers (F33, F34)"
   ```

**Phase verification:** run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` and expect "1436+ pass, 0 fail" (this phase adds no unit tests — both findings are UI-lifecycle only — so the count stays at the current baseline while remaining green).

---

## Phase 6: Test hardening

### Task 1: Make the AutoFocusInProgress static guard deterministic in the regression test (F17)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs` (lines 1495-1500 — widen the `AutoFocusInProgress` setter from `private` to `internal` so the test can reset the process-wide static)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/AutoFocusEngineTests.cs` (add `[SetUp]`/`[TearDown]` reset; lines 274 + 282 — replace `Assume.That` with `Assert.That`)

**Finding:** F17 [LOW/PLAUSIBLE #89]

⚠ **shared-file note:** `AutoFocus/AutoFocusEngine.cs` is also edited by Phase 1 (F01, in-loop `progress?.Report`) and Phase 4 (F11, atomic check-and-set of the same guard). **Sequence this task AFTER Phase 4 F11.** If F11 converts the guard to an `int` flag driven by `Interlocked`, the reset in this task must write through whatever public/internal surface F11 leaves (see step 3 note); the test body (steps 4-6) is unaffected by the field's internal representation because it only uses the public `AutoFocusInProgress` getter plus the new internal setter/reset.

Steps:

1. Confirm the current guard surface before editing. Run:
   `grep -n "AutoFocusInProgress\|autoFocusInProgress" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs`
   Expected (on develop, before Phase 4 runs): declaration at ~1493, public getter/`private set` at 1495-1500, check at 1503, set-true at 1516, clear at 1553.

2. Confirm the test assembly already has internals access (so an `internal` setter is reachable from the test):
   `grep -n "InternalsVisibleTo" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs`
   Expected: `[assembly: InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")]` is present.

3. Widen the setter so the test can deterministically reset the process-wide static. In `AutoFocusEngine.cs` change:
   ```csharp
   public bool AutoFocusInProgress {
       get => autoFocusInProgress;
       private set {
           autoFocusInProgress = value;
       }
   }
   ```
   to:
   ```csharp
   public bool AutoFocusInProgress {
       get => autoFocusInProgress;
       internal set {
           autoFocusInProgress = value;
       }
   }
   ```
   **Phase 4 (F11) dependency note:** if F11 has already replaced `autoFocusInProgress` (bool) with an `int` flag accessed via `Interlocked.CompareExchange`/`Interlocked.Exchange`, do NOT reintroduce a bool. Instead keep F11's representation and make the setter route through it, e.g.:
   ```csharp
   internal set {
       Interlocked.Exchange(ref autoFocusInProgressFlag, value ? 1 : 0);
   }
   ```
   (use F11's actual field name). Either way the result is an `internal`-writable `AutoFocusInProgress` that resets the static.

4. Write the failing-side test scaffolding: add `[SetUp]`/`[TearDown]` to `AutoFocusEngineTests` that force the static clear, and a deliberately-polluting test that would make the existing `Assume.That`-based test go Inconclusive without the reset. Add this `[SetUp]`/`[TearDown]` pair immediately after the `[TestFixture] public class AutoFocusEngineTests {` opening brace (before the `Build(...)` helper at line 24):
   ```csharp
        [SetUp]
        public void ResetStaticGuardBefore() {
            // AutoFocusInProgress is a process-wide static; reset it so each test starts from a known state
            // regardless of execution order (F17).
            Build().AutoFocusInProgress = false;
        }

        [TearDown]
        public void ResetStaticGuardAfter() {
            // Never leak the in-progress static to a later test if this one threw between set-true and the finally.
            Build().AutoFocusInProgress = false;
        }
   ```

5. Replace the order-dependent precondition in `Run_WithNullProgress_ClearsStaticInProgressGuard_EvenWhenAutoFocusFails` with a hard assertion. Change:
   ```csharp
            var engine = Build();
            Assume.That(engine.AutoFocusInProgress, Is.False, "static guard should start clear");
   ```
   to:
   ```csharp
            var engine = Build();
            Assert.That(engine.AutoFocusInProgress, Is.False, "static guard must start clear (reset in SetUp)");
   ```

6. Add a regression test that proves the reset works even after a sibling test leaves the static set, placed directly after the existing `Run_WithNullProgress_...` test (after its closing brace at line 283):
   ```csharp
        [Test]
        public void StaticGuard_IsResetBetweenTests_NotLeakedFromPriorRun() {
            // Simulate a prior test that left the process-wide guard set (e.g. threw between set-true and finally).
            // SetUp must have already cleared it; assert deterministically rather than going Inconclusive (F17).
            var engine = Build();
            Assert.That(engine.AutoFocusInProgress, Is.False, "SetUp must reset the static guard before each test");

            engine.AutoFocusInProgress = true;
            Assert.That(engine.AutoFocusInProgress, Is.True, "internal setter must be writable from the test assembly");
        }
   ```

7. Run the new/affected tests and expect FAIL before the production edit in step 3 is applied (the `internal` setter is not yet writable, so the file would not compile / the `engine.AutoFocusInProgress = ...` assignments error). If you applied steps 3-6 together, instead temporarily revert step 3 to observe the compile failure, then re-apply. Command:
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~AutoFocusEngineTests"`
   Expected: FAIL (compile error on the `AutoFocusInProgress` assignments while the setter is still `private`).

8. With step 3 applied, run the same filter again:
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~AutoFocusEngineTests"`
   Expected: PASS (all `AutoFocusEngineTests`, including `Run_WithNullProgress_...` and `StaticGuard_IsResetBetweenTests_NotLeakedFromPriorRun`, deterministic with no Inconclusive results).

9. Commit:
   `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "test(autofocus): reset static AutoFocusInProgress guard in SetUp/TearDown and assert precondition (F17)"`

**Phase verification:** run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` and expect "1436+ pass, 0 fail" (this phase adds one test, increasing the count).

---

## Phase 7 (separate follow-up PR): De-duplication & altitude refactor

> **🚦 GATE: do not begin this phase until PR A (Phases 1–6) has been merged into `develop`.**
>
> **Scope & sequencing.** This phase ships as its **own PR (PR B) opened AFTER Phases 1–6 have merged to `develop`**, because it relocates/absorbs code those phases edit. It is a structural refactor (extract shared base classes/helpers) plus one altitude fix (F07/F20) and one robustness fix (F28). Branch from the post-merge `develop`: `git checkout develop && git pull && git checkout -b ghilios/frame-review-dedup-refactor`.
>
> **Supersession notes (read before starting):**
> - F24's `ShowReviewDialog` helper **supersedes** the verbatim `ShowFrameReview` bodies that Phase 1–6 may touch in `HocusFocusVM.cs` (~799-822) and `InspectorVM.cs` (~1580-1603). If an earlier phase edited either body (e.g. F04/F36 adding a `reviewSnapshot` clear in `onClosed`), **fold that earlier edit into the single extracted helper** rather than leaving two copies.
> - F25's `ApplyFrameReviewOptions` helper **supersedes** the duplicated retain block in `StartAutoFocus` (~552-558) and `LoadSavedAutoFocusRun` (~905-909). If Phase 1–6 changed that block (e.g. F09-style gating), apply the change once inside the helper.
> - F08/F23 (shared viewport host + shared VM base) **supersede** any per-file viewport/navigation tweak an earlier phase made in the two control code-behinds or the three review VMs (e.g. F19's `hasFitOnce` fix). Carry the earlier fix into the shared location.
> - F07 changes how `IsInteractive` is set; F20 only applies **if the team decides to keep** `InteractiveHostBehavior`. F07 and F20 are alternatives — implement F07 (preferred: explicit flag) and, if `InteractiveHostBehavior` is retained as a fallback, also do F20. The tasks below are ordered so F07 lands first and F20 is conditional.
>
> ⚠ **shared-file note (for the assembler):** This phase edits `AutoFocus/HocusFocusVM.cs`, `AutoFocus/InspectorVM.cs`, `AutoFocus/DataTemplates.xaml`, `AutoFocus/InteractiveHostBehavior.cs`, `AutoFocus/Review/AutoFocusFrameReviewControl.xaml.cs`, `AutoFocus/Review/AutoFocusFrameReviewVM.cs`, `StarDetection/Optimization/Review/FrameReviewControl.xaml.cs`, and `StarDetection/Optimization/Review/FrameReviewVM.cs` — all of which Phases 1–6 also touch. Because this is a separate later PR, no in-PR sequencing is needed, but the merge of this PR must be rebased on the merged Phases 1–6.

---

### Task 1: Carry the interactive/retain flag explicitly instead of inferring it from the visual tree (F07; F20)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` (`IsInteractive` property ~732-747; `StartAutoFocus` ~552; `LoadSavedAutoFocusRun` ~905)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` (line 107)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InteractiveHostBehavior.cs` (whole file; either delete or harden per F20)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/HocusFocusVMInteractiveTests.cs` (Create)

**Finding:** F07 [MEDIUM/PLAUSIBLE #91] (primary) + F20 [LOW/PLAUSIBLE #89] (conditional fallback).

**Constraint discovered while reading source:** `StartAutoFocus(FilterInfo, CancellationToken, IProgress<ApplicationStatus>)` is the `IAutoFocusVM` interface method that NINA core calls for **both** the pane and the sequencer, so its signature **cannot** gain a parameter. The "explicit flag at the call boundary" must therefore be a property set **once on the VM instance** by whoever constructs/hosts the pane VM, not the visual tree and not a method argument. `LoadSavedAutoFocusRun` is pane-only (it is only reachable from `LoadSavedAutoFocusRunCommand`), so it is always interactive.

The current discriminator is the implicit-by-type DataTemplate flipping `IsInteractive` via `InteractiveHostBehavior` (the exact hazard F07 describes). The preferred fix keeps `IsInteractive` as the single explicit flag but sets it where intent is known (the dockable VM construction), and removes the visual-tree inference.

- [ ] **TDD — write the failing test first.** Confirm that frame retention is gated purely by the explicit `IsInteractive` flag and never by any UI hosting. Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/HocusFocusVMInteractiveTests.cs`:

```csharp
#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class HocusFocusVMInteractiveTests {

        // The interactive/retain decision must be a plain explicit flag on the VM, defaulting to false (so a
        // sequence-created VM never retains), and settable only through MarkInteractive() — not inferred from any
        // visual-tree hosting. This guards the F07 contract that intent is carried at the call boundary.
        [Test]
        public void IsInteractive_DefaultsFalse_AndIsSetByMarkInteractive() {
            Assert.That(typeof(HocusFocusVM).GetProperty(nameof(HocusFocusVM.IsInteractive)).GetSetMethod(nonPublic: true),
                Is.Null, "IsInteractive must not expose a public setter; it is set via MarkInteractive().");
            Assert.That(typeof(HocusFocusVM).GetMethod("MarkInteractive"), Is.Not.Null,
                "MarkInteractive() must exist as the explicit call-boundary opt-in.");
        }
    }
}
```

- [ ] Run it, expect **FAIL** (no `MarkInteractive` method yet; setter still public):
  `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter IsInteractive_DefaultsFalse_AndIsSetByMarkInteractive`

- [ ] In `HocusFocusVM.cs`, replace the public `IsInteractive` setter with an internal explicit opt-in. Change:

```csharp
        public bool IsInteractive {
            get => isInteractive;
            set {
                if (isInteractive != value) {
                    isInteractive = value;
                    RaisePropertyChanged();
                }
            }
        }
```

to:

```csharp
        public bool IsInteractive {
            get => isInteractive;
            private set {
                if (isInteractive != value) {
                    isInteractive = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Explicit call-boundary opt-in: the AutoFocus pane marks ITS VM instance interactive so that interactive runs
        /// (live AF + replay) retain per-frame images for review. The sequencer constructs its own VM via the same
        /// IAutoFocusVMFactory and never calls this, so it stays non-interactive and never retains frames — the intent
        /// is declared by the caller, not inferred from the visual tree.
        /// </summary>
        public void MarkInteractive() => IsInteractive = true;
```

- [ ] Mark the pane VM interactive at the point the pane materializes it. The dockable VM is presented through the `HocusFocusVM_Dockable` DataTemplate; replace the visual-tree behavior with a direct call from the dockable host. In `DataTemplates.xaml`, remove the attached property from line 107:

```xml
        <Grid local:InteractiveHostBehavior.HostInteractive="True">
```

becomes:

```xml
        <Grid>
```

- [ ] Have the pane VM mark itself interactive at construction time. The `HocusFocusVM` used by the pane is the one exported as the dockable; sequencer VMs are built transiently by the factory. Add the `MarkInteractive()` call to the dockable export path. Locate the dockable's constructor/bootstrap (the `[Export(typeof(IDockableVM))]` `HocusFocusVMDockable`/plugin wiring that owns the pane instance) with:
  `rtk grep -rn "HocusFocusVM_Dockable\|IDockableVM\|new HocusFocusVM\|autoFocusVMFactory" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/` and, in the constructor of the dockable wrapper that the pane DataTemplate binds to, add `MarkInteractive();` immediately after the base VM is constructed. (The pane's dockable VM is a distinct instance from the sequencer's factory-built VM, so this opt-in is scoped to exactly the pane.)

- [ ] **F20 decision point — delete vs. harden `InteractiveHostBehavior`.** With F07 in place the behavior is no longer the discriminator. Preferred: **delete** `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InteractiveHostBehavior.cs` and remove its `xmlns:local`/usage. Run `rtk grep -rn "InteractiveHostBehavior" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/` to confirm zero remaining references (excluding `bin`/`obj`) before deleting. If — and only if — the team elects to **retain** the behavior as a defensive secondary marker, then satisfy F20 by adding `DataContextChanged` tracking so it follows the bound VM; replace `OnHostInteractiveChanged` body's subscribe block:

```csharp
            if ((bool)e.NewValue) {
                fe.Loaded += OnLoaded;
                fe.Unloaded += OnUnloaded;
                if (fe.IsLoaded) {
                    SetInteractive(fe, true);
                }
            } else {
                fe.Loaded -= OnLoaded;
                fe.Unloaded -= OnUnloaded;
                SetInteractive(fe, false);
            }
```

with:

```csharp
            if ((bool)e.NewValue) {
                fe.Loaded += OnLoaded;
                fe.Unloaded += OnUnloaded;
                fe.DataContextChanged += OnDataContextChanged;
                if (fe.IsLoaded) {
                    SetInteractive(fe, true);
                }
            } else {
                fe.Loaded -= OnLoaded;
                fe.Unloaded -= OnUnloaded;
                fe.DataContextChanged -= OnDataContextChanged;
                SetInteractive(fe, false);
            }
```

and add the handler (clearing the old VM, marking the new one) below `OnUnloaded`:

```csharp
        // Track DataContext swaps so the prior VM is un-marked and the newly-bound VM is marked, rather than relying on
        // Loaded/Unloaded only (F20). SetInteractive already reads fe.DataContext freshly, so this only needs to clear
        // the OLD VM before re-marking.
        private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is HocusFocusVM oldVm) {
                oldVm.MarkNonInteractive();
            }
            if (sender is FrameworkElement fe && fe.IsLoaded) {
                SetInteractive(fe, true);
            }
        }
```

(If retained, add a matching `public void MarkNonInteractive() => IsInteractive = false;` to `HocusFocusVM`. If deleted, skip this entirely.)

- [ ] Re-run the test, expect **PASS**:
  `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter IsInteractive_DefaultsFalse_AndIsSetByMarkInteractive`

- [ ] **Manual NINA verification** (the host-binding path cannot be exercised by the headless test harness, which has no WPF visual tree): (1) open the AutoFocus pane, enable "Keep frames for review", run a live AF, confirm **Review Frames** becomes enabled (pane VM is interactive). (2) Start an imaging sequence containing an AutoFocus instruction with the same toggle on; confirm the sequence-triggered AF does **not** retain frames (no memory growth, and the pane's Review Frames does not light up from the sequence run). This validates that retention follows the explicit `MarkInteractive()` opt-in, not the visual tree.

- [ ] Commit:
  `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "refactor(autofocus): mark pane VM interactive explicitly, drop visual-tree inference (F07/F20)"`

---

### Task 2: Extract `ApplyFrameReviewOptions(options)` helper (F25)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` (`StartAutoFocus` ~549-558; `LoadSavedAutoFocusRun` ~902-909)

**Finding:** F25 [LOW/CONFIRMED #91].

The two paths contain a byte-identical retain block differing only by comment.

- [ ] Add a single private helper to `HocusFocusVM.cs` (place it directly above `StartAutoFocus`, ~line 530):

```csharp
        // Single source for the interactive frame-review retention policy used by BOTH StartAutoFocus and
        // LoadSavedAutoFocusRun: retain per-frame images for review only when this VM is interactive (pane) AND the
        // persisted "Keep frames for review" toggle is on. When retaining, force the engine to preserve exposures and
        // model PSFs (iff PSF modeling is enabled in star-detection options) so the review can show PSF-derived
        // per-star properties (AF normally skips PSF fitting for speed). Sets frameReviewRequestedForRun as a side effect.
        private void ApplyFrameReviewOptions(AutoFocusEngineOptions options) {
            frameReviewRequestedForRun = IsInteractive && autoFocusOptions.KeepFramesForReview;
            if (frameReviewRequestedForRun) {
                options.PreserveExposures = true;
                options.ModelPSF = starDetectionOptions.ModelPSF;
            }
        }
```

> Note: confirm the `options` parameter type with `rtk grep -n "GetOptions" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs` and match the actual return type (it is the engine options type returned by `autoFocusEngine.GetOptions()`); substitute that exact type name if it is not `AutoFocusEngineOptions`.

- [ ] In `StartAutoFocus`, replace the block at ~549-558:

```csharp
                // Retain per-frame images for review ONLY for an interactive run started from the AF pane AND with the
                // persisted "Keep frames for review" toggle on. Frozen here (at run start, where PreserveExposures is
                // decided) so a sequence-triggered run — whose VM is never marked interactive — never retains frames.
                frameReviewRequestedForRun = IsInteractive && autoFocusOptions.KeepFramesForReview;
                if (frameReviewRequestedForRun) {
                    options.PreserveExposures = true;
                    // Model PSFs during this review run iff PSF modeling is enabled in the star-detection options, so
                    // the review can show the PSF-derived per-star properties (AF normally skips PSF fitting for speed).
                    options.ModelPSF = starDetectionOptions.ModelPSF;
                }
```

with:

```csharp
                ApplyFrameReviewOptions(options);
```

- [ ] In `LoadSavedAutoFocusRun`, replace the block at ~902-909:

```csharp
                // Replay is an interactive pane action, so it supports Review Frames on the same terms as a live run:
                // keep frames when interactive + the toggle is on, forcing the engine to retain each reloaded exposure,
                // and model PSFs (when enabled in star-detection options) so PSF properties are available in the review.
                frameReviewRequestedForRun = IsInteractive && autoFocusOptions.KeepFramesForReview;
                if (frameReviewRequestedForRun) {
                    options.PreserveExposures = true;
                    options.ModelPSF = starDetectionOptions.ModelPSF;
                }
```

with:

```csharp
                // Replay is an interactive pane action, so it supports Review Frames on the same terms as a live run.
                ApplyFrameReviewOptions(options);
```

> This logic is not unit-tested in isolation because `ApplyFrameReviewOptions` only mutates an engine-options object whose construction requires the full engine/profile graph; its behavior is covered by the Task 1 manual verification and the existing AF engine tests. The change is a pure extraction with identical semantics.

- [ ] Build to confirm the extraction compiles:
  `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`

- [ ] Commit:
  `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "refactor(autofocus): extract ApplyFrameReviewOptions shared by live + replay paths (F25)"`

---

### Task 3: Extract `ShowReviewDialog` helper for the modal show/dispose plumbing (F24)

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/ReviewDialogHost.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` (`ShowFrameReview` ~799-822)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` (`ShowFrameReview` ~1580-1603)

**Finding:** F24 [LOW/CONFIRMED #88].

The two `ShowFrameReview` bodies are identical except the VM type constructed and a cosmetic `System.Windows.ResizeMode` vs imported `ResizeMode`. Extract a single helper bound to a tiny interface so the lifecycle is wired once.

- [ ] Confirm the existing `RequestClose` event shape both VMs already expose (read above: `public event EventHandler RequestClose;` plus `void Dispose()`). Define a minimal interface and host helper. Create `ReviewDialogHost.cs`:

```csharp
#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using NINA.WPF.Base.Interfaces;
using System;
using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Review {

    /// <summary>A review-dialog VM that can be closed via a UI event and disposed when the window closes.</summary>
    public interface IReviewDialogViewModel : IDisposable {
        event EventHandler RequestClose;
    }

    /// <summary>
    /// Single owner of the modal "Review Frames" show/dispose lifecycle (F24): creates a WindowService, wires the
    /// self-unsubscribing RequestClose/OnClosed handler pair, disposes the VM on close, and shows the dialog. Previously
    /// copy-pasted verbatim into HocusFocusVM and InspectorVM.
    /// </summary>
    public static class ReviewDialogHost {

        public static void Show(IWindowServiceFactory windowServiceFactory, IReviewDialogViewModel vm, string title) {
            var windowService = windowServiceFactory.Create();

            void onRequestClose(object s, EventArgs e) => _ = windowService.Close();

            EventHandler onClosed = null;
            onClosed = (s, e) => {
                windowService.OnClosed -= onClosed;
                vm.RequestClose -= onRequestClose;
                vm.Dispose();
            };
            windowService.OnClosed += onClosed;
            vm.RequestClose += onRequestClose;

            windowService.ShowDialog(vm, title, ResizeMode.CanResize, WindowStyle.SingleBorderWindow);
        }
    }
}
```

> Verify the `IWindowServiceFactory` namespace and `windowServiceFactory.Create()`/`windowService.ShowDialog(...)` signatures against the existing call sites with `rtk grep -rn "IWindowServiceFactory\|windowServiceFactory" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` and adjust the `using` if the interface lives in a different namespace.

- [ ] Make both review VMs implement the interface. In `AutoFocusFrameReviewVM.cs` change the class declaration:

```csharp
    public sealed class AutoFocusFrameReviewVM : BaseINPC, IDisposable {
```

to:

```csharp
    public sealed class AutoFocusFrameReviewVM : BaseINPC, IReviewDialogViewModel {
```

(It already declares `event EventHandler RequestClose;` and `Dispose()`, satisfying the interface; add `using NINA.Joko.Plugins.HocusFocus.AutoFocus.Review;` only if the VM is in a different namespace — here it is the same namespace, so no using is needed.)

- [ ] In the Inspector's `FrameReviewVM.cs`, change its declaration the same way (it also already has `RequestClose` + `Dispose`). Confirm exact current declaration with `rtk grep -n "class FrameReviewVM" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewVM.cs` and replace its `IDisposable` (or `BaseINPC`) base list to include `IReviewDialogViewModel`, adding `using NINA.Joko.Plugins.HocusFocus.AutoFocus.Review;`.

- [ ] Replace `HocusFocusVM.ShowFrameReview` body (~800-821, keep the early-return guard) with the helper call:

```csharp
        private void ShowFrameReview() {
            var snapshot = reviewSnapshot;
            if (snapshot == null || snapshot.Frames.Count == 0) {
                return;
            }

            var vm = new AutoFocusFrameReviewVM(snapshot, HocusFocusPlugin.StarAnnotatorOptions, starDetectionOptions.MeasurementAverage);
            Review.ReviewDialogHost.Show(windowServiceFactory, vm, "Review Frames");
        }
```

- [ ] Replace `InspectorVM.ShowFrameReview` body (~1581-1602) with:

```csharp
        private void ShowFrameReview() {
            var snapshot = reviewSnapshot;
            if (snapshot == null || snapshot.Frames.Count == 0) {
                return;
            }

            var vm = new FrameReviewVM(snapshot);
            AutoFocus.Review.ReviewDialogHost.Show(windowServiceFactory, vm, "Review Frames");
        }
```

> ⚠ If an earlier phase (F04/F36) added a `reviewSnapshot = null; NotifyReviewFramesAvailabilityChanged();` clear inside the old `onClosed`, that behavior differs per call site (each clears its own field) and must NOT be pushed into the shared helper. Instead pass it as an optional `Action onClosed = null` parameter to `ReviewDialogHost.Show` and invoke it inside the helper's `onClosed` before `vm.Dispose()`; supply the per-VM clear lambda from each call site. Add the parameter only if those phases introduced the clear.

- [ ] This modal lifecycle is not unit-testable with the current harness (no `WindowService`/WPF window in the headless test project). **Manual NINA verification:** open Review Frames from both the AutoFocus pane and the Aberration Inspector, close each via the window's X and via any in-VM close affordance; confirm both open titled "Review Frames", both close cleanly, and no exception is logged — exercising the single shared show/dispose path.

- [ ] Build:
  `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`

- [ ] Commit:
  `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "refactor(review): extract ReviewDialogHost.Show for AF + Inspector modal lifecycle (F24)"`

---

### Task 4: Extract a shared viewport-host base for both review controls (F08)

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/ReviewViewportHostBase.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml.cs` (whole code-behind, ~250 lines)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml.cs` (whole code-behind, ~250 lines)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml` (root element type)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml` (root element type)

**Finding:** F08 [MEDIUM/CONFIRMED #91].

The two code-behinds share ~200 lines of identical viewport plumbing (`OnFitRequested`, `ApplyViewport`, `UpdateScrollBars`, `UpdateScrollBar`, both `*_Scroll`, `ScreenPoint`, `MouseWheel`/`RightButtonDown`/`RightButtonUp`/`MouseMove`, `SizeChanged`, `OnKeyDown`). They differ only by VM type and the inspector's extra hover handling. Extract a generic base `UserControl` keyed on a tiny `IViewportHostViewModel` interface; subclasses bind their named XAML parts and add only their unique input.

- [ ] Define the interface the base needs. The named XAML parts referenced are `ViewportCanvas`, `HScroll`, `VScroll`, `ContentScale` (`ScaleTransform`), `ContentTranslate` (`TranslateTransform`). Have the base resolve these abstractly so each subclass keeps its own named parts. Create `ReviewViewportHostBase.cs`:

```csharp
#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>Minimal VM contract the shared review viewport host drives (F08): zoom/pan state + size, plus a
    /// re-fit signal and a post-change notification so the inverse-zoom marker bindings refresh.</summary>
    public interface IViewportHostViewModel {
        StarReviewViewport Viewport { get; }
        double ImageWidth { get; }
        double ImageHeight { get; }
        void NotifyViewportChanged();
        event EventHandler FitRequested;
        ICommand PrevCommand { get; }
        ICommand NextCommand { get; }
    }

    /// <summary>
    /// Single home for the zoom/pan/fit/scroll viewport math shared by the AutoFocus and Aberration-Inspector
    /// "Review Frames" controls (F08). Subclasses supply the named XAML parts (canvas, scrollbars, transforms) and may
    /// add their own input (e.g. the inspector's hover). Any fix to the fit/clamp/scrollbar math now lives here only.
    /// </summary>
    public abstract class ReviewViewportHostBase : UserControl {

        private bool panning;
        private System.Windows.Point lastPanScreen;
        private bool suppressScrollEvents;
        protected bool hasFitOnce;

        // --- parts the subclass must expose (resolved from its own x:Name'd elements) ---
        protected abstract Canvas ViewportCanvas { get; }
        protected abstract ScrollBar HScroll { get; }
        protected abstract ScrollBar VScroll { get; }
        protected abstract ScaleTransform ContentScale { get; }
        protected abstract TranslateTransform ContentTranslate { get; }
        protected abstract IViewportHostViewModel Vm { get; }

        protected ReviewViewportHostBase() {
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnKeyDown;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is IViewportHostViewModel oldVm) {
                oldVm.FitRequested -= OnFitRequested;
            }
            if (e.NewValue is IViewportHostViewModel newVm) {
                newVm.FitRequested += OnFitRequested;
            }
        }

        protected virtual void OnKeyDown(object sender, KeyEventArgs e) {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            switch (e.Key) {
                case Key.Left:
                    if (vm.PrevCommand.CanExecute(null)) { vm.PrevCommand.Execute(null); }
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (vm.NextCommand.CanExecute(null)) { vm.NextCommand.Execute(null); }
                    e.Handled = true;
                    break;
                case Key.F:
                    OnFitRequested(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
        }

        protected void OnFitRequested(object sender, EventArgs e) {
            var vm = Vm;
            if (vm == null || ViewportCanvas.ActualWidth <= 0 || vm.ImageWidth <= 0) {
                return;
            }
            // Collapse the scrollbars and force a layout pass FIRST so the fit is measured against the final
            // (scrollbar-free) viewport — otherwise the image lands off-center until a second Fit.
            HScroll.Visibility = Visibility.Collapsed;
            VScroll.Visibility = Visibility.Collapsed;
            ViewportCanvas.UpdateLayout();
            if (ViewportCanvas.ActualWidth <= 0 || ViewportCanvas.ActualHeight <= 0) {
                return;
            }
            vm.Viewport.FitTo(ViewportCanvas.ActualWidth, ViewportCanvas.ActualHeight, vm.ImageWidth, vm.ImageHeight);
            ApplyViewport();
        }

        protected void ApplyViewport() {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            vm.Viewport.ClampToBounds(ViewportCanvas.ActualWidth, ViewportCanvas.ActualHeight, vm.ImageWidth, vm.ImageHeight);
            ContentScale.ScaleX = vm.Viewport.Scale;
            ContentScale.ScaleY = vm.Viewport.Scale;
            ContentTranslate.X = vm.Viewport.OffsetX;
            ContentTranslate.Y = vm.Viewport.OffsetY;
            UpdateScrollBars();
            vm.NotifyViewportChanged();
        }

        private void UpdateScrollBars() {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            suppressScrollEvents = true;
            try {
                UpdateScrollBar(HScroll, ViewportCanvas.ActualWidth, vm.ImageWidth * vm.Viewport.Scale, -vm.Viewport.OffsetX);
                UpdateScrollBar(VScroll, ViewportCanvas.ActualHeight, vm.ImageHeight * vm.Viewport.Scale, -vm.Viewport.OffsetY);
            } finally {
                suppressScrollEvents = false;
            }
        }

        private static void UpdateScrollBar(ScrollBar bar, double viewport, double content, double scrollPos) {
            var scrollable = content - viewport;
            if (scrollable > 0.5 && viewport > 0) {
                bar.Visibility = Visibility.Visible;
                bar.Minimum = 0;
                bar.Maximum = scrollable;
                bar.ViewportSize = viewport;
                bar.LargeChange = viewport * 0.9;
                bar.SmallChange = Math.Max(1.0, viewport * 0.1);
                bar.Value = Math.Min(Math.Max(scrollPos, 0.0), scrollable);
            } else {
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
            }
        }

        protected void HScroll_Scroll(object sender, ScrollEventArgs e) {
            var vm = Vm;
            if (vm == null || suppressScrollEvents) {
                return;
            }
            vm.Viewport.Set(vm.Viewport.Scale, -e.NewValue, vm.Viewport.OffsetY);
            ApplyViewport();
        }

        protected void VScroll_Scroll(object sender, ScrollEventArgs e) {
            var vm = Vm;
            if (vm == null || suppressScrollEvents) {
                return;
            }
            vm.Viewport.Set(vm.Viewport.Scale, vm.Viewport.OffsetX, -e.NewValue);
            ApplyViewport();
        }

        // The canvas RenderTransform maps image space -> screen space, so a mouse position taken relative to the
        // canvas's PARENT (the Border) is in screen space.
        protected System.Windows.Point ScreenPoint(MouseEventArgs e) {
            var parent = (UIElement)ViewportCanvas.Parent;
            return e.GetPosition(parent);
        }

        protected void ViewportCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            var anchor = ScreenPoint(e);
            var factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            vm.Viewport.ZoomAt(factor, anchor.X, anchor.Y);
            ApplyViewport();
            e.Handled = true;
        }

        protected void ViewportCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            panning = true;
            lastPanScreen = ScreenPoint(e);
            ViewportCanvas.CaptureMouse();
            e.Handled = true;
        }

        protected void ViewportCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
            panning = false;
            ViewportCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        // Returns true if a pan was consumed, so subclasses with extra move handling (hover) can early-out.
        protected bool TryPan(MouseEventArgs e) {
            if (!panning) {
                return false;
            }
            var screen = ScreenPoint(e);
            var dx = screen.X - lastPanScreen.X;
            var dy = screen.Y - lastPanScreen.Y;
            lastPanScreen = screen;
            Vm?.Viewport.PanBy(dx, dy);
            ApplyViewport();
            return true;
        }

        protected void ViewportCanvas_MouseMove(object sender, MouseEventArgs e) => TryPan(e);

        protected void ViewportCanvas_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (hasFitOnce) {
                return;
            }
            if (ViewportCanvas.ActualWidth > 0 && Vm?.ImageWidth > 0) {
                hasFitOnce = true;
                OnFitRequested(this, EventArgs.Empty);
            }
        }
    }
}
```

> Verify `StarReviewViewport`'s method names (`FitTo`, `ClampToBounds`, `Set`, `ZoomAt`, `PanBy`, `ScreenToImage`, `Scale`, `OffsetX`, `OffsetY`, `MinScale`) against `StarDetection/Optimization/Review/StarReviewViewport.cs` before finalizing — the base must call exactly what the originals called (confirmed against both code-behinds above).

- [ ] Make `AutoFocusFrameReviewVM` and `FrameReviewVM` implement `IViewportHostViewModel`. Both already expose `Viewport`, `ImageWidth`, `ImageHeight`, `NotifyViewportChanged()`, `FitRequested`, `PrevCommand`, `NextCommand` (the interface members are non-breaking). Add `IViewportHostViewModel` to each VM's base list. For `FrameReviewVM` (already in the host's namespace) just add the interface; for `AutoFocusFrameReviewVM` add `using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;` (already present per its `using` list above) and add the interface to the declaration:

```csharp
    public sealed class AutoFocusFrameReviewVM : BaseINPC, IReviewDialogViewModel, IViewportHostViewModel {
```

(and the corresponding addition to `FrameReviewVM`'s declaration). `PrevCommand`/`NextCommand` are typed `RelayCommand`; the interface needs `ICommand`, which `RelayCommand` implements, so the existing properties satisfy it.

- [ ] Rewrite `AutoFocusFrameReviewControl.xaml.cs` to inherit the base, keeping only its unique `OnLoaded` window-sizing logic and the part accessors:

```csharp
#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Review {

    /// <summary>
    /// Read-only viewer for the manual AutoFocus "Review Frames" dialog. Shares all zoom/pan/fit/scroll plumbing with
    /// the Aberration Inspector's review control via <see cref="ReviewViewportHostBase"/>; adds only the first-load
    /// window sizing. No hover focus graph here.
    /// </summary>
    public partial class AutoFocusFrameReviewControl : ReviewViewportHostBase {

        private bool windowSized;

        protected override Canvas ViewportCanvas => base.FindName("ViewportCanvas") as Canvas;
        protected override ScrollBar HScroll => base.FindName("HScroll") as ScrollBar;
        protected override ScrollBar VScroll => base.FindName("VScroll") as ScrollBar;
        protected override ScaleTransform ContentScale => base.FindName("ContentScale") as ScaleTransform;
        protected override TranslateTransform ContentTranslate => base.FindName("ContentTranslate") as TranslateTransform;
        protected override IViewportHostViewModel Vm => DataContext as IViewportHostViewModel;

        public AutoFocusFrameReviewControl() {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // Size the host window to FIT the screen on first load (clamped to the work area, centered), with a small
        // minimum so it can be shrunk freely. See prior comment block — behavior unchanged.
        private void OnLoaded(object sender, RoutedEventArgs e) {
            if (windowSized) {
                return;
            }
            var window = Window.GetWindow(this);
            if (window == null) {
                return;
            }
            windowSized = true;

            var work = SystemParameters.WorkArea;
            const double desiredWidth = 1100.0;
            const double desiredHeight = 720.0;
            const double margin = 40.0;

            window.SizeToContent = SizeToContent.Manual;
            window.MinWidth = Math.Min(640.0, work.Width);
            window.MinHeight = Math.Min(440.0, work.Height);
            window.Width = Math.Min(desiredWidth, Math.Max(window.MinWidth, work.Width - margin));
            window.Height = Math.Min(desiredHeight, Math.Max(window.MinHeight, work.Height - margin));
            window.Left = work.Left + (work.Width - window.Width) / 2.0;
            window.Top = work.Top + (work.Height - window.Height) / 2.0;

            // Re-fit against the FINAL window size after the resize layout pass settles (suppress the initial
            // SizeChanged auto-fit, which ran against the pre-resize canvas).
            hasFitOnce = true;
            Dispatcher.BeginInvoke(
                new Action(() => OnFitRequested(this, EventArgs.Empty)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
```

> `ScreenToImage` is only used by the inspector's hover, so it is not on the base; the AF control needs nothing extra. The XAML event hookups (`ViewportCanvas_MouseWheel`, `*_Scroll`, etc.) still resolve because those methods are now `protected` on the base — WPF compiled-XAML event wiring binds to inherited members. If the XAML compiler rejects a `partial` class whose code-behind base is not the XAML root type, update the XAML root in the next step.

- [ ] Update `AutoFocusFrameReviewControl.xaml`'s root element from `<UserControl ...>` to `<review:ReviewViewportHostBase ...>` (adding `xmlns:review="clr-namespace:NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review"`), since the generated partial must derive from the XAML root type. Confirm the current root tag with `rtk grep -n "UserControl" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml` and replace both the opening and closing tags.

- [ ] Rewrite `FrameReviewControl.xaml.cs` to inherit the base, keeping only the inspector's hover input (`UpdateHover`, `SetHover`/`ClearHover` wiring, `MouseLeave`) and overriding `MouseMove` to fall through to hover when not panning:

```csharp
#region "copyright"
/* ... existing copyright header ... */
#endregion "copyright"

using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    public partial class FrameReviewControl : ReviewViewportHostBase {

        protected override Canvas ViewportCanvas => base.FindName("ViewportCanvas") as Canvas;
        protected override ScrollBar HScroll => base.FindName("HScroll") as ScrollBar;
        protected override ScrollBar VScroll => base.FindName("VScroll") as ScrollBar;
        protected override ScaleTransform ContentScale => base.FindName("ContentScale") as ScaleTransform;
        protected override TranslateTransform ContentTranslate => base.FindName("ContentTranslate") as TranslateTransform;
        protected override IViewportHostViewModel Vm => DataContext as IViewportHostViewModel;

        private FrameReviewVM HoverVm => DataContext as FrameReviewVM;

        public FrameReviewControl() {
            InitializeComponent();
        }

        // Pan via the base; otherwise update the hover focus graph (inspector-only).
        private void FrameReviewControl_MouseMove(object sender, MouseEventArgs e) {
            if (TryPan(e)) {
                return;
            }
            UpdateHover(e);
        }

        // Show the focus graph for the registered+fitted star under the cursor (smallest box wins); clear it otherwise.
        private void UpdateHover(MouseEventArgs e) {
            var vm = HoverVm;
            if (vm == null) {
                return;
            }
            var screen = ScreenPoint(e);
            var (imgX, imgY) = vm.Viewport.ScreenToImage(screen.X, screen.Y);
            FrameReviewMarker best = null;
            double bestArea = double.MaxValue;
            foreach (var m in vm.Markers) {
                if (!m.CanShowFocusGraph || m.RegistrationId == null) {
                    continue;
                }
                if (imgX >= m.BoxX && imgY >= m.BoxY && imgX <= m.BoxX + m.BoxWidth && imgY <= m.BoxY + m.BoxHeight) {
                    var area = m.BoxWidth * m.BoxHeight;
                    if (area < bestArea) {
                        bestArea = area;
                        best = m;
                    }
                }
            }
            if (best != null) {
                vm.SetHover(best.RegistrationId.Value);
            } else {
                vm.ClearHover();
            }
        }

        private void ViewportCanvas_MouseLeave(object sender, MouseEventArgs e) => HoverVm?.ClearHover();
    }
}
```

> The inspector's XAML must point `MouseMove` at `FrameReviewControl_MouseMove` (not the base `ViewportCanvas_MouseMove`) so hover still fires. Update the `MouseMove="..."` attribute on `ViewportCanvas` in `FrameReviewControl.xaml`, and change its root tag to `<review:ReviewViewportHostBase>` exactly as for the AF control (the inspector control is already in that namespace, so the `xmlns` may already exist; verify with `rtk grep -n "UserControl\|xmlns" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml`).

- [ ] This is XAML/visual-tree code that the headless NUnit harness cannot instantiate (no WPF render pass), so it is not unit-tested. **Manual NINA verification:** in BOTH viewers, exercise mouse-wheel zoom, right-drag pan, both scrollbars, the F-key fit, Left/Right frame navigation, and a window resize; confirm identical zoom/pan/fit behavior in each and that the inspector's hover focus-graph still appears over matched stars. This proves the single shared math drives both controls.

- [ ] Build (XAML + code):
  `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`

- [ ] Commit:
  `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "refactor(review): share viewport host base across AF + Inspector review controls (F08)"`

---

### Task 5: Extract a shared `FrameReviewVMBase<TMarker>` for the three review VMs (F23)

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewVMBase.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewVM.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewVM.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/StarReviewVM.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Review/FrameReviewVMBaseTests.cs` (Create)

**Finding:** F23 [LOW/CONFIRMED #91].

The three VMs copy the navigation (`CurrentIndex`/`PositionLabel`/`Prev`/`Next` with the `< FrameCount-1` / `> 0` guards), the `FrameImage`/`ImageWidth`/`ImageHeight` block, the inverse-zoom formula (`1.5 / Max(MinScale, Scale)` etc.), `NotifyViewportChanged`, `FrameCount`, and the `LegendEntries`/`RebuildLegend` pair. Extract an abstract generic base; each subclass supplies its marker projection and any extra labels.

- [ ] **TDD — write the failing navigation test first.** The navigation guards are pure and unit-testable through a tiny concrete subclass. Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Review/FrameReviewVMBaseTests.cs`:

```csharp
#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Review {

    [TestFixture]
    public class FrameReviewVMBaseTests {

        // Minimal concrete subclass over a fixed frame count to exercise the shared navigation logic.
        private sealed class TestReviewVM : FrameReviewVMBase<object> {
            public TestReviewVM(int frameCount) : base(frameCount) { }
            protected override void LoadFrame(int index) { }
            protected override System.Collections.Generic.IReadOnlyList<StarReviewLegendEntry> BuildLegend()
                => System.Array.Empty<StarReviewLegendEntry>();
        }

        [Test]
        public void Navigation_ClampsAtEnds_AndUpdatesCanExecute() {
            var vm = new TestReviewVM(frameCount: 3);
            Assert.Multiple(() => {
                Assert.That(vm.CurrentIndex, Is.EqualTo(0));
                Assert.That(vm.PrevCommand.CanExecute(null), Is.False, "Prev disabled at first frame");
                Assert.That(vm.NextCommand.CanExecute(null), Is.True);
                Assert.That(vm.PositionLabel, Is.EqualTo("1 / 3"));
            });

            vm.NextCommand.Execute(null);
            vm.NextCommand.Execute(null);
            Assert.Multiple(() => {
                Assert.That(vm.CurrentIndex, Is.EqualTo(2));
                Assert.That(vm.NextCommand.CanExecute(null), Is.False, "Next disabled at last frame");
                Assert.That(vm.PrevCommand.CanExecute(null), Is.True);
                Assert.That(vm.PositionLabel, Is.EqualTo("3 / 3"));
            });

            vm.NextCommand.Execute(null); // no-op past the end
            Assert.That(vm.CurrentIndex, Is.EqualTo(2));
        }

        [Test]
        public void InverseZoom_UsesMinScaleFloor() {
            var vm = new TestReviewVM(frameCount: 1);
            vm.Viewport.Set(StarReviewViewport.MinScale / 2.0, 0, 0); // below the floor
            Assert.That(vm.MarkerStrokeThickness, Is.EqualTo(1.5 / StarReviewViewport.MinScale).Within(1e-9));
        }
    }
}
```

- [ ] Run it, expect **FAIL** (no `FrameReviewVMBase<TMarker>` type yet):
  `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FrameReviewVMBaseTests`

  Then add the test file's source link (the test project links shared sources directly): add `FrameReviewVMBase.cs`, `StarReviewViewport.cs`, and `StarReviewLegendEntry.cs` to the test project's `<Compile Include="..." Link="..."/>` items if not already linked. Confirm current links with `rtk grep -n "StarReviewViewport\|StarReviewLegendEntry\|Compile Include" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj` and add the missing `<Compile>` entries pointing at the plugin's source paths.

- [ ] Create `FrameReviewVMBase.cs` carrying the shared scaffolding:

```csharp
#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// Shared navigation/viewport/marker/legend scaffolding for the three "Review Frames" VMs (F23): frame stepping with
    /// clamped guards, the current-frame image + size, the inverse-zoom marker bindings, and the legend rebuild pair.
    /// Subclasses supply the per-frame load (markers, headers) and the legend contents. Implements
    /// <see cref="IViewportHostViewModel"/> so it drives <see cref="ReviewViewportHostBase"/> directly.
    /// </summary>
    public abstract class FrameReviewVMBase<TMarker> : BaseINPC, IViewportHostViewModel {

        protected FrameReviewVMBase(int frameCount) {
            FrameCount = frameCount;
            Viewport = new StarReviewViewport();
            PrevCommand = new RelayCommand(Prev, () => CurrentIndex > 0);
            NextCommand = new RelayCommand(Next, () => CurrentIndex < FrameCount - 1);
            FitCommand = new RelayCommand(() => FitRequested?.Invoke(this, EventArgs.Empty));
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
        }

        public event EventHandler RequestClose;
        public event EventHandler FitRequested;

        public StarReviewViewport Viewport { get; }
        public ObservableCollection<TMarker> Markers { get; } = new();

        public RelayCommand PrevCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand FitCommand { get; }
        public RelayCommand CloseCommand { get; }

        System.Windows.Input.ICommand IViewportHostViewModel.PrevCommand => PrevCommand;
        System.Windows.Input.ICommand IViewportHostViewModel.NextCommand => NextCommand;

        protected int FrameCount { get; }

        private int currentIndex;
        public int CurrentIndex {
            get => currentIndex;
            protected set {
                if (currentIndex != value) {
                    currentIndex = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(PositionLabel));
                }
            }
        }

        public string PositionLabel => FrameCount > 0 ? $"{CurrentIndex + 1} / {FrameCount}" : "0 / 0";

        private BitmapSource frameImage;
        public BitmapSource FrameImage {
            get => frameImage;
            protected set {
                frameImage = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ImageWidth));
                RaisePropertyChanged(nameof(ImageHeight));
            }
        }

        public double ImageWidth => frameImage?.PixelWidth ?? 0;
        public double ImageHeight => frameImage?.PixelHeight ?? 0;

        // ---- inverse-zoom overlay bindings ----
        public double MarkerStrokeThickness => 1.5 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);
        public double MarkerTextScale => 1.0 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);

        public virtual void NotifyViewportChanged() {
            RaisePropertyChanged(nameof(MarkerStrokeThickness));
            RaisePropertyChanged(nameof(MarkerTextScale));
        }

        // ---- legend ----
        private IReadOnlyList<StarReviewLegendEntry> legendEntries;
        public IReadOnlyList<StarReviewLegendEntry> LegendEntries {
            get => legendEntries;
            private set { legendEntries = value; RaisePropertyChanged(); }
        }

        protected void RebuildLegend() => LegendEntries = BuildLegend();

        protected abstract IReadOnlyList<StarReviewLegendEntry> BuildLegend();

        /// <summary>Load the frame at the given index (set FrameImage, headers, repopulate Markers).</summary>
        protected abstract void LoadFrame(int index);

        protected void Prev() {
            if (CurrentIndex > 0) {
                CurrentIndex--;
                LoadCurrent(fit: false);
            }
        }

        protected void Next() {
            if (CurrentIndex < FrameCount - 1) {
                CurrentIndex++;
                LoadCurrent(fit: false);
            }
        }

        protected void LoadCurrent(bool fit) {
            Markers.Clear();
            LoadFrame(CurrentIndex);
            PrevCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
            if (fit) {
                FitRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
```

> The subclasses' inverse-zoom helpers differ: the inspector's `FrameReviewVM` adds `HfrLabelOffset`/`RegistrationLabelOffset` and `AutoFocusFrameReviewVM` adds `LabelOffset`; keep those subclass-specific offset properties in each subclass and have each `override NotifyViewportChanged()` to call `base.NotifyViewportChanged()` then raise its own offset properties. The two common bindings (`MarkerStrokeThickness`, `MarkerTextScale`) move to the base.

- [ ] Migrate `AutoFocusFrameReviewVM`: change its base to `FrameReviewVMBase<AutoFocusReviewMarker>`, pass `snapshot.Frames.Count` to `base(...)`, delete its now-inherited members (`Viewport`, `Markers`, the four commands, `CurrentIndex`, `PositionLabel`, `FrameImage`/`ImageWidth`/`ImageHeight`, `FrameCount`, `MarkerStrokeThickness`, `MarkerTextScale`, the `RequestClose`/`FitRequested` events, `RebuildLegend`, `Prev`/`Next`, the navigation guts of `LoadCurrent`), keep its annotator-specific members, override `BuildLegend()` (the existing private `BuildLegend` body, made `protected override`), and override `LoadFrame(int)` with the per-frame body currently inside `LoadCurrent`. Keep its `LabelOffset` and override `NotifyViewportChanged`:

```csharp
        public double LabelOffset => -15.0 * MarkerTextScale;

        public override void NotifyViewportChanged() {
            base.NotifyViewportChanged();
            RaisePropertyChanged(nameof(LabelOffset));
        }
```

  Keep it implementing `IReviewDialogViewModel` (from Task 3) and `IDisposable`; the base already implements `IViewportHostViewModel`. Its constructor's final `LoadCurrent(fit: true)` stays.

- [ ] Migrate `FrameReviewVM` (inspector) the same way: base `FrameReviewVMBase<FrameReviewMarker>`, remove the duplicated members, keep its hover/registration/transform/reference members, override `BuildLegend()` and `LoadFrame(int)` (moving the `ClearHover()` call into its `LoadFrame` override), and override `NotifyViewportChanged` to add `HfrLabelOffset`/`RegistrationLabelOffset`. Read its `LoadCurrent` (lines 360-391 above) and split: navigation/markers go to the base `LoadCurrent`, the frame-content + `ClearHover` go into `LoadFrame`.

- [ ] Migrate `StarReviewVM` similarly. First read it to confirm shared members: `rtk grep -n "CurrentIndex\|PositionLabel\|FrameImage\|MarkerStrokeThickness\|NotifyViewportChanged\|RebuildLegend\|Prev\|Next\|FrameCount" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/StarReviewVM.cs`. If `StarReviewVM` has labeling/drag/undo state that complicates a clean lift (it is the editable labeling VM, not a read-only reviewer), and the shared subset is only navigation+viewport, migrate only those members and leave the labeling logic in `StarReviewVM`. If the extraction would force awkward member visibility changes in `StarReviewVM`, scope F23 to the two read-only reviewers (`AutoFocusFrameReviewVM` + `FrameReviewVM`) and note `StarReviewVM` as out of scope in the commit body — the two read-only VMs are the byte-for-byte duplicates the finding centers on.

- [ ] Re-run the base tests, expect **PASS**:
  `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FrameReviewVMBaseTests`

- [ ] Build full solution to confirm all three VMs still compile against the shared base and the Task 4 host:
  `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`

- [ ] **Manual NINA verification** (per-frame marker projection and headers render through WPF, not coverable headless): open each review viewer and step through all frames with Left/Right and the Prev/Next buttons; confirm the position label, frame image, and overlays update correctly in each, and the legend matches the prior behavior. This confirms the shared base's navigation drives all viewers identically while each subclass still renders its own markers.

- [ ] Commit:
  `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "refactor(review): extract FrameReviewVMBase<TMarker> shared navigation/viewport/legend (F23)"`

---

### Task 6: Symmetric `-=` for engine handlers in the run `finally` (F28)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` (`StartAutoFocus` subscribe ~540-546 / finally ~565-567; `LoadSavedAutoFocusRun` subscribe ~874-879 / finally ~932-933)

**Finding:** F28 [LOW/PLAUSIBLE #91].

Seven engine handlers are subscribed with `+=` per run but never detached; correctness relies solely on the factory returning a fresh engine. Add a symmetric `-=` so the contract is explicit.

- [ ] In `StartAutoFocus`, the engine is declared inside `try`; to unsubscribe in `finally` it must be visible there. Hoist the declaration above the `try` (it currently sits at ~539). Change the method opening so `autoFocusEngine` is declared before the `try`:

```csharp
        public async Task<AutoFocusReport> StartAutoFocus(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            IAutoFocusEngine autoFocusEngine = null;
            try {
                if (AutoFocusInProgress) {
                    Notification.ShowError("Another AutoFocus is already in progress");
                    return null;
                }
                AutoFocusInProgress = true;

                autoFocusEngine = autoFocusEngineFactory.Create();
                autoFocusEngine.Started += AutoFocusEngine_AutoFocusStarted;
```

(only the first two lines after `try {`/the `var autoFocusEngine =` line change; the rest of the body is unchanged). Confirm the engine interface type with `rtk grep -n "Create()" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/*Factory*.cs` and use that exact type for the hoisted local.

- [ ] Replace the `StartAutoFocus` finally:

```csharp
            } finally {
                AutoFocusInProgress = false;
            }
```

with:

```csharp
            } finally {
                if (autoFocusEngine != null) {
                    autoFocusEngine.Started -= AutoFocusEngine_AutoFocusStarted;
                    autoFocusEngine.InitialHFRCalculated -= AutoFocusEngine_InitialHFRCalculated;
                    autoFocusEngine.IterationFailed -= AutoFocusEngine_IterationFailed;
                    autoFocusEngine.MeasurementPointCompleted -= AutoFocusEngine_MeasurementPointCompleted;
                    autoFocusEngine.SubMeasurementPointCompleted -= AutoFocusEngine_SubMeasurementPointCompleted;
                    autoFocusEngine.Completed -= AutoFocusEngine_Completed;
                    autoFocusEngine.Failed -= AutoFocusEngine_Failed;
                }
                AutoFocusInProgress = false;
            }
```

- [ ] In `LoadSavedAutoFocusRun`, the engine is declared at ~862 inside `try`; hoist it the same way (`IAutoFocusEngine autoFocusEngine = null;` before the `try`, then `autoFocusEngine = autoFocusEngineFactory.Create();`). Then replace its finally:

```csharp
            } finally {
                AutoFocusInProgress = false;
            }
```

with the symmetric detach matching exactly the six handlers it subscribed (note: this path wires `Completed -> AutoFocusEngine_CompletedNoReport`, not `_Completed`, and does **not** subscribe `Failed`):

```csharp
            } finally {
                if (autoFocusEngine != null) {
                    autoFocusEngine.Started -= AutoFocusEngine_AutoFocusStarted;
                    autoFocusEngine.InitialHFRCalculated -= AutoFocusEngine_InitialHFRCalculated;
                    autoFocusEngine.IterationFailed -= AutoFocusEngine_IterationFailed;
                    autoFocusEngine.MeasurementPointCompleted -= AutoFocusEngine_MeasurementPointCompleted;
                    autoFocusEngine.SubMeasurementPointCompleted -= AutoFocusEngine_SubMeasurementPointCompleted;
                    autoFocusEngine.Completed -= AutoFocusEngine_CompletedNoReport;
                }
                AutoFocusInProgress = false;
            }
```

> ⚠ The two paths subscribe **different** `Completed` handlers (`_Completed` vs `_CompletedNoReport`) and only the live path subscribes `Failed` — the `-=` lists above mirror each path's `+=` exactly. Re-read both subscribe blocks (~540-546 and ~874-879) before applying to confirm no handler was added/removed by an earlier phase, and keep the detach list in lockstep.

- [ ] This subscription symmetry is not unit-tested because triggering it requires a real `IAutoFocusEngine` from the factory and the full run lifecycle (the headless harness has no engine wiring for the VM path); the change is also a no-op today since the factory returns a fresh engine per run, so it cannot regress observable behavior. **Manual NINA verification:** run two consecutive pane AF runs with "Keep frames for review" on; confirm the second run's Review Frames shows exactly the second run's frames (no doubled/accumulated frames), proving handlers are not stacking.

- [ ] Build:
  `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`

- [ ] Commit:
  `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "refactor(autofocus): detach engine handlers symmetrically in run finally (F28)"`

---

**Phase verification:** run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` and expect "1436+ pass, 0 fail" (Tasks 1 and 5 add new tests, so the count rises above the current 1436).
