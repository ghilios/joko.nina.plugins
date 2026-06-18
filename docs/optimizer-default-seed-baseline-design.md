# Star Detection Optimizer: Default Seed + Current-Settings Baseline — Design

## Summary

Change the Star Detection Optimization Wizard so that **optimization always starts from a
fully-default set of star-detection parameters**, instead of from the user's current settings, while
**still measuring and displaying improvement against the user's current settings**. The user's current
settings are never written to disk during the run, so if the user does not accept the result their
settings are unchanged — "revert on reject" is automatic.

This is a behavior change in the wizard only. The pure optimizer engine, the objective function, the
detector, and the persisted-settings format are unchanged.

## Motivation

Today the optimizer is *seeded from the user's current settings*
(`RunEvaluationLoader.LoadSavedRunAsync` builds `LoadedRun.Seed` from
`detection.GetStarDetectorParams(...)`, i.e. `BuildStarDetectorParams(liveOptions)`). Two consequences:

1. The search is biased by whatever the user currently has. A poorly-tuned (or previously
   hand-edited) starting point can trap the derivative-free search near a local optimum, and results
   are not reproducible run-to-run as the user's settings drift.
2. Because the seed *is* the baseline, the "before → after" improvement the wizard shows is implicitly
   current → optimized.

We want (1) a clean, reproducible **default** starting point, while keeping (2) a meaningful
improvement comparison against **what the user actually runs today**. These two goals pull the single
"seed" object in two directions, so the design splits it.

## Design

### Decouple the optimizer seed from the displayed baseline

`LoadedRun` currently carries one param bundle, `Seed`, used for *both* the optimizer's starting point
*and* the displayed "Current" baseline. Split it:

| Bundle | Value | Used for |
|---|---|---|
| **`Seed`** (semantics changed) | **fully-default** detection params, with image-context fields preserved (PixelScale, Region, `ModelPSF=false`, `SaveIntermediateFilesPath=""`, parallelism) | the optimizer's starting point |
| **`Baseline`** (new) | the user's **current** settings — exactly what `Seed` was constructed from today | seed-fit guard, the "Current" curve/variant, and the **before** numbers (σ, cost J) for the improvement display |

"Fully reset" means *all* option-derived detection fields are at their defaults — not just the curated
knobs the optimizer tunes — so the fixed (non-tuned) parameters the search holds constant are also at
default, making the optimization fully reproducible regardless of the user's current configuration.

### No live-options mutation (chosen mechanism)

The wizard does **not** write defaults into the live `StarDetectionOptions`. The default seed is built
in memory; the user's current settings are captured in memory as `Baseline`. NINA auto-saves the
active profile, so writing defaults to live options would persist immediately and risk losing the
user's settings if the app closed mid-run. Keeping everything in memory means:

- **"Save the settings before optimization"** = the in-memory `Baseline` bundle (plus its evaluated σ
  and J).
- **"Revert if not accepted"** = automatic. `Apply()` (→ `IStarDetectionOptions.ApplyOptimizedSettings`)
  remains the *only* code path that mutates options, and it only runs on **Accept**. Reject / cancel /
  close leaves the live options exactly as they were — a stronger guarantee than an explicit
  save-and-restore, with no crash window.

### Building the default seed

Add to `HocusFocusStarDetection` (the documented options→params source of truth):

- `internal static StarDetectorParams BuildDefaultStarDetectorParams()` — returns a `StarDetectorParams`
  whose option-derived detection fields are all at their defaults (the `StarDetectorParams`
  field-initializer defaults), the analogue of `BuildStarDetectorParams(options)` for a
  freshly-reset options object.
- `StarDetectorParams GetDefaultStarDetectorParams(IRenderedImage image, StarDetectionRegion region,
  bool isAutoFocus)` — layers the same image-context overrides that `GetStarDetectorParams` applies
  (PixelScale from the profile × binning, Region, and for auto-focus `ModelPSF=false` +
  `SaveIntermediateFilesPath=""`). The image-context layering is shared with `GetStarDetectorParams`
  (extract a small private helper) so the two cannot drift.

**Drift guard:** a unit test asserts `BuildDefaultStarDetectorParams()` equals
`BuildStarDetectorParams(options)` field-by-field (for every option-derived field) where `options` is a
`StarDetectionOptions` at `ResetDefaults()`. This is feasible and isolated using the existing
`InMemoryPluginOptionsAccessor` test double (it never touches a real profile). The test ties the two
default sources together so a future change to either `ResetDefaults()` or the `StarDetectorParams`
defaults that breaks the equivalence fails loudly.

### Computing the current-settings baseline (σ and cost J)

