# F11 meanFlux Fix + BrightnessSensitivity Recalibration — Design

Step 6 of the star-detection accuracy analysis (`star-detection-hfr-autofocus-accuracy-analysis.md`,
§9 item 5 / F11, §10 row 6). Branch: `ghilios/f11-meanflux-sensitivity-recalibration` → PR to
`develop`. Mirrors the step-3 σ-consistency precedent (`sigma-consistency-{design,plan}.md`,
`sigma-consistency-f3-results.md`): an honest-definition fix paired with a per-preset knob
recalibration that preserves effective behavior, validated empirically with the TestApp diagnostics.

## Problem

`StarDetector.ComputeStarParameters` (`StarDetector.cs:1208`) computes

```
meanFlux = totalFlux / starPoints.Count
```

where the numerator `totalFlux` sums **only clip-survivors** (pixels with
`raw > background + StarClippingMultiplier·σ`, `:1173/1194`) while the denominator `starPoints.Count`
counts **all** structure-map pixels in the candidate. So `meanFlux` is understated by the
clip-survivor fraction `numUnclippedPixels / starPoints.Count` (**F11**).

`meanFlux` feeds exactly one consumer — the per-candidate sensitivity gate, via

```
NormalizedBrightness = peak − (1 − PeakResponse)·meanFlux          (StarDetector.cs:1253)
sensitivity          = NormalizedBrightness / σ_measure            (StarDetector.cs:853)
reject if sensitivity ≤ BrightnessSensitivity                      (StarDetector.cs:854)
```

With `PeakResponse = 0.75` (the post-step-5 default, `StarDetectionOptions.cs:124`),
`NB = peak − 0.25·meanFlux`. An understated `meanFlux` **inflates** NB, which **loosens** the gate —
disproportionately for faint/spread stars, whose survivor fraction is lowest (many structure pixels
fall below the clip margin). The `BrightnessSensitivity` knob was therefore empirically tuned against
the inflated NB.

The denominator fix (`/numUnclippedPixels`) raises `meanFlux`, lowers NB, and tightens the gate.
**Measured baseline impact: accepted stars dropped 8.1% (1970 → 1810)** on
`C:\Workshop Data\Data\autofocus\uneven\final\11_Frame00_BitDepth16_Bayered0_Focuser56457.xisf` with
no compensating knob change. This step applies the fix and recalibrates `BrightnessSensitivity` per
simple-mode preset to restore the intended faint-star yield.

### Why this is not a clean rescale (key difference from step 3)

In step 3 the σ error was a **uniform** ~5× multiplicative factor, so dividing the knob by ~5
restored the *identical* star set. Here the NB shift is **per-star** — each star's NB drops by
`0.25·totalFlux·(1/numUnclipped − 1/count)`, which depends on that star's survivor fraction. No single
new `BrightnessSensitivity` restores the identical membership. The recalibration target is therefore
the **aggregate operating point** (accepted-star count within tolerance, V-curve and rejection
histograms sane), **not** identical membership — the same acceptance bar step 3 actually used.

## Decisions (made 2026-06-13, with ghilios)

