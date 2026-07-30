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

sys.path.insert(0, os.path.dirname(__file__))
from plausibility import frame_star_scale
from golden_prep import auto_confirmed_indices

FRAME_RE = re.compile(r'Focuser(\d+)', re.IGNORECASE)
FRAME_EXTS = ('.fits', '.fit', '.xisf')


def tier(snr):
    return 'high' if snr >= 12 else 'medium' if snr >= 8 else 'low'


def resolve(cands, auto, qa):
    """Partition candidates into confirmed / rejected / unresolved.

    UNRESOLVED is the point: with a bounded montage budget most candidates are never looked at, and
    counting them as not-stars turns budget truncation into fake false positives (F11). They are
    excluded from both denominators instead.
    """
    auto = set(auto)
    examined = set(qa.get('examined', [])) if qa else set()
    qa_confirmed = set(qa.get('confirmed', [])) if qa else set()
    confirmed = auto | (qa_confirmed & examined) if examined else auto | qa_confirmed
    rejected = examined - confirmed
    unresolved = set(range(len(cands))) - confirmed - rejected
    return {'confirmed': confirmed, 'rejected': rejected, 'unresolved': unresolved}


def coverage(cands, examined, auto):
    """Per-tier examined/total, so a consumer can tell a precision measurement from a lower bound."""
    seen = set(examined) | set(auto)
    out = {'high': {'examined': 0, 'total': 0},
           'medium': {'examined': 0, 'total': 0},
           'low': {'examined': 0, 'total': 0}}
    for i, c in enumerate(cands):
        t = tier(c['snr'])
        out[t]['total'] += 1
        if i in seen:
            out[t]['examined'] += 1
    return out


def _box(c, confidence):
    return {'x': round(c['x'] - c['bw'] / 2), 'y': round(c['y'] - c['bh'] / 2),
            'w': c['bw'], 'h': c['bh'], 'confidence': confidence}


def build_frame(cands, auto, qa, image_file, foc, method, qa_votes, qa_version):
    """Assemble one schema-v2 <image>.golden.json payload."""
    r = resolve(cands, auto, qa)
    examined = set(qa.get('examined', [])) if qa else set()
    return {'imageFile': image_file, 'focuserPosition': foc, 'schemaVersion': 2, 'method': method,
            'stars': [_box(cands[i], tier(cands[i]['snr'])) for i in sorted(r['confirmed'])],
            'unresolved': [_box(cands[i], tier(cands[i]['snr'])) for i in sorted(r['unresolved'])],
            'coverage': coverage(cands, examined, auto),
            'qaVotes': qa_votes, 'qaVersion': qa_version}


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
                    help='SNR at/above which candidates MAY be auto-confirmed without LLM QA. Necessary but no '
                         'longer sufficient: the candidate must also be a plausible size for its frame, and on '
                         'donut-aware runs nothing is auto-confirmed at all.')
    ap.add_argument('--qa-votes', type=int, default=1,
                    help='independent LLM votes per candidate used to produce the QA sidecars (recorded, not applied '
                         'here). 1 is not reproducible on defocused runs -- see design section 6.')
    ap.add_argument('--qa-version', default='sonnet/golden-qa-v2')
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
        scale = frame_star_scale(cand)
        # Donut-aware prep is detectable from the candidate list itself, so a golden can never be built
        # with a different auto-confirm policy than the one its candidates were prepped under.
        donut_mode = any(c.get('src') == 'mf' or c.get('donut') for c in cand)
        auto = auto_confirmed_indices(cand, scale, donut_mode, args.auto_confirm_snr) if scale else set()
        qa = json.load(open(qa_path)) if os.path.exists(qa_path) else None
        if qa is None:
            print(f'  foc {foc}: no QA confirmations ({qa_path}); {len(auto)} auto-confirmed only')
        out = build_frame(cand, auto, qa, fn, foc, args.method, args.qa_votes, args.qa_version)
        json.dump(out, open(os.path.join(args.run_dir, fn + '.golden.json'), 'w'), indent=1)
        stars = out['stars']
        hi = sum(1 for s in stars if s['confidence'] == 'high')
        summary.append((foc, len(cand), len(stars), hi, len(out['unresolved'])))
        total += len(stars)

    print(f'{"foc":>7}  {"cand":>7}  {"golden":>7}  {"high":>7}  unresolved')
    for foc, c, g, h, u in summary:
        print(f'{foc:>7}  {c:>7}  {g:>7}  {h:>7}  {u}')
    print(f'total golden stars: {total}  ({len(summary)} frames)')


if __name__ == '__main__':
    main()
