#!/usr/bin/env python
"""B-phase-2 cost gate: does lowering NoiseClippingMultiplier degrade focus accuracy?
For each NC level, per AF region, build the HFR-vs-focuser curve from detected stars (per-frame median HFR),
fit a parabola -> best-focus position + R²; also report star count and HFR scatter (the noise added)."""
import json, glob, os, csv, re
import numpy as np

RF="/mnt/d/Tilt Calibration Bank/cwhite/TiltCalibration_20260621_214304/01_Baseline/AutoFocus_20260621_214312/attempt01"
W,H=9576,6388
NEAR=2701   # near best focus (reported CalculatedFocusPoint = 2713)
NC_DIRS={'4.0(def)':'/mnt/c/temp/hf-golden/eval/all_M1_default',
         '2.5':'/mnt/c/temp/hf-golden/sweep/allNC2.5',
         '2.0':'/mnt/c/temp/hf-golden/sweep/allNC2.0',
         '1.5':'/mnt/c/temp/hf-golden/sweep/allNC1.5'}
# region rects (pixels) from the saved reports
regions=[]
for i in range(6):
    d=json.load(open(f"{RF}/autofocus_report_Region{i}.json"))['Region']['OuterBoundary']
    regions.append((i, d['StartX']*W, d['StartY']*H, d['Width']*W, d['Height']*H))
RNAME={0:'Global',1:'Center',2:'Corner-TL',3:'Corner-TR',4:'Corner-BL',5:'Corner-BR'}

def load(ncdir):
    """focuser -> list of (cx,cy,hfr)"""
    out={}
    for f in glob.glob(f"{ncdir}/attempt01/detected_f*.csv"):
        foc=int(re.search(r'detected_f(\d+)\.csv',f).group(1))
        rows=[]
        with open(f) as fh:
            for r in csv.DictReader(fh):
                rows.append((float(r['cx']),float(r['cy']),float(r['hfr'])))
        out[foc]=rows
    return out

def in_region(cx,cy,reg): _,x,y,w,h=reg; return x<=cx<x+w and y<=cy<y+h

def parab_fit(xs,ys):
    if len(xs)<4: return None,None
    c=np.polyfit(xs,ys,2)
    if c[0]<=0: return None,None
    minpos=-c[1]/(2*c[0])
    pred=np.polyval(c,xs); ss_res=np.sum((np.array(ys)-pred)**2); ss_tot=np.sum((np.array(ys)-np.mean(ys))**2)
    r2=1-ss_res/ss_tot if ss_tot>0 else float('nan')
    return minpos,r2

print(f"{'region':<11} {'NC':<9} {'nStars@2701':>11} {'bestFocus':>9} {'R2':>6} {'HFRscatter@2701':>16}")
for reg in regions:
    for nc,ncdir in NC_DIRS.items():
        data=load(ncdir)
        xs=[]; ys=[]
        for foc in sorted(data):
            hfrs=[h for cx,cy,h in data[foc] if in_region(cx,cy,reg) and h>0]
            if len(hfrs)>=3:
                xs.append(foc); ys.append(np.median(hfrs))
        near=[h for cx,cy,h in data.get(NEAR,[]) if in_region(cx,cy,reg) and h>0]
        nstar=len(near); scat=float(np.std(near)) if len(near)>2 else float('nan')
        minpos,r2=parab_fit(xs,ys)
        mp=f"{minpos:.0f}" if minpos else "  -"
        r2s=f"{r2:.3f}" if r2 is not None else "  -"
        print(f"{RNAME[reg[0]]:<11} {nc:<9} {nstar:>11} {mp:>9} {r2s:>6} {scat:>16.3f}")
    print()
