# Synthetic Autofocus Bank — Design

## Problem

The real AF bank at `D:\Autofocus Bank` is the only evidence base we have for two very different
questions, and it answers neither cleanly.

**1. Detector recall/precision has no exact ground truth.** The golden star sets built by
`tools/golden/` (SNR reference detector → montage → LLM QA, see `.claude/docs/golden-star-set.md`)
are a genuinely independent reference, but they are a *sample* of the true star field, not the whole
of it. A detection that matches no golden box may be a false positive or may be a real star the
reference pipeline never nominated. So `bank-verify` precision is a **lower bound**, recall is
measured only over the tiers the QA pass actually examined, and the absolute numbers can only be
compared against themselves across runs — never against 1.0.

**2. Optimizer recommendations have never been checked for convergence.** The wizard emits four
bootstrap recommendations — step size (`StepSizeRecommender`), exposure (`ExposureRecommender`),
detection binning (`DetectionBinningResolver.RecommendFromHfr`) and, implicitly, whether donut-aware
detection should be on. Each has been reasoned about and unit-tested in isolation, but nobody has
ever asked the question that actually matters to a user: *if I start with the wrong settings, do
these recommendations walk me toward the right ones, and do they stop when they get there?* On the
real bank that question is unanswerable, because there is no "right answer" to walk toward — the
capture parameters of `cwhite_2026` are whatever the imager happened to use.

A synthetic bank answers both. Every star position, flux and HFR is known *by construction*, so
precision becomes exact rather than a lower bound. And each dataset can declare its own
expected-optimal bootstrap parameters, so a driver can deliberately start wrong and measure whether
the recommenders converge.

**Scope discipline.** This work builds the *instrument*, not the fixes. Every defect the instrument
finds is written up as a `docs/followups.md` entry and left alone. The deliverable is a regression
baseline plus a followups list, not a behavior change.

## Why the in-repo simulator, and why it is trustworthy here

`CameraSimulator/` is already a physics-grade renderer, built and validated for the interactive
simulator camera (`docs/camera-simulator-realism-and-performance-plan.md`,
`docs/synthetic-camera-design.md`). The pieces this design depends on all exist and are unit-tested:

| Component | What it gives us | Location |
|---|---|---|
| `DefocusModel` | closed-form HFR hyperbola `HFR(Δ) = √(HFR_min² + (κ·Δ)²)`, plus annulus inner/outer radii and W20 | `Rendering/DefocusModel.cs` |
| `PsfKernelGenerator` / `PsfKernel` | annulus ⊛ Gaussian donut PSF, 4×4 sub-pixel phases, carries `MeasuredHfrPixels` + `AnalyticHfrPixels` | `Rendering/PsfKernelGenerator.cs` |
| `RadiometryCalculator` | magnitude → electrons through aperture, obstruction, bandwidth, QE, throughput; sky and dark rates | `Rendering/RadiometryCalculator.cs` |
| `NoiseGenerator` + `SeedMixer` | seeded, stripe-parallel, core-count-independent shot/read/dark noise | `Rendering/NoiseGenerator.cs`, `FrameDeveloper.cs` |
| `AstapCatalogReader` | the real Gaia G18 catalog installed at `C:\Program Files\astap` | `Catalog/AstapCatalogReader.cs` |
| `StarFieldCompositor` | tan projection, per-star local defocus from the aberration surface, quantized kernel cache, stripe compositing | `Rendering/StarFieldCompositor.cs` |

The critical property is that **the render matches `DefocusModel` by construction** — the kernel for
a given defocus is generated *from* the model, so `HFR_measured(Δ)` is not an independent
measurement we hope agrees with theory, it is the model evaluated and then rasterized. That makes
the expected HFR curve exact up to pixelization and noise, which is precisely what a fixed-point
convergence test needs.

Using real catalog stars (rather than a synthetic Poisson field) matters too: it gives realistic
crowding, realistic magnitude distributions, and per-pointing density variation, all of which are
what actually break detectors.

### What the simulator does *not* have (built here)

- **No truth export.** `StarFieldCompositor.BuildStampJobs` computes every per-star quantity we need
  — pixel coordinates, magnitude, local defocus, quantized kernel, flux — and then discards all but
  `(Cx, Cy, Kernel, Flux)` into a `StampJob`. Workstream **G1** adds a truth sink.
- **No FITS writer.** The compositor returns `ushort[]`. `ExportLinearRunner.WriteMonoFits16` writes
  a deliberately minimal 8-card header (SIMPLE/BITPIX/NAXIS/NAXIS1/NAXIS2/BZERO/BSCALE/END) with no
  XPIXSZ/FOCALLEN/EXPTIME — not enough for the bank. Workstream **G3** writes a full one.
- **No CLI.** Workstream **G6**.
- **No peak-intensity accessor on `PsfKernel`**, and no inner-radius field — both needed for the SNR
  math that assigns golden tiers. Added in G1.

