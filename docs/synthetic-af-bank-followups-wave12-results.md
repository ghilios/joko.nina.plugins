# Synthetic AF bank — followups wave 12 (results)

Design: [`docs/synthetic-af-bank-followups-wave12-design.md`](synthetic-af-bank-followups-wave12-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave12-plan.md`](../plans/synthetic-af-bank-followups-wave12-plan.md).
Wave 11: [`docs/synthetic-af-bank-followups-wave11-results.md`](synthetic-af-bank-followups-wave11-results.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE — and this wave's banner is three lines, because the fields did the rest
>
> | directory | what it is | `TestApp.dll` sha256 | `BuildId` |
> |---|---|---|---|
> | `D:\hf_w12\exe` | `develop` @ `96f10b3` (PR #189's merge commit, **verified MERGED**), **pre-item-2** | `f16ccfe3…` | `103d61c4…` |
> | `D:\hf_w12\exe2` | the same tree **+ item 2** | `5368498b…` | *(stamped per run)* |
>
> Settings `D:\hf_w11\pinned_settings_w11.json`, md5 **`a67ffc06…`**, on **every** invocation. Profile
> `astrodet (ce3f3e63-…)` pinned on every arm **except item 1's fan-out, where unpinning IS the experiment**
> (§2.2). Neither directory was ever rebuilt (F53(c)).
>
> **Everything else a reader would want is a FIELD on every landing** — `BuildId`, `DetectorVersion`,
> `ProfileId`, `ConcurrencyCheck`, `FitInputs` — and this document diffs them rather than asserting them.

## Status of this document

| item | state |
|---|---|
| **RULE G12** — the gate | **PASS, 8 of 8, and BIT-IDENTICAL to K8 on all sixteen digits** on a third binary. See §1 |
| **item 1** — F55 at the LANDING level | **RULE A12 PASSES ON ALL FOUR CLAUSES. FAN-OUT IS AUTHORISED AT DEGREE 4** — 8 of 8 bit-identical while four workers loaded **four different profiles** spanning both `MaxOutlierRejections` groups. See §2 |
| **and the authorisation is worth far less than assumed** | **1.33×, not 4×.** Every run takes 1.45–2.55× longer under contention. The plan's cost case was wrong by a factor of three, and it is corrected rather than quietly restated. **New: F60.** See §2.4 |
| **item 2** — F58(d) | **All five remaining call sites converted. RULE B12 PASS** (692 leaf values identical) **and RULE B12-D PASS** (the profile moved 15 of 24 scored quantities BEFORE and 0 of 24 after). See §3 |
| **and what it was actually breaking** | **the SENSOR model, not the AF fit.** `bank-verify`'s σ_focus never moved; its tilt θ moved **7.6 %** on `muggsie`. **New: F61.** See §3.3 |
| **item 3** — F19's remainder | **`WingRejectedExcess` REFUTED on a ladder it had not seen, on THREE datasets independently and in BOTH directions. F19's REMAINDER CLOSES.** See §4 |
| **the full suite** | see §5 |

---

## §1 — RULE G12: the coordinate system on a third binary

`D:\hf_w12\gate_w12.sh`, 03:56:22–04:39:32Z, sequential, `--per-run --max-evals 250`, both pins named in the
script header.

| run | `BestJ` (exact) | 6 dp | expected | | bit == K8 | `BaselineJ` | wave 11 |
|---|---|---|---|---|---|---|---|
| `toml999` | 0.9957838768299878 | 0.995784 | 0.995784 | **MATCH** | **YES** | 0.983477 | ok |
| `CWhiteFocus` | 0.9960675916058808 | 0.996068 | 0.996068 | **MATCH** | **YES** | 0.994320 | ok |
| `uneven` | 0.9963677194179505 | 0.996368 | 0.996368 | **MATCH** | **YES** | 0.991794 | ok |
| `muggsie` | 0.9971948738498605 | 0.997195 | 0.997195 | **MATCH** | **YES** | 0.993288 | ok |
| `mccomiskey` | 0.9767460801208465 | 0.976746 | 0.976746 | **MATCH** | **YES** | 0.846082 | ok |
| `D18_m24_deep_shed` | 0.9998815090506263 | 0.999882 | 0.999882 | **MATCH** | **YES** | 0.997405 | ok |
| `D19_cygnus_deep_shed` | 0.9994870586135448 | 0.999487 | 0.999487 | **MATCH** | **YES** | 0.999187 | ok |
| `D20_m24_bright_control` | 0.9997378027339423 | 0.999738 | 0.999738 | **MATCH** | **YES** | 0.999454 | ok |

> **RULE G12: PASS, 8 of 8 to 6 dp — and every one bit-identical to all sixteen digits.** Wave 11's eight values
> have now reproduced across **three** binaries and two settings files. F41's free control passes 8 of 8 too.

**The controls, diffed rather than asserted:** `BuildId` `103d61c4…` ×8 — **differs** from wave 11's `5cb7e474…`,
as a fresh build must; `DetectorVersion` 2 ×8 (and `strings … | grep AtrousWaveletFast` hits);
`ProfileId` `astrodet` ×8; `ConcurrencyCheck` `exclusive` on **all eight, read across the arm**; `FitInputs`
`MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid`
×8.

---

## §2 — Item 1: F55 at the landing level. RULE A12, and fan-out is authorised at degree 4

### §2.1 The profile set moved, exactly as F58 says it does — and that is why it was re-snapshotted

`D:\hf_w12\profiles_before_w12.txt`, taken **after** the gate and **before** the fan-out. The gate pinned
`astrodet` eight times, and `astrodet` is now **#1 by `LastUsed`** where wave 11's snapshot had it third.
**The act of measuring reordered the set that decides what the next measurement draws.** The top four span both
groups — `astrodet` (0), `AA1600MM` (1), `AA1600MM Copy` (0), `Default-2026-08-05T10:57:42` (1) — which was
written down as the prediction for A2 **before** the arm ran.

### §2.2 The arm: eight runs, two batches of four, unpinned

| run | profile the worker drew | `MaxOutlierRejections` | `ConcurrencyCheck` | `BestJ` | bit == K8 |
|---|---|---|---|---|---|
| `toml999` | `AA1600MM` | **1** | concurrent | 0.9957838768299878 | **YES** |
| `CWhiteFocus` | `AA1600MM Copy` | **0** | *exclusive* | 0.9960675916058808 | **YES** |
| `uneven` | `Default-2026-08-05T10:57:42` | **1** | concurrent | 0.9963677194179505 | **YES** |
| `muggsie` | `astrodet` | **0** | concurrent | 0.9971948738498605 | **YES** |
| `mccomiskey` | `AA1600MM` | **1** | *exclusive* | 0.9767460801208465 | **YES** |
| `D18_m24_deep_shed` | `AA1600MM Copy` | **0** | concurrent | 0.9998815090506263 | **YES** |
| `D19_cygnus_deep_shed` | `astrodet` | **0** | concurrent | 0.9994870586135448 | **YES** |
| `D20_m24_bright_control` | `Default-2026-08-05T10:57:42` | **1** | concurrent | 0.9997378027339423 | **YES** |

| clause | verdict | evidence |
|---|---|---|
| **A1** eight values to 6 dp | **PASS** | 8 of 8 — and **bit-identical on all sixteen digits**, which is stronger than the clause asked for |
| **A2** ≥2 profiles, both `MaxOutlierRejections` groups | **PASS** | **FOUR** distinct profiles, **4 runs at `MOR`=0 and 4 at `MOR`=1** |
| **A3** the fan-out actually overlapped | **PASS** | exactly **one `exclusive` per batch** and three `concurrent` — precisely what `WaitOne(0)` must produce at degree 4 |
| **A4** `FitInputs` identical on all eight | **PASS** | one distinct value across four different profiles |

> ### **FAN-OUT IS AUTHORISED AT DEGREE 4**, and the authorisation names the degree.
>
> **A2 is what makes this mean anything.** Four processes drew four different profiles, split 4/4 on the integer
> that used to decide `J` — **pre-fix, these eight landings would have partitioned into two groups.** The hazard
> was fully present and it did not reach a single digit. A fix that pinned the profile would have produced one
> profile, one answer, and no information.
>
> **Power:** at F55's measured 44 % per-run landing rate, `P(8 of 8 | the defect is live) = 0.56⁸ ≈ 0.0097`.

### §2.3 Two limits on the authorisation, both load-bearing

- **It is for UNPINNED fan-out only.** `--profile-id` + fan-out still fails loudly (wave 11's K3), so a fanned-out
  arm cannot also pin its profile — it relies on `--settings` carrying the fit inputs, which is exactly what
  wave 11 shipped. **An arm that needs a specific profile still runs sequentially.** (That is why item 3's
  ladder below is sequential: it is pinned, so fan-out was never available to it.)
- **It names degree 4.** Nothing here says anything about 8 or 48.

### §2.4 AND IT IS WORTH 1.33×, NOT 4× — the cost case was wrong by a factor of three (F60)

| run | sequential | at fan-out 4 | inflation |
|---|---|---|---|
| `toml999` | 1 m 58 s | 5 m 01 s | 2.55× |
| `CWhiteFocus` | 8 m 33 s | 13 m 51 s | 1.62× |
| `uneven` | 6 m 16 s | 9 m 04 s | 1.45× |
| `muggsie` | 1 m 08 s | 2 m 29 s | 2.19× |
| `mccomiskey` | 10 m 28 s | 18 m 36 s | 1.78× |
| `D18_m24_deep_shed` | 6 m 36 s | 13 m 49 s | 2.09× |
| `D19_cygnus_deep_shed` | 6 m 00 s | 9 m 23 s | 1.56× |
| `D20_m24_bright_control` | 2 m 11 s | 3 m 50 s | 1.76× |
| **the arm** | **43 m 10 s** | **32 m 28 s** | **1.33× speedup — 25 % of the wall clock** |

> **`optimize` already saturates this machine** (48 OpenCV threads), so four processes contend rather than
> parallelise: the busy-time sum goes from 43 m to 76 m to buy 11 m of wall clock. **The plan said fan-out would
> make every arm "~4× cheaper". It does not — it makes them ~25 % cheaper**, and a 39-run pass goes from ~3 h to
> ~2 ¼ h rather than to ~45 m.
>
> This is recorded as **[F60](followups.md)** rather than folded into the authorisation, because the two facts
> point in opposite directions and both are true: *fan-out is now safe, and it is barely worth doing.* The
> honest consequence is that **"run it sequentially" remains the default**, and fan-out is for the case where a
> pass is long enough that 25 % is worth the loss of `--profile-id`.

---

## §3 — Item 2: the five remaining F58(d) call sites

### §3.1 What was converted, and the seam that replaced five copies of one line

`HarnessSettingsStore.BuildFitOptions(profileService, resolved)` is now the **only** way a harness runner builds
`AutoFocusOptions`. Converted: `BankVerifyRunner` (**×2** — the run-level fit *and* the `SensorModel`),
`SynthValidateRunner`, `InspectAlignRunner`, `TiltCalibrationRunner`. Each renders `FitInputs` into the output it
already writes.

**`HocusFocusPlugin.cs:119` is untouched and is CORRECT** — in the live app the profile *is* the user's settings.
It is named in the seam's own doc comment so the next reader does not "fix" it.

**The guard is on the CONSTRUCTOR, not on the five sites**, so it holds for a site nobody has written yet:
`HarnessFitSeamTests.NoHarnessRunnerBuildsItsFitFromTheActiveProfile` fails if any `TestApp` source calls the
single-argument `new AutoFocusOptions(x)`. *Wave 11 fixed `optimize` and left five call sites carrying the same
defect, each locally plausible; a rule enforced by remembering it is a rule that comes back.*

**And the guard checks itself first.** `TheGuardsOwnPatternFiresOnTheBannedFormAndNotOnTheRequiredOne` asserts the
regex matches the banned form and does **not** match the accessor-bound one, on literals, before it is run
against the tree — because *"what would this test do if the thing it checks were completely broken?"* has the
answer "sweep clean" for every source guard whose pattern has rotted.

### §3.2 RULE B12 — and it is the WEAK question, which is why B12-D exists

`bank-verify` over all 19 real-bank runs, `--nc-sweep 4`, both passes pinned to `astrodet` and to
`pinned_settings_w11.json` (whose four fit inputs **are** `astrodet`'s effective values).

> **RULE B12: PASS. All 692 compared leaf values identical to full double precision** — 453 of them numeric —
> with `fitInputs`/`profileId`/`generatedUtc`/`detectorCommit` excluded and listed as excluded.
>
> **The comparator was checked in the failing direction**: perturbing one `sigmaFocus` by `1e-12` makes it report
> exactly one difference. *A differ that cannot fail is a differ that always passes.*

**But B12 alone is equally consistent with a conversion that changed nothing.** So the discriminating half:

### §3.3 RULE B12-D — the same runs under a DIFFERENT profile, and what F58(d) was actually breaking

Three runs, same pinned settings file, scored under `astrodet` (`MOR`=0) and under `Default` (`MOR`=1), on both
binaries.

| binary | scored quantities that MOVED between the two profiles |
|---|---|
| `exe` (**pre**-conversion) | **15 of 24** |
| `exe2` (**post**-conversion) | **0 of 24** |

| run | quantity | `astrodet` | `Default` | |
|---|---|---|---|---|
| `toml999` | `sStars` | 613 | **616** | stars in the sensor model |
| | `sR2` | 0.14883143 | **0.14580555** | |
| `muggsie` | `sTheta` | 0.12407606 | **0.11525545** | **tilt θ, 7.6 % apart** |
| | `sRMS` | 6.7519583 | **6.7712252** | |
| `mccomiskey` | `sStars` | 2800 | **2856** | |
| | `sChi` | 0.11344971 | **0.12177990** | |
| all three | `sigmaFocus` | *identical* | *identical* | **the AF fit never moved** |

> **RULE B12-D: PASS — and it found something the plan did not predict.** The profile was reaching
> `bank-verify` through the **SENSOR model**, not the AF fit. `SensorModel.cs:714–715` takes
> `MaxOutlierRejections` and `OutlierRejectionConfidence` for the **per-star** curve fit, so *which stars entered
> the paraboloid* depended on which profile was active — 613 vs 616 on `toml999`, 2800 vs 2856 on `mccomiskey` —
> and everything downstream moved with them, including **tilt θ by 7.6 % on `muggsie`**.
>
> Wave 11 flagged `BankVerifyRunner` as *"×2"* and treated the second site as an afterthought. **It was the one
> that mattered.** Filed as **[F61](followups.md)**, because `InspectAlignRunner` and `TiltCalibrationRunner`
> fit sensor models too, and every tilt number produced by those harnesses before this commit carries an input
> nothing recorded.
>
> *This is the clause that makes §3.2 readable. Without it, "692 values identical" is equally good evidence for a
> working fix and for a no-op.*

---

## §4 — Item 3: `WingRejectedExcess` is refuted, and F19's remainder CLOSES

### §4.1 The ladder, and what it cost

Three datasets the wing statistics had never laddered, freshly rendered at 0.5 / 2 / 8 / 30 / 120 s into
`D:\hf_w12\ladder` (**never into the bank**), `optimize --max-evals 120`, pinned both ways, sequential
(pinned ⇒ fan-out unavailable, §2.3). **Render + 15 optimizes: 13 m 51 s total.**

| dataset | rung | wing | inner | **EXCESS** | ratio (shipped) | ratio (offline) | σ_focus |
|---|---|---|---|---|---|---|---|
| `D17_cdk14_oiii5` | 0.5 s | — | — | — | NaN | NaN | — | *product DECLINED; 0 stars on every frame* |
| | 2 s | 1.0000 | 0.6939 | 0.306122 | 1.441176 | 1.441176 | — | *no σ_focus* |
| | **8 s** | 0.0000 | 0.0000 | **0.000000** | 1.000000 | 1.000000 | **1.86179** |
| | 30 s | 0.0000 | 0.0000 | 0.000000 | 1.000000 | 1.000000 | 0.81138 |
| | **120 s** | 0.5361 | 0.4450 | 0.091106 | 1.204744 | 1.204744 | **0.09659** |
| `D20_m24_bright_control` | **0.5 s** | 0.2460 | 0.0000 | **0.246027** | +∞ | +∞ | **0.00823** |
| | 2 s | 0.0000 | 0.0000 | 0.000000 | 1.000000 | 1.000000 | 0.03488 |
| | 8 s | 0.0000 | 0.0000 | 0.000000 | 1.000000 | 1.000000 | 0.02748 |
| | 30 s | 0.0000 | 0.0000 | 0.000000 | 1.000000 | 1.000000 | 0.03058 |
| | 120 s | 0.0000 | 0.0000 | 0.000000 | 1.000000 | 1.000000 | 0.03313 |
| `D05_tec140_1000mm` | 0.5 s | 0.6040 | 0.0841 | 0.519877 | 7.178265 | 7.178265 | 0.07890 |
| | **2 s** | 0.8936 | 0.3723 | **0.521251** | 2.399931 | 2.399931 | **0.18083** |
| | **8 s** | 0.8651 | 0.0037 | **0.861389** | 235.470037 | 235.470037 | **0.04412** |
| | 30 s | 0.8446 | 0.0000 | 0.844577 | +∞ | +∞ | 0.04940 |
| | 120 s | 0.4987 | 0.0000 | 0.498698 | +∞ | +∞ | 0.12844 |

**The shipped `WingRejectedRatio` field and the independent offline recomputation agree on every rung the product
could look at** — two routes to one number, for free.

### §4.2 The verdict: three datasets fail independently, in BOTH directions

| clause | dataset | measured | implies |
|---|---|---|---|
| **W1** most-starved rung must fire | `D17`@8 s — σ_focus **19.3× worse** than its optimum | excess **exactly 0.000000** | **T ≤ 0** |
| **W2** silent at the optimum | `D05`@8 s — σ_focus **minimised** | excess **0.861389** | T > 0.861 |
| **W2** silent at the optimum | `D20`@0.5 s — σ_focus **minimised (0.00823, the best on the ladder)** | excess **0.246027** | T > 0.246 |
| **W1** most-starved rung must fire | `D05`@2 s — 3.1× worse | excess 0.521251 | T ≤ 0.521 |
| **W5** above the bank median | 39 population runs | 0.088948 | T > 0.0889 |

> **RULE W12 HAS NO SATISFYING THRESHOLD, and it is not close.** W1 requires `T ≤ 0.000000`; W2 and W5 require
> `T > 0.861389`. **`D05` contradicts itself on its own two clauses** (W1 needs T ≤ 0.521, W2 needs T > 0.861),
> so no threshold works even on a single dataset.

### §4.3 And the statistic points the WRONG WAY, which is the finding rather than the arithmetic

Across all **13 usable rungs**, against how bad the focus is (σ_focus ÷ that dataset's own best):

| σ_focus ÷ best | excess |
|---|---|
| **19.28** (`D17`@8 s) | **0.000000** |
| 8.40 (`D17`@30 s) | 0.000000 |
| 4.24 / 4.03 / 3.72 / 3.34 (`D20`) | 0.000000 ×4 |
| 4.10 (`D05`@2 s) | 0.521251 |
| 1.00 (`D05`@8 s — best focus) | **0.861389** |
| 1.00 (`D20`@0.5 s — best focus) | **0.246027** |
| 1.00 (`D17`@120 s — best focus) | 0.091106 |

> **Spearman ρ(focus badness, excess) = −0.665.** A statistic meant to say *"your wings are starved, expose
> longer"* must be **positive** here. **The excess is at its maximum where focus is best and exactly zero where
> focus is 19× worse than it needs to be.**
>
> `D17` is the sharpest case and it is this register's own poster child: a 5 nm OIII field that genuinely IS
> photon-starved, whose σ_focus improves **19-fold** from 8 s to 120 s. **At 8 s its wings and its core reject at
> identical rates — both exactly 0.0000.** The detector is not rejecting *more* in the wings of a starved run;
> on a starved run it is finding so little that there is nothing to reject anywhere.

### §4.4 F19's remainder CLOSES

> **Three statistics over one quantity have now been refuted, each by a rule fixed before it was sized:**
>
> | statistic | killed by | on |
> |---|---|---|
> | `WingRejectedFraction` (absolute) | RULE P — 76.9 % fire rate | 39-run population (wave 10) |
> | `WingRejectedRatio` (wing ÷ inner) | RULE W2 — `D16`@2 s is `+∞` at its σ minimum | wave 7's `D02`/`D16` ladder (wave 11) |
> | `WingRejectedExcess` (wing − inner) | RULE W1 **and** W2 **and** W5, on three datasets | a fresh 3×5 ladder (wave 12) |
>
> **F19's remainder closes as: NO STATISTIC OVER GATE REJECTIONS SEPARATES A STARVED WING FROM A DETECTOR THAT
> REJECTS NOISE EVERYWHERE.** ρ = −0.665 says why, and it is not a property of the arithmetic: *gate rejections
> count what the detector THREW AWAY, and a starved frame's problem is what it never FOUND.* The quantity does
> not carry the signal, and a fourth function of it would not either.
>
> **What ships: nothing.** No verdict, threshold, probe factor or action. `WingRejectedFraction` and
> `WingRejectedRatio` remain measurements with no consumer, exactly as wave 11 left them.

### §4.5 The instrument was wrong twice, and the cross-check is what said so

The offline scorer disagreed with the shipped field on one rung, twice, for two different reasons — and **both
were the scorer's fault, not the product's**:

1. First cut: no fit guard at all, so it produced `0.0` on `D17`@0.5 s where the product correctly returned
   `NaN` (`WingAxis` declines when `BestFocusPosition` is non-finite).
2. Second cut: `HardFloorPassed` used as the fit proxy — **wrong**, because `D17`@2 s has
   `HardFloorPassed = false` and the product *still* produced 1.4412. The `ExposureRecommendation` is computed
   from the **seed** evaluation, which can fit where the optimizer's ≥3-stars-per-frame floor fails.

The fix was to stop conflating two questions: *did the product look* (shipped ≠ NaN — the only rungs that can
validate the pooling) and *can this rung anchor a clause* (σ_focus finite — the only rungs W0–W3 can use).
**Neither exclusion can manufacture a refutation:** `D17`@2 s's excess is 0.306, so admitting it would make the
excess *easier* to accept. *The temptation both times was to narrow the check until it agreed; the cross-check
existed precisely to make that visible.*

---

## §5 — The suite, and what this wave did NOT run

### §5.1 The suite

**3744 passed, 0 failed, 0 skipped.** `develop` was **3740**, so **+4**, and every one is named:

| test | what it pins |
|---|---|
| `TheGuardsOwnPatternFiresOnTheBannedFormAndNotOnTheRequiredOne` | the source guard's own regex, in **both** directions, on literals — so a rotted pattern fails loudly instead of sweeping clean |
| `NoHarnessRunnerBuildsItsFitFromTheActiveProfile` | no `TestApp` source may call the single-argument `new AutoFocusOptions(x)` — the ban is on the CONSTRUCTOR, so it covers a site nobody has written yet |
| `BuildFitOptions_ReadsTheFILE_OnAllFourAxes` | the seam resolves all four fit inputs from the pinned file |
| `BuildFitOptions_RefusesToFallBackToTheProfile` | it THROWS rather than silently returning a profile-backed fit — the only reading that cannot be mistaken for a pinned run |

**The COUNT was verified, not the tick** (F37: a native test-host crash reports `Failed: 0` while ~2000 tests
never execute). Nothing was piped to `tail`, which would mask the exit code.

**And CI was verified the same way**, not by its green tick: run `31298430548` on
[PR #190](https://github.com/ghilios/hocus-focus/pull/190) reports
`Failed: 0, Passed: 3744, Skipped: 0, Total: 3744` — **the same count as local**, read out of the log. An
ABSENT check is more dangerous than a red one, and a green one that ran 1389 of 3371 tests is how F37 was
found.

### §5.2 What was NOT run, and what it would have cost

- **No 39-run population pass.** Item 3 was refuted on necessary clauses by a 15-run ladder, so a population pass
  could not have changed the verdict, and W6 bars its rows from sizing anything. **Now that fan-out is
  authorised it would cost ~2 ¼ h, not ~45 m** (§2.4) — which is the corrected price, recorded so the next wave
  budgets from a measurement rather than from the assumption this wave disproved.
- **No landing-level wavelet bisect** (`D:\hf_w10\exe_v1wav`, built in wave 10, **still unused**). Item 1 bounds
  it in one direction for free: eight landings survived a deliberately perturbed process environment
  bit-identically, so the pattern search did not amplify *any* perturbation present there.
- **F59's five knobs stay at code defaults.** Re-pinning them would move the coordinate system RULE G12 has now
  reproduced three times; it needs a new baseline and is a decision, not a cleanup.
- **F15 is still unfixed** — `optimize --per-run` still rewrites run folders with no suppress flag.
- **Waves 8/9/10's AF/wizard changes are STILL UNCONFIRMED IN THE APP.** The csproj PostBuild xcopy fails
  silently while NINA is running. Not this wave's item; not to be read as confirmed.

---

## Lessons

**1. A control that cannot fail is not a control — and "identical" is the easiest way for one to hide.**
RULE B12 compared 692 values and found them identical, which reads as a strong pass and is equally consistent
with a conversion that did nothing at all. Two things rescued it: perturbing one leaf by `1e-12` to prove the
differ could speak, and **B12-D**, which asked the opposite question — *does the profile still move anything?* —
and answered 15 of 24 before, 0 of 24 after. *Wave 11's "require the hazard to survive the fix", run in the
other direction: require the fix to be VISIBLE, not merely harmless.*

**2. The flagged-but-deferred site was the one that mattered.** Wave 11 recorded `BankVerifyRunner` as *"×2"* and
the second instance was the `SensorModel` — where `MaxOutlierRejections` decides which stars enter the **per-star**
paraboloid. `bank-verify`'s σ_focus never moved between profiles; its **tilt θ moved 7.6 %**. The defect was
found by the arm written to prove the fix was a no-op, on a quantity nobody had thought to check.

**3. Measure the payoff you are buying, not the one you assumed.** Item 1 was justified as making every future
arm "~4× cheaper". It makes them **1.33×** cheaper, because `optimize` already saturates the machine — the
busy-time sum went 43 m → 76 m to buy 11 m. The authorisation is still worth having and the cost case that
motivated it was wrong by a factor of three. *Both halves get written down, because a wave that only records the
half that justified the work is how a project acquires beliefs it never measured.*

**4. Refute on the bar your predecessors faced, and fix the rule BEFORE the data.** The first draft of W1 required
firing on *every* materially-worse rung; wave 9's rule named exactly one per dataset. That draft would have
refuted the excess on a bar neither predecessor had to clear. It was corrected and committed **before the ladder
rendered a frame** — and in the end the excess failed the weaker rule so decisively (`T ≤ 0` against `T > 0.861`)
that the distinction changed nothing. *That is the point: you cannot know that in advance, which is why the rule
moves before the data and not after.*

**5. "Could not look" needs its own state at every level, including the scorer's.** The register has said this
about `NaN`-never-0 since wave 9. This wave needed it three more times: a rung where the detector found **zero
stars**; a rung where the product declined but the optimizer had not; and a rung where the optimizer failed but
the product *had* looked. Two of the scorer's three cuts conflated two of those, and each time the honest fix was
to split the question rather than to widen the exclusion until the numbers agreed.

**6. The refutation that generalises beats the one that fits.** `WingRejectedExcess` could have been closed on
"no threshold satisfies RULE W12" and that would have been true. The finding worth keeping is
**ρ = −0.665**: the statistic is *anti-correlated* with the thing it exists to predict, because gate rejections
count what the detector **threw away** and a starved frame's problem is what it never **found**. That sentence
closes F19's remainder against a fourth candidate; an empty threshold window would only have closed it against
the third.
