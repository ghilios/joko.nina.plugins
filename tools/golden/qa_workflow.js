export const meta = {
  name: 'golden-qa-all',
  description: 'LLM vision QA of SNR candidate montages across all sweep frames',
  phases: [{ title: 'QA' }]
}
const CFG = {
  grid: 6,
  base: '/mnt/c/temp/hf-golden/qa_all',
  frames: [
    { foc: 2613, n: 15 }, { foc: 2635, n: 19 }, { foc: 2657, n: 26 }, { foc: 2679, n: 46 },
    { foc: 2723, n: 44 }, { foc: 2745, n: 36 }, { foc: 2767, n: 23 }, { foc: 2789, n: 15 }
  ]
}
const PER = CFG.grid * CFG.grid
const SCHEMA = { type:'object', additionalProperties:false,
  properties:{ real:{type:'array',items:{type:'integer'}}, donut:{type:'array',items:{type:'integer'}} }, required:['real'] }
function prompt(path, grid) {
  return `Open this image with the Read tool: ${path}
It is a ${grid}x${grid} grid of small crops (cells), numbered 0..${grid*grid-1} in READING ORDER (row-major: top-left=0, left-to-right then next row). Each cell is centered on a candidate source; a small cyan square marks the exact center.
For EACH cell: is there a REAL star whose center is INSIDE the cyan box? A real star = a compact concentrated bright source with a core/glow clearly above the speckle noise, OR a clear defocused disk/ring/crescent (donut). REJECT if the center box is on plain noise/empty, or the only source is off to the side (not centered).
Return JSON {"real":[cell indices 0..${grid*grid-1} with a real CENTERED star], "donut":[subset that are donuts/rings]}. Judge by eye; be decisive.`
}
const work = []
for (const fr of CFG.frames)
  for (let mi = 0; mi < fr.n; mi++)
    work.push({ foc: fr.foc, mi, first: mi*PER, file: `${CFG.base}/f${fr.foc}/montage_${String(mi).padStart(3,'0')}.png` })
log(`QA ${work.length} montages across ${CFG.frames.length} frames`)
const results = await pipeline(work,
  (m) => agent(prompt(m.file, CFG.grid), { label:`qa:${m.foc}:${m.mi}`, phase:'QA', schema:SCHEMA, model:'sonnet', effort:'low' })
    .then(r => ({ m, real:(r&&r.real)?r.real:[], donut:(r&&r.donut)?r.donut:[] })))
const byFoc = {}
for (const it of results.filter(Boolean)) {
  const f = it.m.foc
  if (!byFoc[f]) byFoc[f] = { confirmed: [], donut: [] }
  for (const lo of it.real) if (lo>=0 && lo<PER) byFoc[f].confirmed.push(it.m.first+lo)
  for (const lo of it.donut) if (lo>=0 && lo<PER) byFoc[f].donut.push(it.m.first+lo)
}
for (const f of Object.keys(byFoc)) log(`f${f}: ${byFoc[f].confirmed.length} confirmed`)
return { byFoc }
