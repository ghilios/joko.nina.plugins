# Synthetic AF bank — followups wave 15 (plan)

Design: [`docs/synthetic-af-bank-followups-wave15-design.md`](../docs/synthetic-af-bank-followups-wave15-design.md).
Standing authorisation: [`docs/waves14-21-autonomous-prompt.md`](../docs/waves14-21-autonomous-prompt.md).

**The controller runs every step that touches `TestApp.exe`.** A subagent's background processes are killed
when its session ends and tool timeouts cap at ten minutes; agents read, write, design and analyse, and never
launch an arm. **Only one `TestApp.exe` at a time**, so every arm is strictly sequential.

**Wave 15 ships no code.** There is no CODE agent step, no new test, and the suite count stays at wave 14's
**3781** — which is itself the check: a count that moved without a code change is a finding.

---

## Step order, with the stop conditions

| # | step | tool | ~cost | stops the wave if |
|---|---|---|---|---|
| 0 | commit the design + plan + drivers | git | — | — |
| 1 | build ONE binary into `D:\hf_w15\exe`; record sha256 ×2 and `BuildId` | `dotnet.exe` | ~2 m | build fails |
| 2 | re-check the F15 fingerprint (BEFORE state) | python | < 1 m | any landing already moved |
| 3 | **RULE G15** — the gate | `gate_w15.sh` | **~42 m** | **any of the eight misses at 6 dp** |
| 4 | score the gate, and self-test the scorer in both directions | python | < 1 m | gate FAIL, or the scorer cannot demonstrate both directions |
| 5 | **item A** — V4′ self-test, then the re-score | python | < 1 m | V4′ self-test fails |
| 6 | **item B / STEP 0** — the converter probe | `affit_w15.sh probe` | **~1 m** | `good/` is not TOOK **or** `mutant/` is not DID-NOT-TAKE |
| 7 | **item B** — the two landing arms, paired-interleaved | `land_w15.sh` | **~3 h 8 m** | one-key assertion fails; a half-written pair |
| 8 | fingerprint check after the arms | python | < 1 m | any bank landing changed (F15 regression) |
| 9 | **item B** — the scoring pass, both arms | `affit_w15.sh 0` / `1` | **~8 m** | — |
| 10 | verify both scoring populations by COUNT | `verify_affit_w15.sh` | < 1 m | short population ⇒ L15 UNEVALUATED |
| 11 | score RULE L15 | python | < 1 m | — |
| 12 | `query session` — one line for the UI item | `cmd.exe` | < 1 m | — |
| 13 | run the full suite, verified by COUNT | `dotnet.exe test` | ~4 m | count ≠ 3781, or any failure |
| 14 | ANALYSIS agent writes the results doc + register entries | agent | — | — |
| 15 | commit, push, append a wave-15 section to PR #191 | git | — | — |

**Total measured time: ~4 h 0 m against a 6 h ceiling.** If step 7 over-runs, apply the design's §5.1 drop
order: `D17` first, then the tail, never `D18/D19/D20`, never the scoring pass, never STEP 0.

---

## Step 0 — commit before anything runs

```bash
cd /home/ghilios/src/hocus-focus
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Wave 15 pre-registration: RULE G15, V4', RULE L15, and the landing->settings converter"
```

