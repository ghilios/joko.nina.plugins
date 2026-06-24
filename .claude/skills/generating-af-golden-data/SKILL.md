---
name: generating-af-golden-data
description: Use when building or refreshing the detector-independent golden star reference (per-image *.golden.json) for AF-bank runs — the recall/precision ground truth that bank-verify / golden eval score against. Covers snr_ref + montage + LLM QA + build_goldens, including the rate-limit and XISF/Bayer gotchas.
---

# Generating AF golden data

## Overview

The golden set is a **detector-independent** reference of the real stars in each AF frame, stored per image as
`<frame>.golden.json` (`{imageFile, focuserPosition, stars:[{x,y,w,h,confidence}]}`) beside the frame. It is the
ground truth `bank-verify` / `golden eval` score recall/precision against. Built once and reused across detector
configs (it does not depend on detector settings). Tools live in `tools/golden/`.

**Method:** an independent SNR/connected-component detector on the *linear* frame (`snr_ref.py`, different blind
spots than HF) → **auto-confirm the SNR≥12 high tier** (real after saturation-masking + area filtering; this is the
recall@≥12 reference) → **LLM montage-QA only a bounded slice of the uncertain SNR<12 tier** (for precision) →
assemble. Do NOT build the reference by pure LLM vision de-novo (see `.claude/docs/golden-star-set.md` for why).

## Environment / prerequisites

- **Windows + WSL** (TestApp is a Windows exe; Python tools run in WSL). **Run `bank-donut-meta` first** (the
  running-af-bank-validation skill) so each run's `run_meta.json` carries `donutAware` — that flag decides which
  runs need snr_ref `--donut`.
