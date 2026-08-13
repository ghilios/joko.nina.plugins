# The synthetic AF bank -- results table

Rows: 20 of 20. Precision/recall scored by `golden eval --params optimized --opt-results <w18>/attempt01`, match=center, match-radius=12, pixel-scale=header, on B15 (BuildId d79dae73); all 20 verified against each log's own snapshot-source line.
Optimize columns from wave 18's pinned seedA0 arm. Binning recommendation from wave 25's AFTER arm (B15, S1) -- a DIFFERENT instrument, labelled. Truth from each dataset's synthetic_meta.json.

**Scoring mode: `golden+truth-protected`.** B15 carries `TruthProtection` (shipped `aaf26e8`, 2026-08-03), so a
detection on a real rendered star the golden policy dropped is excluded from the false-positive count. **The
report itself does not say so** -- `golden eval` emits no scoring mode, unlike `bank-verify`, which is the defect
wave 27 fixes and the direct cause of the withdrawn banner below. For runs produced before 2026-08-03 the mode
is `golden` (unprotected) and the two are not comparable.

**Reproducibility:** B15 regenerates this entire population byte for byte -- `RULE R27` = `R-IDENTICAL`, 20 cells,
420 artifacts, 0 differences (`/mnt/d/hf_w27/repro_all_score.txt`).


## CORRECTION, 2026-08-13 (wave 27): the previous banner here was WRONG, and it is withdrawn

**This section previously read "READ THE PRECISION COLUMN AS A LOWER BOUND" and claimed the reference
under-lists real stars, that `D08`'s true precision was nearer `0.997` than `0.966`, and that every `1.000` was
a floor. All of that is false, and it is corrected in place rather than deleted so the error is visible.**

The precision column **is already truth-corrected**. `TruthProtection` shipped on **2026-08-03** (`aaf26e8`) and
is wired into **both** `GoldenEvalRunner.cs:301-307` and `BankVerifyRunner.cs:464-466`: a detection landing on a
real rendered star the golden policy dropped (tier `omitted` or `merged-into`) is excluded from the
false-positive count, at exactly the match radius matching itself uses. B15 -- the binary that produced every
row below -- carries it.

**What the withdrawn banner actually measured was a pre-repair quantity.** Wave 26's re-score
(`/mnt/d/hf_w26/pd08/F31_truth_rescore.txt`) re-derived false positives from the golden's `stars` list **alone**,
consulting neither the golden's own `unresolved` boxes nor `TruthProtection`. Of its 150, **139 had already been
excluded** by the run that produced this table -- 50 by `unresolved`, 89 by truth protection -- and `golden eval`
reported **`FP = 11`**. Two different fields, both called "false positive" ([F68](followups.md) part 1).

Verified on `D08` at best focus, from the artifacts: **119 detections = 81 in golden `stars` boxes (TP) + 15 in
`unresolved` boxes + 23 truth-protected, `FP = 0`** -- exactly what the log prints.

**So `D08`'s `0.966` is `308 TP / (308 + 11 FP)` with protection applied, and the nineteen `1.000` rows carry
`FP = 0` after it. `1.000` is a CEILING here, not a floor.**

**The genuine junk detections number 11, not 13** (two of the 13 sit inside a golden `unresolved` box and were
already excluded), **and they are localised**: `golden eval`'s own per-frame FP column for `D08` reads 3 on
`13672`, 8 on `14328`, and **zero on all seven interior frames**. That is a detector-at-its-limit observation and
it is the only measured false-positive behaviour on the bank.

