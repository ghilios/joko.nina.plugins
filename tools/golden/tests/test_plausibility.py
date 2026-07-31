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


class PlausibilitySaturates(unittest.TestCase):
    """Oversized is not more plausible than right-sized. Without the cap, 36px matched-filter noise
    outranks the real 13px stars on a near-focus frame -- measured: 719 of 720 crops were noise."""

    def test_candidate_at_the_scale_scores_one(self):
        self.assertEqual(P.plausibility(cc(13, 100.0), scale=13.0), 1.0)

    def test_oversized_candidate_does_not_outrank_a_right_sized_one(self):
        self.assertEqual(P.plausibility(mf(36, 100.0), scale=13.0),
                         P.plausibility(cc(13, 100.0), scale=13.0))

    def test_undersized_is_still_penalised_proportionally(self):
        self.assertAlmostEqual(P.plausibility(cc(3, 20.0), scale=36.0), 3 / 36)

    def test_capping_does_not_change_the_auto_confirm_gate(self):
        """Everything the cap clips was already well above the 0.3 threshold."""
        self.assertTrue(P.is_plausible(cc(51, 900.0), scale=13.0))
        self.assertTrue(P.is_plausible(cc(13, 900.0), scale=13.0))
        self.assertFalse(P.is_plausible(cc(3, 20.0), scale=36.0))


class QaOrder(unittest.TestCase):

    def test_plausible_candidates_are_queued_before_significant_spikes(self):
        cands = [cc(3, 20.0, snr=40.0), mf(36, 400.0, snr=8.0), cc(3, 19.0, snr=30.0)]
        self.assertEqual(P.qa_order(cands, scale=36.0)[0], 1)

    def test_ties_break_on_snr_descending_within_a_path(self):
        cands = [mf(36, 100.0, snr=8.0), mf(36, 100.0, snr=20.0)]
        self.assertEqual(P.qa_order(cands, scale=36.0), [1, 0])

    def test_order_is_a_permutation_of_all_indices(self):
        cands = [cc(3, 20.0), mf(36, 400.0), cc(7, 90.0)]
        self.assertEqual(sorted(P.qa_order(cands, scale=36.0)), [0, 1, 2])

    def test_paths_are_interleaved_so_neither_monopolises_the_budget(self):
        """The matched filter emits ~10x more candidates than the CC path; a single merged ranking lets
        it take the whole budget, so QA never sees a real compact star near focus."""
        cands = [cc(13, 500.0, snr=30.0)] + [mf(36, 400.0, snr=15.0 - i * 0.01) for i in range(50)]
        order = P.qa_order(cands, scale=13.0)
        self.assertEqual(order[0], 0, 'the sole CC candidate must not be buried behind 50 MF ones')
        self.assertEqual(len([i for i in order[:10] if cands[i].get('src') == 'mf']), 9)

    def test_interleaving_alternates_while_both_paths_have_candidates(self):
        cands = [cc(10, 100.0, snr=30.0), cc(10, 100.0, snr=20.0),
                 mf(10, 100.0, snr=30.0), mf(10, 100.0, snr=20.0)]
        self.assertEqual(P.qa_order(cands, scale=10.0), [0, 2, 1, 3])


if __name__ == '__main__':
    unittest.main()
