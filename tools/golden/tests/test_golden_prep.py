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
                 cc(2, 30.0, 20.0),     # significant but pixel-scale -> QA
                 cc(10, 6.0, 40.0)]     # full-size but insignificant -> QA
        got = G.auto_confirmed_indices(cands, scale=10.0, donut=False, threshold=12.0)
        self.assertEqual(got, {0})

    def test_compact_star_at_focus_is_still_auto_confirmed(self):
        """A 4px star on a 10px-scale frame is a real star, not a spike."""
        cands = [cc(4, 20.0, 200.0)]
        self.assertEqual(G.auto_confirmed_indices(cands, scale=10.0, donut=False, threshold=12.0), {0})

    def test_the_ratio_threshold_is_inclusive(self):
        """box == 0.3*S is KEPT. On vsn07 (scale 10px) that is the 3px real stars at focus, which the
        measured 87.8% retention depends on -- an exclusive bound would drop them."""
        cands = [cc(3, 20.0, 200.0)]
        self.assertEqual(G.auto_confirmed_indices(cands, scale=10.0, donut=False, threshold=12.0), {0})


class WorklistOrder(unittest.TestCase):

    def test_donut_mode_queues_every_candidate_including_the_high_tier(self):
        cands = [cc(3, 40.0, 20.0), mf(36, 8.0, 400.0)]
        order = G.qa_worklist(cands, scale=36.0, donut=True, threshold=12.0)
        self.assertEqual(sorted(order), [0, 1])
        self.assertEqual(order[0], 1, 'the donut must be queued before the spike')

    def test_normal_mode_excludes_auto_confirmed_candidates_from_the_worklist(self):
        cands = [cc(10, 30.0, 300.0), cc(2, 30.0, 20.0)]
        self.assertEqual(G.qa_worklist(cands, scale=10.0, donut=False, threshold=12.0), [1])


if __name__ == '__main__':
    unittest.main()
