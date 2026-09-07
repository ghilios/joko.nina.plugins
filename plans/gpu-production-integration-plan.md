# GPU Production Integration + CCL Default — Implementation Plan

Follow-on to `plans/gpu-early-pipeline-spike-plan.md` / `docs/gpu-star-detection-optimization-results.md`,
per user direction: promote the spike into the plugin so the Star Detection Optimization Wizard can use the
GPU from inside NINA.

## Scope (user-directed)

1. **CCL candidate collection becomes the DEFAULT** (`UseConnectedComponentCollection = true`). Detection
   behavior change ⇒ `StarDetectorVersion` bump; suite tests pinning legacy candidate behavior get re-pinned
   after verifying the deltas are the documented CCL effects (surviving shadowed neighbors, unified donuts).
   TestApp keeps an escape hatch for A/B: `--legacy-collector`.
2. **New persisted option `GpuAccelerationEnabled` (default ON)** + CheckBox in
   `Resources/OptionsDataTemplates.xaml` (options-system invariant). The option gates the OPTIMIZATION path
   only: a non-persisted `StarDetectorParams.AllowGpuAcceleration` flag (EARLY cache key — GPU results are
   tolerance-different) is set solely where the optimization wizard/harness build their seed/baseline
   params. Sensor modeling, autofocus, and single-frame detection never set it.
3. **GPU implementation moves into the plugin** (`Gpu/` folder: GpuDevice, GpuEarlyKernels, GpuEarlyChain,
   GpuEarlyPipeline): ILGPU 1.5.3 PackageReference + `ILGPU.dll` added to the PostBuild deploy xcopy.
   TestApp consumes the plugin classes; the static `EarlyAcceleratorOverride` hook is replaced by the
   params flag consulting a lazy shared pipeline.
4. **Heuristic (`GpuAccelerationHeuristic`)** — initial logic, review later: CUDA accelerator initializes
   (cached probe) AND frame pixels ≥ 2 MP (below that the CPU build is a few ms and transfer overhead
   dominates) AND device VRAM ≥ working-set estimate with headroom; plus a runtime latch: after 3 per-build
   GPU failures the pipeline stops trying for the process (single warning, no spam).
5. **Debayer optimization** = the sanctioned prepared-source cache (`testapp-cli.md`'s "legitimate
   speedup"): `RunEvaluationData` caches the prepared (CFA-filtered + debayered, pre-clone) source Mat per
   frame keyed on the hotpixel-param subset (`HotpixelFiltering`, `HotpixelThresholdingEnabled`,
   `HotpixelThreshold`), single slot per frame, cloned per build. Exact reuse ⇒ bit-identical; lives in the
   optimizer path only, so live AF/sensor-model behavior is untouched. Bayered optimize runs stop paying
   ~1.2 s CFA+debayer on every early rebuild.

## Verification

- Full suite green after re-pinning; CandidateCollectionTests keep guarding walker==legacy equivalence and
  CCL properties.
- `TestApp optimize` arms: timmer (bayered) with the debayer cache; muggsie/CWhite sanity with the option
  flowing from harness settings; `--gpu`/`--no-gpu` forcing still works.
- Live NINA validation is the user's follow-up (wizard run with GPU on; option toggle visible).

## Out of scope / follow-ups (the "what else is needed" list)

- Golden/bank-verify recall-precision validation of CCL-as-default across the bank.
- Wizard UI surfacing of GPU status (used / skipped+reason).
- Manual (MkDocs) updates for the new option + behavior change; release notes.
- PR review + merge; live in-app validation.
