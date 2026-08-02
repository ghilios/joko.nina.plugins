# Optimizer Exposure Recommendation (Floored Sensitivity Gate) — Design

## Problem

`BrightnessSensitivity` is the detector's signal-to-noise acceptance gate: a candidate must satisfy
`(s − b)/n > Sensitivity` to be counted (`StarDetector.cs:1745-1770`). The shipped default is `10.0`
(`StarDetectionOptions.cs:356`); the Star Detection Optimizer searches it over `[0, 50]`
(`OptimizerVariable.cs:123-124`).

When the optimizer lands that gate at ~0 it has admitted essentially anything above the noise in order to find
stars at all. The wizard then presents a tuned result, a focus curve, a recommended AF step size, a detection
binning recommendation, and an enabled Accept button — every one of them derived from low-confidence detections,
with **nothing on screen saying so**. The real remedy is a longer auto-focus exposure, and the user is never told.

The wizard already says this for the *total* failure case: `SeedFitIsUsableAsync`
(`StarDetectionOptimizerWizardVM.cs:2473-2479`) refuses to optimize and says *"Increase the exposure and run the
sweep again."* This design covers the soft case — the run succeeds while starved.

A floored gate is not hypothetical. `docs/bobp-m101-recall-investigation-results.md` documents a real bank run
whose sibling landed `BrightnessSensitivity = 0`, and `docs/optimizer-sensitivity-pinning-design.md` names that
operating point the **"star-flooding corner"** (`sens 0`, `starClip 0.375`, precision 0.572) — a corner the
objective can reach because `S_stars` saturates and, without labels, nothing penalizes generic faint false
positives.

## Goal

On the Summary page, when the landed gate is at its floor: name the problem, show a **derived** exposure (not a
guessed multiplier), and — for a Live run — re-capture and re-optimize at the new exposure in one click. A Replay
("saved autofocus") run gets the same diagnosis with no action available, and **Accept stays enabled** so the
user can still apply the values they just spent minutes computing.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Trigger | Landed sensitivity **alone**, `<= 1.0` | User's call. No star-count corroboration: a floored gate is worth saying out loud even when exposure turns out not to be the answer — the sub-states carry that nuance instead of hiding the block. |
| Threshold shape | Threshold, never `== 0` | The pattern search refines continuous steps to `InitialStep × StepFloorFraction = 1.0 × 0.125` (`StarDetectionOptimizer.cs:38,485`), so a landed value is routinely `0.125 / 0.25 / 0.5`. An equality test would miss most real cases. `1.0` is one full search step off the floor and one tenth of the shipped default; `2.0` was rejected as a plausible legitimate tuning on a rich field. |
| Suggested value | **Derived** from measured per-star SNR | User's call over a fixed multiplier. Requires plumbing the gate statistic out of the detector — it is computed today and discarded. |
| Scope | Optimizer wizard Summary only | User's call. Not the AF results panel, not the options page. |
| Live action | One-click capture + re-optimize | Mirrors the existing "Optimize again at 2x2" affordance. |

## Approach

### The measurement

`sensitivity` is already computed per candidate at `StarDetector.cs:1746-1762` (with the donut-aware
`max(perPixel, TotalFlux/(σ·√N_unclipped))` branch applied) and compared against `p.Sensitivity` — then thrown
away. Carry it on the accepted `Star` as `MeasuredSensitivity` and plumb it, following the existing `StarHFRs`
precedent verbatim, to `RunEvaluationMetrics.FrameStarSnrs` (jagged, parallel to `FrameStarCounts`).

**The objective `J` must stay bit-identical.** The new field is written by `RunEvaluationData` and read only by
the new recommender; it never enters `JRun`. Enforced mechanically: `FrameStarSnrs` appears exactly once in
`OptimizationObjective.cs` (the declaration).

### The derivation

Per **non-recovery** frame, drop non-finite and non-positive SNRs, sort descending, take the `NTarget`-th
(index `NTarget-1`). `S_now` = the **median of those per-frame values across frames**.

