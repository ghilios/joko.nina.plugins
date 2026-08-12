# Synthetic AF bank — followups wave 17 (implementation plan)

Design: [`docs/synthetic-af-bank-followups-wave17-design.md`](../docs/synthetic-af-bank-followups-wave17-design.md).
Charter: [`docs/waves17-21-handoff-prompt.md`](../docs/waves17-21-handoff-prompt.md).
Drivers: `/mnt/d/hf_w17/` (`D:\hf_w17\`).

**This wave ships NO C# code.** One binary, built once, never rebuilt (F53(c)).
**The controller runs every `TestApp` arm** — a subagent's background processes die with its session and tool
timeouts cap at 10 minutes.

---

## Files in `D:\hf_w17\`

| file | what it is | self-tests? |
|---|---|---|
| `gate_w17.sh` | RULE G17's driver. Writes both BEFORE fingerprints, refuses to run if the arm dir is populated / `TestApp` is running / NINA is running, asserts the population count, then prints the scoring commands | — |
| `prov_w17.py` | G17's free controls as FIELDS, read across the arm. Wave 16's `prov_w16.py` carried forward with `10bc1b47...` added to `PRIOR_BUILD_IDS` | `--self-test` (both directions, mutation asserted by read-back) |
| `binning_w17.sh` | item A's two arms, X1 then X0, with the `G17_PASSED` interlock, the flag-took assertion and the population count | — |
| `score_c17_w17.py` | RULE C17: V1, V2, V3, A, B, C, D and the branch table | `--self-test` (**13 demonstrations**: all six substantive branches reached on constructed input, plus the comparison primitive and the empty-set answer) |
| `score_d17_w17.py` | RULE D17: the seed-vs-baseline field diff over the gate logs, the 50-field domain assertion, the wave-16 reproduction control and the `UseAdvanced` warning read | `--self-test` (**7 demonstrations**, including *"an empty union from an empty population is D-UNEVALUATED, not D-NOOP"*) |
| `aux_fingerprint_w17.py` | the SECOND F15-class control: 39 `harness_settings.json` + 20 `synthetic_meta.json` | `--self-test` (**4 demonstrations**, including *"an empty population FAILS, it does not report nothing-changed"*) |

**All three self-tests were run at pre-registration time and pass (13/13, 7/7, 4/4), and the interlock was
exercised in both directions with the marker removed afterwards.** `prov_w17.py`'s BuildId-novelty clause was
additionally shown to **FAIL on wave 16's real gate** now that `10bc1b47...` is in `PRIOR_BUILD_IDS` — a
demonstrated FAIL direction on real data rather than on a fixture.

Reused unchanged (the cheapest instrument is the one already printed):
`python3 /mnt/d/hf_w12/score_w12.py ... --rule G17` and
`python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write|--check ...`.

**Every scorer refuses a Windows path** and exits 3. **Every scorer has three states** (ok / differs /
could-not-look) and the could-not-look guard comes **before any field read**. **Every printout is ASCII-only** —
a Unicode arrow becomes `0x1A` in a redirected log.

---

## Step 1 — commit the pre-registration (controller)

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Wave 17 pre-registration: F67(c) is neither of wave 16's two candidates, and F63(b) is already printed"
```

Nothing below runs before this commit exists.

## Step 2 — build ONE binary

```bash
dotnet.exe build "$(wslpath -w /home/ghilios/src/hocus-focus/Joko.NINA.Plugins/TestApp/TestApp.csproj)" \
  -c Release -o 'D:\hf_w17\exe'
sha256sum /mnt/d/hf_w17/exe/TestApp.dll /mnt/d/hf_w17/exe/NINA.Joko.Plugins.HocusFocus.dll
strings -el /mnt/d/hf_w17/exe/TestApp.dll | grep -x -- '--no-run-detection-binning' | wc -l   # expect >= 1
strings    /mnt/d/hf_w17/exe/TestApp.dll | grep -x -- '--no-run-detection-binning' | wc -l   # expect 0; this is EXPECTED
```

Record both sha256 values. **The `BuildId` is read from the first gate landing's `Provenance`, never from
`strings`.** Keep the two dll hashes in a table labelled *dll sha256*, never beside the `BuildId` (F66).

## Step 3 — the two BEFORE fingerprints

```bash
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write /mnt/d/hf_w17/bank_landing_fingerprint_BEFORE.json   # expect 42
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --self-test
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --write /mnt/d/hf_w17/bank_aux_fingerprint_BEFORE.json       # expect 39 + 20
```

Both must be written **before the gate**, because the gate runs `optimize` over bank folders.

## Step 4 — RULE G17

```bash
bash /mnt/d/hf_w17/gate_w17.sh 2>&1 | tee /mnt/d/hf_w17/gate_w17.log
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w17/gate --rule G17
python3 /mnt/d/hf_w17/prov_w17.py --self-test /mnt/d/hf_w17/gate
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w17/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w17/bank_aux_fingerprint_BEFORE.json
```

- **G17-1 must be 8 of 8 at 6 dp.** A partial reproduction is a FAILURE and stops the wave.
- `prov_w17.py --self-test` must print `SELF-TEST PASS` **before** any G17 free control is quoted.
- Only after both pass may the interlock be written:

```bash
python3 /mnt/d/hf_w17/score_c17_w17.py --arm-g17-passed /mnt/d/hf_w17/gate   # writes /mnt/d/hf_w17/G17_PASSED
```

`binning_w17.sh` refuses to run without that marker, exactly as wave 16's `W1_PASSED` made five rungs abort in a
second rather than produce output nobody could use. **Only the scorer can write it.**

## Step 5 — RULE D17 (no `TestApp` time)

```bash
python3 /mnt/d/hf_w17/score_d17_w17.py --self-test
python3 /mnt/d/hf_w17/score_d17_w17.py --gate /mnt/d/hf_w17/gate --w16 /mnt/d/hf_w16 \
        --affit /mnt/d/hf_w16/affit_N --out /mnt/d/hf_w17/d17_score.txt
```

D17 gates nothing. Record its verdict (`D-NOOP` / `D-SMALL` / `D-LARGE` / `D-UNEVALUATED`) and, per the design's
§3.4, issue the costed recommendation **DO NOT RE-PIN** regardless of which it is.

## Step 6 — item A, the two arms (controller, sequential, background + `until`-loop)

```bash
bash /mnt/d/hf_w17/binning_w17.sh X1 2>&1 | tee /mnt/d/hf_w17/binX1.log     # ~10 m
bash /mnt/d/hf_w17/binning_w17.sh X0 2>&1 | tee /mnt/d/hf_w17/binX0.log     # ~26 m
```

The driver, for each arm: aborts if the output dir exists, aborts if any `TestApp.exe` or NINA is running, runs
the 10 datasets sequentially with `< /dev/null`, asserts `optimize_result.csv produced: 10 expected: 10`, and
asserts C17-V3's flag-took strings (10 of 10 in X0, 0 of 10 in X1).

**Never rebuild either directory mid-wave.** If an arm has to be re-run, move the old one aside deliberately and
say so in the results doc.

## Step 7 — RULE C17

```bash
python3 /mnt/d/hf_w17/score_c17_w17.py --self-test
python3 /mnt/d/hf_w17/score_c17_w17.py \
        --x1 /mnt/d/hf_w17/binX1 --x0 /mnt/d/hf_w17/binX0 \
        --affit16 /mnt/d/hf_w16/affit_N --gate16 /mnt/d/hf_w16/gate \
        --w13 /mnt/d/hf_w13 --w15 /mnt/d/hf_w15 --gate /mnt/d/hf_w17/gate \
        --out /mnt/d/hf_w17/c17_score.txt
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w17/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w17/bank_aux_fingerprint_BEFORE.json
```

**Apply the branch table as written.** An unsatisfiable clause is a finding, not a licence to re-decide one. If
a validity clause is UNEVALUATED, RULE C17 returns UNEVALUATED and names the failing gate — it does not fall
through to a substantive outcome.

## Step 8 — the suite

```bash
dotnet.exe test "$(wslpath -w /home/ghilios/src/hocus-focus/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```

Expected **3824, unchanged** (no code ships). Verify by **COUNT**, not by tick, and do not pipe to `tail` —
that masks the exit code. `SendAsync_WritesOnABackgroundThread` is a known flake; do not chase it.

## Step 9 — analysis, write-up, push

1. Spawn the ANALYSIS agent. It applies the pre-registered rules and **must not re-decide one**.
2. Write `docs/synthetic-af-bank-followups-wave17-results.md`: PROVENANCE, one section per rule with every
   threshold beside its measurement, the budget estimate-vs-actual, and "what was not run and what it costs".
3. Update the register: F67 (a WAVE 17 block, closing (c)), F63 (closing (b)'s detector half as a costed
   recommendation), F39 (the reach table from §2.4), F68 (a wave-17 instance: a dump taken one statement before
   the mutation it needed to observe), F66 (the inverted-flag comment).
4. Commit with the privacy email, push, append the wave's section to PR #191, verify CI by **COUNT** read out of
   the log.

---

## Definition of done

- [ ] Pre-registration committed **before** the build.
- [ ] ONE binary; both dll sha256 values and a **novel** `BuildId` recorded; `DetectorVersion` read as a FIELD.
- [ ] RULE G17: 8 of 8 at 6 dp; `prov_w17.py --self-test` PASS in both directions with the mutation asserted.
- [ ] Both fingerprints clean before, between and after every arm; `--update-run-folder` passed nowhere.
- [ ] RULE C17 scored with its branch table applied as written, denominators printed, could-not-look named.
- [ ] RULE D17 scored; the recommendation is **do not re-pin** with the offset published.
- [ ] Item C recorded verbatim.
- [ ] Suite **3824**, verified by COUNT; CI verified by COUNT.
- [ ] Estimate **and** actual recorded for every step; the drop order recorded as used or not used.