### Verification corrections carried into this design

Code verification before implementation turned up four places where the original sketch was wrong;
they are corrected throughout this document:

1. The PSF kernel radius cap is **512** (`PsfKernelGenerator.MaxKernelRadius`, throws above it). The
   **500** figure is `StarFieldCompositor.MaxSafeKernelRadius`, a separate, lower clamp applied when
   computing the worst-case margin. The kernel-cap guard sweeps against the compositor's 500.
2. `PsfKernel` exposes `OuterRadiusPixels` but **no inner radius and no peak accessor at all**. Both
   are new API in G1, not existing surface.
3. "Max absolute defocus" exists only as `private static StarFieldCompositor.MaxAbsDefocusMicrons`.
   G1 lifts it to `internal static` so the guard and the renderer share one definition.
4. `GoldenFrame.SchemaVersion` defaults to **1** in C# while the Python writer
   (`tools/golden/build_goldens.py`) emits **2** with a `method` field that has no C# counterpart.
   G5 adds `method` to the C# record and writes `schemaVersion: 2`, aligning the two.

## Bank contract (what the harness requires of the generated files)

Everything here is dictated by existing consumers; the generator conforms rather than the reverse.

- **Discovery** (`TestApp/OptimizationRunDiscovery.cs`): a run is an `^attempt\d+$` directory found
  within `DefaultMaxDepth = 4` of the root, containing ≥ `MinPositionsForFit = 3` distinct focuser
  positions.
- **Frame filename** is the load-bearing interface:
  `^(?<IMAGE_INDEX>\d+)_Frame(?<FRAME_NUMBER>\d+)_BitDepth(?<BITDEPTH>\d+)_Bayered(?<BAYERED>\d)_Focuser(?<FOCUSER>\d+)(_HFR…)?$`.
  Discovery reads only `FOCUSER`; `SavedAutoFocusImage.TryParseFileName` (reached via
  `DiagnosticUtil.IsBayeredFrameFileName`) reads `BAYERED` and `BITDEPTH`. Detection normalizes by
  `1 << BitDepth` from the **filename** token, so the pixel data must genuinely fill 16 bits — hence
  the left-shift for 12/14-bit sensors (below).
- **Headers that reach `ImageMetaData`** via NINA's `FITS.Load`: `XPIXSZ` → `Camera.PixelSize`,
  `FOCALLEN` → `Telescope.FocalLength`, `XBINNING` → `Camera.BinX`, `EXPTIME` → `Image.ExposureTime`.
  These four are what `HarnessSettingsStore.PixelScaleForFrame` and the `optimize` runner's
  `CapturedExposureSeconds` consume. Because NINA's parser semantics are not verifiable by reading
  our code, the generator **self-verifies** by reloading frame 0 of every dataset through `FITS.Load`
  and hard-asserting all four land (risk R1).
- **`run_meta.json`** (written by `BankDonutMetaRunner` on the real bank) carries `donutAware` plus a
  reason and signal block. The synthetic generator writes it *apriori* from truth instead of
  measuring it.
- **Golden sidecars** are `<frame>.fits.golden.json` (`TestApp/GoldenStarSet.cs`), boxes as top-left
  `x,y` + `w,h`, tiers `high`/`medium`/`low` in `stars[]`, and a separate `unresolved[]` list that is
  excluded from false-positive counting by `GoldenMatch.ExcludeUnresolved`.
- **`golden eval --match centroid` requires an explicit `--match-radius`** — its default is `0.0`,
  and `GoldenGeometry` qualifies a centroid match only on `radius > 0 && dist <= radius`, so the
  default silently matches nothing. `bank-verify` has its own default of `12.0`.
- **`bank-clean`** walks each *run* folder recursively and keeps a fixed list (labels, `*.golden.json`,
  `run_meta.json`, `autofocus_report_Region*.json`, `*.linear.fits`, AF sweep frames, plus
  unrecognized files by default). Anything at the *dataset* root is never visited — which is why
  `synthetic_meta.json` lives there.

## Workstream G — the generator

### G1. Truth seam

New `CameraSimulator/Rendering/StarTruth.cs`:

```
record StarTruth {
  double CxPixels, CyPixels;          // sub-pixel, full-frame, native (pre-binning)
  double RaDegrees, DecDegrees;
  double MagnitudeV;
  double FluxElectrons;               // total developed electrons for the star
  double LocalDefocusMicrons;         // from the aberration surface at this star
  double QuantizedDefocusMicrons;     // what the cached kernel actually represents
  double AnalyticHfrPixels, MeasuredHfrPixels;
  double OuterRadiusPixels, InnerRadiusPixels;
  double KernelSupportRadiusPixels;
  double KernelPeakFraction;          // max phase-kernel sample; flux × this = peak electrons
}
```

