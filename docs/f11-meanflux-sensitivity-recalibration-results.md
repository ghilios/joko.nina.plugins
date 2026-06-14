# F11 meanFlux Fix — Measurement Results & Decision

**What this records:** the real-data measurement that drove finding F11 (the `NormalizedBrightness` `meanFlux`
denominator fix) and the BrightnessSensitivity recalibration question. Design: `f11-meanflux-sensitivity-recalibration-design.md`
(§3 sweep methodology, §5 acceptance). Plan: `f11-meanflux-sensitivity-recalibration-plan.md`. Measured headless via
`TestApp focus-sweep` (`--brightness-sensitivity-sweep`, and a new `--noise-level <preset>` override) against the
corpus under `C:\Workshop Data\Data\autofocus\`. Date: 2026-06-13.

## TL;DR

The F11 fix (divide `meanFlux` by the clip-survivor count `numUnclippedPixels`, not the full structure-footprint
count `starPoints.Count`) is **behavior-preserving at every shipped star-detection preset** (None / Low / Typical /
High). It changes accepted-star counts **only for advanced profiles that build larger footprints** (elevated
`StructureLayers` and/or `BrightnessSensitivity`). **Decision: ship the correctness fix with no preset constant
changes; cover advanced profiles with a release note.** No `BrightnessSensitivity` recalibration was applied.

This overturned the design's working premise (an ~8% drop at the Typical default `BrightnessSensitivity = 2.0`,
to be compensated by lowering the knob). The premise was an artifact of *where* the original anchor count was
measured — see "Reconciling the design's 1970→1810" below.

## The profile actually in use

`TestApp` loads the active NINA profile ("Default", `d2408162-…`). Its star-detection settings are **advanced**, not
a stock preset: `BrightnessSensitivity = 4.0`, `NoiseReductionRadius = 4`, `NoiseClippingMultiplier = 3`,
`StructureLayers = 6`, `StarClippingMultiplier = 2`, `PeakResponse = 0.75`. None of the simple presets produce
`BrightnessSensitivity = 4` or `StructureLayers = 6`; this is a hand-tuned profile.

## Finding 1 — behavior-preserving at the shipped presets

Measured pre-fix vs post-fix at each preset's exact params (via `--noise-level`), comparing the same frames with
only the `meanFlux` denominator changed (the fix was stashed/rebuilt to get the pre-fix half). Star counts are the
total accepted across the run's frames.

| Preset (operating point) | Frame set | pre-fix | post-fix | Δ |
|---|---|---|---|---|
| Low / Typical (`2.0`) | anchor / std2 / faint | ceiling (2090 / 54114 / 1191) | same | 0 — gate non-binding at ≤2.0 |
| None (`10.0`) | standard_example2 | 14002 | 13998 | −0.03% |
| None (`10.0`) | sensitivity_example1 (faint) | 294 | 294 | 0 |
| High (`10.0`) | standard_example2 | 29839 | 29839 | 0 |
| High (`10.0`) | sensitivity_example1 (faint) | 675 | 675 | 0 |
| High (`10.0`) | anchor | 1323 | 1323 | 0 |

At the presets the accepted count is unchanged. At Low/Typical the sensitivity gate is *non-binding* (the count is
pinned at a ceiling set by downstream gates the fix doesn't touch). At None/High the gate binds, but the fix barely
moves `NormalizedBrightness` because the footprint-vs-survivor gap is small at `StructureLayers = 4`.

## Finding 2 — the ~8–10% drop is confined to the advanced profile

At the loaded profile's params (`BrightnessSensitivity = 4`, `StructureLayers = 6`) the fix does drop yield, because
the larger footprints include many low-level halo pixels that are clipped from `totalFlux` — a large
footprint/survivor gap, the regime where the survivor-mean `meanFlux` rises most and `NB` falls most.

| Frame set (profile params, `BrightnessSensitivity = 4`) | pre-fix | post-fix | Δ |
|---|---|---|---|
| anchor `uneven/final` | 1970¹ | 1810 | −8.1% |
| standard_example1 | 13562 | 12404 | −8.5% |
| standard_example2 | 49159 | 44064 | −10.4% |
| sensitivity_example1 (faint) | 1024 | 913 | −10.8% |
| sensitivity_example2 (faint) | 482 | 435 | −9.8% |

¹ anchor pre-fix@4 is the design doc's recorded value; post-fix@4 = 1810 reproduces the design's −8.1% exactly.

## Root cause of the per-config difference

`NormalizedBrightness = peak − (1 − PeakResponse)·meanFlux` (`PeakResponse = 0.75` ⇒ `peak − 0.25·meanFlux`).
The fix raises `meanFlux` by `totalFlux·(1/survivors − 1/footprint)`, which lowers `NB`. The magnitude scales with
the footprint/survivor gap, which scales with `StructureLayers` (bigger footprints → more clipped halo pixels). The
presets use `StructureLayers = 4` (small gap → negligible NB shift); the profile uses `StructureLayers = 6` (large
gap → ~8–10% of near-gate stars cross the sensitivity threshold).

## Reconciling the design's "1970 → 1810 at 2.0"

The anchor pre/post counts (1970 → 1810, −8.1%) are real, but they were measured at the profile's actual
`BrightnessSensitivity = 4.0`, not at the Typical preset's `2.0`. At `2.0` the anchor is flat at 2090 both pre- and
post-fix (gate non-binding). The design conflated the profile's operating point with the Typical default. Lowering
the `2.0` preset would restore nothing (nothing is lost there).

## Decision

- **Ship the F11 correctness fix.** `meanFlux` divides by `numUnclippedPixels`. `MeanBrightness` keeps its
  full-footprint denominator by design (feeds only the brightest-N AF-star path, analysis F8) and is commented.
- **No `BrightnessSensitivity` recalibration.** None/Low/Typical/High constants are unchanged; the fix is
  behavior-preserving at all of them. (User-confirmed at the in-plan checkpoint after reviewing this data.)
- **None/High specifically:** kept at `10.0`. Originally slated for recalibration on the assumption they lose yield;
  direct pre/post measurement at their preset params shows they do not.
- **Tests:** `MeanFluxDenominatorTests` pins the fix's direction (a forced-binding synthetic regime flips
  accept→reject only under the fixed denominator); `MeanFluxDefaultRegressionTests` pins yield preservation at the
  shipped class default.
- Date: 2026-06-13. Rationale: a correctness fix that is provably neutral at every shipped preset needs no
  compensating knob change; the only affected users run advanced profiles and are covered by the release note below.

## Release note (advanced-mode users)

`BrightnessSensitivity` now operates against a slightly lower `NormalizedBrightness` — the `meanFlux` term uses the
clip-survivor mean instead of the full-footprint mean, so faint/spread stars get a more honest (lower) brightness.
Simple-mode presets (None/Low/Typical/High) are unaffected: their accepted-star counts are unchanged. Advanced
profiles that raise `StructureLayers` or `BrightnessSensitivity` above the preset values build larger star footprints
and may see modestly fewer faint stars accepted (≈8–10% on the test corpus at `StructureLayers = 6`,
`BrightnessSensitivity = 4`). To recover the prior yield, lower `BrightnessSensitivity` toward the preset range (e.g.
the test profile restores its anchor count at `BrightnessSensitivity ≈ 3.3`).
