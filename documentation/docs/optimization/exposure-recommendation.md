# Exposure Recommendation

When the Star Detection Optimizer's search lands **Brightness Sensitivity** at the bottom of its range,
the wizard's Summary page shows a **Star signal** block above the recommended step size. A gate that low
means the detector accepted almost anything above the noise to find stars at all: the frames are
signal-starved, and the focus curve fitted from them rests on low-confidence detections. The block
responds with a longer exposure rather than a lower gate, and derives the number from the same
accepted-star measurements the run already made.

The logic lives in `ExposureRecommender.cs`; its Summary copy lives in `StarSignalCopy.cs`.

## When it appears

The block shows up purely from the landed gate value: **Brightness Sensitivity at or below 1.0** for the
run you are viewing, the same value the Summary page's **Changed parameters** table below it prints. It
does not look at star counts to decide whether to appear.

The threshold is 1.0. Sensitivity's search axis spans `[0, 50]` with an initial step of 1.0, and the
pattern search that lands it near the floor refines that step by repeated halving down to one eighth of
it, so a genuinely floored search typically settles at 0.125, 0.25, or 0.5, well short of bit-exact 0;
testing for exactly 0 would miss most real cases. 1.0 is one full search step off the floor, and also
exactly a tenth of the default gate this recommendation targets. 2.0 was considered and rejected: a
legitimately hand-tuned gate of 2 on a rich, well-exposed field is plausible on its own and should not
trigger an exposure nag.