`StarFieldCompositor` gains `public ushort[] Render(RenderRequest request, ICollection<StarTruth> truthSink, CancellationToken token)`
**on the concrete class only** — `IStarFieldCompositor` is untouched, so the camera seam and its MEF
composition are unaffected. The existing single-argument `Render` delegates with a null sink.
`BuildStampJobs` appends one `StarTruth` per *accepted* job, which naturally includes the off-frame
wing-spill stars kept by the `psfMargin` from `WorstCaseKernelRadius` (those matter: their arcs land
on the sensor and the detector will see them).

The **null-sink path must remain byte-identical**, and that is asserted by test G8a rather than
assumed.

Supporting API added in the same task:
- `PsfKernel.MaxPeak` and `PsfKernel.PhasePeak(int phaseX, int phaseY)` — for saturation and peak-SNR.
- `PsfKernel.InnerRadiusPixels` — currently computed inside `GenerateAnalytic` and thrown away.
- `internal static double StarFieldCompositor.MaxAbsDefocusMicrons(DefocusModel)` — lifted from
  private, so the kernel-cap guard and the renderer's own `QuantizeLevel` clamp cannot drift apart.

### G2. IMX585 sensor

`SonySensorModel.IMX585` in `Interfaces/ICameraSimulatorOptions.cs` plus a `SensorRegistry` row:

```
new SensorDefinition(SonySensorModel.IMX585, "IMX585", 3840, 2160, 2.90, 12, 40000.0,
    qe, 3.3, 252, 1.0, 0.8, 460, 0.003, 0.0),
```

(positional order: model, name, width, height, pixel µm, bit depth, full well e⁻, QE curve, RN@gain0,
HCG threshold, RN@HCG, RN min, max gain, dark e⁻/px/s at reference, reference °C.)

Chosen over IMX678 because 2.90 µm produces **both** sampling regimes at real focal lengths — badly
undersampled at 2000 mm native, comfortably oversampled at 2000 mm binned 2× — with 4× the full well,
which keeps bright stars out of saturation on the long exposures the narrowband datasets need. The
registry ctor enforces `RN_min < RN_HCG < RN_gain0` and `maxGain > hcgThreshold`; the chosen numbers
satisfy both. `SensorRegistryTests` has a count assertion that self-updates from the enum plus two
`[TestCase]` tables (geometry, e⁻/ADU at gain 0) that need explicit new rows, and a QE-curve identity
assertion that forces reuse of `QeCurve.SonyBsiVisible`.

R3: these are datasheet-approximate. They are pinned verbatim into `synthetic_meta.json` so a future
correction is detectable as a metadata diff rather than a silent baseline shift.

### G3. FITS writer

New `TestApp/MonoFits16Writer.cs`, pure: takes `ushort[]` + width/height + an ordered `FitsCard` list,
emits BZERO-offset big-endian `int16` in 2880-byte blocks — the same on-disk layout as
`ExportLinearRunner.WriteMonoFits16`, which is refactored into a thin adapter over it so there is one
implementation.

Cards, in order: `SIMPLE`, `BITPIX`, `NAXIS`, `NAXIS1`, `NAXIS2`, `BZERO`, `BSCALE`, `XBINNING`,
`YBINNING`, `XPIXSZ`, `YPIXSZ`, `FOCALLEN`, `EXPTIME`, `GAIN`, `FOCUSPOS`, `INSTRUME`, `END`.

`XPIXSZ` follows the **NINA convention**: physical pixel size × capture binning (so a 2.90 µm sensor
captured at bin 2 reports 5.80). This matters because `PixelScaleForFrame` *also* multiplies by
`Camera.BinX`; writing the physical size instead would double-count.

**Bit depth**: 12- and 14-bit sensors are left-shifted by `16 − bits` **after** `BinFrame` (binning
sums developed ADU and clips at `(1 << bitDepth) − 1`, so shifting first would clip wrongly). This
makes the data genuinely fill 0–65535, matching the `_BitDepth16_` filename token that detection
normalizes by.

### G4. Spec and derivations

`TestApp/SynthBank/SynthBankSpec.cs` (POCOs) + `SynthBankDerivations.cs` (pure math). Per dataset:

- `HFR_min`, `κ` — straight from `DefocusModel`.
- **Step size**: `step* = √8 · HFR_eff / (κ · 3.5)`, where `HFR_eff = max(HFR_min, 0.70 px)`.
  Derivation: `StepSizeRecommender` targets the offset where HFR reaches `3 × HFR_min`; on the
  hyperbola that is at `κ·Δ = √8 · HFR_min`, and the recommender then divides the resulting half-width
  by `PointsPerSide = 3.5`. The `0.70 px` floor accounts for the fact that a sub-pixel PSF cannot
  actually measure below roughly that HFR, so the *measured* curve's minimum is floored even when the
  optical one is not. **R2: the floor is an estimate**, relevant only to D01–D03; metadata carries
  both `hfrMin` and `hfrMinEffective` so validation can calibrate it. Flag, don't fix.
