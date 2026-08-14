# F49(b) — the "user-facing lever" question, decided

**DECISION: WON'T FIX AS ASKED. Neither candidate lever addresses the pathology F49 observed, and one of them
has already been retired for this exact reason. The lever F49(b) is reaching for is [F83](followups.md)'s —
the objective's missing precision term — and that is a different entry with a different price.**

One-sentence reason: F49's landing kept **seven times more** stars than its seed, and both candidate levers act
on the *shedding* end of the same axis — so neither would have bound on the run that motivated the question.

This document commits a decision and **ships no code**, so it owes no gate.

---

## 1. What F49(b) asks

Verbatim from the register's *Next step*:

> **(b)** Decide whether a user-facing floor on the search's Sensitivity (or F32's keep fraction, exposed) is the
> right lever, since today there is none.

**The premise is true and was re-verified at `HEAD` for this document, not inherited:**

- `OptimizerSettings.MinDetectionKeepFraction` (`StarDetectionOptimizer.cs:89`) has **no XAML binding anywhere**
  — `grep -rn 'MinDetectionKeepFraction' --include=*.xaml .` returns nothing. It is `--keep-floor` on the harness
  only.
- The search's Sensitivity lower bound is `OptimizerVariable.DefaultSensitivityLower = 0.0`, reachable only
  through the `sensitivityLower` parameter. **It is not exposed to a user either.**
- `ExposureRecommender.SensitivityFloorThreshold = 1.0` is **not** a search floor — it is the predicate for
  *"did the landing come back at the floor"*, and its own doc comment warns against confusing the two.

So: today there is no user lever. The question is whether adding one is right.

---

## 2. The pathology, stated precisely — it is ADMISSION, not shedding

F49's field session, second landing on the shipped `Default` profile:

| quantity | before | after |
|---|---|---|
| `BrightnessSensitivity` | 15.667 | **0.000** |
| `StarClippingMultiplier` | 6.750 | **0.250** |
| stars per frame | 834 | **5 766** (≈ **7×**) |
| σ_focus | 6.12 | 0.55 |

**The landing admitted seven times more candidates.** That direction matters, because it is the direction both
candidate levers do not face.

---

## 3. Candidate 1 — expose F32's keep fraction. **REFUTED, and already retired.**

`MinDetectionKeepFraction` *"rejects a candidate keeping **less than** φ of the SEED's accepted stars"*
([F32](followups.md), quoted from the entry). It is a guard against the search **shedding** detections.

**On F49's run the landing kept ~7× MORE than the seed, so the constraint is not merely unhelpful — it cannot
bind at all.** F49's own body already says this in parentheses; this document promotes it to the decision,
because a lever that cannot fire on the motivating case is not a candidate.

**And it is retired independently.** F32's status reads:

> *"`MinDetectionKeepFraction` stays default OFF **permanently**. The greedy trap itself is confirmed and stands;
> the floor is simply the **wrong instrument** for it."*

Exposing a control that is permanently-off-by-decision, to address a case it structurally cannot reach, would be
worse than leaving it hidden: it would put a knob in the options page whose honest tooltip is *"this does not
apply to your situation."*

---

## 4. Candidate 2 — a user-facing floor on the search's Sensitivity. **REFUTED on three independent grounds.**

### 4.1 It is defeatable by construction, because the axis is half of an inseparable pair

[F6](followups.md): *Sensitivity and star-clip act only in combination.* [F84](followups.md) measured the
consequence on the at-floor landings — the three with sub-1.5 combined gates are **exactly** the three where the
search **also** drove `StarClippingMultiplier` down, twice to its own 0.25 floor. F49's own run is a fourth
instance: Sensitivity 15.667 → 0.000 **and** StarClip 6.750 → 0.250, together.

**So a floor on one member of the pair does not close the admission path; it redirects it to the other member.**
The search would land at (floor, lower StarClip) and admit approximately what it admits today, while the user is
shown a control that appears to have worked.