- **NINA profile**: `export-linear` loads the active NINA profile (it uses NINA's XISF/Bayer loader); pass
  `--profile-id <guid>` if there's no active profile.
- **Python deps**: needs `numpy` + `scipy` + `Pillow`. Bare WSL often lacks pip — bootstrap:
  `curl -sSL https://bootstrap.pypa.io/get-pip.py | python3 - --user` then `python3 -m pip install --user numpy scipy Pillow`.
- **The bank loop + `<runtag>` convention**: runs are `attempt*` folders holding `*_Focuser<NNNN>.fits/.xisf`
  frames. `<runtag>` = the run's path relative to the bank root with `/` and spaces → `_`
  (e.g. `mufti/AutoFocus_.../attempt01` → `mufti_AutoFocus_..._attempt01`). `--snr-dir`, `--qa-dir`, and
  `golden_prep`'s `--out` must all use the same `$SP/<runtag>`.

## Pipeline

```bash
SP=<scratch>/golden_gen ; BANK="D:\Autofocus Bank" ; BANKW=/mnt/d/Autofocus\ Bank
EXE=./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe
python3 -c "import numpy,scipy; from PIL import Image" || python3 -m pip install --user numpy scipy Pillow

# 1. Linear mono FITS for XISF/Bayered runs (snr_ref reads ONLY mono FITS; NINA loader reads XISF + debayers).
$EXE export-linear --runs "$BANK"        # writes <frame>.linear.fits beside each frame (skip-existing)

# 2. Per run (loop attempt folders): snr_ref + bounded uncertain-tier montages.
find "$BANKW" -type d -iname 'attempt*' | while read d; do
  runtag=$(echo "$d" | sed "s#$BANKW/##; s#[/ ]#_#g")
  donut=""; grep -q '"donutAware": true' "$d/run_meta.json" 2>/dev/null && donut="--donut --sat-radius 60"
  python3 tools/golden/golden_prep.py --run-dir "$d" --out "$SP/$runtag" $donut --budget-montages 4
done                                     # -> per run: snr_<foc>.json, f<foc>/montage_*.png, manifest.json

# 3. Compact QA worklist across all prepped runs (small enough to pass inline as Workflow args).
python3 tools/golden/build_qa_worklist.py --scratch-root "$SP" --out worklist.json --max-montages-per-frame 4

# 4. LLM montage QA — invoke the Workflow TOOL (not bash). MUST chunk to dodge rate-limiting (gotcha #1):
#       Workflow({ scriptPath: "tools/golden/qa_workflow.js", args: <contents of worklist.json with "chunk":4 added> })
#    args is the worklist OBJECT itself ({base,grid,runs}) + chunk — NOT a file path (Workflow scripts can't read
#    files). Each montage agent returns {"real":[cells], "donut":[cells]}; the script maps cells->global indices and
#    returns { byKey: { "<runtag> <foc>": {confirmed:[...], donut:[...]} } }. Extract that byKey from the workflow's
#    task .output JSON (result.byKey) and write it to qa_result.json.

# 5. Persist QA + assemble goldens (auto-confirms SNR>=12 + unions QA-confirmed uncertain), looping runs again.
python3 tools/golden/persist_qa.py --bykey qa_result.json --scratch-root "$SP"   # -> qa_<foc>.json per run
for man in "$SP"/*/manifest.json; do
  rundir=$(python3 -c "import json,sys;print(json.load(open(sys.argv[1]))['runDir'])" "$man"); runtag=$(basename "$(dirname "$man")")
  python3 tools/golden/build_goldens.py --run-dir "$rundir" --snr-dir "$SP/$runtag" --qa-dir "$SP/$runtag" --auto-confirm-snr 12
done                                     # -> <frame>.golden.json sidecars beside the frames
```

Confidence tiers: SNR ≥12 high, 8–12 medium, 5–8 low. `golden_prep` writes the run's source dir into
`manifest.json` (`runDir`), so step 5 recovers each run path from there.

## Gotchas (hard-won)

- **Server-side rate-limiting throttles image-heavy QA.** The default ~16-way workflow concurrency trips
  "Server is temporarily limiting requests (not your usage limit)" after ~50–100 montage reads and most agents
  fail. **FIX: run the QA in small SEQUENTIAL chunks** — `qa_workflow.js` takes `chunk` in args (use **4**); it
  processes 4-at-a-time so it never bursts the limiter (488 montages completed with **0** failures at chunk=4 vs
  hundreds of failures at 16). Don't run two image-heavy workflows at once.
- **Half the bank is XISF and some FITS are Bayered (e.g. timmer).** `snr_ref.py` parses ONLY mono FITS.
  `export-linear` MUST run first — it uses NINA's loader (reads XISF, debayers Bayer to luminance) and writes a
  mono FITS the reference can read. `golden_prep` prefers `<frame>.linear.fits`, falling back to a mono `.fits`
  original.
- **Wide-field/deep frames have 10k–100k candidates** → QA-ing the whole uncertain tier is infeasible (~40k
  montages bank-wide). The design is **bounded on purpose**: auto-confirm the SNR≥12 high tier (no QA), and LLM-QA
  only the top ~4 montages/frame of the uncertain tier (highest-SNR-first). **Consequence:** recall@SNR≥12 is exact
  everywhere, but **precision is a lower bound** on deep runs whose uncertain tier is under-sampled. State this when
  reporting precision.
- **Workflow args must be COMPACT.** A per-montage worklist (paths + 36-int maps) is too big to inline. Because
  `golden_prep` renders the uncertain montages contiguously, a cell's global index = `b + montage*36 + cell` where
  `b` = the frame's high-tier count. So `build_qa_worklist` emits only `{base, runs:[{tag, frames:[{foc, n, b}]}]}`
  (~6 KB for the whole bank) and `qa_workflow.js` reconstructs montage paths + indices itself.
- **Workflow scripts have no filesystem access** — everything the script needs comes via `args`; agents read the
  montage PNGs with the Read tool. `qa_workflow.js` also defensively `JSON.parse`s string args (the runtime
  sometimes delivers args as a JSON string → otherwise `args.work` is empty and 0 agents run).
- **Python env is bare in WSL** — no pip/scipy/Pillow by default. Bootstrap: download `get-pip.py`
  (`bootstrap.pypa.io`), `python3 get-pip.py --user`, `pip install --user scipy Pillow` (network works).
- **snr_ref `--donut` is slow** (multi-scale matched filter on the full frame, ~1 min/frame); budget ~75 min for
  the bank's compute (export-linear + snr_ref + montage render). It's all detector-independent → cache it.
- **Driver-loop pitfall:** a shell `ls a.fits b.xisf` test to detect frames returns non-zero if EITHER glob has no
  match, silently skipping pure-mono-FITS runs. Let `golden_prep` discover frames, or test each glob separately.

## Reference

- Method + the "pure-vision doesn't work" lesson: `.claude/docs/golden-star-set.md`.
- Tools: `tools/golden/{snr_ref,qa_montage,golden_prep,build_qa_worklist,persist_qa,build_goldens}.py`,
  `qa_workflow.js`, `README.md`.
- Consumer of the goldens: the **running-af-bank-validation** skill.
