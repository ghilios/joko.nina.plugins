# Synthetic AF bank — followups wave 8 (plan)

Design: [`docs/synthetic-af-bank-followups-wave8-design.md`](../docs/synthetic-af-bank-followups-wave8-design.md).
Baseline: `develop` @ `e8eb8b0`. Suite **3661**. Branch: `ghilios/synthetic-af-bank-followups-wave8`.

**Ordering decisions already taken in the design — do not re-litigate mid-execution.** F32's confirmation arm is
deferred with its cost stated and its trigger fixed (§0.1). F19(c) is deferred to wave 9 because it re-renders
`D14`, which F39(b)'s adoption is simultaneously measuring (§0.2). F39(b)'s status-quo arm runs **FIRST** and the
adopted arm **LAST** (§0.3).

## Working directories

```
Build:  dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w8\exe'
Tests:  dotnet.exe test "$(wslpath -w <abs>/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
Arms:   D:\hf_w8\{p0,p1,p2,p3,adopt,armX}\   scripts + logs alongside
Pinned: D:\hf_w8\pinned_settings.json  (copied from D:\hf_w7\pinned_settings.json — one file for the whole wave)
```

`--settings "$S"` on **every** arm (F42). After the first arm of each step, grep its log for
`Simple-mode presets override` and confirm the count is **0**.

---

## Step 0 — Branch, build, pin

1. `git checkout -b ghilios/synthetic-af-bank-followups-wave8` off `develop` @ `e8eb8b0`.
2. Build `TestApp` into `D:\hf_w8\exe`. Copy `D:\hf_w7\pinned_settings.json` → `D:\hf_w8\pinned_settings.json`.
3. **Inertness control, before anything else** (design §5): run `D:\hf_w7\dryrun_diff.sh` shape against
   `D:\hf_w8\exe\SynthBank\synthetic-bank-spec.json` on this branch vs a `develop` @ `e8eb8b0` build
   (`D:\hf_w8\exe_ctl`), diffed on the derived-parameter lines over all 20 datasets. Expect **IDENTICAL** at this
   point and again at the end of the wave. Record both runs.
4. **R3 (design §1.5): reproduce wave 7's arm-G1 numbers on the new binary** before believing any sweep. Run
   `golden eval --detection-binning 2` on `D12` and `D15` with wave 7's exact flags. `D12` recall@high must read
   **0.813**, `D15` **0.874**. If either differs, stop — the instrument moved and no §1 number is readable.

**Gate:** step 0.4 reproduces exactly.

---

## Step 1 — F46 arm P0: the blend check (no run, no code)

Script `D:\hf_w8\p0\blend_check.py`. Inputs already on disk:
`D:\hf_w7\golden\{D12,D15}_bin{1,2}\attempt01\detected_f*.csv` and the per-image `*.golden.json` in
`D:\SyntheticAutofocusBank\{ds}\attempt01`.

Per frame, reproduce the centroid match (radius 12 px, the value both arms used) and compute:

- the **lost-bright set**: golden boxes with `confidence == "high"` matched at bin1 and unmatched at bin2;
- the **kept-bright set** on the same frames — the comparison group R2 requires;
- for every star in both sets: nearest golden-neighbour distance (px, and in units of the frame's in-focus HFR),
  whether any bin-2 detection centre lies within 12 px, that detection's bbox `w`/`h`, and distance to the nearest
  frame border.

Report: the two nearest-neighbour distributions side by side, the fraction of the lost-bright set with a neighbour
within 2× in-focus HFR **and** a bin-2 detection within 12 px, and the bbox-size distribution of the lost set.

**Apply R2 verbatim.** Record CONFIRMED or REFUTED with the numbers, either way.

---

## Step 2 — F46 arms P1/P2: the knob sweeps (no code)

Script `D:\hf_w8\p1\minbox_sweep.sh`. Wave 7's flags exactly, one variable added:

```
golden eval --runs "D:\SyntheticAutofocusBank\<ds>" --params default --defocus-donut \
  --detection-binning 2 --min-box <v> --match centroid --match-radius 12 --settings "$S" --out ...
```

- **P1:** `D12`, `D15` × `--min-box ∈ {2,3,4,5}`. `5` is the arm's own control and must reproduce step 0.4.
- **P2:** `D12`, `D15` × `--structure-layers ∈ {3,4,5}`. `4` is the control.
- **Confirmation:** whichever value (if any) satisfies R1 on both, re-run on `D08` and `D10` — the other two
  recall@high losers — before believing it generalizes.

Read `recall@high`, `recall@all`, `precision`, and the FN attribution from each run.

**Apply R1 and R4 verbatim.** A knob that raises recall@high AND recall@all AND precision on both datasets is
treated as an instrument fault until explained (R4).

**Branch point:**
- **R1 CONFIRMED** → the fix is a units bug. Go to step 3.
- **R1 REFUTED** → skip step 3, go to step 4 (per-tier attribution), then step 5 (band re-derivation).

---

## Step 3 — F46: ship the knob fix, if R1 confirmed

Only reachable when a knob setting clears the band on all four affected datasets.

1. Decide where the scaling belongs. If it is genuinely a units bug, it belongs in
   `DetectionBinningResolver.ApplyFactor` / `HocusFocusStarDetection.ApplyDetectionImageContext` — i.e. it changes
   the **product**, not just the harness, because a live user at binning 2 pays the same cost today.
2. **That makes it a product behaviour change and it gets its own before/after.** Update
   `DetectionBinningResolver`'s class doc, which currently states the opposite rule (design §1.2) — a fix that
   contradicts the documented intent without editing the doc leaves the next reader with two truths.
3. Tests: at least 2 **discriminating** (revert the scaling ⇒ exactly those fail) plus a guard that factor 1 is
   bit-identical.
4. Re-run the four datasets at factor 2 and confirm the band is met with the fix in the product path rather than
   via a CLI override.

---

## Step 4 — F46 arm P3: per-tier FN attribution (small code, conditional)

Only if steps 1–2 leave the mechanism unnamed.

1. `GoldenEvalRunner`: key the FN attribution by confidence tier as well as gate (the rank is already computed one
   loop earlier at the per-confidence recall block). Print the high-tier attribution as its own section; keep the
   existing aggregate section unchanged so every prior report stays comparable.
2. Tests: 1 discriminating (a high-tier FN and a low-tier FN at the same gate land in different buckets), 1 guard
   (the aggregate section's totals are unchanged).
3. Re-run `D12` and `D15` at factor 1 and factor 2. **Name the gate that eats the bright stars**, with counts.

---

## Step 5 — F46: triage the breach on its merits, if H1 and H2 are both refuted

Follow design §1.6's four conditions. In particular:

1. Re-derive `recallHighMin` for the affected cells **at the intended factor**, stating both directions — most of
   the seven tighten (`D09` 0.989, `D14` 0.992, `D17` 1.000), two loosen.
2. Fix the granularity problem: `L47` holds datasets at two detection factors, so either the band becomes
   per-factor or `D12`/`D15` carry a per-dataset override with its own reason.
3. Add `recallAllMin` for the affected cells so the file records what was **bought** as well as what was sold.
4. **Separate commit** from the adoption.

---

## Step 6 — F39(b) adoption (gated on step 3 or step 5 completing)

1. **Code.** Flip `optimize --apply-run-detection-binning` to default-on with an explicit opt-out; give
   `golden eval` the same default and opt-out. Keep the flag applying **only** the binning (F42). Tests: 2
   discriminating (the default now applies the derived factor; the opt-out is bit-identical to the old default),
   plus the existing wave-7 precedence/fallback tests unchanged.
2. **Arms**, script `D:\hf_w8\adopt\adopt_arms.sh`, one binary, `--settings` pinned:
   - **Arm A — status-quo, FIRST**: `optimize --per-run` + opt-out, **all 20** datasets, `--max-evals 120`.
   - **Arm B — adopted, LAST**: same, at the new default, all 20.
   F15 note in the results doc: the run folders end holding **arm B**'s landing, deliberately.
3. **Controls, checked before reading any result:**
   - the **13** binning-1 datasets are **bit-identical** between A and B (fault, not result, if not);
   - `BaselineJ` identical across arms on every run (F41's tell);
   - the seven's arm-B σ_focus / `J` agree with wave 7's arm-G2 table.
4. **Score against the checked-in bands**, not only against arm A.

---

## Step 7 — F19 arm X: what the block would say (no re-render)

1. **Instrument.** The harness records `ExposureRecommendation` only when `SensitivityIsAtFloor`; make it compute
   and record unconditionally. Gate the *display*, never the *measurement*. 1 discriminating test (a healthy-gate
   run now carries a recommendation object).
2. **Run** on the rungs already on disk — `D:\hf_w7\armE\t{0.5,1,2,4,8}\{D02_rich_135mm,D16_esprit550_ha3}` —
   `optimize --per-run --max-evals 120 --settings "$S"`, output to `D:\hf_w8\armX\`. No `synth-bank`, no render.
3. Read `RawSeconds`, `RecommendedSeconds`, `WasCapped`, `ExposureIsNotTheLimit`, `StarCountIsTheLimit`,
   `StarFieldIsExhausted`, `ShortFrameCount` at each rung.
4. **Apply R5 verbatim**, including `D16`'s control condition. Record which branch fired **before** writing any
   code for part (a).

---

## Step 8 — F19 part (b): saturation is reported, not presented as an answer

1. **Product.** `StarSignalCopy.DescribeExposureDerivation` states the raw arithmetic and which bound trimmed it,
   for the lower branch (`RawSeconds ≤ current`) as well as the two upper caps it already covers. Wording states
   what happened, not a verdict.
2. **Harness.** `synth-bank --dry-run` reports which datasets' derived exposure **saturated at a clamp**, with the
   raw pre-clamp value. Reporting only — no new field in a rendered dataset's `synthetic_meta.json`, so nothing
   re-renders. That list is wave 9's F19(c) work list.
3. Tests: 2 discriminating on the product copy (floored vs genuinely-above-target produce different text), 1 on the
   dry-run report.

---

## Step 9 — F19 part (a): the trigger, in the shape R5 licensed

- **R5 branch 2 (statistic was right, trigger hid it):** surface the exposure block on a **material ask** (≥ one
  `RoundExposureSeconds` ladder step above current), independent of the Sensitivity gate. `LowSignalChartNote`
  stays gated on the Sensitivity floor — its doc comment explains at length why it must not be re-pointed.
  Tests: 3 discriminating + 1 guard (a run with no material ask still shows nothing).
- **R5 branch 1 (statistic is wrong for this population):** do **not** widen the trigger. Ship the instrument and
  part (b), and file a new entry for σ_focus-driven exposure advice. Say plainly in the results doc that the wave
  declined to publish a wrong recommendation more widely.
- **In between:** widen the trigger, file the shortfall.

---

## Step 10 — F48: re-score arm S over the cells that can move (no runs, no plugin code)

Script `D:\hf_w8\f48_rescore.py`, reading `D:\hf_w7\f18arms\{C,D,S}_S*\synth_validate_report.json`.

1. Classify every (dataset, scenario) cell by `len(rounds)`. Cells with `len(rounds) == 1` are **structural ties** —
   the arms cannot differ.
2. Over the movable cells only: n, median σ_focus ratio S/C and D/C, counts >20 % better / worse, and the
   per-scenario breakdown (the wins are expected to concentrate in `S1`, the losses in `S6`).
3. **Report the excluded count and why, in the same table.** A denominator that quietly drops 18 of 26 cells is the
   same failure in the other direction.
4. State explicitly that wave 7's verdict **stands** and that this is a description, not a re-adjudication.

---

## Step 11 — Verify, write up, PR

1. **Full suite**: `dotnet.exe test ... -c Debug --nologo`. Expect **3661 + N**. Never pipe to `tail` (it masks the
   exit code); `SendAsync_WritesOnABackgroundThread` is a known flake and is not chased on full-suite runs.
2. **Discriminating-count audit**: for each code change, neutralize it and re-run the named tests; record how many
   fail. A test that passes either way is a **guard** and is labelled one.
3. **Re-run the step-0.3 dry-run diff** as the closing inertness control: derived parameters identical to `develop`
   on all 20 datasets, i.e. this wave moved no frame.
4. **Write `docs/synthetic-af-bank-followups-wave8-results.md`.** Report every arm as it landed, including any rule
   that fired against this wave's own position. State which of R1–R5 fired and which did not.
5. **Update `docs/followups.md`**: F46, F39, F19, F48 entries; new entries for anything found and not fixed.
6. **PR** to `develop` — never push `develop` directly. Commit with the privacy email
   (`322725+ghilios@users.noreply.github.com`, author **and** committer).
7. **On a red CI check**: verify the test COUNT first (F37's native host crash reports 0 failures), then check
   githubstatus.com before reading it as a regression.

---

## Falsification rules, collected (all fixed before any arm runs)

| rule | subject | fires when |
|---|---|---|
| **R1** | F46 / H1 — a pixel-unit knob | a swept knob brings recall@high ≥ 0.90 on **both** `D12` and `D15` at precision ≥ 0.98 without reducing recall@all below its bin-2 value |
| **R2** | F46 / H2 — blending | ≥ 50 % of the lost-bright set has a golden neighbour within 2× in-focus HFR **and** a bin-2 detection within one match radius, **and** the kept-bright comparison group does not |
| **R3** | instrument | the shipped-default sweep value fails to reproduce wave 7's 0.813 / 0.874 ⇒ **stop**, nothing in §1 is readable |
| **R4** | too-good-to-be-true | a knob raises recall@high **and** recall@all **and** precision on both ⇒ instrument fault until explained |
| **R5** | F19 / the trigger | at `D02`'s 0.5 s rung: `ExposureIsNotTheLimit` or < 2× ⇒ the statistic is wrong, (a) does **not** ship as a trigger widening; ≥ 4× ⇒ (a) is the whole fix. `D16` must NOT ask for materially more than its derived 2 s, or the instrument is wrong |
| **R6** | F46 / the scaling rule | **Added during execution**, fixed in `D:\hf_w8\p2\seven_sweep.sh`'s header **before that sweep ran** — R1–R5 were written against two datasets, and finding a mechanism on two cells is not grounds for a product rule. `StructureLayers += round(log2(factor))` is adopted only if, across **all seven** at factor 2, `layers+1` improves `recall@high` on a **majority** and regresses **none** by more than 0.01, at precision ≥ 0.98. If `layers+2` is needed to clear that bar, the dyadic derivation is wrong and the rule must be stated as an empirical fit, saying so plainly rather than rounding the measurement to the theory |

## Out of scope, deliberately

| item | why, and when it comes back |
|---|---|
| **F32's confirmation arm** | deferred with the cost stated (design §0.1): the φ = 0.50 floor stays default OFF one more wave, and **no comparability is lost** — `D18`/`D19`/`D20` are binning 1 and untouched by anything here or in wave 9. Trigger: before any wave that re-renders those three or runs `optimize --per-run` over the full synthetic bank for another purpose; otherwise **wave 9 item 1** |
| **F19(c)** — the 0.5 s exposure floor | bank-side; re-renders 8 datasets including `D14`, which F39(b)'s adoption is measuring, and 7 of the 13 control datasets. **Wave 9**, with the re-render |
| **F47** — clamping recovery-step placement | engine-side, changes live AF behaviour, wants its own before/after (F18's decision (a) said so) |
| **F21 / F25 / F26** | wave 7 covered magnitude only; the instrumentation each asks for is still owed |