See [Brightness Sensitivity](../settings/acceptance-gates.md#brightness-sensitivity) for what the gate
measures and why smaller values admit fainter stars.

## How the recommended exposure is derived

The recommender reduces every frame to one number: the **N-th brightest** surviving star's
sensitivity-gate SNR. N is the same star-count target the objective's own median-count score uses
(\(N_{\text{target}}\), 20 by default; 60 when the run optimized for aberration inspection). Frames from
the far-from-focus recovery wing are skipped; they are deliberately sparse and do not represent ordinary
gate strength. A frame with fewer than N surviving stars falls back to its faintest survivor: a bounded
under-estimate, since the true N-th-brightest star, had the frame had that many, could only have been
brighter. That frame is counted separately as a **short frame**.

\(S_{\text{now}}\) is the **median** of that per-frame number across the run's usable frames. The
recommended exposure then follows a sky-limited scaling law:

\[
t_{\text{new}} = t_{\text{old}} \left(\frac{10}{S_{\text{now}}}\right)^{2}
\]

where 10 is the shipped default Brightness Sensitivity gate: the faintest star that default,
out-of-the-box gate would still admit.

Below `MinFramesForRecommendation` = 3 usable frames, with no per-star SNR data recorded, or with a
non-positive current exposure to scale from, there is not enough to trust a derived number: the block
still names the floored gate, but the recommended-exposure row and its number are hidden. Older saved
runs that recorded no exposure at all fall back to your profile's auto-focus exposure for this
calculation, and the block says so when that happens.

### Why the N-th brightest star, not the median star

Asking the *median* accepted star to clear the gate over-recommends: on a rich 200-star frame that would
demand 100 stars clear the default gate, far more than the objective actually needs to build a good
curve. The N-th-brightest star asks the real question this recommendation exists to answer: what
exposure would put \(N_{\text{target}}\) stars per frame above a healthy gate. That is also why the
statistic is pinned to the objective's own star-count knee, not a literal number baked into the
recommender: retuning the objective's target moves this recommendation with it, so the two stay in sync.

## The √t assumption

The scaling law assumes SNR grows with the square root of exposure time. That holds when sky background
dominates the noise: background-limited noise \(\sigma\) grows as \(\sqrt{t}\), so SNR (signal over
\(\sigma\)) grows the same way, and reaching an SNR ratio \(r\) costs a time factor of \(r^{2}\).

That is the right assumption for these frames. An autofocus sweep is deliberately defocused, so a star's
flux is spread thin across many pixels, and sky background dominates the noise even for a fairly bright
star. It is also the conservative branch: for \(r > 1\), \(r^{2}\) always asks for more exposure than a
plain \(r\) would. A **read-noise-limited** setup, where background noise is negligible next to a fixed
per-pixel read floor, needs less exposure than the figure this block shows: SNR there scales closer to
\(t\) itself than to \(\sqrt{t}\).

## The two caps

Two independent limits bound how far a single recommendation can push the exposure:

- **At most 4× the current exposure in one step.** Under \(\text{SNR} \propto \sqrt{t}\), a 4× time
  factor is exactly a 2× SNR jump, the largest single-step change this recommendation trusts. A
  signal-starved run is usually starved by more than that, so the cap leaves the number short and lets a
  second optimizer pass, now working from frames with real signal, refine the answer further.
- **An absolute ceiling of 30 seconds per frame.** The wizard's default sweep already samples 9 points
  (±4 offset steps); at 30 s each that is 4.5 minutes of pure integration before counting focuser moves
  and frame downloads, and a live run's recovery wing widens the sweep further still. Past that, a sweep
  stops behaving like an autofocus run and risks hitting NINA's own autofocus timeout, the opposite of
  what this recommendation is for.

Whichever cap binds, the figure is rounded **up** to a practical exposure-time ladder (0.5 s steps below
10 s, 1 s steps from 10 to 30 s, 5 s steps above), and only when the capped value actually exceeds the
current exposure; an unchanged value is never rounded. The caps describe intent, and that last rounding
step always rounds up, so the number you see can land one ladder step past whichever cap applied.

!!! example
    A 3 s run measuring `S/N 4.1` computes a raw `(10 / 4.1)²` factor of about 5.9, or roughly 17.8 s.
    The run-relative cap (3 s × 4 = 12 s) binds before the absolute one and rounds up to the row:

    ```
    Recommended exposure   3 s → 12 s (measured star S/N 4.1; target 10)
    ```

    Its tooltip carries the arithmetic: "Sky-limited scaling: 3 s × (10 / 4.1)² = 18 s per frame,
    roughly 3 minutes per auto-focus run. Capped at 12 s: one run may not raise the exposure by more than
    4x, so a second run refines it."

## What it never does

The recommender never proposes a **shorter** exposure than the one the run already used. If your
brightest stars already clear the default gate (\(S_{\text{now}} \ge 10\)), the block says exactly that:
exposure is not what is limiting this run, and the floored gate is instead admitting a long tail of far
fainter candidates below your genuinely bright stars. When even one frame found fewer stars than the
objective's star-count target, the block adds the count: "N of M frames found fewer stars than the
star-count target, so the low gate is scraping for count in a star-poor field." That is a different
problem than exposure.

## Live vs. saved runs

What the block asks you to *do* about the number depends on where the run's frames came from.

A **Live sweep** run in **Optimize** mode adds a **New sweep exposure** row inside the Star signal block:
an editable exposure box, pre-filled with the recommended value, next to a **Capture a new sweep and
optimize** button. Adjust the number if you want, then the button recaptures the sweep at that exposure
and re-tunes detection settings against the new frames, without leaving the wizard. A Live sweep run in
**Use current settings** mode has nothing to re-tune, so that row is hidden; the block instead tells you
to switch to Optimize mode and run the wizard again.

A **saved ("Replay") run** has no rig attached to recapture from, so the block tells you to raise your
auto-focus exposure to about the recommended value in NINA's focuser options and run the wizard again in
Live mode once you have frames at the new exposure.

**Accepting the run in front of you is still a legitimate choice** in every case: the settings it found
are the best fit for the frames you actually have.

!!! note "Detection binning changes the same measurement"
    The per-star SNRs this recommendation reads are measured in the run's own binned-pixel space, so the
    result is self-consistent within that run, but a [Detection Binning](../settings/detection-binning.md)
    change raises the same per-pixel SNR on its own. When the summary offers both recommendations at
    once, they are not independently additive: change the binning factor first and let the next run
    re-measure the exposure. Stacking both changes in the same step over-corrects.

## See also

- [Brightness Sensitivity](../settings/acceptance-gates.md#brightness-sensitivity): the gate this
  recommendation responds to.
- [Detection Binning](../settings/detection-binning.md): the sibling recommendation on the same Summary
  page, also derived from a real measurement.
- [Star Detection Optimization](index.md): how the wizard scores and searches candidate settings.
