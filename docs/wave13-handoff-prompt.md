Continue the HocusFocus synthetic-AF-bank followups. Wave 12 is PR #190 (VERIFY IT MERGED — at handoff it was
OPEN + MERGEABLE with CI COMPLETED/SUCCESS and the count verified in the log, 3744 of 3744; develop was 3740,
+4 all named). Branch was ghilios/synthetic-af-bank-followups-wave12.

Start by reading docs/synthetic-af-bank-followups-wave12-results.md and the F45, F61, F60, F55, F58, F57, F19,
F15, F59, F53, F41, F42, F8 entries in docs/followups.md.

WHAT WAVE 12 SETTLED, SO YOU DO NOT RE-LITIGATE IT
 - FAN-OUT IS AUTHORISED AT DEGREE 4, UNPINNED ONLY. Eight landings, four workers, FOUR different profiles split
   4/4 on MaxOutlierRejections, all bit-identical to sixteen digits (RULE A12). --profile-id + fan-out STILL
   fails loudly, so a PINNED arm cannot fan out at all and runs sequentially.
 - AND IT BUYS 1.33x, NOT 4x (F60). Per-run time inflates 1.45-2.55x; the eight-run arm went 43m->32m while busy
   time went 43m->76m. BUDGET A 39-RUN PASS AT ~2 1/4 h, NOT ~45 m. Sequential is still the default.
 - F58(d) IS CLOSED. All five harness sites build AutoFocusOptions through HarnessSettingsStore.BuildFitOptions;
   a unit test fails the build if any TestApp source constructs it from the profile again. HocusFocusPlugin.cs
   is deliberately exempt and CORRECT — do not "fix" it.
 - F61 IS NEW AND IT IS THE INTERESTING ONE: the profile was reaching the harness through the SENSOR MODEL, not
   the AF fit. SensorModel.cs:714 takes MaxOutlierRejections/OutlierRejectionConfidence for the PER-STAR curve
   fit, so WHICH STARS entered the paraboloid depended on the active profile — sStars 613 vs 616 on toml999,
   2800 vs 2856 on mccomiskey, and TILT THETA 7.6% APART ON muggsie. bank-verify's sigmaFocus never moved.
 - F19's REMAINDER IS CLOSED. Three statistics over gate rejections are now refuted. The third
   (WingRejectedExcess) is ANTI-CORRELATED with what it predicts: Spearman rho = -0.665 over 13 rungs; on D17,
   sigma_focus 19x worse at 8s than 120s, wings and core reject at IDENTICAL rates, both exactly 0.0000.
   DO NOT PROPOSE A FOURTH STATISTIC OVER GATE REJECTIONS. Gate rejections count what the detector THREW AWAY;
   a starved frame's problem is what it never FOUND. Re-opening needs a different QUANTITY.

THE FIRST GATE
Wave 11's eight values have now reproduced across THREE binaries and two settings files, bit-identically:
  toml999 0.995784 | CWhiteFocus 0.996068 | uneven 0.996368 | muggsie 0.997195
  mccomiskey 0.976746 | D18 0.999882 | D19 0.999487 | D20 0.999738
Re-run D:\hf_w12\gate_w12.sh shape on a FRESH exe from develop-after-#190, with BOTH
  --settings D:\hf_w11\pinned_settings_w11.json   (md5 a67ffc06)
  --profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382   (astrodet)
NAMED IN THE SCRIPT HEADER. ONE pass/fail clause; a partial reproduction is a failure. Score with
D:\hf_w12\score_w12.py (it already carries the eight values, the wave-11 BaselineJ, and the field checks).
Free controls, all FIELDS: BuildId (must DIFFER), DetectorVersion, ProfileId, ConcurrencyCheck, FitInputs.
Wave 5's phi table is invalid on three axes. Do not quote it.

Then, in this order, and do not reorder for convenience:

