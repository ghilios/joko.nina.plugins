# Backfocus Correction Gain: Why the Recommended Move Is ~7× Too Small

**Status:** design, not yet implemented. §8 (apply-time UX) reflects a UX design pass. Companion plan: `plans/backfocus-correction-gain-plan.md`.

**Datasets:** `D:\TiltCalibrationDebug\WithExtraBaseline` (7-step calibration, schema 3) and
`D:\TiltCalibrationDebug\Minus_{100..500}` (five aberration-inspector runs at cumulative piston
offsets of −100 … −500 motor steps on all four screws), captured 2026-08-04 on a 4-screw ASG EAT
(spec 1.8 µm/step, radius 55 mm) with an IMX455-class sensor (9576×6388 @ 3.76 µm), focuser step
0.269 µm, f/6.3, 882 mm, corrector mounted on the drawtube.

---

## 1. The defect

`TiltScrewGeometry.ScrewCorrectionMicrons` computes the backfocus component as the fitted
paraboloid's curvature sag evaluated at the screw position:

```csharp
double backfocusCorrection = -(kx * cx * cx + ky * cy * cy);
```

`SignedTotalAdjustment` then divides by µm-per-step to get a screw move. The method's own doc
comment states the assumption plainly: *"Both are in axial best-focus microns (the same unit as
physical screw travel), so the total is simply their sum."*

That is an **implicit gain of 1.0** — move the plate by X microns, remove X microns of curvature.

For the **tilt** term the assumption is exactly right: displacing a screw by X tilts the plane by X
at that screw, by construction. The code applies the same reasoning to the **curvature** term, where
it does not hold. Translating a flat sensor along the optical axis cannot make it match a curved
focal surface; it only re-centres the defocus. What actually changes the field curvature is a
second-order effect, and it is much weaker than 1:1.

**Measured gain on this rig: 0.140.** The backfocus recommendation therefore understates the
required move by **7.1×**.

| | steps | plate travel |
|---|---|---|
| what the model recommends today | 137 | 0.25 mm |
| what the measurement says is required | 974 | 1.75 mm |

## 2. The mechanism

The chain matters, because it determines when the gain is non-zero at all.

1. The tilt adapter sits between the corrector and the sensor, so moving the plate by δ is a pure
   image-space displacement: defocus changes by δ, coefficient 1.
2. Restoring focus moves the focuser by δ/m², where m is the corrector's transverse magnification
   (the focuser moves corrector *and* camera together, so in the corrector's frame the object moves
   and the image follows with longitudinal magnification m²).
3. **Because the corrector rides the drawtube, that focuser move changes the corrector-to-focal-plane
   distance**, and therefore changes m: `dm = −(m/f)·Δ = −δ/(m·f)`.
4. A corrector only cancels the telescope's field curvature exactly at its design magnification. The
   change in m leaves residual curvature, which is what the sensor measures as a change in sag.

So the backfocus gain is a *composite* quantity: field-curvature sensitivity to corrector position,
mediated by the refocus that the plate move forces.

**Consequence worth stating explicitly:** on a rig where the corrector is threaded to the telescope
and only the camera moves, step 3 does not happen, m never changes, and γ ≈ 0 — the tilt adapter
could not correct backfocus at all, at any travel. The correction only works because the corrector
moves with the focuser.

## 3. Can γ be predicted from the reducer ratio?

**No — not from the ratio alone.** From §2, γ ∝ (1/f)·(dSag/dm). Predicting it requires:

- the corrector's **focal length** f (not the reducer ratio), and
- **dSag/dm**, how much residual field curvature a unit change in magnification produces — a property
  of the corrector's internal design (element powers and spacings), not of its net magnification.

Two correctors with the same 0.9× ratio can have quite different γ. Working the numbers backwards on
this rig: closing the measured 25.08 µm of sag at r = 14.43 mm needs Δm ≈ 0.0116 (a 1.2% change in
magnification), which implies dSag/dm ≈ 2172 µm at that radius. Nothing in "0.9×" predicts that.

**What *can* seed γ without measuring it:** manufacturers commonly publish a backfocus tolerance
("55 mm ± 0.5 mm"). That tolerance is γ in disguise — it is the spacing error at which residual
curvature becomes visually unacceptable. If a user knows their corrector's tolerance and the sag
threshold it implies, γ can be estimated to within a factor of ~2. That is far better than the
current 7× error, and it is the basis for the manual-override path in §5.

