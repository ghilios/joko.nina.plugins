export const meta = {
  name: 'adversarial-doc-review',
  description: 'Adversarial review of the MkDocs manual (documentation/docs): broken math/LaTeX rendering (especially raw pipes inside table-cell math), AI-writer tells including confessional/self-aware "Honest limit" headings, and house-voice drift. It discovers the page list, runs a formatting lens and a style lens on each page, then adversarially verifies every finding before reporting it, so house-voice headings and prose-only pipes do not survive as false positives.',
  whenToUse: 'Before merging documentation changes, or to sweep the whole user-facing manual for rendering bugs and AI-writer tells. Pass args.pages (paths relative to documentation/docs) to narrow the sweep, or args.docsDir to point at a different docs root.',
  phases: [
    { title: 'Discover' },
    { title: 'Review' },
    { title: 'Verify' },
  ],
}

// ---------------------------------------------------------------------------
// The adversarial documentation reviewer for the Hocus Focus MkDocs manual.
//
// Renderer: MkDocs Material + pymdownx.arithmatex (generic) + MathJax 3.
// Inline math is \( \), display math is \[ \]. Absolute-value bars must be
// \lvert ... \rvert: a bare | inside a table cell is parsed as a COLUMN
// separator, which truncates the cell and dumps literal broken LaTeX.
//
// House style: .claude/docs/documentation-style.md.
//
// All paths are relative to the repository root (the working directory the
// subagents run in), so this workflow is portable across checkouts.
// ---------------------------------------------------------------------------

const DOCS = (args && args.docsDir) || 'documentation/docs'
const STYLE_GUIDE = '.claude/docs/documentation-style.md'

const PAGES_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: { pages: { type: 'array', items: { type: 'string' } } },
  required: ['pages'],
}

const FINDINGS_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object', additionalProperties: false,
        properties: {
          severity: { enum: ['critical', 'high', 'medium', 'low'] },
          location: { type: 'string', description: 'file + quoted phrase or line number' },
          issue: { type: 'string' },
          suggestedFix: { type: 'string', description: 'the exact corrected line/text to write' },
        },
        required: ['severity', 'location', 'issue', 'suggestedFix'],
      },
    },
    overallAssessment: { type: 'string' },
  },
  required: ['findings'],
}

const VERDICT_SCHEMA = {
  type: 'object', additionalProperties: false,
  properties: {
    isReal: { type: 'boolean', description: 'true ONLY if the finding survives an honest refutation attempt' },
    why: { type: 'string' },
    finalFix: { type: 'string', description: 'the exact corrected text to apply (may refine the suggested fix)' },
  },
  required: ['isReal', 'why'],
}

// --- Lens 1: formatting / rendering correctness ----------------------------
const fmtPrompt = (path, page) => `You are a Markdown + MathJax rendering checker for a MkDocs Material site.
Math is rendered by pymdownx.arithmatex + MathJax: inline math is \\( ... \\), display math is \\[ ... \\].
Open and read the RAW source of ${path} (page: ${page}). Use grep to list every Markdown table row (a line starting with a pipe) that also contains inline or display math, so you can inspect each for the pipe-truncation bug below.

Find every construct that will render broken or malformed:

1. **[HIGHEST PRIORITY] A raw pipe \`|\` inside math on a Markdown TABLE ROW.** A table row is any line starting with \`|\`. Markdown parses \`|\` as a COLUMN separator, so a \`|\` inside \\( ... \\), \\[ ... \\], or \$ ... \$ on such a row splits the cell in the middle of the math and leaves a dangling \`\\(\` with no close. The rendered cell then shows literal broken LaTeX (e.g. a cell that reads \`\\(R = 1 / (2000\\,\` with an orphan \`K\` in the next column). The usual culprits are absolute-value bars \`|K|\`, \`|x|\`, norms, or \`\\mid\`. The correct fix is to write the bars as \`\\lvert ... \\rvert\` (or \`\\lVert ... \\rVert\` for a norm) — NOT \`\\|\` (which is a norm, not a single bar). Note: a \`|K|\` in ordinary PROSE (not a table row) renders fine; do NOT flag those.
2. **Unbalanced or mismatched math delimiters:** an opening \\( with no \\), \\[ with no \\], an odd count of \$ or \$\$ on a line, or \\( closed by \\]. These dump literal LaTeX into the page.
3. **Malformed tables:** header / separator / body rows whose column counts do not line up, a missing separator row, or stray leading/trailing pipes that add phantom columns.
4. **Mixed math delimiter styles** on the page (some \$...\$, some \\(...\\)) — flag for consistency with the manual convention (the manual uses \\( \\) / \\[ \\]).
5. **Other rendering breakage:** unescaped \`<\`/\`>\` read as an HTML tag, broken admonition indentation (\`!!!\`), broken image/link syntax, stray backslashes, un-closed code fences.

For each issue: give the file + exact quoted line (with line number), say exactly what renders wrong and why, and give the exact corrected line. Severity: a broken table/math cell is \`high\`; cosmetic inconsistency is \`low\`. If the page is clean, return an empty findings array and say so in overallAssessment.`

