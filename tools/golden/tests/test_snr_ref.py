"""snr_ref must emit a flux measure comparable across the connected-component and matched-filter
paths, and must say which path produced each candidate. See docs/golden-tier-plausibility-design.md 4.2."""
import os
import sys
import unittest

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import snr_ref  # noqa: E402


def _synthetic_frame():
    """A 512x512 frame: flat background 200, sigma ~10, one bright compact star and one faint wide disk."""
    rng = np.random.default_rng(1234)
    img = 200.0 + rng.normal(0.0, 10.0, size=(512, 512))
    yy, xx = np.mgrid[0:512, 0:512]
    img += 4000.0 * np.exp(-(((xx - 100) ** 2 + (yy - 100) ** 2) / (2 * 1.5 ** 2)))
    disk = ((xx - 350) ** 2 + (yy - 350) ** 2) <= 14 ** 2
    img[disk] += 45.0
    return np.clip(img, 0, 65535).astype(np.float32)


class SnrRefCandidateFields(unittest.TestCase):

    def setUp(self):
        self.img = _synthetic_frame()

    def _detect(self, **kw):
        bg, sig = snr_ref.coarse_bg(self.img)
        return snr_ref.detect_from_arrays(self.img, bg, sig, **kw)

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
        """The whole point: both paths must expose one comparable integrated-flux measure."""
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
