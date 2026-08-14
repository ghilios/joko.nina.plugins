# F83 — giving `J` a false-positive cost: what is actually available, and what is actually blocking

**This is a design spec. It ships no code and owes no gate.** It exists because F83's two stated exits are not
what they appear to be: **one is refuted outright**, and the other is a term that **already exists and already
ships**, blocked by a precondition that is **not the one the register thinks**.

**HEADLINE — three findings, all source-derived, zero compute:**

1. **"Label the bank" cannot deliver precision.** With the shipped schema, `precision = 1.0` whenever the
   `ShouldReject` list is empty, and the bank's golden sidecars are **positive-only**. Feeding them in would add
   **recall** weight at `Wl = 0.25` — pushing the search *further* toward admission. It makes F83 worse.
2. **The precision term already exists, ships, and is off:** `SMarginalSnr`, *"the objective's only
   false-positive cost"*. Its source names **two** preconditions for revival. **The first — "fix the metric
   (score against truth)" — has been satisfied since 2026-08-03 and nobody noticed**, because the repair
   (`TruthProtection`) and the entry recording it (F85) landed in a different part of the register from the
   comment stating the precondition.
3. **The second precondition is the real blocker, and it is the SAME structural escape that killed
   [F49](followups.md)(b)'s floor**: the search reaches a low admission threshold through
   `PeakResponse × StarClippingMultiplier`, so any remedy keyed to the Sensitivity axis — a hard floor, or a
   statistic left-censored at the effective gate — is lifted out of its own range. **Any successor needs a
   signal the search cannot lift.**

---

## 1. What is settled and is not re-opened

- **`J` has no false-positive cost on an unlabelled run.** `OptimizationObjective.cs:483-491`, verified at `HEAD`:
  the `Wl · sLabel` term is reached only when `effRecall.HasValue && effPrecision.HasValue`. Otherwise
  `J = (Wf·sFocus + Ws·sStars + Wc·sFit) / (Wf + Ws + Wc)` and `sStars` **counts stars, not correct stars.**
- **Every user run is unlabelled**, and so is the whole bank.
- **The pin is load-bearing in `J`'s terms** — `Q26-B`, 4 of 4 costed — so this is not a search artifact to be
  fenced off. It is the objective preferring what the objective was told to prefer.
- **A floor on the Sensitivity axis is not the answer** ([F84](followups.md), and
  `docs/f49b-lever-choice-decision.md` §4).

---

## 2. Exit 2 — "label the bank to activate `Wl·sLabel`" — **REFUTED**

### 2.1 Precision in this schema is not precision

`OptimizationObjective.ComputeLabelScores` (`:1022-1043`), read at `HEAD`:

```csharp
double precision;
if (labeledShouldReject == null || labeledShouldReject.Count == 0) {
    precision = 1.0;                       // nothing that should be rejected
} else {
    var correctlyExcluded = labeledShouldReject.Count(box => !ContainsAnyCenter(acceptedCenters, box));
    precision = (double)correctlyExcluded / labeledShouldReject.Count;
}
```

**This is not `TP / (TP + FP)`.** It is *"what fraction of a curated list of junk locations did you avoid"* — a
**recall of rejections**. It can only ever charge for junk somebody enumerated in advance, box by box.

### 2.2 The bank's labels would be positive-only, so the term would be a recall term

The bank's truth artifacts are `*.golden.json` — **stars that exist**. They map onto `Missed` (recall targets).
Nothing in them enumerates *places a detection would be wrong*, and there is no such thing to enumerate on a
complete-truth dataset: the complement of the golden set is the rest of the frame, not a box list.

So a "labelled" bank run gets `ShouldReject = []`, hence `precision = 1.0` identically, hence

```
sLabel = 0.5·recall + 0.5·1.0
```

**at `Wl = 0.25`. That is a recall term wearing a precision term's name, and it points the wrong way** — it adds
weight to the very axis (more detections recovered) whose pursuit F83 is about.

### 2.3 And the label path is a human flow, not a truth flow

