#!/usr/bin/env python
"""Build QA montages of SNR candidates for LLM vision confirm/reject (classification, not localization).
Each candidate -> a stretched crop centered on it, upscaled, arranged in a labelled grid. An LLM marks
which cells hold a REAL star centered in the cell (vs noise/edge/empty). Robust to image downscaling because
it's a per-cell yes/no, not a coordinate."""
import sys, json, os
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(__file__))
from snr_ref import read_fits, coarse_bg

def stretch_crop(crop, lo, hi):
    x = np.clip((crop - lo) / max(hi - lo, 1.0), 0, 1)
    x = np.arcsinh(x * 10) / np.arcsinh(10)   # asinh to lift faint
    return (x * 255).astype(np.uint8)

def build(fits_path, cands_path, out_dir, crop=48, cell=120, grid=6):
    os.makedirs(out_dir, exist_ok=True)
    img = read_fits(fits_path)
    bg, sig = coarse_bg(img)
    gbg = float(np.median(bg)); gsig = float(np.median(sig))
    lo, hi = gbg - gsig, gbg + 25 * gsig
    H, W = img.shape
    cands = json.load(open(cands_path))
    per = grid * grid
    montages = []
    idx_map = {}
    for m0 in range(0, len(cands), per):
        chunk = cands[m0:m0+per]
        canvas = Image.new('RGB', (grid*cell, grid*cell), (20,20,20))
        d = ImageDraw.Draw(canvas)
        for j, c in enumerate(chunk):
            gi = m0 + j
            cx, cy = int(round(c['x'])), int(round(c['y']))
            x0, y0 = cx-crop//2, cy-crop//2
            sub = np.zeros((crop,crop), np.float32) + gbg
            xa, ya = max(0,x0), max(0,y0); xb, yb = min(W,x0+crop), min(H,y0+crop)
            if xb>xa and yb>ya:
                sub[ya-y0:yb-y0, xa-x0:xb-x0] = img[ya:yb, xa:xb]
            tile = Image.fromarray(stretch_crop(sub, lo, hi)).convert('RGB').resize((cell,cell), Image.NEAREST)
            r, cc = divmod(j, grid)
            px, py = cc*cell, r*cell
            canvas.paste(tile, (px, py))
            # center reticle (the candidate is at the cell center) + index label
            d.rectangle([px+cell//2-7, py+cell//2-7, px+cell//2+7, py+cell//2+7], outline=(0,200,255), width=1)
            d.text((px+3, py+2), str(gi), fill=(255,255,0))
            d.rectangle([px,py,px+cell-1,py+cell-1], outline=(60,60,60), width=1)
            idx_map[gi] = {'x':c['x'],'y':c['y'],'snr':c.get('snr')}
        mi = len(montages)
        fn = os.path.join(out_dir, f'montage_{mi:03d}.png')
        canvas.save(fn)
        montages.append({'file':fn,'first':m0,'count':len(chunk)})
    json.dump({'montages':montages,'idx':idx_map,'grid':grid,'cell':cell,'total':len(cands)}, open(os.path.join(out_dir,'montage_index.json'),'w'))
    print(f'{len(cands)} candidates -> {len(montages)} montages ({grid}x{grid}) in {out_dir}')

if __name__ == '__main__':
    build(sys.argv[1], sys.argv[2], sys.argv[3])
