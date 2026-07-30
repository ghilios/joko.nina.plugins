#!/usr/bin/env python
"""Frame star-SCALE and candidate PLAUSIBILITY for the golden reference.

Why this exists: the SNR>=12 auto-confirm gate assumed a significant candidate is a real star. On a
heavily-defocused run that inverts -- a 3px noise spike at 12 sigma peak is MORE statistically
significant than a 36px donut spread at 0.25 sigma/px, so significance keeps the noise and discards
the stars. Integrated SNR does not fix it (it is the correct significance ordering and still ranks the
spike higher; measured in docs/golden-tier-plausibility-design.md 2). The only thing that separates
them is SCALE: real stars on a frame all share one size, set by defocus and seeing, while noise spikes
sit at the pixel scale whatever the defocus.

Pure functions, no I/O and no numpy, so the estimator is testable without a 116MB FITS.
"""
import math

TOP_FRAC_DEFAULT = 0.005
MIN_TOP_DEFAULT = 20
MIN_RATIO_DEFAULT = 0.3
MIN_SCALE_DEFAULT = 5.0


class ScaleCollapsed(Exception):
    """The frame's star-scale estimate fell to the pixel scale, so the plausibility gate would be a
    silent no-op. Raised rather than returned: emitting a golden in this state is what produced the
    quarantined lumos and SorenVance sets."""


def candidate_box(c):
    """Candidate extent in pixels."""
    return max(c['bw'], c['bh'])


def flux_snr(c):
    """Integrated flux in units of sigma -- the only measure comparable across the two detection paths.

    Falls back to an approximation for legacy candidate lists written before snr_ref emitted the field.
    For the connected-component path that approximation (peak/sigma * sqrt(area)) overestimates, since
    it treats every pixel as being at the peak; it is good enough for ranking, not for reporting.
    """
    if 'fluxSnr' in c:
        return float(c['fluxSnr'])
    return float(c['snr']) * math.sqrt(max(int(c.get('area', 1)), 1))


def frame_star_scale(cands, top_frac=TOP_FRAC_DEFAULT, min_top=MIN_TOP_DEFAULT):
    """Median box size of the top-N candidates by flux, over the CC+MF UNION. None if no candidates.

    Both properties are load-bearing and were measured:

      * UNION, not connected-component only. On lumos nearly every real star is found only by the
        matched filter, so a CC-only scale collapses to 3px against a true 36px and the gate silently
        does nothing while still reporting success.
      * TOP-BY-FLUX, not a plain median. On clean vsn07 at focus the plain median is 3px against a true
        10px, because the faint tail outnumbers the stars.
    """
    if not cands:
        return None
    n = max(min_top, int(len(cands) * top_frac))
    top = sorted(cands, key=flux_snr, reverse=True)[:n]
    boxes = sorted(candidate_box(c) for c in top)
    return float(boxes[len(boxes) // 2])


def check_scale(scale, frame_label, min_scale=MIN_SCALE_DEFAULT):
    """Guard: refuse to proceed when the scale estimate has collapsed to the pixel scale."""
    if scale is None or scale <= min_scale:
        raise ScaleCollapsed(
            f'{frame_label}: star-scale estimate collapsed to {scale} px (<= {min_scale} px). '
            'The matched-filter path found no donuts, so every candidate is at the pixel scale. '
            "Is --donut set, and are donut_radii large enough for this run's defocus? "
            'Refusing to emit a golden -- see docs/golden-tier-plausibility-design.md 4.3.')
    return scale


def plausibility(c, scale):
    """Candidate size relative to the frame's own star scale. 1.0 is a full-size star."""
    return candidate_box(c) / float(scale)


def is_plausible(c, scale, min_ratio=MIN_RATIO_DEFAULT):
    """Could this candidate be a star on THIS frame? Never an absolute pixel cut: a hard threshold
    calibrated on a defocused run rejects 52% of real near-focus stars on a clean one."""
    return plausibility(c, scale) >= min_ratio


def qa_order(cands, scale):
    """QA worklist order -- plausibility descending, then SNR descending. Returns global indices.

    Ordering, not filtering: a candidate the gate mis-scores is demoted in the queue, never deleted,
    so the gate can never permanently lose a real donut.
    """
    idx = list(range(len(cands)))
    idx.sort(key=lambda i: (-plausibility(cands[i], scale), -float(cands[i]['snr'])))
    return idx
