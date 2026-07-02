# Adversarial doc-review findings — full manual sweep (2026-07-02)

Run during the tilt-calibration-ux branch work. 28 pages reviewed, 95 confirmed findings (each adversarially verified). All findings on the four pages edited by the branch were verified to be PRE-EXISTING; the branch's own doc changes came back clean apart from two small fixes already applied. This report is the backlog for a separate docs-cleanup pass.


## index.md (3)

### [high] (style) documentation/docs/index.md line 42: "select **Hocus Focus** in the Star Detection, Star Annotator, and Auto Focus dropdowns"

**Issue:** UI labels do not match the exact in-app text. Both screenshots on this page and in quick-start.md (assets/screenshots/image-options-dropdown.png, image-options-all.png) show the Image options dropdowns labeled "Star Detector", "Star Annotator", and "Autofocus", not "Star Detection" and "Auto Focus". The page even contradicts itself: the image alt text on line 45 correctly says "Star Detector" while the body text two lines above says "Star Detection". The lead-in also says "turn on the star detector and annotator" (two features) but then instructs setting three dropdowns.

**Suggested fix:**

To turn on its features afterward, go to **Options → Imaging → Image Options** and select **Hocus Focus** in the **Star Detector**, **Star Annotator**, and **Autofocus** dropdowns.

### [medium] (style) documentation/docs/index.md line 6: "It measures HFR with a robust, gradient-aware star detector"

**Issue:** "robust" is explicitly on the banned buzzword list in .claude/docs/documentation-style.md ("avoid ... buzzwords ('robust', 'leverage', 'delve', 'powerful')"). It adds nothing that "gradient-aware" and "accurate" (the wording the Key features bullet on line 22 already uses) do not.

**Suggested fix:**

It measures HFR with an accurate, gradient-aware star detector

### [low] (style) documentation/docs/index.md lines 6-9: "It measures HFR with a robust, gradient-aware star detector and fits PSF models to stars for eccentricity and FWHM measurements; lets you swap in just the star detector or just the annotator without taking both; and builds a full sensor tilt-and-curvature model..."

**Issue:** A single 60+ word sentence built as a three-clause parallel semicolon chain (rule-of-three cadence), against the house preference for short declarative sentences. Its middle clause also duplicates the "Mix and match" admonition on lines 33-36 ("You can use the new star detector or the new annotator independently"), so the same fact is stated twice on one page.

**Suggested fix:**

It measures HFR with an accurate, gradient-aware star detector and fits PSF models to stars for eccentricity and FWHM measurements. It also builds a full sensor tilt-and-curvature model so you can quantify and correct aberrations across the field.


## optimization/af-curve-fitting.md (3)

### [low] (style) documentation/docs/optimization/af-curve-fitting.md, line 95: "the objective hard-fails it gracefully to a run score of 0"

**Issue:** Self-contradictory phrasing: "hard-fails ... gracefully" mixes two opposite ideas. The house term on objective-function.md is plain "hard-fails" (e.g. "the run hard-fails (J_run = 0)"); the softener reads as AI hedging and muddies the meaning.

**Suggested fix:**

    parameters). A run that thin produces a NaN \(\sigma\). The evaluation does not throw; the
    objective hard-fails the run to a score of 0. This is why a usable run needs a real
    sweep, not a couple of frames.

### [low] (style) documentation/docs/optimization/af-curve-fitting.md, line 76: "it tries the candidate models and lets the winner compete on merit"

**Issue:** Garbled logic: the models compete and a winner emerges; the winner does not itself "compete on merit". The sentence as written is nonsensical on a careful read.

**Suggested fix:**

