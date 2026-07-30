#!/usr/bin/env python
"""Independent SNR / connected-component star reference detector.
Reads a 16-bit FITS (BZERO unsigned), estimates a local background+noise on a coarse grid,
thresholds (image-bg) > k*sigma, morphologically closes (reconnect donut arcs), labels connected
components, and emits candidates (intensity-weighted centroid, bbox, peak, SNR, area).
Independent of HocusFocus's gates (contamination/distortion/centering/PSF), so candidates it finds
that HF rejects are HF's recall gaps. Donut-aware via the closing + component centroid."""
import sys, json, struct, math
import numpy as np
from scipy import ndimage

DONUT_K_DEFAULT = 8.0
"""Matched-filter response threshold. Was 6.0, which sat inside the noise: on LinwoodFocus the response
distribution's median was 6.41 against a 6.0 cut, so ~98% of the candidate pool was junk and the bounded LLM
QA budget was spent confirming that. Confirmed real donuts there had min 7.48 / median 14.46, so 8.0 drops
94% of the candidates for 7% of the real donuts. The 7% is a lower bound -- the confirmed sample was itself
drawn from the 6.0 pool."""



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

def matched_filter_donuts(signal, sig, radii, k, minsep=12):
    """Multi-scale disk matched filter for low-surface-brightness DONUTS, returned as LOCAL MAXIMA of the
    response (one detection per donut, not a flood of thresholded pixels). For each radius the disk-integrated
    SNR is mean_in_disk*sqrt(Npix)/sigma; we take the per-pixel MAX response across radii, then keep strict
    local maxima above k (a donut gives one clean peak; noise gives many small scattered ones that the local-max
    + separation suppress). Returns list of (cy, cx, response, radius).

    On k: the original 6.0 sat inside the noise. Measured over LinwoodFocus's 48,047 matched-filter candidates,
    the response distribution has p50 = 6.41 -- half of all "detections" within 7% of the threshold -- while
    LLM-confirmed real donuts have min 7.48 and median 14.46. Raising k to 8.0 discards 94% of the candidates
    and 7% of the confirmed donuts. See DONUT_K_DEFAULT."""
    best = np.zeros(signal.shape, dtype=np.float32)
    bestr = np.zeros(signal.shape, dtype=np.int16)
    for r in radii:
        win = 2*r + 1
        boxmean = ndimage.uniform_filter(signal, size=win, mode='nearest')
        resp = (boxmean * np.sqrt(win*win) / sig).astype(np.float32)
        upd = resp > best
        best = np.where(upd, resp, best)
        bestr = np.where(upd, r, bestr)
    localmax = (best == ndimage.maximum_filter(best, size=minsep)) & (best > k)
    ys, xs = np.nonzero(localmax)
    return [(int(y), int(x), float(best[y, x]), int(bestr[y, x])) for y, x in zip(ys, xs)]

def saturation_mask(img, sat_level=60000.0, radius=0.0):
    """Distance-mask around saturated-star cores (the diffraction-spike / bloom region) so the reference does
    not emit a flood of false candidates along the spikes. radius<=0 disables. Returns bool mask to EXCLUDE."""
    if radius <= 0:
        return None
    sat = img >= sat_level
    if not sat.any():
        return None
    dist = ndimage.distance_transform_edt(~sat)
    return dist < radius