// --- Lens 2: style / AI-writer tells (incl. confessional framing) ----------
const stylePrompt = (path, page) => `You are a professional technical editor enforcing this manual's house style (see ${STYLE_GUIDE}). Read ${path} (page: ${page}) and skim the OTHER pages in the same folder so you judge against the established voice, not against your own defaults.

Flag AI-writer tells and voice drift, each with a concrete rewrite:

1. **[NEW, high priority] Confessional / self-aware "honesty" framing.** The tell is an AI narrating its own candor instead of just stating the fact. Flag headings such as "Honest limit", "In all honesty", "To be fair", "Full disclosure", "A confession", "The honest truth", "Real talk", "Let's be honest", and body phrases such as "to be honest", "honestly", "let's be honest", "in fairness", "candidly", "we'll admit", "truth be told", "in the interest of transparency", "I'll be upfront", "if we're being honest". The technical content under such a heading is usually worth keeping — the fix is to state the limitation plainly under a descriptive, house-voiced heading (e.g. "What it does not recover", "Remaining limits", "Where it still misses") and drop the self-aware framing.
2. **Em-dashes** — the single biggest tell. Flag every em-dash (—); replace with a comma, parentheses, colon, period, or a split sentence. (One em-dash for genuine emphasis is tolerable; several per paragraph is not.)
3. **Filler / throat-clearing:** "it's worth noting", "in essence", "simply", "seamlessly", "of course", "needless to say", "at the end of the day".
4. **Buzzwords:** "robust", "leverage", "delve", "powerful", "seamless", "utilize", "cutting-edge".
5. **Reflexive bolding** of every other phrase; the "not just X, but Y" construction; rule-of-three cadence (three parallel clauses for rhythm rather than content).
6. **Voice / terminology drift** from neighboring pages; admonitions that merely restate the main flow; UI labels that do not match the exact in-app text.

IMPORTANT — respect the house voice: plain descriptive section headings such as "The problem", "The justification", "What each knob fixes", "How the ... works" are the established style, NOT tells. Only report genuine issues. If the page reads clean, return an empty findings array and say so.`

// --- Verify: adversarial refutation of each raw finding --------------------
const verifyPrompt = (path, f) => `Adversarially verify one documentation-review finding BEFORE it is applied. Open ${path} at the cited location and actively try to REFUTE the finding. Default to isReal=false unless the evidence is clear.

Finding (lens=${f.lens}, severity=${f.severity}):
  location: ${f.location}
  issue: ${f.issue}
  suggestedFix: ${f.suggestedFix}

Refutation checklist:
- FORMATTING: Does the flagged construct actually render broken? A pipe only breaks a cell if it is BOTH inside math AND on a Markdown table row (a line starting with \`|\`); a \`|K|\` in ordinary prose renders fine — REFUTE those. Confirm delimiters are genuinely unbalanced by reading the whole line. Confirm the proposed fix is valid MathJax (\\lvert / \\rvert are valid) and preserves the mathematical meaning.
- STYLE: Is this a genuine AI tell, or the manual's established house voice / a real technical term? A descriptive heading that matches neighboring pages is house voice, not a tell — REFUTE those. Confirm the rewrite preserves the meaning and reads in the house voice (plain, direct, active).

Return isReal (true only if it survives refutation), a one-line why, and finalFix: the exact corrected text to apply (refine the suggestedFix if needed).`