1. DECIDE MaxOutlierRejections AS A PRODUCT DEFAULT. It is the through-line of waves 9-12 and NOBODY HAS ASKED
   WHICH VALUE IS RIGHT.
   One integer has now moved: BaselineJ by 0.0144 (F57), the two "attractors" that cost waves 9-10 (F55/F58),
   and tilt theta by 7.6% (F61). This machine's nine profiles partition 2/7 on it. NINA's code default is 1;
   astrodet uses 0. Every wave since 5 has treated it as machine state to be pinned. IT IS ALSO A SHIPPED
   PRODUCT DEFAULT that decides whether the AF fit may drop one Grubbs outlier — which is exactly F45's
   complaint (the Grubbs test rejects the IN-FOCUS point of a near-perfect curve, and the blind walk then buys
   an extra exposure).
   THE ARM: measure BOTH values over the bank, on the SAME frames, changing nothing else — sigma_focus and J
   from optimize, recall/precision AND the sensor fit (sStars/sR2/sRMS/sTheta) from bank-verify. Both values
   supplied via --settings (the fit inputs are pinned there since wave 11), so this is a settings sweep and NOT
   a profile sweep — do not reintroduce the defect to measure it.
   PRE-REGISTER THE "NO CHANGE" OUTCOME AS A FIRST-CLASS RESULT: if neither value dominates on the population,
   the default STAYS and the finding is "a knob that moved every number this project argued about does not
   decide the product" — which is a real answer. Name in advance what "dominates" means (a rule over the
   population, not a count of wins) and what would make you SHIP a change.
   WATCH THE ASYMMETRY F61 FOUND: the AF fit and the SENSOR fit may prefer OPPOSITE values. If they do, say so
   and do not average them — they are different products.

2. CONFIRM WAVES 8-12's AF/WIZARD CHANGES IN THE APP. THIS HAS BEEN CARRIED FOR FOUR WAVES AND MUST NOT BE
   CARRIED A FIFTH SILENTLY.
   The csproj PostBuild xcopy FAILS SILENTLY WHEN NINA IS RUNNING, which is why this keeps slipping. Close NINA,
   build, verify the plugin DLL timestamp in NINA's plugin folder BEFORE launching, then launch (Start-Process,
   NOT the MCP launch_executable — see the nina-live-verification-gotchas memory; the Simulator tilt port needs
   the sim camera connected first).
   Give it a HARD TIME BOUND and PRE-REGISTER WHAT TO REPORT IF BLOCKED: exactly what was tried, what failed,
   and which specific changes remain unconfirmed BY NAME. "Not this wave's item" is not an acceptable outcome
   for the fifth consecutive wave.

3. F15 — the last structural blocker, and it is cheap.
   `optimize --per-run` still rewrites optimized_settings.json into each run folder with no suppress flag, so a
   population pass cannot run beside an arm. Now that fan-out is authorised this is the ONLY thing still forcing
   passes to be serialized against each other. Add the opt-out (write only to --out unless a flag opts in, or
   snapshot the previous file), and note the interaction F15 already records: bank-verify --opt-a/--opt-b and
   golden eval --params optimized read the RUN FOLDER copy by default, so a prepass and a later scoring run that
   were meant to be independent can silently share an arm.

DEFERRED, WITH REASONS
 - F59's five knobs (MaxDistortion, StarCenterTolerance, SaturationThreshold, HotpixelThreshold, Sensitivity)
   are at CODE DEFAULTS in the pinned file since wave 5. Waves 5-12 are internally valid; the file does not
   DESCRIBE the detector. Re-pinning MOVES THE COORDINATE SYSTEM and needs a new gate baseline. Price it.
 - The landing-level wavelet bisect (D:\hf_w10\exe_v1wav, built wave 10, STILL UNUSED). Now doubly bounded: the
   seed-level answer is exact and identical, and wave 12's fan-out arm showed the search does not amplify an
   arbitrary process-level perturbation. Consider CLOSING F57(d) rather than running it.
 - F61(b): re-running a historical tilt calibration under pinned fit inputs to see whether a published theta
   moves. Nothing depends on it; the honest statement is "unrecorded input", not "known error".
 - F52(d), F46(b), F54, F50: nothing depends on them.