Then `t_new = t_old · (TargetSensitivity / S_now)²`, with `TargetSensitivity = 10.0` (the shipped default gate).

**Why the `NTarget`-th star and not the median star.** The median accepted star over-recommends: on a 200-star
frame it demands that 100 stars clear the default gate. The `NTarget`-th star asks the actual question — *what
exposure would put 20 stars per frame above the healthy gate* — and ties the recommendation directly to the
objective's own knee.

**Why the median across frames and not the worst frame.** The objective's knee is
`0.4 · clamp01(Median(counts)/NTarget)` (`OptimizationObjective.cs:274`) — a median across frames. Pairing the
`NTarget` knee with a worst-frame aggregation would form a statistic the objective never computes, and would
hand the whole recommendation to one cloud, satellite, or guide bump. Recovery frames are excluded, matching
`JRun`'s own hard-floor exemption at `OptimizationObjective.cs:310-330`.

**Why `SNR ∝ √t`.** Both branches of the gate statistic are (background-subtracted signal) ÷ σ, so the scaling
is set entirely by σ: sky-limited gives `σ ∝ √t` hence `SNR ∝ √t` (factor `r²`); read-noise-limited gives
`σ` constant hence `SNR ∝ t` (factor `r`). Sky-limited is right for *these specific frames* — the sweep is
deliberately defocused, so star flux is spread thin and sky dominates σ across the frame. It is also the
conservative branch (`r² > r` for `r > 1`), so a genuinely read-noise-limited rig gets an overshoot rather than
a recommendation that fails to fix anything. The caps bound the overshoot, and re-measuring costs one click.

### Caps and honesty

Cap the **pre-filled** value at `min(t_old × 4, 30 s)` and report the uncapped `RawSeconds` in the copy.

- **4×** is exactly "at most double the signal-to-noise in one step" under `√t` — a physical statement rather
  than a taste — and matches `StepSizeRecommender`'s converge-over-runs philosophy
  (`MaxHalfWidthSampledHalfSpanMultiple = 1.5`).
- **30 s absolute:** a 9-point sweep at 30 s is 4.5 min of pure integration before focuser moves and downloads,
  the wizard widens it further by `FocusRecoverySteps`, and `AutoFocusEngineOptions.AutoFocusTimeout` is hard
  enforced by the fixed sweep. An uncapped recommendation can produce a sweep that times out.

Keeping `RawSeconds` is what lets the copy say the useful thing when a setup needs 75 s — *"roughly 14 minutes
per auto-focus run; at that length this filter is the limit; consider auto-focusing through a broadband filter
with a filter offset instead"* — rather than silently truncating to 30 s.

Rounding is always **up** (rounding down partially undoes the derivation) and happens **after** the cap, so the
displayed value can never exceed it.

### States

| State | Condition | Response |
|---|---|---|
| Actionable | Live, derived value available | Row + body + editable box + "Capture a new sweep and optimize" |
| Capped | derived > `min(4×, 30 s)` | Same, plus what the uncapped number implies |
| Exposure isn't the limit | `S_now >= 10` | "The gate landed at its floor, but your stars already clear the default gate — this field is star-poor, not under-exposed." Never recommend a shorter exposure. |
| No derivable number | `< 3` usable frames, `t_old` unknown, or no SNR data | Diagnosis only, no number. Precedent: `HasDetectionBinningMeasurement` — a degenerate measurement has nothing to say, and the honest response is silence. |
| Gate healthy | `sensitivity > 1.0` | Block hidden entirely; no new noise on the happy path |

## UI

Two parts, because **Accept lives in the footer outside the ScrollViewer and is always visible** — a block below
the fold can be skipped entirely on a plausible-looking curve.

1. **One italic sentence above the chart**, in the slot `OptimizerNoImprovementNote` already occupies
   (`Optimization/DataTemplates.xaml:716-721`), in `NotificationWarningBrush` — the same treatment as
   `AutoFocus/DataTemplates.xaml:3197`. The page's **only** colored element. Never `NotificationErrorBrush`
   (it must not read as an error); no icon (no warning in this codebase uses one).

