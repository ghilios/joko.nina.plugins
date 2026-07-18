# Non-Negative Motor Travel for the Motorized Tilt Adapter — Plan

## Context

The EAT's step counters are absolute and EEPROM-persisted. Today the only bound is
`|position| <= TiltDeviceMaxExcursionSteps`, so automation may drive a motor to a negative counter.
Per user decision, **travel below 0 is not allowed**: the valid range becomes `[0, maxExcursion]`.

The consequence that shapes this plan: **tilt moves are differential**. A diagonal move drives one
corner `+N` and the opposite corner `−N`, so from counters at 0 *every* tilt correction would violate
a hard floor. Rather than making a zeroed adapter incapable of tilt correction, the planner
**automatically prepends a positive backfocus (piston) bias** large enough that the whole sequence
stays at or above 0.

The bias is a real optical change — all four screws move together, which shifts backfocus — so it
must be disclosed in the approval dialog and counted in the residual, never hidden.

## Design

### Enforcement (`EatTiltMotionController`)

- `ExecuteMoveAsync`: reject a move whose predicted position for any motor is `< 0`, with a distinct
  message from the over-cap case. Still strictly before anything is sent; shadow untouched.
- `PeakExcursion` becomes `RangeOf(order)` returning `(peak, trough)` — the max and min running
  position across the starting state and every intermediate state.

### Auto-bias (`OrderForMinimalPeakExcursion`)

The method keeps its name and signature (no interface churn, and `BuildPlanPreview` already treats
its output as the WYSIWYG execution order, so a prepended bias surfaces in the dialog for free). Its
contract widens: *return the exact sequence to execute*, which may begin with a bias move.

For each candidate ordering: simulate to get `(peak, trough)`; required bias `B = max(0, -trough)`.
Choose the ordering minimizing `B` first, then `peak` — least backfocus disturbance wins. If `B > 0`,
prepend `Backfocus +B`, split by `maxStepsPerCommand` when needed. Reject when `peak + B >
maxExcursion`, naming which bound failed.

### Honest reporting (`InspectorVM.BuildPlanPreview`)

Recompute the residual from the **ordered** move list rather than carrying the unbiased plan's
residual, so the uniform `+B` shows up as backfocus error instead of being silently dropped. Detect
the bias by comparing summed per-corner steps of the ordered list against the planned list.

### Disclosure

- Bias move `Description` states what it is and why.
- Approval dialog gains a warning when a bias is present, giving its backfocus effect in microns.
- Tooltips + manual describe the `[0, maxExcursion]` range and the auto-bias.

## Tasks (test-first; full suite after each)

1. `RangeOf` + the `< 0` rejection in `ExecuteMoveAsync`; tests for below-zero refusal, exactly-zero
   allowed, shadow unchanged, nothing sent.
2. Auto-bias in `OrderForMinimalPeakExcursion`; tests for bias sizing, minimal-bias ordering choice,
   bias splitting under the per-command cap, and infeasible-because-of-the-ceiling.
3. `BuildPlanPreview` residual recomputation; test that a biased plan reports the backfocus residual.
4. Dialog warning; test the warning appears only with a bias.
5. Update existing tests that assume negative travel is legal (they start from `[0,0,0,0]` and expect
   differential moves to succeed) — rebaseline them onto positive starting counters.
6. Tooltips (`Max excursion`) + `documentation/docs/overview/motorized-tilt-adapter.md`.

## Risks

- **Existing tests encode the old rule** (e.g. `EatTiltMotionControllerTests.cs:276`). Each one must
  be re-read and rebaselined deliberately, not mechanically flipped — a test that was asserting
  "negative allowed" is now asserting the opposite behavior and needs a real starting position.
- **A device already at a negative counter** cannot be recovered by the plugin under a hard floor;
  document the vendor-app re-zero path.
- Bias sizing interacts with both bounds; the ceiling check must run *after* the bias is added.
