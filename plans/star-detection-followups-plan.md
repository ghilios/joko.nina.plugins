# Star Detection Optimization — Follow-ups Plan

**Context / rationale:** `docs/star-detection-optimization-wizard-design.md` ("Follow-ups" section) and
`docs/star-detection-optimization-wizard-results.md` (cross-setup + Panos donut diagnosis). These are the
deferred items from the wizard branch (PR #55: `ghilios/star-detection-optimization-wizard`).

**Branch:** create `ghilios/star-detection-followups` **off `ghilios/star-detection-optimization-wizard`**
(the follow-ups depend on the wizard/perf/defocus-gate code, which is in PR #55, not yet on `develop`). Once
PR #55 merges, rebase onto `develop`. Never push `develop`; PR to merge.
**Commit identity:** `George Hilios <322725+ghilios@users.noreply.github.com>` (author + committer), per CLAUDE.md.
**After each task:** `cmd.exe /c "dotnet build Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"`
(+ `TestApp\TestApp.csproj`) and `dotnet test ... -c Debug`; fix root causes before continuing.
CLAUDE.md rules apply (options need UI + tooltips; new `StarDetectorMetrics` fields need the metrics panel).
The user will supply the AF-runs folder `D:\Autofocus Bank` for verification items (F2/F3/F4).

The two defocus-aware gates and the harnesses already exist (see CLAUDE.md "Star Detection Optimizer
harnesses" + the `DefocusAware*` options). Build on them; keep new detector behavior **opt-in / default-OFF**
so detection stays bit-identical when disabled.

---

## F1 — Expose the distortion size reference as a tunable (quick; → 9/9 donut recovery)
Surface `StarDetectorParams.DefocusDistortionSizeReference` (and consider `DefocusDistortionMinFactor`,
`DefocusCenteringToleranceFactor`) as **Advanced** `StarDetectionOptions` (UnitTextBox + DoubleRangeRule +
tooltip per CLAUDE.md), wire through `BuildStarDetectorParams` + `ResetDefaults`, default unchanged (30 / 0.25
/ 2.0). **Verify:** persistence round-trip test; with the gates ON and a lower size-ref, `TestApp diagnose-labels`
on Panos 44396 recovers 9/9 (it already does at `--defocus-size-ref 20`).

## F2 — Precision validation of the relaxed gates (verification; informs F3)
Confirm the *new* accepts the distortion relaxation admits on moderately-defocused frames are real stars, not
junk. Use `review`/`diagnose-labels` (`--defocus-distortion --defocus-centering`) on Panos 40396/42396 (and a
2nd setup) to label false positives; quantify the near-focus false-positive rate vs the donut recall gain.
Decide a safe default size-ref. **Deliverable:** a short results note appended to
`docs/star-detection-optimization-wizard-results.md`. (Needs the AF-runs folder + interactive review.)
**Checkpoint with the user before F3.**

## F3 — Optimizer integration of the defocus gates, guarded by a precision penalty
Add the gate flags (and possibly the size-ref) to the optimizer's curated set **only after** wiring a
**precision / false-positive penalty** into `OptimizationObjective` (else the search cranks recall and floods
near-focus). Likely a near-focus junk proxy (e.g. fraction of accepted stars that are large + low-fill on the
near-focus frames, or a per-frame star-count-stability term). **Verify:** the labeled `optimize --labels` loop
on Panos now improves recall *without* tanking precision; unlabeled path stays bit-identical (default-OFF
gates); re-run the 14-run bank to confirm no regression. **Checkpoint before this objective change.**

## F4 — Structure-detection for dim "no-candidate" misses (research)
4/5 of the flagged Panos misses were true structure gaps (the detector never forms a candidate). Investigate
multi-scale / large-structure / sensitivity handling for dim, bloated defocused stars (e.g. `StructureLayers`
/ wavelet behavior at large defocus). Prototype opt-in/default-OFF; verify via `diagnose-labels` that
previously NO-CANDIDATE boxes become candidates without exploding the candidate count near focus. Exploratory
— scope a spike first.

## F5 — Cosmetic: joint-mode `optimized_settings.json` out-dir copy
In joint mode the `--out` copy of `optimized_settings.json` reflects the *last* run's recommended step (the
per-run source-folder copies are correct). Either write the joint winner's representative step, or omit the
out-dir copy in joint mode. Small fix in `OptimizationDiagnosticRunner`.

## F6 — Coarse-grid resolution vs bound range (optimizer tuning)
Widening the curated bounds (Sensitivity 20→50, StarClipping→[0.25,10]) was ~a wash because the fixed
`CoarseGridLevels=4` coarsens over a wider range. Consider scaling grid levels with the variable range, or a
finer compass step on the two high-impact axes. **Verify:** re-run the bound-pinned setups (CWhite, mccomiskey,
muggsie, timmer) and confirm J/σ_focus improve vs the current wide-bounds result, deterministically.

---

## Order & checkpoints
F1 → F5 → F2 (verification, **checkpoint**) → F3 (**checkpoint before the objective change**) → F6 → F4 (spike).
F1/F5 are quick + independent; F2 gates F3; F4 is exploratory and can be deferred or split to its own PR.

## Global verification
Full `dotnet test` green; `TestApp optimize`/`review`/`diagnose-labels` exercised on `D:\Autofocus Bank`;
all new detector behavior default-OFF ⇒ bit-identical when disabled (diff star counts + J vs committed
baselines for any setup). PR to `develop` (or to the wizard branch if PR #55 is still open).
