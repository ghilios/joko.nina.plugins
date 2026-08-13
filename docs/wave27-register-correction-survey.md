# Wave 27 — register-correction survey: the F31 misreading, inventoried

**Working document.** Produced by the wave-27 survey agent, 2026-08-13, branch
`ghilios/synthetic-af-bank-followups-wave27`. No code, no commits, no edits to any file listed below —
this is the line-anchored inventory the controller decides from.

---

## 0. THE ARITHMETIC, REPRODUCED — this settles it

Every claim in this survey rests on one reproduction. Wave 26's re-score
(`/mnt/d/hf_w26/pd08/F31_truth_rescore.txt`) and `golden eval`'s own report are **both correct**; they
report **two different quantities**, and wave 26 read the first as if it were the second.

Re-derived here from the same on-disk artifacts (`/mnt/d/hf_w25/table18/D08_c11_2800mm/attempt01/detected_f*.csv`
against `/mnt/d/SyntheticAutofocusBank/D08_c11_2800mm/attempt01/*.golden.json` + `*.truth.json`, centre-in-box
match on `stars` as the run used, 12.0 px radius for protection):

| focuser | det | **not in `stars`** | in golden `unresolved` | within 12 px of `omitted`/`merged-into` | within 12 px of ANY truth | **FP after protection** |
|---|---|---|---|---|---|---|
| 13672 | 10 | 4 | 1 | 0 | 0 | **3** |
| 13754 | 16 | 3 | 0 | 3 | 3 | **0** |
| 13836 | 48 | 19 | 7 | 12 | 19 | **0** |
| 13918 | 92 | 27 | 9 | 18 | 27 | **0** |
| 14000 | 119 | **38** | **15** | **23** | 38 | **0** |
| 14082 | 97 | 32 | 9 | 23 | 32 | **0** |
| 14164 | 45 | 16 | 7 | 9 | 16 | **0** |
| 14246 | 16 | 2 | 1 | 1 | 2 | **0** |
| 14328 | 15 | 9 | 1 | 0 | 0 | **8** |
| **total** | 458 | **150** | **50** | **89** | **137** | **11** |

- The **150** and the **137** are wave 26's numbers, reproduced exactly.
- `golden eval`'s reported `FP=11` is reproduced exactly
  (`/mnt/d/hf_w25/table18/D08_c11_2800mm/attempt01/golden_eval.txt`: `OVERALL: TP=308 FP=11 FN=7 precision=0.966`,
  per-frame FP column `3 0 0 0 0 0 0 0 8`).
- **50 + 89 = 139 of the 150 were already excluded** by the run that produced the owner's table: 50 by the
  golden's own `unresolved` boxes, 89 by `TruthProtection`. Wave 26's re-score consulted **neither**.
- The controller's `D08 @ 14000` decomposition is confirmed to the unit: **119 = 81 TP + 15 `unresolved` +
  23 truth-protected, FP = 0.**
- **The "13 junk" figure is also wrong**, by 2: `150 − 137 = 13` detections have no truth star within 12 px,
  but **2 of them (one per wing frame) sit inside a golden `unresolved` box** and are therefore already excluded.
  The correct count of genuinely spurious detections on `D08` is **11**, which is what `golden eval` prints.

**Wiring confirmed live at B15.** `GoldenEvalRunner.cs:301-307` and `BankVerifyRunner.cs:464-466` both call
`TruthProtection.ExcludeProtected(GoldenMatch.ExcludeUnresolved(...))`. The repair landed 2026-08-03 in
`aaf26e8` (14:56 EDT), refined by `5c382a1` (17:04 EDT); `867d41c` re-baselined. `aaf26e8` touched **both**
runners in the same commit.

---

## 1. FINDINGS, ranked by load on the owner's deliverable

Severity key: **FALSE** (states something untrue) · **STALE** (was true, superseded) ·
**MISLEADING** (technically defensible, reads wrong) · **CORRECT-AND-LOAD-BEARING** (cite as counter-evidence,
do not touch) · **GAP** (nothing false; a missing disclosure that let the misreading happen).

---

### FINDING 1 — **FALSE** · `docs/synthetic-af-bank-results-table.md:7-24` · *the owner's deliverable, top of page*

**#1 most load-bearing.** This is the banner the owner reads first.

**Line 7, as written:**
```
## READ THE PRECISION COLUMN AS A LOWER BOUND
```

**Lines 13-17, as written:**
```
**Of the 150 detections the golden calls false, 137 (91.3 %) match a REAL rendered star.** The golden reference
lists **9** stars on a frame where the renderer placed **126**. `D08`'s true precision is therefore nearer
**0.997** than the **0.966** in the table below, and every `1.000` in the column is a floor rather than a
ceiling -- the reference under-lists, most heavily at extreme defocus, and the detector is scored down for
finding what the reference omitted ([F31](followups.md)).
```

Four separate false statements:
1. *"the precision column is a lower bound"* — it is not; the column is truth-protected at source.
2. *"`D08`'s true precision is nearer 0.997 than 0.966"* — `0.966` **is** the truth-protected number
   (`308/(308+11)`). `0.997` is not a precision the bank can produce under any policy; it is an artifact of
   re-deriving FP from `stars` alone and then re-adding 137.
3. *"every `1.000` in the column is a floor rather than a ceiling"* — the nineteen `1.000` rows all read
   `FP=0` **after** protection (verified in all 20 `golden_eval.txt` files, §2 below). 1.000 is a ceiling by
   arithmetic; nothing can lift it.
4. *"the detector is scored down for finding what the reference omitted"* — it is not, and has not been since
   2026-08-03.

**Lines 19-21, as written:**
```
**The 13 genuine junk detections are real, and they are localised:** all 13 fall on the two extreme wing frames
and **zero** on any of the seven interior frames. That is a detector-at-its-limit observation, not a reference
artifact, and it is the only measured false-positive behaviour on the bank.
```
**MISLEADING + off by 2.** The localisation claim is **correct and survives** — but the count is **11**, not 13
(§0), and it does not need the re-score to establish: `golden_eval.txt`'s own per-frame FP column already says
`3` and `8` on the wings and `0` on all seven interior frames.

