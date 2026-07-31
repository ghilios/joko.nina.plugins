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
