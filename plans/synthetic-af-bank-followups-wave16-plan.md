# Synthetic AF bank — followups wave 16 (implementation plan)

Design: [`docs/synthetic-af-bank-followups-wave16-design.md`](../docs/synthetic-af-bank-followups-wave16-design.md).
Charter: [`docs/waves16-21-handoff-prompt.md`](../docs/waves16-21-handoff-prompt.md).

**Every rule, threshold and validity gate is in the design. This plan does not restate them and MUST NOT
re-decide one.** An unsatisfiable clause is a finding (wave 14 Lesson 3).

**THE ORDER IS THE PLAN.** Wave 16 ships code, so:

> **code → ONE build → gate → probe → rungs → analysis → suite → commit/push/CI**

No arm may run before the build; **no rung other than `N` may run before clause W1 passes** (the driver enforces
this with a marker file); nothing may be rebuilt mid-wave (F53(c)).

---

## Artifacts already in place at pre-registration

| path | state |
|---|---|
| `/mnt/d/hf_w16/gate_w16.sh` | written, `bash -n` clean |
| `/mnt/d/hf_w16/prov_w16.py` | written; carries wave 15's `BuildId`; `--self-test` runs both directions |
| `/mnt/d/hf_w16/probe_w16.sh` | written, `bash -n` clean |
| `/mnt/d/hf_w16/affit_w16.sh` | written, `bash -n` clean; refuses any rung until `W1_PASSED` exists |
| `/mnt/d/hf_w16/verify_affit_w16.sh` | written, `bash -n` clean |
| `/mnt/d/hf_w16/score_sem_w16.py` | written. **`--self-test` PASSES, 28 demonstrations.** **`--conform` PASSES: 585 / 0 / 2 / 195**, reproducing wave 15's published V4′ result |
| `/mnt/d/hf_w16/score_params_w16.py` | written. **`--self-test` PASSES, 13 demonstrations.** `--gc-rescore` already reproduces wave 15's **205 of 205 / 40 runs / 0 could-not-look** |
| `/mnt/d/hf_w16/affit_real_list_w16.tsv` | 19 rows, copied from wave 14 so the three waves score the same runs at the same steps |
| `/mnt/d/hf_w16/bank_landing_fingerprint_BEFORE.json` | 42 landings; **verified current at pre-registration: 42 of 42 byte-identical** |
| reused unchanged | `/mnt/d/hf_w12/score_w12.py`, `/mnt/d/hf_w15/bank_fingerprint_w15.py` |

All files are **LF**. Clause **W1 has already been demonstrated in both directions** against wave-14 data
(`A0.00` vs itself → PASS 39/39 byte-identical; `A0.25` vs `A0.00` → FAIL), and the demonstration cannot write
the unlock marker because the scorer only writes it for `/mnt/d/hf_w16`.

---

## Step 0 — commit the pre-registration (controller)

Commit `docs/synthetic-af-bank-followups-wave16-design.md` and this plan **before any code and before any arm**.
Drivers under `D:\hf_w16\` are not in the repo; their content is fixed by this commit's description of them.

```
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
```

Also run, and record the one line:

```
cmd.exe /c "query session"
```

At pre-registration it read **`Disc`** (seventh wave). If it reads `Conn`, item 2's nine UI changes become
attemptable and cost ~30 m by wave 13 §3's procedure.

---

## Step 1 — the code (CODE AGENT), all of it, before the build

**Constraint that governs every line below: the product must be BIT-IDENTICALLY unchanged at the default.**
Clause W1 is what proves it and W1 is a **byte** comparison, so nothing may be printed into
`af_fit_summary.txt` when the spec is `None`.

### 1a. `Joko.NINA.Plugins.HocusFocus/Utility/SemScaleSpec.cs` — new

A `readonly struct SemScaleSpec : IEquatable<SemScaleSpec>`, modelled **line for line** on
`Utility/MadFloorSpec.cs`, including its doc-comment discipline:

- private ctor; the only ways to build a non-empty spec are `Veto(double t)` and `Rank()`, so a spec carrying
  both families is **unrepresentable**;
- `None => default`; `Veto(0)` returns `None` (so the control rung is spelled the same way as "no flag");
- `Veto` rejects NaN / ∞ / negative with `ArgumentOutOfRangeException`;
- `IsNone`, `IsVeto`, `IsRank`, `VetoThreshold`;
- `Equals` / `GetHashCode` / `==` / `!=`;
- `ToString()` → `"none (shipped behaviour)"` / `"family V (SEM veto), t = <R>"` / `"family R (SEM rank)"`,
  **ASCII only**, `CultureInfo.InvariantCulture`.

### 1b. `Utility/MathUtility.cs` — a new overload, not new optional parameters

Keep the existing 4-arg and 6-arg overloads binding exactly as they do. Add:

```csharp
public static ScatterErrorPoint RejectionTest(
        List<ScatterErrorPoint> points, Func<double, double> fitting, double confidence,
        Func<double, double> weights, double scaleFloor,
        SemScaleSpec semScale, Func<double, double> starCounts,
        out double scaleUsed, out double semOfSelected)
