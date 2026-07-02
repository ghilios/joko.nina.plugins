# Manual Documentation Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Burn down the 95 adversarially-confirmed documentation findings in `docs/manual-doc-review-findings.md` across all 27 pages of the MkDocs manual (`documentation/docs/`).

**Architecture:** Docs-only change. The findings file is the work backlog: each finding carries an exact location, the verified issue, and suggested replacement text. Fixes are applied in per-page-group batches, each batch verified with `mkdocs build --strict` and committed separately; a final adversarial-doc-review workflow re-run confirms the backlog is cleared.

**Tech Stack:** MkDocs Material markdown, `mkdocs build --strict`, the repo's `adversarial-doc-review` workflow, git.

---

## Context for a zero-context engineer

- **Repo root:** `/home/ghilios/src/hocus-focus`.
- **Precondition:** PR #117 (`ghilios/tilt-calibration-ux`) must be **merged into `develop`** before starting — the findings were gathered on that branch and three of the affected pages were edited by it. Branch from up-to-date `develop`:
  ```bash
  git checkout develop && git pull && git checkout -b ghilios/manual-doc-cleanup
  ```
  **Never push to `develop` directly.**
- **The backlog:** `docs/manual-doc-review-findings.md` — 95 findings grouped by page, most-severe first within each page. Each finding has `[severity] (lens) location`, an **Issue** paragraph, and a **Suggested fix** (usually exact replacement text). These were produced by a 2-lens-per-page review with 3-vote adversarial verification on 2026-07-02, so false positives were filtered — but they are point-in-time:
  - **Line numbers in the findings are stale.** Use the quoted text as the anchor, never the line number.
  - **Re-verify each finding's premise before applying it.** Especially: (a) findings that quote in-app UI labels must be checked against the current XAML (`Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` and `TiltAdapterWizard/DataTemplates.xaml` — grep for the label text); (b) findings on `overview/tilt-adapter-wizard.md`, `overview/tilt-aberration-inspector.md`, `overview/sensor-model.md`, and `quick-start.md` may have been partially overtaken by PR #117's later commits — if the quoted text no longer exists, mark the finding obsolete in your commit message rather than forcing an edit.
  - Suggested fixes are suggestions. Where a fix conflicts with the style guide or current code, write the correct fix; do not blindly paste.
- **Style contract:** read `.claude/docs/documentation-style.md` in full before Task 1. Hard rules: house voice, no confessional/self-aware headings, no raw pipes inside table-cell math, quote only exact in-app UI text, bold UI labels without quotation marks.
- **Build verification** (run from repo root; config is `mkdocs.yml` at repo root):
  ```bash
  mkdocs build --strict
  ```
  Must stay clean after every batch. Also run the .NET suite once at the end (project invariant, even for docs-only changes):
  ```bash
  dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
  ```
  (On WSL the binary is `dotnet.exe`.)
- **Committing** (GitHub email privacy — author and committer must both use the noreply address):
  ```bash
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>"
  ```

## Batch procedure (identical for Tasks 1–5)

For the task's listed pages, in order:

1. Open the page's section in `docs/manual-doc-review-findings.md` and the page itself.
2. For each finding: locate the quoted text in the current page; re-verify the premise (UI-label claims against the XAML; factual claims against the cited code); apply the suggested fix or your corrected variant; if the premise no longer holds, skip and note it.
3. When two pages share a finding (the findings often say "fix both pages together" — e.g. the `Use RANSAC` → **Align images before matching** rename spans `sensor-model.md` and `tilt-aberration-inspector.md`), apply the shared rename consistently across every occurrence in the batch, and grep the whole manual for stragglers: `grep -rn "Use RANSAC\|Fixed Sensor Center\|Acceptable R² Min" documentation/docs/`.
4. Run `mkdocs build --strict` → must pass.
5. Commit the batch.

---

### Task 1: Landing pages — `index.md`, `quick-start.md`, `overview/index.md` (12 findings)

**Files:**
- Modify: `documentation/docs/index.md` (3 findings)
- Modify: `documentation/docs/quick-start.md` (3 findings)
- Modify: `documentation/docs/overview/index.md` (6 findings)

- [ ] **Step 1:** Read `.claude/docs/documentation-style.md` in full.
- [ ] **Step 2:** Apply the batch procedure to the three pages. Highlights to expect: dropdown labels must match the screenshots (**Star Detector** / **Star Annotator** / **Autofocus**, not "Star Detection"/"Auto Focus"); `overview/index.md` opens with self-citation voice ("in the words of its own description", "Per its documentation") that must be rewritten as direct prose.
- [ ] **Step 3:** `mkdocs build --strict` → PASS.
- [ ] **Step 4:** Commit:
```bash
git add documentation/docs/index.md documentation/docs/quick-start.md documentation/docs/overview/index.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs: fix landing-page findings — exact UI labels, direct voice (review batch 1/5)"
```

---

### Task 2: Detection/AF overview pages — `overview/star-detection.md`, `overview/star-annotation.md`, `overview/autofocus.md`, `overview/hyperbola-fitting.md` (12 findings)

**Files:**
- Modify: `documentation/docs/overview/star-detection.md` (5), `overview/star-annotation.md` (4), `overview/autofocus.md` (1), `overview/hyperbola-fitting.md` (2)