**Line 23-24** (*"Recall is unaffected by this correction and remains the weak axis"*) — **CORRECT and worth
keeping**, but it is currently framed as a rider on a correction that did not happen.

**Minimal replacement for lines 7-21:**
```
## The precision column is truth-protected, and it is a measurement — not a lower bound

Scored with the F31 repair live (`TruthProtection`, on `develop` since 2026-08-03, wired into both
`GoldenEvalRunner.cs:301-307` and `BankVerifyRunner.cs:464-466`). A detection landing on a real rendered star
the golden policy dropped -- tier `omitted` or `merged-into` -- is excluded from the false-positive count, at
exactly the match radius matching itself uses. `D08`'s `0.966` is `308 TP / (308 + 11 FP)` with that protection
applied; the nineteen `1.000` rows carry `FP = 0` after it. **1.000 is a ceiling here, not a floor.**

**The 11 genuine junk detections are real, and they are localised:** `golden eval`'s own per-frame FP column for
`D08` reads 3 on `13672`, 8 on `14328`, and **zero on all seven interior frames**. That is a
detector-at-its-limit observation, and it is the only measured false-positive behaviour on the bank.

**Recall is the weak axis**, and it is tier-dependent, which is why `recall@high` and `recall@all` are both
printed and a bare "recall" never is.
```
*(Also worth adding to line 3-4's provenance block: the run's scoring mode, since the artifact does not record
it — see FINDING 12.)*

---

### FINDING 2 — **FALSE** · `docs/waves22+-handoff-prompt.md:172` · *the top backlog item the next run opens on*

**#2 most load-bearing.** This directs the next wave's first 1-2 hours at work that is already done.

**As written (line 172, the `#1` row):**
```
| **1** | **Re-measure precision against `*.truth.json`, not `*.golden.json`, across the bank** | **~1–2 h, NO
product code, NO binary** | **3, and it re-scores the owner's table** | **NO** | **Now the prerequisite for
every objective-shaped decision, and demonstrably cheap.** ... Measured on `D08` on 2026-08-13: **91.3 % of
golden-false detections are REAL rendered stars**, `0.30` expected by chance. **This is why [F23](followups.md)
is `won't fix as written` — its evidence base was this same artifact — so without a truth-based pass, F23 and
[F83](followups.md) are BOTH undecidable.** The owner's results table's precision column is a lower bound until
this runs |
```

Three defects: (a) the item is **already discharged** (wave 2, 2026-08-03) and shipped; (b) *"the precision
column is a lower bound until this runs"* is false; (c) the F23 clause is **half true and must be split** —
F23's *original* evidence base genuinely was the pre-repair metric (correct), but that does **not** make F23 or
F83 "undecidable" today, because F23 was already re-measured at `afbank-verify/5`
(`followups.md:1458-1490`) and the re-measurement is what voided it.

**Minimal replacement — strike the item and replace it with what actually remains:**
```
| ~~**1**~~ | ~~**Re-measure precision against `*.truth.json`, not `*.golden.json`**~~ **ALREADY DONE —
2026-08-03, wave 2 (`aaf26e8` / `5c382a1`), shipped as `TruthProtection` and wired into BOTH
`GoldenEvalRunner` and `BankVerifyRunner`.** Wave 26's `91.3 %` re-derived false positives from the golden's
`stars` list alone, ignoring both the golden's `unresolved` boxes and `TruthProtection`; of its 150,
**139 were already excluded** by the run that produced the table (50 unresolved + 89 truth-protected) and
`golden eval` reported **FP = 11**, precision **0.966**, exactly as printed. **The owner's table is correct as
published.** F23 was re-measured at `afbank-verify/5` and is voided on that re-measurement, not undecidable.
See [F31](followups.md) | done | 3 | no | struck |
| **1** | **Disclose the scoring mode in `golden eval`** — it prints none, so no reader of a `golden_eval.txt`
can tell whether protection was applied | **~30 m, TestApp only** | instrument integrity | yes | `bank-verify`
prints `scoring: golden+truth-protected (N ... protected)` (`BankVerifyRunner.cs:302-306`) and persists
`scoringMode`/`protectedStars`; `golden eval` emits nothing equivalent to console, `golden_eval.txt`, either
CSV, or any JSON. **This omission is the direct cause of the wave-26 misreading** |
```

**Line 171 (the struck `~~1~~` row)** carries the same `91.3 % reference omission` clause — **FALSE**, same fix.

**Line 188, as written:**
```
**Is another wave worth it? YES, and the answer changed on 2026-08-13.** The old item 1 ran and was refuted,
which removed the last measured support for acting on the sensitivity pin — but the same afternoon's work
showed that **the bank's precision numbers are measured against a reference that under-lists by an order of
magnitude at defocus.** So the next run has a genuine, binary-free, ~1–2 h opening item (the truth re-score)
whose result decides two long-open entries (F23, F83) and re-scores the owner's deliverable. ...
```
**FALSE** (the middle clause and everything downstream of it). Minimal replacement:
```
**Is another wave worth it?** The old item 1 ran and was refuted, which removed the last measured support for
acting on the sensitivity pin. The truth re-score that looked like a follow-on **was already shipped in wave 2**
and the bank's precision numbers are truth-protected; what the 2026-08-13 exercise actually exposed is that
`golden eval` does not disclose its scoring mode, which is a ~30 m instrument fix. **After that, item 8 —
recall on wide fields — is the largest untouched product gap in the series** and is worth a design before it is
worth an arm.
```

**Lines 14 / 17 / 67** (the three-line state block and the "next" pointer) route the reader to §1c; they carry
no false precision claim themselves, but *"one item needs ~2 minutes and no binary"* (line 17) refers to the
already-struck P-D08 item. **STALE**, cosmetic.

---

### FINDING 3 — **FALSE** · `docs/waves27+-autonomous-prompt.md:152-163` · *§7, the charter for the run in progress*

**#3 most load-bearing.** Verbatim propagation of FINDING 2 into the live charter's "what is open" section.

**As written (lines 154-160):**
```
**Start there.** Its item **1** is the one to open on:

