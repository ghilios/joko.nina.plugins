# Labels: Recall and Precision

The objective's [star-count term](objective-function.md) is a *proxy* for good detection. It rewards keeping
enough stars on every frame, but it cannot tell a real faint star from a noise blob, or know that a particular
accepted "star" is actually a hot column. When you want the optimizer to chase **measured** detection quality
instead of a proxy, you give it ground truth: hand-drawn **labels**. With labels present, the objective gains an
additional term, \(S_{\text{label}}\), scored directly against the boxes you drew.

This is entirely optional. Without labels the optimizer runs on the focus / star-count / curve-fit / coverage terms alone.

!!! note "Labels vs golden star sets"
    Labels are *interactive* ground truth: a handful of boxes you draw, scored live inside the optimizer. To
    validate a shipped default across many runs, the project instead measures against a detector-independent
    reference that catalogs every real star in a frame. That method, and how recall and precision are scored
    against it, is on [Precision & Recall](../settings/precision-recall.md).

## Three kinds of label

A label is a **bounding box** drawn on a specific frame (at a known focuser position), in one of three
categories:

| Category | What it marks | Drives |
|---|---|---|
| **Missed** | A real star with no detection marker (a false negative) | Recall |
| **Wrongly-rejected** | A star the detector found but a gate rejected, which you want kept | Recall |
| **Should-reject** | An accepted detection you judge spurious — a false positive | Precision |

You create these in the review view (**"Review frames"** on the wizard summary): drag a box over a real star
the detector missed, click a rejected candidate you think should have been kept, or click an accepted
candidate that should have been thrown out.

## Scoring by box containment

Recall and precision are measured by whether an **accepted star center** falls inside each box:

- **Recall** = the fraction of *recall-target* boxes (missed ∪ wrongly-rejected) that now contain at least one
  accepted star center. If you labeled nothing to recover, recall is 1.0 by definition.
- **Precision** = the fraction of *should-reject* boxes that now contain **no** accepted star center, i.e. the
  spurious detection has been excluded. If you labeled no false positives, precision is 1.0.

A point \((c_x, c_y)\) is inside a box \((x, y, w, h)\) when \(x \le c_x \le x+w\) and \(y \le c_y \le y+h\).
Recall and precision are averaged across all labeled focuser positions, then combined with equal weight:

\[
S_{\text{label}} = 0.5 \cdot \text{recall} + 0.5 \cdot \text{precision}
\]

This term enters the per-run objective with weight \(W_\ell = 0.25\) (the weights renormalize so they still sum
to 1), and **only** when at least one label exists for the run. Otherwise it is dropped entirely. See
[The objective function](objective-function.md) for how it folds into \(J_{\text{run}}\).

!!! note "Why boxes, not points"
    A box tolerates the small centroid wobble between a candidate and its true center, and lets one label cover
    a tight pair without over-constraining which member must be recovered. Containment is cheap to evaluate on
    every candidate move during the search.

## The label-assisted loop

Labeling turns the optimizer into a guided tool: you optimize, look at what it got wrong, label those cases,
and re-optimize so the new term pushes the search toward your judgment.

1. **Optimize** once with no labels to get a baseline detection setting.
2. **Review frames**: drag boxes on missed stars, click false positives (*should-reject*), click
   wrongly-rejected candidates you want back.
3. **Optimize with feedback.** Press **"Optimize with feedback"** (on the Review page, or from the prompt on
   the summary) to re-run the search with your labels. The recall/precision term is now active, so moves that
   recover your missed stars and suppress your false positives score higher.

!!! tip "When labels are worth the effort"
    Reach for labels when the proxy terms have plateaued but you can still *see* problems: faint companions
    being dropped, or a hot column repeatedly accepted as a star. A handful of well-chosen boxes on the
    hardest frames is usually enough to break the tie. For routine tuning, the label-free objective already
    produces good settings.
