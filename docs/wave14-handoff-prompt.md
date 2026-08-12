Continue the HocusFocus synthetic-AF-bank followups. Wave 13 is PR #191 (VERIFY IT MERGED — at handoff it was
OPEN with CI running; develop's suite baseline was 3744 and wave 13 is 3755, +11 all named in the results doc's
§5.1). Branch was ghilios/synthetic-af-bank-followups-wave13.

Start by reading docs/synthetic-af-bank-followups-wave13-results.md and the F63, F62, F45, F57, F61, F15, F59,
F37, F41, F42 entries in docs/followups.md.

WHAT WAVE 13 SETTLED, SO YOU DO NOT RE-LITIGATE IT
 - THE SHIPPED DEFAULT STAYS AT MaxOutlierRejections = 1. RULE M13's pre-registered NO CHANGE outcome fired:
   D1 (out-of-sample vertex error on the 20 synthetic datasets) found 16 of 20 TIES, wins 3-0 to MOR=0, and a
   median displacement of 0.00128 step against a 0.10 floor. DO NOT RE-RUN THE DEFAULT DECISION.
 - AND THE DEFAULT IS THE *BOUND*, NOT MERELY A SAFE VALUE. At MaxOutlierRejections = 3 the Grubbs cascade takes
   caboose — a real run with R^2 = 0.99987 — to sigma_focus 1.4325 -> 18.185 (12.7x worse), reduced chi^2
   0.00427 -> 3.342. Raising the budget is actively harmful. F45(b) (a MAD floor tied to the points' own
   measured sigma) is now the ONLY one of F45's three sub-questions worth doing, because it is what would let
   the budget be raised safely.
 - F57 IS CLOSED BY INTERVENTION. One key in one settings file, profile pinned constant, recovers BOTH
   historical numbers: toml999 BaselineJ 0.983477 at MOR=0 (wave 11's) and 0.997840 at MOR=1 (wave 9's), gap
   0.01436366 against F57's stated 0.0144. The profile was never the cause; it was the carrier.
 - F61's ASYMMETRY WAS CHECKED AND DID NOT MATERIALISE. Wave 12's 7.6% on muggsie conflated two changed fit
   inputs (astrodet vs Default also differ in OutlierRejectionConfidence); the clean one-variable answer is
   7.2%. The AF fit is silent, the sensor fit prefers 0, the out-of-sample arbiter never prefers 1.
 - F15 IS FIXED. optimize --per-run no longer writes into the bank without --update-run-folder, the absent
   write PRINTS, and the backup keeps the OLDEST displaced landing.

THE FIRST GATE
Wave 11's eight values have now reproduced across FOUR binaries and two settings files, bit-identically:
  toml999 0.995784 | CWhiteFocus 0.996068 | uneven 0.996368 | muggsie 0.997195
  mccomiskey 0.976746 | D18 0.999882 | D19 0.999487 | D20 0.999738
Re-run D:\hf_w13\gate_w13.sh's shape on a FRESH exe from develop-after-#191, with BOTH
  --settings D:\hf_w11\pinned_settings_w11.json   (md5 a67ffc06)
  --profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382   (astrodet)
NAMED IN THE SCRIPT HEADER. ONE pass/fail clause; a partial reproduction is a failure. Score with
D:\hf_w12\score_w12.py — AND PASS IT A WSL PATH (/mnt/d/...), NOT A WINDOWS PATH. Wave 13 passed 'D:\hf_w13\gate'
and got 8 x UNEVALUATED; the scorer was right and the operator was wrong.
Free controls, all FIELDS: BuildId (must DIFFER from 62334f10), DetectorVersion, ProfileId, ConcurrencyCheck,
FitInputs. Wave 5's phi table is invalid on three axes. Do not quote it.

Then, in this order, and do not reorder for convenience:

1. F63 — EXTEND D5 TO THE POPULATION. THIS IS THE CLAUSE THAT FIRED AND IT IS BOUNDED TO 8 RUNS.
   Wave 13 measured that the optimizer's LANDING moves on 6 of 8 gate runs between MOR 0 and 1, while the SEED
   evaluation moves on only 1 of those 8 — because a search follows J and J shifts wherever the rejection fires
   ANYWHERE in the explored space. The recommended AF step moved by up to 17% (101 -> 118) and brightness
   sensitivity by a factor of two.
   THE ARM: the 39-run population at --max-evals 250 under both settings files. PRICE IT HONESTLY FIRST — F60
   measured ~3 h sequential / ~2 1/4 h fanned out PER ARM, so this is ~6 h sequential. Decide whether the
   question is worth that, and if you cut the population, SAY WHICH RUNS AND WHY.
   WHAT IS AND IS NOT COMPARABLE: the landed knob values, BrightnessSensitivity and RecommendedStep ARE
   comparable across arms. BestJ IS NOT — it is computed by the fit under test — and wave 13's scorer refuses to
   print a cross-arm BestJ delta so nobody quotes one. Keep that refusal.
   THE CONSEQUENCE THAT MAKES IT MATTER: every landing waves 5-12 published was produced at MOR=0, which is NOT
   the shipped default. If the population confirms 6-of-8, the register's landings are a non-default artifact
   and that has to be said in every doc that quotes one.