2. **A "Star signal" block** between "Stars per frame" and "Auto-focus settings" — the head of the
   recommendation region, above the two blocks whose numbers it impugns, and adjacent to the existing
   `SweepExposureChangeText` exposure row. Structure follows the detection-binning block
   (`Optimization/DataTemplates.xaml:835-861`) verbatim: bold header → `Width="200"` label/value row →
   `MaxWidth="560"` wrapped body → action.

The action row pairs an editable `ninactrl:UnitTextBox` (cloned from the Live-page exposure box at `:576-590`)
with the button. The button label deliberately carries **no number**, breaking from the binning precedent
("Optimize again at 2x2"): beside an editable `LostFocus`-bound box, a numbered label goes stale mid-edit and
disagrees with the box two centimetres away. Consequence in the label, value in the box.

**The principle tying the block together, inherited from the binning block's design comment: the wizard never
writes a value it did not measure with.** That is why Live gets a re-capture button rather than an "apply 12 s"
checkbox — settings tuned on 3 s frames were never evaluated on 12 s frames — and why Replay states the number
instead of offering to write it.

## Replay

`CanAccept()` and `Apply()` need **zero changes**. `CanAccept` reads nothing about exposure or recommendations;
`Apply` writes the exposure only under `lastRunWasLive && capturedLiveExposureSeconds > 0`. The Replay block is
display-only.

**`HasExposureRecommendation` must never enter `CanAccept` in any form.** A recommendation the wizard cannot act
on must not block applying settings the user already paid for.

## Rejected alternatives

- **A fixed multiplier (2×) prefill.** Rejected by the user in favour of a derived value. Would also have been
  ~1.4× SNR — frequently not enough to move a floored gate.
- **Median accepted-star SNR as `S_now`.** Over-recommends; see above.
- **Worst-frame aggregation.** Forms a statistic the objective never computes and is hostage to one bad frame.
- **A two-knee `max(r_NTarget_median, r_NFloor_worst)` rule.** More faithful to both `S_stars` terms, but on
  realistic numbers the wing frames' 8th-brightest SNR binds the cap on nearly every run, so every
  recommendation would read "capped" and the number would stop carrying information.
- **Corroborating the trigger with star counts.** Would suppress the block on rich fields that reach the
  flooding corner. The user chose sensitivity alone; the `ExposureIsNotTheLimit` state covers that case honestly
  instead of silently.
- **Hiding the block when the derived increase is small.** Same reasoning — say it, then qualify it.
- **Extending the Apply toggle to write a Replay-derived exposure.** Breaks the "never write a value you did not
  measure with" principle.
- **Putting the whole interactive block above the chart.** Displaces the evidence that justifies the block's own
  claim and puts the page's only input-plus-button ahead of the result it reacts to. The urgency problem is a
  *visibility* problem, solved by the one-sentence note.

## Known adjacent bug (not fixed here)

`CvImageUtility.AddOffset` (`Utility/CvImageUtility.cs:744-766`) rebuilds a `Star` and copies 8 of 9 fields,
dropping `RelaxationAdmitted` — while its sibling `ScaleToSourcePixels` (`:774-805`) carries it. On any ROI run
the flag is therefore cleared before `BuildStarDetectionResult` re-tallies `RelaxationAdmittedCount`, leaving
`SDefocusPrecision` silently inert.

Fixing it **changes J** for ROI + donut-detection runs, so it must not ride along with this feature. It does not
affect this work — the wizard is constructed with `region: StarDetectionRegion.Full`
(`StarDetectionOptimizerWizardVM.cs:493`) — but `MeasuredSensitivity` must be carried through `AddOffset` from
the start so it cannot inherit the same hole. Filed as a separate follow-up with its own test and a J-impact
note.

## Status

Design approved. Execution plan: `plans/optimizer-exposure-recommendation-plan.md`.
Branch: `ghilios/exposure-recommendation`.
