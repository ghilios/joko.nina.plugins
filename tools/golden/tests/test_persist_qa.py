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


class ExaminedReflectsMontagesActuallyQad(unittest.TestCase):
    """Coverage must never be overstated: if the worklist is capped below the rendered montage count, only
    the montages the workflow completed count as examined."""

    def setUp(self):
        self.root = tempfile.mkdtemp()
        self.run = os.path.join(self.root, 'myrun')
        os.makedirs(self.run)
        # 3 montages' worth of worklist at grid=2 (4 cells each) = 12 candidates.
        json.dump(list(range(100, 112)), open(os.path.join(self.run, 'qaorder_7.json'), 'w'))

    def test_capped_worklist_only_marks_completed_montages_examined(self):
        PQ.persist(self.root, {'myrun 7': {'real': [], 'donut': [], 'montages': 2}}, grid=2)
        got = json.load(open(os.path.join(self.run, 'qa_7.json')))
        self.assertEqual(got['examined'], list(range(100, 108)), 'only 2 montages x 4 cells were examined')

    def test_absent_montage_count_falls_back_to_the_whole_worklist(self):
        PQ.persist(self.root, {'myrun 7': {'real': [], 'donut': []}}, grid=2)
        got = json.load(open(os.path.join(self.run, 'qa_7.json')))
        self.assertEqual(got['examined'], list(range(100, 112)))


if __name__ == '__main__':
    unittest.main()