**Recall is untouched by truth protection by explicit design** (*"a protected star is not promoted to a required
find"*) and remains the weak axis -- and it is tier-dependent, which is why `recall@high` and `recall@all` are
both printed and a bare "recall" never is.

## SEVEN ROWS ARE SCORED AT A DETECTION BINNING THE OPTIMIZER DOES NOT USE -- `RULE D27`, and it MOVED

`golden eval` prints, on 7 of the 20 cells below, *"this run's derived detection binning is 2 ... but it is being
scored at 1. Pass `--detection-binning` to score it at its own factor; `optimize` uses the derived one by
default."* Those 7 are exactly the rows whose `det binning rec` column reads `rec 2 / applied 2 vs 2 MATCH`, so
their precision/recall and their binning columns rest on contradictory assumptions.

Re-scored at the optimizer's own factor on the same binary (B15, no build, `/mnt/d/hf_w27/table18_b2/`),
**all 6 blind datasets moved** at the 0.01 bar this table prints to (`RULE D27` = **`D-MOVED`**):

| dataset | recall@high f1 -> f2 | recall@all f1 -> f2 | prec f1 -> f2 |
|---|---|---|---|
| D08_c11_2800mm *(not blind)* | 1.000 -> 1.000 | 0.978 -> **1.000** | 0.966 -> **0.997** |
| D09_c14_3800mm | 0.911 -> **1.000** | 0.949 -> **0.991** | 1.000 -> 1.000 |
| D10_rc16_3250mm_sparse | 1.000 -> 1.000 | 0.965 -> **1.000** | 1.000 -> 1.000 |
| D12_c14_585_afbin2 | 0.720 -> **0.776** | 0.705 -> **0.729** | 1.000 -> 1.000 |
| **D14_cdk14_2563mm_e47** | 0.862 -> **0.995** | 0.641 -> **0.937** | 1.000 -> 1.000 |
| D15_cdk20_3454mm_e47 | 0.920 -> 0.885 | 0.961 -> **0.949** | 1.000 -> 1.000 |
| D17_cdk14_oiii5 | 1.000 -> 1.000 | 0.875 -> **1.000** | 1.000 -> 1.000 |

**The factor-1 column below UNDERSTATES recall on those seven rows** -- `D14` by **30 points**. `D15` is the only
one that fell, by 0.012. The factor-1 values are kept below because they are what was published and what the
optimize columns were produced alongside; read the seven against this table.

Validity: `D27-V1` (each factor-2 log prints `; detectionBinning 2` and no longer prints the `scored at 1` NOTE)
**7 of 7**, with its FAIL end run on a real factor-1 cell; `D27-V2` (coordinate-collapse guard,
`recall@all >= 0.25`) **7 of 7**, min `0.729`. The invocation differs from the one that produced the table below
by `--detection-binning 2` and a `--profile-id` pin **demonstrated inert** (`D09` re-run at factor 1 with the pin:
20 of 20 artifacts byte-identical).

*Coincidence worth naming so nobody reads it as vindication:* the withdrawn banner guessed `D08`'s precision at
`0.997`, and `0.997` is what factor-2 scoring gives. The mechanisms are unrelated -- the banner's own arithmetic
yields `0.972`, and factor 2 helps because the wing-frame junk largely disappears at half resolution.

| dataset | prec | recall@high | recall@all | opt time (s) | score BestJ (from BaselineJ) | sigma Best (from Baseline) | exposure rec vs optimal | det binning rec vs optimal | BrightnessSensitivity (effective gate) |
|---|---|---|---|---|---|---|---|---|---|
| D01_ultrawide_40mm | 1.000 | 0.183 | 0.123 | 401.6 | 0.994825 (0.000000) | 0.1514 (0.6145) | 0.50 vs 0.50 IN band | rec 1 / applied 1 vs 1 MATCH | 36.333 (36.33) |
| D02_rich_135mm | 1.000 | 0.446 | 0.377 | 254.6 | 0.996558 (0.000000) | 0.0991 (0.2321) | 0.50 vs 0.50 IN band | rec 1 / applied 1 vs 1 MATCH | 33.333 (33.33) |
| D03_redcat_250mm | 1.000 | 0.365 | 0.238 | 83.8 | 0.995494 (0.712458) | 0.2698 (1.2397) | 0.50 vs 0.50 IN band | rec 1 / applied 1 vs 1 MATCH | 31.583 (31.58) |
| D04_esprit_550mm | 1.000 | 0.855 | 0.625 | 429.9 | 0.999863 (0.997120) | 0.0213 (0.2429) | 0.50 vs 0.50 IN band | rec 1 / applied 1 vs 1 MATCH | 19.667 (19.67) |
| D05_tec140_1000mm | 1.000 | 0.989 | 0.914 | 254.0 | 0.999693 (0.999293) | 0.0769 (0.2020) | 0.50 vs 0.50 IN band | n/m vs 1 | 8.000 (8.00) |
| D06_sparse_1000mm | 1.000 | 0.964 | 0.854 | 61.5 | 0.995505 (0.994716) | 0.0579 (0.2627) | 1.00 vs 1.00 IN band | rec 1 / applied 1 vs 1 MATCH | 10.000 (10.00) |
| D07_rc10_2000mm | 1.000 | 0.973 | 0.861 | 36.7 | 0.998530 (0.998281) | 0.0682 (0.1265) | 0.50 vs 0.50 IN band | rec 1 / applied 1 vs 1 MATCH | 8.000 (8.00) |
| D08_c11_2800mm | 0.966 | 1.000 | 0.978 | 35.4 | 0.996396 (0.995175) | 0.7677 (0.6366) | 1.50 vs 1.50 IN band | rec 2 / applied 2 vs 2 MATCH | 0.000 (0.56) AT FLOOR |
| D09_c14_3800mm | 1.000 | 0.911 | 0.949 | 20.9 | 0.996136 (0.993888) | 0.6935 (1.0376) | 1.50 vs 1.50 IN band | rec 2 / applied 2 vs 2 MATCH | 2.500 (2.50) |
| D10_rc16_3250mm_sparse | 1.000 | 1.000 | 0.965 | 41.6 | 0.993449 (0.957201) | 0.5198 (1.2739) | 30.00 vs 30.00 IN band | rec 2 / applied 2 vs 2 MATCH | 0.000 (2.39) AT FLOOR |
| D11_rc10_585_afbin2 | 1.000 | 0.850 | 0.892 | 31.5 | 0.996104 (0.985987) | 0.1861 (0.4803) | 2.50 vs 2.50 IN band | rec 1 / applied 1 vs 1 MATCH | 0.000 (2.36) AT FLOOR |
| D12_c14_585_afbin2 | 1.000 | 0.720 | 0.705 | 11.6 | 0.995742 (0.985663) | 0.8064 (1.7158) | 9.50 vs 9.50 IN band | rec 2 / applied 2 vs 2 MATCH | 0.000 (2.13) AT FLOOR |
| D13_apo200_1800mm | 1.000 | 1.000 | 0.757 | 132.4 | 0.996200 (0.993571) | 1.1673 (2.5231) | 0.50 vs 0.50 IN band | rec 1 / applied 1 vs 1 MATCH | 12.000 (12.00) |
| D14_cdk14_2563mm_e47 | 1.000 | 0.862 | 0.641 | 215.9 | 0.998156 (0.997500) | 0.2856 (0.5742) | 0.50 vs 0.50 IN band | rec 2 / applied 2 vs 2 MATCH | 10.000 (10.00) |
| D15_cdk20_3454mm_e47 | 1.000 | 0.920 | 0.961 | 88.9 | 0.995759 (0.994474) | 0.3403 (0.4806) | 6.00 vs 6.00 IN band | rec 2 / applied 2 vs 2 MATCH | 9.500 (9.50) |
| D16_esprit550_ha3 | 1.000 | 0.908 | 0.546 | 81.5 | 0.995664 (0.979173) | 0.1712 (0.6378) | 2.00 vs 2.00 IN band | rec 1 / applied 1 vs 1 MATCH | 2.500 (2.50) |
| D17_cdk14_oiii5 | 1.000 | 1.000 | 0.875 | 18.3 | 0.995187 (0.993062) | 0.3825 (0.7303) | 30.00 vs 30.00 IN band | rec 2 / applied 2 vs 2 MATCH | 7.500 (7.50) |
| D18_m24_deep_shed | 1.000 | 0.936 | 0.353 | 364.3 | 0.999882 (0.997405) | 0.0167 (0.2275) | 0.50 vs 0.50 IN band | rec 1 / applied 1 vs 1 MATCH | 14.667 (14.67) |
| D19_cygnus_deep_shed | 1.000 | 0.974 | 0.446 | 332.1 | 0.999487 (0.999187) | 0.0921 (0.2292) | 0.50 vs 0.50 IN band | n/m vs 1 | 16.542 (16.54) |
| D20_m24_bright_control | 1.000 | 0.937 | 0.866 | 113.3 | 0.999738 (0.999454) | 0.0082 (0.0675) | 0.50 vs 0.50 IN band | rec 1 / applied 1 vs 1 MATCH | 16.667 (16.67) |

## COULD-NOT-LOOK -- NAMED, and outside every column above

- D01_ultrawide_40mm / binning: final round had hasMeasurement=false; the reported value is the last measured round
- D05_tec140_1000mm / binning: no wave-25 S1 report (cell NOT-RUN or timed out)
- D08_c11_2800mm / binning: recommendation OSCILLATED across rounds: [1, 2, 2]
- D19_cygnus_deep_shed / binning: no wave-25 S1 report (cell NOT-RUN or timed out)
