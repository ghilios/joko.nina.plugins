#!/usr/bin/env python
"""Bridge golden_prep -> qa_workflow: assemble a COMPACT QA worklist (Workflow `args`) from per-run prep manifests.

golden_prep renders the uncertain-tier montages CONTIGUOUSLY (SNR-desc), so a montage cell's GLOBAL index into the
full snr_<foc>.json is simply  high_count + montageIndex*grid^2 + cell  (high_count = the frame's auto-confirmed
SNR>=12 tier size, where the uncertain tier begins). That means the whole bank's QA fan-out needs only a few ints
per frame — not every montage path — so the worklist stays small enough to pass inline as Workflow args. The
workflow reconstructs montage paths from <base>/<tag>/f<foc>/montage_<m>.png and maps cells via the base index.

Emits: { base, grid, runs:[ { tag, frames:[ { foc, n (montages to QA, capped), b (high_count base index) } ] } ] }

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
            frames.append({'foc': fr['foc'], 'n': n, 'b': fr['high']})
            total_montages += n
        if frames:
            runs.append({'tag': entry, 'frames': frames})

    json.dump({'base': os.path.abspath(args.scratch_root), 'grid': args.grid, 'runs': runs}, open(args.out, 'w'))
    print(f'{total_montages} montages to QA across {len(runs)} run(s) -> {args.out}')


if __name__ == '__main__':
    main()