def detect_from_arrays(img, bg, sig, k=5.0, min_area=3, max_area=20000, close=2, donut=False,
                       donut_radii=(6,10,14,18), donut_k=DONUT_K_DEFAULT, sat_radius=0.0):
    """Candidate extraction from already-loaded arrays. Split out of detect() so tests can drive it
    with a synthetic frame instead of a FITS file."""
    signal = img - bg
    satmask = saturation_mask(img, radius=sat_radius)
    mask = signal > (k * sig)
    if close > 0:
        mask = ndimage.binary_closing(mask, structure=np.ones((close,close)))
    lbl, n = ndimage.label(mask)
    objs = ndimage.find_objects(lbl) if n else []
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
        sg = float(np.median(sig[sl]))
        cands.append({'x': round(cx,1), 'y': round(cy,1),
                      'bx': int(xs.start), 'by': int(ys.start),
                      'bw': int(xs.stop-xs.start), 'bh': int(ys.stop-ys.start),
                      'peak': round(peak,1), 'snr': round(peak / sg, 2), 'area': area,
                      # flux in ADU, and flux in units of sigma. fluxSnr is the ONLY quantity comparable
                      # across the two detection paths -- 'snr' is peak/sigma here but a disk-integrated
                      # matched-filter response below, which is what F16 tripped over.
                      'flux': round(float(tot), 1), 'fluxSnr': round(float(tot) / sg, 2),
                      'src': 'cc', 'snrKind': 'peak'})
    if donut:
        # Add donut local maxima not already covered by a peak candidate (dedup by separation).
        existing = [(c['x'], c['y']) for c in cands]
        for (dy, dx, resp, r) in matched_filter_donuts(signal, sig, donut_radii, donut_k):
            if any((dx-ex)**2 + (dy-ey)**2 <= (r*1.0)**2 for ex, ey in existing):
                continue
            area = int(math.pi * r * r)
            # Measure a REAL peak/sigma inside the disk. Without it 'snr' would carry the disk-integrated
            # response here and peak/sigma on the connected-component path -- two different quantities under
            # one name, which is the F16 confusion. tier() buckets on 'snr', so leaving the response there
            # tiers donuts on an incomparable scale: on Panos that put 36px donuts in the medium tier while
            # smaller components filled the high tier, inverting the golden at low QA coverage.
            y0, y1 = max(0, dy - r), min(signal.shape[0], dy + r + 1)
            x0, x1 = max(0, dx - r), min(signal.shape[1], dx + r + 1)
            sub = signal[y0:y1, x0:x1]
            sg = float(np.median(sig[y0:y1, x0:x1]))
            peak = float(sub.max()) if sub.size else 0.0
            cands.append({'x': float(dx), 'y': float(dy), 'bx': dx-r, 'by': dy-r, 'bw': 2*r, 'bh': 2*r,
                          'peak': round(peak, 1), 'snr': round(peak / sg, 2), 'area': area, 'donut': True,
                          # resp = mean*sqrt(N)/sigma, so flux/sigma = resp*sqrt(N).
                          'flux': 0.0, 'fluxSnr': round(resp * math.sqrt(area), 2),
                          'response': round(resp, 2), 'src': 'mf', 'snrKind': 'peak'})
            existing.append((dx, dy))
    if satmask is not None:
        H, W = img.shape
        cands = [c for c in cands if not satmask[min(H-1, max(0, int(round(c['y'])))), min(W-1, max(0, int(round(c['x']))))]]
    return cands


def detect(path, k=5.0, min_area=3, max_area=20000, close=2, donut=False, donut_radii=(6,10,14,18), donut_k=DONUT_K_DEFAULT, sat_radius=0.0):
    img = read_fits(path)
    bg, sig = coarse_bg(img)
    cands = detect_from_arrays(img, bg, sig, k=k, min_area=min_area, max_area=max_area, close=close,
                               donut=donut, donut_radii=donut_radii, donut_k=donut_k, sat_radius=sat_radius)
    return img, bg, sig, cands

if __name__ == '__main__':
    path = sys.argv[1]
    k = float(sys.argv[2]) if len(sys.argv) > 2 else 5.0
    out = sys.argv[3] if len(sys.argv) > 3 else None
    donut = '--donut' in sys.argv
    img, bg, sig, cands = detect(path, k=k, donut=donut)
    print(f'image {img.shape} bg~{np.median(bg):.0f} sig~{np.median(sig):.1f} k={k} donut={donut} candidates={len(cands)}')
    if cands:
        snrs = sorted(c['snr'] for c in cands)
        print(f'  SNR min {snrs[0]} median {snrs[len(snrs)//2]} max {snrs[-1]}; area med {sorted(c["area"] for c in cands)[len(cands)//2]}')
    if out:
        json.dump(cands, open(out, 'w'))
        print('  wrote', out)