Labels are produced by `StarReviewVM` / `StarReviewLabels` — a review UI where a person marks boxes `Missed`,
`WronglyRejected`, `ShouldReject`, converted by `LabelConverter`. **Labelling 22 datasets × 9 frames by hand to
supply negative boxes for a *synthetic* bank whose truth is already known exactly** is the wrong instrument by
construction.

> **Conclusion: exit 2 is withdrawn.** It cannot produce a precision signal, and attempting it would bias the
> objective toward admission. Any future work here must change the *schema*, not the *data*.

---

## 3. Exit 1 — the term already exists: `SMarginalSnr`

`OptimizationObjective.SMarginalSnr` (`:805+`), gated by `MarginalSnrStrength` (default **`0.0`**, `:238`), with
`MarginalSnrFloor = 6.0` (`:204`) and `MarginalSnrMinFactor = 0.5` (`:241`).

**It is designed for exactly F83's situation**, and its design notes (`:150-168`) are worth quoting because they
answer objections a fresh proposal would have to re-derive:

- **The signal is absolutely scaled.** `Star.MeasuredSensitivity = NormalizedBrightness / noiseSigma` — a peak
  SNR in σ units, *"comparable across rigs in a way a count or a ratio would not be."*
- **It is a soft, data-adaptive floor, not a hard one** — *"a dataset can still buy its way below the floor if
  the recall gain is worth it"*; it charges for **admitted marginal detections**, not for a knob's value.
- **It is knob-agnostic below the floor** — *"it charges regardless of WHICH knob opened the door (NoiseClip,
  StructureLayers), not only the Sensitivity axis."*
- **It is near-focus only**, because far from focus a real star's per-pixel peak SNR legitimately falls.

**So the shape F83 wants is already built, tested, and flag-selectable (`--marginal-snr-strength`).** The
question is not "what term?" but "why is it off, and is that still true?"

---

## 4. The two preconditions in the source — one is met, and has been for months

The comment at `:232-237` states them:

> *"Before reviving it: **(1) fix the metric (score against truth)**, then re-establish whether F23 is real at
> all. Also note **(2) the term is structurally escapable** … any successor needs a signal the search cannot
> lift."*

### 4.1 Precondition (1) — **SATISFIED since 2026-08-03**, and the register never joined the two facts

`SMarginalSnr` was turned off because [F31](followups.md) voided the metric [F23](followups.md) was built
against: precision on real data was only ever a **lower bound**, so *"the optimizer trades away half the
precision"* rested on a number that was not measuring precision.

**That is fixed.** `TruthProtection` shipped **2026-08-03** (`aaf26e8`) into both `GoldenEvalRunner` and
`BankVerifyRunner`, and [F85](followups.md) records the consequence: **precision is truth-corrected, and 1.000
is a ceiling rather than a floor.** Wave 26's "150 FPs, 137 real" was a pre-repair quantity; `golden eval`
reported `FP = 11`.

**F83 was written 2026-08-13 and cites F31's void as current.** F85 corrected that void in the *same wave*. The
two entries sit ~1 500 lines apart and neither points at the other. **This is F106's class again** — the same
failure that made F49 look untouched — and here it left a shipped, tested term switched off on a reason that had
already expired.

### 4.2 Precondition (2) — **NOT satisfied, and it is the real blocker**

The accepted-SNR sample is **left-censored at the gate the search is tuning**. The effective gate is

```
EffectiveSensitivityGate = max(Sensitivity, PeakResponse × StarClippingMultiplier)      (StarDetector.cs:1532-1533)
```

so the search can hold `Sensitivity = 0` — free stars, no `sStars` penalty — while keeping the **clip-derived**
term at or above `MarginalSnrFloor = 6.0`. Then **no accepted star can be below the floor, the marginal fraction
is identically 0, and the penalty returns exactly 1.0.** The source names two landings that already sit there:
`D12` at `1.0 × 6.25` and `D15` at `1.0 × 6.75`.

