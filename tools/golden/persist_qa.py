#!/usr/bin/env python
"""Persist the qa_workflow.js byKey result to per-run qa_<foc>.json sidecars for build_goldens.py.

The workflow returns { byKey: { "<runtag><sep><foc>": {real:[cellPosition], donut:[cellPosition]} } }, where a
POSITION is the montage-cell ordinal (m*grid^2 + cell). It is deliberately NOT a global candidate index: the QA
worklist is rendered in PLAUSIBILITY order, which is not a contiguous prefix of snr_<foc>.json, so no arithmetic
can recover a global index from a cell. This maps position -> global index through qaorder_<foc>.json, and records
which candidates were examined at all so build_goldens can tell a QA-rejected candidate from an unexamined one.

Usage: persist_qa.py --bykey <byKey.json> --scratch-root <golden_gen>
  (byKey.json may be the raw byKey object or {"byKey": {...}})
"""
import json, os, re, argparse

KEY_RE = re.compile(r'^(.*?)[\s\x00|]*(\d+)$')


def persist(scratch_root, by_key, grid=6):
    """Map montage cell POSITIONS to global candidate indices via qaorder_<foc>.json, and record which
    candidates were examined at all. Returns the number of sidecars written.

    'examined' is derived from the montage COUNT the workflow actually completed, not from the length of
    qaorder: the worklist can be capped below the rendered montage count, and treating the whole qaorder as
    examined would overstate coverage -- the exact number schema v2 exists to record honestly.
    """
    cells = grid * grid
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
        montages = v.get('montages')
        examined = order[: montages * cells] if montages is not None else list(order)

        def to_global(positions):
            return sorted({order[p] for p in positions if isinstance(p, int) and 0 <= p < len(order)})

        json.dump({'confirmed': to_global(v.get('real', v.get('confirmed', []))),
                   'donut': to_global(v.get('donut', [])),
                   'examined': sorted(examined)},
                  open(os.path.join(outdir, f'qa_{foc}.json'), 'w'))
        written += 1
    return written


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--bykey', required=True)
    ap.add_argument('--scratch-root', required=True)
    ap.add_argument('--grid', type=int, default=6, help='montage grid used at prep time (cells = grid^2)')
    args = ap.parse_args()
    data = json.load(open(args.bykey))
    bk = data.get('byKey', data) if isinstance(data, dict) else {}
    n = persist(args.scratch_root, bk, grid=args.grid)
    print(f'persist_qa: wrote {n} qa_<foc>.json sidecars under {args.scratch_root}')


if __name__ == '__main__':
    main()
