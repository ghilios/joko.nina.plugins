# Synthetic AF bank — followups wave 18 (implementation plan)

Design / pre-registration:
[`docs/synthetic-af-bank-followups-wave18-design.md`](../docs/synthetic-af-bank-followups-wave18-design.md).
**Read it first. Every threshold, branch and denominator lives there and none of them is re-decided here.**
Charter: [`docs/waves17-21-handoff-prompt.md`](../docs/waves17-21-handoff-prompt.md).

> **The one thing to keep in your head while executing this plan.** Wave 18 ships a change that the gate can
> see. So it builds **two** binaries — **B1 = HEAD + Part 1** (gate-safe, and the gate is what proves it) and
> **B2 = B1 + Part 2** (the seed literal). **The gate runs on B1.** If you find yourself running the gate on
> B2, or running an arm on the wrong binary, stop: `gate_w18.sh` and `seed_w18.sh` both assert the seed value
> out of the `PARAMS-DUMP optimize/seed` block and will refuse, but the refusal only helps if you read it.
> **`exit=0` means the wrapper exited, not that the arm ran** (wave 17 §7.1) — open the log.

---

## 0. Artifacts already written

| path | what |
|---|---|
| `/mnt/d/hf_w18/prov_w18.py` | RULE G18's free controls as FIELDS, read across the arm. Wave 17's, carried forward; only the `PRIOR_BUILD_IDS` / `KNOWN_DLL_SHA256` tables changed |
| `/mnt/d/hf_w18/gate_w18.sh` | RULE G18 on **B1**, with the population assertion, both fingerprint preconditions, and the `seed == 3` assertion that catches a mis-built B1 |
| `/mnt/d/hf_w18/seed_w18.sh` | arms **A0** (B1) and **A1** (B2), 20 synthetic datasets each, with the `G18_PASSED` interlock and the per-arm N18-V2 precondition |
| `/mnt/d/hf_w18/affit_w18.sh` | the conversion probe and the two out-of-sample passes, with the `PROBE_PASSED` interlock |
| `/mnt/d/hf_w18/score_n18_w18.py` | RULE N18, `--self-test` (15 demonstrations, all passing), `--reach`, `--arm-g18-passed`, `--probe` |

Reused unchanged: `/mnt/d/hf_w12/score_w12.py`, `/mnt/d/hf_w15/bank_fingerprint_w15.py`,
`/mnt/d/hf_w15/convert_landing_w15.py`, `/mnt/d/hf_w17/aux_fingerprint_w17.py`.

All five new files are **LF, ASCII-only** (verified). All scorers take **WSL** paths.

---

## 1. Task list, in the fixed order of the design's §8

