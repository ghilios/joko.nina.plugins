# Synthetic AF bank — followups wave 20 (implementation plan)

Pre-registration: [`docs/synthetic-af-bank-followups-wave20-design.md`](../docs/synthetic-af-bank-followups-wave20-design.md).
Charter: [`docs/waves17-21-handoff-prompt.md`](../docs/waves17-21-handoff-prompt.md).

**Read the design first.** This file is the execution order and nothing else. It re-decides no rule.

**Hard clock:** the controller starts no arm after **12:00Z**. Step times below assume a 07:15Z start; the
**drop-order triggers in design §13 are wall-clock, not step-relative** — check the clock at each trigger.

---

## Step 0 — commit the pre-registration (2 m)

```bash
cd /home/ghilios/src/hocus-focus
git status --short          # MUST be empty. If it is not, STOP: the brief's stale status has become real.
git rev-parse --short HEAD  # expect 95753a9
git add docs/synthetic-af-bank-followups-wave20-design.md plans/synthetic-af-bank-followups-wave20-plan.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(W20): pre-register the wave -- the gate finally gets a clause that can only pass if the code shipped"
```

**Nothing may be measured before this commit lands.**

---

## Step 1 — **D3, the manual line. ON ITS OWN COMMIT, BEFORE THE BUILD.** (2 m)

This is design §13's **floor**. It is scheduled here so that a cutoff at any later point cannot lose it.

`documentation/docs/settings/preprocessing.md`, **both** places — wave 18's `part2_w18.patch` touches only the
first and **is not a template**:

| line | now | becomes |
|---|---|---|
| **23** (settings-at-a-glance table) | `| Noise Reduction Radius (\`NoiseReductionRadius\`) | 3 | ...` | `... | 4 | ...` |
| **41** (prose, in the section a reader actually reads) | ``**Default:** `3` &nbsp;•&nbsp; **Range:** ...`` | ``**Default:** `4` &nbsp;•&nbsp; **Range:** ...`` |

At `:41`, add one sentence of derivation immediately after the Range clause (house voice, no confessional
heading, no "Honest limit"):

> In Simple mode the value is derived: the **Typical** noise preset sets 3, and enabling hot-pixel thresholding
> alongside hot-pixel filtering — both on by default — adds one to compensate for the blur, giving **4**.

**Do not** touch `:5` (a prose mention of the field name, no value) or `:22`
(`StarMeasurementNoiseReductionEnabled`, which is correct and corroborates the fix).

```bash
grep -n 'NoiseReductionRadius' documentation/docs/settings/preprocessing.md   # :5 and :23
sed -n '41p' documentation/docs/settings/preprocessing.md
# after editing:
grep -n '| 4 |' documentation/docs/settings/preprocessing.md | head
sed -n '41p' documentation/docs/settings/preprocessing.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(W20): NoiseReductionRadius's documented default is 4, in BOTH places (F70, two waves overdue)"
```

**No test, no gate, no binary.** Design §4.3 records that the gate does not reach this and names `D20-M` — the
textual check — as what does.

---

## Step 2 — **D1 and D2** (35 m)

### D1 — `optimize/detected`

**2.1** `Joko.NINA.Plugins/TestApp/ParamsDump.cs` — a fourth constant beside the three, with the naming hazard in
its own docstring:

```csharp
/// <summary>
/// <c>optimize</c>'s bundle AS DETECTED -- the baseline after
/// <c>ApplyRunDetectionBinningIfRequested</c> and after the per-run <c>PixelScale</c> assignment, so it is the
/// object the detector actually received (F69(b)).
///
/// <para><b>The tag is chosen, not incidental.</b> It contains NO existing tag as a substring
/// (<c>optimize/baseline</c>, <c>optimize/seed</c>, <c>af-fit/detector</c>) and is a substring of none of them,
/// so a prior wave's saved <c>grep -l "PARAMS-DUMP optimize/baseline"</c> cannot double-count it. A name like
/// <c>optimize/baseline-resolved</c> would break wave 17's and wave 18's saved drivers. Note also that
/// <c>detector</c> is NOT a substring of <c>detected</c>.</para>
/// </summary>
internal const string OptimizeDetected = "optimize/detected";
```

**2.2** `Joko.NINA.Plugins/TestApp/OptimizationDiagnosticRunner.cs`, in `RunPerRun`, **immediately after** the
existing `ApplyRunDetectionBinningIfRequested(ctx, runFolder, resolvedForRun);` (currently `:660`) and **before**
`Directory.CreateDirectory(subDir);`:

```csharp
// F69(b) -- the bundle AS DETECTED. The two dumps at :420 are taken where the bundles are CONSTRUCTED, before
// the per-run PixelScale assignment above and before ApplyRunDetectionBinningIfRequested mutates
// DetectionBinning + PixelScale. Wave 16's RULE P16 compared optimize's params as CONSTRUCTED against af-fit's
// as DETECTED and reached a wrong verdict on exactly that gap. This is the post-mutation bundle, so the printed
// object is the one the detector received.
ParamsDump.Write(Console.WriteLine, ParamsDump.OptimizeDetected, ctx.Baseline);
```

**Baseline and not seed, deliberately**: `optimize/baseline` is *"the apples-to-apples side, and the only one
`score_params_w16.py` reads"* (`ParamsDump.cs` class remarks). One block, one name, field-for-field comparable
with the block it is meant to be diffed against. `ApplyFactor` mutates **both** bundles, so nothing is lost that a
later wave could not add.

### D2 — the F69(c) notice

Same file, in the block that already prints the F39(b) status line (currently `:494-496`), add:

```csharp
if (DiagnosticUtil.HasFlag(args, "--apply-run-detection-binning")) {
    Console.WriteLine("--apply-run-detection-binning: ACCEPTED NO-OP. F39(b) was adopted as the DEFAULT in "
        + "wave 8; the opt-OUT is --no-run-detection-binning. This flag is retained so wave 7's scripts keep "
        + "running and is not read (F69(c)).");
}
```

**ASCII only** — `ParamsDump`'s class remarks record that a Unicode arrow in a redirected log arrives as the
single byte `0x1A` and made a parser miss 40 perfectly good logs. The existing line at `:496` already contains a
Unicode em dash; **do not add another**, and do not "fix" that one in this wave (it is outside scope and it is in
the `--no-run-detection-binning` branch, which no wave-20 clause reads).

### D1/D2 tests — and **each must be shown to fail against the pre-change source or a named mutant**

`Joko.NINA.Plugins.HocusFocus.Tests/Harness/ParamsDumpTests.cs`:

1. **Update** `TheSourceTags_AreTheThreePreRegisteredStrings` -> `...TheFourPreRegisteredStrings`, asserting all
   four constants. *This test fails to compile against pre-change source, which is the "new API" case: demonstrate
   it against mutant* **M-T1** — *change the constant's value to `"optimize/baseline-resolved"*.
2. **New:** `TheDetectedTag_CollidesWithNoOtherTag` — for each ordered pair of the four tags, assert neither
   contains the other as a substring. Mutant **M-T2**: set the constant to `"optimize/seed-detected"`; the test
   must go red.
3. **New:** `TheDetectedBlock_HasTheSameFieldSurfaceAsTheOthers` — `Lines(OptimizeDetected, p)` and
   `Lines(OptimizeBaseline, p)` over the same `StarDetectorParams` produce identical field lines and differ only
   in the two marker lines. Mutant **M-T3**: filter one property out of `Lines`; red.
4. **New:** `ADetectedBlock_ReflectsAPostApplyFactorBundle` — build a `StarDetectorParams`, dump it, call
   `DetectionBinningResolver.ApplyFactor(p, 2)`, dump again, and assert the two blocks' field sets differ on
   **exactly** `{DetectionBinning, PixelScale}`. **This is `D20-D`'s bar asserted in a unit test**, so the scorer
   and the product agree about what `ApplyFactor` touches. Mutant **M-T4**: have `ApplyFactor` skip `PixelScale`;
   red.

> Run each mutant, record red, revert. **Do not report a test as demonstrated without having seen it fail.**
> Wave 18's idempotence test was a dud until a mutant said so.

---

## Step 3 — the suite, verified by COUNT (4 m)

```bash
dotnet.exe test "$(wslpath -w /home/ghilios/src/hocus-focus/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```

Baseline **3831** (wave 18, verified). Expect **3831 + 3 = 3834** (three new tests; #1 is an edit, not an
addition). **Name every added test in the results.** The charter's "3781" is stale — design §11.5.

If `SendAsync_WritesOnABackgroundThread` fails, it is the known flake and is unrelated; re-run that fixture alone
rather than chasing it.

---

## Step 4 — build, ONE binary, never rebuilt (1 m)

```bash
dotnet.exe build "$(wslpath -w /home/ghilios/src/hocus-focus/Joko.NINA.Plugins/TestApp/TestApp.csproj)" \
  -c Release -o 'D:\hf_w20\exe'
sha256sum /mnt/d/hf_w20/exe/TestApp.dll /mnt/d/hf_w20/exe/NINA.Joko.Plugins.HocusFocus.dll
```

**Hash the dlls, not `TestApp.exe`** — the apphost is byte-identical across genuinely different binaries (wave 18).
`BuildId` is read from the **first landing**, as a field, after the gate's first run. Record both, plus the tree
diff line wave 19 learned to need:

```bash
git diff --name-only 95753a9 HEAD -- '*.cs' '*.csproj' '*.props' '*.targets'   # MUST be NON-empty this wave
```

---

## Step 5 — the four fingerprint classes, `--write` (2 m)

```bash
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write /mnt/d/hf_w20/bank_landing_fingerprint_BEFORE.json  # 42
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --write /mnt/d/hf_w20/bank_aux_fingerprint_BEFORE.json      # 59
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --write /mnt/d/hf_w20/w18_arm_fingerprint_BEFORE.json       # 48
python3 /mnt/d/hf_w20/log_fingerprint_w20.py  --self-test
python3 /mnt/d/hf_w20/log_fingerprint_w20.py  --write /mnt/d/hf_w20/w19_log_fingerprint_BEFORE.json       # 28
```

**Assert every population size.** `gate_w20.sh` aborts unless all four files exist and prints these five commands.

---

## Step 6 — **RULE G20, the gate** (42 m). A partial reproduction stops the wave.

```bash
bash /mnt/d/hf_w20/gate_w20.sh 2>&1 | tee /mnt/d/hf_w20/gate_w20.log
```

Run it **in the background with an `until`-loop wait** — a subagent's background processes die with its session,
so the controller runs every arm. One `TestApp.exe`. No NINA (the driver aborts if one is running).

The driver asserts, before any scorer: `aggregate_summary.json produced: 8`, `optimize/seed` `NoiseReductionRadius=3`
on 8 of 8 (**G20-P1** — if this reads 4, `part2_w18.patch` is in the binary and the wave stops), and the five
**G20-P2** counts.

---

## Step 7 — score the gate (8 m). **Self-tests before any number is quoted.**

```bash
python3 /mnt/d/hf_w20/prov_w20.py --self-test /mnt/d/hf_w20/gate    # BOTH directions, mutation asserted
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w20/gate --rule G20
python3 /mnt/d/hf_w20/score_d20_w20.py --self-test                  # 15 branches, design §7
python3 /mnt/d/hf_w20/score_d20_w20.py --gate /mnt/d/hf_w20/gate --out /mnt/d/hf_w20/g20_p2.txt
```

**WSL paths.** A Windows path yields UNEVALUATED everywhere and looks exactly like a failed arm; the scorers abort
on a backslash. `G20_PASSED` is written only when **all** of the above pass, via
`score_d20_w20.py --arm-g20-passed /mnt/d/hf_w20/gate`, which **refuses** unless G20-P2 passed on all five
sub-clauses.

> **Expected, not a fault:** `prov_w20.py --self-test` against a **prior** wave's arm (e.g. `/mnt/d/hf_w19/gate`)
> returns **SELF-TEST FAIL** on *"BuildId matches a PRIOR wave -- NO BUILD HAPPENED"*. That is the novelty clause
> firing correctly; it was verified at pre-registration time. Against **wave 20's own** gate, on a novel
> `BuildId`, direction 1 passes.

### Pre-registration-time validation already performed (do not redo, but do not assume either)

| instrument | state at pre-registration |
|---|---|
| `gate_w20.sh --self-test` | **PASS** |
| `detected_probe_w20.sh --self-test` | **PASS** |
| `f21_probe_w20.sh --self-test` | **PASS** |
| `log_fingerprint_w20.py --self-test` | **7 of 7**, and the collector verified **28 of 28** on the real files |
| `score_d20_w20.py --self-test` | **15 of 15** |
| `score_d20_w20.py --gate /mnt/d/hf_w19/gate` | reproduces the pre-registered **0-end**: `G20-P2a` **0 of 8**, `G20-P2e` **8 of 8** — the parser reads real logs, and F69(b) is provably absent from wave 19's binary |
| all six drivers | **ASCII-only, LF-only, `bash -n` / `ast.parse` clean** |

---

## Step 8 — the detected probe (3 m). Refuses to run without `G20_PASSED`.

```bash
bash /mnt/d/hf_w20/detected_probe_w20.sh 2>&1 | tee /mnt/d/hf_w20/dprobe_w20.log
```

One dataset, `D12_c14_585_afbin2`, `--per-run --max-evals 250 --apply-run-detection-binning`, both pins, into
`D:\hf_w20\dprobe`. The driver asserts `D20-V1` (`detection binning (F39b): 2 from `) on its own log and writes
`D20_PROBE_READY`.

---

## Step 9 — score RULE D20 (10 m)

```bash
python3 /mnt/d/hf_w20/score_d20_w20.py \
        --gate /mnt/d/hf_w20/gate --probe /mnt/d/hf_w20/dprobe \
        --out /mnt/d/hf_w20/d20_score.txt
```