- **Detection binning**: `DetectionBinningResolver.RecommendFromHfr(HFR_in_focus_in_captured_px)` =
  `clamp(round(hfr/3), 1, 4)`. Note this must be recomputed by the driver — the R² ≥ 0.9 gate that
  guards it lives in `StarDetectionOptimizerWizardVM`, not in the resolver, so no offline code path
  applies it today.
- **Exposure band**: the `t` for which the 20th-brightest on-frame star's gate SNR reaches 7…20 on
  the *median* sweep frame, using the gate's own arithmetic (`≈ 0.85 · peak / σ`, with
  `σ = bin·√((sky + dark)·t + RN²)`, `NTarget = 20`, `TargetSensitivity = 10`), clamped to [0.5, 30] s.
  The band brackets `TargetSensitivity` so that a dataset sitting at its declared exposure produces a
  landed `Sensitivity` comfortably above the floor, i.e. S0 should produce no exposure recommendation.
  **This is the softest derivation in the design** and S0 exists specifically to catch it.
- **Donut expectation**: `donut* = ε > 0 AND r_inner@extreme > 2 px AND HFR@extreme ≥ 9 px`. The last
  term deliberately agrees with `DonutHeuristic.HeavyDefocusHFR = 9.0` so the apriori flag and the
  measured heuristic are on the same scale.
- **Kernel-cap guard**: the sweep's extreme defocus must be ≤ `0.9 × MaxAbsDefocusMicrons(model)`.
  Printed as a table by `--dry-run` before anything renders.

### G5. Golden policy — the substantive design

`TestApp/SynthBank/GoldenFromTruth.cs`, pure and linkable into the Tests project (like
`GoldenStarSet.cs` already is). Truth is native-resolution; goldens are in *captured* pixels, so
first transform: `c_binned = (c + 0.5)/bin − 0.5`, and HFR/radii ÷ bin.

**Tiering by peak SNR.** `peakSNR = Flux · KernelPeakFraction · bin² / σ_bg`. Thresholds (all
spec-configurable):

| peak SNR | disposition |
|---|---|
| ≥ 20 | tier `high` |
| 10 – 20 | tier `medium` |
| 5 – 10 | tier `low` |
| 3.5 – 5 | `unresolved` (neither a required find nor a false positive) |
| < 3.5 | omitted entirely — a detection here **should** count as a false positive |

The `high` cut is deliberately 2× the detector's default `Sensitivity ≈ 10` gate. That is what makes
"recall@high ≈ 1.0" a legitimate expectation rather than a hope: at 2× the gate threshold, a star the
detector misses is a genuine recall defect, not a borderline call.

**Close pairs** (union-find over the pair graph, with `HFR_pair` the flux-weighted mean):

| separation | flux ratio | outcome |
|---|---|---|
| < 2.0 · HFR_pair | any | **one merged box** at the flux-weighted centroid |
| 2.0 – 4.0 · HFR_pair | < 10 | **both → `unresolved`** (blend; detector may legitimately report either count) |
| 2.0 – 4.0 · HFR_pair | ≥ 10 | bright star stays; faint one → `unresolved` (or omitted if below 3.5) |
| ≥ 4.0 · HFR_pair | any | independent |

This is the part that makes exact precision honest. Without it, a detector that correctly reports one
blob for a tight double gets charged a false negative *and* a false positive.

**Box geometry**: `w = h = 2·ceil(max(2·HFR_frame, r_outer + 3σ_min, 4))` — big enough to cover a
donut's full annulus, floored so faint point sources still get a usable box. Boxes clipped by the
frame edge → `unresolved`. Off-frame donut arcs (wing spill) → `unresolved` over the intersection,
because the detector may or may not centroid a partial arc and either is defensible.

**Saturation**: when peak electrons ≥ 0.98 × digital saturation, the star is kept and tiered `high`
(it is unmissable) but marked `"saturated": true` and excluded from the HFR-assessable list, since a
clipped core has no meaningful HFR.

Output per frame: `<frame>.fits.golden.json` via `GoldenStarSetStore.SaveForImage` (schemaVersion 2,
`method: "synthetic"`, full per-tier `coverage` since synthetic coverage is by definition complete),
plus `<frame>.fits.truth.json` — the raw `StarTruth` dump *with each star's disposition* — which is
what makes a false negative debuggable ("which star, how bright, why was it tiered that way"). The
`.truth.json` name is not on `bank-clean`'s denylist, so it survives cleaning.

### G6/G7. Runner, CLI, spec JSON

```
TestApp synth-bank --spec <json> --out D:\SyntheticAutofocusBank
                   [--datasets ids] [--overwrite] [--dry-run] [--verify] [--catalog path]
```

Layout: `<out>\<datasetId>\attempt01\NN_FrameNN_BitDepth16_Bayered0_Focuser<pos>.fits`, 9 frames at
`x0 ± 4·step`, ascending.

