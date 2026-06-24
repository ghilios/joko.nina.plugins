#!/usr/bin/env python
"""Assemble per-image <image>.golden.json sidecars from SNR candidates filtered by montage-QA confirmation.

golden = QA-confirmed SNR candidates; confidence tier from SNR (>=12 high, 8-12 medium, 5-8 low). Run-parameterized
(supersedes the original cwhite-hardcoded script): point it at a run folder plus the scratch dirs holding the
per-frame SNR candidate JSONs (snr_<focuser>.json from snr_ref.py) and per-frame QA confirmations
(qa_<focuser>.json = {"confirmed":[global candidate indices], "donut":[...]}, aggregated from qa_workflow.js).

Frames are matched by the Focuser<NNNN> token, so the golden sidecar lands beside the ORIGINAL frame
(<frame>.fits / <frame>.xisf -> <frame>.golden.json) even when the SNR reference was run on a .linear.fits export
(same pixel dimensions, so candidate coordinates map directly).

Usage:
  build_goldens.py --run-dir <run> --snr-dir <scratch> --qa-dir <scratch> [--method "SNR-ref(k5)+montageQA"]
"""
import sys, os, re, json, glob, argparse

FRAME_RE = re.compile(r'Focuser(\d+)', re.IGNORECASE)
FRAME_EXTS = ('.fits', '.fit', '.xisf')


def tier(snr):
    return 'high' if snr >= 12 else 'medium' if snr >= 8 else 'low'


def frame_focuser(name):
    # Exclude derived sidecars; only real frames.
    low = name.lower()
    if low.endswith('.golden.json') or low.endswith('.linear.fits'):
        return None
    if not low.endswith(FRAME_EXTS):
        return None
    m = FRAME_RE.search(name)
    return int(m.group(1)) if m else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--run-dir', required=True, help='folder holding the original AF frames')
    ap.add_argument('--snr-dir', required=True, help='scratch dir with snr_<focuser>.json candidate lists')
    ap.add_argument('--qa-dir', required=True, help='scratch dir with qa_<focuser>.json confirmations')
    ap.add_argument('--method', default='SNR-ref+montageQA')
    ap.add_argument('--auto-confirm-snr', type=float, default=12.0,
                    help='SNR at/above which candidates are auto-confirmed without LLM QA (high tier is real with ~100%% '
                         'reliability after sat-masking + area filtering). Set very high to disable and QA everything.')
    args = ap.parse_args()

    frames = {}
    for f in sorted(os.listdir(args.run_dir)):
        foc = frame_focuser(f)
        if foc is not None and foc not in frames:
            frames[foc] = f

    total = 0
    summary = []
    for foc, fn in sorted(frames.items()):
        snr_path = os.path.join(args.snr_dir, f'snr_{foc}.json')
        qa_path = os.path.join(args.qa_dir, f'qa_{foc}.json')
        if not os.path.exists(snr_path):
            print(f'  foc {foc}: no SNR candidates ({snr_path}); skipped')
            continue
        cand = json.load(open(snr_path))
        # Auto-confirm the high tier (SNR >= threshold): real with ~100% reliability after sat-masking + area
        # filtering, so they need no LLM QA. This is the recall@SNR>=12 reference (the headline metric).
        confirmed = set(i for i, c in enumerate(cand) if c['snr'] >= args.auto_confirm_snr)
        auto = len(confirmed)
        # Union with the LLM-QA-confirmed UNCERTAIN tier (<threshold), where noise hides — needed for precision.
        if os.path.exists(qa_path):
            qa = json.load(open(qa_path))
            confirmed |= set(qa.get('confirmed', []))
        else:
            print(f'  foc {foc}: no QA confirmations ({qa_path}); high tier auto-confirmed only ({auto})')
        stars = [{'x': round(c['x'] - c['bw'] / 2), 'y': round(c['y'] - c['bh'] / 2),
                  'w': c['bw'], 'h': c['bh'], 'confidence': tier(c['snr'])}
                 for i, c in enumerate(cand) if i in confirmed and i < len(cand)]
        out = {'imageFile': fn, 'focuserPosition': foc, 'schemaVersion': 1, 'method': args.method, 'stars': stars}
        json.dump(out, open(os.path.join(args.run_dir, fn + '.golden.json'), 'w'), indent=1)
        hi = sum(1 for s in stars if s['confidence'] == 'high')
        summary.append((foc, len(cand), len(stars), hi))
        total += len(stars)

    print(f'{"foc":>7}  {"cand":>6}  {"golden":>6}  high(SNR>=12)')
    for foc, c, g, h in summary:
        print(f'{foc:>7}  {c:>6}  {g:>6}  {h}')
    print(f'total golden stars: {total}  ({len(summary)} frames)')


if __name__ == '__main__':
    main()