Commit `docs/synthetic-af-bank-followups-wave15-design.md` and
`plans/synthetic-af-bank-followups-wave15-plan.md`. **The drivers live outside the repo** at `/mnt/d/hf_w15/`
(as every previous wave's have); record their sha256 in the results doc so a later wave can tell whether one
moved.

```bash
sha256sum /mnt/d/hf_w15/*.sh /mnt/d/hf_w15/*.py
```

---

## Step 1 — ONE binary, and record its identity

```bash
cd /home/ghilios/src/hocus-focus
dotnet.exe build "$(wslpath -w "$PWD/Joko.NINA.Plugins/TestApp/TestApp.csproj")" -c Release -o 'D:\hf_w15\exe'
sha256sum /mnt/d/hf_w15/exe/TestApp.dll /mnt/d/hf_w15/exe/NINA.Joko.Plugins.HocusFocus.dll
```

**Never rebuild this directory for the rest of the wave (F53(c)).** Wave 15 ships no code, so nothing can force
a second build directory — that is the whole reason wave 14 needed two and this wave needs one.

`BuildId` is read from the first landing the gate writes, **as a field**. Do **not** run a `strings` probe: it
returns the same answer for every binary this project has produced (F66).

---

## Step 2 — the F15 control, BEFORE state

```bash
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w15/bank_landing_fingerprint_BEFORE.json
```

Expect `42 of 42 BYTE-IDENTICAL`. If it is not, the baseline is stale — re-write it with `--write` **and record
in the results doc that the baseline was re-taken and why**, because a baseline taken after an arm is not a
control.

---

## Step 3 — RULE G15, the gate. THIS STOPS THE WAVE

```bash
bash /mnt/d/hf_w15/gate_w15.sh 2>&1 | tee /mnt/d/hf_w15/gate_w15.log
```

Sequential, ~42 m, machine quiet, no NINA. The script asserts the population is 8 before it prints anything
else.

---

## Step 4 — score the gate, and demonstrate the scorer both ways FIRST

```bash
python3 /mnt/d/hf_w15/prov_w15.py --self-test /mnt/d/hf_w15/gate      # do NOT pipe to tail; it masks the exit code
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w15/gate --rule G15
python3 /mnt/d/hf_w15/prov_w15.py /mnt/d/hf_w15/gate
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w15/bank_landing_fingerprint_BEFORE.json
```

- **WSL paths only.** `D:\hf_w15\gate` yields eight `UNEVALUATED` and looks exactly like a failed arm.
- The `--self-test` run must report **PASS on the real arm** and **FAIL on the mutated copy, on both clauses**,
  with the mutation's before/after values printed. Only then may the plain run be quoted (F66(a)).
- **A partial reproduction is a FAILURE. Stop the wave and report it as a finding**: `pinned_settings_w11.json`
  sets `MaxOutlierRejections` explicitly, so a moved gate means something reached where it should not have.
- Record `BuildId`, `DetectorVersion`, `ProfileId`, `FitInputs`, `ConcurrencyCheck` (**across the arm**) and
  `BaselineJ`.

---

## Step 5 — item A: V4′ and the re-score. Zero `TestApp` time

```bash
python3 /mnt/d/hf_w15/score_f14_w15.py --self-test
python3 /mnt/d/hf_w15/score_f14_w15.py --root /mnt/d/hf_w14 --w13 /mnt/d/hf_w13 \
        --stagea /mnt/d/hf_w14/stageA --committed /home/ghilios/src/hocus-focus/docs/wave14-stageA \
        --out /mnt/d/hf_w15/f14_rescore.txt
```

The self-test must show V4′ **passing** a lawful rung, **reporting** an F65-shaped novel consensus without
failing it, **failing** each of the four violation shapes, and matching a `NaN` row against itself. Only then
run it on wave-14 data.

> ### THE OUTPUT OF THIS STEP IS NOT EVIDENCE — see design §2.3
>
> The results doc must publish it as **`RULE F14 — corrected diagnostic (wave-14 data, post hoc)`**, under the
> banner the scorer prints, and:
>
> - **RULE F14 stays at NO VERDICT** (design §2.4's recommendation). **No rung is named and nothing is
>   recommended.**
> - Every register entry item A touches (F45, F65, F66) carries the words *corrected diagnostic, wave 14 data*.
> - **If the controller decides to convert this into a verdict anyway**, the results doc must say — in the same
>   paragraph as the numbers — that it **overrode the design's recommendation and applied a repaired rule to
>   data whose outcome was public**, citing design §2.4. That is the wave-14 item-1 precedent, and it is the
>   only form of override that stays distinguishable from a moved goalpost.

Expected (already public, so it is a *check on the plumbing*, not a result): V1 PASS, V2 PASS ×6,
V3′ 72 of 72 all INERT with 0 INCONSISTENT, V4′ **0 violations on 585 triples**, and **≥ 1 novel-consensus row**
(`Panos_attempt01` at `B1.00`, budgets 2 and 3). **A zero novel-consensus count means the scorer is not
looking, and is a finding about the scorer.**

---

## Step 6 — item B, STEP 0: the converter probe. THIS GATES THE ARMS

```bash
bash    /mnt/d/hf_w15/affit_w15.sh probe
python3 /mnt/d/hf_w15/score_land_w15.py --self-test
python3 /mnt/d/hf_w15/score_land_w15.py --probe /mnt/d/hf_w15/probe \
        --probe-result /mnt/d/hf_w14/gate/D18_m24_deep_shed/attempt01/optimize_result.csv
```

`affit_w15.sh probe` first runs `convert_landing_w15.py --self-test`, then converts **wave 14's** already-landed
`D18` twice — once properly, once with `UseOptimizedSettings` flipped back to `False` — and runs `af-fit` on
each (~30 s total).

**Required outcome: `good/` → `TOOK`, `mutant/` → `DID-NOT-TAKE`.**

- **If `good/` is not TOOK**, the converter does not reach the detector. **Do not run the arms.** Record the
  classification and the star counts, and report it as a blocker with the diagnosis: the most likely causes are
  a curated knob whose setter clamps its value (the F59 mechanism) or a `PixelScale`-dependent gate resolving
  differently between the two pipelines. Either is a genuine finding.
- **If `mutant/` is not DID-NOT-TAKE**, the control cannot discriminate and is worthless. Same stop.

---

## Step 7 — item B: the two landing arms (~3 h 8 m)

```bash
bash /mnt/d/hf_w15/land_w15.sh 2>&1 | tee /mnt/d/hf_w15/land_w15.log
```

Run it in the background from the **controller** session with an `until`-loop wait; a subagent's background
jobs die with its session.

The script, before it runs anything, re-asserts that S0 and S1 differ in exactly `MaxOutlierRejections` **and in
nothing else, or in nothing** — and aborts on either. It runs the pre-registered order
`D18, D19, D20, D01…D17`, arm 0 then arm 1 per dataset, so any stop point leaves a complete paired population.

**If it over-runs the wave's budget**, stop it between datasets and record exactly which were not run. Do not
restart a half-written pair — the script refuses to, and that refusal is deliberate.

---

## Step 8 — the F15 control, AFTER state

```bash
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w15/bank_landing_fingerprint_BEFORE.json
```

`42 of 42 byte-identical` is the expected result and is the second arm-level confirmation of wave 13's F15 fix.
Any change is an F15 regression and every landing this wave produced must be re-read against a fresh baseline
before it is quoted.

---

## Step 9 — item B: the out-of-sample scoring pass (~8 m)

```bash
bash /mnt/d/hf_w15/affit_w15.sh 0 2>&1 | tee /mnt/d/hf_w15/affit_mor0.log
bash /mnt/d/hf_w15/affit_w15.sh 1 2>&1 | tee /mnt/d/hf_w15/affit_mor1.log
```

Each converts every landing into `/mnt/d/hf_w15/settings/mor<N>/<dataset>.json` (base **S0** for both arms) and
runs `af-fit` with `--params-from __PINNED_NO_SAVED_PARAMS__`, `--confidence 0.95`, `--weighted true` and an
explicit `--step-size` cross-checked against each dataset's `synthetic_meta.json`.

---

## Step 10 — verify both populations by COUNT

```bash
bash /mnt/d/hf_w15/verify_affit_w15.sh /mnt/d/hf_w15/affit_mor0 20 'D:\hf_w15\settings\mor0'
bash /mnt/d/hf_w15/verify_affit_w15.sh /mnt/d/hf_w15/affit_mor1 20 'D:\hf_w15\settings\mor1'
```

This checks the **path** (`Settings: …` is printed only on the fall-through), the **count**, and that every run
produced both an `af_fit_summary.txt` and an `af_fit_points.csv`. It does **not** check that the settings took
— that is the star-count control, clause G-c, and conflating the two is exactly what wave 14's V3 cost.

---

## Step 11 — RULE L15

```bash
python3 /mnt/d/hf_w15/score_land_w15.py \
        --arm0 /mnt/d/hf_w15/land_mor0 --arm1 /mnt/d/hf_w15/land_mor1 \
        --score0 /mnt/d/hf_w15/affit_mor0 --score1 /mnt/d/hf_w15/affit_mor1 \
        --w13 /mnt/d/hf_w13 --gate /mnt/d/hf_w15/gate \
        --out /mnt/d/hf_w15/l15_score.txt
```

Validity gates **G-a…G-e** print before any decision clause. Then L15-S, L15-P, L15-N, L15-M, L15-B.

**The analysis agent must not re-decide a clause.** If one turns out unsatisfiable, that is a finding — and the
design has already pre-registered that **L15-P clause (b) is probably foreclosed** (design §4.1) and that the
expected verdict is **NO DOMINANCE**, with L15-S, L15-M and L15-B carrying the wave.

---

## Step 12 — the UI item, one line

```bash
cmd.exe /c "query session"
```

`Disc` ⇒ *"not attemptable, sixth wave, ~30 m on a connected session"*, one line in the results doc.
`Conn` ⇒ record that it became attemptable and hand it to the next wave with wave 13 §3's procedure.

---

## Step 13 — the suite, verified by COUNT

```bash
cd /home/ghilios/src/hocus-focus
dotnet.exe test "$(wslpath -w "$PWD/Joko.NINA.Plugins/Joko.NINA.Plugins.sln")" -c Debug --nologo
```

- **Read the COUNT out of the log, not the tick** (F37). **Baseline: 3781** — wave 14's count on this branch.
- **Do not pipe to `tail`**: it masks the exit code, and this register has the receipts.
- **Wave 15 adds no code and no tests, so the count must be exactly 3781.** A count that moved is a finding
  about the branch, not a nuisance.
- `SendAsync_WritesOnABackgroundThread` (EAT serial transport) is a known flake, unrelated to this work; if it
  fails, re-run it alone and say so.

---

## Step 14 — the ANALYSIS agent

Spawn a `general-purpose` agent with: the design, this plan, the register, and the artifact paths
(`/mnt/d/hf_w15/{gate_w15.log,f14_rescore.txt,land_w15.log,l15_score.txt,affit_mor*.log}` plus the fingerprint
outputs). It writes `docs/synthetic-af-bank-followups-wave15-results.md` and the `docs/followups.md` entries.

**Its instructions must include, verbatim:**

1. **It must not re-decide a rule.** If a clause is unsatisfiable, that is a finding.
2. **Item A's output is not evidence.** Publish it as `RULE F14 — corrected diagnostic (wave-14 data,
   post hoc)`, keep RULE F14 at **NO VERDICT**, name no rung, and mark every touched register entry
   *corrected diagnostic, wave 14 data*. If the controller overrode this, say so in the same paragraph as the
   numbers, citing design §2.4.
3. **Quote no number from an instrument that has not been demonstrated to pass AND to fail.** Name each
   demonstration.
4. **`UNEVALUATED` is never `0`**, and *"could not look"* is never *"no difference"*.
5. **Never quote `σ_focus`, `J`, `R²` or reduced `χ²` as a vote** on anything that changed which points are
   fitted (F62). They are magnitudes.
6. **Record the actual wall time against both estimates** in §5's table, and say which estimator was right.
7. **Correct the register on wave 13's I5 timing**: `land_w13.log` says 37 m 14 s, wave 13's budget table says
   21 m. Per-run the three synthetic arms agree to within 1 %, so there is one rate (4.71 m/synthetic dataset)
   and a wrong published total.
8. **New register entries this wave can produce**, if the data supports them:
   - `af-fit --settings <landing>` fails silently, and `SettingsFingerprint` cannot detect it (design §3.2) —
     **this is a product/harness defect and is worth an entry whatever item B returns**;
   - the `optimize` ↔ `af-fit` star-count identity, which is a free cross-pipeline control nobody had used;
   - L15-B's alarm, if a landing focuses ≥ 1.0 step from truth;
   - G-e's outcome, which measures the optimizer's own run-to-run reproducibility at fixed settings — a number
     the register has never had.

---

## Step 15 — commit, push, PR

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Wave 15: ..."
git push origin ghilios/synthetic-af-bank-followups-wave13
```

Never push `develop`. Never open a new PR. Append a wave-15 section to PR #191's body, findings first, and
retitle the PR if the scope has outgrown its title.
