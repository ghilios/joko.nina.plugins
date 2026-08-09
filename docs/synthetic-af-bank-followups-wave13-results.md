# Synthetic AF bank — followups wave 13 (results)

Design: [`docs/synthetic-af-bank-followups-wave13-design.md`](synthetic-af-bank-followups-wave13-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave13-plan.md`](../plans/synthetic-af-bank-followups-wave13-plan.md).
Wave 12: [`docs/synthetic-af-bank-followups-wave12-results.md`](synthetic-af-bank-followups-wave12-results.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | tree | `develop` @ `f9f2074` — PR [#190](https://github.com/ghilios/hocus-focus/pull/190)'s merge commit, **VERIFIED MERGED** 2026-08-09T13:24:54Z |
> | binary | `D:\hf_w13\exe` — `TestApp.dll` sha256 `c4bf3280…`, `NINA.Joko.Plugins.HocusFocus.dll` sha256 `4c057544…`, `BuildId 62334f10…` |
> | detector | `strings … \| grep AtrousWaveletFast` **hits** ⇒ `DetectorVersion` 2 (confirmed as a FIELD on all 8 gate landings) |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06…`** — `MaxOutlierRejections` = **0** |
> | settings **S1** | `D:\hf_w13\pinned_settings_w13_mor1.json` md5 **`89e14ef8…`** — **one line differs, one `Options` key differs** |
> | profile | `astrodet` `ce3f3e63-…` pinned on **every** arm |
> | profile set | `D:\hf_w13\profiles_before_w13.txt`, snapshotted before any arm |
>
> **Item 3's F15 change is deliberately NOT in this binary**, so nothing in item 1 rides on a code change made
> in the same wave. **No fan-out**: every arm is `--profile-id` pinned and a pinned arm cannot fan out at all
> (wave 11's K3); wave 12 measured the authorisation at **1.33×**, not 4× ([F60](followups.md)).

## Status of this document

| item | state |
|---|---|
| **RULE G13** — the gate | **PASS, 8 of 8, BIT-IDENTICAL to K8 on all sixteen digits, on a FOURTH binary.** See §1 |
| **item 1** — `MaxOutlierRejections` as a product default | *(§2)* |
| **item 2** — waves 8–12 in the app | *(§3)* |
| **item 3** — F15 | **FIXED.** Opt-in `--update-run-folder`, the absent write prints, and the backup keeps the OLDEST displaced landing. 8 tests, guard verified to fail against `f9f2074`. See §4 |
| the suite | *(§5)* |

---

## §1 — RULE G13: the coordinate system on a fourth binary

`D:\hf_w13\gate_w13.sh`, 14:09:30–14:51:16Z (**41 m 46 s**), sequential, `--per-run --max-evals 250`, both pins
named in the script header.

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

> **RULE G13: PASS, 8 of 8 to 6 dp — and every one bit-identical to all sixteen digits.** Wave 11's eight values
> have now reproduced across **four** binaries and two settings files. F41's free control (`BaselineJ`) passes
> 8 of 8 too.

**The controls, diffed rather than asserted:** `BuildId` `62334f10…` ×8, **differs** from wave 12's `103d61c4…`
as a fresh build must; `DetectorVersion` 2 ×8; `ProfileId` `astrodet` ×8; `ConcurrencyCheck` `exclusive` on
**all eight, read across the arm**; `FitInputs` one distinct value —
`MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid`.

### §1.1 The scorer refused to guess, and it was right to — about me

The first scoring attempt passed `D:\hf_w13\gate` (a **Windows** path) to a scorer running under WSL.
`os.path.join` produced a path that cannot exist, and the scorer reported **8 × `UNEVALUATED`** and a **FAIL**.

It would have been just as easy to write a scorer that reported eight missing files as eight zeros, or as a
quiet "no differences found". **A check that could not run must never look like a check that ran** — the state
exists because wave 12's scorer was wrong twice for want of it, and the first thing it caught in wave 13 was the
person who wrote it. Re-scored against `/mnt/d/hf_w13/gate`: PASS, 8 of 8.

---

## §2 — Item 1: `MaxOutlierRejections` as a product default

### §2.0 The fact that falls out first, and holds whatever the verdict is

`AutoFocusOptions.cs:62` resolves `MaxOutlierRejections` with a code default of **1**, and `InitializeOptions`
sets **1**. The pinned harness file has carried **0** since wave 5, because that is `astrodet`'s value and
`astrodet` is the pinned profile.

> **Every σ_focus and every `J` this project has published — waves 5 through 12 — was measured with the Grubbs
> rejection DISABLED, which is not the shipped default.** No wave has said so. It does not invalidate anything
> (the same value was used throughout, which is what RULE G13 keeps proving), but any sentence describing
> `pinned_settings_w11.json` as "the shipped detector" is wrong in this one field.

### §2.1 I1 — the primary arbiter, out of sample, on the 20 synthetic datasets

`af-fit` evaluates rejection budgets 0…3 **on one detection pass**, so the two arms are perfectly paired:
identical frames, identical detections, identical measured `(position, HFR, σ)` points, and nothing differing
but the budget. `D:\hf_w13\affit_syn`, 14:52:06–14:55:52Z — **3 m 46 s for all twenty**.

**The control on the control passed 20 of 20:** every log carries the `Settings: …pinned_settings_w11.json`
line, which is printed *only* on the fall-through path, so the pinned detector demonstrably took on every
dataset rather than being assumed to. *An instrument that is not connected reports perfect agreement.*

`e_b = |minPos_b − OptimalFocuserPosition| / step`, in step units.

| | count |
|---|---|
| **datasets where the rejection never fired at all** (tie by construction) | **16 of 20** |
| datasets where it fired | **4 of 20** |
| unusable (NOFIT / UNEVALUATED) | 0 |
| **wins for `MOR`=0** | 3 |
| **wins for `MOR`=1** | **0** |
| median paired \|Δe\| over the four | **0.00128 step** (clause (b) needs ≥ 0.10) |

> ### **RULE M13 / D1: NO DOMINANCE. D1 does not fire.**
>
> Clause (a) needs ≥ 15 of 20 and the best either value manages is **3**. Clause (b) needs ≥ 0.10 step and the
> median displacement is **0.00128** — **78× below the floor**, and the largest single displacement in the whole
> population is 0.0099 step. **The pre-registered "no change" outcome is the outcome.**

### §2.2 And the four firings say something the verdict does not

| dataset | rejected point, in steps from TRUE focus | σ_focus 0 → 1 | Δσ | e 0 → 1 | truth says |
|---|---|---|---|---|---|
| `D01_ultrawide_40mm` | **1.00** | 0.6144 → 0.5264 | **−14.3 %** | 0.00111 → 0.00111 | unchanged |
| `D08_c11_2800mm` | **2.00** | 0.7897 → 0.4971 | **−37.0 %** | 0.01829 → 0.01951 | **worse** |
| `D12_c14_585_afbin2` | **3.00** | 1.5717 → 1.7766 | +13.0 % | 0.00142 → 0.01135 | **worse** |
| `D16_esprit550_ha3` | **0.00 — the IN-FOCUS point** | 0.6378 → 0.4069 | **−36.2 %** | 0.00067 → 0.00200 | **worse** |

> **σ_focus improved on 3 of 4. The distance to the generator's own true focus improved on 0 of 4.** They agree
> on one dataset out of four, and on `D08` and `D16` they point in *opposite* directions — the reported
> uncertainty falls by more than a third while the actual error grows.
>
> **This is exactly why RULE M13 put the arbiter out of sample, and it is the wave's most transferable result.**
> σ_focus is the parametric standard error of the fitted minimum, computed on the points that SURVIVED the
> rejection. Discard the worst-fitting point and it must fall; that is arithmetic, not evidence. A wave that had
> scored this arm on σ_focus — the quantity waves 9–12 argued about — would have concluded that allowing the
> rejection improves the fit by a third, on data where it never once moved the answer closer to the truth.
>
> **`D16` is [F45](followups.md) reproduced with ground truth.** The point the Grubbs test discards is position
> **8000 — the true focus** — on a fit with R² = 0.9954, and the vertex then moves *away* from it. F45 found
> this once, on one field run, and could only say the discarded point "is the most informative one in the sweep".
> It is now reproduced on a dataset where the right answer is known by construction.

**The rejection is not gratuitous where it fires, and the budget is the only thing bounding it.** The per-round
Grubbs detail shows the mechanism F45 named — a robust scale applied to a good fit inverts its own purpose —
as a *cascade*: on `D01`, removing one point tightens `MAD(r)` from 0.9885 to 0.0570 and the surviving points'
z-scores jump to **22.4, 32.7, 22.4**; on `D08` round 3 the MAD collapses to 0.0002 and z reaches **503.6**.
Each removal makes the next point look wilder. Production stops at 1 or 2 because the *budget* stops it.

**Fire rate is a property of the confidence too, and this arm ran at the more permissive value.** The pinned
`OutlierRejectionConfidence` is 0.95; F45's field case ran at 0.99. The Grubbs limit at N = 9 is **2.2150** at
0.95 against **2.3868** at 0.99, so **this arm fires more readily than the case that raised the complaint** —
which makes 16 of 20 silent the stronger observation, not the weaker one.

### §2.3 I3 — `bank-verify` ×2 over the 19 real runs: D0, D2 and D3

Two passes, 19 real-bank runs each, `--nc-sweep 4`, both pinned to `astrodet`, differing in **one key of one
file**. 14:55:53–15:18:07Z, 11 m per arm.

**The sweep is shown to have taken before anything is read from it.** Each report echoes its own fit inputs:

```
fitInputs MOR=0: MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid
fitInputs MOR=1: MaxOutlierRejections=1;…
fit inputs differ in EXACTLY: MaxOutlierRejections 0 -> 1   <-- the sweep took
```

The comparator **aborts** if they differ in anything else, or in nothing — a settings sweep that changed two
things is not a settings sweep, and one that changed nothing is not a sweep at all.

> #### **D0: PASS. `recallHigh`, `recallAll`, `precision`, `detections`, `framesAligned`, `scoredFraction` — 0 of 19 moved, to full double precision.**
> `MaxOutlierRejections` reaches the fit and nothing else, exactly as the arm claims. Had one of these moved,
> item 1 would have reported no decision.

> #### **D2: `sigmaFocus`, `afR2`, `afChi` — 0 of 19 moved.** On the real bank the run-level AF fit does not move at all.

**That reading needed a control, because it has two explanations and they are not the same fact:** *(a)* the
Grubbs test genuinely never fires on these 19 curves, or *(b)* the knob never reaches `bank-verify`'s run-level
fit. `BankVerifyRunner.cs:314` assigns `fitConfig.MaxOutlierRejections = afOptions.MaxOutlierRejections` and
`RunEvaluationData.cs:820` hands it straight to `SelectBestModel`, so *(b)* is ruled out **by reading** — and a
code read is weaker than a measurement, in a register whose recurring failure is *an instrument that is not
connected reports perfect agreement*. So a **third arm at `MaxOutlierRejections` = 3** asked the instrument to
move.

#### §2.3.1 The positive control fires — and it is the most useful result in the item

`D:\hf_w13\bv_mor3`, 15:19:48–15:31:18Z, one key changed again (md5 `3350cb45…`).

| | `MOR` 0 → 1 | `MOR` 0 → 3 |
|---|---|---|
| `sigmaFocus` moved | **0 / 19** | **3 / 19** |
| `afR2` moved | 0 / 19 | 3 / 19 |
| `afChi` moved | 0 / 19 | 3 / 19 |

> **The run-level AF fit path is live and measurably responsive.** "0 of 19 at budget 1" is therefore a
> **measured property of the curves**, not a disconnected instrument — which is the whole reason the arm was
> run, and it cost 11 minutes.

**And where budget 3 does fire, it is a disaster.** These are real bank runs, not synthetic:

| run | σ_focus | R² | reduced χ² |
|---|---|---|---|
| **`caboose`** | **1.4325 → 18.185** (**12.7× worse**) | **0.99987 → 0.9235** | **0.00427 → 3.342** (783×) |
| `LinwoodFocus` | 1.9226 → 2.4276 (+26 %) | 0.99711 → 0.98735 | 0.1791 → 0.6147 |
| `Panos` | 130.42 → 101.50 (better) | 0.99871 → 0.99976 | 0.2821 → 0.0796 |

> **`caboose` is F45's mechanism running to completion on a real run.** Its fit at budget 0 has R² = **0.99987**
> — as near perfect as this bank gets — and that is precisely what makes it vulnerable: the residuals are tiny
> and tightly clustered, the MAD collapses, ordinary scatter reads as a wild outlier, and each removal tightens
> the scale for the next. Given three rejections the test eats the curve and the reported focus uncertainty
> grows **twelve-fold**. *`Panos` improves, and it is the one run in the bank with a known-degenerate σ fit.*
>
> **So the shipped default is not merely harmless — it is the bound.** `MaxOutlierRejections` = 1 is safe on
> this population *because it never fires*, and the only thing standing between a near-perfect fit and
> self-destruction is the smallness of the budget. That is an argument against ever raising it, and it is an
> argument the project did not have before this arm.

> #### **D3: the SENSOR fit moves on 15 of 19 runs — F61's prediction, now on a population and with ONE variable changed**

| quantity | moved | note |
|---|---|---|
| `sStars` | **5 / 19** | the star population admitted to the paraboloid |
| `sR2` | 15 / 19 | |
| `sRMS` | 15 / 19 | |
| `sChi` | 15 / 19 | |
| `sTheta` | **15 / 19** | **the published tilt angle** |

**`sTheta` has no "better" — it is an estimate of a physical property of the rig, not a quality score** — so it
is reported as a magnitude:

| run | θ at `MOR`=0 | θ at `MOR`=1 | relative |
|---|---|---|---|
| `fmeschia_Focus` | 0.0316753 | 0.0274381 | **−13.38 %** |
| `muggsie` | 0.1240761 | 0.1151037 | **−7.23 %** |
| `timmer` | 0.0014139 | 0.0013272 | −6.13 % |
| … 12 more | | | median **0.67 %** |

> **Wave 12 measured `muggsie`'s θ moving 7.6 % between two PROFILES. This arm moves ONE KEY IN ONE FILE on the
> same run and gets 7.2 %.** Wave 12's pair was `astrodet` vs `Default`, and those two profiles differ in
> `OutlierRejectionConfidence` as well (0.95 vs 0.99) — so its 7.6 % conflated two changed fit inputs. **The
> clean one-variable answer is 7.2 %, and it pins the cause to the integer** rather than to the profile
> machinery that carried it.

**And the goodness-of-fit half needs the population held constant to mean anything.** At `MOR`=1 more stars can
survive their own per-star fit, so `sRMS` over a larger set is not comparing like with like. Splitting on it:

| subset | `sChi` | `sRMS` | `sR2` |
|---|---|---|---|
| **14 runs with an UNCHANGED star population** | **`MOR`=0 better on 10 of 10** that moved | `MOR`=0 better on 9 of 10 | `MOR`=0 better on 7 of 10 |
| 5 runs where `sStars` changed | not like-for-like — reported, not scored | | |

> **The sensor paraboloid fits WORSE when each star's own curve is allowed a rejection**, on the subset where
> the same stars are being fitted both times. That is the same mechanism D1 found one level up: dropping a point
> from a short, well-behaved curve costs more information than the outlier it removes.
>
> **F61's warned-of asymmetry does NOT materialise.** The AF fit is silent, the sensor fit prefers 0, and the
> out-of-sample arbiter never prefers 1. There is nothing to average and no conflict to report — which is worth
> stating precisely *because* it was pre-registered as the outcome that would have blocked a decision.

### §2.4 I2 — the same budget table over the 19 real bank runs

Same instrument, same pinning, `D:\hf_w13\affit_real`. **Population verified by COUNT: 19 of 19**, and the
`Settings:` positive control present in all 19 logs.

> **3 of 19 fire. 16 are silent** — the same ~20 % rate as the synthetic bank, on data nobody generated.

| run | rejected | vertex 0 → 1 | \|Δvertex\| | σ_focus 0 → 1 | \|rejected − vertex\| |
|---|---|---|---|---|---|
| **`toml999`** | 4174 | 4147.76 → 4150.00 | **0.107 step** | **0.9133 → 0.2704 (−70 %)** | 1.25 step |
| `LinwoodFocus` | 21184 | 21223.3 → 21222.0 | 0.052 step | 0.4996 → 0.3430 (−31 %) | 1.57 step |
| `mufti` | 2725 | 2609.15 → 2608.36 | 0.008 step | 6.7671 → 6.8571 (+1 %) | 1.16 step |

> **`toml999` is where waves 9–12 started.** [F57](followups.md) recorded its `BaselineJ` moving by 0.0144 and
> could only attribute it to "the active profile"; [F58](followups.md) narrowed that to `MaxOutlierRejections`.
> **Here is the whole of it, on one line: one measured point at 4174 is discarded, the fitted vertex moves
> 0.107 step, and the reported σ_focus falls by 70 %.** Two waves of investigation resolve to a single rejected
> point on a single curve.

**And this is where the two halves of the wave meet.** On the real bank the rejection makes σ_focus look **up to
70 % better** and there is no truth to check it against. On the synthetic bank, where the truth exists, an
identical-looking improvement (−37 %, −36 %) bought **no** improvement in accuracy on any dataset and a loss on
three. *The real bank cannot tell you that. It is exactly what the synthetic bank is for.*

**Two honest limits on the fire rate.**

- **F45's "the rejected point is the in-focus one" is one case, not the rule.** On the real bank the discarded
  point sits a median of **1.25 steps** from the fitted focus (1.16 / 1.25 / 1.57). `D16`'s exact-zero is the
  sharpest instance, not the typical one.
- **The fire rate is a property of the DETECTOR, not just the fit.** `bank-verify` scores these same 19 runs at
  `C0@nc4` and sees **0 of 19** fire; `af-fit` scores them at the pinned file's detector and sees 3. The
  detector decides the HFR curve, and the curve decides whether the Grubbs test has anything to reject. So "20 %
  of runs" is not a constant of nature — it is a number for *these settings*, and no fire-rate figure in this
  document should be quoted without them.

### §2.5 What I1 cannot say

Four firings is four. The 3–0 direction is consistent with F45 and is **not evidence at any conventional bar**
(a 3–0 sign test is *p* = 0.25 one-sided); what the arm establishes is the **magnitude bound** — on this
population the knob cannot move a fitted focus by as much as a tenth of a step — and the **σ_focus/truth
divergence**, which does not depend on the sample size in the same way because it is a statement about what the
metric measures.

---

## §3 — Item 2: the app confirmation. BLOCKED, and this time the blocker is NAMED

**The pre-registered procedure ran, in order, and the step that has silently defeated four waves PASSED.**

| step | result |
|---|---|
| NINA not running before building | confirmed — `tasklist` clean |
| plugin built | `Build succeeded` |
| **deployed DLL compared to the freshly built one BEFORE launching** | **sha256 `4d1dd6b325466d88…` on both, same mtime — DEPLOY VERIFIED** |
| launched with `Start-Process` (not the MCP launcher) | NINA pid 76672, `Responding = True`, window handle non-zero |
| capture the UI | **FAILED** |

> **The csproj `xcopy` was NOT the blocker this wave.** The DLL NINA would have loaded was byte-identical to the
> one just built, verified *before* launch. That check is cheap, it worked, and it should be the first line of
> every future attempt.

### What actually blocked it

```
> query session
 SESSIONNAME     USERNAME    ID  STATE
                 ghili        1  Disc          <-- the interactive session is DISCONNECTED
 console                      2  Conn
Screens: WinDisc {X=0,Y=0,Width=1280,Height=800}   <-- the disconnected-session virtual display
```

**There is no composited desktop to capture.** Three routes were tried and all three fail for the same reason:

1. the Windows MCP's `Screenshot` / `Snapshot` → `screen grab failed`, twice;
2. `Graphics.CopyFromScreen` over the virtual screen → a **uniformly blank** 1280×800 PNG;
3. `PrintWindow` with `PW_RENDERFULLCONTENT` on NINA's own `hwnd` → returns `True` and produces the **title bar
   only**; the WPF client area comes back blank, because WPF renders through DWM composition that a
   disconnected session does not run.

**And NINA never finished starting.** After 16 minutes it had consumed **5 s of CPU**, held 121 MB, and written
**no log file at all** — while every `TestApp` process in the same window wrote one immediately. A NINA that
had reached plugin loading would have logged it. So the app was not merely invisible; it was stuck.

NINA was then **killed deliberately**, before chain B started, because of a hazard no wave has recorded:
`Profile.Load` holds the `.profile` open, so a running NINA makes `--profile-id`-pinned harness arms throw
*"No active NINA profile could be loaded"*. **A live UI check and a pinned measurement arm cannot share this
machine.**

### What IS confirmed, and it is not nothing

Every one of A1–A9 has **offline coverage of its logic**, and this wave closed the one real gap in it:

| # | change | offline coverage |
|---|---|---|
| A1 | AF chart info rows survive a round trip | `HocusFocusVMChartReloadTests` — 12 tests |
| **A2** | **the loaded-report lookup** | **`RealReportRoundTripTests` — 3 NEW tests (below)** |
| A3/A9 | exposure-recommendation + Star signal block | `StarSignalCopyTests` — 28 tests |
| A4/A5/A6 | in-run guidance, re-run button, abort advice | `StarDetectionOptimizerWizardVMTests` — 180 tests |
| A7 | F32's exposed restart | `StarDetectionOptimizerTests` |
| A8 | the capped step says what it converges toward | `OptimizationSummaryTests` — 41 tests, asserting the F49(c) text names the target, the measured ratio, the per-run growth and *"partial step"* |

**A2's gap was real and is now closed.** Wave 8 found the loaded-run info rows *"NEVER worked"* in the field
while their tests passed, and named the reason: `HocusFocusReport` carries three **interface-typed** option
blocks, Newtonsoft cannot construct an interface, and the fixtures built reports *without* those blocks — so
the production writer's output was never round-tripped by anything. **A test written against a hand-built
object cannot find a defect that only exists in what the product writes.** The new fixture is a genuine report
off a real run (paths sanitised, nothing else touched), and the first test asserts the fixture **still breaks**
the naive read:

```
APlainDeserializeOfARealReportSTILLTHROWS ............... JsonSerializationException ("interface or abstract")
TheProductionLookupReadsTheSameFileAndKeepsTheInfoRowFIELDS  FinalHFR 1.9076325878204998, InitialFocusPoint 25000
TheFixtureStillCarriesTheThreeInterfaceTypedBlocks ...... all three present
```

*A fixture that has stopped exercising the defect turns the test file into decoration, so it is checked first.*

### What remains unconfirmed, BY NAME

> **All nine of A1–A9 remain unconfirmed AS RENDERED PIXELS.** What is untested is the XAML half — that these
> strings and rows appear, in the right panel, unclipped, in the running app. The logic that produces every one
> of them is covered offline, and A2's coverage went from *absent-and-believed-present* to real this wave.
>
> **This is the fifth wave carrying it, and the honest statement has changed shape:** it is no longer "not this
> wave's item". It is **"the machine has no attached display, and a WPF UI cannot be captured without one"** —
> which is a fact about the environment, reproducible in one command (`query session` → `Disc`), and fixable
> only by running the check from a **connected** session.

**Price of finishing it:** ~30 minutes of a connected desktop session. The procedure above is correct and its
expensive step (the deploy check) already passes; only the capture needs a real display.

---

## §4 — Item 3: F15, and the bank stops being a shared mutable global

`optimize --per-run` wrote its landing back into **every discovered run's own source folder** as well as into
`--out`, with no way to suppress it. It destroyed `bobp_m101`'s historical `sens 50 / clip 9.5` row
mid-investigation, silently re-baselined **both** banks in wave 3, and was the last thing forcing whole passes
to be serialized against one another: two passes over the same bank collide **in the bank**, however carefully
their `--out` directories are kept apart.

**The write is now opt-in behind `--update-run-folder`.** The exact invocation thirteen waves used —
`optimize --per-run --runs <bank> --out <dir>` — no longer touches either bank.

**The capability is KEPT rather than deleted**, because `bank-verify --opt-a/--opt-b`,
`golden eval --params optimized` and `review --runs <same>` all locate the landing by the run-folder copy. The
fix is not to remove the hazard; it is to make it something a command line **says**.

Two choices carry the weight, and both are the same lesson at different scales:

| choice | why |
|---|---|
| **the ABSENT write prints** | it names how many run folders were left untouched *and* the three readers that will therefore see whatever was there before. *An absent side effect has to be visible, because the whole defect was that it was not.* |
| **the backup keeps the OLDEST displaced landing, not the most recent** | the file worth keeping is the one nobody can reproduce. Every landing written since is reproducible from a recorded command line and survives in its own `--out` dir. **A rolling backup would have lost the irreplaceable file on the second pass** — which is F15's own failure, re-implemented one level down |

The backup's name is deliberately not `optimized_settings.json`: every bank reader matches that name **exactly**,
so a backup sharing it would be read back *as* a landing. That is `SettingsHandoffFileName`'s reasoning, and it
is now asserted rather than remembered.

**Guarded by 8 tests** in `LandingWritebackTests`. The policy lives in `TestApp/LandingWriteback.cs`, outside the
WPF-bound runner, so it is tested against a real filesystem instead of asserted about. The flag is matched
**exactly** — `--update-run-folder-never` and `--no-update-run-folder` must **not** opt in, because a prefix
match would let a future flag silently re-enable the write, which is the shape of the defect rather than a
nuisance. The source guard that keeps the write loop gated **checks its own pattern in both directions on
literals first**, and was verified to **FAIL against `f9f2074`'s source** before it was called a test:

```
PRE-CHANGE (HEAD)            guard matches: False
POST-CHANGE (working tree)   guard matches: True
```

**What this does not undo.** Both banks' stored `optimized_settings.json` are still the last landing some wave
wrote into them; nothing here recovers wave 1's. Wave 13 snapshotted all **42** of them to
`D:\hf_w13\bank_settings_snapshot\` before its own arms ran — the first time that has been done — and this
wave's own `--per-run` arms, which run on the **pre-fix** binary by design, are the last that will need it.
