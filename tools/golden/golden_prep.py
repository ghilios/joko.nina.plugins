#!/usr/bin/env python
"""Per-run golden PREP: run the SNR reference on each frame and build the BOUNDED montage set for LLM QA.

The full-bank golden generation is the long pole, and wide-field frames produce 10k+ SNR candidates each — far too
many to LLM-QA wholesale. This prep bounds the work:

  * snr_ref over each frame's linear-mono FITS export (TestApp export-linear: reads XISF + debayers), sorted by SNR
    descending → snr_<foc>.json (the full candidate list; consumed by build_goldens.py).
  * The HIGH tier (SNR >= --auto-confirm-snr) is auto-confirmed by build_goldens.py (real after sat-masking + area
    filtering = the recall@SNR>=12 reference), so it needs NO montages here.
  * Only the UNCERTAIN tier (SNR < threshold), highest-SNR-first, is rendered into montages — capped at
    --budget-montages per frame — for LLM confirm/reject (precision + recall@all on the faint tier). Each montage's
    cells carry their GLOBAL index into snr_<foc>.json (via montage_index.json), so QA confirmations map back
    unambiguously regardless of the cap/offset.

Writes per run: <out>/snr_<foc>.json, <out>/f<foc>/montage_*.png + montage_index.json, and <out>/manifest.json
(frames[].{foc, total, high, uncertainRendered, montageDir, montageCount}) for the QA workflow + build_goldens.

Usage:
  golden_prep.py --run-dir <orig run> --out <scratch run> [--k 5] [--donut] [--sat-radius 0]
                 [--auto-confirm-snr 12] [--budget-montages 60] [--grid 6] [--crop 48] [--cell 120]
"""
import sys, os, re, json, argparse
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(__file__))
from snr_ref import detect, read_fits, coarse_bg
from qa_montage import stretch_crop
from plausibility import check_scale, frame_star_scale, is_plausible, qa_order

FRAME_RE = re.compile(r'Focuser(\d+)', re.IGNORECASE)
FRAME_EXTS = ('.fits', '.fit', '.xisf')


def frame_focuser(name):
    low = name.lower()
    if low.endswith('.golden.json') or low.endswith('.linear.fits'):
        return None
    if not low.endswith(FRAME_EXTS):
        return None
    m = FRAME_RE.search(name)
    return int(m.group(1)) if m else None


def linear_for(run_dir, fn):
    """Prefer the linear-mono export; fall back to the original if it's already a mono FITS."""
    lp = os.path.join(run_dir, fn + '.linear.fits')
    if os.path.exists(lp):
        return lp
    if fn.lower().endswith(('.fits', '.fit')):
        return os.path.join(run_dir, fn)
    return None  # XISF/Bayer with no export — caller must run TestApp export-linear first


