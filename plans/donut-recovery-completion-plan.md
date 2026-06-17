# Donut recovery — completion plan (rim-bridging + TooFlat/Contamination relaxation)

**Status:** ready to execute (user approved "Both"). Branch: continue on `ghilios/donut-defocus-detection` (PR #66)
or a fresh `ghilios/donut-recovery-completion` off `develop` — decide at start. **Clear context (`/clear`) before executing.**

## Why (evidence)

`diagnose-labels --params optimized` on the mufti run (profile 4cf31cda), recovery engaged, attributes the labels:
- Real donuts still missed die at **TooFlat** (1) and **Contaminated** (2) — gates recovery does NOT relax. The big
  `(55×57)` donut: `StarMedian ≥ PeakResponse·Peak` at `StarDetector.cs:1479` (flat 0.957 > 0.8); optimizer raised
  PeakResponse 0.75→0.8, so the search moves away from flat donuts.
- 5/8 flagged **bright rim fragments** (7–11 px) still accepted — small/round/bright pass the ordinary gates; only a
  structural merge can fix this.

See `docs/donut-defocus-detection-analysis-design.md §8` for the full attribution table + design rationale.

## Invariants (do not violate)

- **Default OFF ⇒ bit-identical.** Every new feature is gated; with the defocus-recovery opt-in OFF, detection and the
  optimizer must be byte-for-byte unchanged (the bank-sweep regression check is the gate). All knobs default to the
  no-op value.
- **All new feature work is opt-in under the existing "Recover defocused / donut stars" toggle** (`DefocusRecovery` /
  `StarDetectionOptions.DefocusAwareGates` → `BuildStarDetectorParams`). No new top-level default-on behavior.
- TestApp harnesses stay **read-only on the profile**. Specs in `docs/`, plans in `plans/`. Commit only when asked;
  author/committer `322725+ghilios@users.noreply.github.com`.
- TDD: write the failing test first for each unit (per `superpowers:test-driven-development`).

---

## Part A — Defocus-aware TooFlat relaxation (recall; smallest, do first)

1. **Params** (`Interfaces/IStarDetector.cs`): add `StarDetectorParams.DefocusAwareFlatness` (bool, default false) and
   `DefocusFlatnessToleranceFactor` (double, default 1.0 = no relaxation). LATE params (not in `EarlyCacheKeyProperties`).
2. **Helper** (`StarDetector.cs`, beside `ComputeEffectiveMaxDistortion:1256`): `ComputeEffectivePeakResponse(p, d)` —
   when `!DefocusAwareFlatness` return `p.PeakResponse` (strict); else for large candidates (size `d` >
   `DefocusDistortionSizeReference`) raise the allowed median/peak ratio toward 1.0 by the tolerance factor (size-scaled,
   same shape as the distortion helper). A perfectly flat disk ⇒ median≈peak, so the relaxed bar admits hollow donuts.
3. **Gate** (`StarDetector.cs:1479`): replace `p.PeakResponse` with the effective value; when a star passes ONLY due to
   the relaxed bar, set `relaxationAdmitted = true` (mirror the distortion gate at `:1417/1424`) so `SDefocusPrecision`
   can penalize near-focus abuse.
4. **Options** (`StarDetectionOptions.cs` + `IStarDetectionOptions.cs`): `DefocusFlatnessToleranceFactor` option (the
   bool couples to `DefocusAwareGates` in `BuildStarDetectorParams`, like the other defocus gates). Reset in
   `ResetDefaults`/`DerivePresetSettings`.
5. **BuildStarDetectorParams** (`HocusFocusStarDetection.cs`): map `DefocusAwareFlatness = options.DefocusAwareGates`,
   `DefocusFlatnessToleranceFactor = options.DefocusFlatnessToleranceFactor`.
6. **UI** (`Resources/OptionsDataTemplates.xaml`): Advanced `UnitTextBox` + `DoubleRangeRule` + tooltip.
7. **Optimizer** (`OptimizerVariable.CreateCuratedSet`, opt-in branch): add `DefocusFlatnessToleranceFactor` Continuous
   var (e.g. [1.0, 3.0] step 0.25). Couple the gate bool onto `gatesWrite` (opt-in) like roundness.
8. **Tests:** effective-peak-response math (OFF == strict, ON relaxes only large); gate admits a synthetic flat donut
   only when ON; OFF-path bit-identical. `OptimizerVariableTests` count bump.

## Part B — Defocus-aware Contamination exemption (recall)

1. **Params:** `StarDetectorParams.DefocusAwareContamination` (bool, default false). LATE.
2. **Gate** (`StarDetector.cs`, the `RejectContaminatedStars` rejection path fed by `ComputeGradientContamination:819`):
   when `DefocusAwareContamination` and the candidate is large (size > `DefocusDistortionSizeReference`), do NOT reject
   on contamination — keep the star, still set `StarContaminationSuspected` for display, and set `relaxationAdmitted`.
   (Rationale: a defocused donut's structured rim trips the one-sided-excess test; size-gating limits the exemption to
   genuinely defocused blobs.)
3. **Options / BuildStarDetectorParams / curated set:** couple `DefocusAwareContamination = options.DefocusAwareGates`
   (no separate numeric knob — it's a pure exemption; the size threshold reuses `DefocusDistortionSizeReference`). No new
   UI control needed beyond the existing recovery toggle, but document it in the toggle tooltip.
4. **Tests:** a synthetic large contaminated donut is kept when ON and rejected when OFF; OFF-path bit-identical.

## Part C — Structure-stage rim-bridging (recall + fragment precision; biggest)

1. **Params:** `StarDetectorParams.DefocusRimBridging` (bool, default false) — **EARLY** (add to
   `EarlyCacheKeyProperties`; a move rebuilds the early context). Kernel radius derived from
   `DefocusDistortionSizeReference` (reuse; no new knob initially). Optionally a small integer `RimBridgingRadius`
   override later if needed.
2. **Closing step** (`StarDetector.cs`, between `Binarize:556` and `CollectStarCandidates:575`): when `DefocusRimBridging`,
   apply OpenCV morphological **closing** (`Cv2.MorphologyEx`, `MorphTypes.Close`, elliptical kernel radius r) to the
   binary `structureMap` in place. Guard: `if (!p.DefocusRimBridging) { /* unchanged */ }` ⇒ bit-identical. Dispose the
   kernel Mat.
3. **Metric:** `StarDetectorMetrics.RimBridged` (count of candidates whose bounding box exceeds the pre-close
   fragment scale, or simpler: log that bridging ran) — if added, **show it in the metrics panel** (`AutoFocus/DataTemplates.xaml`).
4. **Optimizer:** add `DefocusRimBridging` as a curated **EARLY** boolean in the opt-in set (like `DefocusAwareStructure`).
5. **Precision validation (REQUIRED before keeping it):** on the mufti sweep, confirm closing (a) merges the bright
   fragments at 2725 into their parent donut (the 5 `shouldReject` boxes stop being separately accepted) and recovers
   fragmented donuts, AND (b) does NOT merge genuine close pairs on the **near-focus** frame 2625 — star count + σ_focus
   at 2625 must not degrade. If over-merge appears, size-gate the closing (only close where local connected-component
   area already exceeds a defocus-size threshold) or fall back to the post-`CollectStarCandidates` **annulus-merge**
   (group small candidates on a common ring) per the original analysis fallback.
6. **Tests:** closing OFF ⇒ structure map identical (bit-identical detection); a synthetic donut (bright ring, dark hole)
   forms ONE candidate with closing ON vs. multiple arcs with it OFF.

## Part D — Verification (end-to-end, the acceptance gate)

1. **Label-level (mufti, profile 4cf31cda):** `optimize --runs <mufti> --defocus-recovery` then
   `diagnose-labels --params optimized --opt-results <out>`. PASS = the 3 gate-rejected donuts (TooFlat + 2 Contaminated)
   → ACCEPTED, and the 5 fragment `shouldReject` boxes → no longer ACCEPTED. Capture before/after table.
2. **Visual:** annotated 2325 / 2725 PNGs — fragments subsumed (no green on rim pieces), donuts recovered, the bright
   corner star handled. Render downscaled + corner crops (ImageMagick) to inspect.
3. **Bank regression (the hard gate):** `optimize --runs "D:\Autofocus Bank" --per-run` with the recovery opt-in OFF
   must be **bit-identical** to current `develop` (σ_focus + bestJ match < 5e-4 on all 16 runs). With opt-in ON, report
   the per-run fit trade so the doc records it (§6 precedent).
4. **Full suite:** `dotnet test ... -c Debug` green; every OFF-path bit-identity test passes.
5. Update `docs/donut-defocus-detection-analysis-design.md §8` with the before/after numbers; refresh
   `docs/star-detection-optimization-wizard-results.md` if the curated-var count changes.

## Sequencing

A → B (cheap LATE-gate relaxations; recover the labeled donuts, verify) → C (rim-bridging; precision + structural recall,
with its own precision-validation gate) → D (bank + suite). Land A/B first so there is a verified recall win even if C's
over-merge guard needs iteration. Keep each part a separate commit.

## Open risks

- **Rim-bridging over-merge near focus** — the main risk; mitigated by opt-in + size-gating + the 2625 validation gate +
  `SDefocusPrecision`. Annulus-merge is the fallback if closing is too blunt.
- **Curated-set dilution** — each new opt-in variable adds a search dimension on a fixed 400-eval budget (§6 cost ≈4
  runs from 4 vars). Keep new knobs minimal (TooFlat factor + bridging bool; contamination is a coupled exemption, no knob).
- **Profile mismatch** — harnesses load the dev profile, not mufti's; gate geometry is pixel-based so attribution holds.