- `run_meta.json` inside `attempt01`, written apriori from truth.
- `synthetic_meta.json` at the **dataset root** — deliberately outside both `bank-clean`'s per-run
  walk and `optimize --per-run`'s write target (F15: that prepass writes `optimized_settings.json`
  back into source run folders).
- Determinism: `NoiseSeed = SeedMixer.Combine(bankSeed, datasetIndex, frameIndex)`, recorded.
- `--dry-run` prints derived parameters, the kernel-guard table and per-dataset catalog counts — the
  latter closing R5 (D01's ~39° diagonal FOV is a large catalog query; we want its cost known before
  a render, not during one).
- Idempotent without `--overwrite`: a dataset is complete iff frames + sidecars + metas all exist;
  stray `optimize` artifacts are ignored.
- The generator does **not** write `harness_settings.json` — pixel scale comes from headers.

**Library API used by V1** (this is why the runner is factored, not monolithic):

```
GenerateSweep(DatasetSpec spec,
              BootstrapParams { centerPosition, stepSize, offsetSteps, exposureSeconds,
                                afBinning, emitGoldens },
              string outDir)
```
with `seed = Mix(datasetSeed, scenarioId, round)` so every scenario round is reproducible.

`TestApp/SynthBank/synthetic-bank-spec.json` is checked in and its SHA-256 recorded in metadata.
Defaults: L filter, gain 100, bias 500, −10 °C, throughput 0.85, offset 4, tier thresholds
{20, 10, 5, 3.5}.

### Dataset matrix — 17 datasets, ≈6.6 GB

| id | FL/f (ε) | sensor, AF bin | ″/px | pointing (density) | exp s | step* | HFR_min→ext px | det bin | donut |
|---|---|---|---|---|---|---|---|---|---|
| D01_ultrawide_40mm | 40/2.8 (0) | IMX571, 1 | 19.4 | Cygnus (rich, mag ≤ 10.5) | 0.5 | 9 | 0.7→2.3 | 1 | no |
| D02_rich_135mm | 135/2.0 (0) | IMX571, 1 | 5.75 | Heart/Soul Cas (rich) | 0.5 | 6 | 0.7→2.2 | 1 | no |
| D03_redcat_250mm | 250/4.9 (0) | IMX533, 1 | 3.10 | Orion Belt (mid) | 0.5 | 16 | 0.7→2.4 | 1 | no |
| D04_esprit_550mm | 550/5.5 (0) | IMX571, 1 | 1.41 | M24 Sgr (very rich) | 0.5 | 15 | 1.2→4.1 | 1 | no |
| D05_tec140_1000mm | 1000/7.1 (0) | IMX455, 1 | 0.78 | Auriga (mid) | 0.5 | 35 | 2.1→7.3 | 1 | no |
| D06_sparse_1000mm | 1000/7.1 (0) | IMX533, 1 | 0.78 | Coma/NGP (sparse, 65★ on-frame) | 0.5 | 35 | 2.1→7.3 | 1 | no |
| D07_rc10_2000mm | 2000/8 (0.47) | IMX533, 1 | 0.39 | Double Cluster (rich) | 0.5 | 55 | 3.5→11.7 | 1 | yes |
| D08_c11_2800mm | 2800/10 (0.34) | IMX571, 1 | 0.28 | Hercules (mid) | 0.5 | 82 | 4.9→16.5 | **2** | yes |
| D09_c14_3800mm | 3800/10.7 (0.34) | IMX294, 1 | 0.25 | Cepheus (rich) | 0.5 | 118 | 5.3→18.1 | **2** | yes |
| D10_rc16_3250mm_sparse | 3250/8 (0.47) | IMX533, 1 | 0.24 | Virgo (sparse, 26★ on-frame) | **17** | 89 | 5.6→19.0 | **2** | yes |
| D11_rc10_585_afbin2 | 2000/8 (0.47) | **IMX585, 2** | 0.30n | Cygnus (rich) | 0.5 | 55 | 5.3n→2.7b | 1 (**AF-bin alone**) | yes |
| D12_c14_585_afbin2 | 3800/10.7 (0.34) | **IMX585, 2** | 0.16n | Cassiopeia (rich) | 2 | 141 | 10.2n→5.1b | **2** (**both**) | yes |
| D13_apo200_1800mm | 1800/9 (**0**) | IMX571, 1 | 0.43 | Lyra (mid) | 0.5 | 127 | 3.1→10.5 | 1 | **no** |
| D14_cdk14_2563mm_e47 | 2563/7.2 (0.47) | IMX455, 1 | 0.30 | Perseus (rich) | 0.5 | 60 | 5.3→17.8 | **2** | yes |
| D15_cdk20_3454mm_e47 | 3454/6.8 (0.47) | IMX571, 1 | 0.23 | Cepheus flare (mid) | 1 | 64 | 5.9→20.1 | **2** | yes |
| D16_esprit550_ha3 | 550/5.5 (0) | IMX571, 1, **Hα 3 nm** | 1.41 | Heart Neb Cas (rich Hα) | 0.5 | 15 | 1.2→4.1 | 1 | no |
| D17_cdk14_oiii5 | 2563/7.2 (0.47) | IMX533, 1, **OIII 5 nm** | 0.30 | Cygnus X (rich) | **8** | 60 | 5.3→17.8 | **2** | yes |

