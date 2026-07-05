# Tilt Calibration Replay Reproducibility — Design

## Problem

Two people replayed the **same** saved tilt-calibration run
(`TiltCalibration_20260705_120026`, a 3-screw adapter) and got materially different "Calibration
Complete" results:

| | Screw 1 | measured µm/turn (nominal 400) | quality warning |
|---|---|---|---|
| astrodet (v4.0.0.1) | 81.9° | 523 (+30.8%) | unequal moves, gap 17.7° |
| local (dev) | 67.1° | 418.7 (+4.7%) | none |
| saved `calibration` block in the folder | 66.0° | 352 | (ratio 5.48 → would warn) |

Worse, the folder **cannot reproduce its own saved numbers**, and astrodet's *own* machine
produced two different answers (66.0° and 81.9°) from the same frames at different points in one
session. A replay is expected to be deterministic; it is not.

## How the wizard actually "replays"

The replay does **not** re-use the saved per-step tilt planes. For each step it re-runs the entire
measurement pipeline on the saved FITS:

```
star detection  →  RANSAC alignment across the focus sweep  →  per-star focus curves
              →  paraboloid sensor-model fit (SensorModel.FitParaboloidModel)
              →  tilt plane A/B  →  screw angles / µm-per-turn / warning (TiltCalibrationCalculator)
```

Entry point: `TiltAdapterWizardVM` replay loop → `inspector.AnalyzeAutoFocusFromSavedPath(folder, token, detectionOverride)`.

The **only immutable input is the FITS pixels.** Everything else — star-detection settings, focuser
step size, f-ratio — is read from live state at replay time unless explicitly pinned.

### Units (why angles and µm/turn behave differently)

`CreateTiltPlaneModel` builds the plane from `sensorModel.TiltAt(±w/2, ±h/2) / focuserStepSizeMicrons`,
so **A/B are in focuser _steps_** and the fit's focuser size cancels out of them. Consequences:

- **Screw angles** = `atan2(A, −B)` on per-step A/B deltas → depend only on the **star set** (the
  direction of each screw's tilt change). Invariant to focuser step and pixel size.
- **Measured µm/turn** = `0.5·(δ1+δ2)/applied`, `δ = |ΔG|·leverArm` → scales linearly with the
  **geometry focuser step** used in `RunCalibrationMath`, on top of the star-set magnitude.
- **f-ratio does not enter the gradient at all** (`SensorParaboloidSolver` never takes it); it only
  scales downstream physical-effect microns. It is a red herring for angles and µm/turn.

So a wrong star set rotates the angles *and* rescales µm/turn; a wrong focuser step rescales µm/turn
only.

## Root cause

The recovered tilt is dominated by **which stars the detector admits**, and the detector's star set
is not pinned to capture time. Evidence from astrodet's NINA log, same `04_Screw1` frame:

| run | detected → in-fit stars | paraboloid RMS / GoD | result |
|---|---|---|---|
| tight | 1055 → 590 | 10.3 µm / 0.97 | 66.0° / 352 µm |
| permissive | 2132 → 1434 | 56.0 µm / 0.82 | 81.9° / 523 µm |

The permissive pass roughly **doubled** the star count with low-SNR/spurious detections (RANSAC
putative-match fractions fell to 2–8%), which **quintupled the fit RMS** and corrupted the tilt
plane — most visibly for the weak screw-2 signal in this dataset. Both runs are the same machine,
same plugin build, same f-ratio; only the detection differed.

Pinning is incomplete in three layers:

1. **The pipeline re-detects rather than replaying stored stars.** Nothing records the actual stars
   used at capture, so the answer is only as reproducible as the detection settings.
2. **The persisted `optimizedStarDetectionSettings` is a curated subset** — 25 of the 53 knobs in the
   full `StarDetectionSettingsSnapshot`. The remaining ~28 (the `UseAdvanced`/`UseOptimizedSettings`
   mode flags, the `Simple_*` preset baseline the curated knobs overlay onto, contamination
   rejection, PSF/HFR fitting, structure dilation, saturation handling) are read live from the
   replaying profile.
3. **The legacy overlay dropped two curated knobs it did have** —
   `LocallyAdaptiveBinarization` and `AdaptiveNoiseBlockSize` were absent from
   `TiltAdapterWizardVM.OverlayOptimizedSettings`, though the canonical
   `StarDetectionOptions.ApplyOptimizedSnapshotToLiveProperties` applies them and the metadata stores
   them. Adaptive binarization is exactly the switch that halves/doubles detected-star count in
   vignetted/gradient fields, so an unpinned value is high-leverage. **(Fixed — see below.)**

Focuser step adds an orthogonal µm/turn-only error for legacy folders: those predate the
`MeasurementContext` capture, so on replay the sensor-model focuser size falls back to the live
`InspectorOptions.MicronsPerFocuserStep` rather than the captured 3.6.

`BuildTiltReplayDetectionOverride` has a branch that *would* prefer a per-step
`AutoFocusReplayMetadata.StarDetection` (the full 53-field snapshot), but **tilt captures do not emit
per-step replay `metadata.json`** — verified across the whole calibration bank, where the only
`metadata.json` is the run-level tilt file (the AF engine's `WriteReplayMetadata` is gated on a save
folder / method the tilt capture path does not satisfy). So *every* tilt replay falls through to the
curated overlay today, regardless of plugin version.

## Operational guidance (until reproducibility is fully fixed)

For the specific run above, the local result is the accurate one because its detector kept only real
stars. To get the clean result, a user should align their live star detection with the run's saved
`optimizedStarDetectionSettings` — in particular **enable Locally Adaptive Binarization** (the
capture used it; it suppresses the spurious detections that inflate the fit). Note that even the
clean answer tripped the weak-screw-2 warning at capture; a recalibration with larger, more equal
screw turns is more robust regardless of detection settings.

## Fix ladder

### 1. Overlay parity — DONE

Added the two missing assignments to `OverlayOptimizedSettings` so it once again mirrors
`ApplyOptimizedSnapshotToLiveProperties`, plus a reflection guard test
(`OverlayOptimizedSettings_AppliesEveryCuratedKnob_...`) that fails if any curated
`OptimizedStarDetectionSettings` knob is not copied onto the replay snapshot — so the two apply
paths cannot silently drift again.

### 2. Persist the full detection snapshot at run level — PROPOSED (primary)

Detection settings are constant across the steps of a single calibration run, so a **run-level**
snapshot is sufficient — no per-step storage is needed.

- When saving a tilt run, persist the complete `StarDetectionSettingsSnapshot` (all 53 fields,
  via `StarDetectionSettingsSnapshot.FromOptions`) in `metadata.json`, alongside the existing
  curated `optimizedStarDetectionSettings` (which stays for the optimizer/UI).
- On replay, when this full snapshot is present, use it directly as the detection override — do not
  overlay the curated subset onto the live profile. This closes the ~28 non-curated knobs.
- Override resolution order in `BuildTiltReplayDetectionOverride`: per-step
  `AutoFocusReplayMetadata.StarDetection` (kept, though tilt captures don't currently emit it) →
  **run-level full snapshot (new — the effective full-pin path for tilt)** → curated-overlay-on-live
  (legacy runs, now correct after #1) → live profile (with the existing "no stored settings" warning).
- Because the snapshot serializes only when present, existing folders (no snapshot) deserialize it to
  `null` and degrade to the legacy overlay — no schema bump needed.

### 3. Focuser step / geometry — already pinned, no change needed

An earlier draft proposed pinning the captured focuser step for the geometry too. On closer analysis
this is unnecessary for the wizard's screw outputs:

- A/B are in focuser **steps** (`CreateTiltPlaneModel` divides `TiltAt` microns by
  `focuserStepSizeMicrons`), so the sensor-model **fit's** focuser size cancels and does not affect
  screw angles or µm/turn.
- The only focuser step that scales µm/turn is the **geometry** step in `RunCalibrationMath`, and the
  "use saved geometry" replay mode already passes `metadata.FocuserStepSizeMicrons`. The "use current
  geometry" mode intentionally uses the live step.

So the motivating µm/turn difference (418.7 vs 352) is a **detection** difference — it also changed
the screw-move ratio (5.48 → ≤1.5), which a common focuser-step rescale cannot do — and is fully
addressed by #2. No code change here.

### Out of scope: persisting detected stars

Storing the registered star table and re-fitting from it would make replay bit-identical regardless
of detector version, but it is deliberately **not** pursued: #2 pins the settings that drive
detection, which is sufficient, and re-running detection keeps replay honest to the current detector.

## Decision

Ship #1 now (small, guarded). #2 is the real reproducibility fix — run-level (settings are constant
across a run) and backward-compatible: folders without the full snapshot degrade to the now-correct
legacy overlay path. Focuser-step geometry is already pinned in saved-geometry mode (#3). Persisting
detected stars is out of scope.
