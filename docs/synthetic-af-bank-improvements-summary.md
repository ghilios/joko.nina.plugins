# What the synthetic AF bank followups actually bought

Only what shipped or measurably improved the product or the measurement. Ruled-out, refuted and fenced
items are omitted. Entry ids (`F<N>`) are checkable in `docs/followups.md`.

## 1. Product behaviour that changed for users

- **`StepSizeRecommender` gained its missing lower bound** (`F81`, wave 25): the `1.5x` ceiling had no
  mirror and the `half-width-unresolved` exit skipped it. Step **widened on 3 of 8 real gate landings,
  narrowed on 0** (`mccomiskey` 31->34); `D02` 2->5 (truth 6) and `D03` 4->12 (truth 16) now converge;
  do-no-harm 18 of 18, 0 regressions. **Blind efficacy unmeasured** - 0 of 13 blind cells engaged it.
- **An unusable step fit now says so** (wave 24 P1): `Degenerate()` reports which exit it took and measures
  `SampledHfrRange` where it had been `NaN`. The wizard's bare "recommended step: 21" now reads
  "21 (NOT measured: <reason>, so your current step size was kept)". Inert on the shipping path, 8 of 8.
- **The optimizer wizard's `Accept` is reachable** (`F76`). Measured before perturbation: the window sat
  **444 px below the work area**. It now fits `min(content, work area)` both ways, re-fits per step, and
  holds still during a run. Eight rounds, five causes, each named by a log line, confirmed in the field.
- **Star detection is 2.5x / 9.7x / 40x faster at `StructureLayers` 4 / 6 / 8** (`F56`, PR #187,
  `StarDetectorVersion` 1->2): the a-trous residual convolved a dense kernel that is 99.5 % zeros.
- **The objective stopped collapsing to `J = 0` below `MinHFR`** (`F20` via `F35`): `D01`/`D02` go from
  `FinalJ` exactly 0 to real landings, with 14/17 synthetic and 19/19 real landings bit-identical to
  control. The chart note now names the measured in-focus HFR, its gate, and the binning relating them.
- **The wizard no longer refuses to start unless the un-optimized defaults already build a usable curve**
  (`F43`, Live path; the replay half is decided but unimplemented).
- **The synthetic camera renders the whole field** (`F44`): the catalog query clamped to one cell width and
  sampled four corners, so wide fields were starless outside a central patch. `D01` was the only bank row
  affected.
- **Long optimizations report honest progress** (`F52`(a)(b), then `a60c189`/`041d66e`). Was one log line in
  over two hours; now a line every 10 evaluations plus the rate, the knob that made it expensive, and what
  Cancel costs. The later fix classifies each evaluation **at its cause** - the old blended-mean bound was
  routinely exceeded because the search opens on cache hits.
- **The AF panel shows the star-count range of the points the curve was fitted on** (`e1227a9`), net of
  Grubbs rejections and window exclusions. Two sweeps with the same R2 are no longer indistinguishable.
- **Replaying a saved run with Save on writes per-region reports** (`de1882a`); previously only a *failed*
  replay wrote them.
- **`optimize` prints the post-mutation detector bundle** (`F69`(b)(c)): 8 of 8 gate logs, 55/55 fields; its
  delta from `optimize/baseline` is exactly `{DetectionBinning, PixelScale}`, 6 of 6. `optimized_settings.json`
  also gained a provenance block (`F30`).
- **`MaxOutlierRejections` defaults to 0** (owner, wave 14). Consequential - `F63` measured the *landing*
  moving on **6 of 8** gate runs under this knob alone (`RecommendedStepSize` 101->118) - but **never
  measured to be better**: `BestJ` is computed by the fit under test, so no direction was established.
- Smaller, unmeasured: the manual's `NoiseReductionRadius` default corrected at both sites (`F70`,
  `2678ecd`), and the inspector stopped writing redundant annotated registration TIFFs (`8a33343`).

## 2. Defects fixed in the tooling the whole series depends on

- **The plugin deploy failed silently while NINA ran** (`F77`): the PostBuild batch exits with its *last*
  command's code, so a failed plugin `xcopy` was swallowed. It cost two ~20-minute owner field tests on a
  stale binary, one of which *looked* like a passing confirmation. Now `HF0001`/`HF0002`, both branches
  measured on real artifacts (FAIL: fires, `3 Warning(s)`, build still succeeds; PASS: silent, hashes match).
- **One non-ASCII byte made `grep` report zero matches** (`F79`): a CP437 `sigma` made GNU grep class a whole
  log as binary, failing **closed** as "the feature is absent" - latent since wave 11 in 8 of 8 gate logs.
  Fixed across **20 TestApp files** with a guard test; the next gate measured **0** non-ASCII bytes in 8 of 8.
  It also exposed a second defect it had been masking.
- **Instruments derived by `sed` keep their predecessor's prose** (`F80`): eight drifts across five
  instruments in one wave, one load-bearing, **every one passing its own `--self-test`**. Wave 25's blocking
  pre-flight caught **21 more** before any measurement, including a `sed` ordering bug that left 10 of 15
  sibling references pointing at a nonexistent file. Two gaps in the checker itself were found and closed.
- **A driver and its scorer can no longer disagree about where an artifact is** (`F74`); three independent
  path assumptions had cost a pre-registered rule its verdict. Fixed by construction.
- **Every build directory silently bootstrapped its own detector settings** (`F42`), so two arms built
  minutes apart could run different detectors. Now a per-user path, a loud bootstrap, and a
  `UseAdvanced=False` warning naming the overridden knobs.