2. ITEM 2 IS STILL BLOCKED AND IT IS NOW ONE COMMAND FROM DONE — BUT ONLY FROM A CONNECTED SESSION.
   Wave 13 verified the deployed plugin DLL byte-for-byte against the freshly built one BEFORE launching, and
   that step PASSED — the csproj xcopy was not the blocker. `query session` reported the interactive session as
   Disc and the display as WinDisc, so there is no composited desktop: the MCP screen grab errors,
   CopyFromScreen returns a uniformly blank 1280x800, and PrintWindow returns NINA's title bar with a blank WPF
   client area. NINA burned 5 s of CPU in 16 minutes and wrote no log at all.
   SO: RUN `query session` FIRST. If it says Disc, item 2 is not attemptable this wave — say so in one line and
   move on; do NOT spend an hour rediscovering it. If it says Active, the procedure in the wave-13 results §3 is
   correct and the nine changes A1-A9 are named there. Price: ~30 minutes.
   AND NEVER RUN NINA DURING A PINNED ARM. Profile.Load holds the .profile open, so a --profile-id-pinned
   optimize/bank-verify/af-fit throws "No active NINA profile could be loaded" while NINA is up.

3. F45(b) — THE MAD FLOOR, WHICH IS NOW THE ONLY LIVE SUB-QUESTION.
   RejectionTest's scale is the MAD of the weighted residuals, so the better the fit the smaller the scale and
   the more aggressively the test fires — visible in wave 13's per-round tables as z reaching 22, 33 and 503
   after successive removals. A floor tied to each point's OWN measured sigma would stop a point being an
   outlier while it sits inside its own error bar.
   IT CHANGES EVERY AF FIT IN THE PRODUCT, so it must be measured on the bank before it is contemplated, and
   the instrument is now known: af-fit's budget table over the 20 synthetic datasets, scored against
   OptimalFocuserPosition, which costs FOUR MINUTES for the whole population.
   PRE-REGISTER WHAT WOULD MAKE YOU SHIP IT. The obvious rule: the floor must not make things worse at budget 1
   (where wave 13 measured the status quo exactly), and must materially reduce the budget-3 cascade (caboose is
   the poster child). If it cannot do both, it does not ship.

DEFERRED, WITH REASONS
 - F59's five knobs (MaxDistortion, StarCenterTolerance, SaturationThreshold, HotpixelThreshold, Sensitivity)
   are at CODE DEFAULTS in the pinned file since wave 5. Re-pinning MOVES THE COORDINATE SYSTEM RULE G13 has now
   reproduced FOUR times; price ~42 m for a new gate plus re-deriving every cross-wave comparison.
 - F57(d)'s landing-level wavelet bisect (D:\hf_w10\exe_v1wav, built wave 10, STILL UNUSED after three waves).
   WAVE 13 RECOMMENDS CLOSING IT and says why: the seed answer is exact and identical, wave 12's fan-out arm
   showed the search does not amplify an arbitrary process perturbation, and RULE G13 has now reproduced the
   same eight landings on a fourth binary. Close it or run it once (~42 m) and close it either way.
 - F61(b): re-running a historical tilt calibration under pinned fit inputs. Nothing depends on it.
 - F52(d), F46(b), F54, F50: nothing depends on them.

