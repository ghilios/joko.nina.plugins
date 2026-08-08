# Synthetic AF bank — followups wave 10 (plan)

> **CLOSED 2026-08-08.** Results: [`docs/synthetic-af-bank-followups-wave10-results.md`](../docs/synthetic-af-bank-followups-wave10-results.md).
> Step 0 ✅ (G10-A PASS 8/8; **G10-B fired and is VOID** — its own control refuted the attribution, see F57).
> Step 1 ✅ (**RULE P FIRES**, the wing verdict is withdrawn). Step 2 ▲ partial (the build hypothesis is dead
> and F53(a) shipped; the KappaSigma trigger rate is still owed). Step 3 ✅ (F49(c) shipped; F21 diagnosed; the
> deadband refuted before implementation, so RULE S was never applied). Step 4 ✅ (3722/0).
> **Not run, and named as such:** the landing-level wavelet bisect (§3.2) and the KappaSigma rate probe.

Design: [`docs/synthetic-af-bank-followups-wave10-design.md`](../docs/synthetic-af-bank-followups-wave10-design.md).
Register: [`docs/followups.md`](followups.md).
Branch: `ghilios/synthetic-af-bank-followups-wave10` off `develop` @ `02c62d8`.

**Ordering is fixed and is not to be rearranged for convenience.** Step 0 gates everything. Steps 1–3 run in the
order below because item 1 is live product behaviour validated on two datasets, item 2 makes every future arm
cheaper and is 40 seconds per trial, and item 3 is the largest user-facing cluster but the one with no
correctness deadline.

**Nothing runs concurrently with an `optimize` pass** (F55). Code editing, doc writing and reading are fine; a
second `optimize` process is not.

---

## Step 0 — The gate ✅ RUNNING