> **Re-measure precision against `*.truth.json`, not `*.golden.json`.** ~1–2 h, **no product code, no binary, no
> gate.** The renderer's complete star list ships beside every frame — **126** stars where the golden lists **9**
> at extreme defocus. Measured on `D08`: **91.3 %** of golden-false detections are **real rendered stars**, 0.30
> expected by chance. **[F23](followups.md) is `won't fix as written` because its evidence base was this same
> artifact**, so until a truth-based pass exists, **F23 and [F83](followups.md) are both undecidable** and the
> owner's results table's precision column is a lower bound.
```

**Minimal replacement:**
```
**Start there.** Its item 1 is **struck** — the truth re-score it proposes shipped on 2026-08-03 (`aaf26e8`,
`TruthProtection`, wired into both `GoldenEvalRunner` and `BankVerifyRunner`), and wave 26's `91.3 %` was a
re-derivation of the PRE-REPAIR quantity. The owner's table is correct as published. The item that replaces it:

> **Make `golden eval` disclose its scoring mode.** ~30 m, TestApp only. `bank-verify` prints
> `scoring: golden+truth-protected (N real-but-unboxed truth stars protected ...)` and persists
> `scoringMode`/`protectedStars`; `golden eval` prints nothing, writes nothing, and has no JSON at all — which
> is exactly how a post-repair report was read as a pre-repair one ten days later.
```

Note lines 165-170 (**recall on wide fields**) and 171-178 (**fenced/rejected**) are unaffected and correct.

---

### FINDING 4 — **FALSE** · `docs/followups.md:432-514` · **F84**, the register entry that carries the misreading

**#4 most load-bearing** — this is where the next reader looks it up.

**Line 459-462 (state-of-play item 4), as written:**
```
4. **It does not measurably cost precision.** `P-D08` **REFUTED at n = 3**: `D01` (gate 0.2234) and `D16`
   (0.1969) both carry base precision **1.000 with ZERO false positives**. And `D08`'s apparent cost is
   **91.3 % reference omission** — 137 of its 150 golden-false detections match a **real rendered truth star**
   at the same 12 px radius, against **0.30** expected by chance. That is [F31](#f31)'s mechanism, quantified.
```
**The first sentence is CORRECT and stands.** The `91.3 %` sentence is **FALSE**. Minimal replacement for the
second half:
```
   And `D08`'s residual `11` false positives are **already truth-protected** — the run that produced the table
   excluded 139 of 150 non-`stars` detections (50 by the golden's own `unresolved` boxes, 89 by
   `TruthProtection`). Wave 26's `91.3 %` re-derived the PRE-REPAIR quantity; it does not describe the scored
   metric. `D08`'s `0.966` is a measurement.
```

**Lines 476-479, as written:**
```
**But do NOT simply switch it on.** F23 is `Won't fix as written` **because its evidence base was VOID** — the
precision collapse it was built to stop (0.993 -> 0.451 on `D09`) was measured against the same under-listing
golden this entry quantifies at 91.3 %. **Enabling a penalty calibrated against void evidence would trade real
recall for an artifact.** The decision needs (2) first.
```
**MISLEADING — half correct, and this is the half the survey brief warns about.** F23's *wave-1* evidence
genuinely was pre-repair, and *"do not switch it on"* stands. What is false is *"this entry quantifies at
91.3 %"* (it does not; F31 quantified it at 96.1 % on D09 pre-repair and then **repaired it**) and *"the
decision needs (2) first"*. Minimal replacement of the last two clauses:
```
... was measured against the pre-repair golden-only metric that [F31](#f31) repaired on 2026-08-03. Re-measured
at `afbank-verify/5`, F23's effect is real but ~1/5 the reported size (`followups.md` F23's void banner).
**Enabling a penalty calibrated against the pre-repair numbers would trade real recall for an artifact** — but
the re-measurement that decides it already exists.
```

**Lines 481-486 (item 2 of "what is owed"), as written:**
```
**(2) Re-measure precision against `truth.json`, not `golden.json`. ~1-2 h, no product code.** This is the
prerequisite for every objective decision, and it is now demonstrably cheap: ... A truth-based precision pass
would give **the first honest precision numbers on this bank** and would say whether a precision term is worth
adding at all. **Without it, F23 and F83 are both undecidable.**
```
**FALSE in full — delete the item and renumber.** *"the first honest precision numbers on this bank"* is
precisely what wave 2 delivered. Minimal replacement:
```
**(2) ~~Re-measure precision against `truth.json`~~ — ALREADY SHIPPED (2026-08-03, `aaf26e8` + `5c382a1`).**
`TruthProtection` protects every `omitted`/`merged-into` truth star from FP scoring at the match radius, in
BOTH `golden eval` and `bank-verify`, and the null control (`precisionNull` 0.000-0.012 on the wave-2 offline
matrix; 0.000-0.169 across all 51 `bank-verify` config rows) shows the repaired metric measures rather than
saturates. What is NOT shipped is **disclosure**: `golden eval` prints no scoring mode. ~30 m, TestApp only.
```

**Line 508, as written:** `**no measured precision** — \`P-D08\` refuted at n = 3, and \`D08\`'s cost 91.3 % artifact.`
**FALSE** in its second clause. Replace with: `... and \`D08\`'s residual 11 FPs are already truth-protected and
confined to the two wing frames.` The **kill list itself (lines 503-508) is correct and stands.**

**Lines 512-514 (the one-sentence version), as written:**
```
**The pin is currently harmless, is a real optimum of an objective that cannot see precision, and the only thing
worth doing before touching it is measuring precision against the renderer's truth instead of a reference that
under-lists by an order of magnitude at defocus.**
```
**FALSE in its second half.** Minimal replacement:
```
**The pin is currently harmless, is a real optimum of an objective that cannot see precision, and the precision
measurement that would price it already exists — the bank has been scored against the renderer's truth since
2026-08-03, and it reads 0.966-1.000.**
```

**Lines 496-499 (item 4, the 13 junk detections)** — **MISLEADING, off by 2**: it is **11**, and the claim needs
no re-score at all (`golden_eval.txt`'s own per-frame FP column already localises them). Otherwise correct and
worth keeping as an open item.

**Line 435** (`reframed by [F31](#f31) and [F23](#f23)`) — leave; F31 *is* the right cross-reference, it was
just read backwards.

---

### FINDING 5 — **STALE/FALSE** · `docs/followups.md:650-661` · **F83** caveat 3

**As written (656-661):**
```
3. **`D08`'s 11 false positives sit exactly where F31 says the reference is incomplete** — **3 on frame `13672`
   and 8 on frame `14328`, the two extreme wing frames, and 0 on all seven interior frames.** F31 measured the
   mechanism on **this dataset by name**: ... The base cell accepts 10 and 15 on frames whose golden
   holds 9. **So the +0.034 may be real faint stars the golden omits rather than junk**, and separating the two
   needs a re-score against each frame's own `*.truth.json` — F31's own method, **not run**.
```

The **frame localisation is correct**. The conclusion is **FALSE**: the separation was already performed by the
scorer. Verified here: on `13672` and `14328`, **zero** of the 11 residual FPs are within 12 px of *any* truth
star. Minimal replacement of the final sentence:
```
   **The separation is already in the number.** Those 11 are the residue after `TruthProtection` excluded every
   detection within the match radius of an `omitted`/`merged-into` truth star; re-checked against
   `*.truth.json`, **none of the 11 has any truth star within 12 px.** They are junk, and the `+0.034` is a
   real precision gain — one confined to two extreme-defocus frames.
```
This is a **strengthening** of F83's `P-D08` result, not a weakening: caveat 3 currently withholds a conclusion
the data supports.

**Caveats 1 and 2 (lines 652-655) are CORRECT-AND-LOAD-BEARING and must not be touched.** Caveat 2's quote of
F31's saturation warning is still exactly right, and the null control (FINDING 15) is its answer.

---

### FINDING 6 — **STALE** · `docs/followups.md:682` · F83 option (c)

**As written:** `... But it makes \`J\` depend on the golden, whose precision F31 shows is a **lower bound**
that evaporates at the sweep wings`

**Minimal replacement:** `... But it makes \`J\` depend on the golden. The golden's \`stars\` list DOES evaporate
at the sweep wings (D08: 81 at focus, 9 at the extreme frame) — precision is protected against that,
**recall@all is not**, so a labelled \`J\` would be reading a recall denominator that shrinks with defocus.`

*(This preserves the objection, which is real, and re-bases it on the half of the mechanism that is still true.)*

---

### FINDING 7 — **STALE** · `docs/followups.md:690` · F83's next step

**As written:** `**Score it against \`*.truth.json\` as well as the golden**, or caveat 3 above applies to the
replication too.`

**Minimal replacement:** `Its \`golden eval\` score is already truth-protected, so caveat 3 does not apply to
the replication.` *(The replication itself already ran — see the struck item 1 in the handoff — so this line
may simply be deleted.)*

---

### FINDING 8 — **MISLEADING (title only)** · `docs/followups.md:2851` · **F31**'s heading

**As written:**
```
### F31 — Synthetic-bank precision is NOT exact: the golden omits real stars, and they score as false positives
```
The status line immediately below says **Done**, but the **title is in the present tense**, and the title is
what gets grepped, quoted and linked. Wave 26 cited F31 four times while reading the title's claim as current.

**Minimal replacement:**
```
### F31 — ~~Synthetic-bank precision is NOT exact: the golden omits real stars, and they score as false positives~~ → REPAIRED: detections on dropped truth stars are protected from FP scoring
```
*(Same treatment F23 and F24 already carry — strikethrough plus the resolution — so the register is internally
consistent about how a closed-and-inverted entry is titled.)*

**Optional, and cheap:** a one-line marker at line 2896, immediately before *"Why it matters — this also
inverts the bank's selling point"* — the paragraph wave 26 quoted — reading
`**(As found, 2026-08-03. Repaired the same day; see "Re-scored with the repair in place" below.)**`. That
paragraph (2896-2899) is **CORRECT AS HISTORY** and must stay; it just needs a tense marker, since it is the
single most quotable sentence in the entry and is three paragraphs above its own repair.

---

### FINDING 9 — **HISTORICAL RECORD, annotate in place** · `docs/synthetic-af-bank-followups-w26-pd08-followon.md:103-152`

This is where the 91.3 % was produced. **It is the record of a wave that ran and it must not be rewritten to
conclude otherwise.** The measurement itself is arithmetically correct; only its interpretation is wrong.

**Where the annotation belongs: immediately after line 103's heading**, i.e. between
`# F31's caveat, SETTLED — 91.3 % of \`D08\`'s "false positives" are real stars the golden omits` (line 103)
and the `Measured 2026-08-13T09:29Z` paragraph (line 105) — so no reader reaches the table without it.

**Annotation text:**
```
> ## ⚠ CORRECTED 2026-08-13 (wave 27) — THIS SECTION MISREADS ITS OWN INPUT
>
> The measurement below is arithmetically correct and reproduces exactly. **Its interpretation is not.** The
> `FP vs golden` column re-derives false positives from the golden's `stars` list ALONE. The scorer that
> produced the owner's table excludes two further populations: the golden's own `unresolved` boxes
> (**50** of the 150) and, since the [F31](followups.md) repair of 2026-08-03 (`aaf26e8`, `TruthProtection`,
> wired into `GoldenEvalRunner.cs:301-307`), every detection within the match radius of an `omitted`/
> `merged-into` truth star (**89** of the 150). `golden eval` reported **FP = 11**, precision **0.966** —
> already truth-protected. **The `150` is the PRE-REPAIR quantity and the `0.997` does not exist under any
> scoring policy.** The two conclusions below invert accordingly: conclusion 1 is withdrawn; conclusion 2
> stands, with the count corrected from 13 to **11** (two of the 13 sit inside a golden `unresolved` box).
```

The heading itself (line 103) should also be struck through in place —
`# ~~F31's caveat, SETTLED — 91.3 % ...~~ → the caveat was already closed in wave 2; see the correction below`
— since it is what the handoff and the charter quote.

**Lines 143-150** (the "What this does to the sensitivity story" bullets) carry the error forward. Line 145
(`- \`D08\`'s apparent precision cost is **91.3 % reference omission**;`) needs the same in-place strike.
**Lines 149-152 (the F83 paragraph) are CORRECT and unaffected** — `J` still carries no precision term, and the
final sentence's irony now cuts the other way, which is worth recording rather than deleting.

---

### FINDING 10 — **HISTORICAL RECORD, annotate in place** · `docs/synthetic-af-bank-followups-wave26-results.md:475-483` (§8.3 caveat 3)

Wave 26's results doc predates the re-score (it says the re-score was *"not run"*), so its text is **honest for
the wave it records** — it is the downstream `w26-pd08-followon.md` that ran it and misread it. Two touches:

**Lines 481-483, as written:**
```
   stars the golden omits, rather than junk.** Distinguishing the two requires re-scoring against each frame's
   own `*.truth.json` — F31's own method — **which this wave did not run**.
```
**Annotation, appended in place (do not alter the sentence):**
```
   > **Corrected 2026-08-13 (wave 27):** the distinction was already made by the scorer. `golden eval` has
   > applied `TruthProtection` since 2026-08-03, so these 11 are the residue AFTER truth protection; none has a
   > truth star within 12 px. They are junk. The follow-on that claimed 91.3 % re-derived the pre-repair
   > quantity — see `docs/synthetic-af-bank-followups-w26-pd08-followon.md`.
```

**Lines 539-541 (§ option (c)):** `it makes \`J\` depend on the golden, and §8.3 plus [F31](followups.md) say
the golden's precision is a **lower bound** that evaporates at the sweep wings.` — **STALE**, same correction as
FINDING 6, but as an in-place annotation rather than a rewrite.

**Line 630** (`| **[F31](followups.md)** | **CITED** — §8.3's caveat is F31's mechanism, on F31's own named
dataset |`) — **MISLEADING**; it cites F31's *defect* where F31's *repair* was the applicable half. One-line
annotation: `(wave 27: cited to F31's pre-repair mechanism; F31's repair had been live since 2026-08-03.)`

**Line 472** (quoting F31's saturation warning) — **CORRECT-AND-LOAD-BEARING**, leave alone.

---

### FINDING 11 — **CONTRADICTION IN SOURCE (root cause)** · `GoldenFromTruth.cs` · *report, do not fix*

`TruthProtection.cs:30-33` names this explicitly:
> *"the policy's own doc comments say so in two places that contradict each other"*

**The two comments, verbatim:**

`Joko.NINA.Plugins/TestApp/SynthBank/GoldenFromTruth.cs:34-36`
```csharp
/// <summary>The star was dropped entirely: it produces no box anywhere in <see cref="GoldenFrame"/>,
/// so a detector reporting something at its location should be scored as a false positive.</summary>
public const string Omitted = "omitted";
```

`Joko.NINA.Plugins/TestApp/SynthBank/GoldenFromTruth.cs:76-84`
```csharp
/// <summary>
/// Peak-pixel SNR floor: at/above this (but below <see cref="LowSnr"/>) a star is real but
/// <see cref="SyntheticTier.Unresolved"/> — neither a required find nor a false positive. Below it the
/// star is <see cref="SyntheticTier.Omitted"/> entirely. This idealized noise model has no matched
/// filter and no local background estimate, so below ~3.5σ its own peak-pixel test cannot vouch for
/// visibility with any confidence; scoring a detection there as a false positive would be punishing a
/// detector for something the reference itself cannot certify either way.
/// </summary>
public double UnresolvedSnr { get; init; } = 3.5;
```

They state opposite policies for the *same* population. **`:34-36` is the one that is now factually wrong**:
since `aaf26e8`, a detection on an `omitted` star is **not** scored as a false positive on the synthetic bank.

**A third site repeats the wrong version at runtime** — `GoldenFromTruth.cs:744-748` writes it into every
golden sidecar's per-star `Reason` field:
```csharp
reason = $"peak SNR {F(comp.CombinedSnr)} < unresolvedSnr {F(thresholds.UnresolvedSnr)}"
    + (members.Count > 1 ? $" (combined over {members.Count} merged stars)" : "")
    + " -> omitted; a detection here should count as a false positive";
```
This string is **on disk in every `*.golden.json` in the bank**, so anyone inspecting a sidecar reads the
pre-repair policy from the data itself. That is a strong candidate for the actual root cause of the wave-26
misreading and is worth a wave-27 fix on its own.

**Minimal replacements** (report only — the survey does not apply them):
- `:34-36` → `/// <summary>The star was dropped from the golden entirely: it produces no box anywhere in <see cref="GoldenFrame"/>. On the SYNTHETIC bank a detection at its location is NOT a false positive — <see cref="TruthProtection"/> protects it from FP scoring at the match radius (F31, 2026-08-03), because the reference cannot certify a sub-3.5σ star either way. The tier means "absent from the golden", not "spurious".</summary>`
- `:748` → `" -> omitted; TruthProtection protects a detection here from FP scoring (F31)"`

---

### FINDING 12 — **GAP (the mechanism that allowed all of the above)** · `GoldenEvalRunner.cs`

`golden eval` **never states which scoring policy it used.** See §3, answer 3. This is the single change that
would have made the wave-26 misreading impossible, and it is ~30 m of TestApp code.

---

### FINDING 13 — **STALE (retired charter)** · `docs/waves22-27-autonomous-prompt.md:216`

**As written:** `... **Score against \`*.truth.json\` as well as the golden** — [F31](followups.md) |`
Superseded by `docs/waves27+-autonomous-prompt.md`, and the item is struck in the handoff. The file carries
**no superseded banner** — worth one line at the top rather than an edit to line 216. Low priority.

---

### FINDING 14 — **GAP** · `.claude/docs/golden-star-set.md`

Documents `golden eval` end-to-end for the **real** bank and never mentions that on the **synthetic** bank the
same command applies `TruthProtection`. Nothing in it is false (line 51's `unresolved` discussion is the real
bank's `budget-truncated` population, a different thing; line 87's *"HF has ~perfect precision but low recall"*
is a real-bank statement and correct). But a reader sent here to interpret a synthetic `golden_eval.txt` learns
nothing about protection. **Minimal addition**, after the `golden eval` bullet around line 61:
```
- **On the SYNTHETIC bank only**, `golden eval` and `bank-verify` additionally protect detections landing on
  real rendered stars the golden policy dropped (`*.truth.json`, tiers `omitted`/`merged-into`) from the
  false-positive count, at the match radius — `TestApp/SynthBank/TruthProtection.cs`, F31. Real-bank runs have
  no truth sidecar, so the path is a no-op and real-bank numbers are unaffected. `bank-verify` announces this
  (`scoring: golden+truth-protected`); **`golden eval` does not**, so date the run against 2026-08-03.
```

---

### FINDINGS 15-20 — **CORRECT-AND-LOAD-BEARING** · cite these, change nothing

| # | file:lines | what it says | why it is the counter-evidence |
|---|---|---|---|
| 15 | `docs/synthetic-af-bank-followups-wave2-results.md:15,36-79,122` | the repair, the offline validation matrix, `precisionNull` 0.000-0.012, `truthViolations` 0 on all 51 rows, and the `/3`-vs-`/4`-vs-truth agreement to 0.006 | **The primary counter-evidence.** It is the document that already answered wave 26's question, on five datasets, in wave 2 |
| 16 | `docs/followups.md:1458-1490` (F23's void banner) | *"THE EVIDENCE BASE ABOVE IS VOID … re-measured at `afbank-verify/5`"*, with the before/after table | Shows F23 was **re-measured, not left undecidable**. Directly refutes FINDING 2's "both undecidable" |
| 17 | `docs/followups.md:2901-2957` (F31's repair half) | the `/4` re-score, the saturation caution, the null control, the `/5` shipping note, the lesson | The entry contains its own refutation of the wave-26 reading, three paragraphs below the sentence wave 26 quoted |
| 18 | `docs/followups.md:8615-8688` (**F11**) | precision is a lower bound on **REAL-bank** runs whose faint tier was `--budget-montages`-truncated | **Do NOT sweep this in.** Different bank, different mechanism (QA budget vs SNR tiering), no `TruthProtection` path (real frames have no `*.truth.json` — `TruthProtection.LoadForImage` returns null and scoring is unchanged), **still valid and still open** |
| 19 | `docs/af-bank-noiseclip-sweep-results.md:103,119,124,149` | *"Precision is a lower bound, not the true value"* | **REAL bank.** F11's mechanism. Correct, leave alone |
| 20 | `docs/synthetic-af-bank-improvements-summary.md:68-69,93` | *"The synthetic bank's precision metric was wrong and is repaired (F31) … Null-controlled"*, *"A repaired `golden eval` precision/recall path (F31), so a bank precision number means what it says"* | The owner-facing summary **already says the repair shipped, and names `golden eval` specifically.** This document and the results-table banner contradict each other today |

**Two more that need a careful read, not a change:**

- `docs/synthetic-af-bank-design.md:12` — *"So `bank-verify` precision is a **lower bound**"* — this is the
  **REAL** bank, in the Problem statement. **CORRECT.** Line 26 — *"precision becomes exact rather than a lower
  bound"* — is the synthetic bank's design promise; F31 found it unmet **and then met it**. Both stand.
- `docs/synthetic-af-bank-baseline-results.md:26` — *"Golden precision (D06, `golden eval`) **1.000** — 205 TP,
  **0 FP**. Exact, not a lower bound"* — this is the sentence F31 opens by quoting as overclaimed. **Its
  numbers are pre-repair and should not be re-used as-is** (the `205 TP` cell is a `/3`-era vector). But the
  *claim* it makes has since been made true: `D06` at wave 18's optimized vector reads `TP=304 FP=0`,
  precision **1.000**, under the repaired metric (§2). The page is pre-repair (created 2026-08-02) and carries
  only a `SUPERSEDED` marker at line 115 pointing at the JSON. **MIXED-VINTAGE, flagged under Q4 below.**

---

## 2. Q1 — Is any published number in this series wrong as a result?

**No. The controller's reading is CONFIRMED: every number in `docs/synthetic-af-bank-results-table.md` is
correct as printed, and only the interpretive banner is false.**

Verified directly against the twenty source reports the table cites
(`/mnt/d/hf_w25/table18/D*/attempt01/golden_eval.txt`, B15, `BuildId d79dae73`, i.e. ten days post-repair):

| | |
|---|---|
| `D08_c11_2800mm` | `OVERALL: TP=308 FP=11 FN=7 precision=0.966` → **`0.966` is correct as printed**, and it is the truth-protected value |
| the other **19** rows | all read `FP=0` → `precision=1.000`, **after** protection. `D01` 13624/0, `D02` 11743/0, `D03` 1690/0, `D04` 28622/0, `D05` 10533/0, `D06` 304/0, `D07` 1266/0, `D09` 222/0, `D10` 111/0, `D11` 297/0, `D12` 232/0, `D13` 598/0, `D14` 767/0, `D15` 244/0, `D16` 447/0, `D17` 182/0, `D18` 29264/0, `D19` 5656/0, `D20` 8683/0 |
| recall columns | untouched by the repair **by construction** — `TruthProtection` changes the FP side only; a protected star is never promoted to a required find (`TruthProtection.cs`, *"Recall is deliberately untouched"*). Every `recall@high` / `recall@all` in the table is comparable to every previously published number |
| the non-precision columns | opt time, `BestJ`/`BaselineJ`, σ, exposure and binning recommendations, `BrightnessSensitivity` — none reads the golden's FP side at all |

`0.997` does not correspond to any policy: truth-direct scoring on `D08` would give `445/458 = 0.972`, and the
protected golden metric gives `308/319 = 0.966`. **The `0.997` in the banner is not a precision figure the bank
can produce.**

**One caveat the correction should carry forward, because it is real and independent of all this:** the
precision instrument has thin dynamic range at these vectors — 19 of 20 rows at exactly 1.000 — which is F83
caveat 2 and F31's own saturation warning. The **answer** to that is the null control (Q2), and the honest
statement is *"precision is a measurement whose null is ~0, and 19 of 20 datasets have no measurable false
positives at their landed vector"* — not *"the column is a lower bound"*.

---

## 3. Q2 — Was F31's null control ever generalised beyond `D09`?

**Partly — and the part that covers the owner's table was never generalised at all, because `golden eval` has
no null control.** There are **two distinct controls** in F31 and they have different reach:

**(a) The coordinate-alignment null** — `followups.md:2886-2887`:
> *"detections match truth at 100% as-is, 2.3% at 0.5× or 2× scale (chance), 0% under a 300 px shift"*

**`D09` only. Never re-run on any other dataset, in any later wave.** It is a *coordinate-system* sanity check
(are truth and detection in the same frame?), and the standing discipline applies verbatim: **a control
demonstrated on one dataset is a control demonstrated on one dataset.**

**(b) The `precisionNull` wraparound control** — `TruthProtection.ShiftForNullControl`, `NullShiftX/Y = 317/211`.
**This one WAS generalised**, and immediately:
- wave 2's offline matrix: **5 datasets** (`D09`, `D13`, `D17`, `D11`, `D12`), nulls **0.0000-0.0093**
  (`synthetic-af-bank-followups-wave2-results.md:41-46`);
- shipped into `bank-verify` at `afbank-verify/5` (`BankVerifyRunner.cs:480-484`) and reported **per config
  row**: *"null control 0.000-0.169, `truthViolations` 0 on all 51 rows"* (wave-2 results line 15) — 17 datasets
  × 3 arms;
- promoted to an instrument gate: `precisionNullMax` 0.20, `truthViolationsMax` 0, in
  `docs/synthetic-af-bank-expectations.json`.

**The gap that matters for wave 27:** `golden eval` implements **neither** control. `ShiftForNullControl` and
`CountWithinRadius` are called **only** from `BankVerifyRunner`. So the twenty rows of the owner's results
table — all produced by `golden eval` — carry **no `precisionNull` and no `truthViolations`**. The 19×1.000
column has never been null-tested *on the runs that produced it*; the assurance is transferred from
`bank-verify` runs at different vectors. That is a defensible transfer and it should be **stated**, not
assumed — and porting both guards to `golden eval` is a natural pairing with FINDING 12.

**No later wave re-ran (a), and no wave has null-controlled a `golden eval` run.**

---

## 4. Q3 — Does `golden eval` report its scoring mode anywhere?

**No. Nowhere. Not to the console, not to `golden_eval.txt`, not to either CSV, and it writes no JSON at all.**
Confirmed by reading every write site in `GoldenEvalRunner.cs`.

**What `bank-verify` does** (`BankVerifyRunner.cs:296-306`, persisted at `:344` / `:671` / `:779`):
```csharp
var scoringMode = truthByFocuser.Count > 0 ? "golden+truth-protected" : "golden";
Console.WriteLine($"  scoring: {scoringMode}"
    + (truthByFocuser.Count > 0
        ? $" ({protectedStars} real-but-unboxed truth stars protected from FP scoring across {truthByFocuser.Count} frames)"
        : " (no truth sidecars; false positives scored against the golden alone)"));
```
plus `scoringMode` / `protectedStars` / `precisionNull` / `truthViolations` / `scoredFraction` persisted per
config row in its JSON.

**What `GoldenEvalRunner` emits, exhaustively:**

| artifact | write site | says anything about protection? |
|---|---|---|
| console header | `:149-150` (`Profile:`, `Output: … params=… match=… iou=… pixelScale=…`) | **no** |
| console per-run | `:196` `matchRadius`, `:260` `pixelScale`, `:271` derived-binning note, `:388` per-frame `golden= accepted= TP= FP= FN=` | **no** |
| `golden_eval.txt` | `:733`; header built at `:663-679` — `params`, `pixelScale`, `matchRadius`, `match`, detector knobs, defocus knobs | **no** |
| `golden_eval_frames.csv` | `:647`; header `focuser,params,golden,accepted,TP,FP,FN,precision,recall,f1,recallHigh,recallHighMed,recallAll,fnAcceptedElsewhere,fnNoCandidate,fnRejected` | **no** |
| `golden_eval_regions.csv` | `:659` | **no** |
| `false_negatives_f*.csv`, `detected_f*.csv` | per frame | **no** (detection dumps only) |
| **any JSON** | — | **`golden eval` writes none** |

Structurally: `WriteReports(...)` (`:631-634`) is not even *passed* the truth dispositions —
`truthDispositions` is a local inside the per-frame loop at `:300` and dies there. Nothing downstream can
report it even if it wanted to.

**What a reader must consult today to learn whether protection was applied — the full list:**
1. The **run's date**, compared against **2026-08-03 ~15:00 EDT** (`aaf26e8`). Nothing else in the artifact
   carries the answer.
2. `git log --format='%h %ad' aaf26e8 5c382a1 -- Joko.NINA.Plugins/TestApp/SynthBank/TruthProtection.cs`, to
   fix that boundary.
3. `GoldenEvalRunner.cs:301-307`, to confirm the call site exists in the binary that ran.
4. The bank folder itself, for `*.truth.json` sidecars beside the frames (absent ⇒ no protection possible).
5. `TruthProtection.cs`'s class doc, for what the protected population is.
6. A `bank-verify` run over the same bank, whose `scoring:` line answers by proxy.
7. **Or**: recompute it, as §0 does — which is what wave 26 half-did and stopped one exclusion short.

`golden_eval.txt` also carries **no BuildId** (checked: neither the report nor
`/mnt/d/hf_w25/table18/D08_c11_2800mm.log`), so even a build-identity route is closed. **This omission is the
proximate cause of the entire misreading.**

---

## 5. Q4 — How far back does the ambiguity go?

### The criterion a reader can apply

**The bright line is the commit `aaf26e8`, 2026-08-03 14:56 EDT** — it wired `TruthProtection` into **both**
`GoldenEvalRunner.cs` and `BankVerifyRunner.cs` in one change. Refinements: `5c382a1` (same day, 17:04 —
symmetric centroid predicate, `precisionNull`, `truthViolations`, `scoredFraction`).

**Which test to apply depends on which tool produced the number:**

| number came from | criterion | reliability |
|---|---|---|
| `bank-verify` | the **`afbank-verify/N` harness tag**, quoted in every doc that reports one. **`/2` and `/3` = PRE-REPAIR. `/4` = repaired but SATURATING (2·HFR protection — reads 1.000 on all 17 datasets; do not trust an all-1.000 `/4` figure). `/5` = the shipped metric.** | **exact and self-declaring** — the tag is in the artifact |
| `golden eval` | **the run's date only.** Before 2026-08-03 15:00 EDT ⇒ pre-repair; after ⇒ repaired. There is no tag, no BuildId, no scoring line | **weak** — external to the artifact; see Q3 |
| a wave-numbered doc | **wave ≤ 1 ⇒ pre-repair. Wave 2 (2026-08-03) is the repair wave. Wave ≥ 3 ⇒ post-repair** | good, and the easiest to apply |

### Documents that are PRE-REPAIR in whole or in part

| file | vintage | status |
|---|---|---|
| `docs/f23-objective-precision-term-results.md` | added 2026-08-03 (`fec6733`), wave 1, `afbank-verify/3` | **Wholly pre-repair.** Its precision numbers (0.451 etc.) are the voided set. Its own `/4` reference near the end is the correction landing |
| `docs/synthetic-af-bank-baseline-results.md` | added **2026-08-02** (`62747f2`), later edited by `867d41c` | **MIXES BOTH, and this is the worst offender.** Line 26 (`Exact, not a lower bound`) and line 30's V2 row (`C0 never below 0.942, config A as low as 0.451`) are `/3`; the V3 matrix added by `867d41c` is `/5`. The `SUPERSEDED` marker at **line 115** is buried mid-document and points at the JSON, not at the headline table. **A reader who stops at the Headline table (lines 20-35) gets pre-repair numbers with no warning** |
| `docs/af-bank-noiseclip-sweep-results.md` | 2026-06-23, `afbank-verify/2` | Pre-repair by date — **but it is a REAL-bank document**, so `TruthProtection` never applied to it and its "lower bound" language is F11's, correct, and unaffected |
| `docs/synthetic-af-bank-design.md` | 2026-08-02 | Design intent, not measurements. Its `/3` reference at line 417 is a pre-repair log excerpt |
| `docs/followups.md` **F23 body** (`:1403-1456`) | wave 1 | Pre-repair — **but correctly fenced** by the void banner at `:1458`. This is the model for how a pre-repair block should be marked |

### Documents that are POST-REPAIR

`docs/synthetic-af-bank-followups-wave2-results.md` (the repair wave, `/4` and `/5` explicitly labelled) and
**everything from wave 3 onward**, including `docs/synthetic-af-bank-results-table.md` (created 2026-08-13,
`7e98915`, scored on B15 `d79dae73`) and all of wave 26. **The `91.3 %` misreading is not a vintage problem —
it is a post-repair document re-deriving a pre-repair quantity by hand.**

### Documents that mix both without saying so

1. **`docs/synthetic-af-bank-baseline-results.md`** — as above. **The one file that needs a vintage banner at
   the top**, not just at line 115. Minimal addition after line 8:
   > `> **VINTAGE.** The Headline and V2 tables on this page were measured at \`afbank-verify/3\`, BEFORE the F31
   > repair of 2026-08-03 (\`aaf26e8\`). Every precision figure in them is the pre-repair quantity. The V3 matrix
   > below is \`/5\` and is current. Recall figures are unaffected in both.`
2. **`docs/synthetic-af-bank-results-table.md`** — mixes a post-repair table with a pre-repair *interpretation*
   (FINDING 1). Fixing the banner fixes the mixing.
3. **`docs/followups.md` F31 itself** — its first half is the defect as found and its second half is the repair,
   separated only by a sub-heading. It is the document wave 26 mis-sampled. FINDING 8's title strike plus the
   one-line tense marker at `:2896` closes this.

---

## 6. Count by severity

| severity | count | findings |
|---|---|---|
| **FALSE** | **5** | 1 (results table), 2 (handoff §1c item 1 + line 188 + line 171), 3 (waves27+ §7), 4 (F84 — five separate passages), 5 (F83 caveat 3, conclusion half) |
| **STALE** | **3** | 6 (F83 option c), 7 (F83 next step), 13 (retired charter) |
| **MISLEADING** | **3** | 8 (F31 title), 10 (wave-26 results §8.3 + line 630), plus the "13 junk" count in 1/4/9 (off by 2) |
| **HISTORICAL — annotate in place, do not rewrite** | **2** | 9 (`w26-pd08-followon.md`), 10 (`wave26-results.md`) |
| **CONTRADICTION IN SOURCE (root cause)** | **1** | 11 (`GoldenFromTruth.cs:34-36` vs `:76-84`, plus the on-disk `Reason` string at `:748`) |
| **GAP** | **2** | 12 (`golden eval` discloses no scoring mode — the proximate cause), 14 (`.claude/docs/golden-star-set.md`) |
| **CORRECT-AND-LOAD-BEARING — cite, do not touch** | **6+2** | 15-20, plus the two careful-read entries (`synthetic-af-bank-design.md`, `synthetic-af-bank-baseline-results.md` line 26) |

**Not swept in, deliberately:** `docs/followups.md` **F11** (`:8615`) and
`docs/af-bank-noiseclip-sweep-results.md` — REAL-bank montage-budget truncation, a different and **still-valid**
mechanism with no `TruthProtection` path (real frames have no `*.truth.json`, so `LoadForImage` returns null and
scoring is byte-identical to before the repair).