`n` = native, `b` = after capture binning. Sky 20.5 mag/arcsec² except D01/D02 at 21.0; seeing 2.5″
except D11/D12/D14/D17 at 3.0″; filter L except D16/D17.

**The exposure and step columns are derived, not chosen.** They are what
`SynthBankDerivations` computes from the checked-in spec, printed by `synth-bank --dry-run`, and every
step\* lands exactly on the value the design predicted. The exposures did not: an earlier draft of this
table carried an imager's intuition (0.5–25 s), and the derivation replaced most of it with the 0.5 s
floor for the reason given under D10 below. Where the two disagreed, the derivation won and the table
was corrected — that is the point of deriving them.

**Why these 17.** The matrix is built to make each axis fail *separately*:

- **Focal length 40 → 3800 mm** spans severely oversampled (HFR_min 0.7 px, where pixelization
  dominates) to severely undersampled donuts. Detector knobs calibrated at one end routinely break at
  the other; that is the whole reason `DetectionBinningResolver` exists.
- **Obstruction tiers** are two, not a continuum: SCT class ≈ **34%** by diameter, RC/CDK class ≈
  **47%**. Anything finer would not change detector behavior meaningfully.
- **D13** is the control that keeps "donut" and "long focal length" from being confounded: a 1800 mm
  *unobstructed* refractor makes filled discs at extreme defocus, `donut* = false`. If any product
  signal recommends donut detection here, that is a defect, not a preference.
- **D11 vs D12** separate the two binnings that are usually conflated. D11 needs AF binning 2 but
  detection binning 1; D12 needs both. D11's near-equivalence (det-bin-2 at AF-bin-1 lands in nearly
  the same place as AF-bin-2 at det-bin-1) is declared in metadata so a driver landing on either is
  not scored as a miss.
- **D06/D10** are sparse (~60 and ~40 stars) — the regime where `StarFieldIsExhausted` and the star
  count probe actually engage.
- **D16/D17** are narrowband: 3 nm Hα and 5 nm OIII pass ≈ 64–107× less flux than L, making them
  read-noise-dominated. Sky background scales down automatically through the filter Δλ in
  `RadiometryCalculator`, so this is physically consistent rather than a fudge. Narrowband datasets
  inherit their optical class's expected P/R bands (M for D16, L47 for D17).
- **D10 is the `CappedByAbsoluteLimit` dataset**, and this was a correction the `--dry-run` forced.
  The design originally assigned that role to D16, on the intuition that a 3 nm filter demands a long
  exposure. Running the derivation showed otherwise: `ExposureRecommender`'s sensitivity metric is the
  **NTarget = 20th-brightest star's** SNR, and a 2.9° field contains 20 bright stars whatever the
  filter, so D16's derived exposure sits at the 0.5 s floor. Long exposures are demanded by *narrow,
  sparse* fields, not by narrowband filters. D10 (3250 mm, sparse Virgo, limiting magnitude 18 so the
  20th-brightest star sits near the faint end) derives ≈17 s with its band top clamped at the 30 s
  ceiling, which is exactly the condition S3 needs.

  This is worth writing down as more than bookkeeping: it means **a rich field can never produce an
  exposure recommendation**, however photon-starved its faint stars are, because the metric only ever
  looks at the brightest 20. Whether that is correct depends on whether AF needs only 20 good stars —
  a defensible position — but it is the same shape of observation as F18 (step size sized by geometry
  with no detectability term), and it is flagged as a followup rather than fixed here.

### `synthetic_meta.json` (dataset root)

```
generator   { tool, policyVersion, specSha256 }
renderRequest { …verbatim RenderRequest… }
sweep       { positions[], noiseSeeds[], kernelCapGuard }
truthModel  { hfrMin, hfrMinEffective, kappa, arcsecPerPixel, captureBinning, readNoiseNote,
              perFrame[ { focuserPosition, defocusMicrons, analyticKernelHfrPixels,
                          measuredKernelHfrPixels, outerRadiusPixels, innerRadiusPixels } ] }
expectedOptimal { exposureSeconds { value, band, definition },
                  stepSizeSteps   { value, tolerance: 0.4 },
                  offsetSteps: 4, autofocusBinning, detectionBinning,
                  donutDetection, donutRationale }
catalog     { pointing, countsByTier, saturated, merged }
goldenPolicy{ thresholds }
matchRadiusPx  // clamp(12, 0.5×maxHFR_ext, 32), floor 6 when median box < 4 px
```