The measurement remains strictly better, and it is already free (§4).

## 4. The calibration run already measures γ

The **AllInward** step is a controlled piston: all four screws move by a known amount, and the
curvature effect is measured before and after. That is exactly the experiment needed.

It must be **drift-corrected**, using the same `mid(Baseline, ReBaseline1)` reference the piston-implied
pitch already uses — the curvature itself drifts substantially over a run (−268.6 → −194.1 µm across
this run's four baseline-equivalent states, ≈ +3.1 µm/min):

| | value |
|---|---|
| Baseline curvature effect at r = 55 mm | −268.6 µm |
| ReBaseline1 | −223.7 µm |
| drift-corrected reference, mid(Baseline, ReBaseline1) | **−246.11 µm** |
| AllInward | −208.2 µm |
| Δ over 150 steps | **+37.88 µm → 0.2526 µm/step** |

**Validation against an independent experiment.** The five-run piston sweep (−100 … −500, a 3.3×
larger excursion in the opposite direction) gives **0.258 µm/step**. The two agree to **2.1%**.

Drift correction is not optional: uncorrected, the AllInward delta reads 0.402 µm/step, **59% too
high**. The same sweep, uncorrected, reads 1.958 µm/step for the piston pitch instead of 2.32 —
thermal drift of −26.2 focuser steps/min ate 18% of the signal.

### Supporting results from the same datasets

- **Piston pitch cross-validated.** AllInward (single +150 push) gives 2.3058 µm/step; the
  drift-corrected 5-run sweep (−500 pull) gives 2.3226–2.3404. Agreement ~1% across opposite
  directions and a 3× range difference.
- **Frame factor F = 1.29–1.30 → m ≈ 0.88**, consistent with a ~0.9× corrector.
- **The piston response is linear to ~1.3%** over 930 µm of focuser travel. The residual quadratic
  has the sign and magnitude predicted by F = 1/m² with the corrector moving. Non-linearity is real
  but is not a significant error source at these excursions.
- **Field dependence of F is 0.76%** out to r = 14.43 mm, measured drift-immune as the difference
  between the centre and corner-mean piston slopes (8.700 vs 8.634 focuser steps per motor step).
  This is tighter than, and supersedes, the 3.5% ± 1.3% figure derived earlier from separately
  drift-corrected centre and corner pistons.

## 5. Design

**γ is measured, persisted, and used** — the corrected figure is what the apply path acts on. The
uncorrected figure stays visible for comparison, and the user can override or scale back the move at
the point of applying. Applying a known-wrong number by default is not a defensible conservatism;
the honest handling is to apply the corrected value and make the choice visible and adjustable.

1. **Compute γ** in `TiltCalibrationCalculator`, from the drift-corrected AllInward curvature delta,
   as a dimensionless gain (µm of curvature effect per µm of plate travel). NaN when no piston ran.
2. **Sanity-band it.** Accept only γ ∈ [0.05, 0.5]. Outside that, treat it as unmeasured — a
   mis-measured γ is the principal risk of this change, and one noisy run must not produce a
   multi-millimetre recommendation.
3. **Persist** the last accepted γ alongside the measured pitch, so runs that skip the piston step
   still benefit. Record its provenance (measured this run / persisted from an earlier run /
   manual override / unavailable).
4. **Manual override.** Let the user enter γ directly, for rigs where the corrector's published
   backfocus tolerance is the best available information (§3), or where they have measured it
   themselves.
5. **Apply the corrected value, with the choice exposed.** The guidance shows the gain-corrected
   figure as the recommendation and the uncorrected one alongside it for comparison, labelled with γ
   and its provenance. At the point of applying, the user can override the amount or scale it back —
   the corrected move is a floor (§6) and iterating is the intended workflow, so a partial apply must
   be a first-class action rather than a workaround. The detailed UX is specified in §8.
6. **Hands-off automation follows a pre-set policy.** Automatic Adjustment applies without a user
   present, so its behaviour is decided in advance in settings rather than at apply time, including
   what it does when γ is unavailable. See §8.5.
7. **Default `MeasureCurvatureDuringCalibration` to ON.** It is now the source of three distinct
   quantities — adapter direction, piston-implied pitch, and γ — and without it most users never get
   an accurate backfocus number. Both optional measurement steps gain explanatory copy stating what
   the extra autofocus runs buy.

## 6. What this does not fix

γ corrects the **conversion**. It does not correct the **curvature magnitude** being converted, and
the two errors compound.

The magnitude comes from the fitted paraboloid, which §4 of
`tilt-calibration-pitch-nonlinearity-design.md` shows under-reads curvature. On this run the
paraboloid puts the baseline curvature effect at −246 µm while the corner-region AF puts it at
−364 µm — a factor of 1.48, which is the difference between a 1.75 mm and a 2.54 mm recommendation.

So after this change the recommendation should still be expected to land low, and should be treated
as a floor rather than a target until the vertex-estimator work in §8 of that document is done.
Iterating (apply, re-measure, refit) converges regardless, because the *local* gain is well
determined even when the extrapolation to zero sag is not.

## 7. Deliberate decisions

- **Apply the corrected value rather than the known-wrong one.** An earlier draft of this design kept
  the uncorrected figure as what gets applied, on the grounds that a 7× change should not switch over
  silently. That was reversed: continuing to apply a figure measured to be 7× too small is not
  caution, it is a known defect left in place. The correct handling is to apply the corrected value,
  keep the uncorrected one visible for comparison, and make overriding or scaling back a first-class
  action at apply time (§8).
- **Sanity band before persistence**, so a bad run cannot poison later runs that rely on the
  persisted value.
- **γ is reported as a gain, not folded into the pitch.** They are independent quantities measured by
  different parts of the same step, and conflating them would make both harder to debug.
- **No change to the tilt term.** Gain 1.0 is correct there and is validated by the synthetic
  round-trip test added in PR #178.

## 8. Apply-time UX

### 8.1 The three apply surfaces, and the invariant between them

- **The Tilt Adapter Guidance table.** For a hand-turned adapter *the table is the apply surface* —
  the user's hands execute it. There is no software control to attach a choice to.
- **Automatic Adjustment (T14).** `RunAutomaticAdjustmentAsync` unconditionally awaits
  `showAdjustmentPromptAsync` — **there is always an approval dialog**. This corrects a premise in
  the brief that produced this design: there is no unattended apply path today.
- **The Manual Adjustment pad.** Raw user-typed steps, not guidance-driven. Untouched.

`BuildPerScrewTargets` was deliberately written so the planner "can never diverge from the guidance
table the user actually approved". **That single-pipeline property is a safety feature and this
design preserves it**: there is one resolved backfocus figure, and both the table and the planner use
it. The apply-time *choice* therefore lives only where software sends moves — the approval dialog.

### 8.2 The corrected figure owns the table

The Backfocus and Total rows show the **gain-corrected** amounts. The uncorrected figure appears as a
single comparison line beneath the table, **not** as a parallel column: two per-screw Totals would
leave a manual user with two instructions and no recommendation. This resolves an ambiguity in §5.5,
whose wording permitted the worse layout.

```
  Backfocus  +974 steps  +974 steps  +974 steps  +974 steps
  Total      +992 steps  +967 steps  +956 steps  +981 steps
  Backfocus is gain-corrected: γ = 0.140 (measured) · uncorrected: +137 steps
```

When γ is unavailable the table shows the uncorrected figure and the line reads: *"Backfocus is a
lower bound: the gain is unmeasured, and plate travel rarely changes curvature 1:1 — the real move is
usually several times larger. Run a calibration with Measure direction on to measure it."*

### 8.3 The apply-time control

The approval dialog's Apply row gains an amount selector beside the Backfocus checkbox — a ComboBox
of four fixed presets, recomputed per invocation and **never persisted** (a remembered scale-back
would silently degrade every later session):

```
Apply  [x] Tilt correction  [x] Backfocus correction  [Corrected · +974 steps ▾]  γ = 0.140 (measured)
                                                       │ Half · +487 steps
                                                       │ Quarter · +244 steps
                                                       │ Uncorrected · +137 steps (gain 1.0)
```

Default is Corrected. Changing the selection re-invokes the replanner so the move list stays WYSIWYG,
honouring the contract `ApplyTilt`/`ApplyBackfocus` already keep. With γ unavailable the ComboBox
collapses to `+137 steps (lower bound — gain unmeasured)`.

**Presets rather than a percentage or free step entry.** A bare percentage hides the physical number
being approved; free entry duplicates the Manual Adjustment pad, which already exists for arbitrary
amounts; presets make the choice auditable as one token in the log. "Uncorrected" is included as an
honestly-labelled continuity option, not disguised as a percentage. Because the corrected figure is a
floor (§6), partial application is the *intended* workflow — Half and Quarter are normal iteration
granularity, not expressions of distrust.

### 8.4 Out of range means a spacer, not a bigger command

The per-command cap is **not** the right trigger: 974 steps against a 200-step cap simply means the
planner emits five chunked commands, which `TiltMovePlanner.ApplyCap` already handles correctly. The
quantity a human can act on is **common-mode plate travel in millimetres**, with a fixed **1.0 mm**
advisory threshold (a constant, not an option — it needs no per-rig tuning).

The advisory is **never blocking**. It appears under the guidance table and, tracking the selected
preset, in the dialog. The existing hard travel limit (`TiltDeviceMaxExcursionSteps`) remains the only
thing that stops Proceed. Text:

> The corrected backfocus move is about {0:0.00} mm of plate travel — more than a tilt adapter is
> meant to take up. Change the camera spacing by about that amount (in the direction the Backfocus
> arrows show) and keep the adapter for the remaining trim. The corrected figure is a lower bound, so
> re-measure after adjusting.

γ applies to spacers identically: the §2 mechanism is spacing → refocus → magnification, however the
spacing changes.

### 8.5 Automation policy: deliberately no new setting

**Automatic Adjustment applies 100% of the same resolved figure the guidance table shows.** There is
no `AutomationBackfocusPolicy` enum, and one must not be added later — it would break the
single-pipeline invariant in §8.1, letting the table say one thing and the hardware do another.

Full application is right because the corrected figure is a floor (§6): the magnitude it converts is
itself under-read, so even 100% under-shoots in expectation. Overshoot could only come from a
mis-measured γ, which the sanity band caps, the AllInward/sweep cross-validation supports (2.1%
agreement), and the excursion limit physically bounds. Defaulting automation to Half would rebuild
the same defect at 2× instead of 7×.

When γ is unavailable, automation applies the uncorrected figure labelled a lower bound — today's
behaviour. Skipping backfocus entirely would regress unmeasured rigs, and under-moving is
directionally safe: each cycle re-measures, so gain-1 application still converges, just slowly.

**The escape hatch is the γ override, with its range widened to [0.05, 1.0].** The *measurement* band
stays [0.05, 0.5] — that band encodes "γ ≈ 1 is physically implausible as a measurement". A manual
override is a user assertion, not a measurement, and capping it at 0.5 would leave no way to request
legacy behaviour. Override = 1.0 exactly reproduces pre-change behaviour and is documented as such.

### 8.6 Provenance

One short parenthetical wherever γ appears; the mechanism lives in tooltips. The inspector says
`measured` or `manual override` (it cannot distinguish this run from an earlier one and should not
pretend to). The wizard's results panel shows a self-labelling row in the `PistonPitchDisplay`
pattern, **including the rejected case** — silently treating an out-of-band γ as unmeasured is right
for persistence but confusing as display:

> Backfocus gain: rejected (measured 0.62, outside 0.05–0.5) — not saved; guidance keeps the previous value.

The override control is a checkbox plus an always-visible-but-disabled TextBox driven by a
`DataTrigger`, keeping `TiltAdapterWizard/DataTemplates.xaml` converter-free per its THEME HAZARD note.

## 9. Open questions

- Whether γ is stable across focuser position. It is measured near the calibration focus; the
  required correction may be millimetres away, where m — and therefore γ — differs. The measured 1.3%
  non-linearity in the piston response suggests the drift is small over ~1 mm, but this is untested.
- Whether the sag target is exactly zero. The design assumes the corrector fully flattens at correct
  spacing; a corrector that leaves residual curvature by design has a non-zero target.
- Whether γ should eventually be measured at two piston offsets within one calibration, which would
  give the local slope without relying on the drift correction being right.
