#!/usr/bin/env python
"""Build per-image golden.json sidecars from SNR candidates filtered by montage QA confirmation.
golden = QA-confirmed SNR candidates; confidence tier from SNR (>=12 high, 8-12 medium, 5-8 low)."""
import json, glob, os, re
SP='/tmp/claude-1000/-mnt-c-Users-ghili-src-nina-plugins/719a8c41-3665-4049-8052-5fd141432f61/scratchpad'
TASKS='/tmp/claude-1000/-mnt-c-Users-ghili-src-nina-plugins/719a8c41-3665-4049-8052-5fd141432f61/tasks'
RF="/mnt/d/Tilt Calibration Bank/cwhite/TiltCalibration_20260621_214304/01_Baseline/AutoFocus_20260621_214312/attempt01"
foc2file={int(re.search(r'Focuser(\d+)\.fits$',f).group(1)):os.path.basename(f) for f in glob.glob(RF+"/*.fits")}

def tier(s): return 'high' if s>=12 else 'medium' if s>=8 else 'low'

# QA confirmations: f2701 from its own run; the rest from the all-frames run.
conf_by_foc={}
qa1=json.load(open(f'{TASKS}/wr410ju3e.output'))['result']
conf_by_foc[2701]=set(i for i in qa1['confirmed'])
qaA=json.load(open(f'{TASKS}/w0whu2tqr.output'))['result']['byFoc']
for foc,d in qaA.items():
    conf_by_foc[int(foc)]=set(d['confirmed'])

summary=[]
for foc,fn in sorted(foc2file.items()):
    cand=json.load(open(f'{SP}/snr_{foc}_k5.json'))
    conf=conf_by_foc.get(foc,set())
    stars=[{'x':round(c['x']-c['bw']/2),'y':round(c['y']-c['bh']/2),'w':c['bw'],'h':c['bh'],'confidence':tier(c['snr'])}
           for i,c in enumerate(cand) if i in conf and i<len(cand)]
    out={'imageFile':fn,'focuserPosition':foc,'schemaVersion':1,'method':'SNR-ref(k5)+montageQA','stars':stars}
    json.dump(out,open(os.path.join(RF,fn+'.golden.json'),'w'),indent=1)
    hi=sum(1 for s in stars if s['confidence']=='high')
    summary.append((foc,len(cand),len(stars),hi))
print('foc   cand  golden  high(SNR>=12)')
for foc,c,g,h in summary: print(f'{foc}  {c:5d}  {g:5d}   {h}')
print('total golden stars', sum(g for _,_,g,_ in summary))