def build_montages(fits_path, cands, global_indices, out_dir, crop=48, cell=120, grid=6):
    """Like qa_montage.build, but each cell records its GLOBAL index into the full candidate list."""
    os.makedirs(out_dir, exist_ok=True)
    img = read_fits(fits_path)
    bg, sig = coarse_bg(img)
    gbg, gsig = float(np.median(bg)), float(np.median(sig))
    lo, hi = gbg - gsig, gbg + 25 * gsig
    H, W = img.shape
    per = grid * grid
    montages, idx_map = [], {}
    for m0 in range(0, len(cands), per):
        chunk = list(zip(cands[m0:m0 + per], global_indices[m0:m0 + per]))
        canvas = Image.new('RGB', (grid * cell, grid * cell), (20, 20, 20))
        d = ImageDraw.Draw(canvas)
        for j, (c, gi) in enumerate(chunk):
            cx, cy = int(round(c['x'])), int(round(c['y']))
            x0, y0 = cx - crop // 2, cy - crop // 2
            sub = np.zeros((crop, crop), np.float32) + gbg
            xa, ya, xb, yb = max(0, x0), max(0, y0), min(W, x0 + crop), min(H, y0 + crop)
            if xb > xa and yb > ya:
                sub[ya - y0:yb - y0, xa - x0:xb - x0] = img[ya:yb, xa:xb]
            tile = Image.fromarray(stretch_crop(sub, lo, hi)).convert('RGB').resize((cell, cell), Image.NEAREST)
            r, cc = divmod(j, grid)
            px, py = cc * cell, r * cell
            canvas.paste(tile, (px, py))
            d.rectangle([px + cell // 2 - 7, py + cell // 2 - 7, px + cell // 2 + 7, py + cell // 2 + 7], outline=(0, 200, 255), width=1)
            d.text((px + 3, py + 2), str(gi), fill=(255, 255, 0))
            d.rectangle([px, py, px + cell - 1, py + cell - 1], outline=(60, 60, 60), width=1)
            idx_map[m0 + j] = gi  # montage cell position -> GLOBAL candidate index
        mi = len(montages)
        fn = os.path.join(out_dir, f'montage_{mi:03d}.png')
        canvas.save(fn)
        montages.append({'file': fn, 'cellToGlobal': {str(m0 + k): global_indices[m0 + k] for k in range(len(chunk))}})
    json.dump({'montages': montages, 'grid': grid, 'cellToGlobal': idx_map}, open(os.path.join(out_dir, 'montage_index.json'), 'w'))
    return montages


def auto_confirmed_indices(cands, scale, donut, threshold):
    """Which candidates skip LLM QA entirely.

    On --donut runs: NONE. The matched filter shreds rings into fragments and the SNR ordering inverts,
    so significance alone cannot be trusted and the high tier is QA'd like any other tier.

    Otherwise: significance AND plausibility. Significance was never sufficient -- a 3px spike at 12
    sigma peak outranks a real donut -- so a candidate must also be a credible size for its frame.
    """
    if donut:
        return set()
    return {i for i, c in enumerate(cands)
            if float(c['snr']) >= threshold and is_plausible(c, scale)}


def qa_worklist(cands, scale, donut, threshold):
    """Global indices to render for QA, in priority order: plausibility desc, then SNR desc.

    Ordering, never filtering -- the budget decides how far down the queue we get, and everything past
    that point is recorded as UNRESOLVED rather than silently treated as not-a-star.
    """
    auto = auto_confirmed_indices(cands, scale, donut, threshold)
    return [i for i in qa_order(cands, scale) if i not in auto]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--run-dir', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--k', type=float, default=5.0)
    ap.add_argument('--donut', action='store_true')
    ap.add_argument('--sat-radius', type=float, default=0.0)
    ap.add_argument('--auto-confirm-snr', type=float, default=12.0)
    ap.add_argument('--budget-montages', type=int, default=60)
    ap.add_argument('--min-scale', type=float, default=5.0,
                    help="abort if a frame's star-scale estimate falls to/below this (px). The guard "
                         'against a silently-inert plausibility gate; see design 4.3.')
    ap.add_argument('--grid', type=int, default=6)
    ap.add_argument('--crop', type=int, default=48)
    ap.add_argument('--cell', type=int, default=120)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    per = args.grid * args.grid

    frames = {}
    for f in sorted(os.listdir(args.run_dir)):
        foc = frame_focuser(f)
        if foc is not None and foc not in frames:
            frames[foc] = f

    manifest = {'runDir': args.run_dir, 'frames': []}
    for foc, fn in sorted(frames.items()):
        lin = linear_for(args.run_dir, fn)
        if lin is None or not os.path.exists(lin):
            print(f'  foc {foc}: no readable mono FITS (run TestApp export-linear first); skipped')
            continue
        _, _, _, cands = detect(lin, k=args.k, donut=args.donut, sat_radius=args.sat_radius)
        cands.sort(key=lambda c: -c['snr'])  # stable, human-readable order for snr_<foc>.json
        json.dump(cands, open(os.path.join(args.out, f'snr_{foc}.json'), 'w'))

        # Label with the last two path components: every run's leaf is "attempt01", so the basename
        # alone cannot identify which run failed in a bank-wide sweep.
        run_label = os.path.join(*os.path.normpath(args.run_dir).split(os.sep)[-2:])
        scale = check_scale(frame_star_scale(cands), f'{run_label} foc {foc}', min_scale=args.min_scale)
        auto = auto_confirmed_indices(cands, scale, args.donut, args.auto_confirm_snr)
        order = qa_worklist(cands, scale, args.donut, args.auto_confirm_snr)
        to_qa = order[: args.budget_montages * per]
        # The workflow returns montage CELL POSITIONS; this file translates position -> global index,
        # which is what lets the worklist be plausibility-ordered instead of a contiguous SNR prefix.
        json.dump(to_qa, open(os.path.join(args.out, f'qaorder_{foc}.json'), 'w'))

        mdir = os.path.join(args.out, f'f{foc}')
        montages = build_montages(lin, [cands[i] for i in to_qa], to_qa, mdir,
                                  crop=args.crop, cell=args.cell, grid=args.grid) if to_qa else []
        manifest['frames'].append({'foc': foc, 'imageFile': fn, 'total': len(cands),
                                   'starScale': scale, 'autoConfirmed': len(auto),
                                   'high': sum(1 for c in cands if c['snr'] >= args.auto_confirm_snr),
                                   'queued': len(order), 'rendered': len(to_qa),
                                   'montageDir': mdir, 'montageCount': len(montages)})
        print(f'  foc {foc}: {len(cands)} cand, scale {scale:.0f}px, {len(auto)} auto-confirmed, '
              f'{len(to_qa)}/{len(order)} queued -> {len(montages)} montages')
    json.dump(manifest, open(os.path.join(args.out, 'manifest.json'), 'w'), indent=1)
    print(f'wrote manifest: {len(manifest["frames"])} frames -> {args.out}')


if __name__ == '__main__':
    main()