The objective the optimizer uses is `OptimizationObjective.JRun` / `JTotal` with
`new ObjectiveConstants()` (the optimizer's default). The wizard computes the baseline the same way so
the numbers are comparable to the optimizer's `BestJ`:

```
baselineJ = JTotal( perRunBaselineMetrics.Select(m => JRun(m, constants)), constants )
```

where `perRunBaselineMetrics` comes from evaluating each run's `Baseline` params. The seed-fit guard
already evaluates a param bundle per run with determinate progress; it evaluates `Baseline` (preserving
its "usable at your current settings" semantics) and its results feed `baselineJ` and the before-σ — no
extra full pass purely for the baseline. The default `Seed` contexts are additionally warmed so the
optimizer's first evaluation is smooth (when the user's current settings already equal the defaults,
this warm pass is all cache hits).

### Wizard changes (`StarDetectionOptimizerWizardVM`)

- **Seed-fit guard** (`SeedFitIsUsableAsync`): evaluate `r.Baseline` (was `r.Seed`). Capture results to
  compute `baselineJ`.
- **Live progress**: set `ProgressSeedJ = baselineJ` (constant for the run) instead of the optimizer's
  `p.SeedJ` (which is now the default seed's J). The live "Detection improved ~X% so far" then reads
  vs current and matches the final summary.
- **`BuildSummaryAsync`**: evaluate `runs[i].Baseline` for the before-σ and the **Current** curve;
  set `SeedJ = baselineJ` (not `res.SeedJ`); keep `BestJ = res.BestJ`. Compute the changed-parameters
  table as **current → optimized**: for each curated `OptimizerVariable`, compare its value read from
  `Baseline` vs from `res.BestParams`, listing only knobs that differ from the user's current value.
  (This is the exact diff the user accepts onto their live settings, and is consistent with the σ/J
  improvement being measured vs current. It replaces today's `res.ChangedVariables`, which is
  default → optimized.)
- **"Current" variant** (`currentResult`): `BestParams = runs[0].Baseline`; `SeedJ = BestJ = baselineJ`.
  `BuildCurrentSummary` already collapses to the summary's seed σ/J, which are now the current-settings
  values, so it needs no further change.

### Scope

- The default-seed change applies to the **initial** optimization pass.
- The **feedback re-optimize** path is unchanged: it intentionally warm-starts from the analytic
  recommendation (`feedback.Recommended`); that is the whole point of the feedback round. Its
  improvement is still reported against the user's current settings (the baseline carries through), and
  it additionally reports how much the feedback round improved over the plain optimization, as today.
- **"Use current settings" mode** continues to short-circuit the optimization, using `Baseline`
  (current settings) as `BestParams` so the summary shows "no changes".

## Behavioral consequence (intended)

Today the optimizer never returns a result worse than its seed, and the seed is the current settings,
so an accepted result was always at least as good as the user's current settings. Starting from
**default**, the optimized result can be **worse than the user's current settings** — if those settings
were already well-tuned and a search from default did not beat them within the evaluation budget.

This is surfaced honestly: the improvement readouts show σ/J "unchanged" (improvements are clamped to
non-negative) or a visibly worse before→after. The safety net is exactly the requested one: the user
**rejects**, and because nothing was written, their current settings remain in force. **Accept** stays
disabled on the "Current" variant (there is nothing to apply — current is already active), so the path
to "keep my current settings" is simply closing the wizard.

No automatic "don't apply if worse than current" guard is included; the honest comparison plus reject
is the agreed UX. (A future enhancement could make the optimizer also consider the current settings as
a final candidate, guaranteeing accept ≥ current, but that partially re-biases toward current and is
out of scope here.)

## Testing

- **Drift-guard unit test**: `BuildDefaultStarDetectorParams()` ≡ `BuildStarDetectorParams(resetOptions)`
  for all option-derived fields (via `InMemoryPluginOptionsAccessor`).
- **Wizard unit tests**: the displayed before-σ and `SeedJ` derive from `Baseline`, not the default
  `Seed`; the changed-parameters table reflects current → optimized; the "Current" variant uses
  `Baseline`. Use the existing fake `IRunEvaluationLoader` / `LoadedRun` test seams.
- Full suite green: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.
- Manual (later, in NINA): run the wizard with non-default current settings; confirm the search starts
  from default, the improvement is shown vs current, rejecting leaves the live options unchanged, and
  accepting applies the optimized snapshot.

## Out of scope

- Any change to the pure optimizer engine, objective weights, detector, or persisted-settings DTO.
- Mutating / restoring live `StarDetectionOptions` (explicitly avoided per the chosen mechanism).
- The feedback re-optimize warm-start strategy.
- An auto-guard against accepting a worse-than-current result.