```

and make the 6-arg overload delegate to it with `SemScaleSpec.None, starCounts: null, out _`.
**One implementation, never two that must be kept in step** — that identity is the whole basis of the control
rung, exactly as the existing comment says about the 4-arg overload.

Semantics, in this order:

1. `semOfSelected = double.NaN` and `scaleUsed = double.NaN` on entry and on **every** early return
   (`points.Count <= 3`, degenerate scale). *"No test was performed" must not be reported as a number.*
2. **`if (!semScale.IsNone && starCounts == null) throw new InvalidOperationException(...)`** — a SEM criterion
   with no star counts is a loud failure, never a silent no-op.
3. `errors[i]` exactly as today. Keep the **unscaled** values in a second array, `rawErrors`, so
   `semOfSelected` is comparable across all rungs.
4. **Family R only**: `errors[i] *= Math.Sqrt(Math.Max(starCounts(points[i].X), 1.0))` **before** `MedianMAD`.
   Guarded on `semScale.IsRank`, so the default path does not evaluate a `Math.Sqrt`.
5. MedianMAD, both degenerate-scale guards, then `scaleFloor` — **unchanged, in that order**.
6. Grubbs limit and `argmax` — unchanged.
7. `semOfSelected = |rawErrors[argmax]| * Math.Sqrt(Math.Max(starCounts(points[argmax].X), 1.0))` when
   `starCounts != null`, else `NaN`. **Computed for the selected point whether or not the rejection stands**, so
   a `KEPT` round prints a number too.
8. `if (maxError.z < grubbZLimit) return null;` — unchanged.
9. **Family V only**: `if (semScale.IsVeto && semOfSelected < semScale.VetoThreshold) return null;`
   — **strictly after** the Grubbs comparison, so a veto can only ever **suppress**.

### 1c. `StarDetection/AlglibHyperbolicFitting.cs` — thread it beside `madFloor`

Add `SemScaleSpec semScale = default, Func<double, double> starCounts = null` to
`SelectBestModel` (the 8-arg overload) and to `FitWithOutlierRejection`, passed verbatim to `RejectionTest`.
The backwards-compatible `SelectBestModel` overload passes `SemScaleSpec.None` **explicitly**, for the same
reason it passes `MadFloorSpec.None` explicitly today. **No production call site changes.**

**Do NOT** touch `WeightRegularization`, `ScatterErrorPoint` construction, or `AutoFocusEngine`. The count travels
as a `Func<double,double>` keyed by `p.X`, the shape `BuildResidualWeights` already uses — design §2.2 records
why the `ScatterErrorPoint.Tag` alternative was rejected.

### 1d. `TestApp/SemScaleArgs.cs` — new, modelled on `TestApp/MadFloorArgs.cs`

`--sem-veto <t>` and `--sem-rank`. **Exact, case-insensitive token match — never `StartsWith`/`Contains`**
(`--sem-veto-never` must not switch it on). A flag present with no value **throws**. Naming more than one rung is
an **error, not a precedence rule** — and that includes naming a SEM family **and** a MAD-floor family in the
same invocation: two mechanisms in one arm is an arm nobody pre-registered.

### 1e. `TestApp/AfFitDiagnosticRunner.cs`

- parse the spec **before anything expensive runs** (the existing `MadFloorArgs.Parse` pattern);
- after `rows` is built, `Func<double,double> starCounts = x => { … }` from `Position → Stars` (build a
  `Dictionary<double,int>` once; unknown X ⇒ **throw**, never a silent 1);
- pass `semScale` and `starCounts` to **both** `SelectBestModel` calls and to the per-round replication;
- `Console.WriteLine($"SEM scale: {SemScaleArgs.Describe(spec)}")` **always** (the log records the rung
  unconditionally), and into `af_fit_summary.txt` **only when the spec is not None**;
- in the per-round detail, **only when the spec is not None**, one ASCII line immediately after
  `effective scale (full precision)`:

```
  SEM detail   : spec=<tag>  N*(sel)=<int>  |r|(sel)=<G17>  s=<G17>  verdict=<KEPT|SUPPRESSED|N/A>