### T0 — commit the pre-registration
`docs/synthetic-af-bank-followups-wave18-design.md` + `plans/synthetic-af-bank-followups-wave18-plan.md`,
with the privacy email. **Before any build.** The drivers under `D:\hf_w18\` are not committed (no wave has
committed them); they are reproduced in the results doc's §Reproduce.

### T1 — item A, **Part 1** (code)
Spawn a code agent with the design's §2.5 as its brief. It must not touch
`HocusFocusStarDetection.cs:425`.

1. **`StarDetectionOptions.ResetDefaultsImpl`** — satisfy the contract: *after `ResetDefaults()`, the
   property state equals that of a freshly constructed `StarDetectionOptions` over a blank accessor, for
   every property, from every entry state.* The natural implementation is an unconditional
   `ConfigureSimpleSettings()` as the final statement, after `optimizedSettings = null` and
   `UseOptimizedSettings = false`. Whatever is chosen must satisfy the contract generally, **not just for
   `NoiseReductionRadius`** — the point is to retire the transcription-drift class, and 19 of the 20 shared
   fields agree only by luck of hand-copying.
2. **The drift guard**, replacing `BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild`:
   - **T1** `BuildDefaultStarDetectorParams()` vs `BuildStarDetectorParams(freshly constructed options)`,
     every option-derived field. Under Part 1 alone this must carry `NoiseReductionRadius` as **one named
     exception asserted in both directions** (3 on the seed bundle, 4 on the constructed one) with a comment
     naming F70; Part 2, if it ships, deletes the exception.
   - **T2** `ResetDefaults()` from four named entry states — virgin, Advanced with a non-default radius,
     `UseOptimizedSettings = true` with a snapshot applied, non-default `Simple_*` — all equal to a fresh
     construction.
   - **T3** the accessor-fallback / literal lockstep guard, modelled on `AutoFocusOptionsTests.cs:103-115`.
3. **F69(a)** — rewrite the four-line comment at `OptimizationDiagnosticRunner.cs:405-409`. Every clause of
   it is backwards: `ApplyRunDetectionBinningIfRequested` runs **unless** `--no-run-detection-binning` is
   passed, the gate and the P16 probe both ran it, and it mutates `DetectionBinning` **and** `PixelScale`
   after the dump above it has printed them.
4. **Demonstrate the new tests fail against the pre-change source.** `T2`'s `UseOptimizedSettings` case is
   the one that must: on the pre-change source `ResetDefaults()` returns 4 from that entry state and 3 from
   a virgin one. **The controller verifies this claim itself** (`git stash` the production change, run the
   named test, see it red, restore) — the code agent asserting it is not evidence.
5. Run the suite. Record the count and name every added test and every changed assertion.

**Expected changed assertions under Part 1 alone:** none, if T1 keeps the exception. If the code agent
instead makes `ResetDefaults()` derive **and** leaves `StarDetectionOptionsTests.cs:513` /
`StarDetectionOptionsBufferedModeTests.cs:115` at `Is.EqualTo(3)`, those two go red — that is expected and
they move to 4 as part of Part 1, because Part 1 is what changes `ResetDefaults()`. Say which happened.

### T2 — build **B1**
```
dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w18\exe_b1'
sha256sum /mnt/d/hf_w18/exe_b1/TestApp.dll /mnt/d/hf_w18/exe_b1/NINA.Joko.Plugins.HocusFocus.dll
```
Record both hashes. The `BuildId` is read from the first landing, not guessed.

### T3 — the BEFORE fingerprints, **both classes**
```
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write /mnt/d/hf_w18/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --write /mnt/d/hf_w18/bank_aux_fingerprint_BEFORE.json
```
42 landings and 59 aux files. Every driver aborts without both.

### T4 — RULE G18 on B1  (~42 m)
```
bash /mnt/d/hf_w18/gate_w18.sh > /mnt/d/hf_w18/gate_w18.log 2>&1
```
Background, with an `until`-loop wait. **Then read the log**, not the exit code.
```
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w18/gate --rule G18
python3 /mnt/d/hf_w18/prov_w18.py  --self-test /mnt/d/hf_w18/gate
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w18/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w18/bank_aux_fingerprint_BEFORE.json
```
**A partial reproduction stops the wave, and Part 1 is the first suspect (design §1.2).** Only after all four
pass:
```
python3 /mnt/d/hf_w18/score_n18_w18.py --arm-g18-passed /mnt/d/hf_w18/gate
```
which re-reads the gate — population 8, and all 8 seed dumps at `NoiseReductionRadius=3` — and writes
`G18_PASSED`.

### T5 — the conversion probe  (~1 m)
```
bash /mnt/d/hf_w18/affit_w18.sh probe
python3 /mnt/d/hf_w18/score_n18_w18.py --probe /mnt/d/hf_w18/probe \
        --probe-result /mnt/d/hf_w18/gate/D18_m24_deep_shed/attempt01/optimize_result.csv
