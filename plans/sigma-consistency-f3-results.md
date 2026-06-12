# F3 Experiment Results: τ-Policy Decision Evidence

## What was measured

HFR bias (mean − ground truth) and seed noise (std across 10 seeds, SeedBase=9000) were measured for each
(τ policy, multiplier) cell using `MeasureStar` on three synthetic shapes with known analytic HFR ground truth.
Noise level σ_n=0.02 was added via `SyntheticDefocusedStarImage.AddGaussianNoise` and passed directly to
`MeasureStar` as `noiseSigma` (so the F4 σ-understating issue is excluded — the true σ is used throughout).
Image size is 81×81, center at (40, 40). Shapes: bright Gaussian (σ=5, peak=1.0 = 50σ_n), faint Gaussian
(σ=5, peak=0.16 = 8σ_n), faint donut/annulus (inner=8, outer=14, peak=0.12 = 6σ_n). Policies tested:
`SubtractTau` (legacy soft-threshold) at 0.4σ, and `GateOnly` (inclusion gate, full flux kept) at 0.4σ,
1.0σ, and 2.0σ. See `plans/sigma-consistency-design.md` §3 for the design rationale and policy definitions.

---

## Tables

```
--- bright gaussian (peak 50σn) (truth HFR=6.267, noise σ=0.02) ---
cell                   bias        std    n
subtract@0.4σ        2.6185     0.0895   10
gate@0.4σ            3.9990     0.0933   10
gate@1.0σ            2.7488     0.1135   10
gate@2.0σ            0.4598     0.0971   10

--- faint donut (peak 6σn) (truth HFR=11.273, noise σ=0.02) ---
cell                   bias        std    n
subtract@0.4σ        5.4802     0.1466   10
gate@0.4σ            7.0538     0.1288   10
gate@1.0σ            5.3751     0.1801   10
gate@2.0σ            1.5597     0.2127   10

--- faint gaussian (peak 8σn) (truth HFR=6.267, noise σ=0.02) ---
cell                   bias        std    n
subtract@0.4σ       11.0239     0.2438   10
gate@0.4σ           13.0222     0.1919   10
gate@1.0σ           10.7565     0.2937   10
gate@2.0σ            3.5380     0.4824   10
```

---

## Reading the table

**Bias**: mean HFR − analytic ground truth. Closer to 0 is more accurate. Positive bias means MeasureStar
overestimates HFR relative to the continuous-field ground truth (note: the ground truth is the ideal
flux-weighted radius for an infinite aperture; some positive offset is expected from discretization and the
finite aperture, but large values indicate the τ policy is materially distorting the measurement).

**Std**: standard deviation of HFR across the 10 seeds. This measures sensitivity to noise realizations —
a policy with materially higher std than `subtract@0.4σ` is paying a noise-admission penalty.

**n**: number of seeds (out of 10) where `MeasureStar` returned a valid measurement. All cells returned
n=10 (gate policies did not drop any measurements at these signal levels).

**Shape priority**: the faint donut row is the AF-critical regime — real defocused stars at and beyond
the AF sweep's outer focus positions are faint annuli. The bias in that row determines whether the focuser
sees an accurate HFR curve shape at the extreme positions. The faint Gaussian row represents in-focus or
lightly defocused faint stars; the bright Gaussian is the low-noise anchor.

**Key observation**: `gate@2.0σ` shows the lowest bias across all three shapes by a wide margin. Its std
is slightly higher than `subtract@0.4σ` (0.48 vs 0.24 on faint Gaussian; 0.21 vs 0.15 on faint donut) —
that is the noise-admission penalty from not subtracting the threshold. On the bright Gaussian `gate@2.0σ`
bias (0.46) is ~6× lower than `subtract@0.4σ` (2.62) and ~9× lower than `gate@0.4σ` (4.00). On the
faint donut, `gate@2.0σ` bias (1.56) vs `subtract@0.4σ` (5.48) is a 3.5× improvement.