| | |
|---|---|
| build | `dotnet.exe build TestApp.csproj -c Release -o 'D:\hf_w10\exe'` — **done**, `02c62d8`, `StarDetectorVersion` 2 |
| settings | `D:\hf_w10\pinned_settings.json`, `md5 df7c7cd14181b4ace59b92b89e219f67` — **verified byte-identical to waves 5–9** (F42) |
| provenance | recorded by hand in `gate_repro_w10.sh`'s header (F53's stamp does not exist yet) — `TestApp.dll` `5e58b669…`, plugin dll `ccb3e3e9…` |
| script | `D:\hf_w10\gate_repro_w10.sh` → `D:\hf_w10\gate\` |

0a. Run the eight sequentially. **Record all eight values as wave 10's fixed point.**
0b. Apply **G10-A**: re-run one of the eight, sequentially and alone; must match to 6 dp. **If it fires, STOP** —
    every wave-10 comparison is void before it is made.
0c. Apply **G10-B**: count how many of the eight moved against wave 5's version-1 values, and read it against the
    0 / 1–3 / ≥ 5 table fixed in the design. **This is a measurement, not a gate** — record the count and its
    reading either way.
0d. `D:\hf_w10\exe` is **never rebuilt**. Later builds → `exe2`, `exe_v1wav`, …

> **Already true after run 1:** `toml999` = **0.995784**, which is wave 9's *fan-out* value, not its sequential
> one. Design §0.2 states the H1/H2 fork this opens; Step 2a is its discriminator. Do not resolve it by argument.

---

## Step 1 — F19's population check (item 1)

**RULE P is in design §1.2 and is fixed. Do not edit it after seeing a number.**

1a. Write `D:\hf_w10\wing_pop.sh`: one **sequential** pass, shipped-default invocation (`--per-run`,
    `--max-evals 250`, `--settings` pinned, no flags), over all 20 synthetic datasets + 19 real runs
    (`astrodet` excluded, F14). `FrameDiagnostics` carried so the wing axis is populated.
1b. Write `D:\hf_w10\score_wing_pop.py`: for every run emit `WingRejectedFraction`, `WingIsShedding`, the final
    ask ratio, `BaselineJ`, `BestJ`, σ_focus. **Must distinguish NaN from 0** — that is P4's whole content, and
    a scorer that coerces them together makes the instrument report an all-clear it never measured.
    (`score_f32.py`'s Newtonsoft `"NaN"`-STRING coercion is the known trap; inherit the fix, not the bug.)
1c. Run it. ~3–5 h. Nothing else runs.
1d. Score against P1–P4. **Name every newly-firing dataset with a reason.**
1e. Free controls, read before the verdict:
    - the 8 gate runs' `BestJ` must match Step 0's values to 6 dp (F41/F55 — same invocation, same binary);
    - `BaselineJ` across all 39 is the standing F55 control.
1f. **F15:** this pass is the shipped-default invocation, so the banks end holding the shipped-default landing.
    No re-land owed.

**Outcome branches, both written down now:**
- **RULE P silent** ⇒ the wing statistic is validated on a population instead of on two datasets. Record the
  fire list and the rate; F19's remainder closes.
- **RULE P fires** ⇒ turn `WingIsShedding` off by default in the same PR, with the measured reason. This is
  product behaviour that can raise a user's exposure 2×; a refuted default does not stay shipped pending a wave.

---

## Step 2 — F55(b), the nondeterminism (item 2)

Everything here is minutes-per-trial. **Do not re-run design §2.2's eliminated candidates.**

2a. **The cross-build probe — decisive for Step 0's H1/H2 fork.** Determinism probe on `toml999`, 5× sequential
    on `D:\hf_w9\exe` (v1) and 5× sequential on `D:\hf_w10\exe` (v2), **back to back in one session**.
    - v1 → 0.997993 ×5 and v2 → 0.995784 ×5 ⇒ **the BUILD selects the attractor**; H1 holds, and F55's
      `toml999` evidence was never about concurrency.
    - either binary splitting within its own five ⇒ H2, and the attractor is session/load state.
2b. **The wavelet bisect.** Build `D:\hf_w10\exe_v1wav` = this tree with `StarDetector.cs:619/622` calling the
    legacy `CvImageUtility` dense path (which survives as the equivalence oracle). Re-run 2a's sequential phase.
    Isolates the wavelet from the rest of the v1→v2 delta. **Not committed** — a bisect build, not a product
    change.
2c. **The OpenCV thread probe.** Pin OpenCV's thread count to 1; re-run the probe's **concurrent** phase.
    Bimodality vanishing ⇒ design §2.4 is the mechanism.
    **`WSLENV` must be set** and the probe must carry a **positive control**: a configuration known to change the
    answer has to be shown changing it, or a null reading is an unplugged instrument (wave 9's own trap).
2d. **F53(a) — the build stamp. Ships regardless of 2a–2c.** `optimize` prints informational version + commit +
    `StarDetectorVersion`. Build to `exe2`; verify inertness by re-running one gate run and matching Step 0 to
    6 dp. This is the standing fix for "a `Reproduce:` line names a COMMAND, not a result".
2e. **F54** — check whether a build-selected attractor explains it for free. If it does, F54 closes without any
    `ApplyFactor` defect; if not, say so and leave it open.
2f. Whatever is found, update F55's entry with what was **eliminated by measurement** — the negative results are
    the durable part.

---

## Step 3 — The step recommender (item 3)

**RULE S is in design §3.4 and is fixed.**

3a. **Measure F21's owed reproducibility first** — it is what sizes the deadband, and it is the diagnosis F21 has
    owed since 2026-08-02. `synth-validate --scenarios S0` over the bank; record the run-to-run spread of
    `HalfWidth` and of the recommended step at a fixed dataset+step. Instrument `FindHalfWidth`'s fitted minimum,
    its `3 × min` target and the bracket it converged on, on the two saved `D17` sweeps, and confirm or refute
    the coarse-walk hypothesis F21 names.
3b. **(A) the copy** — capped ⇒ say it is a partial step, name what it converges toward (`3 × HFR_min`), and
    quote the **exact** ratio (`1.5 × P / 3.5`). Do **not** quote a projected run count: it is computed from the
    extrapolation the cap exists to distrust.
3c. **(B) the deadband** — threshold set at or above 3a's measured spread. Below it the recommendation reads
    "converged", not a new number.
3d. Unit tests, each confirmed by **neutralizing** (remove the mechanism ⇒ the named test fails, and no other):
    the ratio arithmetic at P = 4 and P = 5; the field session's 100 → 214 → 459 sequence; the deadband's
    terminating behaviour at 459 → 474 → 482 (RULE S's S3); byte-identical behaviour when the deadband is
    disabled.
3e. Run RULE S's arm (`synth-validate`, control vs deadband, one binary, `--settings` pinned). **S2 is the
    clause that can kill it and must not be relaxed.**
3f. Ship or don't, per RULE S, and record which clause decided it.

---

## Step 4 — Verification and PR

4a. **Full local suite** — `dotnet.exe test <sln> -c Debug --nologo`. **Record the COUNT, not the tick** (F37).
    `develop` is **3708**; state the delta and what accounts for it.
    Known-flaky and not to be chased on a full-suite run: `SendAsync_WritesOnABackgroundThread`.
4b. Write `docs/synthetic-af-bank-followups-wave10-results.md`, opening with the **provenance** of every number
    in it (`StarDetectorVersion` 2, `D:\hf_w10\exe`, the two sha256s) — wave 9's banner is the model.
4c. Update `docs/followups.md`: F19, F55, F53, F54, F21, F49, F51 as measured; **flag new findings as new
    entries** rather than folding them into old ones.
4d. Re-check `githubstatus.com` before merging and verify the PR's check ran with the **full count**. An ABSENT
    check is more dangerous than a red one.
4e. Push the branch, open the PR against `develop`. **Never push `develop`.** Commit with the privacy email:
    `322725+ghilios@users.noreply.github.com` for both author and committer.

---

## Known deferrals, priced rather than predicted

| item | why it is not in this wave |
|---|---|
| **F52(d)** — a cost term in `J` | PR #187 made per-layer wavelet cost nearly flat; `StructureLayers` is no longer a cost driver and a cost term added now would penalise an artifact |
| **F46(b)** — present the binning recommendation | nothing depends on it |
| **F25's fit-quality gate** | a real defect in the same method; neither of Step 3's halves makes it worse, and bundling it would put two mechanisms behind one acceptance rule |
| **F26's deferral bound** | the wizard's binning-first ORDERING, downstream of F22 — not the recommender's arithmetic |
| **F18's `W_detect` default** | wave 7's arms returned a null; the bank cannot exercise the defect and nothing here changes that |
