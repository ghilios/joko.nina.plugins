#!/usr/bin/env python
"""Bridge golden_prep -> qa_workflow: assemble the QA worklist (Workflow `args`) from per-run prep manifests.

For every run scratch dir (holding manifest.json from golden_prep.py and f<foc>/montage_index.json), emit one work
item per uncertain-tier montage with its cell->GLOBAL candidate-index array, so qa_workflow.js can map LLM cell
confirmations back into the full snr_<foc>.json. Also emits, per run, the inverse needed by the persist step.

Usage:
  build_qa_worklist.py --scratch-root <dir> --out worklist.json
The driver then runs:  Workflow(scriptPath=tools/golden/qa_workflow.js, args=<worklist.json contents>)
and persists the returned byKey as qa_<foc>.json per run before build_goldens.py.
"""
import os, sys, json, argparse


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--scratch-root', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--grid', type=int, default=6)
    args = ap.parse_args()
    per = args.grid * args.grid

    work = []
    runs = []
    for entry in sorted(os.listdir(args.scratch_root)):
        run_dir = os.path.join(args.scratch_root, entry)
        man_path = os.path.join(run_dir, 'manifest.json')
        if not os.path.isfile(man_path):
            continue
        man = json.load(open(man_path))
        runs.append({'run': entry, 'runDir': man.get('runDir'), 'scratch': run_dir})
        for fr in man['frames']:
            foc = fr['foc']
            mi_path = os.path.join(fr['montageDir'], 'montage_index.json')
            if not os.path.isfile(mi_path):
                continue
            mi = json.load(open(mi_path))
            c2g = mi.get('cellToGlobal', {})  # { "<to_qa position>": globalSnrIdx }
            for m, mont in enumerate(mi.get('montages', [])):
                cell_to_global = [c2g.get(str(m * per + j)) for j in range(per)]
                cell_to_global = [g for g in cell_to_global if g is not None]
                work.append({'run': entry, 'foc': foc, 'file': mont['file'], 'cellToGlobal': cell_to_global})

    json.dump({'grid': args.grid, 'work': work, 'runs': runs}, open(args.out, 'w'))
    print(f'{len(work)} montage work items across {len(runs)} run(s) -> {args.out}')


if __name__ == '__main__':
    main()