the autofocus engine uses (`AlglibHyperbolicFitting.SelectBestModel`): it tries the candidate models
and picks the winner on merit, rather than forcing a single shape (see

### [low] (style) documentation/docs/optimization/af-curve-fitting.md, lines 128–136: "\operatorname{clamp}_{[0,1]}(R^2)" and "\tau = 2.0" / "knee \(\tau = 2.0\)" / "\(\tau / \text{reduced }\chi^2\)"

**Issue:** Notation drift from the authoritative objective page this page links to: objective-function.md writes the same formula as \(\operatorname{clip}(R^2)\) with knee \(\chi_\tau = 2.0\). A reader moving between the two pages may think \(\tau\) and \(\chi_\tau\) (or clamp and clip) are different quantities.

**Suggested fix:**

\[
S_{\text{fit}} = \operatorname{clip}_{[0,1]}(R^2) \cdot \text{penalty}, \qquad
\text{penalty} = \begin{cases} 1 & \text{reduced }\chi^2 \le \chi_\tau \\[2pt] \dfrac{\chi_\tau}{\text{reduced }\chi^2} & \text{reduced }\chi^2 > \chi_\tau \end{cases}, \quad \chi_\tau = 2.0 .
\]

![S_fit equals clamped R-squared times a reduced-chi-squared penalty that only bites on the high side](../assets/figures/objective-sfit.png){ width=620 }

*Only the **high** side of reduced \(\chi^2\) is penalized. Star-rich fields routinely produce a
reduced \(\chi^2\) well below 1 (over-fit-looking but benign), so values at or below the knee
\(\chi_\tau = 2.0\) carry no penalty at all; above the knee the penalty decays smoothly as \(\chi_\tau / \text{reduced }\chi^2\).*


## optimization/index.md (3)

### [medium] (style) documentation/docs/optimization/index.md line 59: "Optimize for Aberration Inspection"

**Issue:** Quoted UI label does not match the exact in-app text. The wizard checkbox in StarDetection/Optimization/DataTemplates.xaml (line 363) reads "Optimize for aberration inspection (favor more stars)" — sentence case, not title case. House convention elsewhere on this page correctly truncates trailing parentheticals (e.g. "Start from my current settings", "Recover out-of-focus donut stars"), but the capitalization must match the app. (objective-function.md lines 52–53 shares the same title-case mismatch; out of this page's scope but should be fixed together.)

**Suggested fix:**

    **"Optimize for aberration inspection"** instead reweights the objective toward **recovering many

### [low] (style) documentation/docs/optimization/index.md lines 82–87: "w_f\,S_{\text{focus}} ... a further term \( w_l = 0.25 \)"

**Issue:** Math-notation drift from the detail pages this section defers to. index.md writes the objective weights lowercase (\( w_f, w_s, w_c, w_{\text{cov}}, w_l \)) while objective-function.md and labels-recall-precision.md, which the very next line links to for "the full breakdown", consistently use uppercase \( W_f, W_s, W_c, W_{\text{cov}} \) and \( W_\ell \). A reader following the link sees the same formula under different symbols.

**Suggested fix:**

Line 82 (display-math body) becomes:

J_{\text{run}} = \frac{W_f\,S_{\text{focus}} + W_s\,S_{\text{stars}} + W_c\,S_{\text{fit}} + W_{\text{cov}}\,S_{\text{cov}}}{W_f + W_s + W_c + W_{\text{cov}}}

Lines 85–87 become:

with default weights \( W_f = 0.55 \) (focus), \( W_s = 0.20 \) (star count), \( W_c = 0.25 \)
(curve fit), and \( W_{\text{cov}} = 0.05 \) (how well the accepted stars cover the sensor). When
ground-truth labels are present a further term \( W_\ell = 0.25 \) is added and all weights are

### [low] (style) documentation/docs/optimization/index.md lines 122–124: "see the authoritative design spec, `docs/star-detection-optimization-wizard-design.md`"

**Issue:** Voice/convention drift: this is the only page in the entire manual that points readers at a repo-internal design spec (grep over documentation/docs/ confirms no other page does this), and the path is not part of the published MkDocs site, so a reader of the GitHub Pages manual has nothing to follow. "the authoritative design spec" is also mildly puffy framing.

**Suggested fix:**

For the design rationale behind these choices (why a derivative-free pattern search rather than a
smooth solver, why these weights, and why these exclusions), see
`docs/star-detection-optimization-wizard-design.md` in the project repository.


## optimization/labels-recall-precision.md (4)

### [medium] (style) documentation/docs/optimization/labels-recall-precision.md lines 6-7, phrase "the objective gains a fourth term"

**Issue:** Terminology drift from the sibling page: objective-function.md (and OptimizationObjective.cs, Wcov = 0.05) documents five additive sub-scores (S_focus, S_stars, S_fit, S_cov, S_label), so calling S_label "a fourth term" contradicts the page it links to. "Additional" avoids re-counting and stays correct if terms change.

**Suggested fix:**

instead of a proxy, you give it ground truth: hand-drawn **labels**. With labels present, the objective gains an
additional term, \(S_{\text{label}}\), scored directly against the boxes you drew.

### [low] (style) documentation/docs/optimization/labels-recall-precision.md line 9: "Without labels the optimizer runs on the focus / star-count / curve-fit terms alone."

**Issue:** Same drift as above: the label-free objective also includes the region-coverage term (S_cov, weight 0.05) documented on objective-function.md, so the three-term enumeration is incomplete.

**Suggested fix:**

This is entirely optional. Without labels the optimizer runs on the focus / star-count / curve-fit / coverage terms alone.

### [low] (style) documentation/docs/optimization/labels-recall-precision.md lines 35 and 37, bullets "**Recall** — the fraction..." and "**Precision** — the fraction..."

**Issue:** Paired definitional em-dashes; the directly parallel bullets in objective-function.md write these same two definitions as "**Recall** = the fraction..." / "**Precision** = the fraction...". Using "=" removes both em-dashes and makes the two pages' definitions read identically.

**Suggested fix:**

- **Recall** = the fraction of *recall-target* boxes (missed ∪ wrongly-rejected) that now contain at least one
  accepted star center. If you labeled nothing to recover, recall is 1.0 by definition.
- **Precision** = the fraction of *should-reject* boxes that now contain **no** accepted star center, i.e. the
  spurious detection has successfully been excluded. If you labeled no false positives, precision is 1.0.

### [low] (style) documentation/docs/optimization/labels-recall-precision.md line 38: "the spurious detection has successfully been excluded"

**Issue:** "successfully" is a filler adverb (the exclusion either happened or it did not); the sibling page states the same fact without it ("the bad detection has been excluded").

**Suggested fix:**

i.e. the spurious detection has been excluded. If you labeled no false positives, precision is 1.0.


## optimization/objective-function.md (5)

### [medium] (style) documentation/docs/optimization/objective-function.md lines 52-53: "When you select **\"Optimize for Aberration Inspection\"** on the wizard's start page"

**Issue:** UI label does not match the exact in-app text. The wizard start-page checkbox (StarDetection/Optimization/DataTemplates.xaml line 363) reads "Optimize for aberration inspection (favor more stars)" — sentence case with a parenthetical, not title case. Note: optimization/index.md shares this same drift, so the fix should ideally be applied section-wide.

**Suggested fix:**

When you select **"Optimize for aberration inspection (favor more stars)"** on the wizard's start page, the optimizer swaps in a star-count-favoring objective

### [medium] (style) documentation/docs/optimization/objective-function.md line 164: "This term reinforces what the **\"Optimize for Aberration Inspection\"** objective already favors"

**Issue:** Second occurrence of the same UI-label mismatch: in-app text is sentence case ("Optimize for aberration inspection"), not title case.

**Suggested fix:**

    This term reinforces what the **"Optimize for aberration inspection"** objective already favors: stars spread

### [medium] (style) documentation/docs/optimization/objective-function.md lines 158-159: "Coverage is allowed to cost a little focus tightness — recovering stars across the frame is worth a minor rise in \\(\\sigma_{\\text{focus}}\\) — but at one-eleventh..."

**Issue:** Paired em-dashes in one sentence — the classic AI-writer tell the house style explicitly bans ("several per paragraph is not" fine). Sibling pages use parentheses for asides like this. This page carries 6 body-prose em-dashes vs 0-2 on every neighbor.

**Suggested fix:**

The weight is deliberately small (0.05). Coverage is allowed to cost a little focus tightness (recovering stars across the frame is worth a minor rise in \(\sigma_{\text{focus}}\)), but at one-eleventh of the focus weight it cannot override the dominant focus term.

### [low] (style) documentation/docs/optimization/objective-function.md lines 229-232, the "!!! warning" admonition: "With the gates off (default) this penalty is inert and the objective is identical to the weighted-average form above. It exists so the optimizer can safely explore turning the gates on — recovering bloated donuts on the extremes — without learning..."

**Issue:** Two problems: the admonition's first sentence restates the "Off is bit-identical" bullet ten lines above (admonitions should add information, not restate the main flow), and the second sentence contains another paired em-dash aside.

**Suggested fix:**

!!! warning "The defocus-aware gates are opt-in"
    This penalty exists so the optimizer can safely explore turning the gates on (recovering bloated
    donuts on the extremes) without learning to manufacture spurious near-focus stars.

### [low] (style) documentation/docs/optimization/objective-function.md lines 17-18: "produces a wobbly curve and an uncertain minimum — you would land on a slightly different focuser position every run"

**Issue:** Body-prose em-dash where a colon does the same work; part of the page's above-baseline em-dash density.

**Suggested fix:**

    look pretty but scatters the per-position HFR points produces a wobbly curve and an uncertain minimum:
    you would land on a slightly different focuser position every run. Minimizing that uncertainty


## optimization/search-algorithm.md (4)

### [low] (style) documentation/docs/optimization/search-algorithm.md, lines 168-169: "The recommended auto-focus step size is **derived from the winning fit, not searched** — it is not one of the optimizer's variables."

**Issue:** Body em-dash used for an appositive explanation (a colon reads better and the style guide says not to lean on em-dashes), and "auto-focus" drifts from the folder-standard spelling "autofocus" used across index.md, objective-function.md, step-size.md, and af-curve-fitting.md.

**Suggested fix:**

The recommended autofocus step size is **derived from the winning fit, not searched**: it is not one
of the optimizer's variables.

### [low] (style) documentation/docs/optimization/search-algorithm.md, line 134: "matching the auto-focus engine's averaging."

**Issue:** Terminology drift: "auto-focus" where every sibling page in this folder writes "autofocus" (e.g. af-curve-fitting.md "the autofocus engine", objective-function.md "the autofocus step size").

**Suggested fix:**

   matching the autofocus engine's averaging.

### [low] (style) documentation/docs/optimization/search-algorithm.md, line 41 (figure caption): "halves the step when a sweep finds nothing — converging on a local optimum."

**Issue:** Body em-dash where a comma serves; the equivalent caption for the same figure on index.md and search-variables.md uses no em-dash.

**Suggested fix:**

*The compass search starts at the seed, probes each axis by \(\pm\)step, steps to the best improving
neighbor, and halves the step when a sweep finds nothing, converging on a local optimum.*

### [low] (style) documentation/docs/optimization/search-algorithm.md, line 77 (LATE row of the stage table): "everything else — the gate/measure knobs and the synthetic `DefocusAwareGates` switch"

**Issue:** Em-dash inside a table cell introducing an elaboration; a colon is the cleaner house rendering.

**Suggested fix:**

| **LATE** | Re-runs only the cheap gate-and-measure step against an already-built context (a cache hit) | everything else: the gate/measure knobs and the synthetic `DefocusAwareGates` switch |


## optimization/search-variables.md (8)

### [high] (style) documentation/docs/optimization/search-variables.md line 78: "The optimizer starts from your **current** profile values (the seed) and only accepts strictly-improving moves, so the result can never be worse than where you started."

**Issue:** Voice/factual drift from neighboring pages and the code. index.md and search-algorithm.md ("Three guarantees") both state the seed is the fully-default detection parameters unless the user enables "Start from my current settings", and StarDetectionOptimizerWizardVM.cs confirms it (`var seed = seedOverride ?? (StartFromCurrentSettings ? runs[0].Baseline : runs[0].Seed)`). Saying the seed is "your current profile values" also mis-attributes the never-worse-than-current guarantee, which actually comes from a separate wizard-level guard, not from seeding at current.

**Suggested fix:**

The optimizer starts from the seed (the **default** detection parameters, or your **current** settings when you choose *"Start from my current settings"*) and only accepts strictly-improving moves, so the result can never score worse than the seed; on top of that, the wizard never hands back a result worse than your current settings (see [search algorithm](search-algorithm.md#three-guarantees)).

### [medium] (style) documentation/docs/optimization/search-variables.md line 41: "The seed reads your current params (both OFF by default), so the baseline is unchanged"

**Issue:** Same seed-provenance drift as line 78 (the seed is the default parameters unless "Start from my current settings" is enabled), plus the informal "params" where the folder's prose consistently uses "parameters" (code font `StarDetectorParams` is reserved for code references).

**Suggested fix:**

The variable reads the distortion flag as its value and writes the same value to both flags. Both flags are OFF in the default seed parameters, so the baseline is unchanged, and the search may flip the pair on if it helps the curve.

### [medium] (style) documentation/docs/optimization/search-variables.md line 3: "and it leaves everything else at your profile's current values"

**Issue:** Inaccurate under the default seeding: non-curated parameters sit at the seed's values (the fully-default parameters, per index.md), and when the optimized snapshot is applied the remaining knobs follow the Simple-mode preset defaults (per OptimizedStarDetectionSettings.cs: "the remaining advanced knobs continue to follow Simple-mode preset defaults when this snapshot is applied"), not the profile's current values.

**Suggested fix:**

It tunes a deliberately small, **curated set** of knobs that have the largest, most predictable effect on the autofocus curve, and it leaves the rest out of the search entirely.

### [medium] (style) documentation/docs/optimization/search-variables.md line 66: "The EARLY-keyed curated variables are: `NoiseClippingMultiplier`, `StructureLayers`, `NoiseReductionRadius`, `HotpixelThresholdingEnabled`, `HotpixelThreshold`, and the synthetic `DefocusAwareStructure` (it shares the early cache-key property name)."

**Issue:** The list omits `DonutMorphCloseSize`, a curated axis (added when *Recover out-of-focus donut stars* is on) that is in `StarDetector.EarlyCacheKeyProperties` (the code comments call it "EARLY morph-close kernel"). Since the list already includes the donut-gated `DefocusAwareStructure`, the omission makes line 67's "LATE axes are everything else" wrongly imply a `DonutMorphCloseSize` move is a cache hit. (search-algorithm.md's EARLY/LATE table shares this omission and should get the same correction.)

**Suggested fix:**

The EARLY-keyed curated variables are: `NoiseClippingMultiplier`, `StructureLayers`, `NoiseReductionRadius`, `HotpixelThresholdingEnabled`, `HotpixelThreshold`, the synthetic `DefocusAwareStructure` (it shares the early cache-key property name), and, when donut recovery is enabled, `DonutMorphCloseSize` (the early morphological close).

### [low] (style) documentation/docs/optimization/search-variables.md line 1: "# Search Variables — the Curated Search Space"

**Issue:** Em-dash in the H1. Section headings in this folder do use "X — y" separators (house style), but no sibling page title does; the established H1 pattern for a title-plus-subtitle is a colon ("Labels: Recall and Precision").

**Suggested fix:**

# Search Variables: The Curated Search Space

### [low] (style) documentation/docs/optimization/search-variables.md line 31: "That is the **12** always-on axes."

**Issue:** Subject–verb agreement: singular "That is" with the plural "12 always-on axes". (The counts themselves check out against OptimizerVariable.cs: 12 base axes, 21 with the donut master on.)

**Suggested fix:**

Those are the **12** always-on axes.

### [low] (style) documentation/docs/optimization/search-variables.md line 54: "which is then **clamped** to `[Lower, Upper]` and **quantized** to a legal value of its type before being stored"

**Issue:** Order contradicts both the code and the page's own next bullet: `OptimizerVariable.Quantize` rounds/thresholds first and then clamps, and line 57 says "`Math.Round(v, AwayFromZero)`, then clamped".

**Suggested fix:**

Every proposal flows through the variable's descriptor as a `double`, which is **quantized** to a legal value of its type and then **clamped** to `[Lower, Upper]` before being stored:

### [low] (style) documentation/docs/optimization/search-variables.md line 50: "so the seed's `J` is unchanged"

**Issue:** Terminology drift: every neighboring page sets the objective symbol in math mode (\(J\), \(J_{\text{run}}\), \(J_{\text{seed}}\)); code font `J` breaks that convention.

**Suggested fix:**

so the seed's \(J\) is unchanged


## overview/autofocus.md (1)

### [medium] (style) documentation/docs/overview/autofocus.md, lines 9-14 ("**Multiple curve-fitting models** — hyperbolic ..." through "**HFR-improvement validation** — an optional ...")

**Issue:** Six consecutive bullets use an em-dash as the separator between the bold lead-in and its description. This is the classic AI-writer em-dash cluster (six in one list), and it drifts from the established list style on neighboring pages: star-detection.md and hyperbola-fitting.md use '**Bold lead-in.** Sentence.' (e.g. '**Local background plane.** Pixels in an annulus...', '**Consensus outlier rejection.** Each candidate proposes...') or let the bold phrase flow directly into the sentence with no dash. The house style doc explicitly names leaning on em-dashes as the biggest tell to avoid.

**Suggested fix:**

- **Multiple curve-fitting models.** Hyperbolic (several asymmetric variants), parabolic, and trendline fits, each with a goodness-of-fit rejection gate (\(R^2\) or reduced \(\chi^2\)). See [Hyperbolic Curve Fitting](hyperbola-fitting.md) for the model formulas and when each applies.
- **Hybrid model selection.** At the end of a run every hyperbolic model is refit and the one with the least expected error for the best-focus position is kept, so each run self-selects its most trustworthy fit.
- **Weighted fitting.** Each point carries its own measurement uncertainty \(\sigma\) (the per-frame star-HFR scatter), and the fit can weight points by \(1/\sigma^2\) so a noisy point counts for less.
- **Outlier rejection.** An iterative two-tailed Grubbs test removes points that do not belong on the curve (e.g. a frame ruined by a cloud or satellite).
- **Stability reporting.** A leave-one-out (LOO) cross-validation estimates how much the best-focus position would move if any single point were dropped.
- **HFR-improvement validation.** An optional before/after check confirms the run actually made the stars sharper, retrying the run if it did not.


## overview/hyperbola-fitting.md (2)

### [low] (style) documentation/docs/overview/hyperbola-fitting.md line 211: "Leave it on **Hybrid**."

**Issue:** The in-app dropdown label is "Hybrid (Best Fit)" (Description attribute in Interfaces/HyperbolicFitModel.cs, rendered verbatim by EnumStaticDescriptionValueConverter). This is the one place the page directly tells the user what to select, so the first actionable mention should use the exact in-app text; the "Hybrid" shorthand elsewhere on the page is fine once anchored.

**Suggested fix:**

Leave it on **Hybrid (Best Fit)**. Across a large bank of real runs no single fixed model wins on every curve, so

### [low] (style) documentation/docs/overview/hyperbola-fitting.md line 217: "**Tilted** is the best fixed asymmetric model"

**Issue:** When recommending which fixed model to pick from the dropdown, the bolded name should match the exact in-app entry "Tilted Hyperbola" (Interfaces/HyperbolicFitModel.cs). The "Tilted" shorthand is fine in descriptive prose (e.g. the Hybrid selector steps) but not in the pick-this-option sentence.

**Suggested fix:**

near-symmetric curves; **Tilted Hyperbola** is the best fixed asymmetric model; **Smooth Blend** suits curves


## overview/index.md (6)

### [high] (style) documentation/docs/overview/index.md line 3: "in the words of its own description, provides **\"Improved Star Detection, Star Annotation, Auto Focus, and Tilt Correction for NINA.\"**"

**Issue:** AI-summarizer voice: the manual quotes the plugin's own store blurb ("in the words of its own description") instead of stating the facts natively. Neighboring pages (star-detection.md, autofocus.md, star-annotation.md) open with direct declarative prose; the only house-sanctioned quoting is of exact in-app tooltips. A bolded marketing quote as the opening sentence reads as an AI summarizing a source document rather than writing the manual.

**Suggested fix:**

Hocus Focus is a plugin for [NINA](https://nighttime-imaging.eu/) that provides improved star detection, star annotation, auto focus, and tilt correction. It replaces or augments NINA's built-in star handling and auto-focus routines with a more accurate star detector, a fully customizable annotator, a concurrent auto-focus engine, and an aberration inspector that measures backfocus and sensor-tilt errors.

### [high] (style) documentation/docs/overview/index.md line 5: "Per its documentation, you \"can use the new Star Detector or Annotator without requiring both\""

**Issue:** Same source-narration tell: "Per its documentation" is the manual citing itself, and the scare-quoted fragment is stitched into the sentence. This is voice drift from every neighboring page, which states behavior directly. Also, `select **"Hocus Focus"**` doubles up quotes and bold; house style bolds UI labels without quotation marks.

**Suggested fix:**

The plugin is deliberately modular: the star detector, the annotator, and the auto-focus engine can each be enabled independently, so keep whichever pieces you like and leave the rest on NINA's defaults. The features are wired in under **Options → Imaging → Image Options**, where installing the plugin adds **Star Detection**, **Star Annotator**, and **Auto Focus** dropdowns; select **Hocus Focus** in each to turn that feature on.

### [medium] (style) documentation/docs/overview/index.md line 42: "Start at the Simple level: \"higher accuracy (lower HFR Standard Deviation)\" is available \"if parameters are set properly,\" and the defaults are a reasonable starting point."

**Issue:** Two unattributed quote fragments from the plugin description are stitched into one sentence, producing garbled syntax (X "is available" "if Y,") and continuing the quote-the-blurb pattern. State the claim plainly.

**Suggested fix:**

Start at the Simple level: the defaults are a reasonable starting point, and higher accuracy (lower HFR standard deviation) comes from setting parameters well. Move to Advanced only when you need to correct a specific behavior, and reach for the wizard when you want the settings tuned automatically rather than by hand.

### [medium] (style) documentation/docs/overview/index.md line 34: "Hocus Focus exposes \"simpler configuration, with an advanced mode for fine tuning.\""

**Issue:** Unattributed scare quotes around blurb text, again quoting the plugin description instead of writing native prose. "Simpler" is also a dangling comparative with no attribution left to anchor it.

**Suggested fix:**

Hocus Focus is simple to configure by default, with an advanced mode for fine tuning. There are three levels of control, in increasing order of effort:

### [medium] (style) documentation/docs/overview/index.md line 27: "The inspector estimates **backfocus and tilt errors** by running an auto-focus..."

**Issue:** Reflexive bolding: seven bolded phrases in one paragraph, most of them whole descriptive clauses ("**center and corner regions of the sensor split into a 3×3 grid**", "**replay saved AF runs**") rather than the key terms or UI labels the neighboring pages bold. The paragraph is also one very long sentence.

**Suggested fix:**

The inspector estimates **backfocus and tilt errors** by running an auto-focus and computing AF curves for the center and corner regions of the sensor, split into a 3×3 grid. The result is a full sensor tilt and curvature model, which lets it measure backfocus error even when tilt is present. For single exposures it also generates FWHM contour maps and eccentricity vector fields for a quick visual read, offers a 3D visualization of sensor tilt, and can replay saved AF runs. See [Tilt & Aberration Inspector](tilt-aberration-inspector.md), [Sensor Model Fitting](sensor-model.md) for how the tilt and curvature model is fit, and the [Tilt Adapter Wizard](tilt-adapter-wizard.md) for turning a measured tilt into concrete screw adjustments.

### [low] (style) documentation/docs/overview/index.md line 19: "**customizable colors and fonts** ... **dynamic reloading of annotations without re-running star detection**"

**Issue:** Clause-level bolding of feature descriptions, part of the same reflexive-bolding pattern across the feature-area section.

**Suggested fix:**

The annotator controls how detected stars are drawn on top of an image. It offers customizable colors and fonts for the star overlays, and it can reload annotations dynamically without re-running star detection, so you can restyle the overlay or change what is shown without paying for another detection pass. See [Star Annotation](star-annotation.md).


## overview/sensor-model.md (6)

### [medium] (style) documentation/docs/overview/sensor-model.md line 131: "set by **Use RANSAC** (on by default)"

**Issue:** UI label does not match the exact in-app text. The checkbox bound to InspectorOptions.UseRANSAC is labeled "Align images before matching" (AutoFocus/DataTemplates.xaml line ~1873, inside the Experimental expander). "Use RANSAC" is the option's property name, not a label a user can find in the panel. The dependent phrasings "(RANSAC off)" (line 133), "(RANSAC on)" (line 136), and "used when RANSAC is off" (line 153) lean on the same non-existent label. Note: tilt-aberration-inspector.md's options table uses the same wrong name, so fix both pages together.

**Suggested fix:**

In documentation/docs/overview/sensor-model.md:

1. Replace lines 131-132 with:
Two registration approaches are available, set by **Align images before matching** (on by default) under
[Inspector options](tilt-aberration-inspector.md#inspector-options):

2. Replace line 134 (start of first bullet) with:
- **Nearest-neighbor only** (alignment off). Stars are matched directly in their original pixel

3. Replace lines 136-138 (start of second bullet) with:
- **RANSAC alignment first** (alignment on). Every frame is first transformed onto the reference frame by
  a RANSAC-estimated transform (a similarity transform by default, or an affine one when **Use affine
  alignment (diagnostic)** is on), so matching stars land almost on top of each other.

4. Replace the end of the paragraph at lines 152-154 with:
aligns on the first pass, is neither altered nor slowed. If any frame still cannot be placed, star
matching for the whole sweep reverts to the wider nearest-neighbor search radius used when alignment
is off.

Companion fix (per the finding's note) in documentation/docs/overview/tilt-aberration-inspector.md options table, lines 135-136:
| **Align images before matching** | on | on/off | Align frames with RANSAC before matching stars, improving registration robustness. |
| **Use affine alignment (diagnostic)** | off | on/off | Use a 6-DOF affine transform (adds shear) instead of similarity; only enabled when alignment is on. |

### [medium] (style) documentation/docs/overview/sensor-model.md line 192: "With **Fixed Sensor Center** on (the default)"

**Issue:** UI label mismatch. The in-app checkbox is labeled "Sensor Centered" (AutoFocus/DataTemplates.xaml line ~1827; the property is FixedSensorCenter). A reader scanning the panel will not find a control called "Fixed Sensor Center". Shared with the inspector page's options table; coordinate the fix.

**Suggested fix:**

- **The center.** With **Sensor Centered** on (the default), \(X_0\) and \(Y_0\) are pinned to
  zero and the sensor is assumed centered.

### [medium] (style) documentation/docs/overview/sensor-model.md line 245: "below **Acceptable R² Min** (default \(0.05\))"

**Issue:** UI label mismatch. The in-app label for InspectorOptions.AcceptableRSquaredMin is "Min R² (rejection)" (AutoFocus/DataTemplates.xaml line ~2003). "Acceptable R² Min" is derived from the property name and appears nowhere in the panel. Also used in tilt-aberration-inspector.md's options table.

**Suggested fix:**

  when \(R^2\) is below **Min R² (rejection)** (default \(0.05\)) *and* the reduced \(\chi^2\) is also
  poor (above \(5\)).

### [medium] (style) documentation/docs/overview/sensor-model.md lines 137-138: "when **Use Affine\nAlignment** is on"

**Issue:** UI label mismatch. The in-app checkbox is labeled "Use affine alignment (diagnostic)" (AutoFocus/DataTemplates.xaml line ~1888). The docs' title-cased "Use Affine Alignment" drops the "(diagnostic)" qualifier, which is part of the visible label and signals the option's intent.

**Suggested fix:**

a RANSAC-estimated transform (a similarity transform by default, or an affine one when **Use affine
  alignment (diagnostic)** is on), so matching stars land almost on top of each other.

### [low] (style) documentation/docs/overview/sensor-model.md line 47: "## 4-Corners Model"

**Issue:** Heading-case drift. Every other heading on this page ("## The sensor surface model", "## From stars to data points", "## Fitting the surface") and in neighboring pages ("## The tilt plane", "## The calibration loop", "## Weighted fitting and robustness") uses sentence case; this is the lone title-case heading, and it also disagrees with the body text's own "4-corners model".

**Suggested fix:**

## The 4-corners model

### [low] (style) documentation/docs/overview/sensor-model.md line 59: "Each corner's **Adjustment Required** is its"

**Issue:** Bolded as if it were an in-app label, but no UI element says "Adjustment Required"; the corresponding DataGrid columns are headed "Adj Steps" and "Adj Microns" (AutoFocus/DataTemplates.xaml lines 3482/3486). The inspector page uses the same phrase, so either tie the prose to the real column names or unbold it on both pages.

**Suggested fix:**

Each corner's required adjustment (the **Adj Steps** / **Adj Microns** columns) is its
best-focus position minus that mean, reported in focuser steps (and in microns when *Microns per
Focuser Step* is set).


## overview/star-annotation.md (4)

### [low] (style) documentation/docs/overview/star-annotation.md line 3: "which is invaluable when tuning detection or diagnosing focus problems"

**Issue:** "Invaluable" is promotional puffery of the "powerful/robust" family; neighboring pages state what a feature does rather than praising it.

**Suggested fix:**

It is your window into what the detector actually saw, which helps when tuning detection or diagnosing focus problems.

### [low] (style) documentation/docs/overview/star-annotation.md line 118: "Cap labels (**Show All Stars** off, **Maximum Stars** ≈ 200)"

**Issue:** Internal inconsistency: the earlier tip (line 19) advises capping Maximum Stars at "a few dozen", but this recipe says ≈ 200, which is the documented default (line 33) and therefore caps nothing in practice.

**Suggested fix:**

Cap labels (**Show All Stars** off, **Maximum Stars** ≈ 50), then enable the rejection toggles one or two at a time with distinct colors. Walk the gates until the accepted set looks right for your focal ratio and seeing.

### [low] (style) documentation/docs/overview/star-annotation.md lines 112-113: warning after the structure-map paragraph

**Issue:** The warning's first sentence ("a debugging aid for tuning the structure-detection stage") restates line 110 ("exists for algorithm-level tuning rather than routine use"); only "it obscures the underlying image" is new. On a page already carrying 8 admonitions, this one should be folded into the body.

**Suggested fix:**

The mask pixels are blended onto the image in **Structure Map Color** (*"The color of the overlayed structure map"*; magenta/purple, half-transparent by default). This control is only exposed when the detector's debug mode is enabled, since it exists for algorithm-level tuning rather than routine use. Leave it off during normal focusing; the overlay obscures the underlying image.

### [low] (style) documentation/docs/overview/star-annotation.md lines 43 and 102: quoted tooltips "Whether to show the a reticule on each star center" and "Overlays the structure map to aide debugging"

**Issue:** The doc faithfully quotes tooltips that contain typos in the app itself ("the a reticule", "aide debugging" in Resources/OptionsDataTemplates.xaml lines 478 and 494). The doc is correct as verbatim quotation, but readers will attribute the typos to the manual; the fix belongs at the source, then re-quote.

**Suggested fix:**

Apply four exact-string edits (fix the app source first, then re-quote in the doc):

1. Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml line 478 — replace:
    <TextBlock x:Key="ShowStarCenter_Tooltip" Text="Whether to show the a reticule on each star center" />
with:
    <TextBlock x:Key="ShowStarCenter_Tooltip" Text="Whether to show a reticule on each star center" />

2. Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml line 494 — replace:
    <TextBlock x:Key="ShowStructureMap_Tooltip" Text="Overlays the structure map to aide debugging. Original shows the initial structure map after noise clipping and binarization, and Dilated shows it after dilation has been performed" />
with:
    <TextBlock x:Key="ShowStructureMap_Tooltip" Text="Overlays the structure map to aid debugging. Original shows the initial structure map after noise clipping and binarization, and Dilated shows it after dilation has been performed" />

3. documentation/docs/overview/star-annotation.md line 43 — replace:
| Show Star Center | On | on / off | *"Whether to show the a reticule on each star center"*. |
with:
| Show Star Center | On | on / off | *"Whether to show a reticule on each star center"*. |

4. documentation/docs/overview/star-annotation.md line 102 — replace:
**Show Structure Map** overlays the detector's binary structure mask so you can see exactly which pixels were treated as potential star structure. Its tooltip: *"Overlays the structure map to aide debugging. Original shows the initial structure map after noise clipping and binarization, and Dilated shows it after dilation has been performed"*.
with:
**Show Structure Map** overlays the detector's binary structure mask so you can see exactly which pixels were treated as potential star structure. Its tooltip: *"Overlays the structure map to aid debugging. Original shows the initial structure map after noise clipping and binarization, and Dilated shows it after dilation has been performed"*.


## overview/star-detection.md (5)

### [medium] (style) documentation/docs/overview/star-detection.md line 142, "Tune with" cell: "min star box size"

**Issue:** Setting name matches nothing in the product or the rest of the manual. The in-app label (OptionsDataTemplates.xaml) and the settings page (settings/acceptance-gates.md) both call it "Min Bounding Box Size"; a reader searching the Advanced list for "min star box size" will not find it.

**Suggested fix:**

| Too small | bounding box width or height below the minimum box size | min bounding box size |

### [low] (style) documentation/docs/overview/star-detection.md lines 144-150, gate table "Tune with" column

**Issue:** The remaining "Tune with" entries are lowercased ("max distortion", "brightness sensitivity", "star center tolerance", "star peak response", "min HFR", "contamination sensitivity") while the in-app labels and every settings page use exact Title Case. House rule: use the exact in-app UI labels.

**Suggested fix:**

| Too distorted | fill ratio (pixels / \(d^2\)) below the max-distortion threshold | Max Distortion |
| Degenerate | parameters could not be computed | — |
| Low sensitivity | normalized brightness / noise sigma at or below the sensitivity threshold | Brightness Sensitivity |
| Not centered | centroid falls outside the centered acceptance sub-box | Star Center Tolerance |
| Too flat | star median at or above peak-response × peak (a flat blob, not a peaked star) | Star Peak Response |
| HFR failed / too low | HFR could not be measured, or is at/below the minimum HFR | Min HFR |
| Contaminated | a one-sided neighbor was detected in the annulus (when rejection is on) | Contamination Sensitivity |

### [low] (style) documentation/docs/overview/star-detection.md line 46: "With *hot-pixel thresholding* enabled"

**Issue:** Italicized as a setting reference, but the in-app label is "Use Hotpixel Thresholding" and settings/hotpixel-saturation.md phrases it as "when **Use Hotpixel Thresholding** is on". "hot-pixel thresholding" matches neither the label's spelling nor its casing.

**Suggested fix:**

(which span many pixels) essentially untouched. With **Use Hotpixel Thresholding** enabled, only pixels that

### [low] (style) documentation/docs/overview/star-detection.md line 83: "a **noise-clipping multiplier**"

**Issue:** Bolded as a setting reference but lowercased and hyphenated; the in-app label and settings/preprocessing.md use "Noise Clipping Multiplier". (Lowercase "noise-clipping floor" as a concept elsewhere is house-consistent and fine.)

**Suggested fix:**

structure map's median plus the **Noise Clipping Multiplier** times an estimated noise sigma, where the sigma

### [low] (style) documentation/docs/overview/star-detection.md line 153: "The *defocus-aware gates* option"

**Issue:** Italicized lowercase reference to the setting the in-app UI and settings/acceptance-gates.md call "Defocus-Aware Gates".

**Suggested fix:**

bounding box, while a streak (a satellite trail or merged pair) fills far less. The **Defocus-Aware Gates**


## overview/tilt-aberration-inspector.md (7)

### [high] (style) documentation/docs/overview/tilt-aberration-inspector.md, "Inspector options" table (lines 125-154), e.g. "| **Num Regions Wide** |"

**Issue:** The Setting column uses code-property-derived names instead of the exact in-app labels. The screenshot embedded directly above the table (inspector-options-empty.png) shows the real labels, so the table contradicts its own illustration. Verified against AutoFocus/DataTemplates.xaml: 20 of the 28 rows are wrong.

**Suggested fix:**

Apply these exact replacements in documentation/docs/overview/tilt-aberration-inspector.md:

EDIT 1 — lines 69-71, replace:
its best-focus position minus the mean, reported in focuser steps (and in microns, if you have set
*Microns per Focuser Step*).
with:
its best-focus position minus the mean, reported in focuser steps (and in microns, if you have set
*Focuser Step Size*).

EDIT 2 — line 75, replace:
Enabling **Sensor Curve Model** goes further.
with:
Turning on **Sensor Curve Model Enabled** goes further.

EDIT 3 — line 89, replace:
    Enable **Sensor Curve Model** when you intend to physically correct tilt or want curvature and
with:
    Turn on **Sensor Curve Model Enabled** when you intend to physically correct tilt or want curvature and

EDIT 4 — lines 118-119, replace:
These settings live in the inspector panel (not the main Options page). Defaults and ranges are taken
from the option definitions; descriptions quote the in-app tooltips where one exists.
with:
These settings live in the inspector panel (not the main Options page). Defaults and ranges are taken
from the option definitions; descriptions quote the in-app tooltips where one exists. The alignment,
star-matching, outlier-rejection, and save-images settings (plus **Astigmatic field curvature** and
**Min R² (rejection)**) sit inside an **Experimental** sub-expander within the Options section.

EDIT 5 — replace the full table (lines 125-154) with:
| Setting | Default | Range | What it does |
|---|---|---|---|
| **Eccentricity Grid Width** | 7 | odd, positive | "How many cells wide to divide the sensor pixels when generating a grid of eccentricity vectors … the height will be calculated proportionally." |
| **Focuser Step Size** | -1 (auto) | -1 or &gt;0 | "How much the focuser moves per step, in microns. If this is set, the adjustment chart will include adjustments in microns." |
| **Sensor ROI** | 1.0 | 0.1–1.0 | "Uses only a centered portion of the full sensor when evaluating aberration. This is useful if you have a flattener that cannot produce a flat field for your sensor." |
| **Corners ROI** | 1.0 | 0.1–1.0 | "Reduces the size of the corner regions when performing corners analysis … evaluate only stars closer to the corners than the full 1/9th region. This can be combined with Sensor ROI." |
| **Sensor Curve Model Enabled** | off | on/off | "Create a paraboloid model of the sensor by creating focus curves for every star. This enables calculation of centering error and curvature … similar to … CCD Inspector." |
| **Show Sensor Model** | on | on/off | Displays the 3D surface model in the panel. |
| **Sensor Centered** | on | on/off | "Assume the sensor is perfectly centered in the optical train. If this option is off, the sensor location will be modeled along with the other model parameters." |
| **Astigmatic field curvature** | off | on/off | "When off (default), field curvature is modeled as rotationally symmetric … Enable to fit independent X and Y curvature, representing the saddle-shaped field of an astigmatic optical train. Adds one free parameter." |
| **Align images before matching** | on | on/off | Align frames with RANSAC before matching stars, improving registration robustness. |
| **Use affine alignment (diagnostic)** | off | on/off | Use a 6-DOF affine transform (adds shear) instead of similarity; only enabled when **Align images before matching** is on. |
| **Outlier rejection** | on | on/off | Drop matched stars whose per-star hyperbolic fit is too poor. |
| **Match using brightness** | off | on/off | Enable an adaptive search that rejects matched stars whose brightness differs too much. |
| **Starting Brightness Tolerance** | -1 (auto) | -1 or &ge;0.01 | Starting brightness tolerance for the adaptive match search; -1 starts from the previous run's value. |
| **Min R² (rejection)** | 0.05 | 0–1 | Minimum model \(R^2\) for acceptance; only triggers rejection when reduced \(\chi^2\) also fails. |
| **Max Stars Per Region** | -1 (unlimited) | -1 or &gt;0 | Cap on the number of (brightest) stars used per region. |
| **Eccentricity Color Enabled** | on | on/off | "Enable color on the eccentricity map." |
| **Mouse Events Enabled** | on | on/off | "Enable mouse events on charts to scroll, pan, and zoom. Disable this if you don't want the charts to intercept mouse actions." |
| **Steps** | -1 (auto) | -1 or &gt;0 | "The minimum number of data points needed on each side of the AutoFocus curve minimum. Uses the value set for AutoFocus if blank." |
| **Step Size** | -1 (auto) | -1 or &gt;0 | "How many focuser steps in between each data point … Uses the value set for AutoFocus if blank." |
| **Signal Amplification** | 2 | &ge;1 | "Increases the resolution and signal of sensor-model / tilt calibration runs by capturing more, finer-spaced focuser points. The focuser step size is divided by this factor and the number of steps multiplied by it, so the sweep covers the same range with more points (and smaller defocus jumps between adjacent frames, which makes star alignment more reliable) … Set to 1 to disable. Applies to live captures only." Sits above the Options expander, with a live estimate of the images each run will capture. |
| **Center Focuser First** | off | on/off | "When on, a quick standard AutoFocus is run before each live sensor-model / tilt calibration sweep to center the focuser at best focus. The detailed sweep then brackets focus symmetrically, which reduces extreme one-sided defocus frames that fail to align … Has no effect when replaying saved frames." Sits above the Options expander, next to Signal Amplification. |
| **Exposures per Point** | -1 (auto) | -1 or &ge;1 | "How many exposures to average together for each focuser point. Uses the value set for AutoFocus if blank." |
| **AutoFocus Timeout** | -1 (auto) | -1 or &gt;0 | "How long, in seconds, after which AutoFocus should time out and fail. Uses the value set for AutoFocus if blank." |
| **Simple Analysis exposure** (the unlabeled seconds box beside **Take Exposure**) | -1 (auto) | -1 or &gt;0 | "How long of an exposure to take for analysis. Defaults to the Auto Focus exposure duration if not set." Sets the exposure for the single-frame Simple Analysis. |
| **AutoFocus Exposure** | -1 (auto) | -1 or &gt;0 | Per-frame exposure for a Detailed Analysis sweep; defaults to the AutoFocus exposure duration when blank. Unlike the Simple Analysis exposure box, this sets the per-frame exposure for the multi-frame Detailed Analysis sweep. |
| **Looping** | off | on/off | "If enabled, repeatedly take and analyze exposures." |
| **Save annotated images when rerunning a saved autofocus** | off | on/off | Save registered/alignment images when reanalyzing saved runs. |
| **Save alignment images** | off | on/off | Also save the pre-alignment star-detection images. |

### [medium] (style) documentation/docs/overview/tilt-aberration-inspector.md line 141: "| **Max Stars Per Region** | -1 (unlimited) | -1 or >0 | Cap on the number of (brightest) stars used per region. |"

**Issue:** This row documents a setting that has no control in the inspector panel (MaxStarsPerRegion exists only in IInspectorOptions.cs; no XAML binds it anywhere), contradicting the section lead "These settings live in the inspector panel."

**Suggested fix:**

Delete line 141 from documentation/docs/overview/tilt-aberration-inspector.md — remove this entire table row (no replacement text):

| **Max Stars Per Region** | -1 (unlimited) | -1 or &gt;0 | Cap on the number of (brightest) stars used per region. |

Do not add a substitute note: the option has no control anywhere in the UI (only a persisted-options property consumed by SensorModel.cs), so it does not belong in a section introduced as "These settings live in the inspector panel."

### [medium] (style) documentation/docs/overview/tilt-aberration-inspector.md, "What the inspector measures" table (lines 34-42), e.g. "| **Tilt effect** |" and "| **AutoFocus offset** |"

**Issue:** Row labels do not match the panel's exact text: the app shows "Tilt Effect", "Curvature Effect", "Curvature Radius", "Critical Focus", "Mean Focuser Position", and "Auto Focus Offset" (title case, and "Auto Focus" with a space). This also drifts from tilt-adapter-wizard.md, which already uses "Tilt Effect" and "Curvature Effect".

**Suggested fix:**

| Measurement | What it means (from the panel tooltips) |
|---|---|
| **Tilt** | "The angle the sensor is tilted. 0 indicates no tilt, and values here are typically very small." |
| **Tilt Effect** | "The maximum nominal distance from the corners of the sensor to the center, due purely to the modeled tilt." |
| **Curvature Effect** | "The nominal distance from the corner of the sensor to the center, due purely to the modeled curvature. Curvature effect is more exaggerated on larger sensors." |
| **Curvature Radius** | "Near focus, the base of the sensor parabola is close to a sphere. This value represents the radius of that sphere. Larger values indicate flatter curves, which are better." |
| **Critical Focus** | "The focuser distance a pixel can be from optimal focus before the effects can be noticed. This value increases with larger (slower) F/ratios, which can tolerate more curvature before affecting image quality." |
| **Mean Focuser Position** | "The weighted mean focuser position under the sensor curve … a focus point where pixels throughout the sensor have minimum absolute distance from optimal focus." |
| **Auto Focus Offset** | "The number of focuser steps between the AutoFocus position and the Sensor Mean Focuser Position." |

### [low] (style) documentation/docs/overview/tilt-aberration-inspector.md line 98: "the \"*N frames failed to align*\" condition is now rare"

**Issue:** "is now rare" is changelog voice: it describes an improvement relative to an earlier release the manual reader has never seen. The manual should state current behavior.

**Suggested fix:**

escalates its search box for the hardest frames rather than giving up, so the "*N frames failed to align*" condition is rare.

### [low] (style) documentation/docs/overview/tilt-aberration-inspector.md line 94: "!!! note \"Frame alignment is robust to heavy defocus\"" and line 135: "improving registration robustness"

**Issue:** "robust"/"robustness" is on the house buzzword list. Neighboring pages use "robust" only in the statistics term-of-art sense (robust estimators, MAD); these two uses are the generic marketing sense.

**Suggested fix:**

Line 94: replace
!!! note "Frame alignment is robust to heavy defocus"
with
!!! note "Frame alignment holds up under heavy defocus"

Line 135: replace
| **Use RANSAC** | on | on/off | Align frames with RANSAC before matching stars, improving registration robustness. |
with
| **Use RANSAC** | on | on/off | Align frames with RANSAC before matching stars, so matching stays reliable at the defocused ends of the sweep. |

### [low] (style) documentation/docs/overview/tilt-aberration-inspector.md lines 6-8: "Is my backfocus right?* Backfocus is the spacing between the corrector/flattener and the sensor. And, when paired with a tilt-adapter calibration..."

**Issue:** The backfocus definition interrupts the run of rhetorical questions and forces the next sentence to open with "And,", which reads disjointed.

**Suggested fix:**

*Is one corner sharper than the other? Is the field bowed? Is my backfocus right?* (Backfocus is the spacing between the corrector/flattener and the sensor.) When paired with a tilt-adapter calibration, the inspector translates those numbers into concrete screw-turn guidance.

### [low] (style) documentation/docs/overview/tilt-aberration-inspector.md line 156: "!!! tip \"When Sensor ROI / Corners ROI help\""

**Issue:** The tip largely restates the Sensor ROI and Corners ROI tooltips already quoted in the table 25 lines above; only the "keeps a bad corner from polluting the tilt fit" clause is new information. House style says admonitions should add, not restate.

**Suggested fix:**

!!! tip "Sensor ROI protects the tilt fit"
    Restricting analysis to the well-corrected center (**Sensor ROI**) keeps a corner your
    flattener cannot correct from polluting the tilt fit.


## overview/tilt-adapter-wizard.md (2)

### [medium] (style) documentation/docs/overview/tilt-adapter-wizard.md, lines 35-36: "opposite screws are always 180° apart regardless of mirroring,"

**Issue:** An internal code comment (TiltCalibrationCalculator.cs:221) is quoted verbatim in quotation marks with no attribution. The house voice reserves quotation marks for user-visible text and always attributes it ("Per the tooltip, ...", as in tilt-aberration-inspector.md and the Microns per Focuser Step note on this same page). Presented this way, the quote reads as a citation of UI text the reader could go look for, and quoting one's own source comments is a generated-from-source tell.

**Suggested fix:**

  exploits this: opposite screws are always 180° apart regardless of mirroring, so it measures two
  screws and places the other two 180° across.

### [low] (style) documentation/docs/overview/tilt-adapter-wizard.md, lines 76-87: !!! tip "Tilt, backfocus, and curvature: what the screws can fix"

**Issue:** This admonition carries two paragraphs of core conceptual content: it defines the Tilt Effect and Curvature Effect and explains the per-screw Tilt and Backfocus guidance amounts shown in the screenshot at the top of the page. The style guide says admonitions should add information that breaks the main flow, not carry it, and this page already has six callouts (siblings: sensor-model 0, tilt-aberration-inspector 3, autofocus 3). Promoting this one to a regular section keeps the text and restores the aside-only role of the remaining callouts.

**Suggested fix:**

## What the screws can fix

The Sensor Model splits the focus surface into two effects. The **Tilt Effect** is the linear
plane (one side focuses ahead of the opposite side); you null it by moving the screws
*differentially*, reported as the per-screw **Tilt** amount.

The **Curvature Effect** is the symmetric corners-versus-center bowl. The wizard reads it as a
spacing error and derives a **backfocus** correction from it, reported as the per-screw
**Backfocus** amount: turn all screws the same way to move the whole sensor along the optical axis
(or add spacers for changes beyond the adapter's travel), then re-measure and repeat until the
Curvature Effect stops dropping. What remains is the residual curvature of a correctly spaced
system, set by your corrector design and focal ratio; the adapter cannot remove it (a
better-matched corrector or stopping down does).


## quick-start.md (3)

### [medium] (style) documentation/docs/quick-start.md lines 35-37: "- **Star Detection** — the improved detector ... - **Auto Focus** — the concurrent autofocus engine"

**Issue:** UI labels do not match the exact in-app text. The screenshot directly above (assets/screenshots/image-options-all.png) shows the NINA Image options dropdowns labeled "Star Detector", "Star Annotator", and "Autofocus", and the caption on line 33 uses those names, but the bullet list contradicts both by calling them "Star Detection" and "Auto Focus".

**Suggested fix:**

- **Star Detector** — the improved detector (see [Star Detection](overview/star-detection.md)).
- **Star Annotator** — the customizable overlay (see [Star Annotation](overview/star-annotation.md)).
- **Autofocus** — the concurrent autofocus engine (see [Autofocus](overview/autofocus.md)).

### [medium] (style) documentation/docs/quick-start.md lines 40-41: "require Hocus Focus to be selected for **both** Auto Focus **and** Star Detection."

**Issue:** Same dropdown-label drift as the bullet list ("Auto Focus" / "Star Detection" instead of the in-app "Autofocus" / "Star Detector"), and the bolding lands on the connectives "both" and "and" instead of the UI labels the reader must find.

**Suggested fix:**

    The autofocus engine and the Aberration Inspector require Hocus Focus to be selected for both
    **Autofocus** and **Star Detector**. You can otherwise mix and match, for example keeping only the detector.

### [low] (style) documentation/docs/quick-start.md line 51: "- **Default (autofocus repeatability)** — the everyday choice."

**Issue:** "Default (autofocus repeatability)" is bolded in the same style as the exact UI label beside it ("Optimize for Aberration Inspection"), implying an on-screen option with that name; per optimization/index.md the start page actually has a single "Optimize for Aberration Inspection" toggle, and no control is labeled "Default (autofocus repeatability)".

**Suggested fix:**

- **Autofocus repeatability** (the default) — the everyday choice. It tunes detection so your autofocus
  curves are tight and your best-focus position is repeatable run to run.


## settings/acceptance-gates.md (5)

### [medium] (style) documentation/docs/settings/acceptance-gates.md line 249, blockquote beginning "> MASTER toggle for defocus-aware donut detection."

**Issue:** The blockquote presents itself as the verbatim in-app tooltip but omits the parenthetical "(also on the optimizer wizard's start page)" that the actual DefocusAwareDonutDetection_Tooltip contains (Resources/OptionsDataTemplates.xaml line 453). Quoted UI text must match the exact in-app text.

**Suggested fix:**

> MASTER toggle for defocus-aware donut detection (also on the optimizer wizard's start page). When ON, out-of-focus DONUT stars (heavily defocused stars that appear as hollow rings) are recovered: a morphological close reconnects fragmented rings and an annularity test lets a hollow ring pass the distortion gate like a filled disk (detection-only — it never changes HFR). It also unlocks the optimizer to tune ALL defocus-aware settings and to enable diffraction-spike / saturated-bloom suppression. Recommended for telescopes with a central obstruction (Newtonians/SCTs); leave OFF for refractors. Off by default; when OFF, detection is exactly as before.

### [medium] (style) documentation/docs/settings/acceptance-gates.md line 304, blockquote ending "0 means OFF. Default 0."

**Issue:** The quoted Donut Saturation Bloom Radius tooltip is truncated relative to the actual in-app text, which ends "Default 0 (OFF — the optimizer enables it when you label the saturated star's artifacts as should-reject)." (Resources/OptionsDataTemplates.xaml line 459). The dropped clause carries real information about when the optimizer turns the guard on, and the sibling Donut Max Streak Eccentricity quote on this same page keeps its equivalent clause.

**Suggested fix:**

> Only used while Defocus-Aware Donut Detection is on. Rejects candidates whose center lies within this many pixels of a saturated star, removing the bloom/halo fragments around a bright saturated star while keeping the star itself. 0 means OFF. Default 0 (OFF — the optimizer enables it when you label the saturated star's artifacts as should-reject).

### [low] (style) documentation/docs/settings/acceptance-gates.md line 243: "as **\"Recover out-of-focus donut stars.\"**"

**Issue:** The quoted Optimization Wizard checkbox label does not match the exact in-app text, which is "Recover out-of-focus donut stars (reflectors/SCTs)" (StarDetection/Optimization/DataTemplates.xaml line 346); the doc also folds a sentence-ending period inside the quoted label, making it look like part of the UI string.

**Suggested fix:**

[Optimization Wizard](../optimization/index.md)'s start page as **"Recover out-of-focus donut stars (reflectors/SCTs)"**.

### [low] (style) documentation/docs/settings/acceptance-gates.md line 156: "The default of 1.2 is an honest-HFR floor calibrated to the current measurement pipeline; the older 1.5 floor was tuned against noise-inflated faint-star HFRs and is no longer appropriate."

**Issue:** Developer jargon leaking into the user manual: "honest-HFR floor" is lifted from an internal code comment (StarDetectionOptions.cs line 182, "honest-HFR floor... (F3 follow-up)") and is meaningless to a reader; the sentence reads as changelog-speak rather than user guidance. (Note: "honest multiple of the real noise" elsewhere on the page is established product terminology from the tooltip and preprocessing.md, and was deliberately not flagged; "honest-HFR" appears nowhere else in the manual.)

**Suggested fix:**

Leave it at the default for almost all setups. Lower it only if you are extremely undersampled and confident your real stars measure below 1.2 px. Raising it discards the sharpest stars and is rarely useful. The default of 1.2 is calibrated to the current measurement pipeline; older versions defaulted to 1.5, which compensated for noise-inflated HFR measurements of faint stars and is no longer needed.

### [low] (style) documentation/docs/settings/acceptance-gates.md line 56: "Because σ is measured on the same image that is actually sampled for star measurement, the number is an honest multiple of the real noise and behaves consistently across noise-reduction settings."

**Issue:** This body sentence restates the quoted tooltip two lines above nearly verbatim ("...so this value is an honest multiple of the real noise and means the same thing regardless of noise-reduction settings"). The house pattern (e.g. contamination.md) elaborates on the tooltip rather than echoing it; the echo reads as generated padding.

**Suggested fix:**

**Smaller values are more sensitive** (they admit fainter stars); larger values are stricter. Because σ is measured on the image actually sampled for star measurement, the same threshold keeps its meaning whether or not noise reduction is enabled.


## settings/adaptive-binarization.md (1)

### [low] (style) documentation/docs/settings/adaptive-binarization.md line 103: "## Reverting to legacy behavior (you should not need to)"

**Issue:** Editorializing parenthetical aside in a heading is a mild self-aware framing and drifts from house heading style. Other parenthetical headings in this folder are descriptive qualifiers ("Tuning workflow (Advanced mode)", "Half-Flux Radius (HFR)"), not asides addressed to the reader. The advice itself is already stated plainly in the section body ("The only reason to do that is to reproduce or compare against the old detector... leave them as they are"), so the aside is redundant.

**Suggested fix:**

## Reverting to legacy behavior


## settings/advanced-debug.md (3)

### [medium] (style) documentation/docs/settings/advanced-debug.md, line 263: "> When Save Intermediate is enabled, they are written to this path the next time star detection runs"

**Issue:** Quoted in-app tooltip does not match the actual UI text. The XAML resource SaveIntermediatePath_Tooltip (Resources/OptionsDataTemplates.xaml line 496) reads "When Save Intermediate Files is enabled, ...". Every other blockquote on this page reproduces its tooltip verbatim (even preserving the "due from" grammar slip in the Pixel Scale tooltip), so this one-word paraphrase breaks the page's own convention and the house rule that UI text must match exactly.

**Suggested fix:**

> When Save Intermediate Files is enabled, they are written to this path the next time star detection runs

### [low] (style) documentation/docs/settings/advanced-debug.md, lines 97-98: "watch **Total detected** rise without the spurious/**Structure candidates** counts ballooning"

**Issue:** Garbled compound: "the spurious/**Structure candidates** counts" reads as if the results panel has a "spurious" count, which it does not (index.md lists only Structure candidates, Total detected, and the per-reason rejection counts). It also drifts from the established phrasing in preprocessing.md ("a good move raises **Total detected** while the **Structure candidates** count falls toward it (fewer spurious candidates ...)").

**Suggested fix:**

re-detect and watch **Total detected** rise without the **Structure candidates** count ballooning with spurious candidates in the [Star Detection Results panel](index.md#reading-the-results-the-star-detection-results-panel).

### [low] (style) documentation/docs/settings/advanced-debug.md, lines 84-85: "(Noise-reduction radius: base radius 3/5; +1 is added whenever Hotpixel Thresholding is enabled (the default), so the applied radius is 4/6 (None stays 0 because filtering is off).)"

**Issue:** Triple-nested parentheses (a parenthetical containing two further parentheticals) is over-parenthesized hedging that no sibling page uses; the house voice favors short declarative sentences. The content is fine; the packaging is the tell.

**Suggested fix:**

Noise-reduction radius: the base radius is 3/5; +1 is added whenever Hotpixel Thresholding is enabled (the
default), so the applied radius is 4/6. None stays 0 because filtering is off.


## settings/contamination.md (1)

### [low] (style) documentation/docs/settings/contamination.md line 18 (Settings at a glance table): "0 – 20 (0 disables)"

**Issue:** Spaced en-dash range '0 – 20' drifts from the house convention of unspaced en-dash ranges used in sibling tables and reference lines (e.g. acceptance-gates.md '0–100%', '1–1000', '0.01–1').

**Suggested fix:**

0–20 (0 disables)


## settings/donut-aware.md (3)

### [medium] (style) documentation/docs/settings/donut-aware.md line 11: "The whole group is gated by one master toggle."

**Issue:** Terminology drift from acceptance-gates.md and the code: the Defocus-Aware Donut Detection master gates only the donut-recovery knobs (and implies the relaxations); Defocus-Aware Gates and Defocus-Aware Structure are independent toggles that act even with the master off (StarDetector.cs lines 575-580, 1382). The page's own figure caption ("The defocus-aware gates alone relax that gate") contradicts this sentence.

**Suggested fix:**

The donut-recovery knobs are gated by the Defocus-Aware Donut Detection master toggle; Defocus-Aware Gates and Defocus-Aware Structure are independent toggles. The numeric knobs and their full reference live on the

### [low] (style) documentation/docs/settings/donut-aware.md lines 6-7: "The features are off by default; with the master toggle off, detection is identical to having them absent."

**Issue:** Over-attributes the bit-identical guarantee to the master toggle alone. With the master off but Defocus-Aware Gates or Defocus-Aware Structure on, detection differs. The guarantee holds only when all the toggles are off (the default).

**Suggested fix:**

The features are off by default; with all of them off, detection is identical to having them absent.

### [low] (style) documentation/docs/settings/donut-aware.md line 20: "| Donut morph close, annularity, streak, bloom | per knob | ..."

**Issue:** The Setting column uses lowercase shorthand instead of the exact in-app UI labels; the house style requires exact labels and every other row in this table uses them (labels verified in OptionsDataTemplates.xaml and the acceptance-gates.md table).

**Suggested fix:**

| Donut Morph Close Size, Donut Min Annularity Hole Fraction, Donut Max Streak Eccentricity, Donut Saturation Bloom Radius | per knob | [Acceptance Gates](acceptance-gates.md#recover-out-of-focus-donut-stars) |


## settings/hotpixel-saturation.md (1)

### [low] (style) documentation/docs/settings/hotpixel-saturation.md, lines 105-106: "!!! note — The same threshold gates two things: the per-image **saturated-pixel count** in the metrics, and the per-star mask applied during PSF fitting. It does not, by itself, reject stars from the accepted set."

**Issue:** Admonition that merely restates the main flow. Every point in this note already appears in the body paragraph under Saturation Threshold (line 97): the per-star mask during PSF fitting ("during PSF fitting it discards every pixel whose raw value is at or above the saturation threshold"), the metrics count ("The count of saturated pixels and saturated stars is tracked in the detection metrics"), and the no-rejection point ("Hocus Focus does **not** reject a partially-saturated star outright"). The house style guide says admonitions should add information that breaks the main flow, not restate it, and the neighboring pages' notes (e.g. preprocessing.md's "Why the default is 2", psf-modeling.md's "HFR is always measured; FWHM is not") all carry new content.

**Suggested fix:**

Delete the `!!! note` block at lines 105-106 together with the blank line that precedes it (line 104). Concretely, replace:

!!! tip "When this helps"
    Leave the default (99%) for most setups. **Lower** it if your sensor or processing introduces non-linearity or blooming just below the full-well point, so those tainted near-saturation pixels are also excluded from fits. **Raise** it toward 100% only if you are confident your sensor stays linear right up to the clip point and want to keep as many pixels as possible in the fit. Setting it too low needlessly throws away good pixels and can leave too few for a reliable fit.

!!! note
    The same threshold gates two things: the per-image **saturated-pixel count** in the metrics, and the per-star mask applied during PSF fitting. It does not, by itself, reject stars from the accepted set.

---

with:

!!! tip "When this helps"
    Leave the default (99%) for most setups. **Lower** it if your sensor or processing introduces non-linearity or blooming just below the full-well point, so those tainted near-saturation pixels are also excluded from fits. **Raise** it toward 100% only if you are confident your sensor stays linear right up to the clip point and want to keep as many pixels as possible in the fit. Setting it too low needlessly throws away good pixels and can leave too few for a reliable fit.

---


## settings/index.md (2)

### [low] (style) documentation/docs/settings/index.md line 9: "The star-detection settings live under the star-detection options area."

**Issue:** Tautological sentence that also dodges the exact in-app UI label. The tab is named "Star Detector" (Options.xaml, LblStarDetector), and the page's own screenshot caption already names it; house style requires exact in-app labels.

**Suggested fix:**

Open NINA's options, go to the **Plugins** tab, and select **Hocus Focus**. The star-detection settings live in the **Star Detector** tab. Two top-level switches decide which controls you see:

### [low] (style) documentation/docs/settings/index.md lines 83-84: "**Structure candidates**" and "**Total detected**"

**Issue:** These bolded labels do not match the exact in-app text: the Star Detection Results panel renders "Structure Candidates" and "Total Detected" (AutoFocus/DataTemplates.xaml lines 843 and 831). Note the sentence-case form is repeated in preprocessing.md, structure-detection.md, and advanced-debug.md, so fix manual-wide (or explicitly adopt sentence case) rather than on this page alone; this page is the canonical place that introduces the panel, so it should lead.

**Suggested fix:**

- **Structure Candidates** — bright structures evaluated as potential stars before any gate.
- **Total Detected** — stars accepted after all gates.


## settings/precision-recall.md (3)

### [low] (style) documentation/docs/settings/precision-recall.md, lines 34-35: "It failed on faint subs, and the failure is worth recording so it is not repeated."

**Issue:** "worth recording so it is not repeated" is the "it's worth noting" family of filler plus self-aware meta-narration about the document itself. The section heading ("Why hand-marking every star does not scale") and line 43 ("The lesson shaped the whole method") already carry this purpose, so the clause is redundant throat-clearing.

**Suggested fix:**

The first attempt had a vision model mark every star directly on stretched image tiles. It failed on faint subs. Each tile reaches the model downscaled to roughly a 256-pixel thumbnail, so the model guesses a position and scales it back up.

### [low] (style) documentation/docs/settings/precision-recall.md, line 105: "This split is what turns a recall number into an actionable diagnosis"

**Issue:** "actionable" is buzzword-adjacent ("actionable insights" territory) and adds nothing: the rest of the sentence already states why the split matters (different failure modes call for different fixes). No other page in the manual uses "actionable".

**Suggested fix:**

This split is what turns a recall number into a diagnosis, since a candidate-formation gap and a too-strict gate call for different fixes.

### [low] (style) documentation/docs/settings/precision-recall.md, line 123: "excellent precision (about 0.85) but found only ~19% of the real SNR ≥ 12 stars"

**Issue:** Mixed approximation styles in one sentence: "about 0.85" versus "~19%". The rest of the page's prose uses "about"/"roughly" (e.g. "about 79%", "about 12 pixels"), and the neighboring adaptive-binarization.md reports these same measurements as fractions (0.189, 0.85).

**Suggested fix:**

On a real ZWO ASI6200MM Pro train, Hocus Focus had excellent precision (about 0.85) but found only about 19% of the real SNR ≥ 12 stars.


## settings/preprocessing.md (3)

### [medium] (style) documentation/docs/settings/preprocessing.md, line 100 (blockquote "> Makes the structure-map binarization threshold spatially adaptive...")

**Issue:** The quoted Locally Adaptive Binarization tooltip does not match the actual in-app tooltip text. The doc silently drops the parenthetical "— local background median + Noise Clipping Multiplier × local noise σ —" and the final three sentences ("Recovers faint real stars... On by default... EARLY-stage setting."). The house convention is verbatim tooltip quotes: structure-detection.md line 51 and acceptance-gates.md line 282 both keep trailing sentences like "EARLY-stage setting.", and every other blockquote on this page matches OptionsDataTemplates.xaml exactly.

**Suggested fix:**

> Makes the structure-map binarization threshold spatially adaptive. Normally a single global threshold (background median + Noise Clipping Multiplier × global noise σ) is applied across the whole frame, which is too high in clean regions (losing faint stars) and too low in noisy corners (admitting noise). When enabled, the threshold becomes a smooth surface computed from robust local statistics on a coarse block grid — local background median + Noise Clipping Multiplier × local noise σ — so the same Noise Clipping Multiplier is locally fair everywhere. Recovers faint real stars in clean regions while rejecting noise in vignetted/gradient-heavy corners. On by default; turn it off to revert to the single global threshold, which makes detection bit-for-bit identical to the legacy behavior. EARLY-stage setting.

### [low] (style) documentation/docs/settings/preprocessing.md, lines 137-138 ("!!! note / This value has no effect unless Locally Adaptive Binarization is on.")

**Issue:** Admonition that merely restates the main flow: the quoted tooltip nine lines above already opens with "Only used while Locally Adaptive Binarization is on.", and the section is nested under the Locally Adaptive Binarization heading. The style guide says a note should add information that breaks the main flow, not restate it; sister-page notes (structure-detection.md line 18, acceptance-gates.md line 309) all add cross-page routing rather than repeating the section.

**Suggested fix:**

Delete lines 137-139 of documentation/docs/settings/preprocessing.md (the "!!! note" block and its trailing blank line), so the section ends:

The default of 128 px matches the reference detector used to validate the feature.

## Star Clipping Multiplier

### [low] (style) documentation/docs/settings/index.md, line 165 ("- [Advanced & Debug](advanced-debug.md) — measurement averaging, pixel sample size, intermediate files, debug mode.")

**Issue:** Cross-page drift discovered while checking this page against its neighbors: index.md's sub-page list says Pixel Sample Size is documented on Advanced & Debug, but the setting is actually documented on preprocessing.md (advanced-debug.md only mentions it in a preset table), and the Preprocessing bullet on line 159 omits it. Readers following the index will look for the setting on the wrong page.

**Suggested fix:**

Line 159: - [Preprocessing & Noise](preprocessing.md) — hotpixel filtering, noise reduction radius, clipping multipliers, measurement noise reduction, pixel sample size.
Line 165: - [Advanced & Debug](advanced-debug.md) — measurement averaging, intermediate files, debug mode.


## settings/psf-modeling.md (5)

### [medium] (style) documentation/docs/settings/psf-modeling.md lines 113 and 126: "watch the **PSFFitFailed** count in the [Star Detection Results panel]"

**Issue:** UI label mismatch. The Star Detection Results panel row is labeled "PSF Failed" (AutoFocus/DataTemplates.xaml line 1025, Text="PSF Failed"), but both tips tell the reader to look for a bolded **PSFFitFailed** count in that panel. Sibling pages use the exact panel label when pointing at the results panel (e.g. contamination.md: "watch the **Contaminated** rejection count"). The backticked `PSFFitFailed` code references on lines 13 and 94 are fine; only the two panel-facing bolded uses are wrong.

**Suggested fix:**

Line 113: replace "and watch the **PSFFitFailed** count in the [Star Detection Results panel](index.md#reading-the-results-the-star-detection-results-panel) for any change in fit rejections." with "and watch the **PSF Failed** count in the [Star Detection Results panel](index.md#reading-the-results-the-star-detection-results-panel) for any change in fit rejections."

Line 126: replace "and watch the **PSFFitFailed** count in the [Star Detection Results panel](index.md#reading-the-results-the-star-detection-results-panel) for any shift in fit rejections." with "and watch the **PSF Failed** count in the [Star Detection Results panel](index.md#reading-the-results-the-star-detection-results-panel) for any shift in fit rejections."

(Leave the backticked `PSFFitFailed` code references on lines 13 and 94 unchanged.)

### [low] (style) documentation/docs/settings/psf-modeling.md line 53: "**PSF Type** (property `PSFFitType`) — selects which analytic profile is fit to each star."

**Issue:** Em-dash in running prose plus structural drift: every other setting section on this page and on sibling pages opens with a plain declarative sentence ("**Fit PSF** (property `ModelPSF`) is the master switch...", "How finely each star's bounding box is sampled..."; contamination.md: "Sets how large the one-sided asymmetry must be..."). This is the only headline sentence on the page fused with an em-dash fragment. (The "**Moffat 4.0** — ..." bullets on lines 65-68 were deliberately not flagged: the "**Term** — definition" bullet pattern is established house style in index.md, preprocessing.md, and advanced-debug.md.)

**Suggested fix:**

**PSF Type** (property `PSFFitType`) selects which analytic profile is fit to each star.

### [low] (style) documentation/docs/settings/psf-modeling.md line 126: "To gauge the effect, compare reported FWHM/σ stability before and after and watch the **PSFFitFailed** count in the [Star Detection Results panel](...) for any shift in fit rejections."

**Issue:** Boilerplate repetition, an AI-writer tell: this sentence is a near-verbatim duplicate of the evaluation sentence in the PSF Pixel Integration tip 13 lines earlier (line 113, "To judge it, compare reported FWHM/σ stability before and after, and watch the ... count ... for any change in fit rejections"). Two adjacent tips reciting the same recipe reads as generated; state it once and cross-reference.

**Suggested fix:**

    Try it on **noisy frames** or fields with frequent outlier pixels where ordinary fits are being pulled around, and when you want behavior closer to PixInsight's PSF logic. Judge it the same way as PSF Pixel Integration above: FWHM/σ stability before and after, plus the **PSFFitFailed** count. Because it is experimental and slower, **leave it off** by default and enable it deliberately when robustness matters more than speed.

### [low] (style) documentation/docs/settings/psf-modeling.md line 123: "The internal note describes it as \"more robust to noise and outlier pixels.\""

**Issue:** Redundant self-citation: the sentence re-quotes the same tooltip that is already blockquoted verbatim four lines above (line 119). No sibling page re-cites its own blockquote, and "the internal note" is terminology used nowhere else in the manual (other pages just call it the tooltip or let the blockquote speak).

**Suggested fix:**

Fitting to minimize absolute deviation downweights outlier pixels (a hot pixel, a cosmic-ray hit, a nearby star's flux) relative to a least-squares fit, at a modest extra computational cost.

### [low] (style) documentation/docs/settings/psf-modeling.md lines 41, 57, 79, 92, 108, 121, 134: "**Default:** On &nbsp;·&nbsp; **Range:** On / Off"

**Issue:** Separator drift from neighboring pages: the two sibling pages that use an inline Default/Range separator (contamination.md, preprocessing.md) both use the bullet "&nbsp;•&nbsp;"; this page alone uses the middle dot "&nbsp;·&nbsp;" in all seven Default/Range lines.

**Suggested fix:**

Replace every "&nbsp;·&nbsp;" with "&nbsp;•&nbsp;" in documentation/docs/settings/psf-modeling.md. The seven corrected lines are:

Line 41: **Default:** On &nbsp;•&nbsp; **Range:** On / Off
Line 57: **Default:** Moffat 4.0 &nbsp;•&nbsp; **Range:** Gaussian, Moffat 4.0, Moffat 2.5, Moffat 1.5, Moffat (β fittable)
Line 79: **Default:** 10 (pixels) &nbsp;•&nbsp; **Range:** integer > 0 (the field validates greater-than-zero; the backing property rejects negatives)
Line 92: **Default:** 0.9 (R²) &nbsp;•&nbsp; **Range:** (0, 1]. The field validates `0 ≤ value ≤ 1.0`; the backing property rejects values outside the open-low, closed-high interval \((0, 1]\)
Line 108: **Default:** Off &nbsp;•&nbsp; **Range:** On / Off
Line 121: **Default:** Off &nbsp;•&nbsp; **Range:** On / Off (property name `UsePSFAbsoluteDeviation`)
Line 134: **Default:** 100 (stars) &nbsp;•&nbsp; **Range:** integer ≥ 0 (the backing property rejects negatives)


## settings/structure-detection.md (2)

### [high] (style) documentation/docs/settings/structure-detection.md, line 35: "At the default of 5 (2⁵=32), structures larger than 32 pixels in size are excluded from structure detection"

**Issue:** The quoted tooltip does not match the exact in-app text. The shipped tooltip (Resources/OptionsDataTemplates.xaml line 436, StructureLayers_Tooltip) reads "At the default of 4 (2⁴=16), structures larger than 16 pixels in size are excluded from structure detection". The page quotes a stale 5/32 version.

**Suggested fix:**

> The number of dyadic (power of 2) layers to include in structure detection. At the default of 4 (2⁴=16), structures larger than 16 pixels in size are excluded from structure detection

### [medium] (style) documentation/docs/settings/structure-detection.md, line 37: "**Default:** 4 (the tooltip's \"5/32 px\" wording describes the scaling relationship; the shipped default is 4, i.e. structures larger than \(2^{4}=16\) px)."

**Issue:** This parenthetical explains away a "5/32 px" tooltip wording that no longer exists in the app (the shipped tooltip already says 4/16), so it currently makes a false claim about the UI text and becomes redundant once the quote on line 35 is corrected.

**Suggested fix:**

**Default:** 4 (structures larger than \(2^{4}=16\) px are removed as background). **Range:** any positive integer (validated as greater than zero).
