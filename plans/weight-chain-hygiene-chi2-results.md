# χ² Gate Calibration Results: Threshold Kept at 5.0

## What was measured

The reduced-χ² distribution of weighted hyperbolic fits through the **new regularized weight chain**
(F5a `WeightRegularization` + F6 SEM pooling, this branch), measured by re-fitting every saved
auto-focus report in the user's corpus with every hyperbolic model via the `fit-quality` TestApp verb
(`FitQualityRunner`). Goal: apply the pre-registered decision rule from
`weight-chain-hygiene-design.md` §4 — if the good-run χ²_red distribution is tight (≲ 2 orders of
magnitude spread), set a new default `ReducedChiSquaredRejectionThreshold` from a robust upper fence
(median × 10 or ≈ P99 with margin); if it sprawls, keep 5.0 and document the gate as advisory in
scatter units.

**Corpus**: `C:\Workshop Data\Data\autofocus` — 5 saved AF runs (one per sub-folder,
`AutoFocus_20260607_*`), each with 6 region reports → **30 reports, 6–8 measure points each**
(216 raw MeasurePoints, 2 NaN excluded, **214 points enter the fits**). All from a single rig on a single night (2026-06-07, a user's CTU
tilt-wizard session per the corpus Readme), step size 175, all converged with R² ≥ ~0.97. The 6
regions of a run share the same frames, so the effective number of independent sweeps is ~5.
The folder also contains 281 `*_star_detection_result.json` files which the harness correctly
skipped (no `MeasurePoints`).

**Harness**: `FitQualityRunner` scans `--dir` recursively (`*.json`, `SearchOption.AllDirectories`),
so a single invocation covered the whole corpus:

```
TestApp.exe fit-quality --dir "C:\Workshop Data\Data\autofocus" --out "C:\temp\fitquality-weightchain"
```

Coverage: 311 JSON files found, 30 curves parsed, 281 skipped (all non-reports), **120/120 fits
solved** (4 models × 30 curves). A `--no-weights` control run was also taken
(`C:\temp\fitquality-weightchain-nw`).

---

## Tables

Re-fit reduced χ² through the new chain (weighted, all `SolveOk` rows; n = 30 per model):

```
model              n       min    median       P90       max   max/min
Symmetric         30    0.0065     0.287     0.961      1.79     277
UnevenBlend       30    0.0043     0.240     0.811      2.10     484
TiltedHyperbola   30    0.0043     0.249     0.971      1.23     285
SmoothBlend       30    0.0044     0.245     0.812      1.91     438
ALL              120    0.0043     0.256     0.968      2.10     485
```

Old chain vs new chain, restricted to the model each run actually used
(`HyperbolicFitModelChosen`; n = 30):

```
view                          min    median       P90       max   max/min   spread (OOM)
stored (production gate)  1.26e-4     0.133     0.409      1.79   1.4e+4        4.15
re-fit (new chain)         0.0065     0.287     0.961      1.79      277        2.44
```

P99 is omitted: with n = 30 (and ~5 independent sweeps) it is just an interpolation between the two
largest order statistics and carries no information beyond the max. The spread measurement uses
max/min on the chosen-model rows (primary) and the all-model pool (secondary); P90/median is 3.35
(stored: 3.1) for context.

Unweighted control (chosen-model rows): median χ²_red 0.133 vs 0.287 weighted — same order, nothing
alarming; the per-row values differ from stored, so the near-identical median to the stored view is
coincidence.

---

## Reading the table

**The distribution sprawls.** Good-run χ²_red spans 2.44 orders of magnitude through the new chain
(chosen-model rows), 2.69 OOM across the all-model pool, and 4.15 OOM in the production-gate view
(stored values, see forensics below) — all beyond the pre-registered ≲ 2 OOM tightness bar. This is
on a *single rig, single night, fixed step size* corpus; heterogeneous rigs (star-poor narrowband
fields vs star-rich broadband, since χ²_red scales ~1/N*) would only widen it. The design doc
predicted exactly this sprawl.

**The tight-branch fence would not be safe anyway.** Median × 10 on the re-fit view is 2.9 (→ 3 at
one significant figure), only 1.4× above the observed good-run max of 2.10 in a corpus this
homogeneous. Worse, the same recipe applied to the production-gate (stored) view gives 1.3, which
would falsely reject the legitimate `AutoFocus_20260607_232246/Region3` run (stored χ²_red = 1.79,
its noisiest but still well-converged curve). A fence that flips between 1.3 and 3 depending on
which honest view of the same corpus you compute it from is not a calibration.

#### Spread-estimator sensitivity

The 2.44 OOM figure for the re-fit chosen-model view is sensitive to a single row: `231439/R1`
(χ²_red = 0.00646) sits 3.9× below the second-lowest value (0.0250). Dropping that one row yields
**1.86 OOM**; P5–P95 gives **1.66 OOM** — under a trimmed estimator the re-fit view alone would
land in the TIGHT branch. The sprawling verdict does **not** rest on this fragile figure. It rests
on two sturdier facts:

1. **The production-gate (stored) view sprawls robustly**: 4.15 OOM full, **3.00 OOM at P5–P95**.
   The gate will run on post-rejection stored values in production, so this is the operationally
   relevant distribution.
