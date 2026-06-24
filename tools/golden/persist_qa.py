#!/usr/bin/env python
"""Persist the qa_workflow.js byKey result to per-run qa_<foc>.json sidecars for build_goldens.py.

The workflow returns { byKey: { "<runtag><sep><foc>": {confirmed:[globalIdx], donut:[globalIdx]} } }. This writes
each entry to <scratch-root>/<runtag>/qa_<foc>.json. Run is recovered by stripping the trailing focuser digits, so
it is robust to whatever separator the workflow used between runtag and foc.

Usage: persist_qa.py --bykey <byKey.json> --scratch-root <golden_gen>
  (byKey.json may be the raw byKey object or {"byKey": {...}})
"""
import json, os, re, argparse


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--bykey', required=True)
    ap.add_argument('--scratch-root', required=True)
    args = ap.parse_args()
    data = json.load(open(args.bykey))
    bk = data.get('byKey', data) if isinstance(data, dict) else {}
    written = 0
    for key, v in bk.items():
        m = re.match(r'^(.*?)[\s\x00|]*(\d+)$', key)
        if not m:
            print(f'  ?? unparseable key: {key!r}'); continue
        run = m.group(1).rstrip(' \x00|')
        foc = m.group(2)
        outdir = os.path.join(args.scratch_root, run)
        if not os.path.isdir(outdir):
            print(f'  ?? no scratch dir for run {run!r} (key {key!r})'); continue
        json.dump({'confirmed': sorted(set(v.get('confirmed', []))), 'donut': sorted(set(v.get('donut', [])))},
                  open(os.path.join(outdir, f'qa_{foc}.json'), 'w'))
        written += 1
    print(f'persist_qa: wrote {written} qa_<foc>.json sidecars under {args.scratch_root}')


if __name__ == '__main__':
    main()
