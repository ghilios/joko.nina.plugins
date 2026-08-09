# Synthetic AF bank — followups wave 12 (plan)

Design: [`docs/synthetic-af-bank-followups-wave12-design.md`](../docs/synthetic-af-bank-followups-wave12-design.md).
Branch: `ghilios/synthetic-af-bank-followups-wave12` off `develop` @ `96f10b3` (PR #189's merge commit —
**verified MERGED**).
Artifacts: `D:\hf_w12\`.

## Standing rules for every step

- **`--settings D:\hf_w11\pinned_settings_w11.json` (md5 `a67ffc06…`) on every `optimize`/`bank-verify`
  invocation**, and **`--profile-id ce3f3e63-…` (`astrodet`) on every one except item 1's fan-out arm**, whose
  unpinning is the experiment (design §2.2).
- **Never rebuild a build directory** (F53(c)). `D:\hf_w12\exe` = gate + item 1. `D:\hf_w12\exe2` = post-item-2.
- **Every driver script carries its inputs and its pre-registered rule in its header**, before it runs.
- **Every driver script refuses to start if a `TestApp.exe` is already running** (the `tasklist.exe | grep -c`
  guard from `k8_gate_on_fixed.sh`, which handles `grep -c`'s exit-1-on-zero correctly) — except item 1's, which
  fans out on purpose and asserts the opposite.
- **Long runs are launched from THIS session, not from a subagent** — a subagent's background jobs die with its
  session.

---

## Step 0 — Spec + plan (this document)

- [x] Verify PR #189 MERGED (`96f10b3`, 2026-08-09T00:34:03Z), `develop` fetched.
- [x] `docs/synthetic-af-bank-followups-wave12-design.md`.
- [x] `plans/synthetic-af-bank-followups-wave12-plan.md`.
- [ ] Commit both before any measurement, so the pre-registration is in git history **before** the data.

## Step 1 — Build, and the gate (RULE G12)

1. `dotnet.exe build TestApp.csproj -c Release -o 'D:\hf_w12\exe'` from the branch tip (which is `develop` plus
   the spec/plan commit — **no code change yet**, so the gate measures `develop` after #189).
2. Confirm `strings D:\hf_w12\exe\*.dll | grep AtrousWaveletFast` hits (v2, retroactive check per F53(a)).
3. `D:\hf_w12\gate_w12.sh` — `k8_gate_on_fixed.sh`'s shape, both pins **named in the header**, sequential,
   `--per-run --max-evals 250`, out to `D:\hf_w12\gate`.
4. Score with a small python scorer reading **`aggregate_summary.json`** (NOT the console
   `Optimization complete` line — `uneven` emits none): `BestJ`, `BaselineJ`, and the five provenance fields.

**Gate:** RULE G12 — 8 of 8 `BestJ` to 6 dp against design §1's table. Report bit-identity separately.
**Controls:** `BuildId` MUST DIFFER from `5cb7e474…`; `DetectorVersion` 2; `ProfileId` `astrodet`;
`FitInputs` = the four values; `ConcurrencyCheck` `exclusive` on **all eight**; `BaselineJ` reproduces wave 11.

**If G12 fails: STOP.** Nothing downstream is readable. Report which runs moved and by how much, and diff the
five fields first — the answer is far more likely to be in a field than in the code.

*Cost: build ~2 min, gate ~40 min.*

## Step 2 — Item 1: the fan-out arm (RULE A12)

1. **Re-snapshot the profile set** to `D:\hf_w12\profiles_before_w12.txt` — same format as wave 11's
   (`LastUsed` order, id, and the four fit inputs with `*` marking an absent key), **after** the gate has pinned
   `astrodet` eight times. A2 is scored against THIS snapshot.
2. `D:\hf_w12\fanout_w12.sh`: the same eight runs, **4 concurrent workers**, `--settings` pinned, **no
   `--profile-id`**, out to `D:\hf_w12\fanout`. Two batches of 4.
3. Score the same way, plus: distinct `ProfileId`s, their `MaxOutlierRejections` groups from the snapshot,
   `ConcurrencyCheck` per batch, and `FitInputs` equality across all eight.

**RULE A12** (design §2.3): A1 eight values to 6 dp · A2 ≥2 distinct profiles spanning both MOR groups ·
A3 ≥1 `concurrent` per batch · A4 `FitInputs` identical on all eight.

- **All four ⇒ fan-out AUTHORISED AT DEGREE 4** — recorded in `followups.md` under F55 as a run rule naming the
  degree, and `.claude/docs/testapp-cli.md` updated beside F42's/F57's pinning rules.
- **A1 misses ⇒ sequential stands**; name the run and the delta.
- **A2 or A3 unsatisfied ⇒ no authorisation regardless of agreement**; retry once with per-worker profile copies
  (`make_bisect_profile.py`, fresh GUIDs, deleted afterwards), and if that also fails to spread, say so and stop.

*Cost: ~20 min if authorised-degree parallelism holds; ~40 min worst case.*

## Step 3 — Item 2: convert the four F58(d) runners

Code, on the branch, with the suite run at the end of the step (not after every edit):

1. `BankVerifyRunner.cs:160` → `new AutoFocusOptions(profileService, harnessSettings.Accessor)`.
2. `BankVerifyRunner.cs:518` (the `SensorModel` construction) → same accessor.
3. `SynthValidateRunner.cs:243`, `InspectAlignRunner.cs:103`, `TiltCalibrationRunner.cs:178` → same.
4. Each runner renders `HarnessFitInputs.From(afOptions)` into the banner it already prints; `bank-verify` and
   `synth-validate` additionally into the report they already write.
5. **`HocusFocusPlugin.cs:119` is NOT touched** — a comment there says why, so the next reader does not "fix" it.
6. Tests: extend `HarnessFitInputsTests` with a fixture asserting that **each converted runner's fit inputs come
   from the FILE and not the profile** — the property, not the happy path (F59's lesson). The cheapest honest
   form is a test over the shared construction helper each runner now calls.

**RULE B12** (design §3.2) — `bank-verify` before/after on the golden-scored runs, both pinned:

- **before**: `D:\hf_w12\exe` (pre-conversion), out to `D:\hf_w12\bv_before`.
- **after**: `D:\hf_w12\exe2` (post-conversion), out to `D:\hf_w12\bv_after`.
- Same `--settings`, same `--profile-id astrodet`, same runs, sequential.
- **PASS = every scored quantity identical.** Any difference is a finding and gets documented as a discontinuity
  at this commit.

*Cost: code ~1 h; two `bank-verify` passes over the golden runs.*

## Step 4 — Item 3: the `WingRejectedExcess` arm (RULE W12)

1. Write `D:\hf_w12\ladder\ladder_w12.sh` with **the pre-registered prediction in the header** (design §4.5:
   `D02`@0.5 s excess 0.2439, `D16`@2 s excess 0.0064 — *prediction, not verdict*), the three datasets, the
   rungs, and RULE W12 in full.
2. Render: `synth-bank --spec` with `exposureSecondsOverride` per rung for
   `D17_cdk14_oiii5,D20_m24_bright_control,D05_tec140_1000mm`, rungs **0.5 / 2 / 8 / 30 / 120 s**, out to
   `D:\hf_w12\ladder\t<rung>`. **Renders into `D:\hf_w12`, never into the bank** (F15 + the gate's folders).
3. `optimize --per-run --max-evals 120` per (rung, dataset), pinned both ways, sequential unless step 2
   authorised fan-out — **in which case this is the first arm to spend the authorisation, at the authorised
   degree and no higher**.
4. Score with a wave-12 scorer derived from `score_wing_pop_w11.py` — **its `num()`, not wave 10's** — emitting
   per rung: σ_focus, wing fraction, inner fraction, **excess**, and the **shipped** `WingRejectedRatio`.
5. **Instrument validation first**: offline `wf / inf_` must reproduce the shipped `WingRejectedRatio` on every
   rung. If it does not, the scorer is wrong and no verdict is published from it.
6. **W5's median**: recompute the excess over wave 10's 39 population landings at `D:\hf_w10\pop\` — zero
   compute, conservative-only (design §4.4).
7. Apply W0 → W1–W5 in order.

**Outcomes** (design §4.3): empty window ⇒ **F19's remainder CLOSES**; non-empty ⇒ live candidate, **not
adopted**, owing a fresh-population validation. Either way **no verdict, threshold, or action ships**.

*Cost: render ~5–10 min; 15 optimizes at 120 evals ~20 min sequential.*

## Step 5 — Suite, docs, PR

1. `dotnet.exe test <sln> -c Debug --nologo` — **verify the COUNT**, not the tick (F37); `develop` is at
   **3740**, so the delta must be named test by test. Nothing piped to `tail` (it masks the exit code).
   `SendAsync_WritesOnABackgroundThread` is a known flake and is not chased on a full-suite run.
2. `docs/synthetic-af-bank-followups-wave12-results.md` — provenance banner (build dirs + sha256 + `BuildId`,
   settings md5, profile), one section per item, each with its rule and verdict, and a **"what was not run"**
   section with prices.
3. `docs/followups.md`: F55 (landing-level verdict + the authorisation or its refusal), F58 (d) closed or
   narrowed, F19 (closed or the excess's verdict), and any new entry this wave produces.
4. `.claude/docs/testapp-cli.md` if item 1 authorises fan-out.
5. Commit with the privacy email, push the branch, open the PR against `develop`. **Never push `develop`.**

## Budget

| step | cost |
|---|---|
| build + gate | ~45 min |
| item 1 fan-out | ~20–40 min |
| item 2 code + 2 × `bank-verify` | ~1.5–2 h |
| item 3 render + 15 optimizes | ~30 min |
| suite | ~10–15 min |

## What this plan deliberately does NOT do

- No 39-run population pass. Item 3's verdict does not need one, and W6 bars its rows from sizing the excess.
- No re-pin of F59's five knobs (it would move the coordinate system — design §5).
- No wavelet landing bisect (item 1 bounds it for free — design §5).
- No F15 fix.
- No in-app confirmation of waves 8–10's AF/wizard changes.