TRAPS WAVE 13 CREATED OR CARRIES
 - PASS THE SCORERS WSL PATHS. A Windows path silently yields "no such file" and the honest scorer reports
   UNEVALUATED — which is correct behaviour and looks exactly like a failed arm.
 - SILENT TRUNCATION EXITS 0, TWICE OVER. One bank path contains a SPACE (timmer/5 AutoFocus_.../attempt01), so
   a whitespace-split read truncates it; and TestApp.exe INHERITS a while-read loop's stdin and eats the rest of
   the list unless you redirect `< /dev/null`. Wave 13's I2 "completed" in 25 seconds having scored 1 of 19 runs
   with every row valid. ASSERT THE POPULATION SIZE — verify_affit_w13.sh takes an expected count for this.
 - THE FIRE RATE IS NOT ONE NUMBER. MaxOutlierRejections fires on 8/39 through optimize's pipeline, 7/39
   through af-fit's, and 0/19 through bank-verify's C0@nc4 — and NOT on the same runs. Each pipeline builds the
   HFR curve differently. Never quote a fire rate without the settings that produced it.
 - ConcurrencyCheck == "exclusive" ON A SINGLE LANDING IS NOT EVIDENCE THE MACHINE WAS QUIET. Read it across a
   whole arm.
 - THE PROFILE SET IS MACHINE STATE AND IT MOVES. D:\hf_w13\profiles_before_w13.txt is the wave-13 snapshot
   (Default moved to #2 since wave 12). Re-snapshot with D:\hf_w13\snapshot_profiles_w13.py before any fan-out.
 - DO NOT BUDGET FAN-OUT AT 4x. It is 1.33x (F60), and a pinned arm cannot fan out at all.
 - Newtonsoft writes NaN and Infinity as the STRINGS "NaN"/"Infinity". Use num().
 - "COULD NOT LOOK" NEEDS ITS OWN STATE AT EVERY LEVEL, INCLUDING THE SCORER'S — and the guard has to come
   BEFORE any field read. Wave 13 introduced that bug into its own comparator while improving a header, and
   caught it.
 - D17_cdk14_oiii5 finds ZERO STARS at 0.5s and 2s — a starvation extreme, useless as a measurement.
 - `strings <dll> | grep AtrousWaveletFast` still distinguishes v1 from v2 retroactively.
 - The console "Optimization complete" line is NOT reliably emitted. aggregate_summary.json is the instrument.
 - lumos exits rc=3 reproducibly; astrodet the DATASET is frameless (F14); Panos has a degenerate sigma fit.
 - Never rebuild an arm's directory mid-wave (F53(c)).

MEASUREMENT DISCIPLINE — every line below cost this project real time
 - SCORE THE CHANGE ON SOMETHING THE CHANGE CANNOT MOVE. sigma_focus, J and R^2 are all computed BY the fit
   under test. When what changed is WHICH POINTS ARE FITTED, they measure their own denominator: wave 13 saw
   sigma_focus improve by up to 88% while the distance to a known truth improved on ZERO of four datasets
   (F62). The synthetic bank's OptimalFocuserPosition is the out-of-sample arbiter and it is free.
 - A CONTROL THAT CANNOT FAIL IS NOT A CONTROL — AND "NOTHING MOVED" NEEDS ONE MORE THAN "SOMETHING MOVED".
   D2's clean 0-of-19 null was equally consistent with a knob that never reaches the code. An 11-minute arm at
   budget 3 converted it into a measured fact AND produced the wave's sharpest result by accident.
 - ASK WHERE THE KNOB REACHES, NOT ONLY WHETHER IT MATTERS. Four clauses said "inert"; the fifth asked whether
   the RECOMMENDATION moves and found 6 of 8.
 - INTERVENTION BEATS CORRELATION. Five profiles agreeing named a suspect; one integer in one file convicted it
   and recovered both historical numbers.
 - MEASURE THE PAYOFF YOU ARE BUYING, IN BOTH DIRECTIONS. Wave 13 over-priced af-fit by 16x (60 m estimated,
   3 m 46 s actual) because it priced the tool without its mechanism — af-fit detects each frame ONCE for all
   four budgets. Write down both halves.
 - THE CHEAPEST INSTRUMENT IS THE ONE ALREADY PRINTED AND IGNORED. af-fit's budget table had existed since
   wave 6 and been read for exactly one run.
 - SAY WHAT YOU DID NOT RUN, and price it.
 - PIN --settings AND --profile-id ON EVERY ARM (except a deliberate fan-out, which must argue for itself).
 - On CI: verify the test COUNT, not the tick (F37). An ABSENT check is more dangerous than a red one.
   develop's baseline after wave 13 is 3755.

Write the spec in docs/<topic>-design.md and the plan in plans/<topic>-plan.md FIRST, and COMMIT THEM BEFORE ANY
MEASUREMENT so the pre-registration is in git history before the data. FLAG new findings in docs/followups.md.
Finish with a PR; never push develop.

Build: dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w14\exe'
Tests: dotnet.exe test "$(wslpath -w <abs>/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
Wave-13 artifacts: D:\hf_w13\ (exe, gate\, affit_syn\, affit_real\, bv_mor0\, bv_mor1\, bv_mor3\, pop_mor0\,
  pop_mor1\, land_mor1\, bank_settings_snapshot\ [42 pre-wave landings], item2\,
  gate_w13.sh, affit_w13.sh, bankverify_w13.sh, pop_w13.sh, land_w13.sh, chain_{a,b,c}_w13.sh,
  score_affit_w13.py [budget-table scorer + D1, self-testing], compare_bv_w13.py [D0/D2/D3, self-testing],
  score_pop_w13.py [D4/D5], verify_affit_w13.sh [the pinned-settings control + POPULATION COUNT],
  snapshot_profiles_w13.py, pinned_settings_w13_mor1.json [89e14ef8], pinned_settings_w13_mor3.json [3350cb45],
  gate_score.txt, affit_syn_score.txt, affit_real_score.txt, bv_compare.txt, bv_compare_mor3.txt,
  pop_score.txt, land_score.txt, suite.log)
Wave-11: D:\hf_w11\ (pinned_settings_w11.json [a67ffc06])
Wave-10: D:\hf_w10\ (pop\ = the 39-run population landings; exe_v1wav — UNUSED, recommended for closure)
Wave-7 that must survive: D:\hf_w7\armE (the exposure ladder), f18arms, golden\
Banks: D:\SyntheticAutofocusBank (20 datasets, each with renderRequest.OptimalFocuserPosition = TRUTH),
  D:\Autofocus Bank (19 runs)
