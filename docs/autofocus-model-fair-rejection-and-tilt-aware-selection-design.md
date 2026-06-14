# Autofocus: model-fair outlier rejection + tilt-aware model selection

- **Date:** 2026-06-13
- **Status:** Design approved; ready for implementation planning
- **Area:** `NINA.Joko.Plugins.HocusFocus.StarDetection` (hyperbolic fit) + `…AutoFocus` (options)
- **Driver:** Outlier rejection on the AF V-curve can discard good points and collapse a genuinely
  asymmetric ("tilted") V into a symmetric report.

## 1. Background & motivation

Investigation of the saved run at `C:\Workshop Data\Data\autofocus\uneven` (single region, 10 points,
focuser 54229–58504, ROI annulus Outer 0.8 / Inner 0.5, Median measurement mode, Hybrid model,
`MaxOutlierRejections = 2`, `OutlierRejectionConfidence = 0.90`, weighted fit) reproduced — through the
production fitting code — that **two measured points are rejected as outliers even though the fit is visually
tight** (R² ≈ 0.999):

- Focuser **58504** — abs. residual ≈ 0.17 HFR — the extreme point on the *shallower* wing of an asymmetric V.
- Focuser **56604** — abs. residual ≈ 0.06–0.07 HFR — the **near-focus** point, dropped only as a cascade
  after 58504 is removed and the peer scatter shrinks.

Root cause has **two independent contributors**, confirmed by a faithful per-model reproduction
(`TestApp af-fit`, see §9):

1. **Rejection is model-biased.** Each Hybrid candidate runs the Grubbs test against *its own* residuals.
   The Symmetric model flags exactly the points that reveal the tilt; the asymmetric models flag *none*
   (SmoothBlend flags a different point). Because the test ranks the *standardized* residual `(Y−f)/σ`
   relative to peers — and σ is the star-ensemble scatter (`1.483·MAD`), which overstates the median-HFR
   uncertainty ~√N⋆, so the surviving points fit to ~0.06σ — a 0.07-HFR point becomes a 3σ "outlier."
   A higher `OutlierRejectionConfidence` cannot cleanly fix this: sparing 58504 (z ≈ 2.7) needs confidence
   ≈ 0.9998 ("never reject"), which also passes real bad frames.

