#!/usr/bin/env python
"""Validate a golden star set from its stored <frame>.golden.json sidecars ALONE.

No FITS, no snr_<foc>.json, no LLM -- so the whole bank can be audited in seconds and only the runs
that actually fail need the expensive rebuild. This is what caught LinwoodFocus, whose high tier holds
13px boxes while its QA-confirmed tier holds 28px ones.

Signals (see docs/golden-tier-plausibility-design.md 4.1):
  * SIZE-COLLAPSE  -- the high tier has degenerated to pixel-scale detections.
  * TIER-INVERSION -- high-tier stars are SMALLER than QA-confirmed ones, which is backwards: brighter
                      stars are bigger, so a sound tiering gives a ratio >= 1.
  * WIDTH-FLAT     -- median high-tier width does not respond to focus across the sweep, meaning the
                      reference is measuring something that is not the star field.

Usage:
  golden_health.py --bank "D:/Autofocus Bank" [--json out.json]
  golden_health.py --run-dir <run folder>
"""
import argparse
import glob
import json
import os
import re

SMALL_PX = 4
SMALL_FRACTION_MAX = 0.55
WIDTH_RATIO_MIN = 1.0
WIDTH_DYNAMIC_MIN = 0.15
MIN_FRAMES = 3

FOCUSER_RE = re.compile(r'Focuser(\d+)', re.IGNORECASE)


def _median(xs):
    s = sorted(xs)
    return float(s[len(s) // 2]) if s else 0.0


def _widths(stars, high):
    return [max(s['w'], s['h']) for s in stars
            if ((s.get('confidence') or 'high') == 'high') == high]


def assess(frames):
    """frames: list of parsed golden dicts. Returns metrics + flags."""
    frames = sorted(frames, key=lambda f: f.get('focuserPosition', 0))
    per_frame_ratio, per_frame_width, all_high = [], [], []
    for f in frames:
        hi = _widths(f['stars'], True)
        qa = _widths(f['stars'], False)
        all_high.extend(hi)
        if hi:
            per_frame_width.append(_median(hi))
        if hi and qa:
            per_frame_ratio.append(_median(hi) / max(_median(qa), 1e-9))

    small = (sum(1 for w in all_high if w <= SMALL_PX) / len(all_high)) if all_high else 0.0
    ratio = _median(per_frame_ratio) if per_frame_ratio else float('nan')
    if per_frame_width:
        wmax, wmin = max(per_frame_width), min(per_frame_width)
        dyn = (wmax - wmin) / wmax if wmax > 0 else 0.0
    else:
        dyn = 0.0

    flags = []
    if len(frames) < MIN_FRAMES or not all_high:
        flags.append('INCONCLUSIVE')
    else:
        if small > SMALL_FRACTION_MAX:
            flags.append('SIZE-COLLAPSE')
        if per_frame_ratio and ratio < WIDTH_RATIO_MIN:
            flags.append('TIER-INVERSION')
        if dyn < WIDTH_DYNAMIC_MIN:
            flags.append('WIDTH-FLAT')
    return {'frames': len(frames), 'highStars': len(all_high), 'medianHighWidth': _median(all_high),
            'smallFraction': small, 'widthRatio': ratio, 'widthDynamicRange': dyn, 'flags': flags}


def load_run(run_dir):
    frames = []
    for p in glob.glob(os.path.join(run_dir, '**', '*.golden.json'), recursive=True):
        d = json.load(open(p))
        if 'focuserPosition' not in d:
            m = FOCUSER_RE.search(os.path.basename(p))
            if not m:
                continue
            d['focuserPosition'] = int(m.group(1))
        frames.append(d)
    return frames


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--bank', help='bank root; every immediate subfolder is treated as a run')
    ap.add_argument('--run-dir', help='a single run folder')
    ap.add_argument('--json', help='write the full report here')
    args = ap.parse_args()

    runs = {}
    if args.run_dir:
        runs[os.path.basename(os.path.normpath(args.run_dir))] = load_run(args.run_dir)
    elif args.bank:
        for entry in sorted(os.listdir(args.bank)):
            path = os.path.join(args.bank, entry)
            if os.path.isdir(path) and not entry.startswith('_'):
                fr = load_run(path)
                if fr:
                    runs[entry] = fr
    else:
        ap.error('one of --bank or --run-dir is required')

    report, failed = {}, 0
    print(f'{"run":<20} {"fr":>3} {"high":>8} {"medW":>5} {"<=4px":>6} {"ratio":>6} {"wDyn":>5}  flags')
    for name, frames in runs.items():
        r = assess(frames)
        report[name] = r
        if r['flags'] and r['flags'] != ['INCONCLUSIVE']:
            failed += 1
        print(f'{name:<20} {r["frames"]:>3} {r["highStars"]:>8} {r["medianHighWidth"]:>5.0f} '
              f'{r["smallFraction"] * 100:>5.0f}% {r["widthRatio"]:>6.2f} {r["widthDynamicRange"]:>5.2f}  '
              f'{" + ".join(r["flags"]) or "ok"}')
    if args.json:
        json.dump(report, open(args.json, 'w'), indent=1)
        print(f'wrote {args.json}')
    print(f'{failed} of {len(runs)} run(s) flagged')
    return 1 if failed else 0


if __name__ == '__main__':
    raise SystemExit(main())