```

  `<tag>` has no spaces (`none`, `V1.00`, `R`). `verdict` is `SUPPRESSED` when the veto fired, `KEPT` when a
  rejection stood, `N/A` when the Grubbs test itself found nothing.
- **for family R only**, one further line:
  `  SEM rank     : sqrt(N*) span this round = <min>..<max>` — a span of exactly 1.000 makes family R a no-op,
  and the diagnostic that explains R's effect should not have to be reconstructed by a later wave.

### 1f. `TestApp` — the item-B printouts, through ONE shared formatter

Add a single internal helper (e.g. `TestApp/ParamsDump.cs`) that renders a `StarDetectorParams` as:

```
PARAMS-DUMP <source> BEGIN
  <FieldName>=<value>
PARAMS-DUMP <source> END
```

one public property per line, **sorted by name**, `CultureInfo.InvariantCulture`, **ASCII only**, enumerated by
reflection over the public instance properties so the dump cannot silently miss a field a future change adds.
Call it from:

- `AfFitDiagnosticRunner`, beside the existing `Region:` line, with `source = af-fit/detector`, on the params the
  detector is actually about to receive (i.e. **after** `ModelPSF = false`);
- `OptimizationDiagnosticRunner`, beside the existing `Current (baseline) params:` line, with
  `source = optimize/baseline` for `baseline` and `source = optimize/seed` for `seed`, **after** `ApplyAfContext`.

Both go to the **console**, not into `af_fit_summary.txt`, so clause W1's byte comparison is untouched.

### 1g. Tests — **every new test must be shown to FAIL against the pre-change source**

Mirror `MadFloorRejectionTests` / `MadFloorFitPlumbingTests` / `MadFloorArgsTests`. At minimum:

| test | what it pins |
|---|---|
| `SemScaleSpec` factories | `Veto(0)` is `None`; NaN/∞/negative throw; the two families are mutually exclusive by construction; `ToString` names the rung |
| `RejectionTest` at `SemScaleSpec.None` | returns **the same point** as the 4-arg overload on a battery of inputs — the inertness claim, as a unit test |
| the veto only suppresses | on a fixed point set, `Veto(t)` returns either the same point as `None` or `null`, **never a different point**, for a sweep of `t` |
| the veto fires exactly at the threshold | a constructed point set with a known `s`: kept at `t` just below, suppressed just above |
| `starCounts == null` with a live spec | **throws** |
| family R with a **constant** `N*` | returns **the identical point** as `None` — z is scale-invariant, so this is a theorem, and a test that would catch a rescale applied in the wrong place |
| family R with a varying `N*` | selects a **different** point on a constructed set — the mechanism actually re-ranks |
| `semOfSelected` | equals `\|r\|·√N*` for the selected point, at `None`, at `Veto`, and at `Rank` |
| plumbing | `SelectBestModel` / `FitWithOutlierRejection` at `default` are bit-identical to the existing overloads on a real point set |
| `SemScaleArgs` | exact-match only; missing value throws; two SEM families throw; a SEM family **and** a MAD family throw; `--sem-veto 0` is the control |
| `ParamsDump` | round-trips every public property; is ASCII-only; is stable under property reordering |

**Verification the controller performs itself** (step 7 of the charter): each test is run against the pre-change
source and shown to fail, or — for new API where a revert will not build — against a **named mutant**. Two
mutants to name explicitly, chosen because they are the failure modes that would otherwise be invisible:

- **M1**: move the veto check **above** the `maxError.z < grubbZLimit` comparison ⇒ the veto can create a
  "rejection" decision path; the "only suppresses" test must fail.
- **M2**: apply the `√N*` scaling **after** `MedianMAD` instead of before ⇒ family R becomes a uniform no-op;
  the "varying `N*` re-ranks" test must fail.

---

## Step 2 — build ONE binary (controller)

```
dotnet.exe build "$(wslpath -w $PWD/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w16\exe'
```

Record `sha256` of `TestApp.dll` and `NINA.Joko.Plugins.HocusFocus.dll`. **Never rebuild this directory.**
(The `BuildId` is only stamped into landings, so it is read after the gate, from the gate's own files.)

---

## Step 3 — the F15 baseline (controller)

```
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write /mnt/d/hf_w16/bank_landing_fingerprint_BEFORE.json
```

**Re-write it here even though a current copy exists** — a fingerprint of unknown age is not a BEFORE. Re-check
after the gate, after the probe, after every rung, and after scoring.

---

## Step 4 — RULE G16, the gate (controller). **A partial reproduction STOPS THE WAVE**

```
bash /mnt/d/hf_w16/gate_w16.sh                          # ~42 m, background, wait with an until-loop
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w16/gate --rule G16
python3 /mnt/d/hf_w16/prov_w16.py  --self-test /mnt/d/hf_w16/gate
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w16/bank_landing_fingerprint_BEFORE.json
```

Record the new `BuildId` in the results doc so wave 17 can carry it, and confirm it is novel against
`5cb7e474…`, `103d61c4…`, `62334f10…`, `084e3485…`, `df3a867d…`.
**`a2693968…` is a dll sha256, not a BuildId** — design §10.1.

If G16 fails: stop, write up the gate failure, and run nothing else.

---

## Step 5 — STEP P0, item B's probe (controller, ~1 m)

```
bash /mnt/d/hf_w16/probe_w16.sh
```

Two `optimize` runs (`D12`, `D17`) purely for the `PARAMS-DUMP optimize/baseline` block. Their `BestJ` is read by
nothing.

---

## Step 6 — item A, the ladder (controller, sequential, ~66 m)

**Rung `N` first, alone.**

```
bash /mnt/d/hf_w16/affit_w16.sh N
bash /mnt/d/hf_w16/verify_affit_w16.sh /mnt/d/hf_w16/affit_N/syn  20
bash /mnt/d/hf_w16/verify_affit_w16.sh /mnt/d/hf_w16/affit_N/real 19
python3 /mnt/d/hf_w16/score_sem_w16.py --w1 /mnt/d/hf_w16/affit_N --w14 /mnt/d/hf_w14/affit_A0.00
```

**A W1 miss ⇒ item A is UNEVALUATED and no further rung runs.** A PASS writes `/mnt/d/hf_w16/W1_PASSED`, which is
what unlocks the rest — the driver refuses without it.

Then, one at a time, `< /dev/null` throughout, one `TestApp.exe`, no NINA, verifying the population after each:

```
bash /mnt/d/hf_w16/affit_w16.sh V1.00      # the named criterion  -- never dropped
bash /mnt/d/hf_w16/affit_w16.sh R          # the only live arbiter -- never dropped
bash /mnt/d/hf_w16/affit_w16.sh V2.00
bash /mnt/d/hf_w16/affit_w16.sh V0.07      # the caboose selectivity probe, n = 1 by construction
bash /mnt/d/hf_w16/affit_w16.sh V4.00
```

That order is the **drop order reversed**, so time pressure removes the least valuable rung first
(design §7). Re-check the bank fingerprint after the last rung.

---

## Step 7 — scoring (controller / analysis agent)

```
python3 /mnt/d/hf_w16/score_sem_w16.py --self-test                       # quote nothing before this passes
python3 /mnt/d/hf_w16/score_sem_w16.py --conform --w14root /mnt/d/hf_w14 # V4'-CONFORM: 585 / 0 / 2 / 195
python3 /mnt/d/hf_w16/score_sem_w16.py --root /mnt/d/hf_w16 --w13 /mnt/d/hf_w13 \
        --out /mnt/d/hf_w16/s16_score.txt