2. **Selection is variance-only.** Survivors are ranked by `MinimumStdError` (σ of the focus *position*).
   The 4-parameter Symmetric model has the lowest variance, so it **wins even with zero rejection** on the
   full data, despite the asymmetric (5-parameter) models fitting **4–5× better**:

   | model            | params | σ(focus) | reduced χ² | R²      | min pos | would-reject (budget 2) |
   |------------------|:------:|---------:|-----------:|--------:|--------:|-------------------------|
   | **Symmetric**    | 4      | **14.67**| 0.0260     | 0.99764 | 56452   | 58504, 56604            |
   | UnevenBlend      | 5      | 20.87    | 0.00582    | 0.99953 | 56385   | (none)                  |
   | TiltedHyperbola  | 5      | 17.13    | 0.00707    | 0.99946 | 56412   | (none)                  |
   | SmoothBlend      | 5      | 21.15    | 0.00591    | 0.99952 | 56384   | 58029                   |

   A nested F-test (Symmetric vs each asymmetric, dof (1,5)) gives **F = 17–22 (p < 0.01)**; AICc favors the
   asymmetric models by **ΔAICc ≈ −6 to −8**. The asymmetry is statistically real, and the symmetric report
   biases best focus ~40 steps high (reports **56452** vs the asymmetric models' **~56400**). Both statistics
   are **scale-invariant** to the σ overstatement (they depend only on χ² ratios), so reduced χ² ≪ 1 does not
   invalidate them.

Therefore preserving genuine tilt requires fixing **both** rejection and selection.

## 2. Goals / Non-goals

**Goals**
- Reject a V-curve point only when it is a *true* outlier — an outlier under *every* candidate model shape.
- Report an asymmetric model whenever the asymmetry is statistically justified, so a tilted fit is not
  collapsed to symmetric — neither by point removal nor by the variance-only ranking.
- Keep parsimony: report Symmetric when the data does not justify an extra parameter.

**Non-goals**
- No change to detection, the σ definition (ensemble `1.483·MAD`), or the Median/MeanOutliers measurement.
- No change to the fixed-model (non-Hybrid) path beyond what falls out naturally (see §6).
- Not re-architecting the live mid-sweep fit (`CurveFittingResult`); tracked as an open item (§8).
- The tilt-significance threshold is a **hardcoded constant, not a user option** — no settings or UI surface.

## 3. Design overview

Two coordinated changes inside `AlglibHyperbolicFitting.SelectBestModel` (the Hybrid finalization that
produces the reported fit and rejected-point set, invoked from
`AutoFocusEngine.AutoFocusRegionState.SelectBestHyperbolicModel`):

- **Part 1 — Consensus rejection:** remove only points flagged by *all* solved candidate models; fit/compare
  every model on that single common cleaned set.
- **Part 2 — Significance-gated selection:** unlock the asymmetric models only when a nested F-test against
  the Symmetric fit passes a configurable confidence; otherwise keep Symmetric.

## 4. Part 1 — Model-fair (consensus) rejection

**Current:** `SelectBestModel` calls `FitWithOutlierRejection` per model, which iteratively (≤
`MaxOutlierRejections`) fits → `MathUtility.RejectionTest` (weighted Grubbs) → removes → refits, then each
model competes on its *own* cleaned set; the winner's removed set is reported.

**Proposed algorithm:**
1. Regularize σ once via `WeightRegularization.Regularize` (unchanged; precedes any weighted fit).
2. For each candidate model in `HybridCandidateModels` {Symmetric, UnevenBlend, TiltedHyperbola, SmoothBlend},
   compute its **would-be outlier set** = the ordered points the existing per-model iterative Grubbs loop
   would remove (≤ `MaxOutlierRejections`), **without committing**. Reuse the current loop body
   (`Create` → `Solve` → `BuildResidualWeights` → `RejectionTest` → remove → repeat). A model that fails to
   solve casts no vote.
3. **Consensus set = intersection** of the would-be sets over models that solved, keyed by rounded focuser
   position, capped at `MaxOutlierRejections` (if larger, keep the points with the highest *minimum*
   per-model z). **Decision A: unanimous among solved models** (not majority).
4. Remove the consensus set from the regularized points → one **common cleaned set**.
5. Fit every candidate on the common cleaned set; run Part 2 selection on it; report the **consensus set** as
   the rejected points (`out rejectedPoints`).

**Guards**
- If **fewer than 2 models solve**, we cannot distinguish tilt from a true outlier → **reject nothing**
  (return the current behavior's viable fit with an empty rejected set), and log at INFO.
- `MaxOutlierRejections = 0` ⇒ consensus empty ⇒ no rejection (matches today).
- ≤ 3 points ⇒ `RejectionTest` returns null ⇒ no rejection (matches today).

**Rationale:** a real bad frame (cloud / trail / focuser glitch) sits far from *every* smooth hyperbola and
is flagged by all models → removed. A tilt-revealing point fits the asymmetric models → flagged by Symmetric
only → kept. In-fit Huber IRLS keeps a gross point from dragging any model's curve, so its residual stays
large and it is still flagged by all — intersection does not mask true outliers. Validated: consensus on the
`uneven` run = **none**, so 58504 and 56604 are both retained.

## 5. Part 2 — Significance-gated model selection

**Current:** viable survivors (solved, finite `Minimum.X`, minimum within sampled X range) ranked by
`MinimumStdError` ↑, tiebreak `ReducedChiSquared` ↑ then `RSquared` ↓; LOO best-focus std fallback when no
finite `MinimumStdError`. Symmetric wins on variance.

**Proposed algorithm** (operates on the common cleaned set from Part 1):
1. Fit all candidates; apply the **unchanged** viability filter.
2. Require a usable **Symmetric** baseline fit (solved + viable). If Symmetric is unusable, skip the gate and
   fall back to the current ranking over all survivors.
3. For each viable **asymmetric** (5-param) model, run a **nested F-test vs Symmetric**:

   ```
   F = [ (χ²_sym − χ²_model) / (p_model − p_sym) ] / [ χ²_model / (n − p_model) ]
   ```

   with `p_sym = 4`, `p_model = 5`, `n` = points in the common cleaned set; `χ²` = each fit's weighted
   `ChiSquared` (both fits use the same regularized 1/σ weights ⇒ the ratio is scale-invariant). The model is
   **justified** when `F > FisherSnedecor.InvCDF(1, n−5, c)`, where `c` is the hardcoded
   `TiltSignificanceConfidence = 0.95` (equivalently p < 1 − c). Use
   `MathNet.Numerics.Distributions.FisherSnedecor` (mirrors the `StudentT` usage in `RejectionTest`). If
   `χ²_model ≥ χ²_sym` (no improvement) ⇒ F ≤ 0 ⇒ not justified.
4. **If the justified set is non-empty:** the winner is selected **among the justified asymmetric models** by
   the existing tiering (`MinimumStdError` ↑, then `ReducedChiSquared` ↑, `RSquared` ↓; LOO fallback). Symmetric
   and any unjustified model are excluded from winning.
5. **If the justified set is empty:** winner = Symmetric (parsimony).

**Decision B: F-test at a hardcoded confidence of 0.95** (a `private const` in `AlglibHyperbolicFitting`,
not user-exposed — see §6). AICc agrees here and does not require nesting; compute ΔAICc for the log only
(or as a final cross-check), but the **gate is the F-test**.

**Guards**
- Need `n − 5 ≥ 1` (≥ 6 cleaned points) for the F denominator; with fewer points, skip the gate and use the
  current ranking (asymmetry is not testable).
- Nesting assumption: each 5-param model reduces to Symmetric at its asymmetry-parameter's symmetric value
  (Tilt = 0, equal arms, etc.). If a future model is not strictly nested, prefer ΔAICc for that model.

**Result on `uneven`:** justified = {Uneven, Tilted, Smooth} (F = 17–22); winner among them by `MinimumStdError`
= **TiltedHyperbola** (17.13); reported best focus ≈ 56412 instead of 56452.

## 6. Configuration

- **Hardcoded threshold, no UI.** The tilt-significance confidence is a
  `private const double TiltSignificanceConfidence = 0.95` in `AlglibHyperbolicFitting`, consumed directly by
  the Part 2 gate. It is an internal statistical threshold, not a tuning knob; a settings/UI surface is not
  worth the cost.
- **No new option, interface, or XAML changes.** `AutoFocusOptions`, `IAutoFocusOptions`, `IAutoFocusEngine`,
  and `OptionsDataTemplates.xaml` are untouched (so the project's "every option needs UI" rule does not apply).
- **No public signature changes.** The tilt confidence is internal to `SelectBestModel`, so its existing
  overloads and the `SelectBestHyperbolicModel` call site are unchanged — `SelectBestModel` still returns the
  same winner / fit / rejected-set outputs, now computed via consensus + gate.
- `OutlierRejectionConfidence` (0.90) and `MaxOutlierRejections` are **unchanged** — they still drive the
  per-model Grubbs sets in Part 1 step 2.

## 7. Scope & affected code

| Change | File |
|---|---|
| Consensus rejection + gated selection + `const TiltSignificanceConfidence = 0.95` | `StarDetection/AlglibHyperbolicFitting.cs` (`SelectBestModel`; factor a non-committal `ComputeModelOutliers` out of `FitWithOutlierRejection`) |
| Diagnostic (already prototypes both halves) | `TestApp/AfFitDiagnosticRunner.cs` |
| New unit-test fixtures (§9) | `…HocusFocus.Tests` |

**No changes** to `AutoFocusEngine.cs`, `AutoFocusOptions.cs`, the interfaces, or any XAML — the change is
internal to `SelectBestModel`, whose signature and outputs (winner, fit, rejected set) are unchanged.

**Hybrid only.** A fixed (non-Hybrid) model has no other candidates to vote, so it keeps rejecting against the
chosen model — the user explicitly picked that shape. The target profile uses Hybrid.

## 8. Edge cases & open items

- **Live mid-sweep fit** (`AutoFocusEngine.CurveFittingResult`) fits a single model to decide when to stop and
  to draw the live curve; consensus needs ≥2 models, so it does not directly apply. The *reported* fit +
  rejected points come from the finalization path, which this design covers. **Open item:** decide whether to
  also run a 4-model consensus per incremental fit (heavier) or leave the live path on single-model Grubbs.
  Recommended: leave as-is for now; the final report is what users inspect.
- **Determinism:** preserve the existing fixed `HybridCandidateModels` index ordering for the would-be-outlier
  computation and the final ranking so results are reproducible (the current parallel-into-indexed-slots
  pattern already guarantees this).
- **All models reject the same single point** (e.g. one true bad frame): consensus removes it; F-test then
  runs on the remaining points — correct.
- **Tiny sweeps (<6 points):** gate skipped; current ranking applies (note in log).

## 9. Validation & testing

**Diagnostic (already built, `TestApp af-fit`):** must reproduce, on `…\uneven --params-from initial`:
consensus outliers = **none**; per-model F = 17–22; gated winner = **TiltedHyperbola**; best focus ≈ 56412.
Command:
```
TestApp af-fit --af-run "C:\Workshop Data\Data\autofocus\uneven" --params-from initial --max-rejections 2
```

**Unit tests** (NUnit, link the source as the test project already does) — synthetic V-curves:
1. *Symmetric, no outlier* → winner Symmetric, 0 rejected.
2. *Symmetric + one gross bad frame* → that frame is the consensus outlier (flagged by all), removed; winner
   Symmetric.
3. *Tilted, no outlier* → consensus = none; F-test passes; winner an asymmetric model (not Symmetric).
4. *Tilted + one gross bad frame* → only the bad frame removed (consensus); tilt-revealing wings kept; winner
   asymmetric.
5. *Tilted but below significance* (mild asymmetry within noise) → F-test fails → winner Symmetric (parsimony).
6. *F-test math*: known χ²/dof inputs → expected F; justify/not decision at the hardcoded 0.95 (exercise the
   F-test helper at a second confidence in-test to confirm the threshold responds).
7. *Guard*: <2 models solve → no rejection; <6 points → gate skipped.

**Regression:** run the full suite (`dotnet test …`) — currently 954 passing.

## 10. Key references

- Rejection decision (Grubbs): `Utility/MathUtility.cs` `RejectionTest` (standardized, weighted).
- Model competition: `StarDetection/AlglibHyperbolicFitting.cs` `SelectBestModel` / `FitWithOutlierRejection`;
  `Create`; `ChiSquared`/`DegreesOfFreedom`/`ReducedChiSquared`/`MinimumStdError`/`RSquared`.
- σ regularization: `StarDetection/WeightRegularization.cs`.
- Finalization caller: `AutoFocus/AutoFocusEngine.cs` `SelectBestHyperbolicModel`.
- Models & params: `Symmetric` (4p) `HyperbolicFittingAlglib`; `UnevenBlend`/`TiltedHyperbola`/`SmoothBlend`
  (5p). Enum: `Interfaces/HyperbolicFitModel.cs`.