**This is the same escape that defeats a Sensitivity floor** (`f49b-lever-choice-decision.md` §4.1, F6's
inseparable pair, F84's three landings that drove `StarClippingMultiplier` down alongside the gate). One
structural fact defeats both remedies:

> **Any signal whose range is bounded by a searchable gate can be lifted out of its own range by the search.**

---

## 5. What a successor needs, and which candidates survive that test

**The requirement, stated as a test:** *is the statistic's support controlled by any searchable axis?* If yes, it
is escapable and must be rejected without further measurement.

| candidate | escapable? | verdict |
|---|---|---|
| Raise `OptimizerVariable`'s Sensitivity lower bound | **Yes** — via `StarClippingMultiplier` (F6/F84) | rejected (F49(b)) |
| `SMarginalSnr` as shipped (accepted-SNR below an absolute floor) | **Yes** — support is left-censored at the effective gate | rejected as-is |
| Expose F32's keep fraction | Faces the **shedding** end; cannot bind on an admission pathology | rejected (F49(b) §3) |
| **Statistic on the IMAGE, not on the accepted set** — e.g. accepted-star count against the frame's own measured background σ and structure-map candidate count | **No** — the denominators are properties of the frame, which no knob changes | **survives; needs specification** |
| **Truth-scored precision (synthetic only)** | No | survives, but **helps the bank, not users** — it cannot ship in `J` |
| **Cross-frame consistency** — a real star appears at the same sky position across the sweep; junk does not | **No** — the search cannot make noise repeat across frames | **survives; the strongest candidate, and the most work** |

The last row is the interesting one and it is **not** in the register today: the bank and every user sweep
already contain 9–11 frames of the same field, and a detection that appears on one frame and nowhere else is
false-positive-like **without any truth, any labels, or any knob-derived threshold.** Its cost is that it needs
frame-to-frame registration, which the optimizer does not currently do.

---

## 6. What to do next — smallest discriminating step first

**Do NOT enable `MarginalSnrStrength` and re-baseline.** §4.2 predicts the search escapes, and an arm that
measures "the term changed nothing" would burn a 42 m gate to confirm an escape the source already names.

**Step 1 — measure whether the escape actually happens (a paired arm, and it is pre-registerable).**
The flag exists, so this is cheap:

- population: the 8 gate datasets, or a 6-cell subset; `optimize --per-run --max-evals 250`, paired
  `--marginal-snr-strength 0` vs `> 0`;
- the statistic is **not** `BestJ`. It is **`PeakResponse × StarClippingMultiplier` at the landing**, and
  whether it rises to `≥ MarginalSnrFloor` when the term is on;
- **prediction, fixed now: it rises on a majority of cells, and truth-corrected precision does not improve.**
  Refuted if precision improves materially with the clip-derived gate staying below 6.0 — in which case the
  term works and §4.2 is wrong.

**Step 2, only if step 1 refutes the escape:** re-run F23's question with the now-truth-corrected metric, and
`SMarginalSnr` becomes a shipping decision with a fresh baseline and the 42 m gate.

**Step 2′, if step 1 confirms the escape:** specify the cross-frame-consistency signal (§5, last row). That is a
new detector-side quantity, not an objective tweak, and it deserves its own spec.

---

## 7. Register deltas this document owes

- **F83** — append: exit 2 is **refuted** (§2, with the `precision = 1.0` code path); exit 1 is `SMarginalSnr`,
  which already exists; precondition (1) has been satisfied since 2026-08-03 and the register did not join the
  facts; precondition (2) is the blocker and is the **same escape** as F49(b)'s.
- **F23** — append: its successor term is off on a reason that expired when `TruthProtection` shipped; the live
  objection is escapability, not the metric.
- **F31 / F85** — append a pointer: the repair recorded there **unblocked a precondition in a different file**,
  and nothing propagated it.
- **F6** — append: the inseparable pair is now the named mechanism defeating **three** distinct remedies
  (a Sensitivity floor, `SMarginalSnr`'s censored support, and F49(b)'s lever question).
