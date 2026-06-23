#!/usr/bin/env python
"""Independent SNR / connected-component star reference detector.
Reads a 16-bit FITS (BZERO unsigned), estimates a local background+noise on a coarse grid,
thresholds (image-bg) > k*sigma, morphologically closes (reconnect donut arcs), labels connected
components, and emits candidates (intensity-weighted centroid, bbox, peak, SNR, area).
Independent of HocusFocus's gates (contamination/distortion/centering/PSF), so candidates it finds
that HF rejects are HF's recall gaps. Donut-aware via the closing + component centroid."""
import sys, json, struct
import numpy as np
from scipy import ndimage

def read_fits(path):
    with open(path, 'rb') as f:
        cards = {}
        data_off = 0
        while True:
            block = f.read(2880)
            ended = False
            for i in range(0, 2880, 80):
                card = block[i:i+80].decode('latin1')
                key = card[:8].strip()
                if key == 'END':
                    ended = True
                    break
                if '=' in card[:10]:
                    val = card[10:].split('/')[0].strip()
                    cards[key] = val
            if ended:
                data_off = f.tell()
                break
        naxis1 = int(cards['NAXIS1']); naxis2 = int(cards['NAXIS2'])
        bzero = float(cards.get('BZERO', '0')); bscale = float(cards.get('BSCALE', '1'))
        f.seek(data_off)
        raw = f.read(naxis1 * naxis2 * 2)
        arr = np.frombuffer(raw, dtype='>i2').astype(np.float32)
        arr = arr * bscale + bzero
        return arr.reshape(naxis2, naxis1)  # (rows=y, cols=x)

def coarse_bg(img, grid=128):
    h, w = img.shape
    gy, gx = h // grid, w // grid
    # block medians -> bg; block MAD -> sigma
    crop = img[:gy*grid, :gx*grid].reshape(gy, grid, gx, grid)
    med = np.median(crop, axis=(1,3))
    mad = np.median(np.abs(crop - med[:,None,:,None]), axis=(1,3)) * 1.4826
    # upsample to full size (nearest is fine for a smooth bg)
    bg = np.repeat(np.repeat(med, grid, axis=0), grid, axis=1)
    sig = np.repeat(np.repeat(mad, grid, axis=0), grid, axis=1)
    bg = np.pad(bg, ((0,h-bg.shape[0]),(0,w-bg.shape[1])), mode='edge')
    sig = np.pad(sig, ((0,h-sig.shape[0]),(0,w-sig.shape[1])), mode='edge')
    sig = np.maximum(sig, 1.0)
    return bg, sig

def detect(path, k=5.0, min_area=3, max_area=8000, close=2):
    img = read_fits(path)
    bg, sig = coarse_bg(img)
    signal = img - bg
    mask = signal > (k * sig)
    if close > 0:
        mask = ndimage.binary_closing(mask, structure=np.ones((close,close)))
    lbl, n = ndimage.label(mask)
    if n == 0:
        return img, bg, sig, []
    objs = ndimage.find_objects(lbl)
    cands = []
    for i, sl in enumerate(objs, start=1):
        ys, xs = sl
        area = int((lbl[sl] == i).sum())
        if area < min_area or area > max_area:
            continue
        sub_sig = np.where(lbl[sl] == i, signal[sl], 0)
        tot = sub_sig.sum()
        if tot <= 0:
            continue
        yy, xx = np.mgrid[ys.start:ys.stop, xs.start:xs.stop]
        cy = float((yy * sub_sig).sum() / tot)
        cx = float((xx * sub_sig).sum() / tot)
        peak = float(signal[sl][lbl[sl] == i].max())
        snr = peak / float(np.median(sig[sl]))
        cands.append({'x': round(cx,1), 'y': round(cy,1),
                      'bx': int(xs.start), 'by': int(ys.start),
                      'bw': int(xs.stop-xs.start), 'bh': int(ys.stop-ys.start),
                      'peak': round(peak,1), 'snr': round(snr,2), 'area': area})
    return img, bg, sig, cands

if __name__ == '__main__':
    path = sys.argv[1]
    k = float(sys.argv[2]) if len(sys.argv) > 2 else 5.0
    img, bg, sig, cands = detect(path, k=k)
    out = sys.argv[3] if len(sys.argv) > 3 else None
    print(f'image {img.shape} bg~{np.median(bg):.0f} sig~{np.median(sig):.1f} k={k} candidates={len(cands)}')
    if cands:
        snrs = sorted(c['snr'] for c in cands)
        print(f'  SNR min {snrs[0]} median {snrs[len(snrs)//2]} max {snrs[-1]}; area med {sorted(c["area"] for c in cands)[len(cands)//2]}')
    if out:
        json.dump(cands, open(out, 'w'))
        print('  wrote', out)
