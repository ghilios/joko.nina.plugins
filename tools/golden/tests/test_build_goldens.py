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