## Workstream V — validation

### V-P1. Pixel scale from the frame header (default change, user-approved)

Both `BankVerifyRunner` (line 155, passed at 183, consumed at 205) and `GoldenEvalRunner`
(`BuildEvalParams` line 294) compute **one** pixel scale for the entire bank from the *active NINA
profile*. On a 17-dataset synthetic bank spanning 40–3800 mm that is not merely imprecise, it is
meaningless — and `HarnessSettingsStore.PixelScaleForFrame` already exists as the correct seam,
documented as the fix for exactly this.

Change: per run, after the first frame loads, use `PixelScaleForFrame(meta, harnessSettings, out source)`
when it returns a finite value, keeping the profile value as fallback. `GoldenEvalRunner` needs
`p.PixelScale` moved out of `BuildEvalParams` to after the first frame load. Both gain
`--pixel-scale header|profile`, **defaulting to header**. Report schema bumps to `afbank-verify/3`
and records per-run `pixelScale` + `pixelScaleSource`.

This shifts real-bank numbers, deliberately. Three things keep that honest:
1. `--pixel-scale profile` is an exact escape hatch to the old behavior.
2. **Anchor guard**: `--pixel-scale profile` on the real bank's `cwhite_2026` must reproduce the
   recorded anchor exactly — precision 0.848, recall@≥12 = 0.181, sensor R² = 0.9933, 7/9 aligned.
3. The header-mode real-bank delta is measured once and written down, since pre/post reports are not
   comparable without the flag.

### V-P2 / V-P3

Per-run match radius from `synthetic_meta.json`'s `matchRadiusPx`, precedence **per-run meta > CLI
`--match-radius` > default 12**; absent meta (the real bank) behaves exactly as before. The effective
radius is reported per run. And `bank-clean`'s keep-list gains `synthetic_meta.json` as insurance,
even though it lives outside the walk.

### V1. The convergence driver

New `TestApp/SynthValidateRunner.cs` (+ `SynthValidationScenarios.cs`, `SynthValidationReport.cs`),
running on an STA thread with a pumped `DispatcherSynchronizationContext` copied from
`BankVerifyRunner` — required because `RunEvaluationData.EvaluateAndFitAsync` deadlocks on a
non-pumping captured context and the thread-affine sensor-model fit hangs on a thread-pool thread.
No `ConfigureAwait(false)` anywhere in that path.

```
TestApp synth-validate --spec <json> --out <dir> [--datasets] [--scenarios]
                       [--max-rounds 4] [--max-evals N]
```

`--out` defaults to `D:\SyntheticAutofocusBank-validation` and **must be outside the bank root** —
otherwise discovery would pick up scenario rounds as additional runs and corrupt V2.

Each round: `GenerateSweep` → real FITS on disk → load them *exactly as `optimize` does* (EXPTIME →
`t_old`, step inferred from positions, `PixelScaleForFrame`) → `RunEvaluationData.CreateEvaluator` →
`StarDetectionOptimizer.OptimizeAsync` → extract recommendations → apply the update policy → next
round. Going through real files rather than in-memory frames is the point: it exercises the same
loader, the same header parsing and the same detector configuration the product uses.

**Update policy**: binning first (deferring exposure by one round, because SNRs are per-binned-pixel
and changing binning invalidates the exposure measurement); otherwise exposure (only when at-floor ∧
`IncreasesExposure`) together with step; re-center on the fitted vertex each round. Donut is never
toggled by the loop — it is a bootstrap input, not a recommendation.

**Scenarios**:

| id | bootstrap perturbation | expectation |
|---|---|---|
| S0 | none (bootstrap = expected) | ~no-op recommendations — **the harness self-test**, run first, on every dataset |
| S1 | step × 0.25 | multi-round convergence, `WasCapped` early |
| S2 | step × 4 | converges within ≤ 2 rounds |
| S3 | exposure × 0.25 | multi-round via the 4× per-round cap; on D16 also validates `CappedByAbsoluteLimit` |
| S4 | detection binning 1 where 2 expected | asserts binning-first ordering |
| S5 | donut off on obstructed datasets | **not** a convergence test — PASS = the degradation signature appears; standing FLAG that no product signal recommends donut |
| S6 | step and exposure both wrong | combined interaction |

≈45–60 optimizer invocations total, with scenario subsets chosen per dataset class.

**Assertions A1–A7**, tri-state PASS / FLAG / FAIL (exit 1 only on FAIL):

