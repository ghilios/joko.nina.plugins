# σ Consistency (F4, then F3) — Design

Step 3 of the star-detection accuracy analysis (`star-detection-hfr-autofocus-accuracy-analysis.md`,
§9 item 3, §10 row 3). Branch: `ghilios/sigma-consistency` → PR to `develop`.

## Problem

One noise σ — `KappaSigmaNoiseEstimate` on the **noise-reduced copy** of the image
(`StarDetector.cs:226`) — feeds every σ-based threshold in the detector, including thresholds applied
to the **sharp** image that is actually sampled. A Gaussian blur of the default kernel cuts white-noise
σ ~5×, so by default:

- The sensitivity gate `NB/σ > BrightnessSensitivity` (`StarDetector.cs:818`) at "10" is effectively
  ~2σ_sharp (**F4**).
- `MeasureStar`'s clip threshold τ = `StarClippingMultiplier·σ` (`StarDetector.cs:625`) is effectively
  ~0.4σ_sharp, and is **subtracted** from each pixel's flux rather than used as a gate (**F3**),
  biasing HFR low on stars with radial gradients: −0.47px (τ=0.02) to −1.90px (τ=0.20) measured on a
  synthetic Gaussian; relative bias rises 13%→45% as τ/peak goes 0.05→0.40.
- Both knobs' meanings silently depend on the noise-reduction settings.

F4's understated σ inflated HFR by up to +16.9px in a worst-case synthetic stress test. Magnitudes
are from step 1's evidence base (`MeasureStarBiasTests.cs`, PR #46).

## Decisions (made 2026-06-11, with ghilios)

1. **F4 compatibility: honest σ + recalibrated values, no migration code.** Use the measured-image σ
   everywhere it is consumed against the measured image; divide defaults and simple-mode preset
   values by the nominal mismatch factor so *effective* behavior at defaults is ~unchanged.
   Advanced-mode users' persisted values shift meaning — release note + tooltip update only.
2. **F3 semantics decided empirically inside this step**, via extended synthetic bias tests and a
   mid-plan decision gate (candidates and criteria in §5 below).
3. **Validation bar: synthetic + real.** Unit tests on synthetic ground truth gate the change, AND a
   before/after `TestApp focus-sweep` comparison on a real saved AF sequence is required PR evidence.
4. **σ source: measure on the measured image.** A second `KappaSigmaNoiseEstimate` directly on
   `srcImage`, not an analytic kernel-norm correction (which assumes white noise; real cameras have
   correlated noise after debayer/binning).

## 1. Architecture: two named σs

`DetectStarsAsync` produces two estimates:

| Name | Computed on | Consumers |
|---|---|---|
| `σ_structure` (existing) | noise-reduced copy (`StarDetector.cs:226`) | structure-map binarize threshold (`:260`) — only |
| `σ_measure` (new) | `srcImage` exactly as sampled | sensitivity gate (`:818`), `clipMargin` for star params + iterative centroid (`:1128`), `MeasureStar` τ (`:625`), `ModelPSF` noise floor (`:309`), contamination local-σ fallback (`:1114`) |

Mechanics:

- σ_measure runs in a second `Task.Run` alongside the existing estimate task, overlapping the wavelet
  phase. `srcImage` is read-only between preparation (`:203`) and `ScanStars` (`:301`), so the
  concurrent read is safe.
- **Skip condition:** the two images are identical unless
  `NoiseReductionRadius > 0 && !noiseReductionApplied`; when identical, reuse σ_structure (no second
  pass). This condition exactly captures all configuration permutations, including the subtle one
  where the structure copy gets hotpixel-filtered while `srcImage` does not
  (`StarDetector.cs:213-216`): in that case the images differ and σ_measure is computed on the
  unfiltered `srcImage` — which is precisely the image `MeasureStar` samples.
- When `StarMeasurementNoiseReductionEnabled` is on, `srcImage` *is* the blurred image; σ_measure
  equals σ_structure and the skip condition reuses it — measurement consistency holds automatically.
- Both σs appear in the existing trace lines and TestApp diagnostic outputs (`contamination_summary`,
  focus-sweep) so before/after comparisons can see them.

Out of scope: the structure-map binarize threshold stays on σ_structure (smoothed map, smoothed σ —
internally consistent); the ROI/AddOffset field-dropping issue is F9 (step 5).

## 2. Recalibration: per-preset, preserving today's effective behavior

Discovery during design: today's *effective* sensitivity already differs ~5× across
`Simple_NoiseLevel` presets, because the mismatch only exists on some paths
(`StarDetectionOptions.cs:64-84`):

| `Simple_NoiseLevel` | NR radius | Measurement NR | Mismatch today? |
|---|---|---|---|
| None | 0 | off | No — σ already honest |
| Low / Typical | 3 | off | **Yes, ~5×** |
| High | 5 | on | No — measured image is the blurred one |

One global constant cannot preserve every preset, so each path keeps its own current effective
behavior:

| Path | `BrightnessSensitivity` | `StarClippingMultiplier` |
|---|---|---|
| Simple None / High | 10 (unchanged) | 2.0 (unchanged) |
| Simple Low / Typical | 10 → **2.0** | 2.0 → **0.4** |
| Advanced defaults + `ResetDefaults()` (`StarDetectionOptions.cs:199-201`) | 10 → **2.0** | 2.0 → **0.4** |

- The WideRange / LongFocalLength deltas (`:93`, `:107`) scale by the same factor on the compensated
  path (−2 → −0.4), preserving step 2's intent ratio (PR #47). `ConfigureSimpleSettings` restructures
  so the noise-level branch supplies the baseline + delta scale and the focus-range/pixel-scale
  adjustments apply scaled deltas.
- **Constants are provisional.** The ~5× factor is the white-noise value for the default kernel; real
  correlated noise blurs less, so the true factor is somewhat smaller. The real-data focus-sweep
  check (§6) is the arbiter: if star counts at defaults shift materially, nudge the compensated
  values (e.g. 2.0 → 2.5) before merge.
- `HotpixelThresholdingEnabled` adds +1 to the radius (`:112`), slightly changing the per-image
  factor — irrelevant in production (σ is measured, not derived), and absorbed by the same
  focus-sweep tolerance for the preset constants.
- UI: tooltips for both knobs updated to "multiples of the background noise σ measured on the image
  being measured"; validation-rule ranges checked to admit the new defaults (both knobs already
  allow ≥ 0).
- Step 2's options tests (`StarDetectionOptionsTests.cs`: `..._IncreasesSensitivity` pair) update to
  the recalibrated scale, keeping their ratio assertions (preset < typical baseline).
- Release note for advanced-mode users: knob semantics are now honest σ multiples of the measured
  image; hand-tuned values should be divided by ~5 to keep prior behavior when noise reduction is
  enabled without measurement noise reduction.

## 3. F3: empirical τ-semantics phase with a decision gate

Because recalibration preserves effective τ ≈ 0.4σ_sharp, the F3 choice is a real trade-off at that
level:

- *Subtract* (status quo) biases HFR low on radial-gradient stars, worse when faint/defocused.
- *Gate-only at 0.4σ* removes that bias but admits ~34% of positive noise pixels at full value —
  potential faint-star HFR re-inflation.
- *Gate-only at 1–2σ* is clean and matches the centroid's convention (`StarDetector.cs:969`) but
  shifts HFR absolute values more.

**Phase plan:** extend `MeasureStarBiasTests` + the synthetic generator to compare four uniform
policies — `subtract@0.4σ`, `gate@0.4σ`, `gate@1.0σ`, `gate@2.0σ` — on clean and seeded-noise
Gaussian/disk/annulus shapes across several SNR levels, measuring (a) HFR bias vs analytic ground
truth and (b) HFR variance across noise seeds.

**Decision gate:** results + a recommended winner are presented to the user mid-execution; the chosen
policy is then implemented **uniformly** (the same `StarClippingMultiplier` semantics at all three
clip sites: star params, centroid, `MeasureStar`) — no second knob unless the evidence forces a
split, in which case the gate decides that too. Full-detection interaction effects (clip level →
NB/median/centroid → gates) are deliberately validated by the focus-sweep comparison rather than
unit tests.

## 4. Testing

- **σ-ratio sanity:** KappaSigma on synthetic white noise, blurred vs sharp, ratio ≈ the Gaussian
  kernel's L2 norm (tolerance band) — pins the mechanism the recalibration compensates.
- **Threading test:** detector-level synthetic field verifying σ_measure (not σ_structure) reaches
  the sensitivity gate / MeasureStar when the images differ, and that the skip-condition reuse holds
  when they don't (observable via trace/diagnostics or an internal seam — the implementation plan
  pins the mechanism).
- **Compensation guard:** detector-level synthetic star field at recalibrated defaults
  (radius 3, measurement NR off) detects ~the same faint-star set as the pre-change code at old
  defaults.
- **F3 phase tests** (§3) and the updated options tests (§2); full suite green
  (`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln`).

## 5. Real-data validation (required PR evidence)

Before/after `TestApp focus-sweep` on saved AF image sequences. The implementation plan's first task
explicitly asks the user for the AF run paths (1–3 runs), captures the baselines **before any
production change**, and reuses the same runs for the after-comparison:

- Star counts per focuser position comparable at defaults (recalibration worked).
- V-curve shape sane; fitted minimum within tolerance of before.
- Rejection-reason histograms compared (no new dominant rejection mode).

If counts shift materially, adjust the §2 constants and re-run before merge.

## 6. Rollout

- Branch `ghilios/sigma-consistency`; PR to `develop` (never push develop directly).
- Roadmap §10 housekeeping in the same PR: step 2 row → ✅ Done (PR #47, merged); step 3 row →
  🟡 In progress, pointing at `sigma-consistency-{design,plan}.md`.
- Commits attributed to `George Hilios <322725+ghilios@users.noreply.github.com>`.