1. **Scope the fix to `NormalizedBrightness`.** Change `meanFlux` only. `MeanBrightness =
   TotalFlux / PixelCount` (`StarDetector.cs:877`, `PixelCount = starPoints.Count`, `:1256`) has the
   identical survivor/all-pixels mismatch but is left unchanged — it is a defensible per-footprint
   surface-brightness measure feeding only the rarely-used "brightest N AF stars" path (F8,
   won't-fix). The asymmetry is documented with a code comment so it doesn't read as an oversight.
2. **Calibration method: empirical yield-matching sweep (mirror step 3), with the pre/post sweep
   overlay as a supporting diagnostic.** Find the new constants by sweeping `BrightnessSensitivity`
   on real data and matching the pre-fix accepted count; do not attempt a closed-form NB-shift offset
   (it is per-star and would need empirical confirmation anyway). The pre-fix-vs-post-fix
   count-vs-sensitivity overlay is the diagnostic that explains and de-risks the chosen constant.
3. **Acceptance bar: aggregate count, not membership.** Restore accepted-star count to within
   tolerance of pre-fix at defaults, with V-curve shape and rejection histograms sane.
4. **Validation bar: synthetic + real.** Unit tests on synthetic ground truth gate the change, AND a
   before/after `TestApp focus-sweep` comparison on real saved AF sequences is required PR evidence.
5. **Constants are provisional in this design.** The exact recalibrated `BrightnessSensitivity`
   values are determined by the §5 measurement during implementation (the step-3 §2 pattern). Pinning
   them now would require building and running the fixed binary, which is implementation work.

## 1. The code change (F11 fix)

`StarDetector.ComputeStarParameters`, `StarDetector.cs:1208`:

```csharp
// before
var meanFlux = totalFlux / starPoints.Count;
// after
var meanFlux = totalFlux / numUnclippedPixels; // mean over clip-survivors (the pixels summed into totalFlux)
```

- `numUnclippedPixels` is already computed (`:1165`, incremented at `:1179`) and is guaranteed ≥ 2 at
  this line — the `numUnclippedPixels == 1` degenerate guard at `:1184` returns `null` first. No
  divide-by-zero risk.
- `meanFlux` has exactly one consumer (`NormalizedBrightness`, `:1253`); confirmed by grep there is a
  single NB computation site in the codebase. The ROI/`AddOffset` path recomputes detection per crop
  (it does not copy NB), so no other site is affected.
- A comment at the `MeanBrightness` assignment (`:877`) documents that its all-footprint denominator
  is intentional and distinct from `meanFlux`'s survivor denominator (per decision 1).

## 2. Recalibration mechanics

`ConfigureSimpleSettings` (`StarDetectionOptions.cs:58-136`) today:

| Path | `BrightnessSensitivity` (effective) |
|---|---|
| Simple None / High (`sensitivityScale = 1.0`) | `10.0` |
| Simple Low / Typical (`sensitivityScale = 0.2`) | `2.0` |
| WideRange / LongFocalLength delta | `− 2.0·sensitivityScale` (−2.0 None/High, −0.4 Low/Typical) |
| Advanced default & `ResetDefaults()` (`:214`) | `2.0` |

The F11 fix lowers NB, so the recalibrated constants are **lower**, found empirically (§5). The
existing per-regime baseline + scaled-delta structure is preserved; the recalibration adjusts the
baseline constants (and re-checks the deltas), keeping the WideRange / LongFocalLength
"more-sensitive-than-baseline" **intent ratio** (step 2 / PR #47).

Per-regime expectations to verify against the data:

- **Low / Typical & advanced default (effective 2.0)** — the primary target; the −8.1% baseline was
  measured here. Gets a new, lower constant `K_typ`.
- **None (effective 10.0)** — bright 10σ threshold ⇒ marginal candidates are bright ⇒ high survivor
  fraction ⇒ small ΔNB. Expected to barely move; re-measured to confirm, adjusted only if the count
  shifts materially.
- **High (effective 10.0, measurement noise reduction ON)** — NB is computed on the blurred `srcImage`
  (different survivor fractions), so it gets its own measurement pass rather than inheriting None's
  result.

Constants land in `ConfigureSimpleSettings` (`:97`, `:102`, `:116`), the advanced default
(`InitializeOptions` `:167`), and `ResetDefaults` (`:214`), kept mutually consistent.

## 3. Tooling: BrightnessSensitivity sweep on the focus-sweep runner

`FocusSweepDiagnosticRunner` already builds params via `HocusFocusStarDetection.BuildStarDetectorParams`
(the single options→params source of truth) and reports per-position accepted counts plus the full
rejection histogram including `LowSensitivity` (the metric the gate increments). Add:

- `--brightness-sensitivity <v>` — single override; sets `baseParams.Sensitivity = v` after the base
  params are built (from the profile, or `--default-params`), holding all other params fixed.
- `--brightness-sensitivity-sweep <a,b,step>` — runs the above for each value, emitting
  `brightness_sweep.csv` (`sensitivity, FocuserPosition, StarCount, LowSensitivity, MedianHFR`) plus a
  summary line per value.

This one flag serves both measurements: the single-frame `uneven/final` parses as a one-position run
(`ParseAfRun` picks up its lone `11_Frame00…` file), giving the **anchor** read-off (the value
restoring ~1970), and the four full runs give the **multi-position validation** sweeps.

The contamination runner's existing `--sensitivity`/`--sensitivity-sweep` map to
*ContaminationSensitivity* (a different knob) and are left untouched; the BrightnessSensitivity sweep
belongs on focus-sweep because the operating metric is per-position accepted-star yield and the
V-curve.

**Diagnostic (approach B, realized for free):** run the sweep on the pre-fix binary and the post-fix
binary and overlay count-vs-sensitivity. The horizontal gap at a fixed count is the required
Δsensitivity; the local slope shows whether the chosen constant sits on a cliff (robustness check).
No new detector instrumentation is needed — the sweep curve *is* the ΔNB-distribution diagnostic. The
plan captures the pre-fix sweep before applying the §1 change.

## 4. Testing

- **NB-definition test** (detector-level synthetic, in the spirit of `SigmaConsistencyDetectorTests`):
  a synthetic star whose survivor fraction is meaningfully < 1, asserting `NormalizedBrightness`
  matches `peak − (1 − PeakResponse)·(totalFlux / numUnclipped)` and that acceptance moves in the
  expected direction (a star accepted under the old denominator at a borderline sensitivity is
  rejected under the new one at the same knob value, and re-accepted at the recalibrated value).
- **Compensation guard:** a synthetic faint-star field at the recalibrated defaults detects ~the same
  count as the pre-change code at the old defaults — the "preserve effective behavior" pin.
- **Update existing pins:**
  - `StarDetectionOptionsTests` — `SimpleMode_PixelScaleLongFocalLength_IncreasesSensitivity`
    (`:157-159`) and `SimpleMode_FocusRangeWideRange_IncreasesSensitivity` (`:189-191`) assert
    `typical == 2.0`, `wide/longFl == 1.6`; update to the new constants while keeping the
    ordering/ratio assertions (`< typical`). `SimpleMode_NoiseLevelNone_Keeps...` updated if None
    moves.
  - `HocusFocusReportTests` (`:101`, `p.Sensitivity == 2.0`) → new advanced default.
  - **`SigmaConsistencyDetectorTests`** has NB-magnitude assertions (e.g. "NormalizedBrightness ≈
    6.4–9.0 × σ̂_blurred", `:28`) that the meanFlux change can shift — these are re-derived and
    re-pinned against the survivor-denominator NB, not blindly bumped.
- Full suite green (`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`).

## 5. Real-data validation (required PR evidence)

Corpus (all under `C:\Workshop Data\Data\autofocus\`):

- **Recalibration anchor (single frame):** `uneven\final\11_Frame00…Focuser56457.xisf` — defines the
  1970 → 1810 number. The BrightnessSensitivity sweep here picks `K_typ` (the value restoring ~1970 at
  the Typical/default preset).
- **Validation sweeps (9-position each):** `standard_example1`, `standard_example2` (dense;
  `standard_example2` surfaced step 3's `TooLowHFR` cliff), `sensitivity_example1`,
  `sensitivity_example2` (faint-field / sensitivity-stress — the regime the knob most directly
  governs).

Procedure: capture pre-fix baselines first (the diagnostic overlay), apply §1 + §2, re-run.
Acceptance:

- Anchor accepted count back to ~1970 (within tolerance).
- Per-position counts comparable at defaults across the four sweeps.
- V-curve fitted minimum within tolerance of before.
- Rejection histograms: `LowSensitivity` returns to ~baseline **without** the re-admitted stars
  piling into `TooFlat` / contamination / `TooLowHFR` (the count-not-membership quality guard — re-won
  stars must be real, not junk that merely fails a later gate).

If counts shift materially, adjust the §2 constants and re-run before merge.

## 6. UI, docs, release note

- `BrightnessSensitivity`'s validation-rule range already admits values below 2.0 (allows ≥ 0); no
  rule change. The tooltip meaning ("multiples of the measured-image background σ") is unchanged —
  the gate is still `NB/σ` — so no tooltip edit is expected; re-checked during implementation.
- Release note for advanced-mode users: `BrightnessSensitivity` now operates against a slightly lower
  (survivor-mean) `NormalizedBrightness`; hand-tuned values may want lowering by ~the recalibration
  factor to preserve prior yield (magnitude from §5).
- Update `star-detection-hfr-autofocus-accuracy-analysis.md` §10 row 6 status in the same PR.

## 7. Rollout & dependency

- Branch `ghilios/f11-meanflux-sensitivity-recalibration`; PR to `develop` (never push `develop`
  directly). Commits attributed to `George Hilios <322725+ghilios@users.noreply.github.com>`.
- **Base on current `develop`** (PR #51 / step 5 merged). The recalibration must be measured against
  the post-step-5 `PeakResponse = 0.75`, since `(1 − PeakResponse) = 0.25` is the multiplier on
  `meanFlux` in NB; measuring against the older 0.6 would mis-scale the constants. The honest
  `σ_measure` from step 3 (PR #48) is also assumed present, since it is the gate denominator.
- Roadmap §10 housekeeping in the same PR: step 6 row → ✅ Done with the measured before/after numbers
  and the chosen per-preset constants recorded (mirroring how step 3 recorded its results).