### 4.2 It fights the objective's own gradient, so it is a floor against a real optimum

[F83](followups.md), structural and source-derived: on an **unlabelled** run — which is every run a user makes —
`OptimizationObjective.cs:484-491` composes `J = Wf·sFocus + Ws·sStars + Wc·sFit` with **no label term**, and the
one label-free false-positive cost, `SMarginalSnr`, **ships disabled** at `MarginalSnrStrength = 0.0`. `sStars`
**rewards star count.**

**Driving the gate down buys stars for free.** [F84](followups.md) measured the signature: of 24 pinned
axis-instances, **0 were never-moved and 22 were DRIVEN to the bound.** These are search outcomes, not seed
artifacts. Wave 26 then measured `Q-PIN-COSTED` / `A-RESPONSIVE` with **0 flat of 8** — the pin is a **real
optimum of an objective that cannot see precision.**

A floor therefore does not correct a mistake; it forbids the answer the objective actually prefers, while
leaving the objective unchanged. The search will sit on the floor and the landing will still be the best point
the objective can see.

### 4.3 The register has already ruled on floors of this shape

[F84](followups.md) states it directly — *"Do NOT propose a floor on the Sensitivity axis as the fix"* — on the
evidence that all 8 of 8 at-floor landings are `GateIsProvablyInert` with **zero** low-sensitivity rejections on
every frame. A gate that rejects nothing is not the thing costing the user anything, so flooring it buys nothing
measurable.

---

## 5. What the honest lever is, and why it is a different entry

**The user's complaint is not "I cannot set a floor." It is "I cannot tell whether this landing is good."** The
copy F49(a) shipped is truthful about the mechanism — the gate was lowered to admit more candidates, brightness
was not what was missing — and it names the one control that exists and works today
(`StarDetectionOptions.BrightnessSensitivity`, set by hand, then re-run in *use current settings* mode to
compare two landings).

What is missing underneath is **a number that says the admission cost something**, and on an unlabelled run
**no such number exists**: precision is unmeasured, so `J` cannot charge for it and the UI cannot report it.
That is [F83](followups.md), whose two exits are stated there and are both real work:

1. **give `J` a precision term on unlabelled runs** — a coordinate-system move that owes a fresh 42 m baseline;
   or
2. **label the bank**, activating the existing `Wl·sLabel` path.

**Neither is a lever on an options page, and neither is F49.** F49(b) asked which knob to expose; the answer is
that the knob is not the missing piece.

---

## 6. What would change this decision

Stated now, so it cannot be written after the data:

1. **A measured case where the pathology is SHEDDING rather than admission** — a landing that keeps materially
   *fewer* stars than its seed and that a user calls bad. Then F32's keep fraction is facing the right
   direction and the exposure question re-opens on its own merits. Nothing in the register records one:
   F32 measured the constraint IMPROVING `J` on most binding runs, and F49's case is 7× the other way.
2. **A floor that is shown NOT to be defeatable via `StarClippingMultiplier`** — i.e. a measurement where
   flooring Sensitivity alone materially reduces admission. §4.1 predicts it will not, and F6/F84 are the
   evidence; a paired arm at a pinned floor would settle it and has never been run.
3. **`J` gains a precision term (F83).** Then the landing at the floor stops being optimal, and the question
   becomes moot rather than answered — which is the outcome this document expects.

---

## 7. Register deltas this document owes

- **F49** — status → *(a) and (c) SHIPPED; **(b) decided: won't fix as asked**, see this document*. The entry is
  then **closed**.
- **F51** — status → *all three parts SHIPPED (wave 9); the residual is **not F51's** and is redirected to
  [F21](followups.md)/F49(c)*. The entry is then **closed**.
- **F83** — append: F49(b) resolves *into* this entry. The user-visible symptom of the missing precision term is
  a wizard landing the user cannot evaluate, and a floor is not a substitute for the term.
- **F32** — append: asked again by F49(b) and refused again, for a new reason — the motivating case is on the
  admission end, which the keep fraction does not face.