1. **Direction** — each recommendation moves toward the expected value.
2. **Monotone approach** — no oscillation across rounds.
3. **Terminal band** `[0.6, 1.6] ×` **`step_behavioral`** — the recommender's *own* fixed point
   computed on the truth curve, not the theoretical `step*`. When
   `|step_behavioral − step_theory| > 25%`, that is itself a FLAG referencing F18. This distinction is
   essential: F18 already records that `StepSizeRecommender` sizes by curve geometry alone with no
   detectability term, so its fixed point legitimately differs from theory. Asserting against theory
   would re-report a known issue as a new failure.
4. **Cap semantics** — `WasCapped` iff truth half-width > 1.5 × sampled half-span, the same invariant
   `InFocusHfrDiagnosticTests` already pins against `MaxHalfWidthSampledHalfSpanMultiple`.
5. **State sanity** — `StarFieldIsExhausted` only where plausible, nothing applied when gated,
   `OffsetSteps == 4`.
6. **Fit gate** — R² ≥ 0.95 per round.
7. **Vertex tracking** — `|vertex − x0| ≤ max(2·step, 0.02·W_3x)`.

**F8 discipline**: the driver never asserts on landed optimizer knob values. F8 records that
optimizer landings are not reproducible across invocations on identical frames, so a knob assertion
would be a coin flip. Assertions are confined to recommendations, σ_focus and J. Likewise an S3
at-floor miss is a FLAG, not a FAIL.

Report `synth-validate/1`: JSON + Markdown, per dataset × scenario × round
{params, fit, recommendations, applied, assertions} plus terminal {converged, roundsUsed, deltas,
flags}, with a flushed progress log (WSL buffers stdout, and these runs are hours long).

### V2. Precision/recall baseline

Full real-bank parity: `optimize --per-run` (config A) and `--per-run --donut` (config B) prepasses
run detached, then

```
bank-verify --runs D:\SyntheticAutofocusBank --nc-sweep 2,3,4 --opt-a … --opt-b … --commit <sha>
```

Broken C0 on donut datasets is expected and recorded rather than treated as a failure. Expected bands
are checked into `docs/synthetic-af-bank-expectations.json`:

| class | config | recall@high | precision |
|---|---|---|---|
| W (wide, D01–D03) | C0@nc2 | ≥ 0.90 | ≥ 0.95 |
| M (mid, D04–D06, D13, D16) | C0@nc2 | ≥ 0.97 | ≥ 0.95 |
| L34 / L47 (long, obstructed) | B | ≥ 0.90 | ≥ 0.90 |
| all non-donut | A | — | ≥ 0.98 |

plus AF R² floors. The M-class ≥ 0.97 is the "well-sampled ⇒ essentially everything" anchor: if the
detector cannot find 97% of stars at 2× its own gate threshold on a clean, well-sampled synthetic
frame, something is wrong that no amount of real-bank noise should have been hiding.

Out-of-band cells are FLAGs → followups. **Regression rule** for future runs on the same schema:
recall@high or precision dropping > 0.02, σ_focus worsening > 20%, or AF R² dropping > 0.005.
**Determinism check**: two designated datasets regenerated from seeds into a temp root must produce
byte-identical frames (SHA-256) and bit-identical C0@nc2 metrics. Routine future re-runs are C0-only.

## Harness self-verification order

Any failure here stops all downstream reads, because a broken instrument produces confident wrong
numbers:

**S0 on every dataset → CLI parity → step fixed-point unit test → `cwhite_2026` anchor (profile mode)
→ determinism.**

CLI parity deserves emphasis: the in-process driver's round-0 output must equal what the real
`TestApp optimize --per-run` produces on the same folder. Without it, the driver could be measuring
its own reimplementation of the product rather than the product.

## Followups workflow

Runners never write `docs/followups.md`. Flags are triaged by hand into house-format entries
(`### F<N> — <title>`, `**Status:** Open · <context>`, then `**Evidence.** / **Why it matters.** /
**Next step.**`), starting at **F19**, cross-linked:

- F18 gets evidence from the `step_behavioral` vs `step_theory` deltas.
- A new "no product signal recommends donut detection" entry cross-links F1 (the donut heuristic
  misses small donuts).
- Every V2 band miss becomes an entry with the offending cell quoted.

Flagged, never fixed in this work.

## Risks

| id | risk | mitigation |
|---|---|---|
| R1 | NINA's FITS header parse conventions are unverified from our side | runner self-verify reloads frame 0 via `FITS.Load` and hard-asserts the four fields; hard failure |
| R2 | the 0.70 px pixelization floor (D01–D03) is an estimate | metadata carries model + effective; validation calibrates it; flag, don't fix |
| R3 | IMX585 registry numbers are datasheet-approximate | pinned verbatim in metadata |
| R5 | wide-FOV catalog query cost (D01 ≈ 39° diagonal) | `--dry-run` prints per-dataset counts before any render |
| — | pixel-scale default change shifts real-bank numbers | deliberate and user-approved; schema-bumped, escape-hatched, anchor-guarded, delta documented once |
| — | the exposure truth band is the softest derivation | S0 catches it before it can masquerade as a product failure |