2. **The tight-branch fence is unstable on this corpus regardless**: median × 10 on the stored view
   = 1.33, which is *below* the verified-legitimate stored maximum of 1.79 (`232246/Region3` — a
   well-converged curve, just noisier than the rest). A recipe that would false-reject a good run in
   the calibration corpus is not a calibration. The 5.0 threshold stands.

**Why stored and re-fit differ per row.** Forensics on the per-row deltas (all explained, none
mysterious):

- **13/30 rows reproduce the stored χ²_red exactly** (rel < 1e-4), and stored LOO matches the
  re-fit LOO to within ~1e-7 relative (≈8 significant digits) on 27/30 rows — strong validation that the new chain is
  solve-identical to the production chain whenever regularization is a no-op.
- **14 rows differ because of production's Grubbs outlier rejection**, not the weight chain: the
  engine's stored fit/χ²/σ(focus) come from the *post-rejection* point set, while the report's
  `MeasurePoints` (and hence the harness re-fit) contains all points. Verified by experiment on the
  most extreme row (`225740/Region4`, stored χ²_red = 1.26e-4 vs re-fit 0.33): dropping the point at
  position 1773 and re-fitting reproduces the stored χ²_red (0.000126198172), σ(focus)
  (0.754811879), and minimum (1234.96617) **exactly**. The Huber IRLS in the all-points re-fit
  crushes the outlier's influence, so the minimum agrees to 4e-4 steps — only χ²/σ(focus) move,
  because they honestly count the outlier's residual. This also explains the absurd stored low tail
  (1e-4): a 5-parameter UnevenBlend fit on 6 post-rejection points (dof = 1) nearly interpolates.
  (Production computed the stored LOO on the *full* set — the drop-variant's LOO is 0.61 vs stored
  13.26 — which is why LOO matches the harness even on rejection rows.)
- **3 rows differ because regularization actually touched σ** — the only curves where F5a changes
  anything (see next section).

The gate itself runs on the post-rejection fit in production, so future production gate values will
look like the *stored* column (just with regularized σ on degenerate-σ curves) — the 4.15 OOM view.
Either view sprawls; the decision is the same.

---

## F5a victims (fitted-minimum shifts under the new chain)

The regularizer (`max(σ, 0.2·median)`, invalid → median) modified σ on exactly **3 of 30 curves**;
no curve in the corpus has a *displaced* degenerate-σ point, so there are no true F5a victims here:

```
file                          floored points        min σ/median   minimum shift   σ(focus)
231439/Region2                2                     0.041          0.0001 steps    7.6
232246/Region2                1                     0.043          1.54 steps      15.7
232246/Region3                1                     0.169 (marginal) 0.15 steps    21.2
```

`231439/Region2` is the corpus's one genuine near-degenerate case: two points at σ = 0.041·median
carried ~24× the median weight (~600× squared influence) through the old chain, now capped at 5×.
Its fitted minimum moved 1e-4 steps — those points happened to sit on the curve, so the pin was
harmless *this time*; the cap removes the exposure. All shifts are ≪ σ(focus). The larger
stored-vs-refit position deltas in the corpus (max 5.11 steps, `230403/Region2`; 3.17 steps,
`230906/Region4`) are the rejection-set artifact described above, not chain changes, and are also
well inside σ(focus).

---

## Decision

**Keep `ReducedChiSquaredRejectionThreshold` = 5.0. No code changes.** Taken 2026-06-12 under the
pre-registered rule: the good-run χ²_red distribution sprawls (2.4–4.2 OOM depending on view, vs the
≲ 2 OOM tightness bar), so the sprawling branch applies. The observed good-run maximum is 2.10
(re-fit) / 1.79 (production gate view), so 5.0 rejects nothing in this corpus while still bounding
gross misfits ~20× above the median. The Task 6 tooltip and XML docs already describe the gate as a
coarse sanity bound in scatter units (χ²_red ≪ 1 normal for star-rich fields, scales with
FramesPerPoint); no number in them is contradicted by this data, so they stand unchanged.

---

## Caveats and anomalies

- **FramesPerPoint**: every report has `AutoFocusNumberOfFramesPerPoint` = 1, so this corpus is
  entirely FPP = 1-era — the F6 SEM pooling change (×√n vs ÷√n on multi-frame points) does not
  contaminate any number above. No multi-frame calibration data exists yet; if FPP ≥ 2 corpora
  appear, the gate distribution shifts by ~×n under the SEM chain and this analysis should be
  revisited before trusting the χ² criterion there.
- **Small, homogeneous corpus**: 30 curves but ~5 independent sweeps, one rig, one night. The
  sprawl conclusion is conservative (more diversity → more sprawl); a tight conclusion could never
  have been trusted from this corpus alone.
- Two reports each contain one fully failed measure point (`Value: NaN, Error: NaN` at one
  position); both production and the harness drop them (`232246/Region3` fits on 6 points).
- The harness's inferred step size (median spacing, 175) matched the configured
  `AutoFocusStepSize` (175) on every report, so the UnevenBlend re-fits are like-for-like.
- Run artifacts: `C:\temp\fitquality-weightchain` (weighted), `C:\temp\fitquality-weightchain-nw`
  (unweighted control), `C:\temp\fitquality-dropvariants*` (rejection-forensics experiment).