TRAPS WAVE 12 CREATED OR CARRIES
 - ConcurrencyCheck == "exclusive" ON A SINGLE LANDING IS NOT EVIDENCE THE MACHINE WAS QUIET. WaitOne(0) is won
   by exactly ONE of N contenders, so precisely one landing per batch truthfully reports exclusive. READ IT
   ACROSS A WHOLE ARM.
 - THE PROFILE SET IS MACHINE STATE AND IT MOVES. D:\hf_w12\profiles_before_w12.txt is the wave-12 snapshot
   (astrodet is now #1 by LastUsed because the wave-12 gate pinned it eight times). Re-snapshot with
   D:\hf_w12\snapshot_profiles_w12.py BEFORE any fan-out arm.
 - DO NOT BUDGET FAN-OUT AT 4x. It is 1.33x (F60), and a pinned arm cannot fan out at all.
 - Newtonsoft writes NaN and Infinity as the STRINGS "NaN"/"Infinity". Use score_excess_w12.py's num().
 - "COULD NOT LOOK" NEEDS ITS OWN STATE AT EVERY LEVEL, INCLUDING THE SCORER'S. Wave 12's scorer was wrong
   TWICE by conflating "the product declined" (shipped field is NaN) with "this rung can anchor a clause"
   (sigma_focus is finite). They are different questions and a hard-floor FAIL is not the same as either.
 - D17_cdk14_oiii5 finds ZERO STARS at 0.5s and 2s (5nm OIII) — hard-floor FAIL, everything NaN. Useful as a
   starvation extreme; useless as a measurement.
 - `strings <dll> | grep AtrousWaveletFast` still distinguishes v1 from v2 retroactively.
 - The console "Optimization complete" line is NOT reliably emitted (uneven has none). aggregate_summary.json is
   the instrument.
 - lumos exits rc=3 reproducibly; astrodet the DATASET is frameless (F14); Panos has a degenerate sigma fit.
 - Never rebuild an arm's directory mid-wave (F53(c)).

MEASUREMENT DISCIPLINE — every line below cost this project real time
 - A CONTROL THAT CANNOT FAIL IS NOT A CONTROL, AND "IDENTICAL" IS THE EASIEST WAY FOR ONE TO HIDE. Wave 12's
   RULE B12 compared 692 values and found them identical — equally consistent with a fix and with a no-op. What
   rescued it was perturbing one leaf by 1e-12 to prove the differ could speak, and B12-D asking the OPPOSITE
   question (does the profile still move anything? 15 of 24 before, 0 of 24 after). REQUIRE THE FIX TO BE
   VISIBLE, not merely harmless.
 - THE FLAGGED-BUT-DEFERRED SITE WAS THE ONE THAT MATTERED. Wave 11 recorded BankVerifyRunner as "x2" and the
   second instance was an afterthought. It was the sensor model, and it moved tilt theta 7.6%.
 - MEASURE THE PAYOFF YOU ARE BUYING, NOT THE ONE YOU ASSUMED. Item 1 was justified as "~4x cheaper" and
   delivers 1.33x. Write down both halves; a wave that records only the half that justified the work is how a
   project acquires a belief it never measured.
 - FIX THE RULE BEFORE THE DATA, AND ON THE BAR THE PREDECESSORS FACED. Wave 12's first W1 draft was stricter
   than the rule its predecessors were held to; it was weakened and committed before the ladder rendered a
   frame. Refuting on a stricter bar is a moved goalpost, not a refutation.
 - THE REFUTATION THAT GENERALISES BEATS THE ONE THAT FITS. "No threshold satisfies the rule" closes one
   candidate; "rho = -0.665, because gate rejections count what was thrown away and starvation is about what was
   never found" closes the whole family.
 - THE CHEAPEST INSTRUMENT IS THE ONE ALREADY PRINTED AND IGNORED. Before building one, grep the logs you have.
 - SAY WHAT YOU DID NOT RUN, and price it.
 - PIN --settings AND --profile-id ON EVERY ARM (except a deliberate fan-out, which must argue for itself).
 - On CI: verify the test COUNT, not the tick (F37). An ABSENT check is more dangerous than a red one.
   develop's baseline is 3744.

Write the spec in docs/<topic>-design.md and the plan in plans/<topic>-plan.md FIRST, and COMMIT THEM BEFORE ANY
MEASUREMENT so the pre-registration is in git history before the data. FLAG new findings in docs/followups.md.
Finish with a PR; never push develop.

Build: dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w13\exe'
Tests: dotnet.exe test "$(wslpath -w <abs>/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
Wave-12 artifacts: D:\hf_w12\ (exe [pre-item-2], exe2 [post-item-2], gate\, fanout\, b12d\, ladder\, bv_before\,
  bv_after\, profiles_before_w12.txt, gate_w12.sh, fanout_w12.sh, ladder_w12.sh, bankverify_w12.sh,
  bankverify_disc_w12.sh, score_w12.py [the gate scorer — REUSABLE AS IS], score_excess_w12.py [num() + the
  wing pooling], compare_bv_w12.py, snapshot_profiles_w12.py, gate_score.txt, fanout_score.txt, ladder_score.txt,
  ladder_corr.txt, b12d_score.txt, bv_compare.txt, suite.log)
Wave-11: D:\hf_w11\ (pinned_settings_w11.json [a67ffc06], bisect\make_bisect_profile.py, det\, pop\)
Wave-10: D:\hf_w10\ (pop\ = the 39-run population landings; exe_v1wav)
Wave-7 that must survive: D:\hf_w7\armE (the exposure ladder), f18arms, golden\
Banks: D:\SyntheticAutofocusBank (20 datasets), D:\Autofocus Bank (19 runs)
