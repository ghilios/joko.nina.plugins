#!/usr/bin/env python
"""Bridge golden_prep -> qa_workflow: assemble a COMPACT QA worklist (Workflow `args`) from per-run prep manifests.

The workflow returns montage CELL POSITIONS, not global candidate indices: golden_prep renders the QA worklist in
PLAUSIBILITY order, which is not a contiguous prefix of snr_<foc>.json, so no arithmetic can recover a global index
from a cell. persist_qa.py maps position -> global index through qaorder_<foc>.json. That keeps the worklist small
enough to pass inline as Workflow args while supporting an arbitrary ordering. The workflow reconstructs montage
paths from <base>/<tag>/f<foc>/montage_<m>.png.

Emits: { base, grid, runs:[ { tag, frames:[ { foc, n (montages to QA, capped) } ] } ] }

Usage:
  build_qa_worklist.py --scratch-root <dir> --out worklist.json [--grid 6] [--max-montages-per-frame N]
"""
import os, json, argparse


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--scratch-root', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--grid', type=int, default=6)
    ap.add_argument('--max-montages-per-frame', type=int, default=0, help='0 = no cap')
    args = ap.parse_args()

    runs = []
    total_montages = 0
    for entry in sorted(os.listdir(args.scratch_root)):
        man_path = os.path.join(args.scratch_root, entry, 'manifest.json')
        if not os.path.isfile(man_path):
            continue
        man = json.load(open(man_path))
        frames = []
        for fr in man['frames']:
            n = fr['montageCount']
            if args.max_montages_per_frame > 0:
                n = min(n, args.max_montages_per_frame)
            if n <= 0:
                continue
            frames.append({'foc': fr['foc'], 'n': n})
            total_montages += n
        if frames:
            runs.append({'tag': entry, 'frames': frames})

    json.dump({'base': os.path.abspath(args.scratch_root), 'grid': args.grid, 'runs': runs}, open(args.out, 'w'))
    print(f'{total_montages} montages to QA across {len(runs)} run(s) -> {args.out}')


if __name__ == '__main__':
    main()