- [ ] **Step 1:** Apply the batch procedure to the four pages.
- [ ] **Step 2:** `mkdocs build --strict` → PASS.
- [ ] **Step 3:** Commit (same env-var pattern): `docs: fix detection/AF overview findings (review batch 2/5)`

---

### Task 3: Tilt/sensor pages — `overview/sensor-model.md`, `overview/tilt-aberration-inspector.md`, `overview/tilt-adapter-wizard.md` (15 findings)

**Files:**
- Modify: `documentation/docs/overview/sensor-model.md` (6), `overview/tilt-aberration-inspector.md` (7), `overview/tilt-adapter-wizard.md` (2)

These pages were edited after the findings sweep (PR #117 Fix commits) — re-verify every premise. The big items: the inspector options table uses code-property names instead of in-app labels (the findings enumerate exact row replacements — cross-check each against `AutoFocus/DataTemplates.xaml` at HEAD); the **Max Stars Per Region** row documents a control that does not exist in any XAML (delete the row); `sensor-model.md` label renames (**Align images before matching**, **Sensor Centered**, **Min R² (rejection)**, **Use affine alignment (diagnostic)**) must be applied consistently on both pages.

- [ ] **Step 1:** Apply the batch procedure to the three pages.
- [ ] **Step 2:** Cross-page consistency grep (expect zero hits after the fixes):
```bash
grep -rn "Use RANSAC\|Fixed Sensor Center\|Acceptable R² Min\|Max Stars Per Region\|Adjustment Required" documentation/docs/
```
- [ ] **Step 3:** `mkdocs build --strict` → PASS.
- [ ] **Step 4:** Commit: `docs: fix tilt/sensor page findings — in-app labels, table accuracy (review batch 3/5)`

---

### Task 4: Optimization pages — all six `optimization/*.md` (27 findings)

**Files:**
- Modify: `documentation/docs/optimization/af-curve-fitting.md` (3), `optimization/index.md` (3), `optimization/labels-recall-precision.md` (4), `optimization/objective-function.md` (5), `optimization/search-algorithm.md` (4), `optimization/search-variables.md` (8)

Highlight: `search-variables.md` misstates the optimizer seed (it is the **default** detection parameters unless "Start from my current settings" is enabled — verified against `StarDetectionOptimizerWizardVM.cs`); the never-worse-than-current guarantee is a separate wizard-level guard. The findings give the corrected paragraph.

- [ ] **Step 1:** Apply the batch procedure to the six pages.
- [ ] **Step 2:** `mkdocs build --strict` → PASS.
- [ ] **Step 3:** Commit: `docs: fix optimization page findings — seed semantics, guarantees (review batch 4/5)`

---

### Task 5: Settings pages — all eleven `settings/*.md` (29 findings)

**Files:**
- Modify: `documentation/docs/settings/acceptance-gates.md` (5), `settings/adaptive-binarization.md` (1), `settings/advanced-debug.md` (3), `settings/contamination.md` (1), `settings/donut-aware.md` (3), `settings/hotpixel-saturation.md` (1), `settings/index.md` (2), `settings/precision-recall.md` (3), `settings/preprocessing.md` (3), `settings/psf-modeling.md` (5), `settings/structure-detection.md` (2)

- [ ] **Step 1:** Apply the batch procedure to the eleven pages.
- [ ] **Step 2:** `mkdocs build --strict` → PASS.
- [ ] **Step 3:** Commit: `docs: fix settings page findings (review batch 5/5)`

---

### Task 6: Final verification — re-run the adversarial review

- [ ] **Step 1:** Run the .NET suite once (project invariant): `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` → all green (docs-only branch, so any failure is pre-existing — investigate before proceeding).
- [ ] **Step 2:** Re-run the repo's adversarial-doc-review workflow over the whole manual. Gotcha: `.claude/workflows/adversarial-doc-review.js` has CRLF line endings, which the Workflow permission dialog rejects — make an LF copy first and invoke by `scriptPath`:
```bash
tr -d '\r' < .claude/workflows/adversarial-doc-review.js > "$SCRATCHPAD/adversarial-doc-review.js"
```
then `Workflow({scriptPath: "$SCRATCHPAD/adversarial-doc-review.js"})` (a full sweep — passing `args.pages` did not narrow the sweep on the 2026-07-02 run, so expect all pages).
- [ ] **Step 3:** Triage the re-run's confirmed findings: fix any that are regressions or misses from Tasks 1–5 (amend the relevant batch commit or add a fixup commit); genuinely new/out-of-scope findings get appended to `docs/manual-doc-review-findings.md` with a note.
- [ ] **Step 4:** Update `docs/manual-doc-review-findings.md` header: add a line noting which findings were applied/obsoleted by this branch, so the file reads as a closed backlog. Commit: `docs: close out manual review backlog; re-review results`
- [ ] **Step 5:** Push and open a PR:
```bash
git push -u origin ghilios/manual-doc-cleanup
gh pr create --base develop --title "Manual documentation cleanup — adversarial review backlog" --body "Applies the 95 adversarially-confirmed findings in docs/manual-doc-review-findings.md (exact UI labels, factual corrections, house-voice fixes) across all 27 manual pages. Verified with mkdocs --strict per batch and a full adversarial-doc-review re-run.

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```
