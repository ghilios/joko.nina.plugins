# Star Detection Optimization — Follow-ups Plan

**Context / rationale:** `docs/star-detection-optimization-wizard-design.md` ("Follow-ups" section) and
`docs/star-detection-optimization-wizard-results.md` (cross-setup + Panos donut diagnosis). The deferred items
from the wizard branch (PR #55) **plus 9 user-feedback items** folded in (see "Feedback integration" below).

**Branch:** `ghilios/star-detection-followups`, created **off `develop`** (PR #55 — the optimization-wizard
branch — is already merged to `develop`, commit `85aedcc`, so the earlier "branch off the wizard branch" note
is obsolete). Never push `develop`; PR to merge.
**Commit identity:** `George Hilios <322725+ghilios@users.noreply.github.com>` (author + committer), per CLAUDE.md.
**After each task:** `cmd.exe /c "dotnet build Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"`
(builds TestApp via the solution) and `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"`;
fix root causes before continuing. CLAUDE.md rules apply (every new option needs a UI control + tooltip; new
`StarDetectorMetrics` fields need the metrics panel; new detector behavior stays **opt-in / default-OFF** so
detection is **bit-identical** when disabled).

**AF-runs bank (available now):** `D:\Autofocus Bank` (WSL `/mnt/d/Autofocus Bank`). Pass the Windows path to
TestApp. `optimize --runs "D:\Autofocus Bank"` discovers all 11 setups (attempt-anchored `attempt01`, depths
2–3). **Panos** (long FL, donut stars) already has ground-truth labels at `D:\Autofocus Bank\Panos\labels`.

**Method:** subagent-driven development (implementer → spec-compliance review → code-quality review per task).
**Hard checkpoints (user):** stop before **F3** (the `OptimizationObjective` change) and again **before opening
the PR**.

---

## Decisions locked with the user (2026-06-15)

1. **#7 review UI reuse → extract a shared control.** Move TestApp's `StarReview` view + VM into the **plugin
   project** as one reusable box-labeling control, consumed by **both** the in-NINA wizard and TestApp's
   `review`/`diagnose-labels`. Single source of truth (no drift). Decouple from TestApp-only deps
   (`Program.ToBitmapSource`, STA scaffolding) during the move.
2. **#4 combine → one toggle drives both.** A single persisted option `DefocusAwareGates` replaces the two
   checkboxes and enables **both** the distortion + centering relaxations. The detector keeps both
   `StarDetectorParams` fields internally; TestApp keeps its granular `--defocus-distortion` /
   `--defocus-centering` flags. Per-gate numeric knobs (size-ref / min-factor / centering-factor) become
   separate **Advanced** tunables (F1).
3. **Order:** do all the **no-bank** items first (UI/cosmetic), then the **bank-dependent** detector/optimizer
   items. The F-item relative order from the original plan is preserved (F1 → F5 → F2 → F3 → F6 → F4); the
   wizard/labeling-UI items slot between F5 and F2 (the point where the bank is first needed).

## Feedback integration (where each of the 9 items lives)

| # | Feedback | Task |
|---|---|---|
| 1 | Labeling UI: drop the 3 mode buttons; click rejected = wrongly-rejected, click accepted = should-reject, drag blank = missed | **T6** |
| 2 | Labeling UI: scroll-wheel zoom (exists), right-button pan (exists), **dashed** labeled-star markers (new) | **T6** |
| 3 | "Optimize Star Detection" button: drop the "…"; also expose in the **Imaging-pane** star-detection options | **T3** |
| 4 | Combine the two defocus-aware gates into one setting | **T1** (with F1) |
| 5 | Research a defocus-driven **layer-count** increase using the Panos dataset | **T13** (with F4) |
| 6 | Wizard shows the FQN instead of UI | **T4** |
| 7 | Wizard ends with a **review step** (the labeling UI), all frames visible | **T8** (needs T5 + T6) |
| 8 | After review, if any labels were made, offer to **re-optimize** with the feedback | **T9** |
| 9 | End: show changed settings + **accept/cancel**; on accept, select optimized settings in options | **T7** |

---

## Part A — UI & cosmetic (no AF bank needed)

### T1 — F1 + #4: defocus gate options (combine + expose tunables)
- **Combine (#4):** add a single persisted `StarDetectionOptions.DefocusAwareGates` (bool, default **false**)
  on the options class + `IStarDetectionOptions`. `BuildStarDetectorParams` maps it to **both**
  `params.DefocusAwareDistortion` and `params.DefocusAwareCentering`
  (`HocusFocusStarDetection.cs:303-306`). Remove the two old `DefocusAwareDistortion`/`DefocusAwareCentering`
  options + their two UI rows (`OptionsDataTemplates.xaml:1428-1458`); replace with one Advanced CheckBox +
  tooltip. Update `InitializeOptions`, `ResetDefaults`, and `StarDetectionOptionsTests`.
- **Expose tunables (F1):** surface `DefocusDistortionSizeReference` (30), `DefocusDistortionMinFactor` (0.25),
  `DefocusCenteringToleranceFactor` (2.0) as **Advanced** `StarDetectionOptions` (UnitTextBox + DoubleRangeRule
  + tooltip), wired through `BuildStarDetectorParams` + `ResetDefaults`, defaults unchanged. Keep both
  `StarDetectorParams` fields (`IStarDetector.cs:306-340`).
- **Default OFF ⇒ bit-identical.** **Verify:** persistence round-trip tests; build + full `dotnet test` green.
  (Donut-recovery confirmation at lower size-ref happens in T10/T13 with the bank.)

### T2 — F5: joint-mode `optimized_settings.json` out-dir copy (cosmetic)
In joint mode the `--out` copy of `optimized_settings.json` reflects the **last** run's recommended step
(`OptimizationDiagnosticRunner.cs:~468`; per-run source-folder copies are correct). Either write the joint
winner's representative step or omit the out-dir copy in joint mode. Small, self-contained.

### T3 — #3: launch button text + Imaging-pane availability
- Drop the trailing "…" on the launch button (`Options.xaml:128`, "Optimize Star Detection…").
- Make the button available in the **Imaging-pane** star-detection options. The imaging pane renders via the
  shared `HocusFocus_StarDetection_Options` template (`OptionsDataTemplates.xaml:899`) with DataContext =
  `StarDetectionOptionsVM` (`StarDetection/StarDetectionOptionsVM.cs:24`), which lacks the command. Add an
  `OptimizeStarDetectionCommand` reachable from that template's DataContext (factor the wizard-launch out of
  `HocusFocusPlugin.OptimizeStarDetection` so both the Options page and the imaging-pane VM can invoke it),
  and add the button to the shared template. Verify the main Options page still works (its DataContext is the
  plugin manifest).

### T4 — #6: wizard renders its UI (not the FQN)
**Root cause:** the wizard's DataTemplate is keyed only by string `x:Key`
(`Optimization/DataTemplates.xaml:20`). NINA's `WindowService.ShowDialog(vm, …)` resolves window content via
**implicit `DataType` templates**, not string keys (cf. `AutoFocus/DataTemplates.xaml:610`
`DataType="{x:Type local:HocusFocusVM}"`, `:3622` for `InspectorVM`); with no `DataType` template WPF falls
back to `ToString()` → the FQN. **Fix:** add a `DataTemplate DataType="{x:Type local:StarDetectionOptimizerWizardVM}"`
(reuse the same content). Verify the exported `[Export(typeof(ResourceDictionary))]` dict is merged into app
resources (it is — the DockableVM DataType templates resolve). **Verify:** ideally launch NINA to confirm the
panel renders; at minimum confirm the resolution mechanism against the working DataType templates and that the
build is clean. (Same resolution mechanism governs T8's embedded review content.)

### T5 — extract `StarReview` into the plugin project as a shared labeling control (#7 prerequisite)
Move the `StarReview` view + VM + pure helpers (`StarReviewLabels`, `StarReviewViewport`, marker model) from
`TestApp/StarReview/` into the **plugin project** under a suitable namespace
(`NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review` or similar). Decouple from TestApp-only
deps (replace `Program.ToBitmapSource` with a shared helper; make the host-window/STA concerns the caller's
responsibility). Update TestApp's `review`/`diagnose-labels` to consume the moved control so the dev tools
keep working unchanged. Keep label-JSON shape identical (consumed by `optimize --labels`). **Verify:** TestApp
builds; `StarReviewTests` still pass; no behavior change to the dev tools.

### T6 — #1 + #2: labeling UX (mode-less click semantics + dashed label markers)
On the shared control (T5):
- **#1:** remove the three `Mark Missed/Should-Reject/Wrongly-Rejected` ToggleButtons and the `LabelPass`
  mode. A single left-click hit-tests **both** accepted and rejected detector boxes: click an **accepted** box
  → **should-reject** (false positive); click a **rejected** box → **wrongly-rejected** (false negative);
  click-drag over blank space → **missed** (should-have-detected). Toggling re-clicks remove the label. Keep
  keyboard help text in sync.
- **#2:** scroll-wheel zoom + right-button pan already work — keep them. Render the user-label overlays
  (missed / should-reject / wrongly-rejected) with a **dashed** stroke (`StrokeDashArray`) so labeled stars
  are visually distinct from the solid detector boxes.
- **Verify:** label-store toggle/hit-test unit tests; TestApp `review` still writes the same JSON.

### T7 — #9: wizard accept/cancel + auto-select optimized settings
Restructure the wizard Summary end-flow into an explicit **Accept / Cancel** decision: the changed-settings
table (already in `Optimization/DataTemplates.xaml:198-224`) is the review; **Accept** runs the existing
`Apply()` path AND sets `UseOptimizedSettings = true` (so the optimized snapshot is the active Simple-Mode
source — `OptionsDataTemplates.xaml:1114-1143` toggle); **Cancel** discards without touching options.
`ApplyOptimizedSettings` already sets `HasOptimizedSettings`; ensure Accept also selects it. **Verify:** VM
unit tests for accept (applies + selects) vs cancel (no mutation).

### T8 — #7: embedded review step in the wizard (all frames)
Insert a **Review** wizard step (new `WizardStep.Review`) between Summary and the final Accept, hosting the
shared labeling control (T5/T6) over **all** loaded frames (not just low/uncertain). The wizard already holds
the loaded `RunEvaluationData` per run; surface the per-frame detections + MTF-stretched bitmaps to the
control and persist labels in-memory (and to the run folder, matching `optimize --labels`). Content resolves
via the same `DataType` mechanism as T4. **Verify:** VM step-flow tests (Summary → Review → Accept), label
capture wired.

### T9 — #8: re-optimize with review feedback
After the review step, if **any** labels were made, prompt the user to **re-run optimization** with the
recall/precision label term active (the optimizer already supports labels — `OptimizationObjective`
`ComputeLabelScores`, `RunEvaluationData.ApplyLabelScores`). Re-run feeds the captured labels into a fresh
`StarDetectionOptimizer` pass, then returns to an updated Summary → Review → Accept. **Verify:** VM tests that
a labeled review enables the re-optimize prompt and that re-optimize threads the labels through.

---

## Part B — Detector / optimizer (AF bank `D:\Autofocus Bank`)

### T10 — F2: precision validation of the relaxed gates (verification; informs F3)
Confirm the new accepts the distortion/centering relaxation admits on moderately-defocused frames are real
stars, not junk. Use `review`/`diagnose-labels` (`--defocus-distortion --defocus-centering`, now a combined
gate per T1) on Panos (existing `Panos\labels`) + a 2nd setup; quantify near-focus false-positive rate vs the
donut recall gain; decide a safe default size-ref. **Deliverable:** a results note appended to
`docs/star-detection-optimization-wizard-results.md`. May involve interactive labeling by the user.
**→ Checkpoint with the user before F3.**

### T11 — F3: optimizer integration of the defocus gates, guarded by a precision penalty
**[Checkpoint before this objective change.]** Wire a **precision / false-positive penalty** into
`OptimizationObjective` (e.g. near-focus large-low-fill junk proxy or star-count-stability; data available per
the objective map) **before** adding the combined defocus-gate flag (and possibly size-ref) to the optimizer's
curated set. **Verify:** the labeled `optimize --labels` loop on Panos improves recall without tanking
precision; unlabeled path stays **bit-identical** (default-OFF gates); re-run the bank to confirm no
regression.

### T12 — F6: coarse-grid resolution vs bound range (optimizer tuning)
Widening curated bounds (Sensitivity 20→50, StarClipping→[0.25,10]) was ~a wash because fixed
`CoarseGridLevels=4` (`StarDetectionOptimizer.cs:28`) coarsens over the wider range. Scale grid levels with the
variable range, or use a finer compass step on the two high-impact axes. **Verify:** re-run the bound-pinned
setups (CWhite, mccomiskey, muggsie, timmer); J/σ_focus improve vs the wide-bounds result, deterministically.

### T13 — F4 + #5: structure-detection for dim "no-candidate" misses (research spike)
4/5 of the flagged Panos misses are true structure gaps (no candidate ever forms — donut flux lost to wavelet
residual subtraction, `StarDetector.cs:491`). Investigate a **defocus-aware layer-count increase** (#5:
`StructureLayers` is an early-cache param; more layers = larger surviving structures + larger post-wavelet blur
`StarDetector.cs:500`) and/or multi-scale handling. Prototype **opt-in / default-OFF**; verify via
`diagnose-labels` on Panos that previously NO-CANDIDATE boxes become candidates **without** exploding the
candidate count near focus. Exploratory — scope the spike, may split to its own PR.

---

## Order & checkpoints
T1 → T2 → T3 → T4 → T5 → T6 → T7 → T8 → T9 → **T10 (F2)** → **[checkpoint before F3]** → **T11 (F3)** → T12 →
T13 → **[checkpoint before PR]**.

## Global verification
Full `dotnet test` green; `TestApp optimize`/`review`/`diagnose-labels` exercised on `D:\Autofocus Bank`; all
new detector behavior default-OFF ⇒ **bit-identical** when disabled (diff star counts + J vs committed
baselines for an unchanged setup). PR to `develop`.