**Apply the branch table as written (design §5.2). Do not re-decide a clause.** An unsatisfiable clause is a
finding. If the verdict is `D-PRESENT-ONLY`, name every failing clause and **do not** repair the scorer to obtain
`D-DEMONSTRATED` — that is the harvest RULE F14 was closed to prevent.

---

## Step 10 — item P, the F21 price probe (<= 20 m, **hard timebox**). **Drop trigger D1 at 09:30Z.**

```bash
bash /mnt/d/hf_w20/f21_probe_w20.sh --self-test
bash /mnt/d/hf_w20/f21_probe_w20.sh 2>&1 | tee /mnt/d/hf_w20/f21_w20.log
```

`timeout 1200`, one `synth-validate --scenarios S0 --datasets D17_cdk14_oiii5 --max-rounds 2`. **No rule is
attached and none may be written over the result.** Deliverables, and nothing else:

- wall time (or `"> 20 m, UNMEASURED"` if the timebox fires — **which is a useful answer, not a failure**),
- the round count and **whether a second round occurred at all**,
- `HalfWidth` and the recommended step per round.

**Do not price it from the `optimize` or `af-fit` rates.** `synth-validate` has never been run as an arm in this
series; that is the whole point.

---

## Step 11 — re-check all four fingerprint classes (3 m)

Same five commands as step 5 with `--check`. Expect **42 / 59 / 48 / 28**, 0 changed, 0 added, 0 removed,
0 could-not-look. Re-check again at write-up time.

---

## Step 12 — analysis and the results document (60 m). **Drop trigger D3 at 11:00Z.**

`docs/synthetic-af-bank-followups-wave20-results.md`. Structure, in order:

1. **PROVENANCE** — including `git diff --name-only <old> <new> -- '*.cs'` (**non-empty this wave**), the dll
   sha256 pair, the `BuildId`, the four fingerprint classes, and the suite count.
2. **Status table** — one row per item, its rule, its verdict, what it establishes.
3. **RULE G20** — every clause's pre-registered threshold **beside** its measured value, with the five-part
   statement. **Including §4.3's three-row table: what a PASS proves, and the two rows that say "reached: NO".**
4. **RULE D20** — the same, plus the branch table applied as written.
5. **The §0.1 correction** — `C19-D2` ran; wave 19's results are wrong; the lesson is *measure before you write*.
6. **Register corrections** (design §10, all seven).
7. **Budget: estimated against actual**, every row.
8. **What was NOT run and what it costs** (design §14), cheapest first.
9. **Lessons.**

> **The write-up's FIRST action is `ls -la --time-style=full-iso /mnt/d/hf_w20/`**, compared against the commit
> time of the results commit. Wave 19's write-up raced its own wave by 33 seconds and its re-derivation claim was
> false. **Re-derive every number from the artifacts on disk. Run no `TestApp.exe` at write-up. Write to no arm
> directory.**

---

## Step 13 — commit, push, PR, CI (45 m)

```bash
dotnet.exe test "$(wslpath -w .../Joko.NINA.Plugins.sln)" -c Debug --nologo     # final, by COUNT
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Wave 20: ..."
git push origin ghilios/synthetic-af-bank-followups-wave13
```

Append **one section** to PR #191, findings first; retitle if the scope grew. **Verify CI by the COUNT read out of
the log**, not the tick ([F37](../docs/followups.md)). Never push `develop`; open no new PR.

---

## Standing traps — check each before every step that touches it

- **WSL paths** (`/mnt/d/...`) to every scorer. A Windows path reads as a failed arm.
- **`< /dev/null` in every `while read` loop** — one bank path contains a SPACE and `TestApp.exe` eats stdin.
  **Assert the population size** every time.
- **"Could not look" is its own state and the guard comes BEFORE any field read.**
- **`DetectorVersion` as the FIELD.** `strings`/`strings -el` appears nowhere as a detector check.
- **A landing is not a settings file**; a converted settings file has **two copies** of every knob
  ([F71](../docs/followups.md)) and the live one is the **snapshot**. No clause in this wave converts a landing.
- **`UseAdvanced=False`** makes Simple-mode presets overwrite advanced knobs on load.
- **ASCII-only** for anything a scorer parses.
- **One `TestApp.exe` at a time. No NINA during a pinned arm. Never rebuild an arm's directory mid-wave.**
- **`"NaN"` is a string** — and in this wave it is a *token compared as a token* (`G20-P2c`, `D20-B`).
- **`Panos` is UNEVALUATED by name**; `D17_cdk14_oiii5` finds zero stars at short exposures; `lumos` exits rc=3;
  the `astrodet` **dataset** is frameless (`astrodet` the **profile** is what every arm pins).
- **Three copies of every field name now exist in every log.** Every read is a BEGIN..END range keyed on the
  exact tag. This is the wave that creates the hazard, so this is the wave that must not fall into it.