// ---------------------------------------------------------------------------
// Discover the page list (the script sandbox has no filesystem access, so an
// agent globs the docs tree). Override with args.pages to narrow the sweep.
phase('Discover')
let pages = (args && Array.isArray(args.pages) && args.pages.length) ? args.pages : null
if (!pages) {
  const disc = await agent(
    `List the Markdown pages of the documentation manual to review. From the repository root run: find ${DOCS} -name '*.md'. Return each page path RELATIVE to ${DOCS} (for example "settings/donut-aware.md", "overview/sensor-model.md"). Exclude anything under an assets/ or javascripts/ subdirectory. Return the list sorted.`,
    { label: 'discover-pages', phase: 'Discover', schema: PAGES_SCHEMA },
  )
  pages = (disc && disc.pages) || []
}
if (!pages.length) {
  log('No documentation pages found to review.')
  return { pagesReviewed: 0, rawFindings: 0, confirmedCount: 0, confirmed: [], refuted: [] }
}
log(`Adversarial doc review over ${pages.length} pages: formatting/rendering + style/AI-tells, then adversarial verify.`)

phase('Review')
const results = await pipeline(
  pages,
  // Stage 1 — review each page with both lenses in parallel.
  (page) => {
    const path = `${DOCS}/${page}`
    return parallel([
      () => agent(fmtPrompt(path, page), { label: `fmt:${page}`, phase: 'Review', schema: FINDINGS_SCHEMA }),
      () => agent(stylePrompt(path, page), { label: `style:${page}`, phase: 'Review', schema: FINDINGS_SCHEMA }),
    ]).then(([fmt, sty]) => ({
      page, path,
      findings: [
        ...(((fmt && fmt.findings) || []).map(x => ({ lens: 'formatting', ...x }))),
        ...(((sty && sty.findings) || []).map(x => ({ lens: 'style', ...x }))),
      ],
    }))
  },
  // Stage 2 — adversarially verify each raw finding for this page.
  (rev) => {
    if (!rev || !rev.findings.length) return { page: rev && rev.page, verified: [] }
    return parallel(rev.findings.map(f => () =>
      agent(verifyPrompt(rev.path, f), { label: `verify:${rev.page}`, phase: 'Verify', schema: VERDICT_SCHEMA })
        .then(v => ({ page: rev.page, ...f, verdict: v || { isReal: false, why: 'verify agent failed' } }))
    )).then(verified => ({ page: rev.page, verified }))
  },
)

const all = results.filter(Boolean).flatMap(r => r.verified || [])
const sev = { critical: 0, high: 1, medium: 2, low: 3 }
const confirmed = all
  .filter(f => f.verdict && f.verdict.isReal)
  .sort((a, b) => sev[a.severity] - sev[b.severity])
const refuted = all.filter(f => !(f.verdict && f.verdict.isReal))

return {
  pagesReviewed: pages.length,
  rawFindings: all.length,
  confirmedCount: confirmed.length,
  confirmed: confirmed.map(f => ({
    page: f.page, lens: f.lens, severity: f.severity,
    location: f.location, issue: f.issue,
    fix: (f.verdict && f.verdict.finalFix) || f.suggestedFix,
    why: f.verdict && f.verdict.why,
  })),
  refuted: refuted.map(f => ({ page: f.page, lens: f.lens, location: f.location, issue: f.issue, why: f.verdict && f.verdict.why })),
}