```
Must print **TOOK** for `good` and **DID-NOT-TAKE** for `mutant`, then write `PROBE_PASSED`.
`affit_w18.sh 0|1` refuses to run without it.

### T6 — arm A0 on B1  (~51 m)
```
bash /mnt/d/hf_w18/seed_w18.sh A0 > /mnt/d/hf_w18/seedA0.log 2>&1
```
The driver asserts 20 landings, that every `optimize/seed` block reads `3`, that every `optimize/baseline`
block reads `4`, and re-checks both fingerprints. **Read the log.**

### T7 — item A, **Part 2** (code) and build **B2**
Three edits, no more:
- `HocusFocusStarDetection.cs:425` `NoiseReductionRadius = 3` → `4`, with a comment naming the hotpixel
  compensation it now carries and pointing at `StarDetectionOptions.cs:221-224`.
- `documentation/docs/settings/preprocessing.md:23` default `3` → `4`.
- The drift guard's `NoiseReductionRadius` exception is deleted (T1 becomes a plain equality).
```
dotnet.exe build ... -o 'D:\hf_w18\exe_b2'
sha256sum /mnt/d/hf_w18/exe_b2/TestApp.dll /mnt/d/hf_w18/exe_b2/NINA.Joko.Plugins.HocusFocus.dll
```
Record both. **Do not rebuild B1. Do not touch `/mnt/d/hf_w18/gate` or `/mnt/d/hf_w18/seedA0` (F53(c)).**
Run the suite on this tree too — it must be green before A1 runs, or the arm is measuring a broken build.

### T8 — arm A1 on B2  (~51 m)
```
bash /mnt/d/hf_w18/seed_w18.sh A1 > /mnt/d/hf_w18/seedA1.log 2>&1
```
The driver asserts every `optimize/seed` block reads **4** and every `optimize/baseline` block still reads
**4**. A failure here is `N18-V2` and it means **the wrong binary ran** — it is never "the change did
nothing".

### T9 — the out-of-sample pass  (~8 m)
```
bash /mnt/d/hf_w18/affit_w18.sh 0 > /mnt/d/hf_w18/affitA0.log 2>&1
bash /mnt/d/hf_w18/affit_w18.sh 1 > /mnt/d/hf_w18/affitA1.log 2>&1
```
Both run on **B1** — one instrument, two inputs (design §2.7).

### T10 — score RULE N18
```
python3 /mnt/d/hf_w18/score_n18_w18.py --self-test
python3 /mnt/d/hf_w18/score_n18_w18.py --a0 /mnt/d/hf_w18/seedA0 --a1 /mnt/d/hf_w18/seedA1 \
        --affit0 /mnt/d/hf_w18/affitA0 --affit1 /mnt/d/hf_w18/affitA1 \
        --gate /mnt/d/hf_w18/gate --w15 /mnt/d/hf_w15 \
        --out /mnt/d/hf_w18/n18_score.txt
```
**Apply the branch table as written.** An unsatisfiable clause is a finding, not an invitation to restate it.

### T11 — the branch's consequence
- **`N-ALIGN`** and wall ≤ 3 h 30 m ⇒ run `G18'`: the same eight runs on B2, into `/mnt/d/hf_w18/gateB2`
  (copy `gate_w18.sh`, point `EXE` at `exe_b2`, `OUT` at `gateB2`, **and invert the `seed == 3` assertion to
  `seed == 4`** — an assertion carried over unchanged would abort the arm). Publish the new eight values
  **beside** the old, labelled a new coordinate system.
- **`N-ALIGN`** and wall > 3 h 30 m ⇒ do not run it; record that wave 19's first act is a fresh eight-value
  baseline, priced at 42 m.
- **`N-HOLD`** or **`N-UNEVALUATED`** ⇒ revert T7's three edits and restore the drift guard's one named
  exception. F70(a) becomes a costed recommendation carrying this wave's measured numbers.

### T12 — the suite, on the final tree
`dotnet.exe test ... -c Debug --nologo`, verified by **COUNT** (F37). Baseline 3824. Name every added test.
*Do not pipe to `tail` — it masks the exit code.* `SendAsync_WritesOnABackgroundThread` is a known flake and
is not chased on a full-suite run.

### T13 — commit, push, PR, CI
Append the wave's section to PR #191, findings first. Verify CI by **COUNT read out of the log**.

---

## 2. Progress counting — and it has a could-not-look state

Wave 17's controller reported two fully successful arms as failures because its improvised counter looked for
the wrong filename in the wrong directory and **could not say so**. The exact commands, with the state:

```bash
d=/mnt/d/hf_w18/seedA0                       # or seedA1
if [ ! -d "$d" ]; then echo "COULD-NOT-LOOK: $d does not exist"
else echo "landings: $(find "$d" -name optimized_settings.json | wc -l) of 20"; fi

d=/mnt/d/hf_w18/gate
if [ ! -d "$d" ]; then echo "COULD-NOT-LOOK: $d does not exist"
else echo "gate: $(find "$d" -mindepth 2 -name aggregate_summary.json | wc -l) of 8"; fi

d=/mnt/d/hf_w18/affitA0                      # or affitA1
if [ ! -d "$d" ]; then echo "COULD-NOT-LOOK: $d does not exist"
else echo "af-fit: $(find "$d" -name af_fit_summary.txt | wc -l) of 20"; fi
```

The filenames are `optimized_settings.json`, `aggregate_summary.json` and `af_fit_summary.txt`. They are
different for the three instruments; that is what caught wave 17.

---

## 3. Trap checklist, per step

| trap | where it bites here | the guard |
|---|---|---|
| Windows paths to a scorer | every scorer | all take WSL paths; `score_n18_w18.py` and `prov_w18.py` abort on a backslash |
| `TestApp` eats a `while read` loop's stdin | every loop in all three drivers | `< /dev/null` on every invocation |
| a short population reading as a null | all three drivers | population asserted **in the driver**, before any scorer |
| "could not look" with no state | scorers and the progress counter | three states everywhere; the guard precedes every field read; §2 above |
| `strings` is two instruments | the detector check | `DetectorVersion` is read as the **FIELD**; `strings -el` only as a flag-presence probe |
| a landing is not a settings file | the out-of-sample pass | `convert_landing_w15.py`, through the production Accept path, with a probe in both directions |
| `UseAdvanced=False` makes presets overwrite advanced knobs | the converted settings files | the converter sets `UseOptimizedSettings=True`; the mutant proves it is load-bearing |
| non-ASCII in a parsed file | `/mnt/d/hf_w18/*` | verified ASCII-only, LF |
| one `TestApp.exe`, no NINA | every arm | counted with the refuse-to-guess form in all three drivers |
| no fan-out | every arm | every arm is `--profile-id`-pinned and cannot fan out |
| rebuilding an arm's directory mid-wave | T7 in particular | every driver aborts on a populated output root |
| `"NaN"` is a string | `FinalJ` / `BaselineJ` reads | compared with `repr()` equality, so a `"NaN"` string never silently equals a float |
| `Panos` UNEVALUATED **by name** | not in this wave's population | the 20 synthetic datasets only; `Panos` is a real-bank run and appears nowhere |
| `D17_cdk14_oiii5` finds zero stars at short exposures | it is the **last** dataset in the order | if it degenerates it is COULD-NOT-LOOK by name and out of the denominator, which is re-printed |
| pricing one instrument from another | the budget | three rates, kept apart: `optimize` mixed 5.2 m/run, `optimize` all-synthetic 2.6 m/run, `af-fit` ~11 s/dataset |

---

## 4. What the results doc must contain

- The PROVENANCE block with **both** binaries' dll sha256 pairs and **both** `BuildId`s, explicitly noting
  which arm ran which — and that the gate ran on B1 **by design**, with §2.6's reason restated.
- RULE G18 clause by clause, with `G18-P1`'s measured value, and the statement that its PASS is also the
  measurement that Part 1 is inert.
- RULE N18 clause by clause with the P/S/A/E statement beside each, the branch table as applied, and the
  verdict — including, if the verdict is `N-ALIGN` on a null, the sentence the design requires.
- `N18-R`'s nine-profile table, and the `ResetDefaults()` non-determinism as its own finding.
- The corrections this wave owes the register: F70's mechanism line (`:535` vs `:556`), F70's
  "two shipped defaults" (it is three states, one of them state-dependent), and F69(a) as fixed.
- The budget table, estimate against actual, per instrument.
- §7's "what was not run" list, carried forward with actuals.
- If `N-ALIGN` shipped without `G18'`: **a loud, top-level note that the K8 table is invalid for wave 19.**
