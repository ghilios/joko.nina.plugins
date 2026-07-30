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