python3 /mnt/d/hf_w16/score_params_w16.py --self-test
python3 /mnt/d/hf_w16/score_params_w16.py --affit /mnt/d/hf_w16/affit_N --gate /mnt/d/hf_w16/gate \
        --probe /mnt/d/hf_w16/probe --out /mnt/d/hf_w16/p16_score.txt
python3 /mnt/d/hf_w16/score_params_w16.py --gc-rescore /mnt/d/hf_w15 --out /mnt/d/hf_w16/p16b_score.txt
```

All **WSL** paths. The analysis agent **applies** the design's rules and **must not re-decide one**; an
unsatisfiable clause is a finding it reports, not a clause it repairs.

---

## Step 8 — the suite (controller)

```
dotnet.exe test "$(wslpath -w $PWD/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```

Verified by **COUNT**, not by tick (F37); **not piped to `tail`**, which masks the exit code. Baseline **3781**;
name every added test and state the new total as `3781 + n`. `SendAsync_WritesOnABackgroundThread` in the EAT
serial-transport tests is a known flake and is not chased on a full-suite run.

---

## Step 9 — write up, commit, push, PR, CI (controller)

`docs/synthetic-af-bank-followups-wave16-results.md`, and a wave-16 section appended to **PR #191** (findings
first). It must contain, because the design commits to them:

- the PROVENANCE block with the **one** binary's hashes and `BuildId`, and the sentence that **one binary served
  the gate, the probe and all six rungs** — or, if a second was needed, wave 14's disclosure in wave 14's words;
- **estimate AND actual per step** (design §7), and if the ladder misses its 11 m/rung, why;
- RULE S16's branch, named, with every clause beside its **pre-registered** threshold;
- the four things the design could **not** predict (§9), separated from the ones it could;
- RULE P16's verdict, with the instrument-vs-product reading stated;
- `query session`'s one line;
- §8's "what was not run and what it costs", carried forward and re-priced.

Verify CI by **COUNT read out of the log**, never by the tick.

---

## Register entries this wave will touch

| entry | expected edit |
|---|---|
| **F45** | wave 16's SEM-unit result, with the honest half (`D16` at s = 8.726) in the same breath, and the branch S16 took |
| **F62** | S16-B is built to answer F62 directly: it scores containment on `ΔR²`/`κ` against a reference the treatment cannot move, and quotes σ_focus nowhere as a bar |
| **F65** | V4′'s NOVEL-CONSENSUS count on a **new** population — the first prospective test of the corrected clause |
| **F66** | the ASCII-only printout rule (§2.2), which is a new instance of "a probe that reports absence" — a Unicode arrow becomes `0x1A` in a redirected log and a parser silently reports 0 of 40 |
| **F67** | RULE P16's verdict; P16-b's re-score; and, on a P-b, the narrowing from three candidate causes to two |
| **F63(b)** | close it — after wave 14 the shipped default **is** `astrodet`'s value and wave 16 pins both explicitly on every arm, so nothing is left to move (~0) |
| **F59** | unchanged; still the reason a knob-lifting converter is the wrong one, and still ~42 m to re-pin |

**RULE F14 is not touched, not cited as support, and no rung of wave 14's MAD-floor ladder is named as a
baseline.**
