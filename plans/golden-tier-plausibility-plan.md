# Golden Tier Plausibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the golden reference's `SNR>=12` auto-confirm gate from keeping compact noise spikes and discarding real donuts on heavily-defocused AF runs, so `lumos` and `SorenVance` become scoreable and `LinwoodFocus` stops measuring the wrong star population.

**Architecture:** The tier definition (`snr >= 12` = "high") is unchanged, so `recall@SNR≥12` keeps its meaning. What changes is *membership*: a new frame-relative **star-plausibility** measure (candidate box size ÷ the frame's own star scale) reorders the LLM QA worklist so the montage budget lands on candidates that could be stars; auto-confirm is narrowed by that measure on normal runs and removed entirely on `--donut` runs; and candidates the budget never reaches become a third **unresolved** state excluded from both the recall and precision denominators.

**Tech Stack:** Python 3.10 + numpy/scipy/Pillow (`tools/golden/`, tested with stdlib `unittest` — no new dependency); C# .NET 8 + NUnit 4 (`Joko.NINA.Plugins/TestApp/`, tests in `Joko.NINA.Plugins.HocusFocus.Tests/Golden/`); a Claude Code `Workflow` script for the LLM QA fan-out.

**Read first:** `docs/golden-tier-plausibility-design.md` — especially §2 (why integrated SNR was rejected) and §4.3 (why the scale estimator must use the CC+MF union). Do not re-litigate either; both were measured.

---

## File Structure

| File | Responsibility |
|---|---|
| `tools/golden/plausibility.py` *(new)* | Pure functions: frame star-scale, plausibility ratio, worklist ordering, the collapse guard. No I/O, no numpy — trivially testable, imported by prep and by tests. |
| `tools/golden/golden_health.py` *(new)* | Validator over existing `<frame>.golden.json` sidecars only. No FITS, no `snr_*.json`, no LLM. |
| `tools/golden/snr_ref.py` | Add `flux`, `fluxSnr`, `src`, `snrKind` to each candidate. No behaviour change. |
| `tools/golden/golden_prep.py` | Compute the frame scale, order the worklist by plausibility, apply mode-dependent auto-confirm, write `qaorder_<foc>.json`. |
| `tools/golden/build_qa_worklist.py` | Drop the contiguous-prefix `b` offset; the workflow now returns montage-cell positions. |
| `tools/golden/qa_workflow.js` | Return cell positions rather than global indices; 3-vote majority on the high tier. |
| `tools/golden/persist_qa.py` | Translate cell positions → global indices via `qaorder_<foc>.json`; record `examined`. |
| `tools/golden/build_goldens.py` | Three-state resolution; emit schema v2 sidecars with `unresolved` + `coverage`. |
| `tools/golden/tests/*.py` *(new)* | `unittest` suite for all of the above. |
| `Joko.NINA.Plugins/TestApp/GoldenStarSet.cs` | Schema v2 POCOs: `Unresolved`, `Coverage`, `QaVersion`, `QaVotes`. |
| `Joko.NINA.Plugins/TestApp/GoldenGeometry.cs` | Home of the `ExcludeUnresolved` helper — this file is source-linked into the test project; `GoldenEvalRunner.cs` is not and cannot be (NINA/OpenCV deps). |
| `Joko.NINA.Plugins/TestApp/GoldenEvalRunner.cs` | Call site: exclude detections landing on unresolved boxes from the false-positive count. |

**Test commands** (from repo root):
- Python: `python3 -m unittest discover -s tools/golden/tests -v`
- C#: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`

On this machine `dotnet` lives on the Windows side; run it through WSL interop as `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo` with a 600 s timeout. `SendAsync_WritesOnABackgroundThread` in the EAT serial-transport suite is a known flake unrelated to this work — do not chase it.

---

## Task 1: Test scaffold + `snr_ref.py` emits flux and provenance

Downstream code cannot estimate a star scale without a flux measure comparable across the two detection paths, and cannot tell the paths apart at all today. `snr_ref.py` already computes the flux (`tot`, line 101) and throws it away.

**Files:**
- Create: `tools/golden/tests/__init__.py` (empty)
- Create: `tools/golden/tests/test_snr_ref.py`
- Modify: `tools/golden/snr_ref.py:84-125`

- [ ] **Step 1: Create the empty package marker**

```bash
mkdir -p tools/golden/tests && touch tools/golden/tests/__init__.py
```

- [ ] **Step 2: Write the failing test**

Create `tools/golden/tests/test_snr_ref.py`:

```python
"""snr_ref must emit a flux measure comparable across the connected-component and matched-filter
paths, and must say which path produced each candidate. See docs/golden-tier-plausibility-design.md 4.2."""
import os
import sys
import unittest

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import snr_ref  # noqa: E402


def _synthetic_frame(tmp_path):
    """A 512x512 frame: flat background 200, sigma ~10, one bright compact star and one faint wide disk."""
    rng = np.random.default_rng(1234)
    img = 200.0 + rng.normal(0.0, 10.0, size=(512, 512))
    yy, xx = np.mgrid[0:512, 0:512]
    img += 4000.0 * np.exp(-(((xx - 100) ** 2 + (yy - 100) ** 2) / (2 * 1.5 ** 2)))
    disk = ((xx - 350) ** 2 + (yy - 350) ** 2) <= 14 ** 2
    img[disk] += 45.0
    return np.clip(img, 0, 65535).astype(np.uint16)


class SnrRefCandidateFields(unittest.TestCase):

    def setUp(self):
        self.img = _synthetic_frame(None)

    def _detect(self, **kw):
        bg, sig = snr_ref.coarse_bg(self.img.astype(np.float32))
        return snr_ref.detect_from_arrays(self.img.astype(np.float32), bg, sig, **kw)

    def test_connected_component_candidates_carry_flux_and_provenance(self):
        cands = self._detect(k=5.0, donut=False)
        self.assertTrue(cands, 'expected at least the bright compact star')
        c = max(cands, key=lambda c: c['snr'])
        self.assertEqual(c['src'], 'cc')
        self.assertEqual(c['snrKind'], 'peak')
        self.assertGreater(c['flux'], 0.0)
        # fluxSnr is the integrated flux in units of sigma, so it must exceed the per-pixel peak SNR
        # for any source spread over more than one pixel.
        self.assertGreater(c['fluxSnr'], c['snr'])

    def test_matched_filter_candidates_carry_comparable_flux(self):
        cands = self._detect(k=5.0, donut=True)
        mf = [c for c in cands if c['src'] == 'mf']
        self.assertTrue(mf, 'expected the faint wide disk to be found by the matched filter')
        c = mf[0]
        self.assertEqual(c['snrKind'], 'matched')
        self.assertTrue(c['donut'])
        # fluxSnr = response * sqrt(area): the matched-filter response already carries the sqrt(N) gain.
        self.assertAlmostEqual(c['fluxSnr'], c['snr'] * np.sqrt(c['area']), delta=0.05 * c['fluxSnr'])

    def test_flux_snr_is_comparable_across_paths(self):
        """The whole point: a wide faint disk must not be ranked below a 3px spike by fluxSnr."""
        cands = self._detect(k=5.0, donut=True)
        by_src = {}
        for c in cands:
            by_src.setdefault(c['src'], []).append(c)
        self.assertIn('cc', by_src)
        self.assertIn('mf', by_src)
        for c in cands:
            self.assertIn('fluxSnr', c)
            self.assertGreaterEqual(c['fluxSnr'], 0.0)


if __name__ == '__main__':
    unittest.main()
```

- [ ] **Step 3: Run it to verify it fails**

Run: `python3 -m unittest tools.golden.tests.test_snr_ref -v`
Expected: FAIL — `AttributeError: module 'snr_ref' has no attribute 'detect_from_arrays'`.

- [ ] **Step 4: Split `detect` so the pixel work is testable, and add the fields**

In `tools/golden/snr_ref.py`, add `import math` at the top, then replace the body of `detect` (lines 84-125) with:

```python
def detect_from_arrays(img, bg, sig, k=5.0, min_area=3, max_area=20000, close=2, donut=False,
                       donut_radii=(6, 10, 14, 18), donut_k=6.0, sat_radius=0.0):
    """Candidate extraction from already-loaded arrays. Split out of detect() so tests can drive it
    with a synthetic frame instead of a FITS file."""
    signal = img - bg
    satmask = saturation_mask(img, radius=sat_radius)
    mask = signal > (k * sig)
    if close > 0:
        mask = ndimage.binary_closing(mask, structure=np.ones((close, close)))
    lbl, n = ndimage.label(mask)
    objs = ndimage.find_objects(lbl) if n else []
    cands = []
    for i, sl in enumerate(objs, start=1):
        ys, xs = sl
        area = int((lbl[sl] == i).sum())
        if area < min_area or area > max_area:
            continue
        sub_sig = np.where(lbl[sl] == i, signal[sl], 0)
        tot = sub_sig.sum()
        if tot <= 0:
            continue
        yy, xx = np.mgrid[ys.start:ys.stop, xs.start:xs.stop]
        cy = float((yy * sub_sig).sum() / tot)
        cx = float((xx * sub_sig).sum() / tot)
        peak = float(signal[sl][lbl[sl] == i].max())
        sg = float(np.median(sig[sl]))
        cands.append({'x': round(cx, 1), 'y': round(cy, 1),
                      'bx': int(xs.start), 'by': int(ys.start),
                      'bw': int(xs.stop - xs.start), 'bh': int(ys.stop - ys.start),
                      'peak': round(peak, 1), 'snr': round(peak / sg, 2), 'area': area,
                      # flux in ADU, and flux in units of sigma. fluxSnr is the ONLY quantity comparable
                      # across the two detection paths -- 'snr' is peak/sigma here but a disk-integrated
                      # matched-filter response below, which is what F16 tripped over.
                      'flux': round(float(tot), 1), 'fluxSnr': round(float(tot) / sg, 2),
                      'src': 'cc', 'snrKind': 'peak'})
    if donut:
        existing = [(c['x'], c['y']) for c in cands]
        for (dy, dx, resp, r) in matched_filter_donuts(signal, sig, donut_radii, donut_k):
            if any((dx - ex) ** 2 + (dy - ey) ** 2 <= (r * 1.0) ** 2 for ex, ey in existing):
                continue
            area = int(math.pi * r * r)
            cands.append({'x': float(dx), 'y': float(dy), 'bx': dx - r, 'by': dy - r,
                          'bw': 2 * r, 'bh': 2 * r,
                          'peak': 0.0, 'snr': round(resp, 2), 'area': area, 'donut': True,
                          # resp = mean*sqrt(N)/sigma, so flux/sigma = resp*sqrt(N).
                          'flux': 0.0, 'fluxSnr': round(resp * math.sqrt(area), 2),
                          'src': 'mf', 'snrKind': 'matched'})
            existing.append((dx, dy))
    if satmask is not None:
        H, W = img.shape
        cands = [c for c in cands
                 if not satmask[min(H - 1, max(0, int(round(c['y'])))), min(W - 1, max(0, int(round(c['x']))))]]
    return cands


def detect(path, k=5.0, min_area=3, max_area=20000, close=2, donut=False,
           donut_radii=(6, 10, 14, 18), donut_k=6.0, sat_radius=0.0):
    img = read_fits(path)
    bg, sig = coarse_bg(img)
    cands = detect_from_arrays(img, bg, sig, k=k, min_area=min_area, max_area=max_area, close=close,
                               donut=donut, donut_radii=donut_radii, donut_k=donut_k, sat_radius=sat_radius)
    return img, bg, sig, cands
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `python3 -m unittest discover -s tools/golden/tests -v`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add tools/golden/snr_ref.py tools/golden/tests/
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(golden): snr_ref emits flux, fluxSnr and path provenance

fluxSnr is the only quantity comparable across the connected-component and
matched-filter paths; 'snr' silently carried two different measures."
```

---

## Task 2: `plausibility.py` — frame star-scale, ordering, and the collapse guard

Pure functions with no I/O so the tricky part (§4.3 of the design) is testable without a 116 MB FITS.

**Files:**
- Create: `tools/golden/plausibility.py`
- Create: `tools/golden/tests/test_plausibility.py`

- [ ] **Step 1: Write the failing test**

Create `tools/golden/tests/test_plausibility.py`:

```python
"""The frame star-scale estimator and its guard. The three regressions encoded here are the exact
failures measured in docs/golden-tier-plausibility-design.md 4.3 -- do not relax them."""
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import plausibility as P  # noqa: E402


def cc(box, flux_snr, snr=12.0):
    return {'bw': box, 'bh': box, 'area': box * box, 'snr': snr, 'fluxSnr': flux_snr, 'src': 'cc'}


def mf(box, flux_snr, snr=8.0):
    return {'bw': box, 'bh': box, 'area': box * box, 'snr': snr, 'fluxSnr': flux_snr,
            'src': 'mf', 'donut': True}


class FrameStarScale(unittest.TestCase):

    def test_union_scale_survives_a_spike_dominated_compact_path(self):
        """lumos: 4,676 compact spikes and 40,435 matched-filter donuts. A CC-only scale collapses to
        3px and the gate becomes a silent no-op; the union must report 36px."""
        cands = [cc(3, 20.0) for _ in range(4676)] + [mf(36, 400.0) for _ in range(300)]
        self.assertEqual(P.frame_star_scale(cands), 36.0)

    def test_plain_median_would_fail_but_top_flux_does_not(self):
        """vsn07 at focus: real stars are 10px but the faint tail drags a plain median to 3px."""
        cands = [cc(10, 500.0) for _ in range(40)] + [cc(3, 8.0) for _ in range(960)]
        self.assertEqual(P.frame_star_scale(cands), 10.0)

    def test_scale_uses_at_least_min_top_candidates(self):
        cands = [cc(12, 100.0 - i) for i in range(5)]
        self.assertEqual(P.frame_star_scale(cands), 12.0)

    def test_empty_candidate_list_gives_no_scale(self):
        self.assertIsNone(P.frame_star_scale([]))


class ScaleGuard(unittest.TestCase):

    def test_collapsed_scale_raises_rather_than_emitting_a_golden(self):
        with self.assertRaises(P.ScaleCollapsed) as ctx:
            P.check_scale(3.0, 'lumos foc 209735')
        self.assertIn('lumos foc 209735', str(ctx.exception))
        self.assertIn('--donut', str(ctx.exception))

    def test_none_scale_raises(self):
        with self.assertRaises(P.ScaleCollapsed):
            P.check_scale(None, 'empty frame')

    def test_healthy_scale_passes_through(self):
        self.assertEqual(P.check_scale(10.0, 'vsn07 foc 9893'), 10.0)


class PlausibilityGate(unittest.TestCase):

    def test_spike_is_implausible_against_a_defocused_scale(self):
        self.assertFalse(P.is_plausible(cc(3, 20.0), scale=36.0))

    def test_full_size_donut_is_plausible(self):
        self.assertTrue(P.is_plausible(mf(36, 400.0), scale=36.0))

    def test_compact_star_is_plausible_against_its_own_at_focus_scale(self):
        """A 4px star on a frame whose scale is 10px must survive -- a hard pixel floor would reject
        52% of real near-focus stars on clean vsn07."""
        self.assertTrue(P.is_plausible(cc(4, 200.0), scale=10.0))


class QaOrder(unittest.TestCase):

    def test_plausible_candidates_are_queued_before_significant_spikes(self):
        cands = [cc(3, 20.0, snr=40.0), mf(36, 400.0, snr=8.0), cc(3, 19.0, snr=30.0)]
        self.assertEqual(P.qa_order(cands, scale=36.0)[0], 1)

    def test_ties_break_on_snr_descending(self):
        cands = [mf(36, 100.0, snr=8.0), mf(36, 100.0, snr=20.0)]
        self.assertEqual(P.qa_order(cands, scale=36.0), [1, 0])

    def test_order_is_a_permutation_of_all_indices(self):
        cands = [cc(3, 20.0), mf(36, 400.0), cc(7, 90.0)]
        self.assertEqual(sorted(P.qa_order(cands, scale=36.0)), [0, 1, 2])


if __name__ == '__main__':
    unittest.main()
```

- [ ] **Step 2: Run it to verify it fails**

Run: `python3 -m unittest tools.golden.tests.test_plausibility -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'plausibility'`.

- [ ] **Step 3: Write the implementation**

Create `tools/golden/plausibility.py`:

```python
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
            'Is --donut set, and are donut_radii large enough for this run\'s defocus? '
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `python3 -m unittest discover -s tools/golden/tests -v`
Expected: PASS, 16 tests.

- [ ] **Step 5: Commit**

```bash
git add tools/golden/plausibility.py tools/golden/tests/test_plausibility.py
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(golden): frame star-scale, plausibility ratio and the collapse guard

Scale must come from the CC+MF union ranked by flux: a CC-only scale collapses
to 3px on lumos and a plain median collapses to 3px on vsn07."
```

---

## Task 3: `golden_health.py` — validate an existing golden with no re-read

This is the instrument that decides which goldens the rest of the work must touch, and the regression gate afterwards. It reads sidecars only.

**Files:**
- Create: `tools/golden/golden_health.py`
- Create: `tools/golden/tests/test_golden_health.py`

- [ ] **Step 1: Write the failing test**

Create `tools/golden/tests/test_golden_health.py`:

```python
"""Golden health metrics, computed from stored sidecars alone. The fixtures reproduce the measured
shapes of the real runs so the thresholds stay anchored to data."""
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import golden_health as H  # noqa: E402


def star(w, conf='high'):
    return {'x': 0, 'y': 0, 'w': w, 'h': w, 'confidence': conf}


def frame(foc, high_widths, qa_widths):
    return {'focuserPosition': foc,
            'stars': [star(w, 'high') for w in high_widths] + [star(w, 'medium') for w in qa_widths]}


class SizeCollapse(unittest.TestCase):

    def test_lumos_shape_is_flagged(self):
        """lumos: 91% of the high tier at <=4px."""
        frames = [frame(f, [3] * 91 + [20] * 9, [3] * 50) for f in (100, 200, 300)]
        r = H.assess(frames)
        self.assertAlmostEqual(r['smallFraction'], 0.91, places=2)
        self.assertIn('SIZE-COLLAPSE', r['flags'])

    def test_clean_run_is_not_flagged_for_size(self):
        """vsn07 is the tightest clean case at 34% -- it must pass."""
        frames = [frame(f, [3] * 34 + [12] * 66, [4] * 40) for f in (100, 200, 300)]
        r = H.assess(frames)
        self.assertNotIn('SIZE-COLLAPSE', r['flags'])


class TierInversion(unittest.TestCase):

    def test_linwood_shape_is_flagged(self):
        """LinwoodFocus: high tier median 13px against a QA tier median of 28px -> ratio 0.46."""
        frames = [frame(f, [13] * 100, [28] * 100) for f in (100, 200, 300)]
        r = H.assess(frames)
        self.assertLess(r['widthRatio'], 1.0)
        self.assertIn('TIER-INVERSION', r['flags'])

    def test_sound_tiering_passes(self):
        """mufti: 20px high tier against a 12px QA tier -> ratio 1.67."""
        frames = [frame(f, [20] * 100, [12] * 100) for f in (100, 200, 300)]
        r = H.assess(frames)
        self.assertGreater(r['widthRatio'], 1.0)
        self.assertNotIn('TIER-INVERSION', r['flags'])

    def test_ratio_is_computed_per_frame_then_aggregated(self):
        """Aggregating first hides inversion behind frame mix (Simpson's paradox): CWhiteFocus reads
        0.58 pooled but 1.21 per-frame, and 1.21 is the truthful number."""
        frames = [frame(100, [7] * 1000, [11] * 10), frame(200, [7] * 10, [11] * 1000)]
        r = H.assess(frames)
        self.assertAlmostEqual(r['widthRatio'], 7.0 / 11.0, places=3)


class FocusResponse(unittest.TestCase):

    def test_flat_width_across_the_sweep_is_flagged(self):
        """lumos: median high-tier width is exactly 3px on all 23 frames."""
        frames = [frame(f, [3] * 100, [3] * 50) for f in range(100, 800, 100)]
        r = H.assess(frames)
        self.assertEqual(r['widthDynamicRange'], 0.0)
        self.assertIn('WIDTH-FLAT', r['flags'])

    def test_healthy_sweep_passes(self):
        widths = [20, 14, 8, 5, 8, 14, 20]
        frames = [frame(100 * i, [w] * 100, [w] * 50) for i, w in enumerate(widths)]
        r = H.assess(frames)
        self.assertGreater(r['widthDynamicRange'], 0.15)
        self.assertNotIn('WIDTH-FLAT', r['flags'])


class Verdict(unittest.TestCase):

    def test_clean_run_has_no_flags(self):
        widths = [20, 14, 8, 5, 8, 14, 20]
        frames = [frame(100 * i, [w] * 100, [max(4, w - 4)] * 50) for i, w in enumerate(widths)]
        self.assertEqual(H.assess(frames)['flags'], [])

    def test_too_few_frames_is_inconclusive(self):
        r = H.assess([frame(100, [10] * 10, [8] * 10)])
        self.assertIn('INCONCLUSIVE', r['flags'])


if __name__ == '__main__':
    unittest.main()
```

- [ ] **Step 2: Run it to verify it fails**

Run: `python3 -m unittest tools.golden.tests.test_golden_health -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'golden_health'`.

- [ ] **Step 3: Write the implementation**

Create `tools/golden/golden_health.py`:

```python
#!/usr/bin/env python
"""Validate a golden star set from its stored <frame>.golden.json sidecars ALONE.

No FITS, no snr_<foc>.json, no LLM -- so the whole bank can be audited in seconds and only the runs
that actually fail need the expensive rebuild. This is what caught LinwoodFocus, whose high tier holds
13px boxes while its QA-confirmed tier holds 28px ones.

Signals (see docs/golden-tier-plausibility-design.md 4.1):
  * SIZE-COLLAPSE  -- the high tier has degenerated to pixel-scale detections.
  * TIER-INVERSION -- high-tier stars are SMALLER than QA-confirmed ones, which is backwards: brighter
                      stars are bigger, so a sound tiering gives a ratio >= 1.
  * WIDTH-FLAT     -- median high-tier width does not respond to focus across the sweep, meaning the
                      reference is measuring something that is not the star field.

Usage:
  golden_health.py --bank "D:/Autofocus Bank" [--json out.json]
  golden_health.py --run-dir <run folder>
"""
import argparse
import glob
import json
import os
import re

SMALL_PX = 4
SMALL_FRACTION_MAX = 0.55
WIDTH_RATIO_MIN = 1.0
WIDTH_DYNAMIC_MIN = 0.15
MIN_FRAMES = 3

FOCUSER_RE = re.compile(r'Focuser(\d+)', re.IGNORECASE)


def _median(xs):
    s = sorted(xs)
    return float(s[len(s) // 2]) if s else 0.0


def _widths(stars, high):
    return [max(s['w'], s['h']) for s in stars
            if ((s.get('confidence') or 'high') == 'high') == high]


def assess(frames):
    """frames: list of parsed golden dicts. Returns metrics + flags."""
    frames = sorted(frames, key=lambda f: f.get('focuserPosition', 0))
    per_frame_ratio, per_frame_width, all_high = [], [], []
    for f in frames:
        hi = _widths(f['stars'], True)
        qa = _widths(f['stars'], False)
        all_high.extend(hi)
        if hi:
            per_frame_width.append(_median(hi))
        if hi and qa:
            per_frame_ratio.append(_median(hi) / max(_median(qa), 1e-9))

    small = (sum(1 for w in all_high if w <= SMALL_PX) / len(all_high)) if all_high else 0.0
    ratio = _median(per_frame_ratio) if per_frame_ratio else float('nan')
    if per_frame_width:
        wmax, wmin = max(per_frame_width), min(per_frame_width)
        dyn = (wmax - wmin) / wmax if wmax > 0 else 0.0
    else:
        dyn = 0.0

    flags = []
    if len(frames) < MIN_FRAMES or not all_high:
        flags.append('INCONCLUSIVE')
    else:
        if small > SMALL_FRACTION_MAX:
            flags.append('SIZE-COLLAPSE')
        if per_frame_ratio and ratio < WIDTH_RATIO_MIN:
            flags.append('TIER-INVERSION')
        if dyn < WIDTH_DYNAMIC_MIN:
            flags.append('WIDTH-FLAT')
    return {'frames': len(frames), 'highStars': len(all_high), 'medianHighWidth': _median(all_high),
            'smallFraction': small, 'widthRatio': ratio, 'widthDynamicRange': dyn, 'flags': flags}


def load_run(run_dir):
    frames = []
    for p in glob.glob(os.path.join(run_dir, '**', '*.golden.json'), recursive=True):
        d = json.load(open(p))
        if 'focuserPosition' not in d:
            m = FOCUSER_RE.search(os.path.basename(p))
            if not m:
                continue
            d['focuserPosition'] = int(m.group(1))
        frames.append(d)
    return frames


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--bank', help='bank root; every immediate subfolder is treated as a run')
    ap.add_argument('--run-dir', help='a single run folder')
    ap.add_argument('--json', help='write the full report here')
    args = ap.parse_args()

    runs = {}
    if args.run_dir:
        runs[os.path.basename(os.path.normpath(args.run_dir))] = load_run(args.run_dir)
    elif args.bank:
        for entry in sorted(os.listdir(args.bank)):
            path = os.path.join(args.bank, entry)
            if os.path.isdir(path) and not entry.startswith('_'):
                fr = load_run(path)
                if fr:
                    runs[entry] = fr
    else:
        ap.error('one of --bank or --run-dir is required')

    report, failed = {}, 0
    print(f'{"run":<20} {"fr":>3} {"high":>8} {"medW":>5} {"<=4px":>6} {"ratio":>6} {"wDyn":>5}  flags')
    for name, frames in runs.items():
        r = assess(frames)
        report[name] = r
        if r['flags'] and r['flags'] != ['INCONCLUSIVE']:
            failed += 1
        print(f'{name:<20} {r["frames"]:>3} {r["highStars"]:>8} {r["medianHighWidth"]:>5.0f} '
              f'{r["smallFraction"] * 100:>5.0f}% {r["widthRatio"]:>6.2f} {r["widthDynamicRange"]:>5.2f}  '
              f'{" + ".join(r["flags"]) or "ok"}')
    if args.json:
        json.dump(report, open(args.json, 'w'), indent=1)
        print(f'wrote {args.json}')
    print(f'{failed} of {len(runs)} run(s) flagged')
    return 1 if failed else 0


if __name__ == '__main__':
    raise SystemExit(main())
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `python3 -m unittest discover -s tools/golden/tests -v`
Expected: PASS, 25 tests.

- [ ] **Step 5: Record the baseline verdict for the whole bank**

Run: `python3 tools/golden/golden_health.py --bank "/mnt/d/Autofocus Bank" --json /tmp/golden_health_baseline.json`

Expected: `LinwoodFocus` flagged `TIER-INVERSION`; the other 12 non-donut runs `ok`. Paste the table into the commit message — it is the before-picture the rebuild is measured against.

- [ ] **Step 6: Commit**

```bash
git add tools/golden/golden_health.py tools/golden/tests/test_golden_health.py
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(golden): golden_health validates a golden from its sidecars alone

Size collapse, per-frame tier-size inversion and width-vs-focus response, with
no FITS or LLM, so the bank is audited in seconds and only failures get rebuilt."
```

---

## Task 4: `golden_prep.py` — scale, plausibility ordering, mode-dependent auto-confirm

**Files:**
- Modify: `tools/golden/golden_prep.py:91-133` (`main`), and the `--min-scale` argument
- Create: `tools/golden/tests/test_golden_prep.py`

- [ ] **Step 1: Write the failing test**

Create `tools/golden/tests/test_golden_prep.py`:

```python
"""Prep-stage policy: which candidates are auto-confirmed, and in what order the rest are queued."""
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import golden_prep as G  # noqa: E402


def cc(box, snr, flux_snr):
    return {'bw': box, 'bh': box, 'area': box * box, 'snr': snr, 'fluxSnr': flux_snr, 'src': 'cc'}


def mf(box, snr, flux_snr):
    return {'bw': box, 'bh': box, 'area': box * box, 'snr': snr, 'fluxSnr': flux_snr,
            'src': 'mf', 'donut': True}


class AutoConfirmPolicy(unittest.TestCase):

    def test_donut_mode_auto_confirms_nothing(self):
        cands = [mf(36, 40.0, 400.0), cc(3, 30.0, 20.0)]
        self.assertEqual(G.auto_confirmed_indices(cands, scale=36.0, donut=True, threshold=12.0), set())

    def test_normal_mode_requires_both_significance_and_plausibility(self):
        cands = [cc(10, 30.0, 300.0),   # significant and full-size  -> auto-confirm
                 cc(3, 30.0, 20.0),     # significant but pixel-scale -> QA
                 cc(10, 6.0, 40.0)]     # full-size but insignificant -> QA
        got = G.auto_confirmed_indices(cands, scale=10.0, donut=False, threshold=12.0)
        self.assertEqual(got, {0})

    def test_compact_star_at_focus_is_still_auto_confirmed(self):
        """A 4px star on a 10px-scale frame is a real star, not a spike."""
        cands = [cc(4, 20.0, 200.0)]
        self.assertEqual(G.auto_confirmed_indices(cands, scale=10.0, donut=False, threshold=12.0), {0})


class WorklistOrder(unittest.TestCase):

    def test_donut_mode_queues_every_candidate_including_the_high_tier(self):
        cands = [cc(3, 40.0, 20.0), mf(36, 8.0, 400.0)]
        order = G.qa_worklist(cands, scale=36.0, donut=True, threshold=12.0)
        self.assertEqual(sorted(order), [0, 1])
        self.assertEqual(order[0], 1, 'the donut must be queued before the spike')

    def test_normal_mode_excludes_auto_confirmed_candidates_from_the_worklist(self):
        cands = [cc(10, 30.0, 300.0), cc(3, 30.0, 20.0)]
        self.assertEqual(G.qa_worklist(cands, scale=10.0, donut=False, threshold=12.0), [1])


if __name__ == '__main__':
    unittest.main()
```

- [ ] **Step 2: Run it to verify it fails**

Run: `python3 -m unittest tools.golden.tests.test_golden_prep -v`
Expected: FAIL — `AttributeError: module 'golden_prep' has no attribute 'auto_confirmed_indices'`.

- [ ] **Step 3: Add the policy functions**

In `tools/golden/golden_prep.py`, after the imports add `from plausibility import check_scale, frame_star_scale, is_plausible, qa_order` and then, above `main()`:

```python
def auto_confirmed_indices(cands, scale, donut, threshold):
    """Which candidates skip LLM QA entirely.

    On --donut runs: NONE. The matched filter shreds rings into fragments and the SNR ordering inverts,
    so significance alone cannot be trusted and the high tier is QA'd like any other tier.

    Otherwise: significance AND plausibility. Significance was never sufficient -- a 3px spike at 12
    sigma peak outranks a real donut -- so a candidate must also be a credible size for its frame.
    """
    if donut:
        return set()
    return {i for i, c in enumerate(cands)
            if float(c['snr']) >= threshold and is_plausible(c, scale)}


def qa_worklist(cands, scale, donut, threshold):
    """Global indices to render for QA, in priority order: plausibility desc, then SNR desc.

    Ordering, never filtering -- the budget decides how far down the queue we get, and everything past
    that point is recorded as UNRESOLVED rather than silently treated as not-a-star.
    """
    auto = auto_confirmed_indices(cands, scale, donut, threshold)
    return [i for i in qa_order(cands, scale) if i not in auto]
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `python3 -m unittest discover -s tools/golden/tests -v`
Expected: PASS, 30 tests.

- [ ] **Step 5: Wire the policy into `main`**

In `tools/golden/golden_prep.py`, add the argument next to the others in `main()`:

```python
    ap.add_argument('--min-scale', type=float, default=5.0,
                    help='abort if a frame\'s star-scale estimate falls to/below this (px). The guard '
                         'against a silently-inert plausibility gate; see design 4.3.')
```

Then replace the per-frame body (currently lines 119-131, from `_, _, _, cands = detect(...)` to the `print`) with:

```python
        _, _, _, cands = detect(lin, k=args.k, donut=args.donut, sat_radius=args.sat_radius)
        cands.sort(key=lambda c: -c['snr'])  # stable, human-readable order for snr_<foc>.json
        json.dump(cands, open(os.path.join(args.out, f'snr_{foc}.json'), 'w'))

        scale = check_scale(frame_star_scale(cands), f'{os.path.basename(args.run_dir)} foc {foc}',
                            min_scale=args.min_scale)
        auto = auto_confirmed_indices(cands, scale, args.donut, args.auto_confirm_snr)
        order = qa_worklist(cands, scale, args.donut, args.auto_confirm_snr)
        to_qa = order[: args.budget_montages * per]
        # The workflow returns montage CELL POSITIONS; this file translates position -> global index,
        # which is what lets the worklist be plausibility-ordered instead of a contiguous SNR prefix.
        json.dump(to_qa, open(os.path.join(args.out, f'qaorder_{foc}.json'), 'w'))

        mdir = os.path.join(args.out, f'f{foc}')
        montages = build_montages(lin, [cands[i] for i in to_qa], to_qa, mdir,
                                  crop=args.crop, cell=args.cell, grid=args.grid) if to_qa else []
        manifest['frames'].append({'foc': foc, 'imageFile': fn, 'total': len(cands),
                                   'starScale': scale, 'autoConfirmed': len(auto),
                                   'high': sum(1 for c in cands if c['snr'] >= args.auto_confirm_snr),
                                   'queued': len(order), 'rendered': len(to_qa),
                                   'montageDir': mdir, 'montageCount': len(montages)})
        print(f'  foc {foc}: {len(cands)} cand, scale {scale:.0f}px, {len(auto)} auto-confirmed, '
              f'{len(to_qa)}/{len(order)} queued -> {len(montages)} montages')
```

- [ ] **Step 6: Verify the guard fires on the real broken run**

Run:
```bash
python3 tools/golden/golden_prep.py \
  --run-dir "/mnt/d/Autofocus Bank/lumos/AutoFocus_20260708_231255/attempt01" \
  --out /tmp/lumos_nodonut_probe --budget-montages 1
```
Expected: exits non-zero with `ScaleCollapsed: ... star-scale estimate collapsed to 3.0 px`. This is acceptance criterion 5 — without `--donut`, `lumos` must refuse to produce a golden rather than emit a silently-inert one.

- [ ] **Step 7: Commit**

```bash
git add tools/golden/golden_prep.py tools/golden/tests/test_golden_prep.py
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(golden): plausibility-ordered QA worklist and mode-dependent auto-confirm

--donut runs auto-confirm nothing; normal runs require plausibility as well as
significance. Aborts when the frame star-scale collapses to the pixel scale."
```

---

## Task 5: Remove the contiguous-prefix assumption from the QA bridge

`build_qa_worklist.py:40` passes `b = fr['high']` and `qa_workflow.js:49` computes `g0 = b + m*per`, both assuming the rendered set is a contiguous SNR-descending suffix of the auto-confirmed prefix. Plausibility ordering breaks that, and with auto-confirm off there is no prefix at all. This is a correctness bug the moment Task 4 lands.

**Files:**
- Modify: `tools/golden/build_qa_worklist.py:34-45`
- Modify: `tools/golden/qa_workflow.js:8-11,45-54`
- Modify: `tools/golden/persist_qa.py:14-34`
- Create: `tools/golden/tests/test_persist_qa.py`

- [ ] **Step 1: Write the failing test**

Create `tools/golden/tests/test_persist_qa.py`:

```python
"""The workflow returns montage CELL POSITIONS; persist_qa maps them to global candidate indices via
qaorder_<foc>.json. Positional mapping is what allows a plausibility-ordered worklist."""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import persist_qa as PQ  # noqa: E402


class PositionToGlobalIndex(unittest.TestCase):

    def setUp(self):
        self.root = tempfile.mkdtemp()
        self.run = os.path.join(self.root, 'myrun')
        os.makedirs(self.run)
        # Worklist is NOT a contiguous range: this is the whole point.
        json.dump([90, 12, 7, 55], open(os.path.join(self.run, 'qaorder_1234.json'), 'w'))

    def test_cell_positions_map_through_qaorder(self):
        PQ.persist(self.root, {'myrun 1234': {'real': [0, 3], 'donut': [3]}})
        got = json.load(open(os.path.join(self.run, 'qa_1234.json')))
        self.assertEqual(got['confirmed'], [55, 90])
        self.assertEqual(got['donut'], [55])

    def test_examined_records_every_rendered_candidate(self):
        """Without this, build_goldens cannot tell a QA-rejected candidate from an unexamined one."""
        PQ.persist(self.root, {'myrun 1234': {'real': [0], 'donut': []}})
        got = json.load(open(os.path.join(self.run, 'qa_1234.json')))
        self.assertEqual(got['examined'], [7, 12, 55, 90])

    def test_out_of_range_positions_are_dropped(self):
        PQ.persist(self.root, {'myrun 1234': {'real': [0, 99, -1], 'donut': []}})
        got = json.load(open(os.path.join(self.run, 'qa_1234.json')))
        self.assertEqual(got['confirmed'], [90])


if __name__ == '__main__':
    unittest.main()
```

- [ ] **Step 2: Run it to verify it fails**

Run: `python3 -m unittest tools.golden.tests.test_persist_qa -v`
Expected: FAIL — `AttributeError: module 'persist_qa' has no attribute 'persist'`.

- [ ] **Step 3: Rewrite `persist_qa.py`**

Replace the body of `tools/golden/persist_qa.py` below the docstring with:

```python
import json, os, re, argparse

KEY_RE = re.compile(r'^(.*?)[\s\x00|]*(\d+)$')


def persist(scratch_root, by_key):
    """Map montage cell POSITIONS to global candidate indices via qaorder_<foc>.json, and record which
    candidates were examined at all. Returns the number of sidecars written."""
    written = 0
    for key, v in by_key.items():
        m = KEY_RE.match(key)
        if not m:
            print(f'  ?? unparseable key: {key!r}')
            continue
        run, foc = m.group(1).rstrip(' \x00|'), m.group(2)
        outdir = os.path.join(scratch_root, run)
        order_path = os.path.join(outdir, f'qaorder_{foc}.json')
        if not os.path.isdir(outdir) or not os.path.exists(order_path):
            print(f'  ?? no qaorder_{foc}.json for run {run!r} (key {key!r})')
            continue
        order = json.load(open(order_path))

        def to_global(positions):
            return sorted({order[p] for p in positions if isinstance(p, int) and 0 <= p < len(order)})

        json.dump({'confirmed': to_global(v.get('real', v.get('confirmed', []))),
                   'donut': to_global(v.get('donut', [])),
                   'examined': sorted(order)},
                  open(os.path.join(outdir, f'qa_{foc}.json'), 'w'))
        written += 1
    return written


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--bykey', required=True)
    ap.add_argument('--scratch-root', required=True)
    args = ap.parse_args()
    data = json.load(open(args.bykey))
    bk = data.get('byKey', data) if isinstance(data, dict) else {}
    n = persist(args.scratch_root, bk)
    print(f'persist_qa: wrote {n} qa_<foc>.json sidecars under {args.scratch_root}')


if __name__ == '__main__':
    main()
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `python3 -m unittest discover -s tools/golden/tests -v`
Expected: PASS, 33 tests.

- [ ] **Step 5: Drop `b` from the worklist bridge**

In `tools/golden/build_qa_worklist.py`, replace line 40 with:

```python
            frames.append({'foc': fr['foc'], 'n': n})
```

and update the module docstring's `Emits:` line to:

```
Emits: { base, grid, runs:[ { tag, frames:[ { foc, n (montages to QA, capped) } ] } ] }
```

Replace the first paragraph of that docstring with:

```
The workflow returns montage CELL POSITIONS, not global candidate indices: golden_prep renders the QA
worklist in PLAUSIBILITY order, which is not a contiguous prefix of snr_<foc>.json, so no arithmetic
can recover a global index from a cell. persist_qa.py maps position -> global index through
qaorder_<foc>.json. That keeps the worklist small enough to pass inline as Workflow args while
supporting an arbitrary ordering.
```

- [ ] **Step 6: Update the workflow to return positions**

In `tools/golden/qa_workflow.js`, replace lines 8-11 with:

```javascript
//   args = { base, grid?:6, votes?:1, runs:[ { tag, frames:[ { foc, n } ] } ] }
// Returns { byKey: { "<tag> <foc>": { real:[cellPosition...], donut:[cellPosition...] } } }. Positions are
// montage-cell ordinals (m*grid^2 + cell); persist_qa.py maps them to global candidate indices through
// qaorder_<foc>.json, because the worklist is plausibility-ordered rather than a contiguous SNR prefix.
```

Replace line 23 with:

```javascript
      work.push({ tag: r.tag, foc: fr.foc, m, file: `${base}/${r.tag}/f${fr.foc}/montage_${pad3(m)}.png` })
```

Replace lines 45-54 with:

```javascript
const byKey = {}
for (const it of results.filter(Boolean)) {
  const key = `${it.w.tag} ${it.w.foc}`
  if (!byKey[key]) byKey[key] = { real: [], donut: [] }
  const p0 = it.w.m * per
  for (const cell of it.real) if (cell >= 0 && cell < per) byKey[key].real.push(p0 + cell)
  for (const cell of it.donut) if (cell >= 0 && cell < per) byKey[key].donut.push(p0 + cell)
}
for (const k of Object.keys(byKey)) log(`${k}: ${byKey[k].real.length} confirmed`)
return { byKey }
```

- [ ] **Step 7: Commit**

```bash
git add tools/golden/build_qa_worklist.py tools/golden/qa_workflow.js tools/golden/persist_qa.py tools/golden/tests/test_persist_qa.py
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "fix(golden): map QA results by cell position, not by SNR-prefix arithmetic

The b = high_count base index assumed the rendered set was a contiguous
SNR-descending suffix. Plausibility ordering breaks that, and with auto-confirm
off on donut runs there is no prefix at all."
```

---

## Task 6: 3-vote majority QA on the high tier

Measured on three independent regenerations of the same frames, auto-confirm is bit-for-bit reproducible (Jaccard 1.0000) but the LLM QA pass is not — and the worst case is the deeply-defocused `LinwoodFocus` at **0.62**. Moving the high tier into QA therefore trades a deterministic-but-wrong reference for a correct-but-stochastic one unless the votes are repeated.

**Files:**
- Modify: `tools/golden/qa_workflow.js:35-44`

- [ ] **Step 1: Add vote repetition and majority tally**

In `tools/golden/qa_workflow.js`, replace lines 35-44 with:

```javascript
// Server-side rate-limiting throttles sustained image-heavy bursts at the default ~16-way pipeline
// concurrency, so process in small SEQUENTIAL chunks (effective concurrency = CHUNK).
const CHUNK = (A && A.chunk) || 4
// Independent repeats per montage, majority-confirmed. Two regenerations of the same LinwoodFocus
// frames agreed on only 62% of QA-confirmed stars, so a single vote makes recall@SNR>=12
// non-reproducible on exactly the defocused runs this pipeline now depends on. 1 = legacy behaviour.
const VOTES = (A && A.votes) || 1
const NEED = Math.floor(VOTES / 2) + 1
const results = []
for (let i = 0; i < work.length; i += CHUNK) {
  const batch = work.slice(i, i + CHUNK)
  const r = await parallel(batch.map(w => () =>
    parallel(Array.from({ length: VOTES }, (_, v) => () =>
      agent(prompt(w.file, grid), { label: `qa:${w.tag.slice(0, 12)}:${w.foc}:${w.m}:v${v}`, phase: 'QA', schema: SCHEMA, model: 'sonnet', effort: 'low' })))
      .then(votes => {
        const real = {}, donut = {}
        for (const rv of votes.filter(Boolean)) {
          for (const c of (rv.real || [])) real[c] = (real[c] || 0) + 1
          for (const c of (rv.donut || [])) donut[c] = (donut[c] || 0) + 1
        }
        return { w,
          real: Object.keys(real).filter(c => real[c] >= NEED).map(Number),
          donut: Object.keys(donut).filter(c => donut[c] >= NEED).map(Number) }
      })))
  results.push(...r)
  if ((i / CHUNK) % 25 === 0) log(`QA progress: ${Math.min(i + CHUNK, work.length)}/${work.length} montages x${VOTES} votes`)
}
```

- [ ] **Step 2: Verify the script still parses**

Run: `node --check tools/golden/qa_workflow.js`
Expected: no output, exit 0.

If `node` is unavailable, run `python3 -c "print(open('tools/golden/qa_workflow.js').read().count('{') == open('tools/golden/qa_workflow.js').read().count('}'))"` and expect `True`, then rely on the Task 11 smoke run to exercise it.

- [ ] **Step 3: Confirm the file is LF-only**

Workflow scripts are rejected with a "control characters" error if they contain CRLF.

Run: `file tools/golden/qa_workflow.js`
Expected: no mention of "CRLF line terminators".

- [ ] **Step 4: Commit**

```bash
git add tools/golden/qa_workflow.js
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(golden): optional N-vote majority QA per montage

Two independent QA passes over the same LinwoodFocus frames agreed on only 62%
of confirmed stars. Auto-confirm was deterministic; QA is not, so the high tier
needs repeated votes now that it depends on QA."
```

---

## Task 7: `build_goldens.py` — three-state resolution and schema v2

**Files:**
- Modify: `tools/golden/build_goldens.py` (whole `main`, plus a new `resolve` function)
- Create: `tools/golden/tests/test_build_goldens.py`

- [ ] **Step 1: Write the failing test**

Create `tools/golden/tests/test_build_goldens.py`:

```python
"""Three-state resolution. A candidate the budget never reached is UNRESOLVED -- neither a star nor a
confirmed non-star -- so it biases neither recall nor precision."""
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import build_goldens as B  # noqa: E402


def c(snr, box=10):
    return {'x': 100.0, 'y': 200.0, 'bw': box, 'bh': box, 'snr': snr, 'area': box * box}


class Resolution(unittest.TestCase):

    def setUp(self):
        self.cands = [c(30.0), c(20.0), c(9.0), c(6.0), c(5.5)]

    def test_examined_and_confirmed_becomes_a_star(self):
        r = B.resolve(self.cands, auto=set(), qa={'confirmed': [2], 'examined': [2, 3]})
        self.assertEqual(r['confirmed'], {2})

    def test_examined_and_not_confirmed_is_rejected_not_unresolved(self):
        r = B.resolve(self.cands, auto=set(), qa={'confirmed': [2], 'examined': [2, 3]})
        self.assertEqual(r['rejected'], {3})
        self.assertNotIn(3, r['unresolved'])

    def test_never_examined_is_unresolved(self):
        r = B.resolve(self.cands, auto=set(), qa={'confirmed': [2], 'examined': [2, 3]})
        self.assertEqual(r['unresolved'], {0, 1, 4})

    def test_auto_confirmed_candidates_are_confirmed_without_qa(self):
        r = B.resolve(self.cands, auto={0, 1}, qa={'confirmed': [], 'examined': []})
        self.assertEqual(r['confirmed'], {0, 1})
        self.assertEqual(r['unresolved'], {2, 3, 4})

    def test_missing_qa_sidecar_leaves_everything_unexamined_unresolved(self):
        r = B.resolve(self.cands, auto={0}, qa=None)
        self.assertEqual(r['confirmed'], {0})
        self.assertEqual(r['rejected'], set())
        self.assertEqual(r['unresolved'], {1, 2, 3, 4})


class Coverage(unittest.TestCase):

    def test_coverage_is_reported_per_tier(self):
        cands = [c(30.0), c(20.0), c(9.0), c(6.0)]
        cov = B.coverage(cands, examined={0, 2}, auto={1})
        self.assertEqual(cov['high']['total'], 2)
        self.assertEqual(cov['high']['examined'], 2)   # index 0 QA'd, index 1 auto-confirmed
        self.assertEqual(cov['medium']['total'], 1)
        self.assertEqual(cov['medium']['examined'], 1)
        self.assertEqual(cov['low']['total'], 1)
        self.assertEqual(cov['low']['examined'], 0)


class Sidecar(unittest.TestCase):

    def test_schema_version_is_2_and_unresolved_is_emitted(self):
        cands = [c(30.0), c(9.0)]
        out = B.build_frame(cands, auto={0}, qa={'confirmed': [], 'examined': []},
                            image_file='x.fits', foc=1234, method='m', qa_votes=3,
                            qa_version='sonnet/golden-qa-v2')
        self.assertEqual(out['schemaVersion'], 2)
        self.assertEqual(len(out['stars']), 1)
        self.assertEqual(len(out['unresolved']), 1)
        self.assertEqual(out['qaVotes'], 3)
        self.assertEqual(out['stars'][0]['confidence'], 'high')

    def test_star_box_is_top_left_anchored(self):
        """Same box semantics as the review label boxes -- centroid minus half the extent."""
        out = B.build_frame([c(30.0, box=10)], auto={0}, qa=None, image_file='x.fits', foc=1,
                            method='m', qa_votes=1, qa_version='v')
        self.assertEqual((out['stars'][0]['x'], out['stars'][0]['y']), (95, 195))
        self.assertEqual((out['stars'][0]['w'], out['stars'][0]['h']), (10, 10))


if __name__ == '__main__':
    unittest.main()
```

- [ ] **Step 2: Run it to verify it fails**

Run: `python3 -m unittest tools.golden.tests.test_build_goldens -v`
Expected: FAIL — `AttributeError: module 'build_goldens' has no attribute 'resolve'`.

- [ ] **Step 3: Add the resolution functions**

In `tools/golden/build_goldens.py`, add below `tier()`:

```python
def resolve(cands, auto, qa):
    """Partition candidates into confirmed / rejected / unresolved.

    UNRESOLVED is the point: with a bounded montage budget most candidates are never looked at, and
    counting them as not-stars turns budget truncation into fake false positives (F11). They are
    excluded from both denominators instead.
    """
    auto = set(auto)
    examined = set(qa.get('examined', [])) if qa else set()
    qa_confirmed = set(qa.get('confirmed', [])) if qa else set()
    confirmed = auto | (qa_confirmed & examined) if examined else auto | qa_confirmed
    rejected = examined - confirmed
    unresolved = set(range(len(cands))) - confirmed - rejected
    return {'confirmed': confirmed, 'rejected': rejected, 'unresolved': unresolved}


def coverage(cands, examined, auto):
    """Per-tier examined/total, so a consumer can tell a precision measurement from a lower bound."""
    seen = set(examined) | set(auto)
    out = {'high': {'examined': 0, 'total': 0},
           'medium': {'examined': 0, 'total': 0},
           'low': {'examined': 0, 'total': 0}}
    for i, c in enumerate(cands):
        t = tier(c['snr'])
        out[t]['total'] += 1
        if i in seen:
            out[t]['examined'] += 1
    return out


def _box(c, confidence):
    return {'x': round(c['x'] - c['bw'] / 2), 'y': round(c['y'] - c['bh'] / 2),
            'w': c['bw'], 'h': c['bh'], 'confidence': confidence}


def build_frame(cands, auto, qa, image_file, foc, method, qa_votes, qa_version):
    """Assemble one schema-v2 <image>.golden.json payload."""
    r = resolve(cands, auto, qa)
    examined = set(qa.get('examined', [])) if qa else set()
    return {'imageFile': image_file, 'focuserPosition': foc, 'schemaVersion': 2, 'method': method,
            'stars': [_box(cands[i], tier(cands[i]['snr'])) for i in sorted(r['confirmed'])],
            'unresolved': [_box(cands[i], tier(cands[i]['snr'])) for i in sorted(r['unresolved'])],
            'coverage': coverage(cands, examined, auto),
            'qaVotes': qa_votes, 'qaVersion': qa_version}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `python3 -m unittest discover -s tools/golden/tests -v`
Expected: PASS, 41 tests.

- [ ] **Step 5: Rewrite `main` to use them**

In `tools/golden/build_goldens.py`, add the two new arguments after `--auto-confirm-snr`:

```python
    ap.add_argument('--prep-manifest', help='golden_prep manifest.json; supplies each frame\'s star '
                                            'scale and auto-confirmed count')
    ap.add_argument('--qa-votes', type=int, default=1)
    ap.add_argument('--qa-version', default='sonnet/golden-qa-v2')
```

and replace the per-frame loop body (from `cand = json.load(...)` to `total += len(stars)`) with:

```python
        cand = json.load(open(snr_path))
        scale = frame_star_scale(cand)
        donut_mode = any(c.get('src') == 'mf' or c.get('donut') for c in cand)
        auto = auto_confirmed_indices(cand, scale, donut_mode, args.auto_confirm_snr) if scale else set()
        qa = json.load(open(qa_path)) if os.path.exists(qa_path) else None
        if qa is None:
            print(f'  foc {foc}: no QA confirmations ({qa_path}); {len(auto)} auto-confirmed only')
        out = build_frame(cand, auto, qa, fn, foc, args.method, args.qa_votes, args.qa_version)
        json.dump(out, open(os.path.join(args.run_dir, fn + '.golden.json'), 'w'), indent=1)
        stars = out['stars']
        hi = sum(1 for s in stars if s['confidence'] == 'high')
        summary.append((foc, len(cand), len(stars), hi, len(out['unresolved'])))
        total += len(stars)
```

Add `from plausibility import frame_star_scale` and `from golden_prep import auto_confirmed_indices` to the imports, plus `sys.path.insert(0, os.path.dirname(__file__))` before them. Update the summary print to include the unresolved column:

```python
    print(f'{"foc":>7}  {"cand":>6}  {"golden":>6}  {"high":>6}  unresolved')
    for foc, c, g, h, u in summary:
        print(f'{foc:>7}  {c:>6}  {g:>6}  {h:>6}  {u}')
```

- [ ] **Step 6: Run the full Python suite**

Run: `python3 -m unittest discover -s tools/golden/tests -v`
Expected: PASS, 41 tests.

- [ ] **Step 7: Commit**

```bash
git add tools/golden/build_goldens.py tools/golden/tests/test_build_goldens.py
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(golden): three-state resolution and schema v2 sidecars

Candidates the montage budget never reached are UNRESOLVED rather than
not-stars, and per-tier coverage is recorded so a precision figure can be told
apart from a lower bound (closes the recording half of F11)."
```

---

## Task 8: C# schema v2 POCOs

**Files:**
- Modify: `Joko.NINA.Plugins/TestApp/GoldenStarSet.cs:53-65`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Golden/GoldenStarSetTests.cs`

- [ ] **Step 1: Write the failing test**

Append to the test fixture class in `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Golden/GoldenStarSetTests.cs`:

```csharp
        [Test]
        public void Deserialize_SchemaV1_LeavesUnresolvedNull() {
            // Backward compatibility is load-bearing: 13 untouched runs must score bit-identically.
            var json = @"{""imageFile"":""a.fits"",""focuserPosition"":100,""schemaVersion"":1,
                          ""stars"":[{""x"":1,""y"":2,""w"":3,""h"":4,""confidence"":""high""}]}";
            var frame = GoldenStarSetStore.Deserialize(json);
            Assert.Multiple(() => {
                Assert.That(frame.SchemaVersion, Is.EqualTo(1));
                Assert.That(frame.Stars, Has.Count.EqualTo(1));
                Assert.That(frame.Unresolved, Is.Null);
                Assert.That(frame.Coverage, Is.Null);
            });
        }

        [Test]
        public void Deserialize_SchemaV2_ReadsUnresolvedAndCoverage() {
            var json = @"{""imageFile"":""a.fits"",""focuserPosition"":100,""schemaVersion"":2,
                          ""stars"":[{""x"":1,""y"":2,""w"":3,""h"":4,""confidence"":""high""}],
                          ""unresolved"":[{""x"":9,""y"":9,""w"":36,""h"":36,""confidence"":""medium""}],
                          ""coverage"":{""high"":{""examined"":10,""total"":20}},
                          ""qaVotes"":3,""qaVersion"":""sonnet/golden-qa-v2""}";
            var frame = GoldenStarSetStore.Deserialize(json);
            Assert.Multiple(() => {
                Assert.That(frame.SchemaVersion, Is.EqualTo(2));
                Assert.That(frame.Unresolved, Has.Count.EqualTo(1));
                Assert.That(frame.Unresolved[0].W, Is.EqualTo(36.0));
                Assert.That(frame.Coverage["high"].Examined, Is.EqualTo(10));
                Assert.That(frame.Coverage["high"].Fraction, Is.EqualTo(0.5));
                Assert.That(frame.QaVotes, Is.EqualTo(3));
                Assert.That(frame.QaVersion, Is.EqualTo("sonnet/golden-qa-v2"));
            });
        }

        [Test]
        public void Serialize_OmitsUnresolvedWhenAbsent() {
            var frame = new GoldenFrame { ImageFile = "a.fits", FocuserPosition = 100 };
            Assert.That(GoldenStarSetStore.Serialize(frame), Does.Not.Contain("unresolved"));
        }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~GoldenStarSet"`
Expected: FAIL to compile — `'GoldenFrame' does not contain a definition for 'Unresolved'`.

- [ ] **Step 3: Add the POCO members**

In `Joko.NINA.Plugins/TestApp/GoldenStarSet.cs`, add above `GoldenFrame`:

```csharp
    /// <summary>How much of one SNR tier was actually examined. Lets a consumer tell a precision
    /// MEASUREMENT from a lower bound: with a bounded montage budget, most of the faint tier on a deep
    /// field is never looked at.</summary>
    public sealed class GoldenTierCoverage {
        [JsonProperty("examined")] public int Examined { get; set; }
        [JsonProperty("total")] public int Total { get; set; }

        [JsonIgnore] public double Fraction => Total > 0 ? Examined / (double)Total : 0.0;
    }
```

and inside `GoldenFrame`, after `CoveredTiles`:

```csharp
        /// <summary>Candidates the QA budget never reached — NEITHER stars nor confirmed non-stars.
        /// A detection landing on one of these is excluded from the false-positive count, and these
        /// boxes are never counted as missed stars. Absent (schema v1) ⇒ empty, so every pre-v2 golden
        /// scores exactly as it did before this field existed.</summary>
        [JsonProperty("unresolved", NullValueHandling = NullValueHandling.Ignore)]
        public List<GoldenStarBox> Unresolved { get; set; }

        /// <summary>Per-tier ("high"|"medium"|"low") examined/total counts.</summary>
        [JsonProperty("coverage", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, GoldenTierCoverage> Coverage { get; set; }

        /// <summary>Independent LLM votes per candidate (majority-confirmed). 1 or absent ⇒ single vote,
        /// which is NOT reproducible: two passes over the same defocused frames agreed on 62% of
        /// confirmed stars.</summary>
        [JsonProperty("qaVotes", NullValueHandling = NullValueHandling.Ignore)]
        public int? QaVotes { get; set; }

        /// <summary>Model + prompt revision that produced the QA verdicts, so a reproducibility
        /// regression is attributable.</summary>
        [JsonProperty("qaVersion", NullValueHandling = NullValueHandling.Ignore)]
        public string QaVersion { get; set; }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~GoldenStarSet"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/TestApp/GoldenStarSet.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Golden/GoldenStarSetTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(golden): schema v2 POCOs for unresolved, coverage and QA provenance

Absent unresolved reads as empty, so every v1 sidecar scores unchanged."
```

---

## Task 9: `golden eval` excludes unresolved boxes from the false-positive count

The recall denominator needs no change — unresolved candidates are simply not in `stars[]`, so they are never counted as missed. Only precision needs work: a detection landing on an unexamined candidate is currently a false positive, which is exactly the F11 artifact.

**Files:**
- Modify: `Joko.NINA.Plugins/TestApp/GoldenGeometry.cs` (the helper — see the note below)
- Modify: `Joko.NINA.Plugins/TestApp/GoldenEvalRunner.cs:194-208` (call site)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Golden/GoldenGeometryTests.cs`

**Why the helper goes in `GoldenGeometry.cs`, not `GoldenEvalRunner.cs`:** the test project source-links only
`GoldenStarSet.cs`, `GoldenGeometry.cs`, `OptimizationRunDiscovery.cs`, `StarReviewQueue.cs` and
`BankVerification.cs` (see `Joko.NINA.Plugins.HocusFocus.Tests.csproj:23-27`). `GoldenEvalRunner.cs` cannot be
linked — it pulls in NINA and OpenCV via `DiagnosticUtil.LoadRenderedImage` and `IStarDetector`. Putting the
helper there would make it untestable. `GoldenGeometry.cs` already owns `GoldenMatch`, `DetBox` and the
`BoxMatcher` interop, so it is the right home anyway.

- [ ] **Step 1: Write the failing test**

Append to the fixture class in `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Golden/GoldenGeometryTests.cs`:

```csharp
        [Test]
        public void DetectionOnUnresolvedBox_IsNotAFalsePositive() {
            var unresolved = new List<RectD> { new RectD(100, 100, 20, 20) };
            var det = new List<DetBox> {
                new DetBox(new RectD(102, 102, 16, 16), 110, 110),  // inside an unresolved box
                new DetBox(new RectD(500, 500, 10, 10), 505, 505)   // genuinely spurious
            };
            var kept = GoldenMatch.ExcludeUnresolved(new List<int> { 0, 1 }, det, unresolved);
            Assert.That(kept, Is.EqualTo(new List<int> { 1 }));
        }

        [Test]
        public void ExcludeUnresolved_WithNoUnresolvedBoxes_KeepsEveryFalsePositive() {
            var det = new List<DetBox> { new DetBox(new RectD(0, 0, 10, 10), 5, 5) };
            Assert.That(GoldenMatch.ExcludeUnresolved(new List<int> { 0 }, det, null),
                        Is.EqualTo(new List<int> { 0 }));
        }

        [Test]
        public void ExcludeUnresolved_MatchesOnOverlapNotOnlyCentre() {
            // A detection whose centre is outside the unresolved box but which overlaps it is still unjudged.
            var unresolved = new List<RectD> { new RectD(100, 100, 20, 20) };
            var det = new List<DetBox> { new DetBox(new RectD(115, 115, 20, 20), 125, 125) };
            Assert.That(GoldenMatch.ExcludeUnresolved(new List<int> { 0 }, det, unresolved), Is.Empty);
        }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~GoldenGeometry"`
Expected: FAIL to compile — `'GoldenMatch' does not contain a definition for 'ExcludeUnresolved'`.

- [ ] **Step 3: Add the helper to `GoldenGeometry.cs`**

In `Joko.NINA.Plugins/TestApp/GoldenGeometry.cs`, add to the `GoldenMatch` static class:

```csharp
        /// <summary>Drops false positives that land on an UNRESOLVED golden box. Those candidates were
        /// never examined by QA, so a detection there is unjudged rather than wrong — counting it as a
        /// false positive is what turned montage-budget truncation into thousands of fake FPs (F11).
        /// A null/empty unresolved list is the schema-v1 case and must be a no-op.</summary>
        public static List<int> ExcludeUnresolved(
                IReadOnlyList<int> falsePositives, IReadOnlyList<DetBox> detected, IReadOnlyList<RectD> unresolved) {
            if (unresolved == null || unresolved.Count == 0) {
                return falsePositives.ToList();
            }
            return falsePositives.Where(di => unresolved.All(u => !Covers(u, detected[di]))).ToList();
        }

        private static bool Covers(RectD box, DetBox det) =>
            (det.Cx >= box.X && det.Cx <= box.X + box.W && det.Cy >= box.Y && det.Cy <= box.Y + box.H)
            || BoxMatcher.IoU(box, det.Box) > 0.0;
```

Ensure `GoldenGeometry.cs` has `using System.Linq;` at the top; add it if absent.

- [ ] **Step 3b: Wire it into the eval runner**

In `Joko.NINA.Plugins/TestApp/GoldenEvalRunner.cs`, replace lines 194-204 with:

```csharp
                var goldenRects = gf.Stars.Select(b => new RectD(b.X, b.Y, b.W, b.H)).ToList();
                var unresolvedRects = (gf.Unresolved ?? new List<GoldenStarBox>())
                    .Select(b => new RectD(b.X, b.Y, b.W, b.H)).ToList();
                var match = GoldenMatch.Match(goldenRects, det, matchMode, tau, matchRadius);
                var falsePositives = GoldenMatch.ExcludeUnresolved(match.FalsePositives, det, unresolvedRects);

                var fe = new FrameEval {
                    FocuserPosition = frame.FocuserPosition,
                    ParamsLabel = paramsLabel,
                    GoldenCount = gf.Stars.Count,
                    Accepted = stars.Count,
                    TP = match.Pairs.Count,
                    FP = falsePositives.Count,
                    FN = match.FalseNegatives.Count,
```

Then at line 244, replace `var fp = match.FalsePositives.Count(di => dIdx.Contains(di));` with:

```csharp
                    var fp = falsePositives.Count(di => dIdx.Contains(di));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~Golden"`
Expected: PASS.

- [ ] **Step 5: Run the whole C# suite**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: PASS. `SendAsync_WritesOnABackgroundThread` is a known unrelated flake; re-run that fixture alone if it trips.

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/TestApp/GoldenEvalRunner.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Golden/GoldenGeometryTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(golden): detections on unresolved boxes are not false positives

Recall needs no change (unresolved boxes are not in stars[]); precision does."
```

---

## Task 10: Verify no regression on a sound run, and that v1 goldens score unchanged

Acceptance criteria 2 and 4. `vsn07` is not in the rebuild scope but *is* affected by the narrowed auto-confirm, so it is prepped into scratch and diffed rather than overwritten.

**Files:** none modified — this is a verification gate.

- [ ] **Step 1: Capture the current `vsn07` score**

```bash
dotnet.exe run --project Joko.NINA.Plugins/TestApp -- golden eval \
  --runs "D:\Autofocus Bank\vsn07" --params current --match centroid --match-radius 12 \
  > /tmp/vsn07_before.txt
```
Record `recall@high`.

- [ ] **Step 2: Prep `vsn07` under the new pipeline into scratch**

```bash
python3 tools/golden/golden_prep.py \
  --run-dir "/mnt/d/Autofocus Bank/vsn07/AutoFocus_20260714_213204/attempt01" \
  --out /tmp/vsn07_newprep --budget-montages 20
```
Expected: per-frame `scale` between 6 and 30 px, no `ScaleCollapsed`.

- [ ] **Step 3: Check the auto-confirm delta**

```bash
python3 - <<'PY'
import json
m = json.load(open('/tmp/vsn07_newprep/manifest.json'))
for f in m['frames']:
    keep = f['autoConfirmed'] / max(f['high'], 1)
    print(f"foc {f['foc']}: scale {f['starScale']:.0f}px  auto {f['autoConfirmed']}/{f['high']} = {keep:.2%}")
    assert keep >= 0.85, f"auto-confirm dropped below the 85% bar on foc {f['foc']}"
print('OK: narrowed auto-confirm retains >=85% of the high tier on every frame')
PY
```
Expected: `OK`. If any frame fails, the `0.3` ratio in `plausibility.MIN_RATIO_DEFAULT` is too aggressive for at-focus frames — do **not** special-case `vsn07`; re-derive the ratio and record the new evidence in the design doc.

- [ ] **Step 4: Confirm v1 goldens score bit-identically**

Re-run step 1 and diff:
```bash
dotnet.exe run --project Joko.NINA.Plugins/TestApp -- golden eval \
  --runs "D:\Autofocus Bank\vsn07" --params current --match centroid --match-radius 12 \
  > /tmp/vsn07_after.txt
diff /tmp/vsn07_before.txt /tmp/vsn07_after.txt && echo "IDENTICAL"
```
Expected: `IDENTICAL`. `vsn07`'s shipped sidecar is still v1 with no `unresolved`, so the eval change must be a no-op on it.

- [ ] **Step 5: Commit the evidence**

```bash
git add docs/golden-tier-plausibility-design.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(golden): record the vsn07 no-regression and v1-invariance results" --allow-empty
```

---

## Task 11: Smoke-test the full pipeline on the smallest donut run

`LinwoodFocus` is the cheapest donut-aware run (865 high-tier candidates, ~3 min of single-vote QA) and is known-contaminated, so it is the right first end-to-end target.

**Files:** none modified — operational.

- [ ] **Step 1: Prep**

```bash
python3 tools/golden/golden_prep.py \
  --run-dir "/mnt/d/Autofocus Bank/LinwoodFocus/AutoFocus_20220329_211008/attempt01" \
  --out /tmp/golden_gen4/LinwoodFocus --donut --budget-montages 60
```
Expected: no `ScaleCollapsed`; per-frame `autoConfirmed` is **0** on every frame (donut mode auto-confirms nothing).

- [ ] **Step 2: Build the worklist**

```bash
python3 tools/golden/build_qa_worklist.py --scratch-root /tmp/golden_gen4 --out /tmp/golden_gen4/worklist.json
```
Expected: prints the montage total; each frame entry has `foc` and `n` and **no** `b`.

- [ ] **Step 3: Run QA with 3 votes**

Invoke the `Workflow` tool with `scriptPath: tools/golden/qa_workflow.js` and `args` = the contents of `/tmp/golden_gen4/worklist.json` with `"votes": 3` added. Save the returned `byKey` to `/tmp/golden_gen4/bykey.json`.

- [ ] **Step 4: Persist and build**

```bash
python3 tools/golden/persist_qa.py --bykey /tmp/golden_gen4/bykey.json --scratch-root /tmp/golden_gen4
python3 tools/golden/build_goldens.py \
  --run-dir "/mnt/d/Autofocus Bank/LinwoodFocus/AutoFocus_20220329_211008/attempt01" \
  --snr-dir /tmp/golden_gen4/LinwoodFocus --qa-dir /tmp/golden_gen4/LinwoodFocus \
  --method "SNR-ref(k5,donut)+plausibility+montageQA(3v)" --qa-votes 3
```

Back up the old sidecars first:
```bash
mkdir -p "/mnt/d/Autofocus Bank/_prior_reports/LinwoodFocus_pre_plausibility_20260730"
cp "/mnt/d/Autofocus Bank/LinwoodFocus/AutoFocus_20220329_211008/attempt01/"*.golden.json \
   "/mnt/d/Autofocus Bank/_prior_reports/LinwoodFocus_pre_plausibility_20260730/"
```

- [ ] **Step 5: Validate the result**

```bash
python3 tools/golden/golden_health.py --run-dir "/mnt/d/Autofocus Bank/LinwoodFocus"
```
Expected: **no** `TIER-INVERSION` flag, and a `widthRatio` at or above 1.0 (it was 0.71). If it is still inverted, stop and diagnose before touching the remaining five runs.

- [ ] **Step 6: Commit the run record**

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --allow-empty --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "chore(golden): rebuild LinwoodFocus under plausibility tiering

Was TIER-INVERSION at widthRatio 0.71."
```

---

## Task 12: Rebuild the remaining five donut-aware runs

Order is cheapest-first so a systematic problem surfaces before the expensive runs. Budget from design §7: `mufti` ~48 min, `Panos` ~63 min, `FlyData` ~33 min at 3 votes; `SorenVance` ~4.4 hr and `lumos` ~8.4 hr at 3 votes, or ~88/169 min at 1 vote.

**Files:** none modified — operational.

- [ ] **Step 1: `FlyData` (the regression control — it was already sound)**

Repeat Task 11's steps 1-5 with `--run-dir "/mnt/d/Autofocus Bank/FlyData/AutoFocus_20260607_030642/attempt01"`, `--out /tmp/golden_gen4/FlyData`, `--qa-votes 3`.

Then check acceptance criterion 2:
```bash
python3 - <<'PY'
import glob, json
old = sum(len([s for s in json.load(open(p))['stars'] if s['confidence'] == 'high'])
          for p in glob.glob('/mnt/d/Autofocus Bank/_prior_reports/FlyData_*_donutaware_20260730/*.golden.json'))
new = sum(len([s for s in json.load(open(p))['stars'] if s['confidence'] == 'high'])
          for p in glob.glob('/mnt/d/Autofocus Bank/FlyData/**/*.golden.json', recursive=True))
print(f'high tier: {old} -> {new} ({new/old:.1%})')
assert new / old >= 0.85, 'regression on a run that was already sound'
PY
```
Expected: `>= 85%`.

- [ ] **Step 2: `mufti`**

Same procedure, `--run-dir "/mnt/d/Autofocus Bank/mufti/AutoFocus_20260607_030642-20260615T214722Z-3-001/attempt01"`, `--qa-votes 3`.

- [ ] **Step 3: `Panos`**

Same procedure, `--run-dir "/mnt/d/Autofocus Bank/Panos/attempt01"`, `--qa-votes 3`.

- [ ] **Step 4: `SorenVance`**

Same procedure, `--run-dir "/mnt/d/Autofocus Bank/SorenVance/AutoFocus_20260711_014141/attempt01"`. Use `--qa-votes 1` unless the 4.4 hr cost is acceptable; whichever is chosen is recorded in the sidecar's `qaVotes`.

Restore point: its quarantined golden is at `_prior_reports/SorenVance_BAD_donut_overdetect_20260730/`.

- [ ] **Step 5: `lumos`**

Same procedure, `--run-dir "/mnt/d/Autofocus Bank/lumos/AutoFocus_20260708_231255/attempt01"`, `--qa-votes 1` (3 votes is ~8.4 hr).

- [ ] **Step 6: Validate the whole bank**

```bash
python3 tools/golden/golden_health.py --bank "/mnt/d/Autofocus Bank" --json /tmp/golden_health_after.json
```
Expected: zero flagged runs. Specifically, `lumos` and `SorenVance` must show `<10%` of the high tier at `<=4px` and a `widthRatio >= 1.0` (acceptance criterion 3).

- [ ] **Step 7: Score the two runs that could never be scored**

```bash
dotnet.exe run --project Joko.NINA.Plugins/TestApp -- golden eval \
  --runs "D:\Autofocus Bank\lumos" --params current --match centroid --match-radius 12
dotnet.exe run --project Joko.NINA.Plugins/TestApp -- golden eval \
  --runs "D:\Autofocus Bank\SorenVance" --params current --match centroid --match-radius 12
```
Expected: real numbers rather than NaN, and `recall@high` plausible against HocusFocus's 8-813 stars/frame on `lumos` — not the 0.013 the broken reference produced.

---

## Task 13: Update the docs

**Files:**
- Modify: `.claude/docs/golden-star-set.md`
- Modify: `tools/golden/README.md`
- Modify: `docs/followups.md` (F11 and F16 status)

- [ ] **Step 1: Update the method doc**

In `.claude/docs/golden-star-set.md`, replace the step-3 bullet ("**Golden = QA-confirmed candidates**…") with:

```markdown
3. **Golden = QA-confirmed candidates**, each carrying its **SNR as the confidence tier** (objective:
   SNR≥12 high, 8–12 medium, 5–8 low). Candidates the montage budget never reached are **unresolved**, not
   rejected, and are excluded from both the recall and precision denominators.
   **Significance alone never establishes that a candidate is a star.** On a heavily-defocused run a 3 px
   noise spike at 12σ peak is more significant than a 36 px donut, so auto-confirming on SNR keeps the noise
   and discards the stars. Auto-confirm therefore also requires **plausibility** — the candidate's size
   relative to the frame's own star scale — and is disabled outright when `--donut` is set. Integrated SNR
   does **not** fix this and was measured; see `docs/golden-tier-plausibility-design.md` §2.
```

Replace the "**Donut caveat:**" paragraph's final sentence with:

```markdown
Near-focus and moderately-defocused frames are reliable as-is. Validate any golden with
`tools/golden/golden_health.py --run-dir <run>` before trusting its numbers — it reads the stored sidecars
alone and flags size collapse, tier inversion and a non-responsive width-vs-focus curve.
```

- [ ] **Step 2: Update the tools README**

In `tools/golden/README.md`, replace the numbered pipeline list's item 4 and add items 5-6:

```markdown
4. **`golden_prep.py`** — runs the reference over each frame, estimates the frame's **star scale** (median box
   of the top-flux candidates over the CC+MF union), and renders the QA worklist in **plausibility order** so a
   bounded montage budget lands on candidates that could be stars. Auto-confirms nothing when `--donut` is set.
   Aborts if the scale estimate collapses to the pixel scale, which is what a missing `--donut` looks like.

5. **`build_goldens.py`** — writes schema-v2 `<image>.golden.json` sidecars: `stars[]` (confirmed),
   `unresolved[]` (never examined — excluded from both denominators), and per-tier `coverage`.

6. **`golden_health.py`** — validates a golden from its sidecars alone (no FITS, no LLM). Run it after any
   rebuild and before quoting any number.
```

- [ ] **Step 3: Update the follow-ups register**

In `docs/followups.md`, change F16's status line to:

```markdown
**Status:** Fixed — `docs/golden-tier-plausibility-design.md` / `plans/golden-tier-plausibility-plan.md`
```

and add to F11, after the "Independently of any re-run" paragraph:

```markdown
**The coverage-recording half of this is done**: schema-v2 goldens carry per-tier `examined`/`total`, and
candidates the budget never reached are `unresolved` and excluded from both denominators rather than counted as
false positives. The remaining work here is purely the larger `--budget-montages` re-runs.
```

- [ ] **Step 4: Run both suites one final time**

```bash
python3 -m unittest discover -s tools/golden/tests -v
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```
Expected: both PASS.

- [ ] **Step 5: Commit**

```bash
git add .claude/docs/golden-star-set.md tools/golden/README.md docs/followups.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(golden): record plausibility tiering in the method doc and close F16"
```

- [ ] **Step 6: Open the PR**

```bash
git push -u origin ghilios/golden-tier-plausibility
gh pr create --base develop --title "Golden tier plausibility: fix the SNR>=12 auto-confirm inversion (F16)" \
  --body "$(cat <<'EOF'
Fixes F16. The golden reference's SNR>=12 auto-confirm gate inverted on heavily-defocused runs, keeping compact noise spikes and discarding real donuts, which made `lumos` and `SorenVance` unscoreable and left `LinwoodFocus` silently measuring the wrong star population.

Integrated SNR — the fix F16 originally proposed — was measured and rejected: it is marginally worse. A 3px spike at 12σ peak genuinely is more significant than a 36px donut, so no significance-based statistic can separate them. See `docs/golden-tier-plausibility-design.md` §2.

Instead: the tier definition is unchanged (so `recall@SNR≥12` keeps its meaning), a frame-relative plausibility measure reorders the QA worklist, auto-confirm is narrowed on normal runs and removed on `--donut` runs, and unresolved candidates are excluded from both denominators.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01C7R7TUBTvPt5R44CWhu2HT
EOF
)"
```

---

## Self-Review Notes

**Spec coverage:** §4.1 → Task 3; §4.2 → Task 1; §4.3 → Tasks 2 and 4; §4.4 → Task 5; §4.5 → Task 7; §4.6 → Tasks 8-9; §5 → Task 10 (v1 invariance) and Task 12; §6 → Task 6; §9 criteria 1-6 → Tasks 3, 10, 12, 10, 4, 13.

**Deliberately deferred** (design §10): scaling `donut_radii` to the run's defocus, and the F11 `--budget-montages` re-runs.