- **`optimize --per-run` stopped overwriting each run's stored settings** (`F15`, open thirteen waves).
  Since: 42 of 42 bank landings byte-identical for twelve consecutive waves.
- **The harness's fit no longer reads the ambient NINA profile** (`F61`): scored quantities that moved when
  the profile was switched went **15 of 24 -> 0 of 24**. What had been moving was the sensor model.
- **The synthetic bank's precision metric was wrong and is repaired** (`F31`): of 280 detections scored as
  false positives on `D09`, **269 (96.1 %) were real rendered stars** the golden omitted. Null-controlled
  and re-baselined.
- **The golden-set QA gate inverted on defocused runs** (`F16`, PR #163): `donut_k` 6.0 -> 8.0 cut
  candidates/frame from 5,000-7,300 to 649-919 and raised QA coverage from ~14 % to 78-100 % at unchanged cost.
- **The stall message stopped naming a cause it never checked** (wave 25 P5, closing `F34`): 25 cells,
  **exactly 1** contradiction before (`D01`), **0** after.

- **The settings exporter stopped silently losing knobs** (`F59`, wave 11). Poison values threw on
  validating setters and the export abandoned the remaining fields; it now tries each in turn and verifies by
  read-back. **Recorded here against the controller's own exclusion list, which was wrong:** the entry's
  rejected-on-the-merits half is the separate proposal to add the five recovered knobs to the pinned settings
  file (four are preset-owned and inert; the fifth, `SaturationThreshold`, would bind and owes a fresh 42 m
  baseline). The exporter fix itself shipped and works.

## 3. Measurement capability that did not exist before

- **An eight-value gate reproduced on fifteen binaries** (waves 11-25), bit-identical to sixteen digits every
  time. It is what makes a cross-wave difference attributable to the code, not the coordinate system.
- **Four fingerprint preservation classes**, all byte-identical with 0 could-not-look: 42/42 bank landings,
  59/59 aux files, 48/48 prior-wave arm landings, 38/38 prior-wave reports. Wave 25's gate **refused to
  start twice** because two BEFORE fingerprints were missing; both refusals were correct.
- **`synth-validate` scores step-size accuracy against the bank's rendered truth**, 20 datasets x 2
  scenarios. Best result so far: `stepBehavioral == stepTheory` exactly on **34 of 36** cells, placing every
  failure in the recommender's *walk*, not its arithmetic.
- **A repaired `golden eval` precision/recall path** (`F31`), so a bank precision number means what it says.
  The owner's per-dataset table has since been PRODUCED from it: 20 of 20 datasets scored at `--params
  optimized --opt-results <w18>/attempt01`, match=center, radius=12, each verified against the run's own
  snapshot-source line. Precision is 1.000 on 19 of 20; `recall@high` spans 0.183 to 1.000.
- **Provenance read from the binary, not the driver**: one pinned `--settings` md5 and `--profile-id` on
  every arm and gate, with profile, `DetectorVersion`, concurrency and `FitInputs` read back as **fields**
  (`F57`/`F58`); and suite size read by COUNT, never the tick (`F37`) - now 3973 passed, 0 failed.
- **The A1-A9 rendered-pixel UI check completed** after nine waves blocked on a crash that does not
  reproduce (`F72` item C). It is what found `F76`.

## 4. Things now known that were previously assumed

- **`F67`'s cause is `DetectionBinning`** - neither candidate the prior wave had narrowed to. The
  intervention measured **63 of 63 = 1.0000** against a status quo of **0.000 of 63**. Closed.
- **`F26`'s livelock is in the harness, not the product** (`TestApp/SynthValidateRunner.cs`, which NINA never
  loads). Wave 24's revisit guard bridges the reproducing cell (`roundsUsed` 4->3, step 21->62, converged
  false->true, meeting `F26`'s own bar), but **0 of 7 blind cells** ever revisited a factor, so the livelock
  is rarer than the entry implied.
- **`D01`'s stall is not degeneracy**: `halfWidth` 6.0877 at R2 ~= 1.0 yet 4.5x too narrow - a hyperbola fit
  to a 1.46x slice interpolates perfectly and extrapolates inward, which no R2-keyed gate can catch. `F34`'s
  grouping of `D01`/`D02`/`D03` as three degenerate stalls is corrected; the residual is `F82`.
- **Sensitivity pins are search outcomes, not seed artifacts, and are often inert**: 0 of 24 pinned
  axis-instances were never-moved and 22 were *driven* to the bound, while in 9 of 40 landings the pin
  changes nothing because `EffectiveSensitivityGate` already gates harder.
- **One integer, not the profile, moved `BaselineJ`** (`F57`): with the profile held constant,
  `MaxOutlierRejections` 0 vs 1 recovers both historical values (0.98347680 vs 0.99784046).
- **Fan-out is safe and barely worth doing** (`F60`): degree 4 over eight runs is 43 m 10 s -> 32 m 28 s, a
  **1.33x** speedup bought with 33 extra minutes of busy time. A 39-run pass costs ~2 1/4 h, not ~45 m.
- **Most bank exposures are a clamp, not a derivation** (`F19`(b)): 11 of 20 datasets report the 0.5 s floor
  (`D02` derives 0.001 s, a 500x gap), where the entry had claimed 8 of 17.
- **sigma(focus) is anti-informative when an outlier rejection changed it** (`F62`): up to 88 % better while
  distance to a known truth improves on none.
- **`lumos` is a hard-floor failure, measured** (wave 24, after ten waves as folklore): `rc=3` reproduces,
  `bestJ = currentJ = 0`, min observed 0 stars.
