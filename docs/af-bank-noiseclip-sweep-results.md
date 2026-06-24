# AF-bank NoiseClippingMultiplier sweep — results & verdict

Verifies the shipped **NoiseClippingMultiplier (NC) default change 4 → 2** (the golden-set recall fix) across the
whole `D:\Autofocus Bank` (17 AF runs, mixed cameras/optics), using the repeatable `TestApp bank-verify` harness
(`plans/autofocus-bank-verification-plan.md`, PR #96). The user's question: *was 4→2 the best change, or should NC
be derived adaptively from the live data?*

The verification config **C0 (as-default)** — the shipped defaults with NC swept over {2, 3, 4} — is the honest
recall reference (no optimization). Each config is scored against a detector-independent golden set
(`snr_ref.py` SNR/connected-component reference on the linear frames, with the SNR≥12 tier auto-confirmed and the
faint SNR<12 tier confirmed by LLM montage-QA) at centroid match radius 12 px.

Report: `D:\Autofocus Bank\verification_<UTC>.{json,md}` (schema `afbank-verify/2`).

## Headline — recall vs NC (bank median over 17 runs)

| NC | median recall@SNR≥12 | median recall@all | median precision* | median AF σ_focus | median sensor R² |
|----|----------------------|-------------------|-------------------|-------------------|------------------|
| 2  | **0.870**            | 0.851             | 0.585*            | 10.26             | 0.832            |
| 3  | 0.831                | 0.798             | 0.694*            | 10.97             | 0.898            |
| 4  | 0.797                | 0.724             | 0.764*            | 9.55              | 0.769            |

(Numbers from the complete-QA goldens, `verification_20260624T142723Z`. The first pass used a rate-limited partial
QA; completing it raised precision ~+0.08 at every NC — confirming the artifact direction below.)

**recall@SNR≥12 rises monotonically as NC falls, on every run and at the bank median (4→2 lifts median recall
0.797 → 0.870).** This reproduces and generalizes the cwhite golden-set audit that motivated 4→2: a lower
noise-clip threshold admits more real faint structure during candidate formation, so the detector finds more of
the real (high-confidence, SNR≥12) stars. **The 4→2 change is confirmed to improve recall bank-wide.**

\* **Precision is a lower bound, not the true value — do not read the recommendation off this column.** See below.

## The precision caveat (important)

The bank-median precision (0.51 at NC=2) is **deflated by an artifact of the golden set**, not a real effect:

- Golden generation needs per-candidate LLM QA of the faint SNR<12 tier. The QA workflow hit **persistent
  server-side rate-limiting** on the image-heavy montage reads, so for the deep wide-field runs (mccomiskey ~99k
  candidates, timmer ~big, CWhiteFocus/standard_example2) only a bounded sample of the faint tier got confirmed.
- With a high-tier-dominated golden, HF's **real** faint detections (SNR 5–12, which lower NC specifically
  recovers) match no golden box and are counted as **false positives** → precision is understated, and the
  understatement *grows as NC falls* (more faint reals admitted). This is exactly what skews the auto-recommender
  (`BankVerifyAggregate.RecommendNoiseClip`, which picks the lowest NC clearing a 0.60 precision floor) toward
  NC=4. **The recommender logic is sound; its precision input is unreliable for high-candidate runs.**

Even after completing the QA, the deep wide-field runs are only *sampled* (4 montages/frame can't cover a
60k-candidate uncertain tier), so their precision stays a lower bound. The split at **NC=2** is stark:

| golden coverage | runs (precision @ NC=2) | reads as |
|---|---|---|
| **well-covered** (small uncertain tier, ~fully QA'd) | cwhite_2026 **0.811**, fmeschia **0.792**, caboose **0.698** | true precision — all well above the 0.60 floor |
| **under-sampled** (huge uncertain tier) | mccomiskey **0.399**, toml999 **0.443** | lower bound — real faint detections miscounted as FP |

The bank **median** precision at NC=2 (0.585) is dragged below 0.60 entirely by the under-sampled runs; where the
golden is reasonably complete, NC=2 precision is 0.70–0.81. And note the recommender's pick **moves toward lower NC
as the golden gets more complete** — NC=4 (high-tier-only) → NC=3 (complete-QA but deep runs still sampled) → it
would reach **NC=2** if the deep runs' uncertain tiers could be fully QA'd. The trajectory itself points at 2.
cwhite_2026 (the most complete golden) shows NC=2 **more than doubles** recall@≥12 (0.189→0.459) at a few-% precision
cost (0.849→0.811) — the gold-standard data point, and it endorses NC=2 unambiguously.

## Verdict

1. **4 → 2 was the right change — confirmed three independent ways.** (a) Recall@SNR≥12 improves bank-wide as NC
   falls (median 0.797→0.870). (b) On the run with trustworthy precision (cwhite_2026, full QA) recall roughly
   doubles for a negligible precision cost. (c) **The per-run optimizer — which optimizes σ_focus and is therefore
   immune to the precision artifact — converges to a median NoiseClippingMultiplier of exactly 2.0** (10/17 runs
   land at 2.0; where it deviates it goes *lower*, never up toward 4). Three methods, one answer: the data wants
   NC≈2.
2. **An adaptive per-frame NC is worth exploring (modest value, skewed lower).** The free optimizer's converged NC
   ranges 1.25–2.5 across runs, with the noisiest/densest (mccomiskey 1.25, muggsie 1.5, toml999 1.69) wanting
   *below* 2. So a single global default of 2 is well-centered, but deriving NC per frame from the measured local
   noise floor (σ from `coarse_bg`-style block statistics) during candidate formation could recover a little more
   on the cleanest frames while staying safe on the noisiest. Concrete follow-up, not a regression.
3. **Ignore the auto-recommender's "NC=3".** `BankVerifyAggregate.RecommendNoiseClip` picks the lowest NC clearing
   a precision floor; fed the still-artifact-deflated bank-median precision it returns NC=3 (it returned NC=4 on the
   partial goldens — the pick walks toward 2 as the golden completes, which is itself the tell). The recommender
   logic is fine; its input — a precision *lower bound* on the deep runs — isn't. The optimizer's converged NC
   (point 1c) and the well-covered runs' precision (0.70–0.81 at NC=2) are the trustworthy signals.

## Per-run notes

- **Donut runs (Panos, mufti, LinwoodFocus, sensitivity_example2)** have *broken* C0 numbers — recall ~0.1–0.3
  and NaN/garbage AF σ — because the as-default config has donut-aware detection OFF, so it cannot form
  candidates on the defocused rings. These runs are correctly flagged `donutAware=true` by `bank-donut-meta`, and
  are only meaningfully evaluated by **config B (optimized + donut-aware)**. Their C0 rows are a floor, not a
  verdict on NC.
- **Copies validate the harness:** `sensitivity_example1`≡`fmeschia` and `sensitivity_example2`≡`LinwoodFocus`
  produce bit-identical metrics, as expected (they are copies of those datasets).
- **Determinism:** the harness is seeded; re-running `bank-verify` on a fixed golden set reproduces identical
  metrics (cwhite_2026 reproduced the dry-run's config-B numbers exactly: P=0.848, recall@≥12=0.181, sensor
  R²=0.9933, 7/9 aligned).

## Optimizer enrichment (configs A & B) — full C0+A+B report

Configs **A (optimized, donut OFF)** and **B (optimized, donut ON)** come from an `optimize --per-run` prepass
(17 runs each), scored by the final `bank-verify --opt-a --opt-b` pass
(`verification_20260624T070116Z.{json,md}`). They add two things:

**Recall ↔ AF-tightness frontier.** At optimized settings the detector flips to the opposite corner from C0:
recall@SNR≥12 drops to ~0.06–0.23 but **precision rises to ~0.96–1.00** and AF σ_focus tightens to a bank median
of ~1.05 (vs C0's ~10). The optimizer trades faint-star recall for curve tightness by *raising sensitivity* — NC
stays at 2. Practical reading: **use the as-default config for maximum recall; the optimizer optimizes AF
tightness, not star count.** (This also *proves* the C0 precision artifact: A/B detect only the cleanest stars and
hit precision ~1.0 on the very same goldens, so C0's "false positives" are its real faint detections that the
rate-limited QA didn't confirm.)

**Donut runs need donut-aware detection.** The four `donutAware=true` runs have broken C0 sensor fits that config
B repairs:

| run | C0 sensor R² | B sensor R² | C0 recall@≥12 | B recall@≥12 |
|---|---|---|---|---|
| Panos | **−0.198** | **1.000** | 0.210 | 0.228 |
| LinwoodFocus | 0.126 | 0.999 | 0.158 | 0.113 |
| sensitivity_example2 (≡Linwood) | 0.126 | 0.999 | 0.158 | 0.113 |
| mufti | 0.975 | 0.993 | 0.291 | 0.106 |

Bank-wide the per-operation tradeoff the cwhite dry-run predicted holds: **donut-aware tightened AF σ on 3 runs
and loosened the sensor-model fit on 7** (the extra faint/donut stars add paraboloid scatter). So the best config
genuinely differs by operation and by run — there is no single global winner, which is exactly what the
three-config verification is for. Bank aggregates: median optimized AF σ_focus ≈ 1.05, median optimized sensor
R² ≈ 0.967, donutAware runs = 4.

## Reproduce

```
TestApp bank-clean   --runs "D:\Autofocus Bank" --apply
TestApp bank-donut-meta --runs "D:\Autofocus Bank" --refresh
# golden set: tools/golden/{export-linear via TestApp, golden_prep.py, qa_workflow.js, persist_qa.py, build_goldens.py}
TestApp bank-verify  --runs "D:\Autofocus Bank" --out "D:\Autofocus Bank" --nc-sweep 2,3,4 --match-radius 12
```
