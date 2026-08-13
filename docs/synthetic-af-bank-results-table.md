# The synthetic AF bank -- results table

Rows: 20 of 20. Precision/recall scored by `golden eval --params optimized --opt-results <w18>/attempt01`, match=center, match-radius=12, pixel-scale=header, on B15 (BuildId d79dae73); all 20 verified against each log's own snapshot-source line.
Optimize columns from wave 18's pinned seedA0 arm. Binning recommendation from wave 25's AFTER arm (B15, S1) -- a DIFFERENT instrument, labelled. Truth from each dataset's synthetic_meta.json.

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
