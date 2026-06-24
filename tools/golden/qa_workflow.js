export const meta = {
  name: 'golden-qa-bank',
  description: 'Bounded LLM vision QA of the UNCERTAIN-tier SNR montages across AF-bank runs',
  phases: [{ title: 'QA' }]
}
// COMPACT arg-driven (Workflow scripts have no filesystem): the driver passes a few ints per frame, and the script
// reconstructs montage paths + global indices itself (golden_prep renders the uncertain tier contiguously, so a
// cell's GLOBAL index into snr_<foc>.json = b + m*grid^2 + cell, b = the frame's auto-confirmed high-tier size).
//   args = { base, grid?:6, runs:[ { tag, frames:[ { foc, n, b } ] } ] }
// The HIGH tier (SNR>=threshold) is auto-confirmed by build_goldens.py and is NOT in this worklist.
// Returns { byKey: { "<tag> <foc>": { confirmed:[globalIdx...], donut:[globalIdx...] } } } for persist_qa.py.
let A = args
if (typeof A === 'string') { try { A = JSON.parse(A) } catch (e) { A = {} } }
const base = A && A.base
const grid = (A && A.grid) || 6
const per = grid * grid
const runs = (A && A.runs) || []
const pad3 = (m) => String(m).padStart(3, '0')
const work = []
for (const r of runs)
  for (const fr of r.frames)
    for (let m = 0; m < fr.n; m++)
      work.push({ tag: r.tag, foc: fr.foc, m, b: fr.b, file: `${base}/${r.tag}/f${fr.foc}/montage_${pad3(m)}.png` })
log(`QA ${work.length} uncertain-tier montages across ${runs.length} run(s)`)
const SCHEMA = { type:'object', additionalProperties:false,
  properties:{ real:{type:'array',items:{type:'integer'}}, donut:{type:'array',items:{type:'integer'}} }, required:['real'] }
function prompt(path, g) {
  return `Open this image with the Read tool: ${path}
It is a ${g}x${g} grid of small crops (cells), numbered 0..${g*g-1} in READING ORDER (row-major: top-left=0, left-to-right then next row). Each cell is centered on a faint candidate source; a small cyan square marks the exact center.
For EACH cell: is there a REAL star whose center is INSIDE the cyan box? A real star = a compact concentrated bright source with a core/glow clearly above the speckle noise, OR a clear defocused disk/ring/crescent (donut). REJECT if the center box is on plain noise/empty, a hot-pixel single speck, a diffraction spike, or the only source is off to the side (not centered). These are the FAINT/uncertain tier, so be careful but decisive.
Return JSON {"real":[cell indices 0..${g*g-1} with a real CENTERED star], "donut":[subset that are donuts/rings]}.`
}
// Server-side rate-limiting throttles sustained image-heavy bursts at the default ~16-way pipeline concurrency,
// so process in small SEQUENTIAL chunks (effective concurrency = CHUNK) — slower but it doesn't trip the limiter.
const CHUNK = (A && A.chunk) || 4
const results = []
for (let i = 0; i < work.length; i += CHUNK) {
  const batch = work.slice(i, i + CHUNK)
  const r = await parallel(batch.map(w => () =>
    agent(prompt(w.file, grid), { label:`qa:${w.tag.slice(0,12)}:${w.foc}:${w.m}`, phase:'QA', schema:SCHEMA, model:'sonnet', effort:'low' })
      .then(r => ({ w, real:(r&&r.real)?r.real:[], donut:(r&&r.donut)?r.donut:[] }))))
  results.push(...r)
  if ((i / CHUNK) % 25 === 0) log(`QA progress: ${Math.min(i + CHUNK, work.length)}/${work.length}`)
}
const byKey = {}
for (const it of results.filter(Boolean)) {
  const key = `${it.w.tag} ${it.w.foc}`
  if (!byKey[key]) byKey[key] = { confirmed: [], donut: [] }
  const g0 = it.w.b + it.w.m * per
  for (const cell of it.real) if (cell >= 0 && cell < per) byKey[key].confirmed.push(g0 + cell)
  for (const cell of it.donut) if (cell >= 0 && cell < per) byKey[key].donut.push(g0 + cell)
}
for (const k of Object.keys(byKey)) log(`${k}: ${byKey[k].confirmed.length} confirmed`)
return { byKey }
