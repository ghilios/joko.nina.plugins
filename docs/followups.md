# Follow-ups register

Findings that surfaced while doing something else, were deliberately **not** acted on at the time, and would
otherwise be lost. Each entry says what was observed, the evidence, why it matters, and the suggested next step.

**Conventions.** Keep entries short and falsifiable — quote the measurement, not an impression. When one is picked
up, write its spec in `docs/<topic>-design.md` and its plan in `plans/<topic>-plan.md`, then mark the entry
**Done** with the commit or PR that closed it rather than deleting it, so the reasoning stays traceable.

Status: **Open** · **In progress** · **Done** · **Won't fix**

---

## Detector / optimizer behaviour

### F76 — The optimizer wizard opens with its footer below the bottom of the screen, so `Accept` is unreachable until the window is moved
**Status:** **FIXED and CONFIRMED IN THE FIELD 2026-08-11.** Took SIX attempts and three distinct root causes: the wrong height was read, then the write was overwritten by `SizeToContent`, then the width froze and clipped `Accept`/`Close`. **Every one was settled by a log line, none by reading the source** · found 2026-08-11 (wave 21) by the A1–A9 *rendered pixels* check, on the first run of the wizard that check has ever completed · **SUBSTANTIALLY CORRECTED the same day — see "what the first write-up got wrong"**

Running the Optimization Wizard to completion (Plugins → Hocus Focus → Star Detector → **Optimize Star
Detection**) produces a long summary. **The footer — `Back | Review frames | Continue optimizing | Accept |
Close` — was not on screen**, and the only reachable control was the title-bar **X**, which *discards* the run,
while the help text says *"…then Accept to apply the selected variant … or Close to discard."*

**The one measurement taken BEFORE anything was perturbed**, read via `GetWindowRect` on the wizard's hwnd:

```
window   : T=444  B=1836   height=1392
work area: T=0    B=1392   height=1392
screen   : 3440 x 1440
```

**The height is already correct** — 1392, exactly the work area, which is `ClampWindowToWorkArea` doing its job.
**The position is not:** the window's bottom sits **444 px below the work area** (396 below the screen), so the
correctly-pinned footer renders off the bottom of the display. `ClampWindowToWorkArea`'s docstring says
`WM_WINDOWPOSCHANGING` clamps the height *"and nudged back on-screen"*; the height clamp demonstrably ran and
the on-screen nudge demonstrably did not leave the window on-screen. **Which of the two — never fired, or fired
and was overridden by NINA's owner-centring — is NOT established here.**

### What the first write-up of F76 got wrong, and why it is recorded rather than quietly amended

The first version of this entry asserted the cause was *"a `SizeToContent` window with no ScrollViewer"* and
recommended *"a ScrollViewer around the content with the action row pinned outside it."* **Reading the source
refuted every part of that:**

| the claim | the source |
|---|---|
| "no ScrollViewer" | **There is one** — `Optimization/DataTemplates.xaml:292`, closed at `:1190`, `VerticalScrollBarVisibility="Auto"` |
| "the action row should be pinned outside it" | **It already is** — the root grid has four rows; the body ScrollViewer is `Grid.Row="1"` (`Height="*"`) and the footer is a later `Auto` row, outside it (`:1240+`). The XAML comment at `:762` states the intent: *"Accept lives in the footer outside this ScrollViewer and is always reachable"* |
| "WPF clamps a SizeToContent window to the work area" | The clamping is **deliberate plugin code**, `StarDetection/Optimization/ClampWindowToWorkArea.cs`, whose docstring describes *this exact failure mode* as the thing it exists to prevent |

**So the recommended fix was already shipped, and the design is right.** The defect is that the window is
*placed* outside the work area, not that its content cannot scroll.

**Evidence withdrawn as confounded.** After the first measurement the controller moved and tried to resize the
window with `SetWindowPos` to bring the footer into view. Every later observation is therefore about a window
the controller had perturbed, and three of them must not be quoted:

- *"all five buttons report coordinates `(0,0)`"* — taken after the move, and `(0,0)` is the automation's
  no-resolvable-point value, not a measured location.
- *"no inner ScrollViewer responds to the wheel"* — the wheel was sent over a `DataGrid`, which swallows it, and
  **scrolling was never going to reveal the footer anyway**, because the footer is outside the ScrollViewer by
  design. The test was invalid for the conclusion it was used to support.
- *"the window refuses programmatic resize"* — expected: `ClampWindowToWorkArea` hooks
  `WM_WINDOWPOSCHANGING` precisely to override external resizes. That was the guard working, quoted as a symptom.

*A finding whose mechanism is refuted by the source it is about must be rewritten, not defended — and the parts
of it that were measured on an instrument the observer had already disturbed have to be withdrawn by name.*

**What survives is still a real user-facing defect** and still explains the original symptom: on a 3440×1440
display the wizard opens with `Accept` off the bottom of the screen.

**Why the suite is green, unchanged.** `StarDetectionOptimizerWizardVMTests` has **180 passing tests** and is
right to pass — the ViewModel is correct and so is the XAML. The failure is in window *placement*, which no VM
test and no XAML review reaches. Wave 13's results named this gap: *"what is untested is … that these rows
appear, in the right panel, unclipped, in the running app."*

### The defect is LOCATED — by the OWNER'S behavioural evidence, which refuted the controller's third guess too

The owner reproduced it on a smaller screen and supplied the two state transitions that discriminate between
hypotheses. **This is the account that survives; the two earlier ones in this entry did not.**

| # | observed | what it proves |
|---|---|---|
| 1 | the wizard **opens** with its bottom below the screen; the body's ScrollViewer is present and **working** (visible, draggable scrollbar) | the layout is fine — the *window* is too tall |
| 2 | **dragging** it makes it **jump to the top of the screen, and the footer is STILL not visible** | **the height is `> work area`.** Had `cy` been clamped, a window at `work.Top` would be wholly on screen. The jump is `ClampWindowToWorkArea:127-133` firing in sequence: `y = work.Bottom - cy` goes negative, then `if (y < work.Top) y = work.Top` pins it to 0 |
| 3 | **resizing** it by hand makes the buttons **appear immediately** | resizing is the one action that makes WPF set `SizeToContent = Manual` |

**So the height clamp is not holding, and row 3 says why.** `SizeToContent` is still **active** on this window.
The hook only edits `pos.cy` inside individual `WM_WINDOWPOSCHANGING` messages; it **never turns `SizeToContent`
off**, so WPF keeps re-asserting a content-derived height and wins the tug-of-war. A user resize is the fix
precisely because WPF *automatically* switches `SizeToContent` to `Manual` when the user resizes a window —
after which the Grid reflows (body ScrollViewer at `Height="*"` shrinks, footer `Auto` row gets its space) and
the buttons render.

**The correct pattern is already in this codebase, in the same folder.** `Review/ReviewViewportHostBase.cs:86`
does it the working way — it takes control in WPF rather than fighting it from below:

```csharp
var work = SystemParameters.WorkArea;   // DIPs, excludes the taskbar
window.SizeToContent = SizeToContent.Manual;
window.Height = Math.Min(DesiredWindowHeight, Math.Max(window.MinHeight, work.Height - margin));
window.Top    = work.Top + (work.Height - window.Height) / 2.0;
```

`grep -rn SizeToContent` over `StarDetection/Optimization/` returns that file and two *comments* in
`ClampWindowToWorkArea.cs` — **the wizard never disables it.** Two sibling windows, two strategies; the one that
clamps at the Win32 level while leaving `SizeToContent` on is the one that fails.

**Confidence, stated honestly.** Row 2's inference (`cy > work area`) is solid — it follows from the observed
geometry alone. That `SizeToContent` re-assertion is the *cause* is the leading hypothesis, strongly supported by
row 3 and by the sibling window's contrasting approach, but the message sequence has still not been logged.

### FIXED by MEASUREMENT — and the measurement cost 20 lines of logging after four wrong diagnoses

The clamp was made self-reporting and the owner ran the wizard once. **The log named the defect in a single
line**, and nothing about it could have been reached by reading the source harder:

```
F76 clamp: attached to 'CustomWindow' hwnd=557846578; workArea=0,0,1280,752
F76 clamp: h=505 actual=505 top=101 stc=WidthAndHeight work=752@0 => declined
F76 clamp: h=505 actual=492 top=123 stc=WidthAndHeight work=752@0 => declined
F76 clamp: h=492 actual=581 top=123 stc=WidthAndHeight work=752@0 => declined
F76 clamp: h=581 actual=492 top=123 stc=WidthAndHeight work=752@0 => declined
F76 clamp: h=492 actual=817 top=123 stc=WidthAndHeight work=752@0 => declined   <-- the summary step
```

**`ActualHeight` reached 817 against a 752 work area.** Bottom at `123 + 817 = 940` versus `752` — **188 DIPs,
about 282 physical px at the owner's 150 % scaling**, which is precisely the overflow in the screenshot. And
`Height`, the **requested** value, lagged at **492**. The code read `Height`, saw `492 <= 752`, and declined.

**Everything else was already correct**: the behaviour was attached (to NINA's `CustomWindow`), the work area was
right, the geometry was right, `SizeToContent` handling was right. **The only defect was WHICH NUMBER was
measured** — and three of the four earlier write-ups had proposed changing things that were already correct.

`EffectiveHeight(actualHeight, heightProperty)` now returns `max(ActualHeight, Height)` with `NaN` treated as 0:
the rendered height is what puts `Accept` off the bottom, and a larger pending request would do so on the next
layout pass. Pinned by three tests carrying the measured values **verbatim**, so this entry and the assertions
cite the same numbers. Red against exactly what shipped: mutant **M-F76c** ("prefer the requested height") fails
`EffectiveHeight_PrefersTheRenderedHeight_WhenTheHeightPropertyLags` and
`TheMeasuredOverflowingWindow_IsBroughtFullyOnScreen`, **2 of 10**. Suite **3905**.

### The lesson, which is the expensive part of this entry

**F76 was diagnosed wrong FOUR times, every one of them by reasoning from source, and settled on the first run
that produced numbers.** The four:

| # | claimed cause | refuted by |
|---|---|---|
| 1 | "`SizeToContent` window with **no ScrollViewer**" | the ScrollViewer is at `DataTemplates.xaml:292` with the footer already pinned outside it |
| 2 | "the **`y` clamp** is missing / unreachable under `SWP_NOMOVE`" | both code paths already clamp `y` |
| 3 | "`SizeToContent` is never **disabled**, so WPF re-asserts the height" | plausible, shipped, and the bug survived it — the owner reproduced on the fixed build |
| 4 | "the condition asks *too tall* instead of *does it fit*" | a real gap, fixed, but still not why it declined |

Each proposed a change to code that was already correct, and the third one **shipped a fix that did nothing**.
The controller also mis-attributed a red CI run to its own new test and marked it `[Explicit]`, removing the only
test that catches the defect from CI — reverted after noticing the next commit passed with the same fixture.

> **Instrument first when the mechanism is not observable.** Twenty lines of `Logger.Info` printing the numbers a
> decision is made from would have been cheaper than any single one of those four attempts, and cheaper than the
> two ~20-minute runs the owner spent reproducing. **A fix that cannot report whether it engaged is not
> finished** — attempt 3 shipped, changed nothing, and looked identical from the outside to a fix that worked.

### F77 — The plugin deploy fails SILENTLY while NINA is running, so two field tests ran against a stale binary
**Status:** **CLOSED — fixed and verified in both directions, wave 22 (2026-08-12)** · found 2026-08-11 (F76's
confirmation) · **cost: two ~20-minute owner runs that tested the wrong code**

The csproj's PostBuild step xcopies the plugin into NINA's plugin folder on every build. **NINA holds that DLL
open while it runs, the copy fails, and nothing surfaces it** — the build reports success and the developer
believes the fix is deployed.

Measured: NINA ran `15:02Z → 16:26Z`. Every build in that window — including the ones carrying `a68c7f2` (the
*does it FIT* widening) and `2489a78` (`EffectiveHeight`) — left the deployed DLL at its `14:59:44Z` version.
Proven rather than assumed: with NINA closed, `cp` of the same file **succeeded immediately**, and
`strings <deployed> | grep -c EffectiveHeight` went `0 → 1`.

**Consequence, and it is the dangerous part.** The owner re-ran the wizard to confirm F76's fix. That run
exercised a binary **without** the fix, and its log looked *encouraging* — the window reached `752@0`, fitting
the work area exactly. Reading that as "the fix works" would have been a false confirmation of code that was
never loaded. **A green field test against a stale binary is worse than no test**, because it closes the entry.

**Check the binary, not the build log.** Before quoting any field result, verify the deployed artifact contains
the symbol under test:

```bash
strings "<plugin dir>/NINA.Joko.Plugins.HocusFocus.dll" | grep -c <NewMethodName>
```

and compare its mtime against the process start time (the NINA log filename encodes both:
`<yyyyMMdd>-<HHmmss>-<version>.<pid>-*.log`). *This is the same family as [F66](#f66) — an instrument that cannot
report its own failure — applied to the deploy step rather than to a test.*

### CLOSED — the mechanism, measured, and a warning proven in both directions (wave 22, 2026-08-12)

**Root cause, measured rather than read.** The PostBuild `Exec` is a **ten-command batch**. The plugin DLL
`xcopy` is the **first**; the last is `Microsoft.CodeAnalysis*.dll`. A batch's exit code is its *last* command's,
so an early failure is swallowed whole. Probed directly with a throwaway batch — a failing `xcopy` sets
`errorlevel=4`, a later `echo` runs, and the batch still exits **0**. MSBuild never had anything to report.

**Fix.** An `if errorlevel 1 echo <proj> : warning HF0001: …` immediately after the DLL copy, and `HF0002` after
the pdb. The canonical MSBuild diagnostic format makes these **real warnings** (they appear in the `Warning(s)`
count), while the build still *succeeds* — so `dotnet test` is not broken by an open NINA, which is the common
case. The checks sit immediately after their own `xcopy` because `echo` resets `errorlevel`.

**Both branches measured on real artifacts, in one sitting:**

| branch | condition | HF0001 | deployed DLL |
|---|---|---|---|
| FAIL | NINA pid 86368 holding the DLL | **fires**, `3 Warning(s)`, build still succeeded | SRC `c18987a…` ≠ DST `7dae5fb…`, DST frozen at `12:53:10Z` |
| PASS | NINA closed | silent, `0` HF warnings | hashes **match** `c18987a…`, DST `13:03:27Z` |

The lock was proven, not assumed: `Get-Process 86368 | Modules` listed the deployed DLL, and
`[IO.File]::Open(dst,'Open','Write','None')` threw *"being used by another process"*.

**The instrument discriminates per file, not per "is NINA running".** In the FAIL build `HF0002` stayed
**silent** and the pdb hashes matched — NINA locks the DLL but not the pdb. A blanket "NINA is running" warning
would have fired on both and taught nothing.

**Refinement to the §4 check — the lock is acquired at plugin LOAD, not at NINA start.** At `12:53:10Z` the copy
**succeeded** even though NINA had been running since `08:58:23Z`, because the plugin had not been loaded yet; it
locked the file later. So "NINA is running" is neither necessary nor sufficient for the deploy to fail — and,
worse, **the mtime/hash check can PASS while a long-lived session still runs the older code in memory**. Comparing
the DLL's mtime against the NINA log's start time (`<yyyyMMdd>-<HHmmss>-<version>.<pid>-*.log`) remains the only
way to prove *which* binary a session actually loaded. HF0001 closes the on-disk hole; the in-memory hole is
closed only by restarting NINA after a deploy.

### CONFIRMED IN THE FIELD — the same log line, on the same window, now clamps

Third run, on a binary verified to contain the fix per [F77](#f77): session started `12:36:16` local and loaded
the plugin at `12:36:26`; the fixed DLL was deployed at `16:29:07Z`, **seven minutes earlier**.

```
F76 clamp: attached to 'CustomWindow' hwnd=333978352; workArea=0,0,1280,752
F76 clamp: h=505 actual=505 top=101 stc=WidthAndHeight work=752@0 => declined
F76 clamp: h=505 actual=492 top=123 stc=WidthAndHeight work=752@0 => declined
F76 clamp: h=581 actual=581 top=123 stc=WidthAndHeight work=752@0 => declined
F76 clamp: h=581 actual=492 top=123 stc=WidthAndHeight work=752@0 => declined
F76 clamp: h=817 actual=817 top=123 stc=WidthAndHeight work=752@0 => CLAMP h=752 top=0   <-- the summary
F76 clamp: h=752 actual=752 top=0   stc=Manual         work=752@0 => declined            <-- settled, idempotent
```

**The before/after differ in exactly one quantity, which is the whole fix:**

| | the summary step |
|---|---|
| shipped (declined) | `h=492 actual=817 top=123 => declined` — read the stale requested height |
| fixed (clamps) | `h=817 actual=817 top=123 => CLAMP h=752 top=0` — reads the rendered height |

The settled line is the property that matters: height **exactly** the work area at `top=0`, so `top + height =
752 = work.Bottom` and the footer — `Back / Review frames / Continue optimizing / **Accept** / Close` — is on
screen. `stc=Manual` confirms `SizeToContent` was disabled *because* the window shrank, and the second pass
**declines rather than recursing**, which is what `ClampingIsIdempotent_*` asserts offline.

**The instrumentation is kept deliberately.** It is a handful of `Logger.Info` lines per wizard run, one per
distinct state, and it is the only reason this entry has an answer instead of a fifth guess. An entry with four
wrong diagnoses has earned the right to keep reporting itself.

### The last cause, and why it hid behind two others

`SetWindowPos` is what separated the two operations that had always been issued together:

```
asked cy=1128px y=0px;  rect was 738px@119  now 738px@0;  wpf H=492 A=492 stc=Width
```

**One call: the MOVE took, the RESIZE did not.** So nothing was rejecting the write — something
*re-imposed* the height afterwards, and `738px / 1.5 = 492 DIPs` is exactly WPF's own `Height`.
With `SizeToContent` still set (to `Width`), **WPF re-applies its size on every layout pass** and
overwrites the property assignment and the native resize alike.

That door was held open by an earlier fix in this same entry: `SizeToContent` was kept at `Width`
so the footer would not be clipped. Correct for the width, and the precise reason the height never
stuck. *Three rounds of "the write is being discarded" were one cause wearing three disguises —
"refused" and "overwritten" look identical from outside, and I treated them as the same thing.*

Resolved by going fully `Manual` **with the width explicitly carried over from `ActualWidth`**.
Freezing the width at `Manual`'s default is what clipped `Accept`/`Close`; by the summary step
auto-sizing has already produced the right width, so pinning *that* keeps the footer while handing
us the height.

### Applied to the review windows — CLAMP ONLY, and the distinction is load-bearing

`ClampWindowToWorkArea.Enabled` now attaches to `FrameReviewControl` and
`AutoFocusFrameReviewControl`, giving them the ongoing work-area clamp, the maximize cap and the
on-load correction. `ReviewViewportHostBase` already sets `SizeToContent = Manual` and sizes to
`work - 40`, but **only once, behind a `windowSized` latch**, so nothing maintained it afterwards.

**Growing to content is now opt-in (`FitContent`), OFF by default, and the review windows must not
set it.** They host an image viewport whose ScrollViewer extent is the *image*: growing to it would
size the window to the picture rather than to a sensible dialog, and would fight
`ReviewViewportHostBase`'s own fit logic. The wizard opts in; nothing else does. A test guards the
**default** rather than the wizard's opt-in, because the default is what every future window
inherits.

### F78 — `SystemParameters.WorkArea` reports the PRIMARY monitor, so the WPF half of the clamp uses the wrong rect on a secondary display
**Status:** **open — BLOCKED on hardware, not on understanding** · raised 2026-08-12 (wave 22) · **cost to fix: ~25 m; cost to VERIFY: a second display this machine does not have**

`ClampWindowToWorkArea` resolves the work area **twice, by two different instruments**, and only one of them is
per-monitor. The Win32 half already does the right thing via `MonitorFromWindow`. The WPF half reads
`SystemParameters.WorkArea`, which is documented to return the **primary** monitor's work area regardless of
where the window actually is. Four call sites pass it straight into `ApplyWorkAreaLimit`:

```
StarDetection/Optimization/ClampWindowToWorkArea.cs:105, :186, :197, :203
StarDetection/Optimization/Review/ReviewViewportHostBase.cs:83
```

A wizard opened on a secondary display is therefore clamped to the *primary* display's rect — the exact class of
bug F76 was, with the same symptom (a footer off-screen) and a different cause. The shape of the fix is small and
known: a `WorkAreaFor(Window)` helper resolving via `MonitorFromWindow` + `GetMonitorInfo` (the P/Invoke is
already in this file), converted to DIPs, replacing all five reads.

**Why it is NOT shipped.** This machine has exactly one display —
`\\.\DISPLAY1 primary=True bounds=3440x1440 work={0,0,3440,1392}`. On one monitor `MonitorFromWindow` returns
the primary, so the change is **provably inert here and cannot be exercised in either direction**. Shipping it
would mean adding a fix that cannot report whether it engaged — the precise failure this register has paid for in
[F76](#f76) (five wrong diagnoses from reading source) and [F77](#f77) (a green field test against code that was
never loaded). **A gate never shown to PASS is not a gate.**

### Next step
Either attach a second display and measure both branches — window on secondary clamps to the secondary's rect,
window on primary unchanged — or factor only the *selection* (given a window rect and a set of monitor rects,
which work area applies) into a pure function and unit-test that, accepting that the `MonitorFromWindow` plumbing
stays unexercised. Do not ship the plumbing on the strength of reading the documentation.

### Still owed
Nothing on the fix itself. **Previously owed and now discharged:** *neither of the owner's first two runs tested
the fix* — the first ran the original
`ApplyWorkAreaLimit` (which genuinely declined, and that measurement stands), the second ran the same stale
binary again. The fixed DLL was deployed at `16:29:07Z`, verified to contain `EffectiveHeight`.
**The next wizard run is the first real test of the fix.** Nobody has yet seen the footer appear. The owner's NINA is running the pre-fix build; the
next wizard run on the current build either shows `Accept` or leaves a `F76 clamp: … => CLAMP …` line with the
next number. **Do not mark this closed until one of those two is in hand** — this entry has claimed "fixed" once
already and was wrong.

**Known, unaddressed:** `SystemParameters.WorkArea` reports the **primary** monitor's work area. If the wizard
opens on a secondary monitor the clamp will use the wrong rectangle. The Win32 half already resolves the correct
per-monitor work area via `MonitorFromWindow`; the WPF half does not. Deliberately not folded in until the fix
above is confirmed.

### F1 — The donut heuristic misses small donuts
**Status:** Open · found 2026-07-30 during the bank donut audit

`BankVerification.Decide` flags a run donut-aware only when
`donutPeakFracMax >= 1.0 AND (extremeFrameMedianHFR >= 9.0 OR extremeDonutBBoxMedianPx >= 24)`. The second clause
is calibrated for **large** donuts and vetoes small ones.

**Evidence.** `FlyData` (frac 10.36, extremeHFR 5.0, bbox 13 → vetoed) shows unmistakable annuli — a bright ring
with a dark centre in nearly every one of the 36 largest stars at its most-defocused frame. Meanwhile `toml999`
(frac **47.17**) and `bobp_m101` (frac **39.00**) are pure filled disks, so `frac` alone predicts nothing: on
saturated runs a flat-topped clipped core mimics the donut signature, which is exactly what the veto was written
to suppress, and there it works correctly.

**Why it matters.** Annularity is a geometric ratio (the central-obstruction shadow), not an absolute size, so a
fast short-focal-length rig produces a genuine ring only ~10–15 px across. A vetoed run is scored with donut
detection off *and* its golden is built without `snr_ref --donut`, and per-pixel SNR under-counts donuts — so the
reference itself is incomplete, not merely the detector config.

**Next step.** Re-calibrate the heavy-defocus clause on a size-independent annularity measure (e.g. central-dip
depth relative to ring flux) rather than HFR/bbox thresholds. It changes the verdict for every run, so it needs
its own spec and a re-audit of the bank. Do **not** simply lower the thresholds — that would re-admit the
saturated-core false positives the veto exists to block.

### F2 — `cwhite_2026`'s donut status is ambiguous
**Status:** Open · needs a human eye

The validation anchor is donut-off because its `frac` is 0.67, below the 1.0 threshold. Its most-defocused stars
show **irregular, partial** central darkening at low SNR — not the clean symmetric rings of `mufti` / `Panos` /
`FlyData`, but not clean filled disks either. It is also the run the veto is named after
("cwhite-style over-flag avoided"), so its appearance informed the original calibration.

**Why it matters.** It is the harness anchor; if its donut status is wrong, the anchor is measured on the wrong
config. Bundle this with F1's re-audit rather than deciding it in isolation.

### F3 — `Wtie` does not prevent star-shedding in general
**Status:** Open · documentation/characterisation

`docs/optimizer-sensitivity-pinning-design.md` frames `Wtie = 0.02` as stopping the optimizer landing on the
star-shedding corner. Measured bank-wide with `Wtie` present (`ec6a1b4`), A/B configs still shed heavily:
`mccomiskey` A → recall@≥12 **0.079** vs C0's 0.871; `timmer` B → **0.055**; `CWhiteFocus` A at sens 50 → 0.327.

This is *correct by construction* — `Wtie` only arbitrates genuine plateaus, and these were real σ improvements —
but the doc should say it prevents shedding chosen **on a σ-wiggle**, not shedding generally.

### F4 — The objective has no sensor-model term
**Status:** Open · potentially the most consequential entry here

J scores curve tightness and star count, with no term for the downstream sensor/tilt fit. So it can pick an
operating point that tightens focus while destroying tilt measurement.

**Evidence.** `mccomiskey` config A: σ_focus 0.460 and J 0.9940, but sensor stars collapse 3,618 → **43**, sensor
R² 0.944 → **0.526**, alignment 9/9 → 7/9. Config B reaches the same σ_focus (0.441) at J 0.9950 while keeping
937 sensor stars and R² 0.954 — **A is dominated by B on every axis**, and J could not tell them apart (Δ 0.001).

**Next step.** Consider a sensor-fit guard or term in the objective, at least for the inspection variant.

### F5 — Donut detection halves σ_focus on a run with **no** donuts
**Status:** Open · unexplained

On `mccomiskey` (max HFR 2.01", no donuts — see the F1 audit), enabling the donut master takes σ_focus
**3.263 → 1.648**, a ~49% improvement, measured by one-at-a-time ablation holding sensitivity and star-clip fixed.

**Why it matters.** If the donut path's morphological processing improves HFR consistency generally rather than
only on rings, that is a free win available to every run — or a sign that something in the non-donut path is
worse than it needs to be. Worth understanding before anyone "optimises" the donut path away.

### F6 — Sensitivity and star-clip act only in combination
**Status:** Open · explains the documented plateau

One-at-a-time ablation on `mccomiskey` σ_focus (adaptive on, donut on, NC 4):

| | `clip 2` | `clip 10` |
|---|---|---|
| **`sens 10`** | 1.648 | 2.960 |
| **`sens 33.3`** | 1.503 | **0.441** |

At clip 2 sensitivity barely matters; at clip 10 it is decisive. Raising clip *hurts* at default sensitivity and
*helps* strongly at high sensitivity. Neither knob does anything useful alone — the objective surface is a
**diagonal valley**, not two independent axes, which is a concrete mechanism for the plateau
`docs/optimizer-sensitivity-pinning-design.md` describes and a hint that a coordinate-wise search is the wrong
shape for this space.

> **WAVE 26 — the 2×2 was replicated on the synthetic bank and the DIAGONAL SIGNATURE DID NOT APPEAR.** `Q26-A`
> ran F6's own ablation (sensitivity alone / clip alone / both) at the landed vector of all **8** at-floor
> wave-18 landings. **0 of 8** show `dJ(both)` with the opposite sign to `dJ(sens)`. So this entry is **cited,
> not extended**: F6's table stands on `mccomiskey`, a real-bank run, and the synthetic bank does not reproduce
> its sign structure at these points. **What DOES corroborate F6 is the landings themselves** — see
> [F83](#f83--j-carries-no-precision-term-on-an-unlabelled-run-so-a-sensitivity-pin-is-free-in-the-objective-by-construction)
> §"where the two knobs meet": the three at-floor landings with a genuinely low combined gate are exactly the
> three where the search **also** drove `StarClippingMultiplier` down, twice to the axis's own 0.25 floor.
> Reproduce: `/mnt/d/hf_w26/q26_score.txt`; `docs/synthetic-af-bank-followups-wave26-results.md` §4.3, §7.

### F104 — `D01`'s two candidate gates release 36 660 high-tier candidates and re-capture takes 92 %: the joint is material and ADDITIVE, so F99's joint-recovery claim is refuted at a pre-registered bar

**Status:** Closed — the product question is answered, and answered negatively · found 2026-08-13, wave 30,
`RULE W30-G` = **`G-NOT-RECOVERABLE`** · `/mnt/d/hf_w30/w30g_score.txt`, `w30g_manifest.tsv`

**[F22](#f22) FENCE, inline and mandatory:** on `D01_ultrawide_40mm` the detector's HFR over-reads by a large
factor below ~1.1 px measured HFR and its autofocus already lands at `BestJ 0.994825`. **No count in this entry
may be quoted as a recall or focus improvement for a user.** The only frame in which it may be read is goal 3 —
*is a landed knob sitting at an extreme against the physics?*

A 2×2 factorial on `MaxDistortion` (0.5 → 0.3) and `BrightnessSensitivity` (36.333 → 32.333, four axis steps
**computed** from the product's own declaration), five run cells and two imported from wave 29, on
`D01_ultrawide_40mm`, 9 frames per cell, on the B15 binary already on disk.

| quantity | value |
|---|---|
| `Σ R_g` over all single-knob cells | **36 660** |
| `Σ capture_g` | **33 884** |
| `recapture` (pooled) | **0.9243** — bar 0.90 → **`G-2` True** |
| `recapture` excluding the fenced `M4` | 0.9288 (agrees) |
| `FN_total(M0)` | **50 249** |
| material bar, **computed** as `0.01 × FN_total(M0)` | **502.5** |
| `ΔTP(M3)`, the joint | **+1 572** |
| `Σ ΔTP` over the unfenced singles `M1`+`M2` | **+1 480** |
| `ΔTP(joint) ≥ 502.5` | **True** |
| `ΔTP(joint) ≥ 2 × 1 480 = 2 960` | **False** → **`G-3` False** |

**The joint IS material** (3.1× the bar; 3.1 % of all misses) **and it is essentially additive**: the
interaction term is **+92**, a multiple of **1.06×** against a pre-registered bar of 2×.

**Re-capture dominates, and the mechanism is one column of the published matrix.** `TooDistorted` at 0.3
releases **12 090** candidates and **`LowSensitivity` absorbs 9 676 of them — 80.0 %.** Relaxing
`LowSensitivity` by 11 % on top converts **92** of those 9 676 — **0.95 %**. A 36 σ bar relaxed by 11 % does not
turn a low-fill blob into a detection, which is exactly the mechanism the pre-registration gave for predicting
this outcome.

**Goal-3 answer, and it is a negative.** No landed knob on `D01` is pinned against the physics in a recoverable
way. `MinStarBoundingBoxSize` at 6 (against a shipped default of 5) is the one knob at an extreme, and its flow
releases 9 537 candidates of which **3** reach acceptance. `MaxDistortion` landed mid-axis. **40.4 % of the
misses live upstream of both gates** — `NO CANDIDATE (structure gap)` 8 931 and `TooSmall` 11 372 — where no
setting of either gate can reach them. `D01`'s recall gap is the physics of a 19.4 ″/px rig with a ~0.7 px
kernel.

**Two rival predictions were registered before the data**, in the same pre-registration section, attached to
two distinct terminal verdicts of the same 36-region tree: `G-JOINTLY-BOUND` (F99's) and `G-NOT-RECOVERABLE`
(the design's). The data landed in the second. **Neither document could have claimed a hit it did not call**,
and this is the strongest evidential form the series has produced.

**Not measured, and owed by any ship that acts on this:** precision at the opened settings. 23–25 % of the
distortion gate's acceptances were `ACCEPTED-elsewhere` — accepted but not matched to the star they were
released from — against 5–10 % for the sensitivity gate. That is a signal, not a measurement; the pre-registered
statistic reads only the false-negative attribution block. ~5 m to resolve.

*(Design §16 assigned F104 to this verdict; the assignment holds.)*

---

### F105 — `golden_eval`'s first-rejection-wins attribution CONSERVES on real data, 5 of 5 cells, integer-exact — so waves 28's and 29's gate-count differences are safe to read

**Status:** Closed — the model is checked · found 2026-08-13, wave 30, `RULE W30-G` clause `G-1` = HOLDS ·
`/mnt/d/hf_w30/w30g_score.txt`

Relaxing exactly one gate `g` changes no other threshold, so the report's attribution must satisfy, **exactly,
in integers**:

> `ΔNO-CANDIDATE = 0`; `ΔFN_h = 0` for every `h` strictly upstream of `g`; and
> `R_g = Σ_{h downstream of g} ΔFN_h + ΔContaminated + ΔACCEPTED-elsewhere + ΔTP`

**Five single-knob cells, five exact balances, no tolerance**, with every upstream invariant and the pre-chain
`BloomSuppressed` stage returning zero:

| cell | gate (position of 11) | `R_g` | balance |
|---|---|---|---|
| `M1` | `TooDistorted` (4) | 12 090 | ✓ |
| `M2` | `LowSensitivity` (6) | 486 | ✓ |
| `M4` | `TooDistorted` (4) | 14 425 | ✓ |
| `L1` | `TooSmall` (1) | 9 537 | ✓ |
| `L3` | `LowSensitivity` (6) | 122 | ✓ |

Two of these (`L1`, `L3`) were pre-computed from wave 29's published artifacts before the pre-registration and
balanced then. **Three (`M1`, `M2`, `M4`) were blind by design §8 and balanced on first contact.**

**What it licenses.** The `G-MODEL-BROKEN` branch of the tree read: *"the report's first-rejection-wins
attribution does not conserve, and **every gate-count difference in waves 28 and 29 is unsafe.**"* It did not
fire. **First-rejection-wins is the correct model of the `FALSE-NEGATIVE ATTRIBUTION` block, and every
Δ-on-a-bucket waves 28 and 29 published can be read as a flow rather than as a coincidence.** This is the first
checked model of these reports in eleven waves, and it was granted by measurement, not by argument.

**It can fail, and the failure is exercised.** Self-test `[2]` moves one bucket by one on a live fixture and the
balance breaks; `[9]` drives a non-conserving cell to `G-MODEL-BROKEN` with a non-zero exit.

**A design difference the instrument reported rather than repaired.** The design's hand-typed §4.1(c) chain had
**7** gates; the arm's run-time extraction found **11** (`…, TooFlat, HFRAnalysisFailed, TooLowHFR,
Contaminated`) plus the pre-chain `BloomSuppressed`. The design's formula named contamination as a separate
terminal **and** the chain contains it as the last gate; counted twice it would have doubled. The scorer
partitioned once, printed the difference, and **the five exact balances are themselves the proof that the
11-gate partition is right** — the 7-gate one could not have balanced.

---

### F106 — A hand-carried debt ledger rots in BOTH directions, and the rot is measured: `P-D08` was published seven hours before the wave that listed it as owed

**Status:** Open — the class is structural · found 2026-08-13, wave 30, `RULE W30-D` = **`D-STALE`** ·
`/mnt/d/hf_w30/w30d_score.txt`

`RULE W30-D` parsed wave 29's §11 *"WHAT WAS NOT RUN"* table out of the committed document (sha256 re-asserted
at scoring time), resolved each of **14** rows to exactly one predicate with a declared **scope**, and searched
six roots plus the docs directory. `D-0` held (14 parsed, heading matched once, 0 unresolvable, 6 of 6 roots
present). `D-1` returned **False**: 6 of 10 debt-claiming rows have a satisfying artifact.

**Two rows are genuinely stale**, and one of them is why the rule exists:

- **`P-D08` at `n = 3`** — measured 09:06–09:08Z, published under a heading *"`# RESULT — P-D08 is REFUTED at
  n = 3`"*, committed `e42e2df` at 09:09:37Z, artifacts at `/mnt/d/hf_w26/pd08/`. Wave 29's ledger listed it
  **owed** at 18:05Z the same day. **Seven hours, inside a single day's run.** The cause: the search was for an
  artifact *under the wave's own root* rather than for the measurement wherever it lives.
- **the closing `V29`** — `vdrift_w29_CLOSING.txt`, 18:18:01Z, fourteen minutes **after** wave 29's roll call.
  The ledger was true when written and stale by the time it was read.

**This is [F85](#f85)'s class recurring** — a closed, shipped correction re-measured as an open defect — **and
the gap is now under twelve hours.** The remedy is not to fix a row. It is to stop inheriting the list: resolve
each row to a **checkable predicate with a declared scope**, where a **per-wave obligation** is discharged only
under the ledger wave's own root and a **measurement** is discharged wherever its artifact lives. Applying the
per-wave scope to a measurement row is the exact error that produced this stale debt, and the scope distinction
is what found it.

**Wave 30's own §10 ledger is written in that form** — one literal search command per row — and **withdraws
four rows** rather than carrying them a further wave.

*(Design §16 assigned F106 to the stale debt; the assignment holds. See F107 for the rule's own defect.)*

---

### F107 — A ledger checker whose row predicates are FILENAME globs converts a false "owed" into a false "already paid", which is the worse error: 3 of 6 on its first run

**Status:** Open — diagnosed, unrepaired by design · found 2026-08-13, wave 30, verifying `RULE W30-D`'s own
output · `/mnt/d/hf_w30/w30d_score.txt`, `/mnt/d/hf_w29/l/opt/L0/attempt01/optimize_summary.txt`,
`/mnt/d/hf_w25/s1_arm_w25.sh`, `/mnt/d/hf_w27/s27_*`

`RULE W30-D` named **six** already-paid rows. Checked against what each ledger row actually claimed:
**two genuine, one true-but-circular, three false.**

| row | matched | verdict on the match |
|---|---|---|
| **the 42 m `optimize` gate** | `hf_w29/l/opt/L{0..4}/…/optimize_summary.txt` | **FALSE.** Each is *"Runs root: `D01_ultrawide_40mm`; **Runs: 1**"* — a per-cell optimize from wave 29's own arm, not the 8-dataset `Q27-V1` gate. **And the row's own text reads "NOT OWED this wave"** — a row that disclaims the debt was scored as claiming one |
| a full paired 18-cell S1 arm | `hf_w25/s1_arm_w25.sh` | **FALSE, twice.** The match is a **shell script**, not a run record; and the row's own text says *"a **second** arm buys a second `SAME 13`"* — wave 25's arm is the row's premise, not its discharge |
| `S27-1` and wave 27's three mutants | `hf_w27/s27_*` (6 files) | **FALSE.** The glob matched wave 27's **`S27-4`** do-no-harm arm. The debt is `S27-1`'s **route agreement** (wave 27 §7.3: *"not adjudicated, by the scorer's own printed statement"*) and the **`M-S1`/`M-S2a`/`M-S2b`** records (§7.4: *"remain testimony"*). The one evidenced mutant, `M-S3`, was **never the debt**. One of the six matches, `s27_score.txt`, is a file the series has already annotated as unquotable |
| localising `D01` | `hf_w30/w30g_manifest.tsv` | **TRUE BUT CIRCULAR.** Written by **wave 30's own arm** at 19:52:10Z and scored "already paid" at 19:53:49Z — **99 seconds later** |

**Three defects, in increasing order of transferability.**

1. **The specs are hand-typed filename regexes** — `^.*optimize.*\.(txt|log)$`, `^s1_arm.*$`, `^s27.*$`. The
   rule replaced a hand-carried *list* with a hand-carried *regex*. **It rots the same way and it rots
   silently**, because a glob that matches too much produces a confident wrong answer where a glob that matches
   nothing produces a visible "not satisfied."

2. **`D-1` has no time ordering.** A row paid seven hours *before* the ledger, a row paid fourteen minutes
   *after* it, and a row paid *by the checking wave itself* all score identically. `paid_at <
   ledger_written_at` is the missing comparison, and **both timestamps are already in the rule's own
   manifest.** Without it, a wave that discharges an inherited debt will report itself as evidence that the
   ledger rotted.

3. **The validity clause cannot see any of this, by construction.** `D-0` asserts every row resolves to
   **exactly one** predicate — 14 of 14, unresolvable 0, correctly. **A validity clause that checks a row
   resolves *uniquely* cannot check that it resolves *correctly*.** This is the same shape as [F108](#f108):
   asserting a key is *present* is not asserting it is the *right* key.

**The direction of the error is what makes this high severity.** A false "owed" costs a re-run. **A false
"already paid" retires an obligation nobody met** — row 1 would have retired the 42 m gate, row 6 a control
open four waves that two wave-27 ship-clauses relied upon.

**What works and should be kept:** the **scope distinction**. Every *unsatisfied* result the rule produced is
correct — the wave-29 BEFORE fingerprint and the three scorer self-test captures were both scoped `ledger` and
both correctly reported not satisfied — and the scope is what found `P-D08`. **The rule is not repaired and not
re-scored**; `D-STALE` stands on its two genuine rows.

---

### F108 — A stale literal passed to `split()` silently widened a self-test's slice to the whole document, and no checker in this apparatus can see it: a stale ordinal used as a KEY is not printed output

**Status:** Closed in the instrument (fixed and the fix asserted) · Open as a class · found 2026-08-13, wave 30
· `/mnt/d/hf_w30/score_w30g.py:1176-1192`, `instrumentcheck/score_g_selftest.txt` (57) vs
`w30g_selftest.txt` (59)

`score_w30g.py`'s self-test clause `[6]` — the [F84](#f84) / `RULE S16` sensitivity fence, one of two fences the
design declared load-bearing — sliced the artifact with the needle **`"READING: more than a tenth"`**. **The
emitter stopped producing that text when the material bar became computed**: the `G-SINGLE-BINDS` reading line
is now formatted from `G2_BAR` (`:857`, *"READING: more than %.0f%% of the released mass reached acceptance…"*)
and the literal *"a tenth"* appears nowhere in the output.

**`str.split()` on an absent needle returns a ONE-ELEMENT list**, so `[-1]` handed back **the whole document**.
The assertion below it — *"the fenced name appears in no verdict sentence"* — stopped being about a verdict
sentence and became a document-wide absence check that passes trivially. **It would have passed forever while
asserting nothing.**

**What caught it, and what structurally could not:**

- the **blocking pre-flight** flagged the stale phrase **only where it was printed** — it found the emitter's
  copy, not the self-test's;
- **`V30-C` cannot see this copy at all.** `V30-C` scans **printing lines** for typed ordinals and counts; its
  population this wave was **667** (*"printing lines 980, of which 273 carry a format slot and are skipped"*).
  **A needle passed to `split()` is not printed output**, so it is outside `V30-C`'s population by construction.

> **A checker that looks for typed ordinals in output cannot find a stale ordinal used as a KEY.**

**The remedy is local and permanent:** assert the anchor is **present**, and assert the slice is a **proper**
substring so it can never widen to the whole text again. The anchor was shortened to the stable prefix
`"READING: more than "`. **Self-test `57 of 57` → `59 of 59`.**

**Class and generalisation.** This is [F102](#f102)'s class — *a clause whose population quietly went empty
while still printing a pass* — recurring **in the wave that built F102's remedy**, in a form that remedy does
not cover: F102 makes populations printable and refusable, which works for populations the instrument
**computes**, and **this slice was a population nothing ever counted.** The general rule: **`split()`,
`partition()`, `find()`, `re.search(...).group()` and every other locate-by-literal idiom degrades silently to a
no-op or to the identity when its literal goes stale, and a population counter on the *output* cannot observe
the *key*.** Every such call needs its key asserted present and its result asserted a proper part.

---

### F109 — Waves 27–30 ship as ONE PR at the owner's instruction, and the design's stacked-base prescription was overruled: only the PR base prevents accretion, but consolidation is the owner's call

**Status:** Closed — decided by the owner · 2026-08-13, wave 30 · `gh pr list`, `git merge-base`

**Measured at wave 30's pre-registration:** #196 (`…wave27`) and #197 (`…wave29`) both targeted **`develop`**,
so #197's own diff carried waves 27, 28 **and** 29 plus wave 27's C# ship — **25 files.** Wave 29 had opened a
fresh branch specifically so *"#196 does not carry a third section"* and succeeded at that; **it did not stop
the accretion, it moved it.** The design's diagnosis was that the mechanism is the PR **base**, not the branch.

**The diagnosis was acted on; the prescription was rejected.** The design pre-registered a fourth branch with
`--base ghilios/…wave29`, to make *"the first PR in this family whose diff is one wave."* **The owner instructed
one PR instead.** #197 was re-targeted to `…wave27` (the design's own §0 recommendation, which it had declined
to perform itself) and **merged**; **#199 was closed**; the branch was **rebased onto `develop @ ec06bec` and
force-pushed** with `--force-with-lease`. Waves 27–30 now sit on `ghilios/synthetic-af-bank-followups-wave27` =
**PR #196**, base `develop`, titled *"Waves 27-30: …"*.

**Design §0's goal of a one-wave PR diff was not achieved and will not be.** Recorded as an overrule, not as a
design success.

**One measured consequence for the gate check.** Design §9's reversal check is
`git diff --name-only <base> HEAD -- '*.cs'`. Against the **PR's** base it returns **7 C# files** from waves
27's and 28's ships. Against **wave 30's own** base (`0b86a2e`) it is **empty**, as is
`git ls-files --others --exclude-standard -- '*.cs'`. **Wave 30 changed no C# and owes no gate** — but `<base>`
was written assuming one wave per PR, and a later reader running the check literally against the PR base would
conclude otherwise. **From here on the check must name the wave's base explicitly.**

---

### AMENDMENTS TO EXISTING ENTRIES

**[F99](#f99) — AMEND IN PLACE.** Append:

> **AMENDED 2026-08-13, wave 30, `RULE W30-G` = `G-NOT-RECOVERABLE`
> (`/mnt/d/hf_w30/w30g_score.txt`).**
>
> **The mechanism holds and is now quantified on the gate this entry named.** `TooDistorted` at
> `MaxDistortion` 0.3 releases **12 090** high-tier candidates on `D01` and **`LowSensitivity` re-captures
> 9 676 of them — 80.0 %.** At 0.2: 14 425 released, 11 652 re-captured. **This entry's central observation —
> that a gate's entire yield can be absorbed one and two gates downstream, and that a `FN_total` difference
> cannot see it — is confirmed on a second knob and a second gate.** Its methodological half, *"wave 30's rule
> over sequential gates must read the per-gate flow, not the net,"* is **vindicated and superseded**: the flow
> is now a conservation identity that balances exactly and can fail ([F105](#f105)).
>
> **The substantive hypothesis is REFUTED at the pre-registered bar.** This entry claimed *"`D01`'s binding
> constraint is `TooDistorted`, **jointly with** `LowSensitivity`, and no single-knob change recovers it."*
> Measured on the 2×2: `ΔTP(joint) = +1 572` against `Σ ΔTP(singles) = +1 480` — an interaction of **+92**, a
> multiple of **1.06×** against the pre-registered bar of **2×**. **The pair does not bind jointly; the joint
> is essentially additive.** It *is* material (1 572 ≥ the computed bar of 502.5) — so the second half of the
> claim, *"no single-knob change recovers it,"* is also wrong in the direction that matters: `M1` alone
> delivers 1 096, itself above the material bar. **[F22](#f22) fence: none of these counts may be quoted as a
> recall or focus benefit; the frame is goal 3 only.**
>
> **Status → Closed.** The hypothesis was stated so it could be wrong, it was pre-registered as `PREDICTION A`
> against a rival `PREDICTION B` in wave 30's design §4.6, and the data chose. That is the entry working as
> intended.

**[F98](#f98) — APPEND.** Wave 30 is the **first measurement of the low end** of the `MaxDistortion` axis.
Measured at 0.3 and 0.2 on `D01`: no collapse, `R_g` = 12 090 and 14 425, pass-through 9.1 % and 8.3 %, marginal
pass-through on the second dose **4.1 %**. **Part 1 (the instrument finding) is untouched.** **Part 2 (bound the
axis at ~π/4 and rename) gains support at the top and gains a NEW precondition at the bottom**: 23–25 % of the
distortion gate's acceptances at the opened settings were `ACCEPTED-elsewhere` (accepted, not matched to the
star they were released from) against 5–10 % for the sensitivity gate. **A ship that bounds the axis at the top
is supported; a ship that lowers the default at the bottom is not yet supported and owes a ~5 m precision
measurement** (§10 row 7). The axis has 10 steps and wave 30 measured 3 of them.

**[F103](#f103) — APPEND.** **The forward prediction hit 4 of 4, second consecutive wave.** Predicted at wave
29 and measured at wave 30 (`vdrift_selftest_w30.txt` `[5b]`, 19:26:49Z): `/mnt/d/hf_w28` expired from **100
`-B` findings to 0**; `/mnt/d/hf_w29` became the live `-B` fixture at **311**; `/mnt/d/hf_w24` held as the
durable `-C` fixture at **2**, fourth wave running; `/mnt/d/hf_w26` and `/mnt/d/hf_w27` stayed silent at
**0**. **A pinned per-clause fixture goes silent exactly one wave later, on schedule.** The expired roots are
kept and asserted silent, so the expiry is demonstrated rather than described. **Wave 31's prediction, if there
is one:** `/mnt/d/hf_w29` expires to 0 and `/mnt/d/hf_w30` becomes the live `-B` fixture.

**[F94](#f94) — APPEND.** The `None` enumeration plus the **realized-tuple membership assertion** ran in **four**
instruments this wave (`W30-G` 36 regions, `W30-D` 6, `V30` 12, `W30-FP` 18), every one printing `uncovered: 0`
**and** its own realized tuple **and** `member of the enumerated set: True`. **It caught nothing this wave** —
every tuple was inside its domain — and that is the honest report. What it *did* demonstrate is that the
assertion can discriminate: `score_w30g.py`'s self-test `[13]` shows a tuple carrying `None` **is** a member and
a tuple outside the declared domains **is not**, so the assertion can fail. **A check that cannot fire in a
given wave is still worth its cost only if its firing end is exercised, and it was.**

**[F102](#f102) — APPEND.** Every clause in every wave-30 instrument printed `candidates: N   findings: M`.
**No clause printed an unexpected `N = 0` and no instrument refused with `POPULATION-EMPTY`** — the populations
were 5/7/8/3/4/4/2 (`G-0`'s seven), 5 (`G-1`), 36 660 (`G-2`), 2 (`G-3`), 14 and 10 (`W30-D`), 12/5 026/667
(`V30`), 1 230 (`W30-FP`). **But F102's class recurred anyway, in a place populations cannot reach** — see
[F108](#f108). **F102's remedy covers populations the instrument computes; it does not cover a literal used as
a key, which nothing counts.** The remedy is not weakened, it is bounded, and the bound should be written into
it.

**[F95](#f95) — APPEND.** Wave 30 ran the pre-flight **over a populated root** (7 files, 12 `-A` candidates,
5 026 `-B`, 667 `-C`) **and at the close**, both from the pre-registration rather than from a debt, and the two
runs are **byte-identical**. **Both halves of the remedy, from the start, for the first time.** One deviation
against F95's neighbour requirement is recorded in §7.4: the `W30-FP` BEFORE sweep ran **26 minutes after**
`V30`'s five fixture probes read `/mnt/d/hf_w29`, against design §6's bolded ordering. Consequence measured:
none (`PRESERVED`, 0 moved). **The ordering must be enforced by the driver, not by intent.**

**[F21](#f21) — APPEND.** Estimate vs actual, wave 30, sixth consecutive wave under on the analysis halves:

| step | est | actual |
|---|---|---|
| 1 — write all 7 instruments | 60 m | **~39 m** (18:54:08 → 19:33:44Z) |
| 2 — `W30-FP` BEFORE | 5 m | **≤ 60 s** |
| 3 — `V30` pre-flight + 5 fixture probes | 15 m | **~7 m** |
| 4 — the 5-cell arm | 25 m wall / ~15 m compute | **17 m 37 s wall / 1 057 s compute** |
| 5 — `W30-G` scoring | 12 m | **~15 s** to emit |
| 6 — `W30-D` | 15 m | **~2 m** |
| 7 — the `K` column | 12 m | **NOT RUN** (§10 withdraws it) |
| 8 — AFTER + closing `V30` | 8 m | **~2 m** |
| **Steps 1–8 excluding 7** | **~140 m** | **~62 m — 56 % under** |

**Two figures were priced from a measured rate and one of them was mildly optimistic.** The arm was priced at
~950 s from wave 29's 145–198 s per cell and came in at **1 057 s, 11 % over** — because wave 29's two fastest
cells were its two *dead* ones, so the reference rate was biased low. **A rate measured over an arm containing
dead cells under-prices an arm with none.** The sweep price (3 m/side, from 61 MB/s over a trimmed 2.4 GB root
set) was **generous by ~3×**: both sides completed in ≲ 2 minutes. **New constant handed forward:** 1 230 files
across 6 declared roots sweep in **under a minute** on DrvFs. **And it could only be bounded, not measured** —
see §8.3: the fingerprint records no start time.

**`S27-1` and wave 27's three mutants — WITHDRAWN**, formally, after four waves on the cut list, per design §16
and wave 29 §13. **And note that [F107](#f107) shows `RULE W30-D` falsely reported this row already paid** — it
matched wave 27's `S27-4` arm. The withdrawal is on the merits, not on that match.

**`docs/synthetic-af-bank-results-table.md`'s `K` column — WITHDRAWN**, per design §13's own rule for this item.
Owed by waves 28, 29 and 30; unpaid by all three. `kcol_w30.py` remains on disk with a `21 of 21` self-test and
can be run in ~2 m by any later ship.

---

### F96 — The harness's `stepBehavioral` "truth" is a fixed point of the recommender it scores, so every goal-2 conclusion is denominated in a bar the code under test produces

**Status:** Open (diagnosed to a line; no fix priced) · found 2026-08-13, wave 29, `RULE W29-S` clause `S-a`
= HOLDS · `/mnt/d/hf_w29/w29s_score.txt`

`SynthValidateRunner.ComputeStepBehavioral` (`:1213–1270`) builds an analytic curve, fits it, calls
`StepSizeRecommender.Recommend` at **`:1262`**, and loops until `rec.StepSize == step`. `RULE W29-S` asserts
from source, at `HEAD`, by sha256, that the method is **declared once**, contains **exactly one** `Recommend(`
call site inside its body (whole-file count 2, at `:792` and `:1262`, printed so the scoping is visibly doing
work), and is the **only writer** over 70 harness files of the value serialized as `terminal.stepBehavioral`.

**Consequence.** A change to `Recommend` moves the bar **and** the measurement together. This is the mechanism
behind [F82](#f82)'s observation that `stepBehavioral` moved 8.0 → 9.0 across wave 25's two arms while it was
identical on the other 17 cells: wave 25 changed `Recommend`. **Assertion `A3`, and every goal-2 statement in
waves 20–29 that quotes `stepBehavioral` as truth, is a self-consistency check and not an accuracy measurement.**

**What it does NOT say.** It does not say the recommender is wrong; a fixed point can be correct. It says the
harness cannot tell. **The remedy is a spec-derived truth model** — backlog item 4's `A4` gap, priced at 20 m
of code **+ 42 m of gate** because it rebuilds `TestApp`.

**Note on scope.** Design §14 pre-registered F96 as a two-part entry — the truth-model coupling **and** "F82's
fix has no product carrier" — to be **withdrawn in place** on `S-CARRIER-EXISTS`. The verdict was
`S-CARRIER-EXISTS`, so **the carrier half is withdrawn and is not registered.** The coupling half is `S-a`,
which held, and stands.

---

### F97 — F82's shrink-while-widening transition occurs on exactly ONE cell in the bank, and on ZERO of the fifteen that were blind

**Status:** Open (measured; F82's fix choice unchanged) · found 2026-08-13, wave 29, `RULE W29-R` =
**`R-STANDS`** · `/mnt/d/hf_w29/w29r_score.txt`

Wave 26 pre-registered a reversal condition on its choice between F82's two fixes — *"if a later wave measures
that `SearchSpan` … shrinks on more than a single cell while the requested sweep widens, then … (2) becomes
correct."* **Three waves quoted it; none ran it.** Wave 29 ran it at zero compute over the 18 addressable
paired S1 cells under `/mnt/d/hf_w25/after`, via the exact inversion
`impliedSearchSpan = stepRecommendation.halfWidth / (1.5 × 0.5)`, with the multiple asserted `= 1.5` at **both**
the B15 tree `29665a62e4f8` and `HEAD`, and with all 37 rounds confirmed capped-or-floored and none
detect-bounded before any statistic was taken.

```
k = 18   c = 1   c_blind = 0   qualifying cells: ['D01_ultrawide_40mm']
```

**`c = 1`, and the one cell is `D01`, which was already burned.** `D05_tec140_1000mm` and
`D19_cygnus_deep_shed` produced no report and **left the denominator** as named `CNL-NOREPORT`, never as zeros.

**What this changes.** F82 is real and (per [F98's sibling finding in `RULE W29-S`](#f96)) reaches a real
product path — but it is **rare, not general**: one cell in eighteen, zero in fifteen blind. On all 17 other
cells the implied and requested spans move together on every transition. **Anyone pricing a fix should price it
against 1 of 18, not against the 37-of-37 capped-or-floored population.** Wave 26's choice of fix (1) stands on
wave 26's own grounds; the reversal condition is now **evaluated** rather than merely unexamined.

**Blindness spent.** Producing this verdict required publishing the per-cell table for all 18 cells, so the 15
formerly-blind cells' `halfWidth`/`bootstrap`/implied-span values are now read. See F100.

---

### F98 — `MaxDistortion` is a MINIMUM fill-ratio despite its name, and 0.9 is above the ~0.79 ceiling of a perfect disk, so a whole arm cell was dead before it ran

**Status:** Open — **candidate goal-3 product finding** · found 2026-08-13, wave 29, `RULE W29-L` =
**`L-UNEVALUATED`** · `/mnt/d/hf_w29/w29l_score.txt`, `/mnt/d/hf_w29/l/out/L{0,2,4}.log`

`StarDetector.cs:1804` rejects a candidate as `TooDistorted` when `fillRatio < effectiveMaxDistortion`. The
parameter is a **lower bound on bounding-box fill ratio**; **raising it tightens the gate.** The file's own
comment at `:1786` states the ceiling: *"a perfect disk fills ~PI/4 ≈ 0.79."* With `DefocusAwareGates: false`
in `D01`'s landed tree, `ComputeEffectiveMaxDistortion` returns `p.MaxDistortion` verbatim.

Wave 29's design §6 pre-registered `MaxDistortion` **0.5 → 0.9** under the heading *"one knob relaxed per
cell"*. Measured result on `D01`, 9 frames: **`TP=0 FP=0 FN=110384`, `recall@high = 0.000 (0/62 411)`** on
both `L2` and `L4` — total detection collapse. Corroborated by wall clock: `L2` (152 s) and `L4` (145 s) were
the two **fastest** cells in a five-cell arm, because nothing survived the gate to do downstream work.

**Two findings, and they are separable.**

1. **The instrument finding.** A pre-registration can specify a knob edit whose *direction* no clause reads.
   The arm's self-test proved the edit was **surgical** (`33 of 33`, *"exactly 1 file changed"*, *"an unnamed
   field is UNCHANGED"*) — surgical is not correctly signed. `RULE W29-L`'s verdict is
   **`L-UNEVALUATED`** and it stands; the cell was not re-run at a corrected value and scored.
2. **The product finding.** `OptimizerVariable.cs:176` declares the searchable axis
   `Continuous(nameof(StarDetectorParams.MaxDistortion), 0.1, 1.0, 0.1)`. **The upper ~21 % of that axis
   (> π/4 ≈ 0.785) is provably detection-killing for round stars** — any value above the perfect-disk fill
   ratio rejects every candidate. A searchable range whose top fifth returns zero detections is a parameter
   space that wastes optimizer evaluations, and the name `MaxDistortion` reads as the opposite of what it
   gates. **Worth ~20 m to bound the axis at π/4 and rename or document the field; owes the 42 m gate because
   it touches plugin code.**

---

### F99 — Relaxing the bounding-box floor on `D01` releases 9 537 candidates and three survive: the binding gates are downstream, and the pre-registered statistic could not see it

**Status:** Open — the hypothesis wave 30 should pre-register · found 2026-08-13, wave 29 ·
`/mnt/d/hf_w29/l/out/L{0,1}/attempt01/golden_eval.txt`

> #### AMENDED, wave 30 — the mechanism HOLDS, the joint-recovery claim is REFUTED at a pre-registered bar
>
> `RULE W30-G` = **`G-NOT-RECOVERABLE`**, on a 2x2 factorial with `MaxDistortion` opened in the **correct**
> direction (lowered; wave 29 raised it and killed two cells before they ran, F98).
>
> **What holds:** the two gates release real mass — 36 660 high-tier candidates across the cells — and the
> conservation identity balances **integer-exact on 5 of 5** real cells (F105), so the flow account this entry
> rests on is sound.
>
> **What is refuted:** the *joint recovery* claim. `dTP(joint) = +1572` against a summed-singles `+1480` and a
> pre-registered synergy bar of `2 x singles = 2960`. The joint clears the **material** bar (502.5, computed as
> `0.01 x FN_total`) and is **essentially ADDITIVE, not synergistic**, while `G-2` shows **re-capture dominates
> at 92 %** — what one gate releases, the gates downstream take.
>
> **The outcome was called either way.** Wave 30 registered TWO differing predictions before the data — this
> entry's (`G-JOINTLY-BOUND`) and the pre-registration's own (`G-NOT-RECOVERABLE`) — so neither result let the
> wave claim a hit it had not called. That is the strongest form of evidence this series has produced.
>
> **[F22](#f22) fences the reading:** on this rig HFR over-reads badly below ~1.1 px and autofocus already
> lands, so **none of this may be quoted as a focus improvement**. The deliverable is which gate binds, a
> goal-3 question.

`RULE W29-L`'s pre-registered statistic is the difference in `FN_total`. On `D01`, lowering
`MinStarBoundingBoxSize` 6 → 3 gives `FN_total` 50 249 → 50 246, i.e. **−3**, which reads as "this gate does
almost nothing". The per-gate blocks in the same artifact say otherwise:

| gate | `L0` | `L1` (MinBox 6→3) | Δ |
|---|---|---|---|
| `REJECTED:TooSmall` | 11 372 | 1 835 | **−9 537** |
| `REJECTED:TooDistorted` | 14 876 | 20 573 | **+5 697** |
| `REJECTED:LowSensitivity` | 13 612 | 17 426 | **+3 814** |
| others | | | +23 |

**9 537 high-tier candidates were released from the bounding-box floor and 99.97 % of them were immediately
re-caught by `TooDistorted` and `LowSensitivity`. Three reached acceptance.** The gate is not inert; its entire
yield is absorbed one and two gates downstream.

**The lesson is about the statistic, not the detector.** A first-rejection-wins attribution differenced only at
the *total* cannot distinguish a gate that never fires from a gate whose output is fully captured downstream.
**Wave 30's rule over sequential gates must read the per-gate flow, not the net.** And the substantive
hypothesis this generates, stated so it can be wrong: **`D01`'s binding constraint is `TooDistorted`, jointly
with `LowSensitivity`, and no single-knob change recovers it** — which is `L-JOINTLY-BOUND`'s claim arrived at
by a different route than the rule that could not evaluate it.

---

### F100 — A blindness ledger that promises a quantity will stay unread, beside an instrument that must read it, burns the population at pre-registration time

**Status:** Recorded (the burn is spent; the structural fix is cheap) · found 2026-08-13, wave 29 ·
design §7 vs `/mnt/d/hf_w29/w29r_score.txt`

Wave 29's design §7 wrote: *"the requested-vs-implied comparison has **never** been computed on any other
cell"*, and named the 15 non-burned cells as **BLIND, and carrying `W29-R`'s verdict**. The same document's
§5.3 pre-registered a scorer that computes exactly that comparison over all 18 addressable cells and prints
`c` and `c_blind`. **`w29r_score.txt` publishes the full 18-row table.** The ledger's claim is now false, and
the instrument that falsified it was commissioned by the document that made the claim.

**The rule was not tuned** — `W29-R` is implemented exactly as §5.3 writes it, and its verdict was taken on the
first computation of the statistic, which is what blindness protects. **But it is the last verdict on this
population that can claim blindness.** Any future rule over `SearchSpan` persistence on `/mnt/d/hf_w25/after`
has no blind cells left.

**Fix, ~5 m per wave:** the design's blindness ledger must be checked against the design's own instrument
specifications before the pre-registration commit — every quantity the ledger promises stays unread must not
appear in any clause's per-member output. Nothing does this today.

---

### F101 — The derivation checker's `historical_ranges` has now failed THREE generations, each time with a passing self-test

**Status:** Repaired in wave 29's checker; the pattern is the entry · found 2026-08-13, wave 29 ·
`/mnt/d/hf_w28/verify_derivation_w28.py:579`, `/mnt/d/hf_w29/verify_derivation_w29.py:224–238`

[F87](followups.md) recorded this function eating its own file; wave 27 repaired it; wave 28's design required
the repair be carried forward and demonstrated, and wave 28's self-test clause `[6]` did demonstrate both ends
it knew about. **It was still broken.** Wave 28 counted brackets on the **raw** line, so an unbalanced `[` or
`{` inside a **string literal** opened a phantom block that ran to EOF. Measured on wave 28's own 599-line
file:

```
ranges computed by WAVE 28's own function: [(93, 120), (557, 565), (579, 598)]
  range 579 -> 598   opener: defonly = ['HISTORICAL_BLOCKS = ("PRIOR_BUILD_IDS", "ALLOW = [")',  ...
   *** RUNS TO EOF ***
```

**29 of 599 lines of wave 28's own checker, including its tail, were exempt from `V28-B` and `V28-C` for the
whole of wave 28.** It concealed nothing — every token in the exempt span is wave 28's own wave id — and that
is luck, not design. Wave 29 blanks string literals length-preservingly before counting and asserts all four
ends in clause `[6]`, including *"a token INSIDE the declared historical literal is NOT reported"*.

**The durable finding is the pattern:** three generations, three passing self-tests, because each generation's
self-test tested the ends the previous generation broke. **A self-test that only covers the last bug found is a
regression test, not a specification.**

---

### F102 — `V28-C` returned zero because its population was almost empty: 124 of 126 printing lines in wave 28's scorers were never scanned

**Status:** Repaired in wave 29's checker; the vacuity class is the entry · found 2026-08-13, wave 29 ·
`/mnt/d/hf_w28/verify_derivation_w28.py:170`, `/mnt/d/hf_w29/verify_derivation_w29.py:160`

`V*-C` forbids typed ordinals and counts in an instrument's printed output. Wave 28's line-selector was
`\b(print|log|echo|printf|Prog|tee)\b`; wave 28's three scorers emit through `out.write(`, which matches none
of it. Measured over `score_w28b.py` (36), `score_w28n.py` (52), `score_w28s.py` (38):

```
out.write( lines total : 126
matched by w28 PRINTS  : 2      (incidentally -- the string being WRITTEN contained a keyword)
matched by w29 PRINTS  : 126
```

**Wave 28's `V28-C: 0` was vacuous.** Wave 29 widened the selector to
`\b(?:\w*[._])?(?:printf|print|echo|log|write|emit|say|tee|Prog)\b` and **demonstrated** the widening rather
than asserting it — self-test `[3]` carries *"V29-C via `out.write` fired on its own mutant"* and *"V29-C via
an underscored helper fired on its own mutant"*.

**The class, which is the same as F101's and should be read with it:** a clause can pass for three waves
because it has no candidates, and **no verdict tree in this series distinguishes "zero findings" from "zero
candidates".** [F94](followups.md) made trees total over their *outcomes*; nothing makes a clause report its
*population*. **~10 m to make every clause print `candidates: N   findings: M` and refuse on `N = 0` where a
population is expected.**

---

### F103 — A pinned per-clause fixture goes silent exactly one wave later, as predicted in an instrument's own header and confirmed by measurement

**Status:** Closed as a mechanism; standing obligation to re-measure each wave · found 2026-08-13, wave 29 ·
`/mnt/d/hf_w29/vdrift_selftest_w29.txt` `[5b]`, `vdrift_probe_hf_w2{4,6,7,8}.txt`

Wave 28 pinned `/mnt/d/hf_w27` as its live `V28-B` fixture and wrote into its own instrument's header that such
a pin expires one wave later, because `PREV` advances and last wave's tokens stop being "previous-wave". Wave
29 measured all four candidate roots before pinning anything:

| root | `-A` resolving | `-B` findings | `-C` findings | role |
|---|---|---|---|---|
| `hf_w24` | 1 of 1 | 0 | **2** | durable `-C` fixture, third wave running |
| `hf_w26` | 2 of 6 | **0** | 1 | expired at wave 28; kept and **asserted silent** |
| `hf_w27` | 1 of 5 | **0** | 1 | **EXPIRED as predicted** — wave 28's live `-B` |
| `hf_w28` | 0 of 5 | **100** | 1 | the new live `-B` |

**This is the strongest evidence class this series has produced**, and the reason is structural: it is a
**falsifiable forward claim about the instruments, written before the measurement, with a stated mechanism, and
checked one wave later.** Everything else in the series is a rule scored against artifacts that already
existed. **`hf_w28` is next to expire; wave 30 must re-measure and must treat a clause with no live fixture as
a FAIL, not a pass.**

---

### AMENDMENTS TO EXISTING ENTRIES

**[F82](followups.md) — AMEND IN PLACE. Its fix choice stands; its scope, its generality and its truth
model are now measured.** Append:

> **Amended 2026-08-13, wave 29.** Three things about this entry were unmeasured when it was written.
>
> 1. **The "truth of 9" is a PRODUCT-DERIVED FIXED POINT, not a spec-derived truth.** `terminal.stepBehavioral`
>    is `StepSizeRecommender.Recommend` iterated to a fixed point by
>    `SynthValidateRunner.ComputeStepBehavioral:1262` — see **F96**. `D01`'s stall is measured against a bar the
>    recommender itself produces, and wave 25 moved both together.
> 2. **"Confined to the recommender's caller state" names a caller, and it is
>    `StarDetectionOptimizerWizardVM.CaptureNewSweepAsync` (`:5012`).** `RULE W29-S` clause `S-b` returned
>    **FALSE**: of `BuildSummaryAsync`'s five callers, `CaptureNewSweepAsync` takes a **fresh sweep**
>    (`RunLiveAttemptAsync`, `:5071`), calls `BuildSummaryAsync` at `:5098`, and carries the previous round's
>    recommended geometry forward through the instance field `recaptureStepSize` (`:2608`, assigned `:5037`,
>    consumed `:3236`). **F82 is a real product defect on a real product path**, and the wave-29
>    pre-registration's claim that it "reaches no user" and "scores goal 2 zero" is **withdrawn**.
> 3. **Its generality is one cell in eighteen.** `RULE W29-R` = `R-STANDS`: `k = 18`, `c = 1`, `c_blind = 0`.
>    The only qualifying cell is `D01` itself. See **F97**. Wave 26's choice of fix (1) stands, on wave 26's
>    own grounds, with the escape clause now **evaluated** rather than unexamined.
> 4. **A third candidate fix is registered and deliberately NOT chosen** (wave 29 design §4.4): bound
>    `maxHalfWidth` below by the span the sweep actually **requested**, guarded by
>    `BandDemonstrablyUnsampled(sampledHfrRange)`. `RULE W29-S` clause `S-c` **HOLDS** — the requested positions
>    are in `SweepDetectability.FrameFocuserPositions`, reachable inside `Recommend` via
>    `MeasureMaxUsefulHalfSpan`. It needs **no cross-round state and no `A3` re-derivation**, which makes it the
>    cheapest of the three (~45 m + a 3-cell re-run + the 42 m gate) and is precisely why the wave that thought
>    of it did not select it. **The next wave pre-registers the choice among three.**

**[F81](followups.md)** — append: the floor F81 shipped **is** reached in the product on a re-swept round, not
only at round 0 of a session. `CaptureNewSweepAsync` (`:5012`) re-captures and carries `recaptureStepSize`
forward, so the wizard's `Capture new sweep` path has genuine cross-round sweep state. The `Continue` and
`Re-optimize` paths do not — they call `LoadRunStampedAsync` and re-optimize already-captured frames — so on
those two the floor is inert after round 0, and that half of the original claim stands.

**[F94](followups.md)** — append: wave 29's design §10 is the first in the series to publish its own outcome
space as a table, and **every scorer re-derived it in code and agreed with the published counts**: `W29-S`
**8 / 0 uncovered**, `W29-R` **4 / 0**, `W29-L` **8 / 0** — matching design §10's three tables exactly — and
`V29`, which §10 declared as inheriting wave 28's tree without a count, re-derived **12 / 0** including the
`A=None` empty-population region. The two logically-unreachable regions (`R-INCOHERENT`, `L-INCOHERENT`) are
present, asserted empty, and checked against the measured numbers (`c = 1 ≤ k = 18 is True`; `L-1 ∧ L-2 =
False`). **The remedy works for outcome coverage.** It does **not** cover two adjacent failures found this wave: a clause whose *population* is
empty (**F102**) and a clause whose realized value is **neither `True` nor `False`** — `W29-L` ended at
`L-0 = True, L-1 = None, L-2 = None`, a state outside the enumerated boolean cube, yet the scorer still printed
`uncovered: 0` and appended a canned `READING:` line saying *"the control did not hold"* **when `L-0` in fact
HELD**. The tree was total over booleans and the run was not a boolean. **Trees must enumerate `None` as a
clause value, and a verdict's explanatory text must be derived from the clause values rather than templated per
verdict name.**

**[F95](followups.md)** — append: **the remedy's first half works and was proved this wave.** Wave 29's plan
ordered instruments (Step 1) before the blocking pre-flight (Step 2); the last instrument was written at
`17:01:28Z`, the pre-flight ran at `17:35:55Z` over a **populated root of 7 files**, and returned `V-CLEAN`
with `SELFTEST V29 27 of 27`. Wave 28's equivalent certified **one file**. **The second half did not run** —
the closing `V29` (`vdrift_w29_FINAL.txt`) has no artifact, against design §11's *"NOT cut, at any budget."*
Both halves are needed and the series has now demonstrated one of them.

**[F21](followups.md)** — append wave 29's per-step estimate vs actual (§10). Steps 1, 2, 4, 5 and 6 all landed
**under**; the only two that overran were the two priced from instinct rather than from a measured rate
(fingerprint sweeps, §10). Measured constant for future waves: **the six declared read-only roots are ~41.8 GB
/ 2 452 files and hash at ~61 MB/s, i.e. ~12 m per sweep, ~24 m for a two-sided fingerprint.**

**`docs/synthetic-af-bank-results-table.md`** — still owed from wave 28: the `K` column from `truthModel`
(`max(hfrMin, hfrMinEffective)`), the note that `D01`/`D02`/`D03` are the only three floored by the generator,
and **no 2–4 px band claim below the band** (`W28-B` = `B-SPLIT`). **Not paid this wave.** ~10 m.

---

### F92 — The post-wavelet blur is WELDED to `StructureLayers`, so one knob sets two opposing scale cutoffs

**Status:** **Open — source-derived, zero compute, and it re-opens [F43](#f43)** (2026-08-13, wave 28,
`RULE W28-S` = `S-WELDED`, 3 of 3)

`StarDetector.cs:631-632` blurs the structure map immediately after the à trous residual is subtracted:

```csharp
// Step 5: Excluding large structures can cut off the outsides of large stars, or leave holes when far out
//         of focus. Blurring smooths this out well for structure detection
CvImageUtility.ConvolveGaussian(structureMap, structureMap, p.StructureLayers * 2 + 1);
```

with `sigma = 0.159758 * kernelSize` when unset (`CvImageUtility.cs:80-82`). At the shipped
`StructureLayers = 4` the kernel is 9×9 and **σ ≈ 1.44 px**, so a 1-px source loses roughly `1/(2πσ²) ≈ ×0.077`
of its peak amplitude **before** it is compared against
`binarizeThreshold = median + NoiseClippingMultiplier · sigma` (`:653`).

**The blur's stated purpose is to help LARGE and defocused stars, and its entire cost falls on small ones.**
Its width is keyed to `StructureLayers`, which is the knob for the *upper* scale cutoff — the shipped tooltip
says so: *"At the default of 4 (2⁴=16), structures larger than 16 pixels in size are excluded"*
(`OptionsDataTemplates.xaml:444`). **Lowering `StructureLayers` therefore narrows the blur (helps a 1-px star)
and makes the residual less smoothed (hurts it), in one move.**

**Measured as a rule, both hashes recorded** (`/mnt/d/hf_w28/w28s_score.txt`, `StarDetector.cs`
`1e202777…`, `IStarDetector.cs` `5304ed5f…`): the width argument is literally `p.StructureLayers * 2 + 1`
(raw, **not** `EffectiveStructureLayers`); **no** member of `StarDetectorParams` — 55 properties — controls the
blur width independently; and `EffectiveStructureLayers(p)` reaches the residual at `:619`/`:622` and appears
**0 times** in the blur's argument list. The defocus/donut boost (`:1567-1578`) already decouples them in one
direction, so **there is precedent for decoupling the other.**

**Consequence for the register:** [F43](#f43)'s *"`StructureLayers` 4 → 2 makes it strictly worse"* moved both
cutoffs at once and is **confounded in source**. The experiment that separates them has never been run.

**Price to run it:** ~45 m code (a dedicated blur-width field defaulting to today's expression, so the default
is bit-identical) + ~10 m arm; **plus a fresh 42 m `optimize` baseline and a re-derivation if it is ever to
ship**, because the change reaches the detector. Design §11 defers it; `RULE W28-N` (see [F93](#f93-ready-to-paste))
is the evidence that the amplitude account it rests on is correct.

### F93 — `NoiseClippingMultiplier` is the binding gate below the calibrated band on the bank too — with one dataset where it is not enough

**Status:** **Open — measured, and explicitly NOT a licence to change a default** (2026-08-13, wave 28,
`RULE W28-N` = `N-RECOVERS`, 3 of 3) · corroborates [F43](#f43) on synthetic frames · bounded by [F22](#f22)

[F43](#f43) found that on a reporter's **real** 61 MP 19.4″/px rig the binding gate was `NoiseClippingMultiplier`,
not `Sensitivity`. Wave 28 ran it on the bank: 3 floored datasets × `NC ∈ {landed, 2.0, 1.0}`, no build, one
edited field per cell, 432 s.

| dataset | `NC` landed | high-tier `NO CANDIDATE` landed → `NC 1.0` | `recall@high` landed → `NC 1.0` | `recall@all` | precision at 1.0 |
|---|---|---|---|---|---|
| `D01_ultrawide_40mm` | 3.8125 | 42 991 → **8 931** (−79.2 %) | 0.183 → **0.195** | 0.123 → 0.183 | 1.000 |
| `D02_rich_135mm` | 3.9375 | 3 732 → **1 071** (−71.3 %) | 0.446 → **0.687** | 0.377 → 0.598 | 1.000 |
| `D03_redcat_250mm` | 3.625 | 197 → **7** (−96.4 %) | 0.365 → **0.549** | 0.238 → 0.364 | 1.000 |

**The mechanism is confirmed and the payoff is not uniform.** `D02` and `D03` recover a quarter and a fifth of
`recall@high`. **`D01` does not:** 34 060 high-tier candidates now form, **97.8 % of them are re-rejected by a
later gate** (`TooDistorted` +13 866, `LowSensitivity` +12 826, `TooSmall` +6 515), and `recall@high` moves
+0.012. Its attribution profile inverts — `NO CANDIDATE` 84.3 % → 17.8 %, gates 12.1 % → 79.3 % — so **at
`NC = 1.0` `D01` looks like `D02`/`D03` did at their landed settings.** The gates fire in sequence
(`TooSmall` → `OnBorder` → `TooDistorted` → `Degenerate` → `LowSensitivity`), so those counts are
first-rejection-wins and **which gate binds on `D01` is NOT established.**

**Three things this entry does not say.** (1) It is **not a ship**: no wave-28 rule pre-registered a change on
this axis, and the optimizer *chose* the landed values under an objective that charges nothing for a missed
detection ([F83](#f83)). A default or search-bound change owes its own wave with a fresh baseline. (2) The
`precision = 1.000` is **1.000 at baseline too**, so `N-3` shows no detected cost rather than a measured cost of
zero. (3) [F22](#f22) still binds: below ~1.1 px measured HFR over-reads by up to +234 %, so **stars recovered
below the band carry an HFR that cannot resolve the vertex** — and `D01` already reaches `BestJ` 0.994825.

**Artifacts:** `/mnt/d/hf_w28/nc/<dataset>_nc<v>/attempt01/golden_eval.txt`, `w28n_score.txt`,
`w28n_manifest.tsv`. The three landed cells reproduce the published `table18` rows exactly, so every delta is
attributable to the one edited field.

### F94 — Verdict trees keep shipping with uncovered regions, and the standing rule against it did not stop the second one

**Status:** **Open — a class, now on its THIRD consecutive wave, and wave 29 found the reason the fix does not
work** (2026-08-13, waves 27–29) · generalises wave 27 §8.1 · belongs beside [F68](#f68) part 5

> #### APPEND, wave 29 — the coverage PROOF is the thing that is wrong, not just the tree
>
> Wave 29 answered F94 by **enumerating every tree and printing `uncovered: 0`** on all five instruments. It
> was not enough, and the failure is instructive.
>
> `RULE W29-L` printed `regions enumerated: 8   uncovered: 0` and then ran to the clause state
> **`(L-0=True, L-1=None, L-2=None)`** — a triple its own enumeration **never visits**, because the
> enumeration ranges over `{True, False}` and the clauses are three-valued: `L-1` and `L-2` are `None` when
> `joint` is not positive, which is a could-not-look and not a `False`.
>
> **A tree proved total over `{True, False}` is not total over `{True, False, None}`**, and every rule in this
> series that has a could-not-look state has three-valued clauses. So the remedy waves 28 and 29 adopted —
> enumerate and assert `uncovered: 0` — **proves the wrong totality** and will keep printing a clean coverage
> line beside an uncovered state.
>
> **The corrected remedy: enumerate over the clause's ACTUAL value domain, `None` included, and assert that
> the verdict function is total over THAT product.** A cheap check that would have caught it: assert the run's
> own observed clause tuple is a member of the enumerated set, which costs one line and cannot be satisfied by
> a proof of the wrong thing.
>
> Wave 29's `w29l_score.txt` also shipped a **false explanatory sentence** on the same run — *"the control did
> not hold"* when `L-0` **held** (8 fields, 0 disagreeing). The verdict `L-UNEVALUATED` is correct on the
> numbers; only the prose was wrong. It is annotated in place on the artifact rather than edited out.

Wave 27's `RULE T27` tree left `3 ≤ union ≤ 9` with neither set at 5 uncovered, and the scorer printed
`T-TREE-GAP` rather than papering over it. Wave 28's design §12 turned that into a standing rule — *"every
verdict tree in this wave is checked for total coverage of its outcome space at pre-registration"* — and then
**`RULE W28-N`'s tree shipped with 4 of 135 regions uncovered, every one of them a region where `N-1` is
false.** `N-1` is the clause that checks the edit took effect; a run where it silently did not would have scored
`N-RECOVERS` on the tree as written.

**The pattern is specific and predictable: the uncovered region is always the VALIDITY clause.** `N-2` and `N-3`
are substantive and appear in every branch; `N-1` is the gate that says the measurement happened at all, it is
expected to hold, and it fell out of the tree. `W28-S` (27 regions) and `W28-B` (432 regions) both enumerate
clean — and neither has a validity clause outside its own conjunction.

**Remedy, and it is mechanical rather than a reminder:** a design must **enumerate its own outcome space at
pre-registration** — the scorers already do this in code, and printing `regions enumerated: N   uncovered: 0`
costs nothing — and **every clause, including the ones expected to hold, must appear in the tree.** A scorer
that finds a gap prints `<RULE>-TREE-GAP`, enumerates the uncovered regions, prints what the design's literal
tree would have said, and **does not repair and re-score** (`/mnt/d/hf_w28/w28n_score.txt`, self-test [5]).

### F95 — A blocking pre-flight that runs before the instruments exist certifies an empty population

> **NARROWED the same day, and the narrowing is the useful part.** The pre-flight was **re-run over the
> populated root** and returns **`V-CLEAN`: 7 files checked, `V28-A` 8 of 8 siblings resolving, `V28-B` 0,
> `V28-C` 0** (`/mnt/d/hf_w28/vdrift_w28_FINAL.txt`). **The instruments were clean all along**, so this entry
> is not "a wave shipped unchecked instruments" — it is a **SEQUENCING** defect, and that is the durable
> lesson:
>
> **A blocking pre-flight must run BEFORE the measurement and AFTER the instruments exist.** Every plan in this
> series has said "before anything", which is a strictly larger window and admits an empty population. The
> checker behaved correctly throughout — it refused to call an empty population 1.0000 and said so in those
> words — so what failed was the *plan's* ordering, not the instrument. Both artifacts are kept: `vdrift_w28.txt`
> (the empty-population run) and `vdrift_w28_FINAL.txt` (the real one).


**Status:** **Open — remedy known, not applied this wave** (2026-08-13, wave 28) · [F80](#f80)/[F87](#f87)'s
family, third distinct shape

`verify_derivation_w28.py` ran at `11:06:36` over `/mnt/d/hf_w28/`, which then contained exactly one file —
itself. `vdrift_w28.txt` reads `files checked: 1` and `>>> V-UNEVALUATED: no sibling target was referenced by
any driver in this root`. The wave's six instruments (`fp_w28.sh`, `score_w28s.py`, `score_w28b.py`,
`score_w28n.py`, `w28b_manifest.sh`, `w28n_arm_w28.sh`) were all written afterwards and **were never checked**.
The plan's gate required `V-CLEAN`; the design says `UNEVALUATED` is never a pass; the wave proceeded.

**The self-test and both pinned FAIL fixtures are healthy** — `SELF-TEST PASS`, 262 `V28-B` findings on
`/mnt/d/hf_w27`, 2 `V28-C` findings on `/mnt/d/hf_w24`, and `/mnt/d/hf_w26` demonstrating expiry with 0. **The
instrument works; it was pointed at nothing.**

**The class, stated so it survives this wave:** F80's first three gaps were a *pattern* too narrow; F87's was a
*population containing the instrument*; this is a *population that was empty at the only moment the check was
taken*. **A blocking pre-flight must be re-run at the wave's close over the finished root, and the closing run
is the one whose verdict is quoted.** The pre-flight run proves the checker works; the closing run proves the
wave is clean. They are two runs and this series has been conflating them. Price: **~2 m per wave.**

### 9.5 Amendments owed to existing entries

* **[F43](#f43) — AMEND IN PLACE, and it is the amendment this wave owes most.** The entry's *"`StructureLayers`
  4 → 2 makes it strictly worse (0 at both central positions)"* is **confounded in source**, proved by
  `RULE W28-S` = `S-WELDED` at zero compute. Lowering `StructureLayers` moves **two** cutoffs in opposite
  directions: it narrows the post-wavelet Gaussian at `StarDetector.cs:632` (whose width is literally
  `p.StructureLayers * 2 + 1`, so 4 → 2 takes the kernel 9×9 → 5×5 and σ 1.44 → 0.80 px, which **helps** a 1-px
  star clear the binarization threshold) **and** shrinks the à trous residual's low-pass from ≈2⁴ to ≈2² px, so
  `src − residual` retains less of the star (which **hurts** it, and by more). The measurement stands; the
  **inference that the axis is useless does not**, because the two effects were never separated and no member of
  `StarDetectorParams` can separate them. **F43's "`MinHFR` is the ONLY axis that rescues it" must now be read as
  "the only axis that rescues it among those tested, on an axis set that contained a confounded knob."** See
  [F92](#f92-ready-to-paste); and note [F93](#f93-ready-to-paste) finds a **second** axis that rescues `D01`'s
  candidate formation (`NoiseClippingMultiplier` 3.8125 → 1.0 cuts its structure gap 79.2 %) — though not its
  `recall@high`.
* **[F62](#f62)** — append that wave 28 tested the "one band-pass phenomenon" claim out of sample and **it did
  not survive below the band**. `RULE D27`'s upper-edge result is untouched and the prediction holds on **9 of 10**
  blind datasets above the band; the lower-edge prediction fails on **0 of 4** LOW datasets, with `Δacc > 0` on
  all 17. `RULE D27` and item 8 may still be two views of one mechanism, but the *evidence* for the unification is
  one-sided and must be quoted that way.
* **[F31](#f31)** — append a second, independent demonstration of the reference's wing evaporation: on wave 28's
  blind population the golden-denominated and detector-only routes disagree on **5 of 17** datasets and **all
  five disagree in the same direction** (`Δrecall < 0 < Δacc`). A per-frame recall statistic is biased against
  the focus frame wherever the golden's denominator peaks there.
* **[F21](#f21)** — extend the measured `golden eval` rate. Wave 27 recorded 6–46 s per 9-frame cell (full
  20-dataset arm 349 s). **Wave 28's `D01` at `NC = 1.0` took 179 s** — 3.9× the previous maximum — because
  lowering the threshold multiplies the candidate count. **Price a cell from the RANGE and from the knob
  setting, not from the dataset alone.** Wave 28's 10 cells totalled 381 s of `TestApp` time inside a 432 s wall.
* **[F35](#f35)** — its finding that *"the W class's recall is lost in candidate FORMATION — the structure map
  never proposes 49 % of them"* now has a lever: `NoiseClippingMultiplier` converts most of that loss into
  formed candidates ([F93](#f93-ready-to-paste)). On `D02`/`D03` they become detections; on `D01` 97.8 % of them
  are re-rejected downstream, which is also where F35's *"`MinimumStarBoundingBoxSize` rejects another 17 % as
  `TooSmall`"* now points.
* **[F84](#f84)** — unchanged and re-affirmed: **wave 28 proposed and measured nothing on the `Sensitivity`
  axis.** `D01`'s `LowSensitivity` count rising from 786 to 13 612 at `NC = 1.0` is a **downstream consequence**
  of more candidates existing, not evidence for or against a sensitivity floor.
* **`docs/synthetic-af-bank-results-table.md`** — add the **in-focus kernel HFR `K` (px)** column from
  `truthModel` (`max(hfrMin, hfrMinEffective)`), and the note that `D01`/`D02`/`D03` are the **only three floored
  by the generator** (`hfrMin < hfrMinEffective`), all at `K = 0.700` despite 19.4 / 5.7 / 3.1 ″/px. It reorders
  the recall column into a monotone story and costs nothing. **Do not annotate it with a 2–4 px band claim
  below the band** — `W28-B` is `B-SPLIT` and the lower edge is not established.

---

### F85 — A closed, shipped correction was re-measured as an open defect, because two fields share a name

**Status:** **Done** (2026-08-13, wave 27) — the disclosure gap is fixed; the register contradiction is
corrected in place · relates to [F31](#f31), [F68](#f68) part 1, [F79](#f79), [F84](#f84)

`golden eval`'s `FP` and a Python re-derivation from the golden's `stars` list alone are **both** "false
positives": **11** and **150** on the same detections. Wave 26 read the second and compared it against the
first, concluding the owner's precision column was a lower bound when it was already truth-corrected. The 150
decomposes as **50** excluded by the golden's own `unresolved` boxes + **89** excluded by `TruthProtection` +
**11** surviving genuine wing-frame junk; its `137` ("within 12 px of ANY truth star") is a **third** quantity,
neither of the other two. **The junk count is 11, not 13** — two of the "13" sit inside an `unresolved` box.

**Proximate cause:** `GoldenEvalRunner` applied `TruthProtection` and never reported that it did, while
`BankVerifyRunner` reported it in three places. A grep of the 20 published logs, `golden_eval.txt` and
`golden_eval_frames.csv` for `scoringMode` / `protected` / `precisionNull` returned **zero hits**.

**Independent confirmation of the 89:** `score_t27_w27.py --protection off` gives `D08` FP `11 → 100`
(`/mnt/d/hf_w27/t27_v0_failend.txt`), and **350** across the population.

**Remedy, shipped:** `TestApp/SynthBank/TruthDisclosure.cs` ports all four of `bank-verify`'s disclosures into
`golden eval` — `scoringMode` + `protectedStars`, `precisionNull`, `truthViolations`, `scoredFraction` — plus a
fifth, `protectedDetections` (protection *exercised*). Wording is shared between the two harnesses so they
cannot drift apart. **`RULE S27-4` proves the change is report-only at byte level: 360 of 360 detection
artifacts byte-identical, `TP`/`FP`/`FN` unchanged on 20 of 20, and 20 of 20 report sinks changed** (so the port
is not inert). **Open sub-item:** `S27-1`'s route agreement was not adjudicated, and `protectedDetections` is
single-routed — see wave 27 results §7.3 and §8.6.

### F86 — The golden sidecars on disk carry the PRE-REPAIR policy in their own words

**Status:** **Open — a named caveat, deliberately not fixed** (2026-08-13, wave 27) · relates to [F31](#f31),
[F85](#f85)

`GoldenFromTruth.cs:744-748` writes *"a detection here should count as a false positive"* into the `Reason`
field of every `omitted` component, and that string is sitting in **all 180 `*.golden.json` in the bank**. The
behaviour it describes was repaired on **2026-08-03** and the string was not, because it is a diagnostic field
nothing reads programmatically. **It is the single most likely thing that actually misled wave 26.**

Wave 27 fixes the two contradicting XML doc comments (`GoldenFromTruth.cs:34-36` *"a detector reporting
something at its location should be scored as a false positive"* vs `:76-84` *"scoring a detection there as a
false positive would be punishing a detector for something the reference itself cannot certify either way"*) and
**deliberately does NOT regenerate the sidecars**, for three reasons in this order:

1. regenerating 180 goldens collides with **fingerprint class 2 preservation** and would invalidate every
   precision/recall number this series has published;
2. changing only the generator string would make future sidecars disagree with the 180 on disk — a silent
   divergence, the same class of defect being fixed;
3. nothing reads `Reason` programmatically.

**Anyone reading a `Reason` field in a `*.golden.json` must read this entry first.** The correct policy is:
`omitted` and `unresolved` are both *visibility* judgements, visibility bears on **recall**, and a rendered star
is not a false positive at any SNR.

### F87 — A rule that describes itself must exclude itself from its own population, or it launders its own subject

**Status:** **Done** (2026-08-13, wave 27) — closed with a both-ends self-test · a **fourth** gap in
[F80](#f80)'s checker, and a **new class**, not the `_`-word-boundary family

`verify_derivation_w27.py`'s `in_historical_block()` scanned backwards for any line *containing* a
`HISTORICAL_BLOCKS` token and treated it as opening a historical literal — **but the line that DEFINES
`HISTORICAL_BLOCKS` contains every one of those tokens**, so from that line to EOF the `V27-B` clause was
switched **off**.

**Evidence, both ends:** before the repair the checker reported **`V-CLEAN`, 0 unlicensed tokens** on its own
file **while printing `=== RULE V26 ===` as its banner on a wave-27 root** (`/mnt/d/hf_w27/vdrift_w27_run1.txt`);
after the repair it reported **32 findings on itself** (`vdrift_w27_run2.txt`, the pre-repair copy is preserved
as `verify_derivation_w27.py.bak` and contains **69** literal `V26` occurrences), and **255** on the pinned
`/mnt/d/hf_w26` fixture (`vdrift_selftest_FINAL.txt` `[5b]`). *(A "53 tokens" figure quoted during the wave was
a `grep -c` of lines, a third quantity, and is withdrawn; a pre-repair "204" on the `hf_w26` fixture was read but
its artifact was not kept, so it is testimony. **Neither is load-bearing** — the run1-versus-run2 pair proves the
defect without any count.)*

**The lesson, and it generalises past this checker:** the first three F80 gaps were a *pattern* being too narrow.
This one is a *population* silently containing the instrument, so the instrument's own definition of what to look
for became a licence to stop looking. **Any self-describing rule must state whether its own source is in its
population, and prove the answer with a fixture.** Closed with self-test clause `[6]`, which demonstrates that a
real historical block is exempt, that the file after it is not, and that the definition line opens nothing. Two
FAIL fixtures are now pinned: `hf_w24` (kept) and `hf_w26` (added, not swapped).

### F88 — `git diff --name-only <tree>` omits UNTRACKED files, so a two-binary provenance diff can miss a whole new source file

**Status:** **Open — remedy known and applied once** (2026-08-13, wave 27) · belongs beside [F66](#f66)

Wave 27's B16 adds `TestApp/SynthBank/TruthDisclosure.cs` — the entire substance of the change — and it was
**untracked at build time**, so `binary_provenance_w27.txt`'s tracked diff lists 3 files and omits it.
`Q27-V1`, which intersects the change set against the `optimize` gate's reachable set to decide whether a
42-minute gate is owed, would have computed that intersection over an incomplete population.

**Remedy, applied:** take the **union** of `git diff --name-only <tree> -- '*.cs'` and `git ls-files --others
--exclude-standard -- '*.cs'`. `q27_v1.txt` does this and reports 5 files.

**Same shape as [F66](#f66):** a provenance instrument reporting confidently on a population that silently
excludes the thing under examination. Every future two-binary provenance step must take the union.

### F89 — A mutation harness that restores with `cp -p` restores the pre-mutation MTIME, and MSBuild then skips the recompile

**Status:** **Open — remedy known** (2026-08-13, wave 27, controller process finding; **no artifact**)

`cp -p` preserves mtime. After a mutant is restored with it, MSBuild sees a source no newer than its object and
**skips the rebuild**, so the next run silently re-measures the mutant. In wave 27 it made a correctly-restored
tree look broken.

**Remedy:** `touch` the file after every restore; keep the sha256 check, which guards content but says nothing
about mtime. **Recorded honestly: this finding's evidence is the controller's account, not a file.** One mutant
(`M-S3`) was subsequently re-verified with its run record kept (`s27_2_mutant_MS3.log`, 3 red of 19); `M-S1`,
`M-S2a` and `M-S2b` remain testimony, and this defect is the likeliest reason their records were lost.

### F90 — Reuse the computation, never the banner, or the artifact lies about which rule it evaluated

**Status:** **Open — remedy known, one instance annotated** (2026-08-13, wave 27) · belongs with
[F74](#f74)'s family, not [F80](#f80)'s

`score_repro_w27.py` — the `RULE R27` comparator — was reused to perform wave 27's `S27-4` byte comparison. It
emits its own rule name **unconditionally**, so `/mnt/d/hf_w27/s27_score.txt` ends
`>>> RULE R27 = R-DIFFERS (40 files)` while `/mnt/d/hf_w27/repro_all_score.txt` ends
`>>> RULE R27 = R-IDENTICAL`. **For a period the record carried two contradictory verdicts for the same rule
name**, on two different populations, and a reader grepping `RULE R27 =` would have found the contradiction with
no way to tell which was which.

**This is sharper than F80's prose drift.** F80 is about *commentary* that stops describing the instrument.
Here the **verdict line itself** — the one line a future reader greps — was correct for the code and wrong for
the run.

**Remedy:** a reusable scorer must take its rule label as an **argument** and assert it against the manifest it
was handed, exactly as this series already computes every ordinal rather than typing it. **Fixed for wave 27 by
annotation, not deletion**: `s27_score.txt` now opens with a banner saying its own verdict line must not be
quoted and naming `s27_4_score.txt` as the scored clause, with the wrong line left in place at the foot so the
record shows what happened.

### F91 — A BEFORE/AFTER control whose two sweeps are built by different expressions reports a FALSE violation

**Status:** **Done** (2026-08-13, wave 27) — caught by the population assertion, corrected, both readings kept ·
[F74](#f74)'s shape in the BEFORE/AFTER direction

Wave 27's first `T27-V4` AFTER sweep returned **`T27-V4 = VIOLATED`**. **Nothing on disk had changed.** The
AFTER expression omitted the **20 `.log` files** that the BEFORE expression had captured, so it compared 780
paths against a BEFORE list of 800 and reported the 20 absentees as moved. A fingerprint gate is the control
that decides whether a wave's denominators moved under it; a false `VIOLATED` there forces `T-UNEVALUATED` on a
wave whose population was in fact untouched.

**The only reason it surfaced** is the standing rule that the **population size is asserted inside the file** —
`BEFORE 800 / AFTER 780` is arithmetic, not a judgement call. The corrected sweep reads **`BEFORE 800 AFTER 800
compared 800 … DIFFERING: 0 → T27-V4 = PRESERVED`**.

**The rule, general:** *a BEFORE/AFTER control must build both populations from the SAME expression.* Two
expressions intended to describe one population are two populations, and every difference the control reports is
then unattributable — in either direction. Prefer one function, called twice, with the population size asserted
equal before any hash is compared.

### Amendments owed to existing entries

* **[F31](#f31)** — append `RULE T27`'s verdict: **the repair it made is SOUND on 18 of the 19 blind datasets,
  saturated on `D18_m24_deep_shed` alone (`precisionNull` 0.2820, `A_all` 0.2372, `A_protOnly` 0.1938), with
  `D01_ultrawide_40mm` a named near-miss at 0.2182.** F31 is **not** reopened — the verdict is neither
  `T-SATURATED` nor `T-CHANCE-DOMINATED`, so the density guard is not triggered. Also record that F31's null was
  demonstrated on one dataset (`D09`) and is now generalised to 20, at four offsets, and that the shipped
  single-offset control is **under-powered on 7 of them**.
* **[F83](#f83)** — the decision proceeds on real numbers. **Caveat 3's `+0.034` must carry a factor label**: it
  is `+0.034` at detection binning 1 and `+0.003` at binning 2, where `D08` reads `P=0.997 FP=1` (§4.3).
* **[F62](#f62)** — append `RULE D27` = `D-MOVED`, 6 of 6: scoring at the optimizer's own detection-binning
  factor moves `recall@all` by up to **+0.296** (`D14`) and by **−0.012** on `D15`. The no-`BestJ`-across-factors
  landmine now has a measured magnitude on the recall side.
* **[F21](#f21)** — add the measured `golden eval` rate (§10) so no future wave prices a golden-eval arm off the
  `optimize` rate.
* **[F80](#f80)** — cross-reference [F87](#f87) as the fourth gap and the first of a new class; and add the
  reuse-direction case from wave 27 results §7.8, where a scorer re-aimed at a new population kept the original
  rule's verdict banner, so `s27_score.txt` claims `RULE R27 = R-DIFFERS` while `repro_all_score.txt` claims
  `RULE R27 = R-IDENTICAL`.
* **`docs/synthetic-af-bank-results-table.md`** — annotate `D18`'s precision cell in place as **uninformative**
  with its three statistics; annotate `D01` with the near-miss; add the `scoredFraction` column or at minimum
  the six low rows of §3; re-state the seven `RULE D27` rows at both factors with each labelled.

---

### F84 — The at-floor sensitivity datasets: what is actually owed, and what the evidence has already killed

**Status:** **OPEN — a decision, not a defect** (2026-08-13, answering the owner's question after wave 26) ·
depends on [F83](#f83), reframed by [F31](#f31) and [F23](#f23)

> #### CORRECTION, wave 27, 2026-08-13 — this entry's `91.3 %` describes a PRE-REPAIR quantity
>
> **Every passage below that says "91.3 % reference omission", and item (2), was written as though
> [F31](#f31)'s repair did not exist. It shipped 2026-08-03** (`aaf26e8`, refined `5c382a1`) as
> `TestApp/SynthBank/TruthProtection.cs`, wired into **both** `GoldenEvalRunner.cs:301-307` and
> `BankVerifyRunner.cs:464-466` — **and F31 is recorded `Done` in this same file, ~1 600 lines above.**
>
> Wave 26's re-score (`/mnt/d/hf_w26/pd08/F31_truth_rescore.txt`) re-derived false positives from the golden's
> `stars` list **alone**, consulting neither the golden's own `unresolved` boxes nor `TruthProtection`. Its 150
> decompose as **50 excluded by `unresolved` + 89 truth-protected + 11 surviving**; its `137` is a *third*
> quantity ("within 12 px of ANY truth star"). **`golden eval`'s FP field on those same detections is 11**, and
> the owner's table's `0.966` is `308/(308+11)` — truth-corrected as printed. Two different fields, both called
> "false positive" ([F68](#f68) part 1).
>
> In place, below:
> * **Item (2) — "Re-measure precision against `truth.json`" — is DISCHARGED, not owed.** It shipped in wave 2.
> * *"without it, F23 and F83 are both undecidable"* is **false**. F23 was re-measured at `afbank-verify/5`, and
>   that re-measurement is what voided it.
> * Point 4's *"`D08`'s apparent cost is 91.3 % reference omission"* should read: **`D08`'s scored cost is 11
>   genuine junk detections**, all on the two extreme wing frames, zero on the seven interior frames. The
>   correction **strengthens** this entry's conclusion — see [F83](#f83) caveat 3.
> * Item (4)'s **"13 junk detections" is 11**: two of the 13 sit inside a golden `unresolved` box.
>
> **What was actually open:** `golden eval` emits **none** of `bank-verify`'s four truth disclosures
> (`scoringMode`/`protectedStars`, `precisionNull`, `truthViolations`, `scoredFraction`), so no reader of a
> published `golden_eval.txt` can tell whether protection was applied. That silence is the mechanism that
> produced this misreading, and wave 27 ships the fix. See
> `docs/synthetic-af-bank-followups-wave27-design.md` and `docs/wave27-register-correction-survey.md`.

**The eight at-floor landings** (`BrightnessSensitivity = 0.0`), resolved from source predicates by wave 26's
prep, with their combined effective gates:

| seed | landing | gate | seed | landing | gate |
|---|---|---|---|---|---|
| seedA0 | `D08_c11_2800mm` | 0.5625 | seedA1 | `D01_ultrawide_40mm` | 0.2234 |
| seedA0 | `D10_rc16_3250mm_sparse` | 2.3906 | seedA1 | `D09_c14_3800mm` | 2.4891 |
| seedA0 | `D11_rc10_585_afbin2` | 2.3625 | seedA1 | `D12_c14_585_afbin2` | 1.6000 |
| seedA0 | `D12_c14_585_afbin2` | 2.1313 | seedA1 | `D16_esprit550_ha3` | 0.1969 |

Only `D12` pins on **both** seeds. Pinning is a property of a **landing**, not of a dataset.

### The state of play, before any followup is chosen

1. **The pin is INERT at every one of the eight landings.** All 8 read `GateIsProvablyInert = True`,
   `GateRejectedCount = 0`, and `LowSensitivityRejections = 0` **on every frame**. **There is no measured harm
   today.**
2. **It is a real optimum, not a wandering artifact.** `RULE Q26` = `Q-PIN-COSTED`, `Q26-A` = `A-RESPONSIVE`
   with **0 flat of 8**: moving sensitivity off the floor costs `J` every time.
3. **It costs `J` to forbid it**, `dJ` `0.000104`-`0.019269` on the four scored (`Q26-B`), and is **nearly free
   to forbid where the search was not going there** — `Q26-D`, 16 datasets, mostly ~`1e-4` and **exactly 0**
   where the unconstrained search already chose a sensitivity at or above the bound.
4. **It does not measurably cost precision.** `P-D08` **REFUTED at n = 3**: `D01` (gate 0.2234) and `D16`
   (0.1969) both carry base precision **1.000 with ZERO false positives**. And `D08`'s apparent cost is
   **91.3 % reference omission** — 137 of its 150 golden-false detections match a **real rendered truth star**
   at the same 12 px radius, against **0.30** expected by chance. That is [F31](#f31)'s mechanism, quantified.
5. **What the pin buys is FAINT-TIER RECALL ONLY.** `recall@high` and `recall@high+med` are **unchanged on all
   four** scored datasets when sensitivity is raised.
6. **It is [F6](#f6)'s pair, not one axis.** The three landings with sub-`1.5` gates are exactly the three where
   the search **also** drove `StarClippingMultiplier` down, twice to that axis's own `0.25` floor.

### What is owed — in priority order, with prices

**(1) The `MarginalSnrStrength` decision. ~0 m to decide; a fresh 42 m baseline to act.** [F23](#f23) built the
objective's **only** false-positive cost — `SMarginalSnr`, multiplicative, applied after the weighted sum — and
it **ships at `MarginalSnrStrength = 0.0`, disabled.** Its own comment says it returns `1.0` *"whenever the
candidate's Sensitivity is at or above that floor"*, i.e. **it is purpose-built to bite exactly when sensitivity
is pinned below the peak-SNR floor.** Constants already chosen: `MarginalSnrFloor = 6.0`,
`MarginalSnrThreshold = 0.05`, `MarginalSnrMinFactor = 0.5`.
**But do NOT simply switch it on.** F23 is `Won't fix as written` **because its evidence base was VOID** — the
precision collapse it was built to stop (0.993 -> 0.451 on `D09`) was measured against the same under-listing
golden this entry quantifies at 91.3 %. **Enabling a penalty calibrated against void evidence would trade real
recall for an artifact.** The decision needs (2) first.

**(2) Re-measure precision against `truth.json`, not `golden.json`. ~1-2 h, no product code.** This is the
prerequisite for every objective decision, and it is now demonstrably cheap: the renderer's complete star list
ships beside every frame (`*.truth.json`, 126 stars where the golden lists 9), the detections are already on
disk in `golden eval`'s own `detected_f*.csv`, and the match radius is declared in `synthetic_meta.json`. A
truth-based precision pass would give **the first honest precision numbers on this bank** and would say whether
a precision term is worth adding at all. **Without it, F23 and F83 are both undecidable.**

**(3) Report the EFFECTIVE gate, not the nominal `0.0`. ~30 m, product, no measurement.** The landing, the
summary and the wizard all show `BrightnessSensitivity = 0.000`, which looks alarming and **means nothing on its
own** — the value that actually rejects candidates is `EffectiveSensitivityGate` (`0.20`-`2.49` across the
eight), and `SensitivityIsAtFloor` / `GateIsProvablyInert` are already computed and already in
`aggregate_summary.json` (nested under `ExposureRecommendation`). Surfacing them turns *"the optimizer chose
zero"* into *"the optimizer chose zero, and it is inert because the structure/clip stage gates harder"*. **This
is the only item that improves what a user sees, and it changes no behaviour.**

**(4) The 13 junk detections. ~30 m to characterise.** The one genuinely wrong thing measured: `D08`'s 13
detections matching **neither** golden nor truth, **all on the two extreme wing frames** (`13672`, `14328`) and
**zero** on any of the seven interior frames. That is a detector-at-its-limit behaviour at extreme defocus, and
it is unrelated to the pin.

### What the evidence has KILLED — do not propose these

- **Raising `DefaultSensitivityLower`, or any floor on the Sensitivity axis alone.** The search drives
  `StarClippingMultiplier` down alongside it ([F6](#f6)); bounding one axis moves the landing along the other.
- **"Stop persisting/reporting the extreme."** It hides the pathology instead of fixing it, and item (3) is the
  honest version of the same impulse.
- **Forbidding the extreme generally**, on the current evidence. It costs `J` where the search wants it and buys
  **no measured precision** — `P-D08` refuted at n = 3, and `D08`'s cost 91.3 % artifact.

### The one-sentence version

**The pin is currently harmless, is a real optimum of an objective that cannot see precision, and the only thing
worth doing before touching it is measuring precision against the renderer's truth instead of a reference that
under-lists by an order of magnitude at defocus.**

### F83 — `J` carries no precision term on an unlabelled run, so a sensitivity pin is free in the objective by construction
**Status:** Open · **structural, source-derived, zero compute** · found 2026-08-13, wave 26, pricing the sensitivity
pin the owner asked to avoid

**Every `optimize` result this project has produced on the synthetic AF bank was scored by an objective with no
false-positive cost in it.** Not because a term is mis-tuned — because the branch that carries precision is not
taken.

`JRun` composes the objective two ways (`OptimizationObjective.cs:484-490`):

```csharp
if (effRecall.HasValue && effPrecision.HasValue) {
    var sLabel = LabelScore(effRecall.Value, effPrecision.Value);
    num = c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit + c.Wl * sLabel;
    den = c.Wf + c.Ws + c.Wc + c.Wl;
} else {
    num = c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit;
    den = c.Wf + c.Ws + c.Wc;
}
```

`LabelScore = 0.5·recall + 0.5·precision` (`:426-428`) at `Wl = 0.25` (`:33`) is **the only place precision enters
the weighted sum**, and it is reached only when the run carries labels. **The bank runs unlabelled** — wave 18's
own aggregate summary prints `Labels: (none — unlabeled)` on line 4 of every dataset. So on this bank

```
J = (Wf·sFocus + Ws·sStars + Wc·sFit) / (Wf + Ws + Wc)     Wf = 0.55, Ws = 0.20, Wc = 0.25
```

and **`SStars` (`:400`) counts stars, not correct stars.**

**The one false-positive term that exists ships OFF.** [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)'s
successor `SMarginalSnr` — whose own comment at `:519` calls it *"the objective's only false-positive cost"* — is
gated on `MarginalSnrStrength`, shipped default **`0.0`** (`:238`), i.e. disabled, deliberately and with reasons
(F31 voided the metric F23 was built against). The other multiplicative terms (`SDefocusPrecision`, `SFitGuard`,
`SHfrOutlier`) all return exactly `1.0` at this baseline. **Nothing charges for a wrong detection.**

**Consequence: driving `BrightnessSensitivity` to its floor buys stars for free.** More detections raise
`sStars`; the gate that would have kept the junk out is the knob being lowered; and the score has no term that
notices. **That is the mechanical cause of `RULE P23`'s "22 of 24 driven to the bound"** — P23 established that
**0** pinned axis-instances were seeds sitting where they started and **22** were search outcomes, and had no
mechanism for the walk. This is the mechanism, and it is arithmetic on the shipping source.

**Scope, stated precisely.** This does not invalidate any published `J`: those are correct computations of the
objective as composed. It invalidates a *reading* — any sentence of the form *"the optimizer chose this because
it is better"* means **better in a score with no false-positive cost**, and that includes wave 18's 40 landings,
`docs/synthetic-af-bank-results-table.md`, and every landing quoted in this series since wave 5.

---

#### `RULE P23`'s owed arm is DISCHARGED here, and the flat-direction hypothesis is REFUTED

Wave 23 §5.2 registered the arm this finding owed: *"a one-axis-at-a-time perturbation of the landed vector,
re-evaluating `J` at each bound ±1 step"*, to separate *"pinned because flat"* from *"pinned because the bound is
genuinely optimal"*. **Wave 26 ran a strictly stronger instrument** — F6's 2×2 at a fixed vector, **plus** a
paired re-search under `--sensitivity-floor`, which the one-axis form cannot reach — and the answer is
unambiguous. (P23 itself has no entry in this register; it lives in
`docs/synthetic-af-bank-followups-wave23-results.md` §5, and this section is its answer.)

| clause | population | result |
|---|---|---|
| **`Q26-A`** — F6's 2×2 at the landed vector, `optimize --max-evals 1` | 8 at-floor landings of wave 18's 40 (20 seedA0 + 20 seedA1, 0 could-not-look) | **`A-RESPONSIVE`. 0 flat of 8.** `dJ(sens)` spans 0.00092 – 0.0494 |
| **`Q26-B`** — paired re-search, `±--sensitivity-floor 10.0`, `--max-evals 250` | the 4 seedA0 datasets contributing an at-floor landing | **4 of 4 COSTED.** 0 lost the hard floor; **0 equal-or-better** |
| **`RULE Q26`** | branch table applied as written | **`Q-PIN-COSTED`** |

`Q26-B`, per dataset — `dJ = BestJ(unconstrained) − BestJ(constrained)`, against that run's **own** wave-18 search
gain as a named reference scale, **no bar**:

| dataset | unconstrained (sens) | constrained (sens) | `dJ` | its own search gain |
|---|---|---|---|---|
| `D08_c11_2800mm` | 0.9963959372017032 (0.0) | 0.9962920262241155 (**11.0**) | 0.000104 | 0.001221 |
| `D10_rc16_3250mm_sparse` | 0.9934488063105885 (0.0) | 0.9741799265643882 (10.0) | **0.019269** | 0.036248 |
| `D11_rc10_585_afbin2` | 0.9961036483632415 (0.0) | 0.9953022891499348 (10.0) | 0.000801 | 0.010117 |
| `D12_c14_585_afbin2` | 0.9957418877445253 (0.0) | 0.9944025574491060 (10.0) | 0.001339 | 0.010079 |

**So the pin is load-bearing in `J`'s terms — and `J`'s terms are the defect.** The two halves of this entry are
one finding: forbidding the extreme costs measurable objective, and the objective it costs cannot see precision.
`D08` is worth noting separately: the constrained search lands at **11.0**, *above* the floor it was given, which
is what a genuine optimum above the bound looks like rather than a search pinned to a new wall.

---

#### At the landing, the pinned knob is PROVABLY INERT on 8 of 8 — and that is a different statement

Read from the landings' own `ExposureRecommendation` block at zero compute. `GateIsProvablyInert` is `True`,
`GateRejectedCount` is `0`, and per-frame `LowSensitivityRejections` is `0` on **every frame of all eight**.

The mechanism is arithmetic: `EffectiveSensitivityGate = max(Sensitivity, PeakResponse × StarClippingMultiplier)`
(`StarDetector.cs:1532-1533`, `:1552-1553`), so **when `Sensitivity` is 0 the `max` is always taken by the
clip-derived term and the gate lands exactly on `InertSensitivityBound` by construction.** On the at-floor
subpopulation the inert rate is **8 of 8 = 100 %**, which sharpens P23's pooled `9 of 40 = 0.2250`: *every*
landing that pins sensitivity has a sensitivity knob that rejects nothing.

**This does not contradict `Q26-A`, and the two must not be pooled.** `dJ(sens) > 0` is a statement about
`Sensitivity = 10`, where the gate is **not** inert and does start rejecting; it says nothing about whether `0`
was doing anything. Two points, two statements.

#### Where the two knobs meet — F6's diagonal, visible in the landings

Of the eight at-floor landings, the three whose effective gate sits below the source's *"provably inert at
shipped defaults"* boundary of ~1.5 (`OptimizerVariable.cs:126-131`, 0.75 × 2.0) are **exactly** the three where
the search **also** drove `StarClippingMultiplier` down:

| landing | `StarClippingMultiplier` | `PeakResponse` | effective gate |
|---|---|---|---|
| `seedA0 / D08_c11_2800mm` | 0.75 | 0.75 | **0.5625** |
| `seedA1 / D01_ultrawide_40mm` | **0.25 — the axis's own lower bound** | 0.89375 | **0.2234375** |
| `seedA1 / D16_esprit550_ha3` | **0.25 — the axis's own lower bound** | 0.7875 | **0.196875** |

On the other five, `Sensitivity = 0` is masked by a clip the search **raised** (2.0 – 3.375). `n = 3`, **reported,
no bar** — but it is the cheapest available discriminator between at-floor landings that are cosmetic and ones
that are behavioural, and it is why a floor on the Sensitivity axis alone cannot work: **the search reaches a low
combined gate through the clip axis, which no sensitivity floor closes.** That is
[F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)'s
measured *"it loosens other gates to win the stars back, admitting junk through a different door"*, visible in
the landings rather than in an arm.

---

#### What it costs in precision: `PREDICTION P-D08` CONFIRMED at `n = 1`, with caveats larger than the effect

Pre-registered before the data (wave 26 design §7.4) from a **source boundary**, not from a number: of the four
seedA0 at-floor gates, `D08`'s **0.5625 is the only one below ~1.5**, and `D08` is the **only precision miss in
the owner's whole 20-dataset table** (0.966 against 1.000 everywhere else). The prediction: re-scoring the landed
vector at the shipped `--sensitivity 10.0` raises `D08`'s precision, leaves the other three at 1.000,
lowers `recall@all` on all four, and raises `REJECTED:LowSensitivity`. **All four held.**

| dataset | precision | `recall@all` | **`recall@high`** | `FN:LowSensitivity` |
|---|---|---|---|---|
| `D08_c11_2800mm` | **0.966 → 1.000 (+0.034)** | 0.978 → 0.803 (−0.175) | 1.000 → 1.000 (**unchanged**) | None → 55 |
| `D10_rc16_3250mm_sparse` | 1.000 → 1.000 | 0.965 → 0.730 (−0.235) | 1.000 → 1.000 (**unchanged**) | None → 28 |
| `D11_rc10_585_afbin2` | 1.000 → 1.000 | 0.892 → 0.853 (−0.039) | 0.850 → 0.850 (**unchanged**) | None → 16 |
| `D12_c14_585_afbin2` | 1.000 → 1.000 | 0.705 → 0.593 (−0.112) | 0.720 → 0.720 (**unchanged**) | None → 37 |

**`n = 1` is a lead, not a law**, and three caveats keep it there:

1. **Three of the four cells cannot falsify the prediction's second half** — 1.000 is the ceiling.
2. **The precision instrument has almost no dynamic range here.** 19 of the owner's 20 datasets read exactly
   1.000, and [F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)
   warns in terms: *"all-1.000 means the metric is saturated again, not that the detector is perfect."*
3. **`D08`'s 11 false positives sit exactly where F31 says the reference is incomplete** — **3 on frame `13672`
   and 8 on frame `14328`, the two extreme wing frames, and 0 on all seven interior frames.** F31 measured the
   mechanism on **this dataset by name**: *"`D08` holds 81 golden stars at focus and 9 at the extreme frame,
   against 123–126 truth stars per frame throughout."* The base cell accepts 10 and 15 on frames whose golden
   holds 9. **So the +0.034 may be real faint stars the golden omits rather than junk**, and separating the two
   needs a re-score against each frame's own `*.truth.json` — F31's own method, ~~**not run**~~.

   > **CAVEAT 3 IS DISCHARGED, wave 27, 2026-08-13 — and it resolves IN THIS ENTRY'S FAVOUR.** The separating
   > measurement had already been run, twice over. (i) The scoring is truth-protected at source since
   > 2026-08-03 ([F31](#f31)), so these 11 survived `ExcludeProtected` — by construction **none of them has an
   > `omitted`/`merged-into` truth star within the 12 px radius**. (ii) Wave 26's own per-frame table
   > (`/mnt/d/hf_w26/pd08/F31_truth_rescore.txt`) reports **0 real** of the wing-frame false positives on both
   > `13672` and `14328`, against "ALL REAL" on the seven interior frames. **So the 11 are genuine junk and the
   > `+0.034` is real, not a reference artifact.** Caveat 2 (saturation) is untouched by this and is precisely
   > what `RULE T27` measures.

**What survives all three**, because it does not read the golden at all: the FN attribution. `REJECTED:LowSensitivity`
is the *detector's* count of what the sensitivity gate rejected, and it goes None → 55 / 28 / 16 / 37.

**And one thing nobody predicted, which bounds the whole trade: `recall@high` does not move on any of the four**,
nor does `recall@high+med`. **Every star the pin buys is in the faint tier.** Same shape as the F23 calibration
table at `OptimizationObjective.cs:180-195` (*"the bright tier is never at risk from this gate"*), reproduced on
a different population at the landed vectors.

---

**Why it matters.** The owner's goal 3 is to **avoid** parameters pinned to extreme values. The measurement says
the pin is not cosmetic in `J`, so simply forbidding it costs measurable objective — **but the objective it costs
cannot see precision**, and at the landing the pinned knob rejects nothing. The decision is therefore an owner's
call between three options, none of them measured, and this entry does not pick one:

| option | evidence | price |
|---|---|---|
| **(a) raise `DefaultSensitivityLower`** (`OptimizerVariable.cs:145`, `:150`; shipped `0.0`) | one line, and exactly the shape of "avoid this region" — but **F23 already measured a hard floor at 6 as worse than doing nothing** (broke four healthy datasets, helped three), and the clip-axis escape above is why | 1 line + **a fresh 42 m baseline** (a floor change invalidates every landing in the bank) |
| **(b) give `J` a precision term on unlabelled runs** | this entry is the mechanism; `SMarginalSnr` is already implemented, tested and flag-selectable at `--marginal-snr-strength` — but [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains) says `J` sits at 0.98–0.999 before the search starts, so a new term competes for an exhausted fourth decimal, and the term is **structurally escapable** (`D12` landed at 6.25, `D15` at 6.75, against a floor of 6.0) | **a coordinate-system move owing a fresh baseline** — [F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none) / [F59](#f59--the-settings-export-drops-every-knob-whose-setter-validates-so-pinned_settingsjson-has-been-missing-five-detector-knobs-since-wave-5)'s costing lesson: **42 m + re-derivation**, not the edit |
| **(c) label the bank so the existing `Wl · sLabel` path activates** | **changes no product code** — the branch is already there and tested; it is the `else` at `:488` this bank takes. But it makes `J` depend on the golden, whose precision F31 shows is a **lower bound** that evaporates at the sweep wings | **unmeasured.** Needs a labelled `optimize` run, which nobody has performed |

**Next step, and it is the cheapest decision-moving measurement left in the register: take `PREDICTION P-D08`
from `n = 1` to `n = 3`.** The **seedA1** root carries two more at-floor landings with sub-1.5 combined gates —
`D01_ultrawide_40mm` (0.2234) and `D16_esprit550_ha3` (0.1969), **both with `StarClippingMultiplier` pinned at
its own 0.25 floor** — and neither has ever been scored for precision at a raised sensitivity. **Two
`golden eval` cells, ~2 minutes, no binary, no gate.** If `D08`'s +0.034 replicates there the lead becomes a
finding; if it does not, the co-occurrence is a coincidence and option (b) loses its only measured support.
**Score it against `*.truth.json` as well as the golden**, or caveat 3 above applies to the replication too.

Reproduce: `/mnt/d/hf_w26/q26_score.txt`; `/mnt/d/hf_w26/c_golden/D08_c11_2800mm__{base,sens}/attempt01/golden_eval.txt`;
`/mnt/d/hf_w18/seedA0/D08_c11_2800mm/aggregate_summary.{json,txt}`;
`OptimizationObjective.cs:33,238,400,426-428,484-490,519`; `OptimizerVariable.cs:126-131,145,150`;
`docs/synthetic-af-bank-followups-wave26-results.md` §6–§9;
`docs/synthetic-af-bank-followups-wave23-results.md` §5.2.

### F7 — Adaptive binarization is not uniformly good for the AF fit
**Status:** Open · first observation under a correct C0

Now that C0 runs as-shipped (`2c91e1b`), enabling `LocallyAdaptiveBinarization` improves σ_focus on some runs and
degrades it on others: `CWhiteFocus` 3.000 → 2.502 and `mufti` 6.917 → 5.894, but `caboose` 1.398 → 4.196 and
`uneven` 14.107 → 20.123. Recall is essentially unchanged (median Δ −0.0003).

The feature was AF-bank-gated on other criteria; this is the first look at it with C0 measuring the shipped
config. Not necessarily wrong — but it should not be assumed uniformly beneficial.

### F8 — Optimizer landings are not reproducible across invocations
**Status:** Open

`bobp_m101` landed at three different points on the same frames and the same corrected pipeline:
`sens 0 / clip 0.25`, `sens 0 / clip 6.875` (prepass A), `sens 30.1 / clip 3.44` (prepass B). Consistent with the
F6 diagonal valley, but it means **a single landing is not evidence** about which corner the optimizer prefers,
and any claim resting on one `optimize` run should be treated as anecdote.

> **BOUNDED 2026-08-11 (wave 19, RULE R19).** This entry's headline does **not** survive `--settings` +
> `--profile-id` pinning with one `TestApp.exe`. Re-running the same 20-dataset arm on a second build and diffing
> the **full 33-key landing vector** gave **20 of 20 datasets and 660 of 660 key comparisons identical** —
> `FinalJ` and `BaselineJ` included, and every knob besides. See
> [F73](#f73--the-gate-has-certified-nine-binaries-by-checking-one-of-a-landings-35-fields-and-the-other-33-had-never-been-looked-at--they-are-identical-660-of-660).
>
> **The entry is bounded, not refuted, and the distinction is the evidence it rests on.** Its three landings came
> from three *different pipelines*, before `--settings`/`--profile-id` pinning existed; wave 19's null arm varied
> only the build stamp and the invocation. **So "not reproducible across invocations" is now measured false for a
> pinned pipeline, and remains untested across code changes** — which is the axis `bobp_m101` actually varied.
> Cite this entry for unpinned or cross-pipeline runs; cite F73 for the pinned noise floor.

### F18 — Step size is sized by curve geometry alone, so the sweep outruns what the detector can see
**Status:** Open — **mechanism shipped behind flags, default OFF (wave 7)**; both open decisions closed; the
σ_focus arms RAN and returned a **null** — the bank cannot exercise this defect · found 2026-08-02 reproducing a
3800 mm sweep that yielded four dead frames

`StepSizeRecommender` sets the half-width from the fitted HFR curve: the distance at which HFR reaches
`HfrThresholdMultiple = 3.0` × its minimum, then `step = W / 3.5` at `DefaultOffsetSteps = 4`. That is a pure
**curve-geometry** criterion — nothing in it asks whether stars are still *detectable* out there.

Measured on `AutoFocus_20260802_122354` (3800 mm, 14 s, in-focus HFR 5.96 px, one frame per position):

| distance | 1770 | 3540 | 5310 | 7080 | 8850 |
|---|---|---|---|---|---|
| HFR (× min) | 1.2–1.3× | 1.8–2.0× | 2.6–2.8× | 3.4–3.6× | — |
| stars | 10 | 10 | 6, 3 | 1, 1 | **0** |

Two separate over-reaches:

1. **The 3× band is wider than detectability.** The band puts the half-width at 6221 steps (→ step 1778; the
   wizard recommended 1725), but the outermost position still yielding the `NHard = 3` stars the objective
   requires is at **5310**. The constant is ~15% past what this rig can measure.
2. **The executed sweep exceeds the band the step was sized for.** The step is sized for 3.5 points per side
   *within* the band, but the run takes 4 offset + 1 focus-recovery = 5 per side: 5 × 1770 = 8850 = **4.4× min
   HFR**, 43% beyond the band. The recovery step lands where nothing is detectable, which is where all the
   flat-topped rejections and starless frames came from.

**Why it matters beyond one rig.** The band is relative to min HFR, so it adapts correctly when a filter changes
the focus spread — but it does **not** adapt to flux. A narrowband filter spreads fewer photons over the same
defocused area, so stars vanish at a *lower* HFR multiple while the curve looks similar. This is the likely cause
of step-size recommendations that vary confusingly between filters on the same rig: each is right about geometry
and blind to signal.

**Suggested next step.** Bound the half-width by both criteria:

```
half_width = min(W_3x,        # current: fitted 3x min-HFR band
                 W_detect)    # NEW: outermost position still yielding >= NHard stars
step = half_width / points_per_side   # sized for the sweep ACTUALLY run, incl. recovery
```

`W_detect` is *measured*, not extrapolated, so it fits the recommender's converge-over-runs philosophy and its
`MaxHalfWidthSampledHalfSpanMultiple = 1.5` cap, and it adapts per filter for free. The per-frame star counts it
needs are already plumbed (`RunEvaluationMetrics.FrameStarCounts`). On the run above it gives
`min(6221, 5310) = 5310` → step ≈ 1060–1330 instead of 1725, putting every frame inside the detectable range.

Two open decisions: whether focus-recovery steps *should* extend the sweep past the band (they are deliberately
far-from-focus, but today they silently widen it by 43%), and what floor `W_detect` needs so a starless run cannot
collapse the sweep — it should only ever tighten `W_3x`, never drive it below a sane minimum.

This changes the shipped recommender for every user, so it wants a design spec plus bank validation with σ_focus
as the acceptance metric, not an inline patch. Related: the flat-topped rejections that motivated this are
surfaced as sweep-geometry evidence by `ExposureRecommendation.FlatRejectedCount` (PR #159).

**DESIGNED AND SHIPPED BEHIND FLAGS 2026-08-06 (wave 7); the σ_focus arms are wired and OWED.** Spec:
[`docs/synthetic-af-bank-followups-wave7-design.md`](synthetic-af-bank-followups-wave7-design.md) §2.

`StepSizeRecommender.Recommend` takes an optional `SweepDetectability` (per-frame star counts, focuser positions,
recovery flags, `NHard`) and computes `half_width = min(W_3x, max(W_detect, floor))`. Absent ⇒ **byte-identical**,
pinned by a test, so one binary is both arms ([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)).
On this entry's own 3800 mm run: `min(6221, 5310) = 5310` → step **1517**. *(This entry quotes 1060–1330 for the
same run; that divides by 4–5, i.e. it is the EXECUTED-sweep figure — arm S gives 5310/4.375 = 1214. Both are unit
tests now, and separating them is why there are two arms.)*

**The two decisions this entry left open, both closed:**

- **(a) Should focus-recovery steps extend the sweep past the band?** *Yes for the 3× band — that is their purpose,
  and `JRun` already exempts them from its hard floor — but never past detectability.* Which of the two the SHIPPED
  default should be is settled by MEASUREMENT: sizing the base step for a conditional path costs **20% of the lever
  arm on every successful run** (divisor 3.5 → 4.375 at 4 offset + 1 recovery), and that is not obviously worth
  paying. So the recommendation now reports `MaxUsefulHalfSpan` = the outermost frame that measured anything, and
  arm S (`--step-size-for-executed-sweep`) is measured against arm D (`--step-detect-bound`) with a rule fixed in
  advance. **Clamping the engine's recovery-step placement to `MaxUsefulHalfSpan` is engine-side and is NOT done
  here** — filed as its own followup.
- **(b) What floor does `W_detect` need?** Two guards. Fewer than `MinFramesForDetectHalfWidth = 3` qualifying
  non-recovery frames ⇒ `W_detect` is **NaN (no bound), never 0** — a thin run has shown that it did not detect
  anything out there, not that nothing is detectable, and only the first reading is safe. And
  `MinHalfWidthSampledHalfSpanMultiple = 0.5`, **the mirror of the existing `MaxHalfWidthSampledHalfSpanMultiple =
  1.5`**: the recommender has always bounded how far one run may WIDEN and had no bound on how far it may NARROW.
  At 0.5 the worst one-run shrink is 0.57×, so a pathological run halves the step and converges over runs.

**Acceptance rules, pre-registered.** Arm D ships if median σ_focus over the bank is no worse than the control by
more than 2%, no dataset regresses more than 20%, and the A3 final-step assertion passes on at least as many cells.
Arm S ships instead of D only if it beats D by more than 5% median. `BaselineJ` identical across C/D/S before any
σ is read (F41's free check); and if arm D's `W_detect` equals the sampled half-span on most datasets it is
reporting the sweep's edge rather than detectability and the arm is void.

**`optimize` deliberately stays on the pre-F18 recommender**, so every F32/F35 arm's reported step remains
comparable; the arms run through `synth-validate`, which is the recommender's own convergence driver.

**A DEFECT IN THE RULE, caught by this entry's own pre-registered instrument check.** The check said: *if
`W_detect` equals the sampled half-span on most datasets it is reporting the sweep's edge rather than
detectability, and the arm is void.* It came back equal on **all 20** datasets. `W_detect` is bounded above by the
sampled half-span **by construction** — it is the outermost SAMPLED position that cleared `NHard` — so when no
frame falls below the floor, returning the sweep's own edge turns `min(W_3x, W_detect)` into *"never recommend a
sweep wider than the one you just took"* on every healthy run. That is a cap on WIDENING, a different rule, and one
the recommender already has. **Fixed: no starved frame ⇒ NaN ⇒ no bound** — "unmeasurable is not zero", at the
other end. The first arm run was discarded and re-run on the corrected binary.

**ARMS RUN 2026-08-06 (wave 7). The rule fires for arm D, against arm S — and arm D's pass is VACUOUS.**
28 (dataset, scenario) cells over `D05`/`D06`/`D09`/`D12`/`D15`/`D16` × S0/S1/S2 (+S3/S6 where applicable), three
arms, one binary, `--settings` pinned. Round-0 control: **0 violations**. Assertion verdicts **identical across all
three arms** (Pass 82 / Fail 2; the 2 are in arm C too — pre-existing S2 behaviour, F25/F34).

| rule (fixed in advance) | outcome |
|---|---|
| arm D ships if median σ_focus no worse than C by >2%, no dataset worse by >20%, A3 count not reduced | **FIRES** — median ratio 1.0000, 0 worse, counts identical |
| arm S ships instead of D only if it beats D by >5% median | **DOES NOT FIRE** — median 1.0000, and S is >20% WORSE on 2 cells |

**But arm D is byte-identical to the control on 26 of 28 cells.** The bound bound in exactly ONE cell (`D16` S2),
whose σ_focus is **NaN** (degenerate fit) — so its only active cell produced no readable acceptance metric.
**Zero** cells improved. The rule fires because the arm is INERT, not because it is good.

**Consequence: the mechanism ships, the flag stays default OFF.** This entry asked for bank validation with
σ_focus as the acceptance metric and the bank returned a **null**: at the derived exposures the detectability limit
is never reached (edge-frame headroom 3–2746 stars against a floor of 3), and this entry's own evidence came from a
real 3800 mm rig at 14 s whose wings went starless — which no dataset here reproduces. Turning it on for every
user on a vacuous pass would be reading a null as a green light. **Adoption needs a rig where the defect
reproduces**, and wave 7 shipped the instrument that identifies one (`StepSizeRecommendation.MaxUsefulHalfSpan`).

**What the bank cannot do, recorded as a property of the bank:** its exposure derivation targets `NTarget = 20`
stars over the gate on the MEDIAN frame, which on these fields implies ≥ `NHard` at the edge. A dataset that could
exercise this entry has to be built deliberately — starved on purpose — or borrowed from the real bank.

### F25 — From a far-too-wide sweep the step recommender widens it further, instead of recovering
**Status:** Open · found 2026-08-03 running scenario S2 (step ×4) on the synthetic AF bank

When the sweep is so wide that the hyperbola fit degenerates, `StepSizeRecommender` responds by asking for a
**wider** sweep still. There is nothing that recognises "this fit is garbage, retreat".

**Evidence.** `D05_tec140_1000mm`, scenario S2 (bootstrap step = 140, i.e. 4× the correct 35):

| round | step | fit R² | recommended |
|---|---|---|---|
| 0 | 140 | **−0.223** | **240** |
| 1 | 240 | 1.000 | 36 |

A **negative** R² means the fit is worse than a horizontal line — there is no usable curve at all — and from
that the recommender produced 240, moving 4× too wide to nearly 7× too wide. It only recovered because round 1
happened to fit cleanly at the wider spacing. `A4` also fired here (`WasCapped=True but truth predicts False`),
which is the cap logic responding to the same degenerate fit.

**Why it matters.** This is the mirror image of [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see):
F18 is the sweep over-reaching what the detector can see, and this is the recommender *amplifying* that
over-reach once it has. A user who starts too wide — the likely case on an unfamiliar rig — can be walked
further out rather than back in. The recovery on D05 was luck, not design.

**Re-measured 2026-08-03 (F23 wave 1) — the manifestation is seed-dependent.** On the re-run D05 S2 fits at
**R² = −0.106** (still negative — no usable curve) and the recommender answers **140**, i.e. it *held* the step
rather than widening it to 240. The specific 4×→7× over-reach in the evidence above did not recur.

The code gap this entry names is untouched, so the entry stands: `Recommend` still has no fit-quality gate, and
whether a degenerate fit happens to hold or to widen is left to the noise realization. But the headline number
is not the typical case.

**Found while re-measuring — a fourth harness-calibration bug.** That same D05 S2 run is scored
`converged: true`, `stoppedReason: "converged (round applied nothing)"`, with `finalStepSize 140` against
`stepBehavioral 35` — four times too wide. Assertion A3 correctly FAILs it (`final step 140 outside [21,56]`),
but the convergence flag reads PASS. A degenerate fit produces a no-op recommendation, and the loop reads
"nothing changed" as "converged". This inflates convergence counts on exactly the runs that are most broken,
and belongs with the three calibration bugs already recorded in
[`docs/synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md).

**Next step.** Gate the recommendation on fit quality. The R² is already in hand at the call site; a fit below
some floor should either hold the current step or shrink it, never widen it. Reproduce with
`synth-validate --datasets D05_tec140_1000mm --scenarios S2 --max-rounds 4`. Separately: make "converged" require
being inside the tolerance band, not merely unchanged.

**Wave 7 bounds the MAGNITUDE and leaves the gap (2026-08-06).**
[F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see)'s
`W_detect` is measured and can never exceed what the sweep sampled, so `min(W_3x, W_detect)` bounds the 4×→7×
over-reach on any run whose star counts are populated. That is not this entry's fix: at R² = −0.223 the
recommender still ANSWERS rather than recognising it has no curve, and a degenerate fit that happens to be bounded
is still a degenerate fit being trusted. **The fit-quality gate, and the "converged means unchanged" half, are
both still owed.**

> ### THE MIRROR IS NOW MEASURED, AND IT SETTLES WHAT THE OWED GATE MUST DO (2026-08-12, wave 23, RULE V23)
>
> This entry's next step reads: *"a fit below some floor should either hold the current step or shrink it, never
> widen it."* **Wave 23 measures the too-NARROW end, where holding is the harm** — three cells whose degenerate
> fit made the recommender hold, stopping the loop after one round of four at **3-4.5x below** the correct step
> (`D01_ultrawide_40mm`/S1 2 vs 9, `D02_rich_135mm`/S1 2 vs 6 at **R² = −0.2741**, `D03_redcat_250mm`/S1 4 vs 16).
> Full mechanism in [F34](#f34--synth-validate-scored-a-stalled-run-as-converged-at-a-step-4-outside-the-band-its-own-assertion-failed-it-on)
> and `docs/synthetic-af-bank-followups-wave23-results.md` §4.1.
>
> **So "hold" is not a safe default; it is only safe at this entry's end of the range.** The gate that is still
> owed must be **directional** — widen when the sweep sampled too little curve, shrink when it sampled too much —
> and the discriminator is already in the report as `stepRecommendation.sampledHfrRange`. A gate written from this
> entry alone would have hard-coded the wrong half.
>
> **And this entry's `Recommend`-has-no-fit-quality-gate claim is confirmed in source at wave 23's binary:**
> `StepSizeRecommender.Recommend` still routes every unusable fit to `Degenerate(currentStepSize, …)`
> (`StepSizeRecommender.cs:410-416`) with no quality branch. There is also a **structural asymmetry** worth
> recording here, since this entry is the one about direction: growth is capped at **1.714x per round**
> (`MaxHalfWidthSampledHalfSpanMultiple = 1.5`, `:185`, with `PointsPerSide = 3.5`), while **shrinking has no
> equivalent clamp on the geometric path** — the only floor is `ClampStep`'s `if (step < 1) step = 1`. The code's
> own comment at `:191-196` says so: *"It had no bound on how far one run may NARROW it, and that asymmetry is
> what lets a run whose only star-bearing frames are the innermost ones collapse the sweep in a single step."*
>
> The *"converged means unchanged"* half of this entry's next step was discharged by
> [F34](#f34--synth-validate-scored-a-stalled-run-as-converged-at-a-step-4-outside-the-band-its-own-assertion-failed-it-on)
> in wave 2 and is **not** re-opened; what wave 23 re-opens is the **stop** that fix shipped alongside it.

> ### THE OWED GATE IS NOW IMPLEMENTABLE, AND THE OBVIOUS VERSION OF IT WOULD BE WRONG (2026-08-12, wave 24, P1 + RULE D24)
>
> **What was blocking it, and it was not nerve.** Wave 23's Bridge A says *"the gate must be on
> `sampledHfrRange`, not on R²."* **`SampledHfrRange` was assigned ONLY on the non-degenerate path**
> (`StepSizeRecommender.cs:295`). On the `:252` half-width exit `bestFit.Outputs` **is** present and measurable
> and was thrown away. **The gate's decision variable was `NaN` on exactly the branch the gate must decide**, so
> the bridge was not implementable as written, at any price.
>
> **Wave 24 shipped the instrument** (P1, **shipping plugin code**): `Degenerate(...)` now takes a reason and sets
> `DegenerateReason` on all three exits — `"no-fit"` (`:233`), `"non-finite-vertex"` (`:239`),
> `"half-width-unresolved"` (`:252`) — and measures `SampledHfrRange = MeasureSampledHfrRange(bestFit)` whenever
> `bestFit != null`. Both are mirrored into the report and surfaced in the wizard's step block. **P1 changes no
> recommended step size**: `G24-P3b` pinned the eight gate values as exact integers and returned **8 of 8**.
>
> **The census it enables — RULE D24, `D-REPORTED`, NO BAR by pre-registration:**
>
> ```
> dataset              sc  rnd  degenerateReason       sampledHfr   R^2        bootstrapStep
> D02_rich_135mm       S1  0    half-width-unresolved  1.2216       -0.2741    2
> D03_redcat_250mm     S1  0    half-width-unresolved  1.0799        0.9835    4
> wide-end (S2) degenerate rounds: 0     narrow-end (S1/S4) degenerate rounds: 2
> ```
>
> **Three things wave 25 must not get wrong, all measured:**
>
> 1. **A gate keyed on R² MISSES `D03`.** `D03_redcat_250mm`/S1 is a **degenerate** recommendation at
>    **R² = 0.9835**; `D02` is degenerate at **−0.2741**. Same exit, R² 1.26 apart, one of them excellent. This
>    corroborates [F21](#f21--stepsizerecommenders-half-width-is-not-stable-against-noise-even-on-a-perfect-fit)'s
>    *"R² measures fit to the SAMPLED points and says nothing about whether the vertex is identifiable"* on a
>    second, independent population. `sampledHfrRange` separates them; R² does not.
> 2. **Only ONE of the three exits fires in the field** — `half-width-unresolved`, **2 of 2**. `no-fit` and
>    `non-finite-vertex` are **0**. The schema invariant holds **23 of 23**: `halfWidth == "NaN"` **iff**
>    `degenerateReason` is non-null, so there is **no fourth degenerate path** P1 failed to label.
> 3. **The WIDE end is EMPTY.** Seven S2 cells (step ×4, this entry's own direction) produced **zero** degenerate
>    rounds. **Wave 25's directional threshold will be one-sided and must say so in writing before the data** —
>    and this entry's own wide-end example, `D05_tec140_1000mm`/S2, is **published**, so it cannot be used blind
>    either.
> Reproduce: `/mnt/d/hf_w24/d24_score.txt`; `/mnt/d/hf_w24/after/D0{1,2,3}_*__S1/synth_validate_report.json`;
> `docs/synthetic-af-bank-followups-wave24-results.md` §5.

> ### THE NARROW HALF OF THE OWED GATE IS SHIPPED. THE WIDE HALF IS STILL UNMEASURED, NOT UNTRIGGERED (2026-08-13, wave 25, P4)
>
> **What shipped.** Wave 25's P4 is the directional gate this entry has been asking for, keyed on
> `sampledHfrRange` exactly as wave 23 and wave 24 specified, and **one-sided by construction rather than by
> declaration**: it is a `Math.Max` against an existing ceiling, so it has no code path that lowers a
> recommendation. Full mechanism in [F81](#f81--maxhalfwidthsampledhalfspanmultiple-has-been-a-ceiling-with-no-floor-and-the-half-width-unresolved-exit-returned-before-the-ceiling-was-consulted-at-all)
> and `docs/synthetic-af-bank-followups-wave25-results.md` §10.1.
>
> **What it covers.** A sweep whose own HFRs prove the 3× band was never reached now gets the widest step the
> data supports (`W = 1.5 × half-span`) instead of holding — on **both** the under-reaching-fit path and the
> `half-width-unresolved` exit that previously returned before the bound was consulted at all.
>
> **What it does NOT cover, stated so nobody reads more into it:**
>
> | | |
> |---|---|
> | **the wide end — this entry's own direction** | **UNMEASURED, not untriggered.** No clause in wave 25 touches it. `D05_tec140_1000mm`/S2 remains published and therefore unusable blind, and wave 24's S2 census found **0 of 7** degenerate rounds, so the wide end has no field instance to fix against |
> | **the `no-fit` and `non-finite-vertex` exits** | **deliberately not floored.** `no-fit` has no fit object and so no sampled span; `non-finite-vertex` has no origin to measure an offset from. Wave 24's `D24-B` measured **0** field occurrences of either, so the limit costs nothing ever observed, and a unit test pins it |
> | **a fit-quality gate on R²** | still absent, still correctly absent. `D01` stalls at `R² = 0.99999999999994` and `D03` is degenerate at `R² = 0.9835`; an R²-keyed gate misses both |
> | **an efficacy RATE on a blind population** | **not measurable on this bank.** Wave 25's blind arm returned `W-UNEXERCISED`: **0 of 13** paired blind S1 cells engaged the floor, because all 13 already converged. **The pathology is rare on the synthetic bank at S1** — and *only* there: on the eight `optimize` gate landings the floor moved **3 of 8**, including `mccomiskey`, a **real-bank** dataset. A mechanism that is 0 of 13 in one population and 3 of 8 in another is not rare in general, it is rare *here* |
>
> **What the blind arm did buy: do-no-harm evidence.** `RULE N25` over all 18 paired cells returned
> `N-PRESERVED` — round-0 inertness **15 of 15**, assertion `A4` **18 of 18**, convergence lost **0**, distance
> REGRESSED **0**. Both 100 % clauses had answers **predicted from the code before the arm ran**, so a failure
> would have been unambiguous evidence that the shipped code was not the designed code.
> Reproduce: `docs/synthetic-af-bank-followups-wave25-results.md` §4, §5, §10.1;
> `/mnt/d/hf_w25/{before,after}/*__S1/synth_validate_report.json`; `/mnt/d/hf_w25/p3c_score.txt`.

### F81 — `MaxHalfWidthSampledHalfSpanMultiple` has been a ceiling with no floor, and the `half-width-unresolved` exit returned before the ceiling was consulted at all
**Status:** **Done** (2026-08-13, wave 25, P4 — shipping plugin code, user-visible via the wizard's step block) ·
found 2026-08-12 designing wave 25 from [F34](#f34--synth-validate-scored-a-stalled-run-as-converged-at-a-step-4-outside-the-band-its-own-assertion-failed-it-on)'s
and [F25](#f25--from-a-far-too-wide-sweep-the-step-recommender-widens-it-further-instead-of-recovering)'s stall cells

**The stall was never "the fit was bad", and no existing entry owned the real cause.** `StepSizeRecommender`
sizes the step from the half-width `W` of the band where fitted HFR climbs to `HfrThresholdMultiple = 3.0×` its
minimum, and it has always bounded how far it will trust the model **past** the data:
`maxHalfWidth = MaxHalfWidthSampledHalfSpanMultiple (1.5) × half-span`. **There was no mirror.** Two consequences,
one variable, two code paths:

| mechanism | cell | what happened |
|---|---|---|
| **the fit UNDER-reaches and nothing binds** | `D01_ultrawide_40mm`/S1 | `halfWidth = 6.0877` against a cap boundary of 12.0, so `if (halfWidth > maxHalfWidth)` is **false** and the cap — an **upper** bound — never fires. A hyperbola fitted to a 1.46× slice of curve interpolates that slice perfectly (**`R² = 0.99999999999994`**) and gets its asymptote badly wrong, so the extrapolated 3× crossing lands *inside* the sampled span. Recommendation: **hold 2**, against a truth of 8–9 |
| **the exit that skips the bound entirely** | `D02_rich_135mm`/S1, `D03_redcat_250mm`/S1 | `FindHalfWidth` returns `NaN` on both sides, so `Recommend` takes the `half-width-unresolved` early return **before `maxHalfWidth` is consulted at all**. The recommendation is the caller's own current step, **held** — which guarantees the next sweep is exactly as narrow as the one that just failed |

**All five S1 cells whose `stepRecommendation` was published have `sampledHfrRange` far below 3.0 — every one of
them failed to sample the band the step is sized from. Two converge and three stall, and the difference is
entirely whether the CAP happened to bind.** `D11`/S1 and `D08`/S1 converge because their fits *over*-reached, so
`W` became `1.5 × half-span` **exactly** — 1.5 × 56 = 84, 1.5 × 96 = 144, 1.5 × 84 = 126, 1.5 × 144 = 216, four
for four against the published reports. **On those cells the cap is not a safety net; it IS the convergence
mechanism.** The three stall cells simply never entered it.

**The asymmetry was already named in this very file, about the other bound.** `MinHalfWidthSampledHalfSpanMultiple`'s
doc comment reads: *"The recommender has always bounded how far one run may WIDEN the sweep … It had no bound on
how far one run may NARROW it, and that asymmetry is what lets a run … collapse the sweep in a single step."*
Wave 7 fixed that asymmetry for the **detectability** bound. **The identical asymmetry in the band bound went
unfixed for eighteen months, and it is what `D01` fell through.**

**Fixed (wave 25, P4), and not one threshold was chosen by looking at data.** `3.0` is `HfrThresholdMultiple`,
the constant that *defines* what the step is sized from; `1.5` is `MaxHalfWidthSampledHalfSpanMultiple`, the
constant that already governs the majority path. Both predate this series. **A threshold that cannot be moved by
the data cannot be contaminated by it**, which is why the [F14](#f14--astrodet-is-frameless)/`S16`/`D20`
harvesting fence is not engaged: nothing here was harvested, the bar was read off the product's own definition
of the quantity.

```csharp
/// True only when the sweep's HFR dynamic range was MEASURED and is below the band the step is sized from.
/// NaN means "could not look" and must NOT engage the floor.
private static bool BandDemonstrablyUnsampled(double sampledHfrRange) =>
    double.IsFinite(sampledHfrRange) && sampledHfrRange < HfrThresholdMultiple;
```

- **P4a** — after the cap block: `BandDemonstrablyUnsampled(...) && maxHalfWidth > 0 && halfWidth < maxHalfWidth`
  → `halfWidth = maxHalfWidth`, `wasBandFloored = true`.
- **P4b** — at the `half-width-unresolved` return, when the band is demonstrably unsampled, **do not take the
  degenerate exit**: floor, leave `DegenerateReason` **null**, continue down the ordinary path.
- **The `NaN` guard is the most important line.** `x < 3.0` is already false for `NaN`, but writing it that way
  makes the safety depend on IEEE-754 trivia. [F79](#f79--a-single-non-ascii-byte-in-a-redirected-log-makes-grep-report-zero-matches-for-strings-elsewhere-in-the-file)'s
  defect shape is a check that fails **closed to a value** instead of reporting that it could not look, and this
  is its mirror.
- **`WasCapped` is NOT overloaded**, deliberately. The floor branch never writes it and is entered only when
  `halfWidth < maxHalfWidth` — exactly when the cap branch was not — so the two are **mutually exclusive by
  construction**, and the harness's `A4` assertion (which reads `WasCapped` alone) is provably untouched at
  round 0. Measured: **18 of 18** paired cells bit-identical.
- **A floored recommendation is NOT degenerate**, and that matters for the UI. `IsDegenerate` means *"this is a
  HELD value, not a measurement"*; a floored recommendation **is** a measurement of the sweep — its own HFRs
  prove the crossing lies beyond everything sampled. `D24-A`'s schema invariant survives: `halfWidth == NaN`
  **iff** `degenerateReason != null` still holds, because floored rounds carry a finite half-width **and** a null
  reason.
- **New observable** ([F76](#f76--the-optimizer-wizard-opens-with-its-footer-below-the-bottom-of-the-screen-so-accept-is-unreachable-until-the-window-is-moved)'s
  *"a fix that cannot report whether it engaged is not finished"*): `WasBandFloored` on the recommendation, on
  the wizard's `OptimizationSummary`, and on the report snapshot. **User-visible:** `StepSizeText`'s "partial
  step" wording is now gated on `StepSizeWasCapped || StepSizeWasBandFloored`; no new XAML row.

**Scope limit, declared rather than overlooked:** the `no-fit` and `non-finite-vertex` exits are **not** floored
(`no-fit` has no fit object and so no sampled span; `non-finite-vertex` has no origin to measure from). Wave 24's
`D24-B` measured **0** field occurrences of either, so the limit costs nothing ever observed, and a unit test
pins it.

**Measured, under rules fixed and committed before the data:**

| | |
|---|---|
| **gate** — `optimize` `FinalJ` bit-identical to K8, 8 of 8 at sixteen digits | the change contributes nothing to the objective, as declared under [F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none) |
| **`G25-P3c`, the directional clause** | **3 WIDENED, 5 UNCHANGED, 0 NARROWED** on eight real gate landings — `D18` 18→26, `D20` 19→26, **`mccomiskey` 31→34 (a REAL-bank dataset)**. `NARROWED ≥ 1` was pre-registered as proof the shipped code was not the designed code. It did not occur |
| **the three stall cells** | `D02` 2→3→**5** (truth 6) and `D03` 4→7→**12** (truth 16) both **converge**, reproducing the design's per-round arithmetic **exactly**. `D01` moves 2→3 and **still stalls** — see [F82](#f82--the-half-width-floor-is-not-sticky-across-rounds-and-the-cap-recomputed-from-a-shrunken-fit-pulls-it-back-down) |
| **do-no-harm** (`RULE N25`, all 18 paired S1 cells) | **`N-PRESERVED`**: round-0 inertness **15 of 15**, `A4` **18 of 18**, convergence lost **0**, distance REGRESSED **0** |
| **blind efficacy** | **none measurable.** `RULE W25` = `W-UNEXERCISED`, **0 of 13** blind cells floored, because all 13 already converged |

**The blast radius is wider than the three named cells, and this was recorded BEFORE the data.** The rule fires on
*any* sweep with `sampledHfrRange < 3` whose fitted half-width lands below `1.5 ×` the sampled half-span. For a
standard hyperbola of edge ratio `e`, `halfWidth / halfSpan = sqrt(8 / (e² − 1))`, so the floor engages across
**`e` ∈ (~2.135, 3.0)**: at `e = 2.5` a **1.215×** widening, just below `e = 3` a **1.5×** widening, at
`e = 3.001` nothing — **a discontinuity at the threshold that moves a NUMBER.** A high `WasBandFloored` count is
therefore **not** evidence of over-reach and a low one is **not** evidence of failure to engage; the do-no-harm
rule is what decides harm. **The behaviour is convergent, like the cap**: the next, deeper sweep clears 3× and
the floor stops binding — it is a widening that switches itself off.

**One thing this turned up about the existing tests.** Three rows of the parametric recommender test
(`e = 2.19, 2.30, 2.50`) now receive a floored step **and still pass**, because they assert only `WasCapped` and
legality. **An existing test survived a real behavioural change, so it was never a control on that quantity.**
Reproduce: `StepSizeRecommender.cs` (the `BandDemonstrablyUnsampled` guard, the P4b branch at the
`half-width-unresolved` exit, the P4a block after the cap); `/mnt/d/hf_w25/p3c_score.txt`;
`/mnt/d/hf_w25/{before,after}/D0{1,2,3}_*__S1/synth_validate_report.json`;
`docs/synthetic-af-bank-followups-wave25-results.md` §3.1, §7, §10.1.

### F82 — The half-width floor is not sticky across rounds, and the cap recomputed from a shrunken fit pulls it back down
**Status:** Open (diagnosed to a line, two candidate fixes, priced) · found 2026-08-13, wave 25, on the one
labelled control that missed its pre-registered bar · **SCOPE AND GENERALITY MEASURED, wave 29 — see below**

> #### AMENDMENT, wave 29, 2026-08-13 — the fix choice stands; the scope and the generality are now measured
>
> **1. It IS a product defect, on a real product path.** Wave 29's own pre-registration argued the opposite —
> that no caller had anywhere to put a cross-round half-width, so the fix would reach no user — and **its own
> rule refuted it**: `RULE W29-S` = **`S-CARRIER-EXISTS`**. The design's caller table named two of
> `BuildSummaryAsync`'s **five** callers; **`CaptureNewSweepAsync`** (`StarDetectionOptimizerWizardVM.cs:5012`)
> takes a **fresh sweep** via `RunLiveAttemptAsync`, calls `BuildSummaryAsync` at `:5098`, and carries the
> previous round's recommended geometry forward in `recaptureStepSize`. So `SearchSpan` **can** change between
> wizard rounds and the defect **can** occur there.
>
> **2. But it is far rarer than this entry implies.** `RULE W29-R` ran wave 26's own reversal condition, which
> three waves had quoted and none had executed: over **18 addressable paired S1 cells**, the
> shrink-while-widening transition occurs on **`c = 1`** — and that one cell is `D01`, which is burned.
> **`c_blind = 0` over the fifteen cells that were blind.** See [F97](#f97).
>
> **3. The reversal condition is NOT met, so fix (1) stands — now on evidence rather than on caution.**
> Wave 29 deliberately did **not** flip the choice, and did not choose the third candidate it registered:
> choosing a fix in the same wave that discovers a new reason to prefer it is the [F14](#f14) / `RULE D20`
> fence in a new costume.
>
> **4. The bar this entry is scored against is produced by the code under test** — `SynthValidateRunner
> .ComputeStepBehavioral:1262` iterates `StepSizeRecommender.Recommend` to a fixed point. That is
> [F96](#f96), and it is why `stepBehavioral` moved 8.0 → 9.0 across wave 25's two arms while staying
> identical on the other seventeen cells: wave 25 changed `Recommend`, so it moved the bar as well as the
> measurement. **Any future scoring of this entry must say which side of that loop it is standing on.**
>
> **5. The price in the handoff (~45 m) is for the HARNESS change.** The product carrier needs new persisted
> session state surviving between wizard sessions, with the `Continue`/`Re-optimize` paths excluded where it is
> inert, plus migration and the question of whether it is an option (and therefore owes a control in
> `Resources/OptionsDataTemplates.xaml`). That is a ~2–4 h band and nobody has priced it.

[F81](#f81--maxhalfwidthsampledhalfspanmultiple-has-been-a-ceiling-with-no-floor-and-the-half-width-unresolved-exit-returned-before-the-ceiling-was-consulted-at-all)'s
floor engaged on **all three** published stall cells at round 0 and handed off to the cap at round 1 exactly as
designed. **On two of three it converges. On the third the half-width SHRANK between rounds and the stall
survived.**

| cell | r0 | r1 | outcome |
|---|---|---|---|
| `D02_rich_135mm`/S1 | **floored**, `halfWidth` **12.0**, step 3 | **capped**, `halfWidth` **18.0**, step 5 | converged, final **5** (truth 6) |
| `D03_redcat_250mm`/S1 | **floored**, `halfWidth` **24.0**, step 7 | **capped**, `halfWidth` **42.0**, step 12 | converged, final **12** (truth 16) |
| `D01_ultrawide_40mm`/S1 | **floored**, `halfWidth` **12.0**, step 3 | **capped**, `halfWidth` **9.0**, step 3 | **STALLED**, final **3** (truth 9) |

`sampledHfrRange` stayed below the band on both of `D01`'s rounds (`1.4628`, `1.4587`), so the band was never
reached and nothing else could rescue it.

**The defect, stated narrowly.** The floor bounds the half-width from below *within* a round, against that
round's own bound. It does **not** prevent the next round from re-deriving a **smaller** bound. **The cap and the
floor are computed from the same quantity, so a shrinking bound drags both down together** — and because the cap
is applied as an **upper** bound after the floor's value has been forgotten, round 1 recomputed `9.0` where round
0 had already established `12.0`.

**The mechanism is one level deeper than "the sweep narrowed", and this changes which fix is right.**
`maxHalfWidth = MaxHalfWidthSampledHalfSpanMultiple × 0.5 × SearchSpan(bestFit)`, and `SearchSpan` is
`max(x) − min(x)` over **`bestFit.Inputs` — the points that actually entered the fit**, not over the positions
the sweep requested:

| `D01`/S1 | r0 | r1 |
|---|---|---|
| **requested** sweep | centre 6000, step **2**, ±4 → span **16** | centre 6008, step **3**, ±4 → span **24** |
| **fitted** span (`SearchSpan`) | **16** → `maxHalfWidth` **12.0** | **12** → `maxHalfWidth` **9.0** |
| `worstFrameStarCount` | **0** | **0** |
| `R²` | 0.99999999999994 | **0.7603** (`A6` FAILs: *"below 0.8"*) |

**The round-1 sweep was requested WIDER — 16 → 24 focuser units — and its FITTED span NARROWED, 16 → 12**,
because on a 40 mm ultrawide the outer frames of a widened sweep stopped yielding usable HFR points. **So the
bound that is supposed to reward widening penalises it on exactly the star-poor fields that most need it.**

**A second instrument measures the same divergence from the other side.** The harness's `A4` assertion computes
the cap boundary from the **requested** sweep and reports
`"WasCapped=True but truth predicts False (halfWidth=10.4 vs cap boundary 18, ratio 0.58)"` — **18** against the
product's **9**. And the corroboration identity every floored round satisfies
(`halfWidth == 1.5 × offsetSteps × bootstrapStep`, 3 of 3 on the floored rounds) is **exactly the identity
`D01` r1 fails**: `1.5 × 4 × 3 = 18 ≠ 9.0`.

**Two candidate fixes. Pre-register which one before looking at anything:**

1. **Make the floor monotone across rounds** — carry the previous round's floored half-width forward as a lower
   bound on the next round's `maxHalfWidth`. Simple, fixes `D01` directly, and confined to the recommender's
   caller state.
2. **Make the bound robust to a fit that loses its outer points** — derive `SearchSpan` from the **requested**
   span (which is what the harness's own truth model already uses) rather than from the fitted inputs. Larger
   blast radius: it changes `maxHalfWidth` on every capped round, so it needs the full do-no-harm treatment.

**Do NOT re-run a full paired 20-cell arm to score this.** Wave 25 measured `W-UNEXERCISED` — 13 blind S1 cells
moved by exactly zero across a real product change — so the only cells that can move are the three published
controls plus whatever the 8-run `optimize` gate moves. **Price: ~45 m code + tests, plus a 3-cell targeted
re-run (~10 m) and the gate (~42 m).** A second full arm would buy a second `SAME 13` for ~2 h 40 m.

**Not a reason to doubt the ship.** `RULE N25` returned `N-PRESERVED` on its own pre-registered terms, no cell
regressed, and `D01` still moved 2 → 3 rather than staying put.

**One measurement caveat that belongs with this entry.** `terminal.stepBehavioral` — the truth the bar is scored
against — is **not arm-invariant on `D01`**: `8.0` (band 3.2) BEFORE, `9.0` (band 3.6) AFTER, while identical
across arms on the other 17 paired cells. `D01` misses under **either** truth (final 3 vs `[4.8, 11.2]` and
`[5.4, 14.4]`), so no verdict turns on it — but *"the truth is spec-derived and identical across arms"* is an
assumption this series can no longer make for free, and a rule that compares a measured value to
`stepBehavioral` should assert cross-arm equality first, per cell, and name any cell where it fails.
Reproduce: `/mnt/d/hf_w25/after/D01_ultrawide_40mm__S1/synth_validate_report.json` (rounds 0 and 1);
`StepSizeRecommender.SearchSpan`; `/mnt/d/hf_w25/CONTROLLER_DEVIATIONS.md` D10;
`docs/synthetic-af-bank-followups-wave25-results.md` §7.3.

### F26 — A stuck binning recommendation starves the step update indefinitely
**Status:** Open · found 2026-08-03 running scenarios S1/S6 on the synthetic AF bank

The wizard's update ordering applies **binning first** and defers the step by a round, on the sound reasoning
that SNRs are per-binned-pixel so changing binning invalidates the exposure and step measurements. But when the
binning recommendation is *persistently wrong* ([F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor)),
"defer the step" becomes "never update the step".

**Evidence.** `D08_c11_2800mm`, scenario S1 (bootstrap step 21, correct 82), four rounds, fits at R² = 0.999–1.000:

| round | step | binning recommendation | step applied? |
|---|---|---|---|
| 0 | 21 | 1 (expected 2) | no — binning first |
| 1 | 21 | 1 | no |
| 2 | 21 | 1 | no |
| 3 | 21 | 1 | no |

The step never moves off 21 — a quarter of the correct value — despite a near-perfect fit every round.
`D12_c14_585_afbin2` S6 shows the same pattern (35 → 60 → 60 → 74 against a target of 141). Both are datasets
whose true HFR sits near the 4.5 px binning boundary, i.e. exactly F22's population.

**Why it matters.** Two individually-defensible behaviours compose into a livelock: a measurement bias that
flips a threshold, plus an ordering rule that waits for that threshold to settle. The user sees the wizard
"recommending" the same wrong binning every round and never getting to the knob that actually matters. Neither
F22 nor the ordering rule looks broken on its own, which is why this needs its own entry.

**Re-measured 2026-08-03 (F23 wave 1) — the LIVELOCK does not reproduce; the deferral does.** Same harness,
same datasets, `--max-rounds 4 --max-evals 120`:

| run | recorded evidence | re-measured |
|---|---|---|
| D08 S1 | step held at 21 for **4 rounds**, target 82 | 21 → 21 → 36 → **62**, converged in 3 rounds |
| D12 S6 | 35 → 60 → 60 → 74, target 141 | 35 → 60 → 60 → 80, still not converged at round 4 |

The binning-first deferral is still visible and still costs a round — D08 applies step 21 twice, and D12
applies 60 twice — but the step then updates and D08 converges. The unbounded stall does not recur. The
difference is that the landed Sensitivity varied round to round here (16.7 / 15.7 / 10 on D08) rather than
sitting at the floor, so the binning recommendation settled instead of being persistently wrong — which is
consistent with F26 being downstream of [F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor)/F23
rather than an independent defect.

Also measured: with the F23 marginal-SNR term enabled, **D12 S6 converges** (3 rounds, final step 103) where
shipping does not (4 rounds, final 80, not converged). So the objective fix helps this loop even though it
fails its own precision gates.

> **The "fails its own precision gates" half is REFUTED, re-scored at `/5` (2026-08-05, wave 5).** This was the
> single clause [F36](#f36--which-pre-wave-2-entries-actually-rested-on-the-broken-precision-metric-audited-and-it-is-none-of-them)
> left open across F1–F8/F18/F21/F25/F26. Arm (a)'s landings scored with `golden eval` (which applies the `/5`
> `TruthProtection` repair), against the control arm `H_A` on the same three datasets — the three arm (a) was
> recorded as failing the ≥ 0.90 precision gate on:
>
> | dataset | control `H_A` | **arm (a)** | arm (a) as recorded at `/3` |
> |---|---|---|---|
> | `D09_c14_3800mm` | 0.958 | **1.000** | 0.451 |
> | `D12_c14_585_afbin2` | 1.000 | **1.000** | 0.653 |
> | `D15_cdk20_3454mm_e47` | 0.968 | **1.000** | 0.531 |
>
> **Arm (a) clears the gate on all three, and beats the control on two of them.** The gate failure was entirely
> an artifact of the pre-[F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)
> metric. What the term actually cost is **recall** — 0.983 → 0.932 on D09, 0.942 → 0.900 on D12, 0.965 → 0.917
> on D15 — the same inversion F31 found: both mechanisms were suppressing *real detections*, not junk.
>
> So F26's convergence result stands **and** its caveat does not: the marginal-SNR term helped this loop without
> failing any precision gate. It remains "won't fix as written" under [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)
> on F31's grounds — a precision defect one thirtieth the size of the artifact that hid it does not justify a
> term — and this clause is now closed rather than unverified. Reproduce: `D:\hf_w5\f26\run.sh`.

> ### THE LIVELOCK REPRODUCES, AND THE STATUS REVISION BELOW IS REVERTED (2026-08-12, wave 23, RULE V23)
>
> **The unbounded form is back, on the same dataset, the same scenario and the same numbers as this entry's own
> evidence table** — measured on the **thirteenth** binary at the **shipped** `--max-rounds 4`, inside a
> pre-registered rule with a 17-cell denominator (`docs/synthetic-af-bank-followups-wave23-results.md` §4.2):
>
> ```
> D08_c11_2800mm / S1        expected step 82, bootstrap 21 (= 0.25x)
> r0: bootstrap 21 -> recommended 36   binning factor 1   vertexHfr 4.455   hfr/3 = 1.485
> r1: bootstrap 21 -> recommended 36   binning factor 2   vertexHfr 4.661   hfr/3 = 1.554
> r2: bootstrap 21 -> recommended 36   binning factor 1   vertexHfr 4.149   hfr/3 = 1.383
> r3: bootstrap 21 -> recommended 36   binning factor 2   vertexHfr 4.750   hfr/3 = 1.583
> terminal: finalStepSize 21, converged=false, "reached --max-rounds (4)", A3 FAIL
> ```
>
> **Four rounds; the same recommendation computed four times and discarded four times; the bootstrap never leaves
> 21.** It is not a slow climb that ran out of budget — the growth cap permits 4× in three rounds
> (`log 4 / log 1.714 = 2.57`) and four were allowed. The control cell `D11`/`S1` in the same arm applies its
> recommendation normally (`r0` 14 → 24, `r1` bootstrap **24** → 41), so the loop is not broken in general.
>
> **The mechanism is confirmed in source and in data simultaneously.** `SynthValidateRunner.cs:829` branches on
> `binningDiffers`, and its `else` branch (`:836-848`) holds **both** exposure and step under the comment
> *"Binning first: apply ONLY the binning change this round and defer exposure (and step) by one round."* This
> dataset's `vertexHfr/3` straddles the **1.5** rounding boundary of `clamp(round(hfr/3), 1, 4)`, so the factor
> oscillates `1, 2, 1, 2`, `binningDiffers` is true on **every** round, and the deferral never expires. That is
> this entry's title and [F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor)'s
> population, both verbatim.
>
> **Why the 2026-08-03 re-measurement missed it, on its own explanation.** That run recorded *"the landed
> Sensitivity varied round to round here (16.7 / 15.7 / 10) rather than sitting at the floor, so the binning
> recommendation settled instead of being persistently wrong."* In wave 23 it does not settle, and round 2 of this
> cell carries `sensitivityAtFloor = true`. **That link is one round of four and is offered as consistent, not as
> established** — the oscillation across the 1.5 boundary is measured directly and needs no help from it.
>
> **A measurement gap this entry now owns.** `binningRecommendation` in the report carries only
> `{hasMeasurement, recommendedFactor, vertexHfr}` — there is **no `appliedFactor` and no `currentFactor`** — so
> the starvation must be *inferred* from the bootstrap never moving rather than *read*. Adding `appliedFactor` to
> the round record is ~2 lines and makes the mechanism checkable from the report alone.
>
> **Priced.** The guard below — bound the deferral at N = 2 consecutive rounds without an applied change — is
> **~10 lines and one test, ~30 m**, and `D08`/`S1` alone is a complete before/after in ~10 m. The
> pre-registrable bar: `roundsUsed <= 3` with `finalStepSize` inside `[49.2, 131.2]`. The `RecommendFromHfr`
> hysteresis that would dissolve the cause is a **separate, larger** change owing a coordinate-system re-baseline
> and must not be bundled with it.
> Reproduce: `/mnt/d/hf_w23/v23/D08_c11_2800mm__S1/synth_validate_report.json`; `/mnt/d/hf_w23/v23_score.txt`.

> ### SCOPE CORRECTION (2026-08-12, wave 24 pre-registration) — THE LIVELOCK IS IN THE HARNESS, NOT THE PRODUCT
>
> **This entry has said "The wizard applies binning first" since it was written, and that is FALSE.** It has been
> quoted forward across waves, and wave 23's controller repeated it — reporting the reproduction above as a
> *"live product defect restored to full severity"*, in the wave-23 results document and in PR #195. **Corrected
> here rather than quietly amended.**
>
> The deferral loop is **`TestApp/SynthValidateRunner.cs:828-834`** — `binningDiffers` and the
> `"deferring exposure/step this round"` reason. `grep` over the whole solution returns those identifiers in
> **that file only**, and the shipped plugin does not reference `SynthValidateRunner` at all. **NINA never loads
> it.**
>
> The product's recommendation path has **no round loop to livelock**:
> `StarDetectionOptimizerWizardVM.cs:4060-4085` builds **one** `OptimizationSummary` carrying
> `RecommendedStepSize` **and** `RunDetectionBinning`/`PersistedDetectionBinning` **together**, in one shot. The
> file contains **zero** occurrences of `deferring`, `binningDiffers`, `maxRounds` or `roundsUsed`.
>
> **What survives, and what does not:**
>
> - **Survives:** the reproduction is real, the retraction of the 2026-08-03 withdrawal stands, and the guard is
>   still worth shipping — because the harness is the instrument every step-size measurement in this series is
>   made on. `V23-G`'s S1 **13 of 17** was measured on a loop that **starves the recommender**, so the number is
>   partly a property of the instrument.
> - **Does NOT survive:** any claim that a user's autofocus run is affected by *this* mechanism. `D08`/`S1`'s
>   miss is a **harness artifact**. It must not be cited as evidence of a field defect.
> - **Unaffected:** the degenerate-fit finding (`D02_rich_135mm/S1`, `R^2 = -0.2741`) **is** product-relevant —
>   `StepSizeRecommender` is shipped plugin code, called at `:4062`.
>
> **The lesson, which is this register's own recurring one:** the entry named a component (*"the wizard"*) it had
> never opened. The check that settles it is two greps and costs a minute. **Name the file and the line, or do
> not name the component.**

> ### GUARDED (2026-08-12, wave 24, P2) — IT MEETS THIS ENTRY'S OWN BAR ON `D08`/S1, AND THE LIVELOCK IS RARER THAN THIS ENTRY IMPLIES
>
> **The scope correction above is unchanged and unaffected.** P2 is a **harness** change
> (`TestApp/SynthValidateRunner.cs` + `TestApp/SynthBank/BinningRevisitPolicy.cs`); it reaches no user, and
> nothing below should be read as a field bridge.
>
> **The guard that shipped is NOT this entry's next step as worded, and the reason is measured.** This entry says
> *"if the binning recommendation **has not changed the applied value** for N consecutive rounds, stop deferring."*
> **On the oscillating form wave 23 reproduced, the applied value changes every single round** — `2 -> 1 -> 2 -> 1` —
> so a counter keyed on *"the applied value has not changed"* **never fires** on the trace this entry is now cited
> for. That wording matches the **original 2026-08-03 evidence**, where the recommendation was stuck at `1` for
> four rounds; it does not match the oscillation. A consecutive-**deferral** counter (`N = 2`) would fire, but it
> also fires on a **legitimate monotone walk** (`1 -> 2 -> 3` is two honest deferrals, because no measurement at
> factor 3 exists yet).
>
> **What shipped is the REVISIT rule**, derived from the deferral's own stated justification (`:830-832`:
> *"changing binning invalidates the exposure measurement … this round just took"* — a justification that is
> **empty for a factor already measured**): *defer only when the recommended factor has not already been in
> effect during this scenario.* It changes behaviour **if and only if the recommendation has entered a cycle**,
> which is precisely the defect.
>
> **On `D08_c11_2800mm`/S1 — this entry's own dataset and scenario — it works, and it meets the bar this entry
> pre-registered:**
>
> | | BEFORE (B13, wave 23's binary) | AFTER (B14) |
> |---|---|---|
> | trajectory | `21 -> 21 -> 21 -> 21` | **`21 -> 36 -> 62`** |
> | `roundsUsed` | 4 | **3** |
> | `converged` | **false** | **true** |
> | `stoppedReason` | `reached --max-rounds (4)` | `converged (step 62 within the 32.8 tolerance band of step_behavioral 82)` |
> | `overallVerdict` | 2 | 1 |
>
> **This entry's priced bar was *"`roundsUsed <= 3` with `finalStepSize` inside `[49.2, 131.2]`."* Measured:
> `roundsUsed = 3`, `finalStepSize = 62`.** P2's engagement marker `applied.binningDeferralBoundReached` is
> **true on round 1**, exactly where the revisit occurs, with the reason
> *"binning 1 -> 2 (already measured at factor 2; not deferring)"* — [F76](#f76--the-optimizer-wizard-opens-with-its-footer-below-the-bottom-of-the-screen-so-accept-is-unreachable-until-the-window-is-moved)'s
> rule honoured: *a fix that cannot report whether it engaged is not finished.*
>
> **AND `D08`/S1 CANNOT CARRY A VERDICT, WHICH IS THE OTHER HALF.** Its four round records were read to *design*
> the fix, so it is a **labelled reproduction control excluded from every denominator**. The wave's blind
> population was **scenario S4 — never run in any prior wave, in no results document, in no register entry** —
> requested on all 20 datasets, 7 binary-resolved applicable.
>
> **RULE B24 returned `B-UNEXERCISED`: 0 of 7 BEFORE cells revisited a binning factor** (`factors seen=[1]` on
> every one), so the engagement marker fired **0 of 7** and do-no-harm read **7 of 7 identical** — exactly how P2
> is defined to behave with no revisit. The branch table, fixed before the data, calls this **NOT a pass and NOT a
> fail**.
>
> **So the honest status of this entry is: the livelock is CONDITIONAL and RARER than its own evidence suggests.**
> It did not occur once in a blind seven-dataset population selected to provoke binning mismatches, at the shipped
> `--max-rounds 4`. Wave 23 found a revisit in 1 cell of 38 from the other side. Whether it fires is decided by
> where a rig's vertex HFR sits relative to the `1.5` rounding boundary of `clamp(round(hfr/3), 1, 4)`
> ([F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor)),
> **not** by the deferral rule being generally wrong. **A future wave wanting the mechanism must select datasets
> whose vertex HFR straddles that boundary, not datasets with `expectedDetectionBinning == 2`.**
>
> **The measurement gap this entry opened is CLOSED.** It recorded that `binningRecommendation` carried no
> `appliedFactor` and no `currentFactor`, so starvation had to be *inferred* from the bootstrap never moving.
> Both fields now exist and are populated (P3), along with `applied.stepDeferredByBinning` and
> `applied.binningDeferralBoundReached`.
> Reproduce: `/mnt/d/hf_w24/before/D08_c11_2800mm__S1/synth_validate_report.json` against
> `/mnt/d/hf_w24/after/D08_c11_2800mm__S1/synth_validate_report.json`; `/mnt/d/hf_w24/b24_score.txt`;
> `docs/synthetic-af-bank-followups-wave24-results.md` §4.

**Status revision, and it is now itself revised.** The one-round deferral cost is real and worth the guard below.
The 2026-08-03 re-measurement withdrew the word *"indefinitely"* from this entry's title on the grounds that the
unbounded form did not reproduce; **wave 23 reproduces it, so the title stands as written and that withdrawal is
retracted.** The honest reading of the two measurements together: the livelock is **conditional, not
intermittent** — it holds for as long as the binning recommendation keeps oscillating, and whether it oscillates
is decided by where the rig's vertex HFR sits relative to the 1.5 boundary
([F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor)).

**Next step.** Bound the deferral: if the binning recommendation has not changed the applied value for N
consecutive rounds, stop deferring and let the step update proceed. Fixing F22 would also dissolve this, but the
livelock is worth guarding against independently — any future oscillating recommendation would reproduce it.

**Explicitly NOT covered by wave 7's F18 work (2026-08-06).** F18, F21, F25 and F26 are the same component read
four ways, and a passing bank must not be read as four entries closed. This one is the wizard's binning-first
update ORDERING, downstream of [F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor),
not the recommender's arithmetic. Untouched, and the guard above is still owed.

### F23 — ~~The optimizer objective has no precision term, so it trades precision away for marginal recall~~
**Status:** **Won't fix as written** (2026-08-03, wave 2 — evidence base void; the real effect is ~1/5 the size and the axis is recall, see F32/F33) · found 2026-08-02

The objective `J` rewards star count and fit quality. Nothing in it penalises a false positive — and
nothing could have, because until this bank existed precision was only ever a *lower bound* on real data
(`F11`). Given exact precision, the optimizer's landings are revealed to be a bad trade: it gains a few
points of recall and gives up **half** the precision.

**Evidence.** `bank-verify --nc-sweep 2,3,4 --opt-a --opt-b` over all 17 synthetic datasets
(`afbank-verify/3`, header pixel scale, 0 failed). C0 = stock defaults; A = `optimize --per-run`;
B = the same with donut detection forced on. recall@high / precision:

| dataset | C0@nc2 | A | B |
|---|---|---|---|
| D08_c11_2800mm | 1.000 / **0.959** | 0.990 / **0.748** | 1.000 / 0.781 |
| D09_c14_3800mm | 0.922 / **0.993** | 0.956 / **0.451** | 1.000 / 0.448 |
| D10_rc16_3250mm_sparse | 0.983 / **1.000** | 0.931 / **0.547** | 0.966 / 0.661 |
| D11_rc10_585_afbin2 | 0.887 / **0.986** | 0.850 / **0.732** | 0.917 / 0.627 |
| D12_c14_585_afbin2 | 0.897 / **0.952** | 0.897 / **0.653** | 0.879 / 0.506 |
| D15_cdk20_3454mm_e47 | 0.954 / **0.979** | 0.943 / **0.531** | 0.931 / 0.708 |
| D16_esprit550_ha3 | 0.886 / **0.985** | 0.908 / **0.744** | 0.739 / 0.983 |
| D17_cdk14_oiii5 | 1.000 / **0.942** | 1.000 / **0.465** | 1.000 / 0.567 |

C0's precision never drops below **0.942** on any of the 17 datasets. Config A drops as low as 0.451.
On D09 the optimizer bought +0.034 recall for −0.542 precision.

**Why it matters.** This is the wizard's headline output — the settings a user is invited to Accept. On
a long-focal-length rig it is currently recommending a configuration that roughly doubles the false-
positive rate. Those false positives then feed the autofocus fit and the sensor-model fit, so the cost
is not confined to a reported number. It also reframes the real-bank optimizer results: every prior "A
beat C0" conclusion was scored against a precision figure that could not see this.

**Wave 1 executed 2026-08-03 — BOTH candidate mechanisms measured, both REJECTED.** Plan:
[`plans/af-recommender-hardening-plan.md`](../plans/af-recommender-hardening-plan.md); full results:
[`docs/f23-objective-precision-term-results.md`](f23-objective-precision-term-results.md). Three arms, one
binary, arm selected by flag; scored by `bank-verify` config A against a same-session control:

| arm | precision < 0.90 | recall@high drop > 0.02 | σ_focus worse > 20% |
|---|---|---|---|
| H — unmodified HEAD (control) | 8/17 | — | — |
| b — hard floor on the searchable Sensitivity range | **9/17** | | |
| a — `SMarginalSnr` objective term | **7/17** | 3 | 3 |

**(b) is worse than doing nothing.** Forcing Sensitivity to 6 breaks four datasets that were healthy — D13
0.985 → 0.860, D14 0.948 → 0.739, D04 0.977 → 0.859 — while helping three. Restricting the *domain* does not
remove the incentive to buy star count; the search simply loosens other gates to win the stars back, admitting
junk through a different door. This is the strongest available argument that the fix belongs in the objective.

**(a) works where it can fire, and is structurally escapable.** Real gains (D09 0.451 → **0.944**, D17 0.465 →
0.735, D10 0.547 → 0.814) but no gate cleared. The reason is not a mis-set constant: the gate guarantees
`sensitivity >= PeakResponse × StarClippingMultiplier`, and **both are searchable curated axes**, so the
optimizer can lift the statistic's own lower bound above the floor and make the penalty unable to fire while
the false positives remain. Measured: D12 landed at `1.0 × 6.25 = 6.25` and D15 at `1.0 × 6.75 = 6.75`, both
just past the 6.0 floor, precision stranded at 0.659 and 0.587. A floor of 8 would be escaped at 8, so the
budgeted retune round was deliberately not spent.

**Also learned:** D08 lands at Sensitivity 8 — above any floor, term legitimately inert — with precision
0.748. So a floored Sensitivity is **not** the only source of false positives, and this entry's mechanism
section describes part of the problem, not all of it.

`MarginalSnrStrength` therefore ships at **0** (inert; J bit-identical to before). The implementation is kept,
tested and flag-selectable so the next attempt starts from a measured position.

> ## ⚠ THE EVIDENCE BASE ABOVE IS VOID (2026-08-03, wave 2 — re-measured at `afbank-verify/5`)
>
> Every precision figure in this entry came from a metric that charged a false positive for each real star the
> golden policy had dropped ([F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)).
> Re-measured on the same landings:
>
> | | as recorded above | re-measured at `/5` |
> |---|---|---|
> | C0@nc2 precision, worst of 17 | 0.942 | **1.000** |
> | Config A precision, worst of 17 | 0.451 (D09) | **0.910** (D10) |
> | Config A datasets below 0.90 | 6 | **0** |
> | D09 "bought +0.034 recall for −0.542 precision" | — | +0.034 recall for **−0.042** precision |
>
> **The headline claim — "it gains a few points of recall and gives up half the precision" — is false.** The
> detector's real false-positive rate on this bank is 0–9%, and the reading is a measurement rather than a
> saturated metric: the null control (the same detections translated with wraparound) scores 0.000–0.169, and
> `truthViolations` is 0 on all 51 config rows.
>
> **What survives.** The *direction* is real and lands on exactly the datasets named here — D10 0.910,
> D09 0.958, D17 0.959, D15 0.968, every one a long-focal-length rig landing at Sensitivity 0. That is roughly
> one fifth the size of the artifact that hid it. Far too small to justify an objective term; not zero either.
> Both wave-1 mechanisms are correctly rejected, but for a reason the entry does not give: they were suppressing
> **real detections**. On D09 the control arm found 510 stars at 97.8% true precision where the term-on arm
> found 231.
>
> **And a term could not have helped anyway.** See [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains):
> `J` already sits at 0.98–0.999 before the search starts, so any new term competes for an exhausted fourth
> decimal place. That is the better explanation for why mechanism (a) produced real precision movement and still
> cleared no gate.
>
> **Where the problem actually is.** [F33](#f33--the-synthetic-bank-does-not-reproduce-the-real-banks-optimizer-failure-mode):
> on the REAL bank the optimizer drives Sensitivity *up* to 31–50 and sheds 45% of recall at the median, the
> opposite sign of the synthetic behaviour this entry describes. Recall, not precision, is the axis with a
> defect on it.

**Next step (revised again).** Do not build a precision term. Read F32 and F33 first: the objective's scale is
exhausted, and the failure mode this entry describes does not transfer to real rigs. The "second false-positive
source at healthy Sensitivity" (D08 at 8 with 0.748, D16 at 2.5 with 0.744) also dissolves — both score **1.000**
at `/5`. Earlier framings retained below as the record.

**Next step (original).** Add a precision-like term to the objective. It cannot be true precision on real data
(that is the whole problem), but two proxies are already available: the golden-independent
false-positive *proxies* the detector already collects, and — for tuning and regression — this bank,
where precision is exact. Any change wants scoring against **both** banks, since the synthetic one can
now measure exactly the quantity the real one cannot. Related: [F4](#f4--the-objective-has-no-sensor-model-term)
is the same shape of gap (a term the objective omits), and [F11](#f11--precision-is-a-lower-bound-on-runs-whose-faint-tier-was-budget-truncated--re-run-these-with-more-montages) is why this went unseen.

### F24 — ~~Donut detection costs precision even where donuts exist, and badly where they do not~~ → it costs RECALL where it is not needed
**Status:** Open — **remaining step ANSWERED (wave 5): do NOT neutralize the master's two defaults**; the fix is
a condition on star size, which routes into [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains).
Previously **restated** (2026-08-03, wave 2 — the precision claim is refuted; a recall claim replaces it) · found 2026-08-02 on the synthetic AF bank

Config B (donut-aware detection forced on) reduced precision on **every** dataset where it was
measurable, including the datasets that genuinely have donuts, and most sharply on the ε=0 control that
has none.

**Evidence.** `D13_apo200_1800mm` is a 1800 mm **unobstructed** refractor — the design's donut control,
present precisely so "donut" and "long focal length" cannot be confounded. Its extreme-defocus PSFs are
filled discs, not annuli:

| dataset | ε | C0@nc2 precision | B precision |
|---|---|---|---|
| **D13_apo200_1800mm** | **0** | 0.962 | **0.653** |
| D14_cdk14_2563mm_e47 | 0.47 | 0.965 | 0.671 |
| D12_c14_585_afbin2 | 0.34 | 0.952 | 0.506 |
| D09_c14_3800mm | 0.34 | 0.993 | 0.448 |

Two further expectations the run overturned: the design assumed **C0 would be broken on donut datasets**
(as it is on the real bank's `Panos`/`mufti`/`LinwoodFocus`) — it is not, reaching recall 1.000 on D08
and D17 with precision 0.942–0.959. And `donutEffect` over the 17 A/B pairs is `donutHelpedAF: 5`,
`donutHurtSensor: 6` — no clear win.

**Why it matters.** Donut detection is a bootstrap input that gates nine optimizer axes, and there is
still no product signal that recommends it ([F1](#f1--the-donut-heuristic-misses-small-donuts) is about
the heuristic missing *small* donuts). This says the cost of enabling it wrongly is high and concrete,
which raises the stakes on getting that signal right.

> ## ⚠ REFUTED AND RESTATED (2026-08-03, wave 2)
>
> **The precision claim above is an artifact of the pre-[F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)
> metric and does not survive re-measurement.** This is the one wave-1 followup that was never re-measured,
> because the config-B prepass output no longer existed; it has now been re-derived and scored at
> `afbank-verify/5`.
>
> | dataset | ε | C0@nc2 | **B, as re-measured** | B, as this entry recorded it |
> |---|---|---|---|---|
> | **D13_apo200_1800mm** | **0** | 1.000 | **1.000** | 0.653 |
> | D14_cdk14_2563mm_e47 | 0.47 | 1.000 | **0.997** | 0.671 |
> | D12_c14_585_afbin2 | 0.34 | 1.000 | **0.997** | 0.506 |
> | D09_c14_3800mm | 0.34 | 1.000 | **1.000** | 0.448 |
>
> **Config B's precision never falls below 0.991 on any of the 17 datasets**, and on the ε=0 control it is a
> flat 1.000. There is no precision cost to find, so there is nothing for the "score the nine donut-gated axes"
> next step to attribute.

**What is actually true, measured at `/5`: donut detection costs RECALL where it is not needed, and buys a
large σ_focus improvement almost everywhere.** Config B against C0@nc2:

| dataset | ε | Δrecall@high | σ_focus C0 → B |
|---|---|---|---|
| D16_esprit550_ha3 | 0 | **−0.147** | 0.615 → 0.315 |
| D04_esprit_550mm | 0 | **−0.113** | 0.232 → 0.059 |
| D13_apo200_1800mm | 0 | −0.014 | **2.352 → 0.291** |
| D03_redcat_250mm | 0 | **+0.138** | 1.183 → 0.341 |
| D11_rc10_585_afbin2 | 0.47 | +0.030 | 0.101 → 0.444 |
| D09_c14_3800mm | 0.34 | +0.078 | 1.141 → 0.644 |

D13 is the sharpest reversal. This entry cited it as the case that proved donut detection fires on things that
are not donuts and does damage; re-measured, D13 keeps 1.000 precision, gives up 0.014 recall, and its AF fit
**improves eightfold** (σ_focus 2.352 → 0.291). That is [F5](#f5--donut-detection-halves-σ_focus-on-a-run-with-no-donuts)
happening again, not a defect. `donutEffect` over the 17 A/B pairs is now `donutHelpedAF: 8`,
`donutHurtSensor: 6` — still no clean win, but no longer the "costs precision everywhere" picture.

**Why it matters, restated.** Donut detection remains a bootstrap input gating nine optimizer axes with no
product signal recommending it ([F1](#f1--the-donut-heuristic-misses-small-donuts) is the heuristic missing
*small* donuts). The stakes on getting that signal right are unchanged — but the cost of enabling it wrongly is
**lost faint stars on a well-sampled refractor**, not admitted junk, and a fix aimed at the wrong one of those
would have been wasted.

**Next step.** Find what the recall loss on D16 and D04 is: both are unobstructed 550 mm refractors, so the
donut-gated relaxations should be inert on them and are not. Score the nine gated axes against ΔRECALL on those
two datasets. Note also that the two survivors of the original entry are untouched by all of this: C0 is *not*
broken on the synthetic donut datasets (unlike the real bank's `Panos`/`mufti`/`LinwoodFocus`), and there is
still no signal that recommends the master.

**Answered 2026-08-03 (wave 3): it is not any of the nine axes — it is what the MASTER TOGGLE turns on by
default.** Reading the two landings before running anything settled the framing: **the search moved none of the
nine.** D16 landed every one at its default; D04 moved only `DefocusDistortionSizeReference` 30 → 28.75, and
that one is provably inert (bit-identical recall). A per-axis ablation would have returned nine nulls.

Arms hold every parameter at defaults and vary only master-gated mechanisms. `golden eval`, ~40 s each,
precision **1.000** / FP **0** throughout:

| arm | D16 recall@high | D04 recall@high |
|---|---|---|
| master OFF | **0.821** (151/184) | **0.762** (16853/22109) |
| master ON | 0.793 | 0.723 |
| + structure boost 0 | **0.821** (all recovered) | 0.732 (23%) |
| + morph-close 1 | 0.799 (21%) | 0.758 (90%) |
| **+ both** | **0.821 (151/184)** | **0.762 (16853/22109)** |

Neutralizing both recovers the master-OFF recall **exactly — the same star counts, not merely the same ratio**,
on both datasets. The two mechanisms are:

1. **A silent +2 structure boost.** Master ON with `DefocusAwareStructure` OFF still applies
   `DonutDefaultStructureLayerBoost = 2` (`StarDetector.cs:610-615`), taking `StructureLayers` 4 → 6. More
   wavelet layers subtracted erases more small-star structure. Dominant on D16.
2. **A 5 px morph-close.** `DonutMorphCloseSize` **defaults to 5 and is neutral at 1**, so the master runs a
   morphological close that merges compact stars. Dominant on D04.

**Caveat:** these arms run at default shared params (NC 4.0), so −0.028 / −0.039 is the master's own cost, not
the −0.147 / −0.113 above (NC 2, against B's full landing). The master residue is a fifth to a third of it; the
rest is B's different landing on the *shared* axes.

**And the mechanism is [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains).**
Both are reachable by the search — `DonutMorphCloseSize` is a curated axis and `StructureLayerBoost` becomes
settable once `DefocusAwareStructure` flips on. The optimizer never neutralizes them because `J` does not pay
for recall. **This is not a missing knob; it is a saturated objective, measured on two specific rigs.**

**Remaining next step.** Consider defaulting the master's structure boost and morph-close to neutral on runs the
donut heuristic did not flag, or making the objective pay for recall. Both are behaviour changes, so per
[F33](#f33--the-synthetic-bank-does-not-reproduce-the-real-banks-optimizer-failure-mode) they must be scored on
**both** banks. Reproduce: `D:\hf_w3\run_f24_arms.sh` and `run_f24_arms2.sh`.

**ANSWERED 2026-08-05 (wave 5): do NOT default them to neutral. The wave-3 conclusion was drawn from two
datasets and does not generalize to twenty.** Five arms × all 20 synthetic datasets, `golden eval` at default
shared params with the master forced on, ~55 min total and **no code change** — the overrides already exist
(`GoldenEvalRunner.cs:461-479`). Precision is **1.000 on every arm and every dataset**.

**First, wave 3 reproduces exactly.** D16 masterOFF 0.821 → shipping 0.793, and `boost0` restores 0.821; D04
0.762 → 0.723, and `close1` restores 0.758. Neutralizing **both** recovers master-OFF recall on **19 of 20**
datasets (D07 exceeds it). The mechanism claim is confirmed, and now on the whole bank.

**But the master's two mechanisms are not a uniform cost — they are a rig-dependent trade:**

| direction | datasets | Δrecall (masterOFF − shipping) |
|---|---|---|
| master **HURTS** recall | 12 — D01–D05, D07, D10, D13, D16, D18, D19, D20 | +0.007 … **+0.067** |
| master **HELPS** recall | **3 — D06, D09, D14** | −0.005 … **−0.067** |
| no effect | 5 — D08, D11, D12, D15, D17 | 0.000 |

On all three of the datasets where it helps, `boost0` gives the gain back **exactly** (boost0 = masterOFF to
3 dp), so **the +2 structure boost is what buys it** and the morph-close is inert there. And the three are
`D06_sparse_1000mm`, `D09_c14_3800mm`, `D14_cdk14_2563mm_e47` — long focal length and sparse, i.e. **large
defocused stars**, which is precisely what a coarser wavelet residual exists to preserve. The story is coherent
in both directions: the boost saves big defocused stars from the subtraction and erases small-star structure, so
it helps where stars are large and hurts where they are small and well sampled.

**So the pre-registered criterion FAILS** (recall Δ ≥ −0.005 on *every* dataset): both-neutral costs
**−0.067 on D09**, −0.024 on D06 and −0.005 on D14. A blanket default change takes recall away from exactly the
rigs the feature was built for.

**What the fix actually is.** Not a default — a *condition*. The knob wants to depend on star size, and the
optimizer can already reach it (`DefocusAwareStructure` is a curated axis, and flipping it on makes
`StructureLayerBoost` settable, so `DefocusAwareStructure=true, boost=0` is a reachable neutral point). It never
goes there because `J` does not pay for recall — which is [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains),
and F32's constraint is the general form of this fix.

**Note the keep floor does NOT dissolve this.** The seed has the master OFF, and the master's own cost is
≤ 0.067 of recall — a candidate flipping it on stays feasible at any floor ≤ 0.9. F32 bounds catastrophic
shedding; it does not price a 4% one. The two entries are independent.

Reproduce: `D:\hf_w5\f24_arms.sh`, analysed by `D:\hf_w5\analyze_f24.py`.

> **The suspect D01/D02 rows of that table, resolved (wave 6).** `D02`'s frames turned out to be unaffected
> (bit-identical after the F44 fix), so its row never needed replacing. `D01`'s did, and re-running all five arms
> on the corrected field changes nothing that matters:
>
> | arm | D01 recall@high, old (truncated) | **D01, new (full field)** | TP, new | FP |
> |---|---|---|---|---|
> | `masterOFF` | 0.129 | **0.127** | 19039 | 0 |
> | `shipping` | 0.115 | 0.114 | 17636 | 0 |
> | `boost0` | 0.119 | 0.119 | 17988 | 0 |
> | `close1` | 0.126 | 0.125 | 19004 | 0 |
> | `both` | 0.129 | **0.127** | 19039 | 0 |
>
> The master still **hurts** on D01 (masterOFF − shipping = +0.014 old, +0.013 new), `both` still recovers
> master-OFF recall exactly, and precision is still 1.000 with zero false positives. The wave-5 verdict turned on
> D06/D09/D14 — none of which was ever suspect — and is untouched.

### F19 — The exposure recommendation is decided by the 20 brightest stars, so a rich field can never earn one
**Status:** **(a) refuted (wave 8) · (b) DONE (wave 8) · (c) RESOLVED "the floor stays", free, no re-render
(wave 9) · THE REMAINDER IS OPEN AGAIN (wave 10): wave 9's wing verdict was REFUTED by the population check it
recorded as owed, and is WITHDRAWN · AND THE SUCCESSOR IS REFUTED TOO (wave 11), before implementation, by
RULE W2 on the exposure ladder: `D16` at 2 s has an inner rejected fraction of EXACTLY 0.000, so the ratio is
INFINITE and fires where more exposure is measurably wrong. NO THRESHOLD SATISFIES RULE W.** The gate-floor
THEOREM stands; `WingRejectedFraction` AND `WingRejectedRatio` both stay as measurements with no verdict
attached ·
**THE REMAINDER IS CLOSED (wave 12): `WingRejectedExcess` = wing − inner is REFUTED on a fresh ladder it had not
seen, by W1 AND W2 AND W5, on three datasets independently and in both directions — and it is
ANTI-CORRELATED with what it exists to predict (Spearman ρ = −0.665 over 13 rungs). NO STATISTIC OVER GATE
REJECTIONS SEPARATES A STARVED WING FROM A DETECTOR THAT REJECTS NOISE EVERYWHERE.**
· found 2026-08-02 deriving expected-optimal exposures for the synthetic AF bank

`ExposureRecommender`'s `S_now` is the median, across non-recovery frames, of each frame's
**`NTarget`-th-brightest** accepted-star SNR, with `NTarget = 20`. Any reasonably wide field contains 20 stars
bright enough to sail past the gate no matter what filter is in front of them, so `SensitivityIsAtFloor` never
trips and no recommendation is ever offered.

**Evidence.** Deriving the exposure band for the synthetic bank from the recommender's own arithmetic (see
`docs/synthetic-af-bank-design.md`) produced the 0.5 s floor for **12 of 17** datasets. The clearest case is
`D16_esprit550_ha3` — a 550 mm refractor behind a **3 nm Hα** filter, passing roughly 64× less flux than
luminance. It still derives 0.5 s, because its 2.9° field carries 6835 on-frame stars and the 20th brightest is
magnitude ~9. The datasets that do demand a long exposure are the *narrow, sparse* ones — `D10` (0.28° field,
26 on-frame stars) derives 30 s — and they get there by having few bright stars, not by being photon-starved.

> **These two numbers have DRIFTED and are no longer what the derivation produces (re-measured 2026-08-06, wave 7).**
> `synth-bank --dry-run` over all 20 datasets on `develop` @ `761b9b1`:
>
> | claim as filed | measured today |
> |---|---|
> | the 0.5 s floor for **12 of 17** | **8 of 17** (`D01`–`D05`, `D07`, `D13`, `D14`) |
> | `D16_esprit550_ha3` derives **0.5 s** | **2 s** (band 1.079–3.102 s) |
>
> The cause is **not identified** — it lies somewhere between this entry's filing (2026-08-02) and now, and the
> candidates are [F44](#f44--the-synthetic-camera-queries-the-catalog-for-at-most-one-cell-so-a-wide-field-is-rendered-starless-outside-a-small-central-patch)'s
> catalog-query fix (which changes on-frame star counts on exactly the wide fields this claim rests on) and the
> derivation's own move to a median-across-sweep-frames statistic. Recorded rather than chased: the entry's
> ARGUMENT is unaffected — a 3 nm Hα field 64× down on flux earning 2 s is still "sized by field richness rather
> than by photon starvation" — but its specific numbers must not be quoted again without re-measuring. It also
> moves `D16` above the `MeaningfulExposureAboveFloorSeconds` gate, so it now qualifies for scenario S3, which it
> did not when this entry was written.

**Why it matters.** The knob is sized by field richness rather than by whether the stars the autofocus fit
actually depends on are above the noise. A long-focal-length rig on a bright field will report "exposure is not
the limit" while its faint-end stars — the ones that carry the wings of the V-curve — are still noise-dominated.
That is the same shape of gap as [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see):
a recommendation computed from one convenient statistic rather than from what the fit needs.

**Next step.** Decide deliberately whether `NTarget = 20` is the right population for this question. If autofocus
genuinely only needs 20 good stars, then the current behaviour is correct and this entry closes as "working as
intended" — but that should be a stated position, not an accident of which statistic was nearest to hand. If it
is not, the candidate is a faint-end statistic (e.g. the SNR at the star count the fit actually consumes) rather
than a fixed rank.

**DECIDED 2026-08-06 (wave 7): `NTarget = 20` STAYS, and here is the position.** Full reasoning in
[`docs/synthetic-af-bank-followups-wave7-design.md`](synthetic-af-bank-followups-wave7-design.md) §1. Three legs:

1. **The recommendation inverts the objective's own knee.** `S_stars` saturates at `nMedian >= NTarget`; past 20
   stars per frame the optimizer stops rewarding star count at all, so an exposure recommendation sized from a
   fainter population would be buying stars the objective is indifferent to. `Recommend` reads `NTarget` from the
   caller's `ObjectiveConstants` rather than a literal, so an objective retune moves both together.
2. **The named alternative is PINNED BY THE GATE and cannot work as a control signal.** "The SNR at the star count
   the fit actually consumes" is the faintest accepted star's SNR. A candidate is accepted iff its gate statistic
   exceeds `max(Sensitivity, StarDetector.InertSensitivityBound)`, and this advice is surfaced ONLY when
   `Sensitivity <= 1.0` (`OptimizationSummary.HasLowStarSignal`) — so the binding bound is the inert one, **1.5**
   at shipped defaults. A longer exposure does not raise that number; it admits MORE faint stars whose faintest
   again sits at the bound. `t·(TargetSensitivity/1.5)² = 44·t` therefore saturates `MaxExposureFactor = 4` on
   every run, forever, and never converges. The measured variable would be a fixed point of the actuator's own
   gate rather than a measurement of the sky.
3. **The half of this entry that is a real defect already has its own mechanism, and it post-dates the entry.**
   PR #159 shipped THREE verdicts, not one: `ExposureIsNotTheLimit`, `StarCountIsTheLimit` (a fixed ×2 PROBE with
   a stopping rule) and `StarFieldIsExhausted`. The "are there enough stars?" question is answered there, by a
   probe rather than a formula, precisely because whether more exposure reveals more stars depends on a luminosity
   function one sweep cannot measure. What is left of this entry — a rich field whose WING frames are starved — is
   a sweep-geometry defect owned by [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see),
   and the product already says so for the adjacent signal (`FlatRejectedCount` is documented as *"narrow the
   sweep, never expose longer"*).

**What can still refute this, pre-registered.** The position makes a claim about the world: *a field that already
carries 20 bright stars gains nothing material from more exposure*. **Arm E** tests it — `D16_esprit550_ha3` (this
entry's own poster child) and `D02_rich_135mm` rendered at 0.5/1/2/4/8 s, fixed detector settings, σ_focus per
rung. **Rule fixed in advance: if any longer rung improves σ_focus by more than 20% relative to the
derived-exposure rung on either dataset, this entry REOPENS.**

> ## THE RULE FIRED. THE "no change" POSITION ABOVE IS REFUTED, AND THIS ENTRY IS OPEN AGAIN (2026-08-06, wave 7).
>
> `D02_rich_135mm` — a **rich** wide field, 2746 stars still above the gate at the sweep's outermost frame, whose
> derived exposure is the **0.5 s clamp floor**:
>
> | exposure | σ_focus (current) | σ_focus (optimized) | J best | min stars/frame |
> |---|---|---|---|---|
> | **0.5 s (derived)** | 0.23211 | **0.10927** | 0.99633 | 23 |
> | 1 s | 0.19828 | 0.09125 (+16.5%) | 0.99720 | 34 |
> | 2 s | 0.18119 | 0.09482 (+13.2%) | 0.99708 | 58 |
> | 4 s | 0.16754 | **0.08182 (+25.1%)** | 0.99770 | 63 |
> | 8 s | 0.15238 | **0.06109 (+44.1%)** | 0.99853 | 37 |
>
> σ_focus improves **monotonically** on the current-settings column and by **44% at 8×** on the optimized one.
> A rich field demonstrably DOES gain from more exposure, and the shipped recommender says nothing about it.
>
> **And the diagnosis is sharper than this entry's own framing.** `SensitivityIsAtFloor` is **False at every
> rung** — the landed Sensitivity is 9–49, nowhere near the floor — so the exposure block is **never surfaced at
> all** on this dataset. The `NTarget = 20` statistic is not merely answering the wrong question here; on this
> population it is **never evaluated**, because the block's TRIGGER (`OptimizationSummary.HasLowStarSignal`,
> `Sensitivity <= 1.0`) gates it out first. The gap is upstream of the statistic.
>
> **The 20-star derivation is NOT uniformly wrong, which matters for the fix.** `D16_esprit550_ha3` — the
> narrowband case this entry opens with — has its σ_focus MINIMUM exactly at its derived 2 s (0.06423), and gets
> **worse** at 4 s (0.12034) and 8 s (0.14828). So the derivation nails D16 and under-serves D02. The
> distinguishing feature is that **D02's derived exposure sits at the `MinExposureSeconds = 0.5 s` clamp floor** —
> the arithmetic asked for less than the minimum, i.e. it saturated and carried no information — while D16's is a
> free solution inside the band.
>
> **The confound, declared before the arm ran and still true:** more exposure adds stars AND measures the existing
> ones better; this arm cannot separate them. It does not need to. The question was *"should a rich field ever
> earn an exposure recommendation?"* and the answer is **yes**.
>
> **Next step, and it is not "change NTarget".** (a) The trigger, not the statistic, is what silences this
> population — decide whether the exposure block should also fire when the landed Sensitivity is healthy but
> σ_focus is poor, or when the derived exposure saturated at a clamp. (b) A recommendation whose arithmetic hits
> either clamp should report that it saturated rather than reporting the clamp as an answer. (c) Re-check
> `MinExposureSeconds = 0.5 s`: on `D02` it is the binding constraint and it is 16× below what the fit wants.
> Reproduce: `D:\hf_w7\remaining_arms.sh` (Arm E), scored by `D:\hf_w7\score_armE.py`.

> ## AND (a) — "widen the trigger" — IS ALSO REFUTED, by its own pre-registered test (2026-08-06, wave 8).
>
> Widening the trigger only helps if the block, once surfaced, says something TRUE. Nobody had checked, because
> **the harness could not**: `OptimizationDiagnosticRunner` recorded `ExposureRecommendation` as `null` whenever
> the gate was healthy, reproducing the product's blind spot instead of measuring it. Wave 8 computes it for every
> run — **gate the DISPLAY, never the MEASUREMENT** — and re-ran wave 7's five exposure rungs, which are still on
> disk, so **no re-render**.
>
> **Rule R5, fixed before the arm ran:** *if at `D02`'s derived rung the recommendation reports
> `ExposureIsNotTheLimit`, or asks for less than 2× current, the statistic is wrong for this population and (a)
> does NOT ship as a trigger widening.*
>
> | dataset | rung | σ_focus | **S_now** | **raw ask** | recommended | `ExposureIsNotTheLimit` |
> |---|---|---|---|---|---|---|
> | `D02_rich_135mm` | **0.5 s (derived)** | 0.10927 | **991.8** | **0.000 s** | 0.50 s = current | **True** |
> | `D02_rich_135mm` | 1 s | 0.09125 | 1226.3 | 0.000 s | = current | True |
> | `D02_rich_135mm` | 2 s | 0.09482 | 1864.4 | 0.000 s | = current | True |
> | `D02_rich_135mm` | 4 s | 0.08182 | 2308.8 | 0.000 s | = current | True |
> | `D02_rich_135mm` | **8 s** | **0.06109** (−44.1 %) | 1894.9 | 0.000 s | = current | True |
>
> **THE RULE FIRES.** The recommendation says "your exposure is fine" at **every rung across a 16× range** over
> which σ_focus improves 44 %. It is not wrong at one operating point a wider trigger might have caught — it is
> wrong everywhere on the axis it is being asked about. **Surfacing it would have published that answer to more
> users and looked like a fix.**
>
> **The control passes on every rung, which is what makes the reading usable.** `D16_esprit550_ha3` at 0.5 s
> (below its derived value) correctly asks for **1.5 s**; at its derived 2 s — exactly where its σ_focus minimum
> sits (0.06423) — it asks for nothing more; and at 4 s and 8 s, where σ_focus is measurably worse (0.12034,
> 0.14828), it again asks for nothing more. **The recommender is right wherever it can see and blind where it
> cannot.**
>
> **And the magnitude finally has a number.** Against `TargetSensitivity = 10`, `S_now` on `D02` measures **991.8
> to 2308.8 — 100× to 231× the target** — so `RawSeconds = 0.5 × (10/991.8)² = 5 × 10⁻⁵ s`. The statistic is not
> slightly wrong on this population; it is wrong **by four orders of magnitude**, and it saturates HARDER as
> exposure rises (992 → 1226 → 1864 → 2309), so it can never converge toward asking for more.
>
> **(b) is DONE, and half of it needed no code.** Harness: `SynthBankDerivations` now records
> `exposureRawSeconds` and `exposureClamp` (`none`/`floor`/`ceiling`/`not-derived`) as **fields**, states the
> saturation in the definition text, and `synth-bank --dry-run` prints the saturated set — which is (c)'s work
> list. A field rather than only prose, for the reason [F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(a)
> added `DetectionBinningSource`: a reader diffs fields, nobody diffs a sentence. **Product: already correct** —
> `StarSignalCopy.DescribeExposureDerivation` already reports every clamp branch and names which bound bound it.
> Verified, not implemented.
>
> **What is left is not a trigger and not `NTarget`:** a statistic that can see the WING frames — σ_focus, or the
> wing-frame star count — which is a new mechanism rather than a repair to this one. **(c) is deferred to wave 9**
> with its re-render (moving `MinExposureSeconds` re-derives 8 datasets including `D14`, which wave 8's F39(b)
> adoption arm was simultaneously measuring).
> Reproduce: `D:\hf_w8\armX\arm_x.sh`.
>
> ### The saturated set, listed for the first time — and THIS ENTRY'S COUNT WAS LOW IN BOTH DIRECTIONS
>
> (b)'s reporting change produced it as a by-product. **13 of 20 datasets report a clamp rather than a derived
> value**, measured by `synth-bank --dry-run`:
>
> | clamp | datasets | the solve asked for → reported |
> |---|---|---|
> | **floor** (11) | `D02` **0.001 s → 0.5 s (500×)**, `D01`/`D03` 0.003, `D04`/`D18`/`D20` 0.004, `D19` 0.008, `D05` 0.014, `D14` 0.055, `D07` 0.066, `D13` 0.264 | all reported as 0.5 s |
> | **ceiling** (2) | **`D10_rc16_3250mm_sparse` 335.6 s → 30 s (11× SHORT)**, `D17_cdk14_oiii5` 40.4 s → 30 s | reported as 30 s |
>
> - This entry says *"the 0.5 s floor for 8 of 17"*. Measured: **11 at the floor** — those eight plus `D18`/`D19`/
>   `D20`, which the 17-dataset count never included.
> - **And 2 at the CEILING, which nobody had counted at all.** Running *out* of exposure is the same defect from
>   the other side and had no name until (b) printed it.
> - **`D02` makes this entry's case far more sharply than the entry does:** the arithmetic asks for **0.001 s**,
>   gets 0.5 s, and arm E measured that dataset improving 44 % of σ_focus at **8 s** — four orders of magnitude
>   above the ask.
> - **`D18`/`D19`/`D20` are floor-saturated**, which belongs in
>   [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
>   confirmation-arm write-up: its entire synthetic half sits on clamps rather than derived exposures. It does not
>   invalidate that arm (every φ arm sees the same frames) but it must be stated there.
>
> **This list IS (c)'s work list**, produced by (b) rather than by a separate investigation.

> ## THE REMAINDER IS FIXED (2026-08-07, wave 9), AND THE REASON BOTH EARLIER FIXES FAILED IS A THEOREM
>
> ### The gate floor theorem
>
> `StarDetector.InertSensitivityBound` proves every candidate reaching the Sensitivity gate satisfies
> `sensitivity > PeakResponse × EffectiveClipMultiplier`, and acceptance additionally requires it to exceed
> `StarDetectorParams.Sensitivity`. So **every accepted star's gate statistic strictly exceeds
> `EffectiveSensitivityGate = max(Sensitivity, InertSensitivityBound)`** — and therefore **ANY order statistic
> over accepted stars, at ANY rank, on ANY subset of frames, is bounded below by that gate.**
>
> **Corollary: when the optimizer lands a gate at or above `TargetSensitivity` (10), `ExposureIsNotTheLimit` is
> true BY CONSTRUCTION, whatever the sky contains.** `D02_rich_135mm` lands its effective gate at **16.7 / 50.0 /
> 34.3 / 10.0 / 14.0** across a 16× exposure ladder over which σ_focus improves **48 %**.
>
> **This is why both earlier fixes were refuted by their own pre-registered tests.** Wave 7's "change `NTarget`"
> and wave 8's "widen the trigger" each moved the rank or the display and left the POPULATION untouched. It is
> strictly stronger than wave 7's leg 2, which established only that the *faintest accepted star* is pinned by the
> gate; the theorem says every statistic over accepted stars is. **And it killed wave 9's own first candidate
> family before a line of it was implemented** — all four candidates were order statistics over accepted stars.
>
> ### What ships: the WING REJECTED FRACTION
>
> The candidates the gate **rejected** on the sweep's wing frames are the only population in a run that is not
> floored by the gate, and they are exactly the stars a longer exposure can convert.
> `ExposureRecommendation.WingRejectedFraction` = `rejected / (rejected + accepted)` pooled over the outer third
> of the non-recovery frames by distance from the fitted focus; `WingIsShedding` at ≥ 0.20.
>
> - **Pooled over a wing SET, not a worst frame** — which answers this class's own documented objection that a
>   worst-frame rule "would hand the entire recommendation to whichever single frame had a passing cloud".
> - **A PROBE (×2), not a formula**, following `StarCountProbeFactor`'s precedent: the run records HOW MANY
>   candidates were rejected out there, not what SNR they sat at, so there is nothing to derive a magnitude from.
>   **Fabricating one was measured and rejected** — a `(1/(1−f))²` form asked **1.25×** on `D16` at exactly the
>   exposure where its σ_focus is minimised, which is the control the whole statistic has to pass.
> - **It WIDENS rather than replaces:** `max(existing, probe)`. Both halves are load-bearing.
> - **NaN, never 0**, when the run cannot be placed on the wing axis. "We could not look" and "we looked and
>   nothing was shedding" must not be the same number, because the caller turns one of them into an instruction.
> - **Inert unless the data supports it:** a caller that does not populate the per-frame wing axis is
>   byte-identical to before.
>
> ### RULE W, fixed BEFORE the statistic was written, and applied to this binary's own ladder
>
> Validated on wave 7's exposure ladder, still on disk at `D:\hf_w7\armE\t{0.5,1,2,4,8}`. **No re-render.**
>
> | | W1 `D02`@0.5 s ≥ 2× | W2 `D16`@2 s < 1.25× | W3 converges | W4 `D16`@0.5 s asks | verdict |
> |---|---|---|---|---|---|
> | shipped statistic | ✗ 1.00× | ✓ | ✓ | ✓ | fires on NEITHER |
> | wing fraction, `(1/(1−f))²` magnitude | ✓ 4.00× | ✗ 1.25× | ✗ | ✗ | fires on BOTH |
> | wing headroom vs the gate | ✓ 2.00× | ✓ | ✗ | ✗ | — |
> | wing probe ALONE | ✓ 2.00× | ✓ | ✓ | ✗ | — |
> | **`max(shipped, wing probe)`** | **✓ 2.00×** | **✓ 1.00×** | **✓** | **✓ 3.00×** | **ADOPTED** |
>
> **Five of seven candidates failed, three of them AFTER passing W1** — the clause that looks like the whole point.
> On `D02` the adopted statistic asks 2× at 0.5 / 1 / 2 s and goes **silent at 4 s**, which is exactly where this
> binary's σ_focus minimum sits (0.05208).
>
> **The ladder MOVED between binaries** ([F54](#f54--f39bs-default-flip-moves-a-landing-at-a-resolved-factor-of-1-where-it-is-documented-as-a-no-op)),
> so the rule was applied to this binary's own σ values rather than to wave 7's — after checking that **all four
> clauses' premises survive on both ladders**. `D02` gains 48 % here against wave 7's 44 %; `D16`'s minimum is at
> its derived 2 s on both, and 4 s / 8 s are worse on both.
>
> **The verdict is reproduced END TO END by the shipped code**, not only by the offline scorer: re-running the ten
> rungs with `ExposureRecommender` itself gives the same four passes.
>
> **Tests: 8, four discriminating**, each confirmed by neutralizing — delete the probe ⇒ W1/W3/W4 fail; `max()` ⇒
> plain assignment ⇒ W4 alone fails; fire on any rejection ⇒ W2 alone fails. That also caught an overclaim in one
> of this wave's own test comments: the F28 conjunct is **redundant by construction** and fails nothing when
> removed, so both the code and the test now say so.
>
> **Still owed: the §3.5 population check** — the verdict measured across all 20 synthetic datasets and the real
> bank. It is blocked behind F32's confirmation arm, structurally rather than by scheduling: `optimize --per-run`
> writes back into the bank's run folders ([F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings))
> and there is no flag to suppress it, so a population pass while that arm is in flight would race it on every
> folder. Reproduce: `D:\hf_w9\wing2\`, `D:\hf_w9\wing\score_wing.py`.

> ## THE POPULATION CHECK RAN (2026-08-08, wave 10). RULE P FIRES, AND THE WING VERDICT IS WITHDRAWN.
>
> Wave 9 shipped `WingIsShedding` validated on **two** datasets and recorded the population check as *"still
> owed"*. Wave 10 ran it: **39 runs, both full banks, sequential, shipped-default invocation**, against RULE P
> fixed before the pass started.
>
> | clause | measured | verdict |
> |---|---|---|
> | **P1** the pre-registered fires | `D17` fires; `D10` does not | silent (P1 required neither individually) |
> | **P2** fire rate | **30 of 39 = 76.9 %** | **FIRES — REFUTED** |
> | **P3** `D16` at its derived exposure | 0.006, silent | silent — W2 survives |
> | **P4** NaN rate | 0 of 39 | silent — the instrument was connected |
>
> ### Why it fires, and it is not simply "the threshold is too low"
>
> **The control the statistic never had.** `WingRejectedFraction` is `rejected/(rejected+accepted)` over the
> outer third. Computing the same quantity on the **INNER** third — free from the per-frame table the harness
> already writes — asks whether the wings are special at all. Median wing/inner over the 30 firing runs is
> **1.39**, so the wings do reject somewhat more. **But the threshold is on the ABSOLUTE fraction and does not
> track that ratio:** `D17_cdk14_oiii5` fires at a ratio of **0.59** (its wings reject 0.297, its core 0.507),
> `D20_m24_bright_control` fires with an inner fraction of exactly **0.000**, and four real-bank runs
> (`LinwoodFocus` 0.70, `vsn07` 0.94, `toml999` 0.96, `cwhite_2026` 0.98) fire with wings CLEANER than their
> cores. Same verdict, opposite structures.
>
> **The threshold's whole discriminating power over 39 runs is ONE dataset.** Sorted, the fractions are
> **0.000 ×8**, then **0.006** (`D16`), then **0.246 … 0.883**. Every threshold in **(0.006, 0.246)** selects the
> same 30 runs; the only thing 0.20 separates is `D16`.
>
> **Why 0.20 looked well-chosen.** The unit fixtures span **0.002** ("healthy wings") to **0.75** ("shedding"),
> and 0.20 sits comfortably between two poles two orders of magnitude apart. **Exactly one run in 39 resembles
> the healthy pole.** *A unit fixture's range is not the population's range*, and no unit test can notice that.
>
> **And every firing run asks exactly 2×**, because the accepted-star term is far below the probe on all of them,
> so `max()` always chooses the probe. The recommendation carried no information beyond "it fired".
>
> ### What was withdrawn, and what was kept
>
> | | |
> |---|---|
> | **withdrawn** | `WingIsShedding`, `WingSheddingThreshold`, `WingProbeFactor`, and the **three** sites that acted on them: the 2× ask; the `ExposureIsNotTheLimit` override; and [F52](#f52--a-two-hour-optimization-logs-one-line-and-offers-no-cost-context-and-the-search-is-not-cost-aware)(c)'s advice to **cancel a running two-hour search** |
> | **kept** | `WingRejectedFraction` — the MEASUREMENT is real and correctly computed, and the successor is specified against it. A public bool named *"is shedding"* that nothing acts on would be worse than either shipping or removing it |
>
> **F52(c) returns to wave 8's position rather than to nothing.** Wave 8 withheld that advice for want of a
> statistic; wave 9 shipped it on this one; wave 10 withholds it again for the same reason. That is the same
> decision made twice on better evidence, not a regression — and it is the site that mattered most, because the
> advice is computed from the **seed** evaluation and so was firing in the first minute of most users' runs.
>
> ### The successor, PRE-REGISTERED AND NOT ADOPTED
>
> `WingRejectedRatio` = wing-third ÷ inner-third, which is what the claim was always about. Its rule was fixed
> **while the pass was still running**, before the verdict, precisely so it could not be adopted on the data that
> refuted its predecessor: **RULE W1–W4 unchanged**, plus **W5** (its threshold must sit above the bank's median
> wing/inner ratio, or it is the same defect in a new coordinate) and **W6** (it may NOT be validated on this
> population). *A statistic tuned on the data that killed the last one has been fitted, not tested* — which is
> wave 9's own R3 lesson one level up.
>
> **So F19's remainder is OPEN again**, and honestly so: this wave did not fix F19, it corrected a wrong fix and
> left a sharper question with a control that did not exist before.
> Reproduce: `D:\hf_w10\wing_pop.sh`, `D:\hf_w10\score_wing_pop.py`, `D:\hf_w10\pop_score.txt`.

> ## THE SUCCESSOR IS REFUTED TOO (2026-08-08, wave 11) — BEFORE IMPLEMENTATION, BY RULE W ITSELF
>
> `WingRejectedRatio` = wing-third ÷ inner-third was pre-registered with RULE W1–W6. Wave 11 shipped the
> **measurement** and then applied the rule to size its threshold. **The rule has no satisfying input.**
>
> W1–W4 are validated on wave 7's exposure ladder. Wave 7's own aggregates predate `FrameDiagnostics`, but wave 9
> left a 10-rung probe at `D:\hf_w9\wing2\` that has it, so the ratio was readable at **zero compute** — and was
> then **re-measured on pinned provenance** (`exe_fix2`, `pinned_settings_w11.json`, `--profile-id astrodet`,
> `--max-evals 120`, wave 7's frames, no re-render), because `wing2` ran on the v1 binary under an unrecorded
> profile and that is exactly the provenance [F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections)
> condemns. **All ten rungs reproduced**, across a binary change and a profile change, and the shipped field
> agreed with an independent offline recomputation on every one.
>
> | clause | requirement | measured | implies |
> |---|---|---|---|
> | **W1** `D02`@0.5 s must fire | ratio ≥ T | **1.5683** | T ≤ 1.5683 |
> | **W5** above the bank median | — | 1.39 | T > 1.39 |
> | **W2** `D16`@2 s must stay SILENT | ratio < T | **+∞** | **T > +∞ — impossible** |
>
> **W1 ∧ W5 leave the window (1.39, 1.5683]. W2 empties it.**
>
> `D16` at 2 s has an inner rejected fraction of **exactly 0.000** against a wing fraction of **0.0064**, so its
> ratio is infinite and it fires at every finite threshold. **That rung is where `D16`'s σ_focus is MINIMISED**
> (0.17117 against 0.35169 / 0.26026 / 0.20421 / 0.19659), so firing there asks the user to make their focus
> worse — which is the control the whole statistic exists to pass.
>
> **The mechanism is worse than the arithmetic.** On a clean, well-exposed narrowband run the core rejects
> NOTHING, so ANY wing rejection at all becomes an infinite ratio: the ratio form is maximally unstable exactly
> where the statistic must be silent. **The absolute fraction got this rung RIGHT** (0.0064 ≪ 0.20). On the one
> control that matters the successor is not merely no better than its predecessor — **it is strictly worse**.
>
> **What ships:** `WingRejectedRatio` as a MEASUREMENT ONLY, four-state (`NaN` = could not look / `+∞` = the core
> rejects nothing and the wings do / `1.0` = both reject nothing, i.e. equal rates / the ratio). Newtonsoft writes
> the first two as the STRINGS `"NaN"` and `"Infinity"`, pinned by a serialization test — *a scorer that coerces
> either disables its own falsification rule*, and wave 10's scorer, reused verbatim, would have mapped `+∞` to
> NaN and hidden exactly the runs this form fails on. **No verdict, no threshold, no action ships.**
>
> **The predicted hazard class landed on a different dataset than the two named.** Wave 11's design named `D17`
> (ratio 0.59) and `D20` (inner exactly 0.000). The killer was `D16`@2 s — the **same shape as `D20`** on a
> dataset listed under a different clause. *Naming the hazard CLASS in advance worked even though the specific
> dataset was wrong.*
>
> **`WingRejectedExcess` = wing − inner is named as the next candidate and is NOT evaluated anywhere in wave 11.**
> The ladder is now the data that refuted the ratio, and W6 applies to the ladder exactly as it applied to the
> 39-run population.
>
> **The 39-run population pass was NOT run**, and the trade is recorded rather than left as a silently smaller
> pass: the refutation came on a NECESSARY clause, so the pass could not have changed the verdict, and W6 bars
> its rows from sizing the next candidate. **This wave therefore publishes no 39-run distribution for the ratio.**
> Reproduce: `D:\hf_w11\pop\ladder_w11.sh`, `D:\hf_w11\pop\ladder_score.txt`, `D:\hf_w11\pop\score_wing_pop_w11.py`.

> ## THE REMAINDER IS CLOSED (2026-08-09, wave 12). THE THIRD STATISTIC IS REFUTED, AND IT POINTS THE WRONG WAY.
>
> `WingRejectedExcess` = wing − inner got its own arm on a ladder neither prior refutation had touched: three
> datasets the wing statistics had never laddered — `D17_cdk14_oiii5` (genuinely OIII-starved), `D20_m24_bright_control`
> (a known inner-fraction-exactly-0.000 instance) and `D05_tec140_1000mm` (mid-population) — freshly rendered at
> **0.5 / 2 / 8 / 30 / 120 s**, pinned provenance, `--max-evals 120`. W6 satisfied: the 39-run population killed
> the fraction and wave 7's `D02`/`D16` ladder killed the ratio, and neither was used here.
>
> **The threshold window is empty by a wide margin, and three datasets empty it independently:**
>
> | clause | dataset | measured | implies |
> |---|---|---|---|
> | **W1** the most-starved rung must FIRE | `D17`@8 s — σ_focus **19.3× worse** than its own optimum | excess **exactly 0.000000** | **T ≤ 0** |
> | **W2** SILENT at the σ_focus minimum | `D05`@8 s | excess **0.861389** | T > 0.861 |
> | **W2** SILENT at the σ_focus minimum | `D20`@0.5 s (σ_focus 0.00823 — the best focus on the ladder) | excess **0.246027** | T > 0.246 |
> | **W1** | `D05`@2 s — 3.1× worse | excess 0.521251 | T ≤ 0.521 |
> | **W5** above the excess's own bank median | 39 population runs, recomputed offline | 0.088948 | T > 0.0889 |
>
> **`D05` contradicts itself on its own two clauses** (W1 needs T ≤ 0.521, W2 needs T > 0.861), so no threshold
> works even on a single dataset.
>
> ### And the arithmetic is not the finding — the DIRECTION is
>
> Over all **13 usable rungs**, against how bad the focus is (σ_focus ÷ that dataset's own best):
> **Spearman ρ(focus badness, excess) = −0.665.** A statistic meant to say *"your wings are starved, expose
> longer"* must be POSITIVE. **It is at its maximum where focus is best and exactly zero where focus is 19×
> worse than it needs to be.**
>
> `D17` is the sharpest case and it is this entry's own poster child: a 5 nm OIII field that genuinely IS
> photon-starved, whose σ_focus improves **19-fold** from 8 s to 120 s. **At 8 s its wings and its core reject at
> identical rates — both exactly 0.0000.** At 0.5 s and 2 s the detector finds ZERO stars on some frame, so the
> statistic is `NaN` ("could not look") rather than silent.
>
> ### So the remainder closes, and against a FOURTH candidate rather than only the third
>
> | statistic | refuted by | on |
> |---|---|---|
> | `WingRejectedFraction` (absolute) | RULE P — 76.9 % fire rate | 39-run population (wave 10) |
> | `WingRejectedRatio` (wing ÷ inner) | RULE W2 — `D16`@2 s is `+∞` at its σ minimum | wave 7's ladder (wave 11) |
> | `WingRejectedExcess` (wing − inner) | RULE W1 ∧ W2 ∧ W5, three datasets, ρ = −0.665 | a fresh 3×5 ladder (wave 12) |
>
> **NO STATISTIC OVER GATE REJECTIONS SEPARATES A STARVED WING FROM A DETECTOR THAT REJECTS NOISE EVERYWHERE**,
> and ρ = −0.665 says why it is not a property of the arithmetic: **gate rejections count what the detector THREW
> AWAY, and a starved frame's problem is what it never FOUND.** A fourth function of the same quantity would
> inherit the same blindness. Anyone re-opening this must bring a different QUANTITY, not a different formula —
> and the obvious one is what the faint end's SNR distribution does with exposure, which is not a rejection count.
>
> **What ships: nothing.** No verdict, threshold, probe factor or action. `WingRejectedFraction` and
> `WingRejectedRatio` remain measurements with no consumer.
>
> **W1 was WEAKENED before the data, not after.** The first draft required firing on EVERY materially-worse rung
> short of the optimum; wave 9's rule named exactly one per dataset, so the draft would have held the third
> candidate to a bar neither predecessor had to clear. It was corrected and committed before the ladder rendered
> a frame — and the excess then failed the WEAKER rule by `T ≤ 0` against `T > 0.861`, so the distinction changed
> nothing. *Which is the point: that cannot be known in advance, which is why the rule moves before the data.*
> Reproduce: `D:\hf_w12\ladder_w12.sh`, `D:\hf_w12\score_excess_w12.py`, `D:\hf_w12\ladder_score.txt`,
> `D:\hf_w12\ladder_corr.txt`.

> ## (c) RESOLVED 2026-08-07 (wave 9): THE FLOOR STAYS, THE CEILING STAYS, AND NOTHING RE-RENDERS
>
> (c) was filed as expensive — moving `MinExposureSeconds` re-derives and re-renders 11 datasets. **The check
> that decides whether to spend that is free, and it was run first.** Rule F19c, fixed before it ran: *(c)
> resolves as "the floor stays" unless a floor-clamped dataset can be shown, from data already on disk, to have
> its σ_focus minimum BELOW 0.5 s.*
>
> **The floor. Every one of the 11 clamped datasets asks for LESS than 0.5 s**, so a lower floor moves all eleven
> **down** — and the one dataset with a measured exposure ladder moves the other way: wave 7's arm E has `D02`
> improving **44 % of σ_focus at 8 s**, four orders of magnitude above its 0.001 s ask. **Lowering the floor makes
> `D02` worse; raising it is not what a floor is for** (a floor stops an absurdly short exposure, it does not
> supply an exposure the derivation failed to find). **The floor is the symptom, not the defect.**
>
> **And the free check produced a number the entry never had.** `synth-bank --dry-run` on the wave-9 binary
> reproduces the saturated set exactly (**13 of 20**, 11 floor + 2 ceiling), and printed beside the on-frame star
> count the 20th-brightest is drawn from it says what this entry has always ARGUED:
>
> | dataset | on-frame stars | raw ask | clamp |
> |---|---|---|---|
> | `D10_rc16_3250mm_sparse` | **26** | **335.6 s** | ceiling |
> | `D13_apo200_1800mm` | 177 | 0.264 s | floor |
> | `D07_rc10_2000mm` | 428 | 0.066 s | floor |
> | `D14_cdk14_2563mm_e47` | 640 | 0.055 s | floor |
> | **`D17_cdk14_oiii5`** | **799** | **40.4 s** | **ceiling** |
> | `D03` / `D20` / `D05` / `D02` | 969 / 1151 / 2346 / 3590 | 0.003 / 0.004 / 0.014 / **0.001** s | floor |
> | `D19` / `D04` / `D01` / `D18` | 4914 / 7480 / 19201 / **27084** | 0.008 / 0.004 / 0.003 / 0.004 s | floor |
>
> **Spearman ρ(on-frame star count, raw ask) = −0.68 over the 13.** The two CEILING datasets are the two
> sparsest-or-faintest; all eleven FLOOR datasets are the rich ones. *"Sized by field richness rather than by
> whether the stars the fit depends on are above the noise"* is no longer an argument — it is a rank correlation
> over the whole bank, in both directions at once.
>
> **The one exception is the honest one, and it is this entry's own poster child.** `D17_cdk14_oiii5` has 799
> on-frame stars — more than four of the floor-clamped datasets — and still asks for 40.4 s, because an OIII
> filter genuinely starves it. So richness is not the WHOLE story; it is simply the dominant term, and the
> statistic has no way to tell the two apart. That is the wing-statistic problem, not a clamp problem.
>
> ### The CEILING, which had never been examined
>
> Separable from the floor and **not obviously the same defect** — at the floor the arithmetic asks for less than
> any sane exposure; at the ceiling it asks for more than an AF sweep can spend.
>
> - **Product: `MaxRecommendedExposureSeconds = 30 s` STAYS. Verified, not changed.** `D10`'s 335.6 s over a
>   9-point sweep is ~50 minutes of pure integration and `AutoFocusEngineOptions.AutoFocusTimeout` would kill the
>   run. The cap is doing its job, and `StarSignalCopy.DescribeExposureDerivation` already names *which* bound
>   bound it (F19(b) verified that).
> - **Bank fidelity: `D10` and `D17` are RENDERED at 30 s while their own physics asks 335.6 / 40.4 s.** Two of
>   the twenty datasets are deliberately photon-starved relative to their derivation and no write-up had ever said
>   so. **Recorded, not re-rendered:** a `D10` rendered at 335 s would be a dataset no user could capture.
> - **Rule CEIL, fixed in advance:** the ceiling moves only if a dataset's σ_focus is measured to improve
>   materially between 30 s and its raw ask. No such ladder exists, and rendering one would re-render the bank
>   [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
>   confirmation arm was running on. **Deferred with a stated price**, which is the thing wave 8's §0.1 got wrong
>   by deferring on a prediction instead.
>
> **So (c) costs nothing and re-renders nothing, and the wave's item 1 was worth running for its own sake rather
> than as a prerequisite.** Reproduce: `D:\hf_w9\dryrun_w9.txt`.

### F20 — Below `MinHFR` the autofocus objective collapses to exactly zero, with no diagnostic
**Status:** Done (parts 1 and 2) · found 2026-08-02 running `optimize --per-run` over the
synthetic AF bank

> **Where this stands after wave 3.** The two halves of this entry's fix have diverged and the entry should not
> be read as half-closed by accident:
>
> - **Part 2, "seed out of the plateau": DONE** via [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it).
>   D01 and D02 go from `FinalJ` exactly 0 to real landings; 14/17 synthetic and 19/19 real landings are
>   bit-identical to the control.
> - **Part 1, "report it": DONE (wave 4)** — and the blocker this entry named was not the blocker.
>   `UndersampledStarsChartNote` now states the measured in-focus HFR, the gate it fell under, and the binning
>   that relates them, as an italic warning above the focus curve (`Optimization/DataTemplates.xaml`), plus
>   `FrameTooLowHFRCounts` through the optimizer.
>
>   **The `*Bounds` framing was a red herring.** The seam that reads `LowSensitivity`/`TooFlat`
>   (`RunEvaluationLoader.cs:334-335`) reads **int properties** — `Metrics?.LowSensitivity ?? 0` — and
>   `StarDetectorMetrics.TooLowHFR` is already exactly such an int (`IStarDetector.cs:755`), already incremented
>   unconditionally (`StarDetector.cs:1895`), already merged across threads (`IStarDetector.cs:832`), and
>   **already rendered in `AutoFocus/DataTemplates.xaml:1062-1074` as "HFR Too Low"**. Whether a gate owns a
>   `*Bounds` rect list has nothing to do with reachability at that seam. So the project invariant about new
>   `StarDetectorMetrics` fields was **already satisfied**, and the real work was the optimizer-side hop
>   (`FrameDetectionResult` → `RunEvaluationMetrics`) — four edit sites across three files, not a detector change.
>
>   Deliberately **not** folded into the "Star signal" block: its doc comment (`VM:227-248`) scopes it to the
>   *Sensitivity* gate, and this is a different gate with a different remedy. It is a sibling note instead, and
>   both can be visible at once. The trigger is `MinHfrSeed.IsBelowGate`, shared with the seed so the message and
>   the seeding decision cannot drift apart.
> - **The real-bank claim below is disproven** (see the correction under Evidence). What reproduces on real rigs
>   is the *symptom*, not the cause.

When a sweep's in-focus HFR falls below the detector's `MinHFR` gate (default 1.2 px), the whole core of the
V-curve is rejected, the run objective is `0`, and the optimizer terminates having explored the space for
nothing. Nothing in the output says "your stars are smaller than the minimum HFR".

**Evidence.** `D01_ultrawide_40mm` (40 mm f/2.8, 19.4″/px) has truth HFR per frame
`[2.27, 1.71, 1.15, 0.61, 0.24, 0.61, 1.15, 1.71, 2.27]` px — the middle **five of nine** frames sit at or below
`MinHFR = 1.2`. `optimize --per-run` reports `Current settings J: 0`, then
`Optimization complete: currentJ=0 -> bestJ=0 (no improvement over current), evals=88`. The goldens for those
frames are populated (the stars are there and bright — tiering is by SNR, not size), so this is a gate rejecting
real, well-detected signal, not an empty field.

**Why it matters.** `J = 0` is indistinguishable from "starless frames", "wrong folder", and "detector
misconfigured". A user pointing the wizard at a short-focal-length rig gets a silent null result.

**And the capability is reachable — the optimizer simply cannot get to it.** `MinHFR` *is* a curated optimizer
axis, searchable over **0.1–5.0** (`OptimizerVariable.CreateCuratedSet`), so lowering it is exactly the move that
would unlock these rigs. What the landings show is that the optimizer only makes that move when it already has a
gradient:

| dataset | in-focus HFR (px) | `MinHFR` landed by config A | outcome |
|---|---|---|---|
| `D01_ultrawide_40mm` | 0.24 | **1.2 — the default, unmoved** | J = 0, recall 0.129 |
| `D02_rich_135mm` | ~0.7 | **1.2 — the default, unmoved** | recall 0.451 |
| `D03_redcat_250mm` | ~0.97 | **0.45** (moved down) | recall 0.396 → 0.537 under B |
| `D05_tec140_1000mm` | 1.77 | 1.45 | recall 0.989 |

D03 proves the search *can* find a sub-default `MinHFR` when the objective is non-zero. D01 and D02 never move it,
because `J` is identically 0 across the neighbourhood the search explores — the objective only becomes non-zero
once *enough* of the curve is simultaneously measurable, so no single-axis step off the seed improves anything.
This is a **cold-start plateau**, not a missing knob, and it is why the earlier framing of this entry ("the gate
is defensible, only the silence is a problem") was too generous: the gate costs a whole class of rigs their
autofocus, and the fix is within the existing search space.

**It also reproduces on the REAL bank (2026-08-03).** `BaselineJ` — the objective at shipped defaults — is
exactly **0.0000** on `Panos` and `LinwoodFocus`. Both are the same silent null result D01/D02 produce
synthetically, on real frames from real rigs, and both are runs whose recall@≥12 *improves* under the optimizer
(+0.031 and +0.089), for the reason F32 gives: they have headroom to climb. So this is not a synthetic-bank
artifact of the render, and the affected population is not hypothetical.

> **Corrected 2026-08-03 (wave 3), twice over.**
>
> **(a) "The only two runs of nineteen" is wrong — it is four.** `SorenVance` and `lumos` also carry
> `BaselineJ` exactly 0.0000. They split, too: `SorenVance` climbs 0 → **0.9701** while `lumos` stays 0 → 0, so
> the cold-start plateau is **sometimes** escapable — which the two-run framing hid.
>
> **(b) NONE of the four is the defect this entry describes, and that is the bigger correction.** The claim
> above — that they are "the same silent null result D01/D02 produce synthetically" — was tested directly by the
> [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it)
> seeding run over all 19 real runs. **The seed fired on zero of them**, because all four have a fit vertex
> *above* the 1.2 gate, and all 19 landings came back bit-identical to the control. Their `J = 0` comes from
> having almost no stars at all — `lumos` and `SorenVance` detect **zero** at C0, `Panos` 33 and `LinwoodFocus`
> 13 across nine frames — not from stars being rejected for measuring too small.
>
> **Two different defects present identically** as `currentJ=0 bestJ=0 hard-floor FAIL`: the `MinHFR` gate
> emptying the curve's core (synthetic D01/D02) and a frame with no detectable signal (real `lumos`/`SorenVance`).
> Only the first is a detector-configuration problem. So this entry's strongest claim — that the affected
> population is not hypothetical because it reproduces on real rigs — **is not supported**; what reproduces on
> the real bank is the *symptom*, not the cause. The synthetic bank is currently the only place the real defect
> is known to exist.

> ### `RULE L26` — THE `lumos` HALF IS STILL UNANSWERED, AND THE INSTRUMENT REFUSED RATHER THAN GUESSING (2026-08-13, wave 26)
>
> Correction (b) above splits `J = 0` into two defects that present identically. **Which one `lumos` is has been
> open for eleven waves.** Wave 24 reproduced its `rc=3` and explained it as a **hard-floor FAIL**
> (`bestJ = currentJ = 0`, *"at least one frame has < 3 stars under optimized params (min observed = 0)"*), which
> narrows the question to exactly two answers:
>
> > Is the zero-star frame a property of the **FRAME** — no usable signal at any settings — or of the **PARAMETER
> > VECTOR** — the optimizer's landing gates it out?
>
> **`af-fit` was the right instrument** (it applies no run detection binning — that is
> [F67](#f67--af-fits-star-count-and-optimizes-are-not-the-same-number-so-the-control-built-on-their-equality-reports-could-not-look-on-exactly-the-datasets-where-the-intervention-bites-hardest)'s
> whole mechanism — and reads the run's own detection result rather than an optimized snapshot). **It produced no
> `af_fit_points.csv`.** The driver named the absence, wrote **0 rows**, wrote **no marker**, and the scorer
> returned **COULD-NOT-LOOK**:
>
> ```
> 2026-08-13T07:09:11Z    NAMED: af-fit produced no af_fit_points.csv
> 2026-08-13T07:09:11Z  L26_ROWS 0
> 2026-08-13T07:09:11Z  L26 REFUSED: no artifact.
> ```
>
> **The refusal is the instrument working, not a failure.** `L26-A` counts rows whose `Stars` column is `0`; a
> driver that had defaulted a missing file to 0 rows would have branched to **`L-FRAME`** — *"the run is unusable
> and the gate question is closed"* — which is **the wrong answer produced by an absent measurement**, on the
> side that closes the question. *"Could not look" needs its own state, and this is the case that shows why the
> state must be reachable from a MISSING artifact and not only from an unreadable one.*
>
> **Still priced at ~10 m**, with one addition: the next attempt **owes a diagnosis of why `af-fit` emitted
> nothing** before it re-runs the same command. Reproduce: `/mnt/d/hf_w26/w26_l.log`,
> `/mnt/d/hf_w26/q26_score.txt`; `docs/synthetic-af-bank-followups-wave26-results.md` §10.

**Next step.** Two parts, and the second is the substantive one.
1. *Report it.* When a large fraction of accepted candidates are rejected by `MinHFR` specifically, say so and
   name the pixel-scale / focal-length combination. The counts are already collected — but **not where the
   optimizer can see them**: `CollectRejectedCandidateDiagnostics` records never reach the optimizer's
   evaluation path ([F27](#f27--the-optimizer-cannot-reach-the-rejected-candidate-diagnostics-an-approved-spec-says-it-can)).
   The nine per-gate `*Bounds` rect lists *are* reachable at the same seam that reads
   `LowSensitivity`/`TooFlat`, and one of them is `TooLowHFR`… except it is not: `*Bounds` covers eight of
   `RejectionGate`'s twelve constants and `TooLowHFR` is one of the four with none. So this is reporting **plus**
   one new counter, not reporting alone.
2. *Seed out of the plateau.* Before optimizing, if the median in-focus HFR is at or below `MinHFR`, seed
   `MinHFR` beneath it (the measured HFR is available from the same in-focus record
   `DetectionBinningResolver` already consumes) so the search starts somewhere with a gradient. Score any change
   on **D01–D03** of the synthetic bank, where the correct answer is known and current recall is 0.135 / 0.475 /
   0.400.

   **Measured 2026-08-03 — see [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it), which changes this step in two ways.**
   (a) The trigger as written is **circular** — D01's in-focus frame detects zero stars, so there is no median
   in-focus HFR to read. It has to come from the sweep WINGS, which are richly populated (816–2002 stars/frame)
   and already fit at R² = 0.911. (b) The success criterion here is wrong: lowering the gate moves D01's recall
   only **0.135 → 0.165**, because `TooLowHFR` accounts for just 1877 of ~38500 missed stars while the structure
   map never proposes 18801 of them. What it *does* do is put 5 stars back on the vertex frame at `MinHFR` 0.5,
   which clears the `NHard` = 3 hard floor and is the entire reason `FinalJ` is 0. **Score this fix on "the hard
   floor passes", not on recall** — and do not expect it to lift the W-class band.

   **The risk to design against.** A seeded `MinHFR` is a knob the search may not be able to climb back out of:
   the objective is flat in its neighbourhood on exactly these rigs, which is why the search never moves it
   today. Seed it to a value *measured* from the frames, not to a permissively low one, and re-check that a rig
   which does NOT need the seed (D05, whose search finds 1.45 unaided) is left alone. Compounding factor from
   [F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor),
   **with its direction corrected in wave 4**: below ~1.1 px the measurement *over*-reads (the pixelization
   floor, ~0.7 px), it does not under-read, so it makes an undersampled rig look further from this cliff than it
   is rather than closer. The rig is still gated — `MinHFR` defaults to 1.2 and the floored measurement sits
   below it — but any seed sized from that measurement inherits an understatement of how far the gate must come
   down, which is the bias F35 exists to route around.

### F21 — `StepSizeRecommender`'s half-width is not stable against noise, even on a perfect fit

> **WAVE 23: THE 45-52 s PRICE IS A PROBE PRICE, NOT AN ARM RATE, AND BUDGETING FROM IT UNDER-RESERVES BY 3.5x.**
>
> Wave 23 ran the first real `synth-validate` **arm** — 20 datasets x 2 scenarios = **40 cells** at the shipped
> `--max-rounds 4` — and priced it from the measurement below. **Estimated ~35 m; measured 2 h 04 m 06 s**
> (`V23_START 2026-08-12T17:24:28Z -> V23_DONE 19:28:34Z`).
>
> | | |
> |---|---|
> | pre-registered estimate | ~75-125 s per dataset (both scenarios) ⇒ **21-35 m** for 20 datasets |
> | **measured** | **7 446 s / 40 scheduled cells = ~186 s per cell, ~372 s per dataset** |
> | overrun | **x3.5** against the 35 m estimate; per-cell spread **23 s to > 600 s** |
>
> **Why the price below could not transfer, in three named denominators.** It was measured on
> `D11_rc10_585_afbin2` — chosen explicitly *"for price, not physics"*, the bank's **smallest** dataset (38 MB)
> and a **factor-2** dataset, i.e. detection on a **quarter of the pixels** — at **`--max-rounds 2`**, on **one**
> cell. Wave 23's arm ran **mixed factor-1** datasets at **`--max-rounds 4`** over **40** cells.
>
> **The handoff's budget table warned about this axis in one direction only, and the wave walked into its mirror
> image with the warning on the page.** The recorded caveat is that *budgeting a **factor-2** arm at the synthetic
> rate **over**-reserves by ~10x*. **The inverse was never written down: pricing a **factor-1** arm from a
> **factor-2** measurement **under**-reserves.** The general rule, stated so it does not have to be rediscovered a
> third time: **a rate measured at one binning factor does not transfer to another in EITHER direction.**
>
> **The corrected rows for the charter's budget table:**
>
> | instrument | rate | caveat |
> |---|---|---|
> | `synth-validate`, factor-2, `--max-rounds 2`, 1 dataset x 1 scenario | **45-52 s** (wave 21, n = 1) | a **probe** price; not an arm rate |
> | `synth-validate`, mixed factor-1, `--max-rounds 4`, per (dataset, scenario) cell | **~186 s/cell, ~372 s/dataset** (wave 23, n = 40 scheduled / 38 landed) | S0 is one round, S1 is 1-4. **Budget 2 h for a 40-cell arm**, keep `timeout 600` per cell |
>
> Two cells hit the 600 s timeout (`D19_cygnus_deep_shed`/S1, `D05_tec140_1000mm`/S1), produced no report, and are
> **NOT-RUN and named** — 38 of 40 rows, both scenarios still above their pre-registered minimum.
>
> **And this entry's own subject got a datum, in a population of 19.** `D16_esprit550_ha3`/S0 — the bank's 3 nm
> Ha dataset, the lowest-SNR member — was handed the **exactly correct** step of 15, recommended **22** (+47 %),
> and then held there: `halfWidth` 78.06 then 78.41 on two independent noise realisations. **The answer is
> reproducible, not jittery**, and it is ~1.49x the half-width truth implies. It was the **only** miss in the
> do-no-harm direction (`V23-G` on S0: 18 of 19). Meanwhile `terminal.stepBehavioralVsTheoryDeltaFraction` — this
> recommender's own fixed point on the **noiseless** truth curve — is **exactly 0.0000 on 34 of 36 cells**, so the
> residual on `D16` is a **measurement bias in `FindHalfWidth` under low SNR**, not an arithmetic error.
> **n = 1, so no fix is proposed** ([F68](#f68--a-threshold-stated-as-a-count-carries-a-denominator-and-three-consecutive-satisfiability-analyses-have-checked-the-value-a-clause-can-reach-without-checking-the-population-it-is-computed-over)):
> the arm that would size it is S0 across the bank at several **noise seeds**, and **seed sensitivity is still
> unmeasured — wave 23 varied no seed either**, so that arm still needs a seed override `synth-validate` does not
> expose.
> Reproduce: `/mnt/d/hf_w23/v23_arm_w23.log`, `/mnt/d/hf_w23/v23_score.txt`;
> `docs/synthetic-af-bank-followups-wave23-results.md` §9.1 and §4.3.

> **WAVE 21, item P: THE INSTRUMENT RAN. The price is 52 seconds, and here is the invocation that works.**
> Rule-free by pre-registration, `timeout 1200`, pinned like every other arm. `F21_START 2026-08-11T10:09:19Z →
> F21_DONE 10:10:11Z`, **exit 0 by name** (not 124 the timebox, not wave 20's 2):
>
> ```bash
> timeout 1200 "$EXE" synth-validate \
>   --spec 'D:\hf_w21\exe\SynthBank\synthetic-bank-spec.json' \
>   --out 'D:\hf_w21\f21' \
>   --datasets D11_rc10_585_afbin2 --scenarios S1 --max-rounds 2 \
>   --settings 'D:\hf_w11\pinned_settings_w11.json' \
>   --profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382
> ```
>
> The spec **ships with the build** (`<exe>\SynthBank\synthetic-bank-spec.json`, sha256 `bf10522e670a…`), so no
> file has to be authored. `S1` is *"step ×0.25 — multi-round convergence"*, the scenario that makes a second
> round happen by construction; `D11_rc10_585_afbin2` is the bank's smallest dataset (38 MB) and was chosen for
> **price, not physics**. **Price: 45–52 s** for one dataset × one scenario × two rounds.
>
> | deliverable | value |
> |---|---|
> | round count | `maxRounds=2`, **`roundsUsed=2`** |
> | **did a second round occur at all** | **Yes** — two `round_*/attempt01/` trees, **9 `.fits` frames each**, on disk |
> | trajectory | round 0: bootstrap 14 → step **24**, `halfWidth` 84.0, `wasCapped` **True**, ratio 1.7142857142857142 · round 1: bootstrap 24 → step **41**, `halfWidth` 144.0, `wasCapped` **True**, same ratio |
> | terminal | `converged=True`, `finalStepSize=41`, `expectedStepSize=55`, `stepTheory=55`, `stepBehavioral=55`, `stepToleranceBand=22.0`, `overallVerdict=0`, `stoppedReason=`**`converged (step 41 within the 22 tolerance band of step_behavioral 55)`** |
>
> **The report's real field names, because the probe's own reader guessed wrong and printed four `None`s** — the
> driver was instructed to say so rather than read empty fields as "no second round occurred", and it did:
> `datasetId` / `scenarioId` (not `dataset`/`scenario`); `rounds[N].stepRecommendation.{halfWidth, stepSize,
> wasCapped, cappedGrowthRatio}` and `rounds[N].bootstrap.{stepSize, seed}` (**nested**, not flat);
> `terminal.assertions[].{id, verdict, detail}` (not `name`/`passed`). *A reader and a writer disagreeing about a
> name — [F74](#f74--a-driver-and-its-scorer-each-rebuilt-the-artifact-path-from-a-template-disagreed-and-cost-a-pre-registered-rule-its-verdict--while-the-interlock-marker-between-them-already-carried-the-answer)'s
> family, in the probe rather than in the product.*
>
> **A rule-free follow-on, and the correction it forced.** The stop looked suspicious — `converged` fired at
> `roundsUsed == maxRounds == 2` while the step was still climbing at a capped 1.714× — so a second run with
> `--max-rounds 5` and nothing else changed was made, with the prediction written first. It stopped at
> **`roundsUsed=2` again**, same `finalStepSize=41`, same `stoppedReason`, in 45 s: **the convergence test is
> really firing, not `maxRounds` running out.** **But it is NOT a reproducibility measurement.** Both runs record
> the *same* per-round seeds (`-25215000`, `1539450862`) — they are derived from the spec and scenario, not drawn
> per invocation — and the rendered frames are **byte-identical** between the runs, with the two report
> markdowns differing only in `Generated:` and `Out:`. So the follow-on establishes **bit-exact determinism at
> identical inputs**, and **seed sensitivity remains UNMEASURED**: nothing in the wave varied a seed.
>
> **What this does NOT do.** It does not test F21's own hypothesis. No clause was written over it
> ([F68](#f68--a-threshold-stated-as-a-count-carries-a-denominator-and-three-consecutive-satisfiability-analyses-have-checked-the-value-a-clause-can-reach-without-checking-the-population-it-is-computed-over):
> F21's hypothesis was refuted in wave 10 and its headline case did not reproduce, so a bar over "the population"
> would be a bar over a population nobody has shown exists). What the next wave gets is a **working, pinned
> invocation and a measured rate**, so an actual half-width-stability arm can be priced from data.
> Reproduce: `/mnt/d/hf_w21/f21_chain.log`, `/mnt/d/hf_w21/f21_probe_w21.sh`,
> `/mnt/d/hf_w21/f21{,b}/synth_validate_report.json`;
> `docs/synthetic-af-bank-followups-wave21-results.md` §7.

> **WAVE 20, item P: the price probe ran and returned `COULD-NOT-LOOK` in 0 s — `synth-validate` exits 2 as
> invoked.** The rate is **UNMEASURED** and the exit code is recorded by name; it is *not* "the instrument is
> fast". `synth-validate` requires `--spec <json>` (its usage line: `--spec <json> --out <dir> [--datasets …]
> [--scenarios …] [--max-rounds 4] …`) and the probe passes none, so nothing was measured about half-width
> instability. The probe also refused to read its own two empty field lines as "no second round occurred",
> printing *"if these are empty, the fields are named differently in this build: SAY SO"* instead.
>
> **After four waves of deferral this is the useful answer, and it is cheaper than a fifth deferral: the
> blocker is not the population, it is that nobody has ever invoked the instrument successfully.** The next
> attempt costs a `--spec` file and one run, not an arm — and it must establish that a second round happens at
> all before any clause is written over one, since [F68](#f68--a-threshold-stated-as-a-count-carries-a-denominator-and-three-consecutive-satisfiability-analyses-have-checked-the-value-a-clause-can-reach-without-checking-the-population-it-is-computed-over)
> forbids a bar over a population nobody has shown exists.
> Reproduce: `/mnt/d/hf_w20/f21_probe_w20.sh`, `/mnt/d/hf_w20/f21_probe_w20.log`.
**Status:** Open · found 2026-08-02 running the synthetic bank's S0 control

Two sweeps of the **same dataset at the same step**, differing only in noise seed and both fitting at
**R² = 1.0000**, produced half-widths an order of magnitude apart — and therefore recommended steps an order of
magnitude apart.

| dataset | round | fit R² | `HalfWidth` | recommended step |
|---|---|---|---|---|
| `D17_cdk14_oiii5` | 0 | 1.0000 | 143.6 | 41 |
| `D17_cdk14_oiii5` | 1 | 1.0000 | **12.1** | **3** |
| `D02_rich_135mm` | 0 | 0.9156 | 23.2 | 7 |
| `D02_rich_135mm` | 1 | 0.9324 | **2.5** | **1** |

**Evidence.** `TestApp synth-validate --scenarios S0`, bootstrap = the dataset's own expected optimum
(D17: step 60). A recommended step of 3 where 60 is correct turns a ±240-step sweep into a ±12-step one — every
frame lands inside the focus zone and the V-curve has no wings at all. This is *not* [F8](#f8--optimizer-landings-are-not-reproducible-across-invocations):
F8 is about the optimizer's search landing in different corners of a flat valley, whereas here the fit is
essentially exact both times and it is `FindHalfWidth`'s own outward search that returns a wildly different
answer.

**Why it matters.** The step size is the one recommendation a user is most likely to accept unread, and a
collapse of this magnitude silently destroys the next autofocus run. It also makes any single measurement of
"what step does the recommender want" untrustworthy — the same caveat F8 imposes on optimizer landings now
applies to the recommender itself.

**Next step.** Instrument `FindHalfWidth`: log the fitted `minimum`, the `3 × minimum` target, and the bracket it
converged on, for both rounds of D17. The suspicion is that when the fitted minimum sits slightly high, the
`3 × min` crossing is found very close to focus and the coarse walk terminates before it reaches the real one —
but that is a hypothesis, not a diagnosis, and it should be confirmed on the two saved sweeps before any change.
Reproduce: `synth-validate --datasets D17_cdk14_oiii5 --scenarios S0 --max-rounds 2` (seeds are deterministic).

**Wave 7 bounds this entry's SYMPTOM and does not touch its cause (2026-08-06).**
[F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see)'s
`MinHalfWidthSampledHalfSpanMultiple = 0.5` caps how far one run may narrow the sweep, so a 143.6 → 12.1 collapse
can no longer land as a 41 → 3 step in one round. But nothing has diagnosed WHY `FindHalfWidth`'s outward search
returned 12.1 on a fit at R² = 1.0000, and a bound on a wrong answer is not an explanation. **This entry's own
next step — instrument `FindHalfWidth` on the two saved `D17` sweeps and confirm or refute the coarse-walk
hypothesis — is still owed**, and the entry stays Open.

> ### DIAGNOSED 2026-08-08 (wave 10): the coarse walk is EXACT, and this entry's own case will not reproduce
>
> This entry's next step — *"instrument `FindHalfWidth` ... confirm or refute the coarse-walk hypothesis"* — is
> discharged, and **the hypothesis is refuted.**
>
> **`FindHalfWidth` is exact for a hyperbola.** It returns `√8 · HFR_min / κ`, so its output is PROPORTIONAL to
> the fitted vertex HFR. Wave 7's F18 control arm, already on disk, shows exactly that: over uncapped rounds
> `halfWidth / vertexY` holds to **×1.03–×1.14** within a dataset while `halfWidth` itself spans up to **×1.69**,
> and the ratio differs strongly BETWEEN datasets as `√8/κ` should. **The walk is not what moves — the FIT's
> vertex is.** `R² = 1.0000` is no evidence against that: R² measures fit to the SAMPLED points and says nothing
> about whether the vertex is identifiable from them, which on a sweep that never leaves the focus zone it is not.
>
> **This entry's own reproduce line was run, and round 0 reproduces EXACTLY:**
>
> | | as recorded | wave 10 |
> |---|---|---|
> | round 0 `HalfWidth` / step | 143.6 / 41 | **143.583 / 41** |
> | round 1 | **12.1 / 3** | **did not occur — converged in 1 round** |
>
> So the instrument is right and **the pathology did not recur** — the same shape as
> [F25](#f25--from-a-far-too-wide-sweep-the-step-recommender-widens-it-further-instead-of-recovering), whose
> 4×→7× over-reach also failed to re-measure. The headline stays a real observation of a real run and is **not**
> a reproducible case, which is why the diagnosis rests on six OTHER datasets.
>
> **And with a fixed seed the recommender is BIT-REPRODUCIBLE**: all six S0 half-widths are identical to full
> double precision between wave 7's v1 binary and wave 10's v2 (130.132 / 129.313 / 399.122 / 396.872 / 168.835 /
> 73.358). The instability this entry names is a property of the NOISE REALIZATION, not of the arithmetic.
>
> **A deadband was proposed to bound the symptom and REFUTED before implementation** by the rule fixed to size
> it (wave-10 design §3.3B: *"a deadband narrower than the recommender's own reproducibility does nothing"*).
> With a fixed seed that spread is ZERO, so a compliant deadband is zero; and on S0 — where the bootstrap IS each
> dataset's expected optimum — the recommender asks a median **19.9 %** (max 40 %), so a deadband wide enough to
> matter would suppress 4 of 6 of those and bury F18's open question about which of the two is right.
> **Still owed: WHY the fitted vertex moves**, which is a fit-identifiability question and not a search one.
> Reproduce: `D:\hf_w10\step_probe.sh`, `D:\hf_w10\score_step.py`.

> **The instrument has now been UNPRICED for three consecutive waves (2026-08-11).** Wave 17 asked what one
> `synth-validate` round costs, wave 18 dropped the probe, and wave 19 pre-registered it as a rule-free,
> hard-timeboxed tail item (`timeout 1200`, driver `/mnt/d/hf_w19/f21_probe_w19.sh`) and dropped it too — first
> in the drop order, in a wave that used 1 h 35 m of a 6 h ceiling.
>
> **This series has never run `synth-validate` as an arm**, so its cost is not merely unrecorded, it has no
> measured rate at all — and it must **not** be priced from the `optimize` rate (5.2 m/run mixed, 2.64–2.73 m/run
> all-synthetic) or the `af-fit` rate (~11 s/dataset). *Pricing one instrument from another over-prices by
> roughly an order of magnitude*, which is why the timebox is the deliverable and `"> 20 m, UNMEASURED"` is a
> more useful answer than a wave that ran long.
>
> **No clause should be written over it until then**, and that is deliberate: this entry's own hypothesis was
> refuted in wave 10 and its headline case *did not reproduce*, so a rule over the population would be a rule
> over a population that may not exist — exactly the defect
> [F68](#f68--a-threshold-stated-as-a-count-carries-a-denominator-and-three-consecutive-satisfiability-analyses-have-checked-the-value-a-clause-can-reach-without-checking-the-population-it-is-computed-over)
> catalogues. The probe asks only: *does the population exist, and what does one round cost?* Deliverables and
> nothing else: the wall time, the round count, `HalfWidth` and recommended step per round, and whether a second
> round occurred at all.

> ### THE COSTING, FINISHED (2026-08-12, wave 24) — AND THE FAILURE MODE IS NOW EXTRAPOLATION, NOT INSTRUMENT
>
> Wave 21 priced the probe at **45–52 s**; wave 23 priced a 40-cell arm from it and **under-reserved by 3.5×**,
> because that figure was measured on `D11`, a **factor-2, 38 MB** dataset at `--max-rounds 2`. Wave 24 priced
> **per (scenario, dataset)** from wave 23's own `run.log` mtime deltas — the finest instrument that exists for
> this question, accounting for 97 % of that arm's wall clock:
>
> | scenario | n | mean | median | min | max |
> |---|---|---|---|---|---|
> | S0 | 19 | **136 s** | 86 s | 12 s | 460 s |
> | S1 | 20 | **234 s** | 130 s | 30 s | 577 s |
>
> **Both are far finer than the blended `7 446 s / 40 = 186 s` this entry could previously offer, and where they
> were applied they were excellent. What missed was the derived prices.**
>
> | wave-24 step | priced | actual | ratio | priced from |
> |---|---|---|---|---|
> | R-AFTER arm, S0 × 20 | ~45 m | **42 m 56 s** | **0.95×** | **the S0 rate above — measured** |
> | RULE G24 gate, 8 `optimize` runs | ~42 m | **41 m 15 s** | **0.98×** | six prior waves at ~5.2 m/run — **measured** |
> | A-BEFORE arm, S4 × 20 + 2 | ~45 m | **17 m 39 s** | **0.39×** | **2× those datasets' S1 sum — DERIVED** |
> | A-AFTER arm, + 3 controls | ~50 m | **18 m 12 s** | **0.36×** | **DERIVED, same way** |
> | S2 census, 7 cells | ~18 m | **7 m 47 s** | **0.43×** | **1× their S1 sum — DERIVED** |
> | item L24, 2 `optimize` runs | ~12 m | **10 m 17 s** | **0.86×** | the mixed `optimize` rate — measured |
>
> **The rule this entry should now carry:** *price from the same instrument **AND the same scenario**. When a
> scenario has never been run, say the price is a derivation and give it a BAND, not a number.* S4 and S2 had
> never been run; both derivations over-priced by ~2.5×, while every measured price landed within 5 %. The
> prediction that 13 inapplicable cells would return in 1–2 s each was **correct**; what was over-priced was the
> **applicable** cells, which ran 13 s – 6 m 39 s against an assumed ~270 s.
>
> **And the over-reserve was worth keeping.** The spare 1 h 14 m let wave 24 run **both** items at the top of its
> drop list (the S2 census, drop D1; item L24, drop D2). *A conservative derived price is a cheaper error than an
> aggressive one* — the correction to wave 23's under-reserve should not be applied in the other direction.
> Reproduce: `docs/synthetic-af-bank-followups-wave24-design.md` §7.5;
> `docs/synthetic-af-bank-followups-wave24-results.md` §12.

> ### THE S1 ROW IS NOW CORROBORATED BY THREE INDEPENDENT ARMS AND SHOULD BE TREATED AS SETTLED (2026-08-13, wave 25)
>
> Wave 25 ran the **same instrument at the same scenario** twice — a paired `synth-validate` S1 arm, 20 datasets
> scheduled per side, `--max-rounds 4`, `timeout 600` per cell, sequential, one `TestApp.exe` — on two different
> binaries. Both arms landed **18 of 20** rows.
>
> | arm | binary | window | wall | scheduled | **per cell** | rows |
> |---|---|---|---|---|---|---|
> | BEFORE | B14 (`476369a`) | `01:13:17Z → 02:27:21Z` | **4 444 s** | 20 | **222 s** | 18 |
> | AFTER | B15 (`29665a6`) | `03:44:22Z → 05:03:49Z` | **4 767 s** | 20 | **238 s** | 18 |
>
> **Both land within 6 % of this entry's recorded `S1 mean 234 s`**, measured by wave 24 on a different arm. The
> S1 budget row is corroborated on three arms and ~80 cells and needs no further pricing work.
>
> **`D05_tec140_1000mm` and `D19_cygnus_deep_shed` timed out on BOTH arms**, `exit=124` at `timeout 600`, no
> report, no manifest row. That is now **four consecutive S1 arms** (wave 23's, and both of wave 25's) losing
> exactly these two cells to exactly this timeout. **They are a structural property of the instrument at
> `timeout 600`, not a flake**, and any wave that needs them must raise the timeout — which changes the
> instrument and breaks comparability with every prior S1 number. Wave 25 kept `timeout 600` deliberately for
> that reason and named both cells before the data.
>
> **Design-time pricing, and the paired-arm correction that made the wave land.** Wave 24 §14 priced wave 25 at
> ~3 h 15 m by costing **one** S1 arm for a **paired** comparison. Wave 25's design §1.1 caught the arithmetic
> before starting and re-priced it at ~3 h 27 m TestApp / ~4 h 15 m critical path. **Actual TestApp wall:
> 74 m + 43 m + 79 m = 3 h 16 m** — inside the corrected band, and outside the original estimate's accounting.
> *A paired arm is TWO arms* belongs next to this entry's other pricing rules.
>
> | wave-25 step | priced | actual | ratio | priced from |
> |---|---|---|---|---|
> | S1 BEFORE arm, 20 cells | ~80 m (band 70–100) | **74 m 04 s** | **0.93×** | measured, same instrument + scenario |
> | `optimize` gate, 8 runs | ~42 m | **42 m 43 s** | **1.02×** | measured, eight consecutive waves at ~5.2 m/run |
> | S1 AFTER arm, 20 cells | ~85 m (*derived*, band 70–110) | **79 m 27 s** | **0.94×** | derived from the BEFORE arm + floored cells using more rounds |
>
> **The derived AFTER price was right, and right for the right reason.** The AFTER arm cost **323 s** more than
> the BEFORE arm, and **266 s of that (82 %) is the three labelled control cells** (`D01` 97→210 s, `D02`
> 113→217 s, `D03` 69→118 s), each of which took a second round where it previously stopped after one. The extra
> time is localised to exactly the cells that engaged the new code path.
>
> **And a third population on which R² is no defence.** `D01_ultrawide_40mm`/S1 stalls at
> **`R² = 0.99999999999994`** with `sampledHfrRange = 1.4628` and a **resolved** half-width. This entry's wave-10
> diagnosis — *"R² measures fit to the SAMPLED points and says nothing about whether the vertex is identifiable
> from them"* — now has three independent instances (wave 10's noise sweep, wave 24's `D03` at 0.9835, wave 25's
> `D01` at ~1.0). **The corrective variable is the sampled HFR range, not the fit quality**, and wave 25 shipped
> the bound that uses it ([F81](#f81--maxhalfwidthsampledhalfspanmultiple-has-been-a-ceiling-with-no-floor-and-the-half-width-unresolved-exit-returned-before-the-ceiling-was-consulted-at-all)).
> Reproduce: `/mnt/d/hf_w25/w25_before.log`, `/mnt/d/hf_w25/w25_after.log`, `/mnt/d/hf_w25/gate_w25.log`;
> `docs/synthetic-af-bank-followups-wave25-results.md` §12.

### F22 — Detection binning is a hard threshold on a measurement that under-reads, so boundary rigs get the wrong factor
**Status:** Open · found 2026-08-02 running the synthetic bank's S0 control

`DetectionBinningResolver.RecommendFromHfr` is `clamp(round(hfr / 3), 1, 4)` — a hard threshold with its 1→2
boundary at **4.5 px**. The HFR it is given is the detector's *measured* in-focus HFR, which departs from the
optical HFR by 2–10% usually but by up to **38%** in the cases that matter. Rigs whose true HFR sits near 4.5 px
therefore land on the wrong side.

> **The "under-reads" in this entry's title is only half the story (recorded 2026-08-04, wave 4).** The departure
> is **bimodal**, not a systematic under-read — see the measurement below. It under-reads on the large-HFR rigs
> this entry is about, and *over-reads*, sometimes enormously, on small-HFR rigs. Both halves matter, and they
> matter to different entries.

**Evidence.** On S0, where the bootstrap already *is* each dataset's expected optimum, three datasets that need
binning 2 were told to use 1:

| dataset | optical HFR_min (captured px) | measured vertex HFR | under-read | recommended | correct |
|---|---|---|---|---|---|
| `D14_cdk14_2563mm_e47` | 5.3 | 4.92 | −7% | **2** | 2 |
| `D17_cdk14_oiii5` | 5.3 | 3.27 | **−38%** | **1** | 2 |
| `D15_cdk20_3454mm_e47` | 5.9 | 4.02 | −32% | **1** | 2 |
| `D12_c14_585_afbin2` | 5.07 | 3.94 | −22% | **1** | 2 |
| `D10_rc16_3250mm_sparse` | 5.6 | 5.66 | +1% | 2 | 2 |

The cleanest pair is **D14 vs D17**: *identical* optics (2563 mm f/7.2, ε=0.47), *identical* pixel size (3.76 µm),
so identical true HFR — and opposite recommendations. The only differences are the filter and exposure (L at 0.5 s
vs OIII 5 nm at 30 s), i.e. the star population and SNR. The measurement, not the optics, decided the answer.

**Why it matters.** Binning is the highest-leverage knob for a long-focal-length rig — it is why
`DetectionBinningResolver` exists — and the wizard's gate on it is fit quality (R² ≥ 0.9), which is satisfied
here (R² = 1.0000). So the recommendation is delivered with full confidence and is wrong. Worse, it is
*bistable*: the same rig can be told 1 on a narrowband night and 2 on a luminance night.

**Confirmed as an F23 symptom (2026-08-03) — measured with a toggle, not inferred.** The marginal-SNR
false-positive term built in F23 wave 1 is switchable (`--marginal-snr-strength`) and changes *only* the
objective, so the same frames and the same seed can be scored with the optimizer landing at Sensitivity 0 or
above it. `D17_cdk14_oiii5` scenario S0:

| objective | landed Sensitivity | measured in-focus HFR | binning recommended |
|---|---|---|---|
| term OFF (shipping) | 0.0 | **3.27 px** | 1 |
| term ON | 7.0 | **4.66 px** | **2** |

The 4.5 px threshold sits between the two readings, so the Sensitivity landing *alone* flips the factor — a
30% shift in the measured HFR from nothing but admitted noise. The same signature appears on
`D12_c14_585_afbin2` S6 round 2 (Sensitivity 0 → 6 moves the in-focus HFR 2.99 → 4.45, +49%) and on
`D08_c11_2800mm` S0. This is the causal chain the design spec asserted, now measured: **noise blobs admitted
at a floored Sensitivity pull the in-focus HFR median down, and on a boundary rig that flips the binning
factor.**

The term does **not** ship (it fails its acceptance gates — see [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)),
so F22 still reproduces in shipping behaviour. What is settled is the *attribution*, and with it that
calibrating the HFR bias would have been the wrong fix — the bias is not a property of the measurement, it is
a property of what the optimizer chose to detect.

**And the departure is BIMODAL, which reverses its sign on the rigs F20 is about.** Measured-vs-optical in-focus
HFR across all 17 datasets (originally from the AF-recommender-hardening design spec, PR #167, which was closed
as superseded — this is the part of it that survived re-measurement):

| regime | datasets | measured vs optical |
|---|---|---|
| HFR ≳ 1.8 px | D05–D15, D17 | **−4% to −23%** (under-reads) |
| HFR ≲ 1.1 px | D01–D04, D16 | **+26% to +234%** (OVER-reads) |

The over-read at small HFR is the **pixelization floor**: a star whose flux lands in essentially one pixel still
measures a few tenths of a pixel, so the measurement cannot follow the optics down. `D01_ultrawide_40mm`'s
optical vertex is **0.231 px** and it measures **0.77 px**. That is independently corroborated three ways: the
bank's own truth model carries `hfrMinEffective = 0.7` for D01 (`synthetic_meta.json`), the derivations use a
0.70 px floor for R2, and [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it)'s
live pre-search fit independently read **0.762 px** against the same 0.238 px truth.

**Next step.** Two candidates, not mutually exclusive. (a) ~~Calibrate out the bias~~ — **ruled out twice over**:
the departure is not systematic (it tracks the Sensitivity landing) *and* it is not even one-signed (it inverts
below ~1.1 px). (b) Add hysteresis or a dead band around the 4.5/7.5 px boundaries so a marginal rig does not
flip between sessions, and say "borderline" in the UI rather than presenting a coin flip as a recommendation.

> **Correction to this entry's own compounding note (wave 4).** It previously read "the same under-reading pushes
> small-HFR rigs toward the `MinHFR` cliff." That is **backwards in direction**: below ~1.1 px the measurement
> *over*-reads, so it makes a rig look FURTHER from the gate than it is. The real compounding with
> [F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic) is subtler and
> worse: the over-read is floored near 0.7 px while `MinHFR` defaults to **1.2**, so an undersampled rig is still
> gated — the measurement simply **understates how far below the gate it really sits**. That is exactly why F35
> concluded the fit may only TRIGGER the seed and must never SIZE it.

### F27 — The optimizer cannot reach the rejected-candidate diagnostics an approved spec says it can
**Status:** **Done** (2026-08-03, wave 2 — the spec was never committed; record corrected and the seam documented in code) · found 2026-08-03 implementing [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)

[`docs/af-recommender-hardening-design.md`](af-recommender-hardening-design.md) lists "the rejected-candidate
diagnostics (`CollectRejectedCandidateDiagnostics`)" among the signals "already collected" and available to
build a false-positive term from. They are not reachable from the optimizer's evaluation path.

**Evidence.** `RejectedCandidateRecord`s are produced onto `HocusFocusStarDetectorResult.RejectedCandidates`
(`IStarDetector.cs:981`, set in `StarDetector.cs:878`). But `HocusFocusStarDetection.BuildStarDetectionResult`
copies only `Metrics` onto the `HocusFocusStarDetectionResult` the optimizer consumes
(`HocusFocusStarDetection.cs:818`), and that type has **no `RejectedCandidates` member at all**
(`HocusFocusStarDetection.cs:180-213`; the identifier does not appear anywhere in that file). The optimizer's
per-frame contract carries exactly three metric scalars — `RelaxationAdmittedCount`, `LowSensitivityCount`,
`TooFlatCount` (`RunEvaluationData.cs:68,76,86`). Only the review/feedback path re-detects with the flag on,
and it does so outside the optimizer loop (`Review/FrameReviewBuilder.cs:163-184`).

**Why it matters.** It is a load-bearing claim in an approved spec — F23's wave 1 was written assuming a
choice of three proxy signals and in fact had one. The gap is cheap to close: the flag is on the detection
cache-key **denylist** (`IStarDetector.cs:523-542`), so enabling it inside the optimizer would invalidate no
memo and no early-context key; the only cost is per-detection allocation in the hot loop.

**Resolved 2026-08-03 by correcting the record, not the code.** Every code claim above was re-verified line by
line and holds at HEAD. Two things changed the resolution:

1. **The spec was never merged.** `docs/af-recommender-hardening-design.md` is absent from `develop`; it exists
   only on the unmerged branch `ghilios/af-recommender-hardening-design` (`8b7867c`). Three merged documents
   linked to it, so every one of those links dangles for anyone not sitting on that branch. They now point at
   [`plans/af-recommender-hardening-plan.md`](../plans/af-recommender-hardening-plan.md), the design of record on
   `develop`. (Worth stating precisely: a first pass at this entry said the spec "was never committed and no
   revision of it exists", which is wrong in the direction that would have justified rewriting it from
   scratch.)
2. **The plan already had it right.** Its "signals already available" table reads `CollectRejectedCandidateDiagnostics
   records | no — RejectedCandidates never reaches HocusFocusStarDetectionResult`. So the false claim lived only
   in the uncommitted spec, and the committed artifact contradicted it correctly the whole time. Wave 1 was
   written from the version that was wrong.

**Not plumbed, deliberately.** Adding `RejectedCandidates` to the optimizer's contract buys per-rejection detail
nothing currently needs, at a per-detection allocation cost in the hot loop. The seam is instead **documented
where someone would look for it** (`Interfaces/IStarDetector.cs`, on `RejectedCandidateRecord`), so the next
person to assume the optimizer can see these records is told otherwise by the type itself rather than by a
design document that may not survive. The `*Bounds` rect lists are noted there too, with the caveat the original
entry left out: they carry geometry only — no measured value, no threshold — so they answer a strictly weaker
question and are not a drop-in substitute.

**Also fixed:** the `AllBoundsLists()` doc comment said "seven" and "an eighth in the future" while the helper
returns **nine** — it had drifted when `TooElongatedBounds` and `BloomSuppressedBounds` were added. The
`RejectedCandidateRecord` comment repeated the same stale count, and the true relationship is more lopsided than
either number suggests: `RejectionGate` has **twelve** constants, the nine `*Bounds` lists cover **eight** of
them (TooSmall, OnBorder, HFRAnalysisFailed and TooLowHFR have none), and the ninth — `SaturatedBounds` —
corresponds to no gate at all, because saturated stars are KEPT and masked during the PSF fit rather than
rejected. So "the `*Bounds` lists cover most gates" was making the gap look smaller than it is.

### F28 — `LowSensitivity` reads exactly zero precisely when the Sensitivity gate has collapsed
**Status:** **Done** (2026-08-03, wave 2 — discriminator now gated on the gate being able to reject; user-visible) · found 2026-08-03 implementing [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)

The gate rejects on `sensitivity <= p.Sensitivity` (`StarDetector.cs:1763`), and every candidate's
`sensitivity` is bounded below by `PeakResponse × EffectiveClipMultiplier` — 0.75 × 2.0 = **1.5** at shipped
defaults. So a gate anywhere below 1.5 rejects **nothing**, and `StarDetectorMetrics.LowSensitivity` is
identically 0.

**Derivation.** Clip survivors satisfy `raw > background + clipMargin` with
`clipMargin = EffectiveClipMultiplier · σ` (`StarDetector.cs:2179`), so `meanFlux > EffectiveClipMultiplier · σ`;
`peak ≥ meanFlux`; and `NormalizedBrightness = peak − (1 − PeakResponse)·meanFlux ≥ PeakResponse · meanFlux`
(`StarDetector.cs:2274`). Hence `sensitivity = NormalizedBrightness / σ > PeakResponse × EffectiveClipMultiplier`.

**Why it matters.** `FrameLowSensitivityCounts` exists specifically to separate "the gate is holding stars
back" from "there is nothing left to find" (`OptimizationObjective.cs:194-199`), and
`ExposureRecommender.Recommend` turns it into `gateIsHoldingStarsBack = gateRejectedCount > 0`, which is the
sole discriminator between `StarCountIsTheLimit` and `StarFieldIsExhausted` (`ExposureRecommender.cs:515-517`).
The exposure advice is surfaced only when `HasLowStarSignal` — i.e. `Sensitivity ≤ 1.0`
(`StarDetectionOptimizerWizardVM.cs:248`, `ExposureRecommender.cs:284,336`) — which sits strictly inside the
provably-inert region. So on exactly the population the affordance was written for, the evidence test is
structurally dead: `StarCountIsTheLimit` can never fire, `StarFieldIsExhausted` is always taken, and the
recommender always concludes the field has nothing more to give.

**Why it did not show up before.** The zero-rejections test was added from a real rig where it was correct
and valuable (2 s → 5 rejections, 14 s → zero; the comment at `ExposureRecommender.cs:511-514` records it).
That rig was not at the search floor. The defect is the *interaction* with the floor, not the test.

**Fixed 2026-08-03 (wave 2).** The discriminator is now conditional on the gate being able to reject at all.
`StarDetector.InertSensitivityBound(p)` = `PeakResponse × MinEffectiveClipMultiplier(p)` is computed from the
run's own params — never hard-coded, because both factors are searched axes and F23 wave 1 measured landings at
`StarClip` 6.25 and 6.75 where the bound is over 4× the default. `ExposureRecommendation` gains
`InertGateBound` and `GateIsProvablyInert`; an inert gate now falls to the **probe** (which ships with its own
stopping rule) rather than to a verdict of exhaustion nothing in the run supports, and the derivation tooltip
says why the rejection count carries no information.

**This is user-visible**, and narrowly so: signal-sufficient runs where every frame is short of the star-count
target AND the gate sat below its own inert bound move from "no exposure offered" to a 2× probe. The rig that
motivated the zero-rejections test is untouched — it rejected 5 candidates at 2 s, so its gate was demonstrably
live, and the two populations are disjoint by construction (`gateIsProvablyInert` requires
`gateRejectedCount == 0`). An observed rejection refutes the derivation and wins.

The entry's closing warning stands and is worth repeating: **never use this counter as a false-positive
signal.** The pathological landing produces its cleanest possible value.

### F30 — A stored `optimized_settings.json` does not say which config produced it
**Status:** **Done** (2026-08-03, wave 2 — provenance block added, schema 3) · found 2026-08-03 pinning the [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall) baseline

**Retraction first.** This entry was originally filed as "the published V2 config-A landings do not
reproduce". **That was wrong, and the error was mine, not the tool's.** Re-running `optimize --per-run` at
HEAD reproduces the V2 config A *exactly*: the same six datasets at `BrightnessSensitivity` 0.0 (D09, D10,
D11, D12, D15, D17 — precisely the design spec's Evidence-1 list) and identical `bank-verify` precision on
**all 17 datasets**, to every published digit. C0@nc2 likewise reproduces exactly. The optimizer, the bank,
the detector and the scoring chain are all reproducible.

**What actually happened.** The `optimized_settings.json` copies sitting in each dataset's `attempt01/` were
compared against the published **config A** table and found not to match — 5 at Sensitivity 0.0 instead of 6,
D15 at 10.0 instead of 0.0. They did not match because **they were config B's landings**, written by the
`--donut` prepass that ran last (each carried `DefocusAwareDonutDetection: true`, which is the tell, and which
was visible in the file the whole time). Nothing was irreproducible; the artifact was simply misattributed.

**The real gap, which survives.** `OptimizedStarDetectionSettings` records `CreatedAtUtc`, `RunCount`,
`BaselineJ`, `FinalJ` and `SchemaVersion` — but nothing that identifies **which invocation produced it**. Since
[F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings) has the prepass overwrite these files in
place, a bank folder accumulates landings from whichever run went last, and there is no way to tell config A's
from config B's except by inferring it from `DefocusAwareDonutDetection` — an inference that is only valid
while `--donut` is the only thing that varies between prepasses. It stops being valid the moment two arms
differ by anything else, which is exactly what F23 wave 1 did (three arms differing by objective constants and
search domain).

**Why it matters.** The misattribution cost real time and produced a wrong followup entry that was committed
twice before the control arm disproved it. A one-line provenance field would have made it impossible.

**Fixed 2026-08-03 (wave 2).** `OptimizedStarDetectionSettings` gains an optional `Provenance` block
(`Producer`, `CommandLine`, `SettingsFingerprint`, `ProducerVersion`) and the schema goes to **3**. The
fingerprint hashes the harness settings' *semantic* content — the parsed option bag, key-sorted, plus the
resolved pixel-scale inputs — not the file's bytes, so re-exporting or re-indenting a settings file does not make
a landing look as though it came from a different configuration.

**Degrades in both directions.** The block is omitted from the JSON entirely when null, so a snapshot written by
the shipping plugin is byte-identical to before; a v1/v2 file still loads with `Provenance` null, which reads as
**unattributable** and must never be read as "matches me"; and the tilt wizard's `CaptureDetectionSettings` —
which snapshots *live* settings rather than an optimizer result — deliberately keeps it null, because stamping a
producer on it would be a lie. `Clone()` deep-copies it: `MemberwiseClone` is shallow, so without that every copy
would alias one instance.

The underlying overwrite ([F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings)) is unchanged — a
bank folder still accumulates whichever prepass went last. What changes is that the survivor now says so.

### F31 — ~~Synthetic-bank precision is NOT exact: the golden omits real stars, and they score as false positives~~ — FIXED 2026-08-03; the title is the DEFECT, not the current state
**Status:** **Done** (2026-08-03, wave 2 — repaired, validated against a null control, and everything re-baselined at `afbank-verify/5`) · found 2026-08-03 verifying the F23 wave-1 result · **INVALIDATED F23's evidence base**

`docs/synthetic-af-bank-baseline-results.md` headlines the synthetic bank with "Golden precision (D06,
`golden eval`) **1.000** — 205 TP, **0 FP**. Exact, not a lower bound." That claim does not generalise. Measured
against each frame's own `*.truth.json`, **96% of the false positives the bank reports are real rendered stars.**

**Evidence.** `D09_c14_3800mm`, config A as landed by an unmodified-HEAD control arm, all 9 frames, 12 px
centroid match:

| | count |
|---|---|
| detections | 510 |
| scored FP against `*.golden.json` | 280 |
| of those, a **real truth star** within 12 px | **269 (96.1%)** |
| genuinely spurious | **11** |

The 269 break down by truth tier as **198 `omitted`** and **71 `unresolved`**. Re-scored against truth, the
four datasets that drove the F23 finding all sit at 0.95–1.00 precision on **every** arm:

| dataset | true precision (control / floor / term) | golden-scored (what F23 used) |
|---|---|---|
| D09_c14_3800mm | **0.978** / 1.000 / 1.000 | 0.525 / 0.991 / 1.000 |
| D10_rc16_3250mm_sparse | **0.946** / 1.000 / 0.993 | 0.634 / 0.933 / 0.974 |
| D11_rc10_585_afbin2 | **1.000** / 1.000 / 1.000 | 0.871 / 1.000 / 0.967 |
| D12_c14_585_afbin2 | **1.000** / 1.000 / 1.000 | 0.899 / 0.736 / 0.905 |

**Mechanism.** `GoldenFromTruth` tiers truth stars by native **peak-pixel** SNR (`GoldenFromTruth.cs`, thresholds
`goldenHighSnr` 20 / `goldenMediumSnr` 10 / `goldenLowSnr` 5 / `goldenUnresolvedSnr` 3.5 in
`SynthBank/synthetic-bank-spec.json`). Below 3.5 a star is tiered `omitted` and appears in **neither** `stars`
**nor** `unresolved` — so `GoldenMatch.ExcludeUnresolved` (`GoldenEvalRunner.cs:261-266`) cannot protect it, and
`bank-verify` does not call `ExcludeUnresolved` at all. Defocus destroys peak-pixel SNR while leaving integrated
flux intact, so the golden evaporates toward the sweep wings: D08 holds **81 golden stars at focus and 9 at the
extreme frame**, against 123–126 truth stars per frame throughout.

**Coordinate alignment was null-tested** before believing this: detections match truth at 100% as-is, 2.3% at
0.5× or 2× scale (chance), 0% under a 300 px shift.

**Why it matters — this inverts F23.** F23's headline is "C0 precision never drops below 0.942; config A reaches
0.451, so the optimizer trades precision away for marginal recall." The real mechanism is the opposite: C0 at
`Sensitivity` 10 detects only bright stars, all of which are in the golden; config A at `Sensitivity` 0 detects
**many more real but faint stars**, which the golden omits, and the metric charges every one as a false positive.
The control arm detects **510** stars on D09 at **97.8%** true precision where the term-on arm detects **231** at
100% — so both F23 wave-1 mechanisms were suppressing *real detections*, not junk.

**Why it matters — this also inverts the bank's selling point.** Precision against the synthetic golden is a
**lower bound**, for exactly the reason [F11](#f11--precision-is-a-lower-bound-on-runs-whose-faint-tier-was-budget-truncated--re-run-these-with-more-montages)
gives on the real bank: the reference is incomplete below the tier cut. The synthetic bank's claim to measure
precision *exactly* is what justified building it, and as implemented it does not hold.

**Re-scored with the repair in place (afbank-verify/4).** Control arm, config A, 17 datasets: the `/3`
golden-only metric gave 0.451–0.992 with 8 datasets below 0.90; the repaired metric gives **0.982–1.000**,
none below 0.90. The detector's real false-positive rate on this bank is **0–1.8%** at every configuration
tested. The residual is real rather than noise — the only four datasets short of 1.000 are D09 (0.991),
D17 (0.986), D15 (0.988) and D10 (0.982), i.e. the long-focal-length rigs landing at Sensitivity 0 plus the
sparse field. That is F23's predicted effect at roughly **1/30th** the size of the artifact that masked it.

**A caution for whoever re-baselines.** The first cut of the repair sized protection by the star's light
footprint (2·HFR, ~40 px on a wing donut). That removed the bias and replaced it with **saturation**:
precision read 1.000 on all 17 datasets for all three arms — which looks like a clean result and measures
nothing. Protection is now the match radius exactly. Before trusting a re-baseline, check that precision
still SPREADS across datasets; all-1.000 means the metric is saturated again, not that the detector is
perfect.

**Closed 2026-08-03 (wave 2), with the instrument validated before anything was baselined against it.**

**The validation came first, and it was cheap.** `golden eval` writes `detected_f<focuser>.csv` per frame, so 30
saved configurations (D09/D13/D17 at nine Sensitivity values, plus D10/D11/D12) could be re-scored **offline**
under four policies with the detector never running again. Re-implementing `GoldenMatch` in Python reproduced
the published C# `/3` numbers exactly — D09 s0 0.8037 vs 0.804, D17 s0 0.6974 vs 0.697, D13 s0 0.8504 vs 0.850 —
which is what made its other columns believable. Minutes of work; it would have caught this before wave 1 began.

What it showed:

- **The metric is NOT saturated.** A null control (the same detections translated with wraparound — count and
  clustering preserved, correspondence destroyed) scores **0.000–0.012**. The 2·HFR saturation this entry warns
  about would have shown up as a high null. It does not.
- **Three independent policies agree to within 0.006** at 0.98–1.00: the as-implemented `/4` metric, a variant
  whose protection predicate is exactly the matching predicate, and direct truth scoring.
- **The `/3` violation, counted:** detections charged as a false positive while sitting within the match radius
  of a real rendered star number 52/318 on D09 s0, 79/337 on D17 s0, 139/1075 on D13 s0. Under `/4`, **0 on all
  30 configurations**.
- **A residual asymmetry in the `/4` repair, always flattering.** `GoldenMatch.Covers` excludes on
  `centre-in-box OR IoU(box, det.bbox) > 0`, so the *detection's own bounding box* dilates every protection box
  and a wide donut detection is protected well past the match radius — despite the class comment claiming
  protection is "exactly as generous as matching". Measured: D09 s0 reads 1.0000 under the implemented predicate
  against 0.9909 under the symmetric one. Small (≤0.011), systematic, one-directional.

**Shipped as `afbank-verify/5`.** Truth protection now uses the centroid predicate matching itself uses (the
golden's own `unresolved` boxes keep `Covers`, since that is the real bank's path and must not move), and every
config row now reports `precisionNull`, `truthViolations` and `scoredFraction`. Next-step (1) — score directly
against truth — was measured and found to agree with the repaired golden+protection scoring to within 0.006, so
it was not worth a second scoring path; next-step (2) is moot.

**Next-step (3) is done.** The V3 matrix in
[`synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md), a new measured
[`synthetic-af-bank-baseline.json`](synthetic-af-bank-baseline.json), re-derived `precisionMin` bands in
[`synthetic-af-bank-expectations.json`](synthetic-af-bank-expectations.json) (the old 0.95/0.98 were fitted to
the broken metric and were *not* carried forward), and [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)
/ [F24](#f24--donut-detection-costs-precision-even-where-donuts-exist-and-badly-where-they-do-not--it-costs-recall-where-it-is-not-needed)
both re-measured — F23 void as written, F24 refuted and restated as a recall finding.

**The lesson, stated so it outlives the bug.** Reproducibility validated nothing here. Every check confirmed
determinism, and a deterministic pipeline reproduces a systematic error perfectly; the bias lived in the
reference all the checks shared. What caught it was scoring against an *independent* source. And a metric can
fail in two directions — biased, then saturated — which is why `precisionNull` now ships alongside every
precision figure rather than being something a reader has to think to ask for.

### F32 — `J` is saturated near 1.0, so the optimizer trades enormous recall for numerically trivial gains
**Status:** **ANSWERED 2026-08-07 (wave 9): the confirmation arm ran on both full banks and phi = 0.50 does NOT
ship.** R1(c) fails catastrophically (σ_focus x2000 worse on `vsn07`), R3 fails on the newly-covered population,
and R2 INVERTS wave 6 — restarts recover 228 % of the floor's gain, so the floor is not a distinct mechanism.
`MinDetectionKeepFraction` stays default OFF permanently. **The greedy trap itself is confirmed and stands**; the
floor is simply the wrong instrument for it · found 2026-08-03 re-reading the wave-1 real-bank control arm

The objective's landings are not close calls. Across the 17 scorable real-bank runs, `optimize --per-run` gives
up a **median 0.243 of recall@SNR≥12** to gain a **median ΔJ of +0.0125** — and the worst cases are far starker
than the median.

**Evidence.** `bank-verify --runs "D:\\Autofocus Bank" --opt-a` (wave-1 control arm, `afbank-verify/3`, 19 runs,
0 failed), against each run's own `BaselineJ`/`FinalJ` from its stored landing:

| run | recall@≥12 C0 → A | Δrecall | ΔJ | landed Sensitivity |
|---|---|---|---|---|
| `toml999` | 0.819 → 0.357 | **−0.462** | **+0.0002** | 33.3 |
| `muggsie` | 0.879 → 0.512 | −0.368 | +0.0039 | 17.2 |
| `CWhiteFocus` | 0.810 → 0.327 | −0.483 | +0.0042 | 50.0 |
| `uneven` | 0.931 → 0.319 | −0.613 | +0.0046 | 31.2 |
| `bobp` | 0.580 → 0.495 | −0.086 | +0.0041 | 10.0 |

`toml999` is the entry's clearest statement: **two ten-thousandths of `J` bought with 46 points of recall.** At
`BaselineJ` values of 0.98–0.999 there is almost no headroom left, so every remaining move is a rounding error in
the objective and a catastrophe in the star list. The search is behaving correctly; the scale it is climbing has
run out.

**Why it matters.** This is upstream of [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)
and of [F4](#f4--the-objective-has-no-sensor-model-term). A precision term, a sensor term, or any other new term
added to a `J` that already sits at 0.998 will be competing for the same exhausted fourth decimal place. It also
explains why F23's wave-1 mechanism (a) could produce real precision movement and still clear no acceptance gate:
the term was fighting for headroom that does not exist. And it reframes the wizard's own presentation — a landing
reported as an improvement over the current settings is, on most runs, an improvement too small to mean anything
while the change in what gets detected is enormous.

**Next step.** Before adding any further term to `J`, measure its dynamic range on both banks: the distribution of
`FinalJ − BaselineJ` over the runs, and the recall/precision movement per unit `J`. If the trade rate is what
these numbers say, the fix is to rescale or re-anchor the objective (or gate acceptance on the *magnitude* of the
improvement) rather than to add a term. Cheap to check: the numbers above come from files already on disk.

**Measured 2026-08-03 (wave 3).** Done, from files already on disk, over all three arms. `BaselineJ` and
`FinalJ` come from each run's stored landing; recall from the `/5` synthetic verify and the `/3` real one —
valid because `recallHigh` derives only from `match.Pairs` (`BankVerifyRunner.cs:488`) and the `/5` repair
touched only the false-positive list, so the real bank's *precision* is void but its *recall* is not.

| arm | median `BaselineJ` | median Δ`J` | median Δrecall@≥12 | **median trade rate** (Δrecall per unit Δ`J`) |
|---|---|---|---|---|
| synthetic A | 0.952 | +0.0101 | **0.000** | **0.00** |
| synthetic B | 0.992 | +0.0021 | −0.014 | **−1.01** |
| **real A** | 0.977 | +0.0126 | **−0.243** | **−17.32** |

Three things follow, and the third is the one that changes the plan:

1. **The headline number reproduces exactly.** Median Δrecall on the real bank is −0.243, as recorded above.
2. **The worst case is far worse than "0.0002 for 46 points".** Expressed as a rate, `toml999` gives up **3018
   points of recall per unit of `J`**; `uneven` −134, `CWhiteFocus` −116, `muggsie` −94. The fourth decimal is
   not a rounding error, it is the entire remaining scale.
3. **The two banks disagree in SIGN, not just in magnitude** — the synthetic bank's median trade rate is
   **0.00**, i.e. config A costs the synthetic bank no recall at all while costing the real bank a quarter of
   it. So the synthetic bank cannot measure this defect, and
   [F33](#f33--the-synthetic-bank-does-not-reproduce-the-real-banks-optimizer-failure-mode)'s rule applies to
   any proposed rescaling as much as to a new term. **A candidate objective change must move the real-bank
   trade rate, and the synthetic bank cannot tell you whether it did.**

Reproduce: `D:\hf_w3\f32_dynrange.py` (reads `hf_w2/verify_v5`, `hf_f23/verify_real_H`, and the `H_A` / `B_A`
/ `H_real_A` landings; no detector run).

**SHIPPED 2026-08-05 (wave 5) as an acceptance constraint — and the arms overturn this entry's own framing.**
`OptimizerSettings.MinDetectionKeepFraction` rejects a candidate keeping less than φ of the SEED's accepted stars
(min over runs) **ahead of** the `j > bestJ` compare at all three accept sites. `J` is never multiplied or
re-anchored, so landings stay comparable to every prior arm. Default null; **inertness measured against the
parent commit with settings pinned: bit-identical**. Multi-pass callers pin round 0's seed totals (at φ=0.5,
three `--continue-rounds` would otherwise reach 0.125 of where the user started).

**32 optimizations, φ ∈ {0.30, 0.50, 0.75} + a feature-OFF control, one binary, `--settings` pinned
([F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)).
`BaselineJ` identical across all four arms of every run ([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s
tell), so the comparison is valid.**

**The headline is not what this entry predicted: on most binding runs the constraint IMPROVES `J` while keeping
2–4× more stars.** At φ = 0.75, **5 of 7** binding runs land at a higher `J` than the unconstrained search found:

| run | keep off → φ=0.75 | Δ`J` | σ_focus vs unconstrained |
|---|---|---|---|
| `CWhiteFocus` | 0.429 → **1.819** | **+0.00139** | **0.156×** |
| `uneven` | 0.353 → 0.937 | +0.00165 | 0.785× |
| `D19_cygnus_deep_shed` | 0.600 → 1.276 | +0.00024 | 0.364× |
| `toml999` | 0.397 → **1.163** | +0.00054 | 0.959× |
| `D18_m24_deep_shed` | 0.511 → **1.819** | +0.00009 | 1.337× |
| `muggsie` | 0.612 → 0.805 | −0.00234 | 1.676× |
| `mccomiskey` | 0.095 → 0.771 | −0.03097 | 2.812× |

**A constrained maximum cannot exceed an unconstrained GLOBAL maximum**, so this proves the unconstrained search
**was not finding the global optimum**. The shedding corner is substantially a **greedy trap**, not the rational
purchase this entry and wave 4 concluded it was — the mechanism `RevertNeutralAxes` already documents, one level
up: Phase A grids Sensitivity × StarClip with Sensitivity as the OUTER loop, the winning StarClip is discovered
in a shedding row, and strict `j > bestJ` freezes it there. **"It is genuinely buying a much better fit with the
stars it discards" is true relative to the baseline and false as a claim about the best available trade.**

**Inertness measured 12 times, 9 bit-identical** — D20 at all three floors, D19 and muggsie at two each, toml999
and uneven at φ=0.30. The 3 that changed are the pre-registered caveat (a landing can be feasible while a
candidate *visited on the way* was not); two changed for the better, one by −0.0001 of `J`.

**Exact recall on the synthetic arms: precision 1.000 and FP 0 at every floor** — every star won back is real.
D18 recall@high 0.607 → **0.945** at φ=0.50; D19 0.968 → 0.984 with **2×** the detections at φ=0.75; **D20
identical to the last detection at all four arms**. Note φ=0.75 **overshoots on D18** — its effective gate
collapses to 0.234, the Sensitivity-floor pathology reached from the other side.

**Recommended floor: φ = 0.50**, by the rule fixed in advance (*the smallest φ meeting the criteria*). φ=0.30
does not address the defect (only `mccomiskey` binds, and it pays). **Adoption still needs the confirmation arm**
— both full banks plus `bank-verify` for real-bank recall, since the efficacy criterion could only be evaluated
by keep-% proxy here. Ships default OFF until then. Reproduce: `D:\hf_w5\f32_arms.sh`,
`D:\hf_w5\scorecard.py`, `D:\hf_w5\score_f32_synth.sh`.

**CONFIRMATION ARM RE-VALIDATED AS THE RIGHT EXPERIMENT (wave 6), against a pre-registered alternative.** Before
spending ~6 h, the competing hypothesis was tested: if the unconstrained search is merely *stuck*, any RESTART
should recover the floor's gain with no floor at all. `--continue-rounds` already is that mechanism — each round
re-seeds from the prior best with a **fresh** curated set, resetting the pattern-search stride. **Arm R:** the same
8 runs, `--continue-rounds 2`, **no keep floor**, same binary, `--settings` pinned.

**First, the control.** Arm R's round 0 is **identical to wave 5's feature-OFF arm on all 8 runs, to 6 dp** —
0.997993 / 0.998476 / 0.996368 / 0.997195 / 0.994025 / 0.999822 / 0.999557 / 0.999766. Two waves, two binaries,
one pinned settings file, byte-identical landings. [F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s
check passing this cleanly is what makes the rest of the table readable.

| run | round 0 (= wave-5 OFF) | **Arm R final** | Δ from restarts | wave-5 φ=0.75 | Δ from the floor | **restarts recover** |
|---|---|---|---|---|---|---|
| `toml999` | 0.997993 | 0.998068 | +0.000075 | 0.998536 | +0.000543 | **13.8%** |
| `CWhiteFocus` | 0.998476 | 0.998650 | +0.000174 | 0.999867 | +0.001391 | **12.5%** |
| `uneven` | 0.996368 | 0.996745 | +0.000377 | 0.998014 | +0.001646 | **22.9%** |
| `D18_m24_deep_shed` | 0.999822 | 0.999855 | +0.000033 | 0.999914 | +0.000092 | **35.9%** |
| `D19_cygnus_deep_shed` | 0.999557 | 0.999557 | 0 | 0.999800 | +0.000243 | **0%** |
| `muggsie` | 0.997195 | 0.997195 | 0 | 0.994854 | **−0.002341** | floor HURT |
| `mccomiskey` | 0.994025 | **0.997394** | **+0.003369** | 0.963058 | **−0.030967** | floor HURT |
| **`D20` (control)** | 0.999766 | 0.999768 | +0.000002 | 0.999766 | 0 | — |

**Three findings, and they are not the same finding.**

1. **The greedy trap is CONFIRMED as a fact, not an inference.** With no constraint whatsoever, simply restarting
   improves `J` on **5 of the 7 binding runs** (all but `muggsie` and `D19`; the `D20` control moves 2×10⁻⁶,
   i.e. not at all). A converged global optimum cannot be improved by re-seeding from itself, so
   round 0 demonstrably was not one. This is the same phenomenon as
   [F8](#f8--optimizer-landings-are-not-reproducible-across-invocations) — a landing is a property of the
   trajectory, not of the objective.
2. **But restarting is NOT a substitute for the floor.** On the five runs where the floor helped, restarts recover
   **0–36% (median 13.8%)** of the floor's gain. A restart changes the *trajectory*; the floor changes the
   *feasible set*, and it reaches a region three restarts do not find. **So the pre-registered rule fires:
   recovery < 50% on 5 of 5 → keep the confirmation arm as designed, at φ = 0.50.**
3. **The floor's two failures are exactly where restarts do best.** `mccomiskey` — the worst floor case at −0.031 —
   *gains* +0.0034 from restarts alone, and `muggsie` (−0.0023 under the floor) is untouched by them. The two
   mechanisms are complementary rather than competing, so **the confirmation arm should carry a third
   `--continue-rounds` arm** rather than being floor-vs-nothing. That is a change to the experiment, and it costs
   one more arm, not six hours more.

> **Instrument caveat found while reading Arm R, and it would have inverted the result.** The end-of-run
> `detections kept vs seed` line reads **≈ 1.0 in every multi-round run** (`toml999` 1.00069, `CWhiteFocus`
> 1.00259) — which looks like "the unconstrained search stopped shedding entirely" and is nothing of the sort.
> `DetectionKeepBaselineTotals` is pinned to round 0's seed **only when a floor is set** (`:690`), so with no floor
> each round's keep is measured against *that round's own seed* — the last round barely moves, so the ratio is ~1.
> Round 0's true keep is wave 5's control column (0.397, 0.429, 0.353, 0.612, 0.095). **A diagnostic whose meaning
> silently changes with an unrelated flag is worse than an absent one**; the pinning should apply to the readout
> whether or not a floor is in force. Reproduce: `D:\hf_w6\f32_armR.sh`.

**Wave 7 checked whether it was about to invalidate this arm, and it did not (2026-08-06).** Wave 7's three items
were expected to share a bank re-baseline, which would have made a confirmation arm on the new frames
incomparable with wave 5's φ table. Measured instead of assumed: **F19 decided "no change"** (so the exposure axis
of the derivation does not move) and **F39(b) needed no re-render at all** (the factor is detector-side; the
frames' exposures were already derived at binning 2). Nothing in wave 7 has re-rendered a frame, so `D18` / `D19`
/ `D20` — this arm's entire synthetic half — are bit-for-bit what wave 5 and wave 6 measured, and its other five
runs are on the real bank, which wave 7 never touches. **The arm is runnable and comparable TODAY.**

> ## THE CONFIRMATION ARM RAN (2026-08-07, wave 9). BOTH CONTROLS PASS, AND phi = 0.50 DOES NOT SHIP.
>
> Deferred across waves 5, 6, 7 and 8; run sequentially over **both full banks** (39 runs x 3 arms = 117
> optimizations, 9 h 24 m) after [F55](#f55--optimize-is-not-reproducible-when-several-instances-run-at-once-and-the-seed-evaluation-is-what-moves)
> voided a first attempt at fan-out 4.
>
> ### The controls, which is what makes any of it readable
>
> | control | fan-out attempt | **sequential** |
> |---|---|---|
> | **RULE G** — arm A vs wave 5's feature-OFF arm | FAIL 2 of 8 | **PASS 8 of 8 to 6 dp** |
> | **`BaselineJ`** — [F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s tell | 6 of 39 moved | **0 of 39 moved** |
>
> ### The verdict: 11 of 39 runs bind, and phi = 0.50 FAILS on three independent pre-registered rules
>
> | rule | measured | verdict |
> |---|---|---|
> | **R1(a)** median Δ`J` (B − A) on binding runs | **+0.000076** | pass |
> | **R1(c)** worst σ_focus regression | **`vsn07` 0.00083 → 1.71721** | **FAIL** |
> | **R2** restarts' recovery of the floor's median gain | **228 %** | **the floor is NOT a distinct mechanism** |
> | **R3** newly-covered binding runs | 6: **2 improved, 4 regressed** | **FAIL** |
>
> **R1(c) fails catastrophically, not marginally.** The bar was 20 %. `vsn07` degrades σ_focus by a factor of
> **2000** (0.00083 → 1.71721) and `FlyData` by a factor of 7.7 (0.16738 → 1.28554). On these runs the floor does
> not trade recall for `J` — **it destroys the focus fit**, which is the one thing the objective exists to protect.
>
> **R3 fails, and it is wave 8's `StructureLayers` lesson repeating exactly.** On the runs wave 5 never covered
> the floor regresses 4 and improves 2. Wave 5's evidence was 7 binding runs on a subset chosen because the floor
> looked good there; the population that did not motivate the hypothesis refuted it.
>
> **R2 INVERTS wave 6's finding, and this is the wave's most interesting result.** At 8 runs, `--continue-rounds 2`
> recovered **0–36 %** (median 13.8 %) of the floor's gain, which is what justified keeping the floor as a distinct
> mechanism worth confirming. At full-bank scale restarts recover **228 %** — they are strictly BETTER than the
> floor, on the floor's own binding set. Wave 6's conclusion was drawn from 7 runs and reverses on 11.
>
> **So: `MinDetectionKeepFraction` stays default OFF, permanently rather than pending.** It remains available as
> `--keep-floor` on the harness. **The greedy trap this entry discovered is real and is NOT withdrawn** — restarts
> demonstrably improve `J` on most binding runs — but the keep floor is the wrong instrument for it, and the
> honest product change is to expose RESTARTS (the wizard's "Continue optimizing" button already is one) rather
> than a feasibility constraint that can cost 2000× of σ_focus.
>
> ### WHAT SHIPS INSTEAD: the restart, exposed (2026-08-07, wave 9)
>
> R2 says restarts recover **228 %** of the floor's gain, so the mechanism ships as the affordance the user
> already had. **"Continue optimizing" IS a restart** — each pass re-seeds from the prior best with a fresh
> curated set, resetting the pattern-search stride — and it has always been on the Summary page. Nothing told the
> user when pressing it was worth anything.
>
> `StarDetectionOptimizerWizardVM.ContinueOptimizingAdviceText` is that missing half: when the landing kept less
> than **half** the stars its own seed did (φ = 0.50 reused as a DIAGNOSTIC, the one role this arm supports for
> it), the Summary says so and points at the button.
>
> - It **names a control that is present AND enabled** — gated on `CanContinueOptimization`, so it disappears at
>   the 3-round cap and while a pass is running (the house rule at `ShowOptimizeAgainAtRecommendedBinning`).
> - It **never names `MinDetectionKeepFraction`**, which has no XAML binding and which this arm measured as
>   harmful as a default.
> - It promises a **direction, not a magnitude** — restarts helped on most binding runs, not all.
> - It is **absent on healthy landings**, because a note that always fires says nothing.
>
> **Tests: 4, two of them discriminating**, each confirmed by neutralizing: removing the shedding threshold fails
> `ContinueAdvice_HealthyLanding_SaysNOTHING`; removing the `CanContinueOptimization` guard fails
> `ContinueAdvice_NeverPromisesAnActionThatCannotBeTaken`.
>
> **F15:** arm A ran LAST, so both banks hold the SHIPPED-DEFAULT landing — the correct resting state, and no
> re-land is owed since nothing was adopted. Reproduce: `D:\hf_w9\f32_arms.sh` (FANOUT=1), `D:\hf_w9\score_f32.py`.

**When F18's re-render does happen, the rule for this arm is fixed in advance:** the datasets whose `step*` moves
get their pre-wave frames preserved (`D:\hf_w7\oldframes`, as wave 6 did) and the arm runs on those; an arm run
on the NEW bank must re-run wave 5's φ arms on that bank rather than compare across it. That is
[F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary) applied to frames instead of to
binaries.

> ### THE SATURATION IS NOT ONLY A RECALL PROBLEM — ON AN UNLABELLED RUN `J` HAS NO PRECISION TERM AT ALL (2026-08-13, wave 26)
>
> This entry's shape is *"the optimizer trades enormous recall for numerically trivial gains"*. Wave 26 measured
> the same trade pointed at **precision**, and found the reason it is free rather than merely cheap:
> **`JRun` composes without the `Wl · sLabel` term whenever a run carries no labels**
> (`OptimizationObjective.cs:484-490`), and **the whole synthetic bank runs unlabelled**. So on this bank `J`
> contains **no precision term at all** — not a small one competing for an exhausted fourth decimal, **none** —
> and `SStars` counts stars rather than correct stars. A knob that admits false positives is free in `J` **by
> construction**, not by scale.
>
> **The two findings compose rather than compete.** F32's saturation says any *new* term would have almost no
> room; wave 26 says the term that already exists is **not being evaluated**, so the first question is not "how
> big should a precision term be" but "why is the branch that carries one never taken on the population every
> conclusion is drawn from". Measured consequence, `n = 1` and caveated: at `D08`'s at-floor landing, restoring
> the shipped sensitivity moves precision **0.966 → 1.000** while `recall@all` falls 0.978 → 0.803 — and
> **`recall@high` does not move at all**, on any of four datasets. Full account, with the
> [F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)
> caveat that may explain the whole +0.034:
> [F83](#f83--j-carries-no-precision-term-on-an-unlabelled-run-so-a-sensitivity-pin-is-free-in-the-objective-by-construction).

### F33 — ~~The synthetic bank does not reproduce the real bank's optimizer failure mode~~ → it does now
**Status:** Done (part 1 wave 3, part 2 wave 4) · found 2026-08-03 re-reading the wave-1 arms side by side

> **Both parts are shipped.** Part 1 (report the *effective* gate) landed in wave 3 as
> `StarDetector.EffectiveSensitivityGate`. Part 2 (decide whether the bank grows a shedding class) was decided
> **yes** in wave 4, and the class was generated and **validated against a criterion fixed before generation** —
> `D18`/`D19` shed at trade rates −53.4/−40.4 inside the real bank's regime, while the `D20` control comes back
> at **+44.9**. The title's claim no longer holds: the synthetic bank now reproduces the regime, so the
> "never the synthetic alone" rule it imposed on objective changes is satisfiable rather than blocking.

On the synthetic bank the optimizer drives `BrightnessSensitivity` **down** to its 0.0 floor and detects *more*.
On the real bank it drives Sensitivity **up**, often to the top of the range, and detects far *fewer*. Those are
opposite behaviours, and the synthetic bank was built to study the first one.

**Evidence.** Landed Sensitivity and detection counts, same wave-1 control arm, same binary:

| bank | landings | detections C0 → A |
|---|---|---|
| synthetic (6 of 17 datasets) | Sensitivity **0.0** (D09, D10, D11, D12, D15, D17) | up |
| real: `CWhiteFocus` | **50.0** | 1807 → **534** |
| real: `standard_example1` | **34.3** | 602 → **177** |
| real: `toml999` | **33.3** | 677 → **228** |
| real: `uneven` | **31.2** | 406 → **133** |
| real: `mccomiskey` | 0.0, but **StarClip 10.0** (the maximum) | 3606 → **43** |

`mccomiskey` is the instructive one: Sensitivity reads 0.0, which looks like the synthetic pathology, but the
effective gate is `PeakResponse × StarClip` — the *same escape route* F23 wave 1 measured on D12 and D15, here
operating as the shedding mechanism rather than the loosening one. Reading the Sensitivity axis alone
misclassifies this run.

> **Corrected 2026-08-03 (wave 3), and it is more general than one run.** The `0.75 × 10 = 7.5` above used the
> *default* `PeakResponse`; this landing's own `StarPeakResponse` is **0.98**, so its effective gate is
> **9.81**. Both inputs are searched axes, so the gate must always be computed from the landing's own params —
> the same trap the entry warns about, one level down. And `mccomiskey` is not alone: **`caboose`** also lands
> Sensitivity 0.0 with an effective gate of **5.05**, and on the synthetic bank **2 of the 6** "Sensitivity 0.0"
> landings F23 cites are not floor landings either (**D11 → 2.36**, **D12 → 2.10**; D09/D10/D15/D17 are genuine,
> all below the 1.5 inert bound). So this is a systematic reading error, not one odd run.
>
> **Shipped:** `StarDetector.EffectiveSensitivityGate(p)` = `max(Sensitivity, InertSensitivityBound(p))`,
> reported alongside every quoted landing Sensitivity — `optimized_settings.json` (derived, get-only, no schema
> bump), `optimize`'s console + `optimize_summary.txt` + `aggregate_summary.*`, and `bank-verify`'s per-config
> `effectiveSensitivity`. The `afbank-verify` schema is deliberately **not** bumped: the field is additive and
> derived, no number changes, and the /4 and /5 bumps were for changes that made numbers non-comparable.

**Why it matters.** [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)'s
framing — "the optimizer drives `BrightnessSensitivity` to its 0.0 floor" — is a **synthetic-bank-only**
description. Any fix designed and accepted against the synthetic bank alone is being tuned on the opposite sign of
the effect it needs to correct on real rigs, which is how wave 1's arm (a) came to be a wash on the real bank
while showing real movement on the synthetic one. It also bounds the bank's claim: the synthetic bank measures
precision exactly (that is real and now verified), but it does not currently exhibit
[F4](#f4--the-objective-has-no-sensor-model-term)'s star-shedding at all.

**Next step.** Two parts. (1) Report the *effective* gate `max(Sensitivity, PeakResponse × StarClip)` wherever a
landing's Sensitivity is quoted, so a `mccomiskey`-shaped landing is not read as a floor landing. (2) Decide
deliberately whether the bank should grow a dataset class that reproduces the shedding regime — and until it does,
score every candidate objective change on **both** banks, never the synthetic one alone.

**Part 2 DECIDED (wave 4): yes, and the mechanism is not what this entry assumes.** The synthetic bank does not
fail to shed because its *render* differs from real frames. Measured with `golden eval` at ~40 s per arm, the
detector's response to the gate is much the same at any density:

| dataset | recall@high, Sensitivity 10 | Sensitivity 50 | high-tier stars surviving at 50 |
|---|---|---|---|
| `D04_esprit_550mm` (dense) | 0.762 | **0.208** | **4591** |
| `D16_esprit550_ha3` (sparse) | 0.821 | **0.130** | **24** across 9 frames |

Precision is **1.000 and FP is 0 on every arm** — everything shed is a real star. What density changes is not
whether the detector *will* shed but whether the optimizer can **afford** to: D04 keeps thousands of stars and
still clears `NHard = 3` on every frame, so the search can climb to a shedding operating point and score it; D16
falls to ~2.7 stars/frame, `J` goes to 0, and the search is repelled. The synthetic bank's median min-frame
detection count is **7** against the real bank's **37**, and only 2 of 17 datasets clear 100.

So the missing ingredient is **headroom above the hard floor**, and it is a spec-level property — density is set
by `limitingMagnitude` and pointing against a real Gaia/ASTAP catalog, not by generator code.

**Shipped:** three rows in `synthetic-bank-spec.json` — `D18_m24_deep_shed` (M24 at limiting mag 15.5, 27084
on-frame stars), `D19_cygnus_deep_shed` (a second optic and field so no finding rests on one geometry), and
`D20_m24_bright_control` (the same rig and field at limiting mag 12.0: dense enough to have the headroom, but
bright-dominated, so there is no faint near-threshold tail whose membership changes with focus). **The control
was designed before the experiment** — wave 3's D05 lesson — and it isolates the faint tail from mere density: if
D20 sheds too, the faint-tail mechanism is refuted and density alone explains the regime.

**Acceptance criterion, fixed before generation so it cannot be moved afterwards:** a row earns its place only if
at config A it reaches median trade rate **≤ −10** recall-points per unit `J` with **keep% < 80%**, at precision
**≥ 0.99**. D20 must NOT meet it.

**Measured — the class PASSES and the control separates cleanly:**

| dataset | landed Sens | recall C0 → A | Δrecall | Δ`J` | trade rate | keep% | criterion |
|---|---|---|---|---|---|---|---|
| `D18_m24_deep_shed` | 32.83 | 0.736 → 0.607 | −0.129 | 0.00242 | **−53.4** | **48.8%** | **MEETS** |
| `D19_cygnus_deep_shed` | 16.67 | 0.983 → 0.968 | −0.015 | 0.00037 | **−40.4** | 59.2% | **MEETS** |
| **`D20_m24_bright_control`** | 15.67 | 0.946 → **0.960** | **+0.014** | 0.00031 | **+44.9** | **94.8%** | **does not meet** |

Precision is 1.000 on all three. **D20's trade rate is positive** — the optimizer *gained* recall on the
bright-dominated control while raising its gate. So the mechanism is decided rather than assumed: all three
fields have the headroom to shed, and only the two with a faint near-threshold tail actually do. **Density
supplies the headroom; the faint tail supplies the motive.**

Note all three raised Sensitivity above the default, control included — a landing that moves the gate is not by
itself evidence of shedding, and an arm read off landed parameters alone would have misclassified D20.

**So [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
both-bank requirement is now satisfiable**: the synthetic bank finally contains the regime a candidate objective
change has to be shown to move.

**Why this is worth spending anything on:** only the synthetic bank knows the **true optimal focuser position**
(`optimalFocuserPosition` is a spec input). So only it can answer whether shedding bought *real focus accuracy*
or merely a smaller **self-reported** σ from a fit with fewer, better-behaved points — the crux of F32 and F4,
currently unanswerable on either bank.

### F38 — The `MinHFR` seed trigger compares a captured-pixel vertex against a binned-pixel gate
**Status:** Done (wave 4) · found 2026-08-04 checking F20 part 1's trigger before reusing it for user-facing copy

`MinHfrSeed.Resolve(fitVertexHfr, currentMinHfr)` compared two numbers that are not in the same space. The gate
fires **inside the binned raster** — `if (star.HFR <= p.MinHFR)` at `StarDetector.cs:1894`, over the `BinMean`
output — while every HFR the detector *reports* has already been scaled back to source pixels at `:805-808`
(`ScaleToSourcePixels` multiplies HFR by the factor, `CvImageUtility.cs:818`). That rescaled value is what flows
to `AverageHFR` → the hyperbola → `BestFit.Minimum.Y`. Nothing divided the factor back out.

**The entry's own doc comment defended the wrong half.** `MinHfrSeed` argued the comparison was safe because
"from the software-binning stage onward the whole pipeline runs in binned pixels" (`StarDetector.cs:483-486`).
That sentence is about the **right-hand** operand, and its very next clause says the other half: "GateAndMeasure­Internal
scales every pixel-space output back to source pixels before returning, **so callers never see binned units**."
`MinHfrSeed` is exactly such a caller. The detector is careful about this elsewhere — `RejectedCandidateRecord`'s
`MeasuredValue`/`ThresholdValue` are deliberately *not* rescaled because they are "the gate's own comparison pair"
and "must stay in the same space" (`StarDetector.cs:847-857`). This was a gate comparison pair that was not.

**Direction: missed seeds only, never spurious.** `{h ≤ G} ⊂ {h ≤ N·G}`, so every seed that fired was one the
correct rule also fires. Sound but incomplete — no risk of lowering a gate that did not need lowering.

**The silent window**, at the shipped `MinHFR = 1.2` and `N = 2`: a captured vertex in **(1.2, 2.4]** px. The
rig's near-focus frames really are being emptied by `TooLowHFR`, but the *wing* frames still fit, so
`BestFit.Minimum.Y` is finite and the rule takes the **"the rig's stars clear the gate; leave it alone (the D05
case)"** branch on a rig that emphatically does not. It then prints nothing, because the seed simply never fires.

**Reach — and why no F35 number is invalidated.** TestApp `optimize` never sets `DetectionBinning`
(`ApplyAfContext` writes only PixelScale/Region/ModelPSF/paths), so it runs at the `StarDetectorParams` default
of **1**, where both spaces coincide. The entire F35 evidence base — 17 synthetic datasets, 19 real runs, the D01
firing, the D05 control — was produced at N = 1 and stands unchanged. The bug is **latent headless and ACTIVE in
the wizard**, which stamps the user's real profile/per-filter factor via `ApplyDetectionImageContext`
(`HocusFocusStarDetection.cs:573-574`) — i.e. it bites the actual product, on exactly the undersampled population
F20/F35 exist to rescue.

**Shipped:** `Resolve` and the new shared `MinHfrSeed.IsBelowGate` take the factor explicitly, so the mixed-unit
comparison cannot recur silently, and F20 part 1's message reuses `IsBelowGate` rather than re-deriving it.
`MinHfrSeedTests` had **zero binning coverage**, which is why this survived wave 3; it now has four discriminating
cases, confirmed failing with the division reverted.

**Lesson.** The first version of that coverage was five tests of which only **one** failed when the fix was
reverted — the other four asserted behaviour identical with and without it. Wave 3's rule ("a regression test that
passes either way is worth nothing") applies to a *set* of tests too: count the ones that discriminate, not the
ones you wrote.

**The BANK can finally exercise this fix (2026-08-06, wave 7).** This entry's population is a run whose
`DetectionBinning > 1`, and until wave 7 the harness had never produced one —
[F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(b)
was exactly that gap. `optimize --apply-run-detection-binning` now makes the seven `detectionBinning = 2` datasets
detect at 2, so a bank-level regression assertion for the captured-vs-binned comparison became possible for the
first time — and **it was added in the same wave**: the whole (1.2, 2.4] silent window at factor 2, plus the four
real bank in-focus HFRs pinning that honouring the factor did NOT quietly start seeding the entire binning-2
population. **3 discriminating** (reverting the division fails them) **/ 5 guards**.

**A related hypothesis this entry did NOT explain, checked and refuted.** Wave 7's binning arm shows `recall@high`
falling on 4 of 7 datasets at factor 2, which looks exactly like this entry's mismatch (a gate in binned space
effectively doubling `MinHFR` in captured pixels). `golden eval`'s false-negative attribution reports **no
`TooLowHFR` at all** in any of the fourteen runs: the loss is candidate formation and the shape/size gates. Filed
separately as [F46](#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates).

### F39 — The harness records a detection binning the run never applied, and 7 datasets have never run at theirs
**Status:** Open — part (a) done (wave 6); **part (b) MEASURED IN FULL (wave 7, and it needed no re-render):
recall up on 7/7 AND σ_focus up 17–95% on 7/7**; the adoption re-baseline is a wave of its own · found 2026-08-04
checking whether the banks differ in binning

Every `harness_settings.json` in the synthetic bank says `"DetectionBinning": "Bin2"`; every real run says
`Bin1`. That looks like a systematic difference between the banks that could explain F33 outright. **It is not,
because the value is inert on the headless path** — but two real defects sit underneath it.

**1. The recorded value is not the value used.** Nothing reads `harness_settings.json`'s `DetectionBinning` back
into `StarDetectorParams`. `BuildStarDetectorParams` / `BuildDefaultStarDetectorParams` never map it; only
`ApplyDetectionImageContext` does, and the headless runners never call it. `OptimizationDiagnosticRunner.cs:443`
calls `HarnessSettingsStore.ResolveForRun(...)` and **discards the returned value** — it is a pure write
side-effect. So every `optimize --per-run` and every `golden eval` over both banks ran at the default of **1**,
and the file on disk claims otherwise. Anyone reproducing a run from that file gets the wrong binning.

**2. The derivation never ran, on any dataset.** `ResolveForRun` derives the factor from
`RecommendFromHfr(inFocusHfrPixels)` only when the run has an `autofocus_report_Region0.json` carrying a fitted
minimum. No synthetic bank folder has one, so all 17 fell to "kept from base" and inherited `Bin2` from a profile
export — every one of them says so in its own `DerivedNotes`: *"DetectionBinning kept from base (no fitted
in-focus HFR for this dataset)"*.

And it is **circular on exactly the datasets that matter most**: the derivation needs a fitted in-focus HFR, and
D01/D02 have none *because the `MinHFR` gate zeroed the curve* — the very defect F20/F35 are about.

**3. The consequence: an axis of the bank has never been exercised.** `expectedOptimal.detectionBinning = 2` for
**D08, D09, D10, D12, D14, D15, D17** — seven datasets — and all seven have only ever been scored at 1.
`D08_c11_2800mm`'s spec description calls it *"the first detectionBinning=2 dataset (in-focus HFR crosses the
`DetectionBinningResolver` threshold)"*. It has never been one. Note this is also the population
[F38](#f38--the-minhfr-seed-trigger-compares-a-captured-pixel-vertex-against-a-binned-pixel-gate) would bite on,
so the bank cannot currently regression-test that fix either.

**Why it matters.** No prior measurement is invalidated — the factor was a uniform 1 across every arm, so all
A/B comparisons remain internally valid, and this is a provenance and coverage defect rather than a numbers
defect. But it means the bank silently does not test what it says it tests, and a file that records settings a
run did not use is worse than one that records nothing.

**Next step.** Deliberately **not fixed inline**: making the headless path honour a per-run binning changes every
synthetic number at once and would re-baseline the bank mid-wave. Two separable pieces. (a) Stop writing a
derived-looking value that was not derived — either omit the field or mark it `kept-from-base` in the file
itself, not only in `DerivedNotes`. (b) Decide whether the seven `detectionBinning = 2` datasets should be run at
their expected factor, which is a re-baseline and needs its own arm.

**PART (a) DONE 2026-08-06 (wave 6); part (b) DEFERRED, deliberately.** `ResolveForRun` now writes
`DetectionBinningSource` — `derived-from-in-focus-hfr` or `kept-from-base` — as a **field** rather than only as
prose inside `DerivedNotes`. A reader diffs fields; nobody diffs a sentence, which is why seventeen files could
say "kept from base" in prose while presenting `"DetectionBinning": "Bin2"` next to sixteen genuinely exported
values. 2 tests, both discriminating.

**Part (b) is not paired with wave 6's re-baseline on purpose.** Wave 6 re-rendered `D01`/`D02` and the whole
value of that pass is that **exactly one thing changed** — the star field, with every derived parameter (step,
exposure, binning, donut) provably identical. Honouring a per-run binning would have moved a second variable on
7 other datasets in the same wave, and the two effects could not then be separated. It needs its own arm, with
its own before/after, on a bank nobody is simultaneously re-rendering.

**PART (b) MEASURED 2026-08-06 (wave 7) — and it needed NO re-render at all.** The premise that it would was
wrong, and reading one stored field is what showed it. `D08_c11_2800mm`'s own `synthetic_meta.json` says:

> `exposureDefinition`: *"Exposure t solving snr(t) = ... = 10 for the 20th-brightest of 123 on-frame stars ... **at
> binning=2 (captureBinning=1×detectionBinning=2)**, clamped to [0.5, 30] s"*

The bank's exposures for these seven **were already derived assuming binning 2** while every run scored them at 1,
and `detectionBinning` enters no render input (`GenerateSweep` takes centre/step/offsetSteps/exposure). Honouring
the factor therefore REMOVES an inconsistency rather than creating a new configuration.

**Arm G1 — `golden eval --detection-binning 1` vs `2`, `--params default --defocus-donut`, `--match centroid
--match-radius 12`, `--settings` pinned. 14 evals, ~6 minutes, no optimizer.** (The flag is new; `golden eval`
already exposed every other detection override, so this was one `Int(...)` line routed through
`DetectionBinningResolver.ApplyFactor` plus the per-run `PixelScale × factor` the detector itself applies.)

| dataset | recall@all 1 → **2** | recall@high 1 → **2** | precision | `LowSensitivity` FN 1 → **2** | other FN 1 → 2 |
|---|---|---|---|---|---|
| `D08_c11_2800mm` | 0.870 → **0.962** | 1.000 → 0.970 | 1.000 / 1.000 | 41 → **3** | 0 → 9 |
| `D09_c14_3800mm` | 0.761 → **0.906** | 0.978 → **0.989** | 1.000 / 1.000 | 49 → **7** | 7 → 15 |
| `D10_rc16_3250mm_sparse` | 0.852 → **0.939** | 0.966 → 0.948 | 1.000 / 1.000 | 15 → **1** | 2 → 6 |
| `D12_c14_585_afbin2` | 0.562 → **0.675** | 0.879 → 0.813 | 1.000 / 1.000 | 114 → **34** | 30 → 73 |
| `D14_cdk14_2563mm_e47` | 0.841 → **0.987** | 0.984 → **0.992** | 0.999 → **1.000** | 184 → **4** | 6 → 11 |
| `D15_cdk20_3454mm_e47` | 0.764 → **0.917** | 0.931 → 0.874 | 1.000 / 1.000 | 52 → **0** | 8 → 21 |
| `D17_cdk14_oiii5` | 0.875 → **0.990** | 1.000 → 1.000 | 1.000 / 1.000 | 26 → **2** | 0 → 0 |

1. **The physics-derived factor is right and the bank can finally say so.** Overall recall improves on **7 of 7**
   (+0.087 … +0.146) at **zero** precision cost — the single false positive anywhere in the set disappears.
2. **The mechanism is measured, not inferred.** `golden eval`'s FN attribution names it: `LowSensitivity`
   rejections collapse (**184 → 4** on `D14`). Binning raises the per-binned-pixel SNR, so the Sensitivity gate
   stops eating faint stars. Nothing about the gate changed.
3. **It is not free, and the price lands on the BRIGHT tier** — see the new followup below.

**Shipped:** `golden eval --detection-binning N`; `optimize --apply-run-detection-binning` (opt-in, absent ⇒
bit-identical, and it applies ONLY the binning — making the whole per-run `Resolved` authoritative would let a
per-run file shadow the `--settings` every arm pins, F42); and
`HarnessSettingsStore.ResolveRunDetectionBinningFactor`, which prefers the dataset's physics-derived
`synthetic_meta.json` value over the settings file's (`kept-from-base`, part (a)'s field) and **prints which
source it used**. 4 tests.

**ARM G2 — `optimize --per-run` with and without the flag. This is the largest effect in wave 7.**
**σ_focus improves at binning 2 on 7 of 7 datasets, by 17% to 95%:**

| dataset | `J` bin1 → **bin2** | σ_focus bin1 → **bin2** | σ improvement |
|---|---|---|---|
| `D17_cdk14_oiii5` | 0.97854 → **0.99518** | 2.64723 → **0.12033** | **+95.5%** |
| `D15_cdk20_3454mm_e47` | 0.99519 → 0.99518 | 0.73796 → **0.05577** | **+92.4%** |
| `D09_c14_3800mm` | 0.99182 → **0.99505** | 2.40151 → **0.20483** | **+91.5%** |
| `D10_rc16_3250mm_sparse` | 0.97880 → **0.99351** | 2.39831 → **0.45592** | **+81.0%** |
| `D12_c14_585_afbin2` | 0.98653 → **0.99631** | 3.88174 → **0.95750** | **+75.3%** |
| `D08_c11_2800mm` | 0.99466 → **0.99685** | 1.06022 → **0.53243** | **+49.8%** |
| `D14_cdk14_2563mm_e47` | 0.99886 → 0.99860 | 0.26950 → **0.22277** | **+17.3%** |

`J` improves on 6 of 7 (`D14` flat at −0.0003). **σ_focus is what autofocus is for**, and on `D17` it goes from
2.65 focuser steps of uncertainty to 0.12 — a factor of 22. Seven datasets have been scored for their entire
existence at a factor their own physics says is wrong, and it cost between a sixth and nineteen twentieths of
their focus precision. Prior waves' numbers on these seven stay internally valid (the factor was a uniform 1
across every arm) but were measured on a configuration the bank did not intend.

**[F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings) handled deliberately:** the arms were
ordered so the **status-quo binning-1 arm ran LAST**, so the bank's run folders still hold their pre-wave
landings. Verified from the files — `Provenance.CommandLine` reads `--out D:\hf_w7\g2\bin1\…`
([F30](#f30--a-stored-optimized_settingsjson-does-not-say-which-config-produced-it) doing its job). Re-baselining
seven datasets on a decision nobody has taken is exactly the silent drift that rule exists to prevent.

**The adoption decision is NOT taken here.** Acting on this means re-baselining the seven, which is a wave of its
own with its own before/after on a bank nobody is simultaneously re-rendering. What wave 7 delivers is the
measurement and the flag.

**Still owed: the ADOPTION wave.** Wave 7 delivered the measurement and the flag; it did not take the decision.
Everything below is what that wave needs, so it does not have to re-derive it.

**1. The decision to take.** Should the seven be scored at their derived factor permanently — i.e. does
`--apply-run-detection-binning` become the default on the headless path (or the resolved value stop being
discarded at all)? The evidence says yes on recall and emphatically yes on σ_focus, and the counter-evidence is
[F46](#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates).

**2. What changes, and what does not.** The frames do NOT move (`detectionBinning` enters no render input — §3.3),
so **no re-render**, and no `--dry-run` diff is needed to scope one. The **product is not affected either**: the
live app already applies the user's factor through `ApplyDetectionImageContext`. This is a harness/bank change
only, on 7 of 20 datasets. The other 13 are binning 1 under either configuration, which makes them a **free
control — they must come back bit-identical**, and an adoption arm that moves one of them has a fault, not a
result ([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s shape).

**3. It BREACHES two checked-in expectation bands, and that is the real gate.** Scored against
[`docs/synthetic-af-bank-expectations.json`](synthetic-af-bank-expectations.json)'s per-class `recallHighMin`
(both L34 and L47 are 0.90), using wave 7's arm-G1 numbers under config B:

| dataset | class | floor | recall@high bin1 | **bin2** | verdict at bin2 |
|---|---|---|---|---|---|
| `D12_c14_585_afbin2` | L34 | 0.90 | 0.879 | **0.813** | **BREACH** (and it already breached at bin1) |
| `D15_cdk20_3454mm_e47` | L47 | 0.90 | 0.931 | **0.874** | **BREACH** (bin1 was fine) |
| `D08` / `D09` / `D10` / `D14` / `D17` | — | 0.90 | — | 0.948–1.000 | ok |

That file's own rule is explicit: *"A cell landing outside its band is a FLAG that gets triaged into
docs/followups.md — it is NEVER fixed by widening the band here."* So **adoption cannot proceed by relaxing the
band**. Either F46 is understood first and the bright-tier loss is reduced, or the breach is triaged on its
merits and the band is re-derived with a stated justification — which is a legitimate move (the bands were
calibrated at binning 1, a configuration the bank did not intend) but a deliberate, separately-argued one.

**Note the direction is not uniform**: overall recall RISES on all seven while `recall@high` falls on four, so a
re-derivation would tighten some bands and loosen others. Both halves have to be stated.

**4. Ordering, so the adoption arm is the one the bank keeps.**
[F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings): `optimize --per-run` rewrites
`optimized_settings.json` into the run folders. Wave 7 ran the status-quo arm LAST on purpose; an adoption wave
must run it **FIRST** and the binning-2 arm last, then record the mapping in its results doc.

**5. What it does NOT disturb.** `D18`/`D19`/`D20` are not among the seven, so
[F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
confirmation arm is unaffected by this adoption — the question that dominated wave 7's ordering does not recur
here. Prior waves' A/B comparisons on the seven also stay internally valid (the factor was a uniform 1 across
every arm of every wave); what changes is that future numbers are not comparable to them, which is
[F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary) again and wants the usual
re-run-rather-than-compare-across treatment.

**6. Reproduce the wave-7 measurement it builds on:** `D:\hf_w7\f39b_golden.sh` (arm G1, recall/precision, ~6 min)
and the G2 block of `D:\hf_w7\remaining_arms.sh` (σ_focus / `J`). Per-dataset outputs in `D:\hf_w7\golden` and
`D:\hf_w7\g2`.

**PART (b) ADOPTED 2026-08-06 (wave 8).** `optimize --per-run` now applies each run's derived detection binning
**by default**; `--no-run-detection-binning` is the opt-out, and it exists so every arm this project has already
run stays reproducible on a current binary — rebuilding an old commit to get a control arm is not a control
([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)). Wave 7's
`--apply-run-detection-binning` is still accepted and is now a no-op, so its scripts keep working *and keep
meaning what they said*. Arms ran **status-quo FIRST, adopted LAST** per §4 above, so the bank's run folders hold
the adopted landing.

**`golden eval` is deliberately NOT re-based.** It is the instrument every prior wave's golden arm was measured
with, and silently defaulting it would re-baseline those comparisons rather than extend them. Instead it now
**states the disagreement**: when the factor it is scoring at differs from the run's derived one, the report says
so and names the source. The two harnesses cannot disagree *quietly*, which was the actual requirement.

**The §3 gate is resolved by applying the expectations file's own rule rather than working around it.**
[F46](#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates) was triaged in
full (mechanism named, binning-specificity established by a control, scaling rule refuted on its own pre-registered
terms) and produced no fix that clears the band. The file says a cell outside its band *"is a FLAG that gets
triaged into `docs/followups.md`"* — it does not say adoption is blocked, and the bands are documentation rather
than a gate (nothing in the codebase reads that file). **So the bands are left untouched and are now telling the
truth:** `D12` at 0.813 and `D15` at 0.874 say *the shipped default `StructureLayers = 4` under-performs at
detection binning 2 on rigs with large wing donuts*, which is a true statement about the product. Re-deriving the
band would have encoded a known defect into the file as an expectation — the outcome the rule exists to prevent,
reached from the direction nobody anticipated. Note `D12` was **already** breaching at binning 1 (0.879), so
adoption deepens that breach rather than creating it; `D15`'s is new to adoption and is fully explained
(`--structure-layers 6` takes it to 0.977).
Reproduce: `D:\hf_w8\adopt\adopt_arms.sh`.

### F40 — The settings handoff shipped in wave 4 had never once been written to disk
**Status:** Done (wave 5 — backfilled and now exercised) · found 2026-08-05 backfilling it

Wave 4 shipped `hocusfocus_star_detection.json`, the `StarDetectionSettingsExport` envelope that makes a bank
landing importable by the NINA UI. A filesystem scan of `D:\` and the user profile before the wave-5 backfill
found **zero files of that name anywhere**.

**It is not a bug in the writer.** `WriteSettingsHandoff` is called from the one `WriteOptimizedSettings` site
with a non-null `baseOptions`, and the format has unit coverage (`OptimizedLandingExportTests`). The cause is
ordering: wave 4's own arms — including the `optA` acceptance run on D18/D19/D20 — ran **before** the handoff was
committed, and no `optimize` pass has run since. Wave 4's write-up says as much in its last line ("the envelope
appears on the next `optimize` pass over a run"), which is correct and reads much weaker than the headline
"**Shipped.** Every landing is now stored in a form the app can import and replay with".

**Why it matters, and it generalizes past this file.** A feature can be written, unit-tested, merged, and
described as shipped while never having *executed* in the environment it exists for. Unit tests prove the mapping;
they do not prove a file arrives on disk. The distance between "the code that writes it is correct" and "it has
been written" is exactly one arm that nobody ran.

**Fixed (wave 5):** `TestApp bank-export-settings --runs <bank-root> [--apply]` converts each folder's existing
`optimized_settings.json` in place, with no optimizer run — so F15 is never touched. **42 landings backfilled
(20 synthetic + 22 real), 0 failed**, every one round-trip verified through `DiffKnobs` *before* being written.
That backfill is the format's first end-to-end exercise outside unit tests.

### F41 — A prior wave's control arm is not a control for a later wave's binary
**Status:** Open (recorded as a standing rule) · found 2026-08-05, twelve minutes into the first wave-5 arm

Wave 5's C0 acceptance criterion was "the feature-OFF landing must be bit-identical to `hf_f23/H_real_A`", the
wave-1 control arm. On `toml999` it failed across nine knobs — Sensitivity 33.3 → 16.7, StarClip 3.5 → 6.875,
MaxDistortion 0.10 → 0.45 — which reads as a serious regression in the change under test.

**It is not one, and the same output says so.** `BaselineJ` also differs, **0.99784 → 0.98348**. `BaselineJ` is
the *current settings*' score: no search is involved in producing it, so a search-side change cannot move it. A
changed `BaselineJ` can only mean the objective or the detector changed — and between wave 1 and wave 5 they
changed at least five times (`5115885` marginal-SNR default, `c2db33e` inert-gate fix, `7b5a695` effective gate,
`238623d` MinHFR seeding/reporting, plus PR #174's bimodal HFR).

**The rule.** An arm directory records what a *particular binary* landed. It is a valid baseline only for
comparisons against **that same binary**. For "is my change inert", build the **immediate parent commit**; for
"what did my change do", use **this binary's own feature-OFF arm**. Reusing an older wave's arm silently measures
every intervening merge and attributes it to the change under test.

**The tell is free and worth checking first.** `BaselineJ` (and any other search-independent quantity) should be
identical between two arms of the *same* binary. If it is not, the binaries differ and no knob comparison between
them means anything — check that single number before reading a diff as a regression.

### F42 — Every build directory silently gets its OWN detector settings, and the run instructions require a new one per arm
**Status:** **Done (wave 6)** — per-user default path, loud bootstrap, and a `UseAdvanced=False` warning that names
the overridden knobs · found 2026-08-05 chasing a `BaselineJ` gap that turned out not to be the code under test

`HarnessSettingsStore.DefaultPath()` is `Path.Combine(AppContext.BaseDirectory, "harness_settings.json")` — the
file sits **next to the exe** — and `ResolveAt` **bootstraps one from the live NINA profile** when it is absent.
Meanwhile the AF-bank run instructions say to build each arm to a separate `-o` directory, because the exe is
file-locked while a run is in progress.

Those two facts compose into a silent confound: **every new build directory bootstraps a fresh settings file from
whatever the profile happens to hold at that moment**, so two arms built minutes apart can run different
detectors. Measured across three wave-5 build dirs:

| option | `exe2` | `exe_base` |
|---|---|---|
| `LocallyAdaptiveBinarization` | True | **False** |
| `ModelPSF` | True | **False** |
| `UseOptimizedSettings` | False | **True** |
| `DetectionDebugMode` | False | **True** |
| `PixelSizeMicrons` / `FocalLengthMm` | 3.8 / **NaN** | 3.76 / 688.0 |

`UseOptimizedSettings = True` alone changes what `BuildStarDetectorParams` returns for the BASELINE — the "before"
every improvement is measured against. `LocallyAdaptiveBinarization` changes candidate formation outright. Each
bootstrap also stamps a differently-named profile snapshot (`Default-2026-08-05T10:54:36`), which is the visible
tell in the run's own log: *"Settings: … (exported … from profile 'Default-…')"*.

**The irony is the point.** This store exists precisely to stop profile state leaking into runs — its own comment
says "a profile-sourced seed is mutable machine state nothing records, and `TryLoad("")` picks whichever profile
is ACTIVE — two runs of the same data minutes apart were seeded from different telescopes." The *bootstrap* path
reintroduces exactly that, and the build-to-a-separate-directory workflow guarantees it fires.

**What it does and does not invalidate.** An arm set run from ONE build directory is internally valid — every arm
shares the file, so a flag remains the only difference (this is true of wave 5's own F32 and F24 arms). What is
invalid is any comparison ACROSS build directories, which is every cross-wave and every
before/after-a-code-change comparison — the ones [F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)
is about. The two findings are the same hazard from two directions.

**Next step.** Pass `--settings <one fixed path>` on every arm, and make the bootstrap loud: print a WARNING when
a settings file is created rather than loaded, since that is the moment an arm silently stops being comparable to
its predecessor. Consider defaulting the path to a fixed per-user location rather than `AppContext.BaseDirectory`,
so a new build directory inherits instead of bootstrapping.

**FIXED 2026-08-06 (wave 6).** All three, plus one the entry did not know about:

1. **The default path now INHERITS.** `DefaultPath()` = an existing file beside the exe (unchanged for any arm
   directory that already has one) → otherwise `%LOCALAPPDATA%\HocusFocusHarness\harness_settings.json`. A fresh
   `-o` directory therefore picks up the same file the last one used instead of minting a new detector. The
   decision is a pure function (`ResolveDefaultPath`) so it is tested without a filesystem.
2. **The bootstrap is loud** — `WARNING` on stderr plus `Logger.Warning`, naming the profile and saying outright
   that the run is not comparable to any earlier arm using a different file.
3. **A `UseAdvanced = False` settings file now warns, and names the knobs it is lying about.**
   `StarDetectionOptions.InitializeOptions` ends in `ConfigureSimpleSettings()`, which in Simple mode runs
   `DerivePresetSettings()` and **overwrites ~16 advanced knobs** from the three `Simple_*` presets — so editing
   `NoiseClippingMultiplier` or `MinHFR` in a pinned file does *nothing*, silently. This is the wave-5 probe set
   where four supposedly different configurations returned identical star counts, promoted from an anecdote in
   [F43](#f43--the-optimizer-wizard-refuses-to-start-unless-the-default-settings-already-produce-a-usable-curve) to
   a check. The overridden keys are MEASURED (hand the file's own bag to a throwaway options instance, diff it
   afterwards) rather than hard-coded, so the list cannot drift from what the class does.

**Checked, and wave 5 is NOT invalidated by (3):** `D:\hf_w5\pinned_settings.json`'s advanced values coincide with
the Typical presets (`NoiseClip 4`, `StarClip 2`, `StructureLayers 4`, `Sensitivity 10`, `MinHFR 1.2`,
`MinBox 5`, `NoiseReductionRadius 3+1`), so the diff is empty and every wave-5 arm ran the detector its file
describes. The hazard was real and did not fire. **Nor is any wave-5 comparison affected by (1)**: those arms
pinned `--settings` explicitly, which bypasses `DefaultPath()` entirely.

7 tests, of which **4 discriminate** (confirmed by neutralizing the three behaviours and re-running: exactly those
4 fail); the other 3 are labelled guards — the beside-the-exe precedence, Advanced-mode silence, and the
agrees-with-the-presets case.

### F43 — The optimizer wizard refuses to start unless the DEFAULT settings already produce a usable curve
**Status:** **Done (wave 5)** for the Live path; **replay DECIDED (wave 6) — extend, but not before the
`BaselineJ = 0` presentation question is answered**, so the implementation is still open · found 2026-08-05 from a
user report on a 40 mm rig · **AMENDED wave 28: this entry's "`StructureLayers` 4 → 2 makes it strictly worse"
is CONFOUNDED IN SOURCE — one knob moves two opposing cutoffs (`RULE W28-S` = `S-WELDED`). See the amendment
below and F92**

`SeedFitIsUsableAsync` (`StarDetectionOptimizerWizardVM.cs:2869`, called at `:2541`) evaluates the **default seed
params** once and refuses to optimize unless some run yields a finite σ(focus) over ≥ 3 positions. On a Live
sweep it retries exactly one way — widening the focus-recovery exemption to drop starless *outermost* positions —
and then errors out with *"The captured sweep does not produce a usable focus curve at the default detection
settings."*

**This is circular.** The tool exists to find settings that build a usable curve, and it declines to run unless
the settings it has not optimized yet already build one. The guard tests **one point** in a search space the
optimizer is free to explore; `MinHFR` alone spans [0.1, 5.0] against a default of 1.2.

**Measured on `D01_ultrawide_40mm`** — the bank's own 40 mm rig, `golden eval --params default`, per-frame
accepted stars across the sweep:

| focuser | 5964 | 5973 | 5982 | 5991 | **6000 (focus)** | 6009 | 6018 | 6027 | 6036 |
|---|---|---|---|---|---|---|---|---|---|
| `MinHFR = 1.2` (default) | 816 | 1670 | 1773 | 11 | **0** | 5 | 1748 | 1672 | 815 |
| `MinHFR = 0.30` (F35 floor) | 816 | 1670 | 2463 | 204 | **5** | 187 | 2469 | 1672 | 815 |
| `MinHFR = 0.10` (search floor) | 816 | 1670 | 2463 | 208 | **6** | 190 | 2469 | 1672 | 815 |

> **Re-measured on the F44-corrected D01 (wave 6).** The table above was taken on a spatially truncated field; the
> corrected one is 2.3× richer everywhere and **the core is still empty**, which is the whole point of the entry:
>
> | focuser | 5964 | 5973 | 5982 | 5991 | **6000 (focus)** | 6009 | 6018 | 6027 | 6036 |
> |---|---|---|---|---|---|---|---|---|---|
> | `MinHFR = 1.2` (default) | 1859 | 3758 | 4157 | 13 | **0** | 11 | 4182 | 3761 | 1867 |
> | `MinHFR = 0.30` | 1859 | 3758 | 5784 | 476 | **7** | 438 | 5786 | 3761 | 1867 |
> | `MinHFR = 0.10` | 1859 | 3758 | 5784 | 478 | **7** | 443 | 5786 | 3761 | 1867 |
>
> Thousands of stars in the wings, **zero at focus at the default gate**, and lowering the gate buys 7 — against
> `NHard = 3`. The rescue is exactly as thin on a correct field as it looked on a truncated one, so the entry's
> "genuinely thin on the rigs it exists for" framing stands unaltered. The old rows reproduce **exactly** on the
> wave-6 binary, so the two tables differ only by the frames.

The wings detect thousands of stars; the **core of the curve is empty**. That is F20's signature — rejections
concentrated on the INNER frames — and at 40 mm it is expected: in-focus stars are ~1 px, so the `MinHFR` gate
removes precisely the frames the vertex is fitted from.

**And `MinHFR` is the ONLY axis that rescues it.** `MinimumStarBoundingBoxSize` 5 → 3 changes nothing (still 0 at
focus); `StructureLayers` 4 → 2 makes it strictly worse (0 at *both* central positions). So the single knob that
un-blocks this rig class is the one the guard's refusal prevents the search from ever touching.

> #### AMENDMENT, wave 28, 2026-08-13 — the `StructureLayers` result above is CONFOUNDED IN SOURCE
>
> **The measurement stands. The inference that the axis is useless does not.** `RULE W28-S` = `S-WELDED`, 3 of 3
> clauses, decided from source at zero compute (`/mnt/d/hf_w28/w28s_score.txt`; `StarDetector.cs` sha256
> `1e202777…`, `IStarDetector.cs` `5304ed5f…`): **`StructureLayers` sets two opposing scale cutoffs at once.**
>
> * It sets the **upper** cutoff, via the à trous residual's ≈`2^StructureLayers` px low-pass, which is
>   *subtracted* — the knob's documented purpose.
> * It **also** sets the width of the post-wavelet Gaussian blur, which is the **lower** cutoff:
>   `CvImageUtility.ConvolveGaussian(structureMap, structureMap, p.StructureLayers * 2 + 1)`
>   (`StarDetector.cs:631-632`), with `sigma = 0.159758 * kernelSize` (`CvImageUtility.cs:80-82`). The width
>   argument is **raw** `p.StructureLayers`, not `EffectiveStructureLayers(p)`; **no** member of
>   `StarDetectorParams` (55 properties) controls it independently; and `EffectiveStructureLayers(p)` reaches the
>   residual at `:619`/`:622` and appears **0 times** inside the blur call's argument list.
>
> So **4 → 2 takes the kernel 9×9 → 5×5 and σ 1.44 → 0.80 px, which HELPS a ~1 px star clear the binarization
> threshold at `:653`, while simultaneously shrinking the residual's low-pass from ≈2⁴ to ≈2² px, so
> `src − residual` retains less of the star — which HURTS it, and by more.** The two effects were never
> separated, and with the shipped parameter set they **cannot** be: there is no knob for the blur width alone.
> The experiment this entry's sentence assumes was run has never been run.
>
> **Read the claim as: "`MinHFR` is the only axis that rescued it among those tested, on an axis set that
> contained a confounded knob."** See **F92** for the decoupling build (~45 m + ~10 m arm, +42 m `optimize`
> baseline if it is ever to ship). And note **F93**: `NoiseClippingMultiplier` 3.8125 → 1.0 on this same `D01`
> cuts its high-tier `NO CANDIDATE (structure gap)` count **42 991 → 8 931 (−79.2 %)** at precision 1.000 — a
> **second** axis that rescues candidate *formation* on this rig class, though on `D01` 97.8 % of the recovered
> candidates are then re-rejected by a later gate and `recall@high` moves only +0.012. Full working:
> `docs/synthetic-af-bank-followups-wave28-results.md` §1, §2, §9.

**Why [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it)
does not already cover this.** F35's `MinHfrSeed` is applied inside `OptimizeAsync` (`:3076`) — **after** this
guard — and its trigger is `BestFit.Minimum.Y <= MinHFR`, i.e. it needs a **fitted vertex**. When the gate has
destroyed the fit there is no vertex, `seedFitVertexHfr` is NaN, and the rule correctly declines. So F35 rescues
"the vertex sits under the gate" (D01/D02 headless, which still fit) and **not** "the gate destroyed the fit",
which is the strictly worse case and the one users hit.

**The error message's own advice is unreachable too.** It suggests "the recommended detection binning" — but that
recommendation is derived from a fitted in-focus HFR ([F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)),
which does not exist for exactly these runs. The remedy offered requires the thing whose absence caused the
error.

**FIXED 2026-08-05 (wave 5).** Before refusing, `TryRescueWithLowerMinHfrAsync` probes a **lowered gate** —
`MinHfrSeed.SeedFloor`, then the curated variable's own `Lower` bound, least-aggressive first. The ladder is read
from `OptimizerVariable.CreateCuratedSet` rather than written as constants, so the guard can never admit a rig on
a gate the search is not allowed to reach, nor refuse one it could have rescued because a constant drifted. If a
probe makes the curve fittable, the run proceeds **and the rescued gate becomes the seed** via the existing
`OptimizerSettings.MinHfrSeedFloor` — F35's mechanism, triggered by *feasibility* instead of by a vertex.
`OptimizeAsync` takes the **lower** of the two floors, since both only ever lower the gate and either may be
absent.

The rescue is **not silent**: `MinHfrRescueNotice` tells the user which gate failed and what it was lowered to,
because the wizard is then reporting results from a gate they did not choose — and on a short focal length that
is the setting they most need to know about.

Three things the fix deliberately does **not** do. It does not relax the bar to "the counts look healthy": the
probe asks only whether a curve is *determinable*, because the rescue is genuinely thin on the rigs it exists for
(6 stars on D01's in-focus frame against `NHard = 3`) and any richer bar re-rejects exactly that population —
raising the counts from there is the search's job. It does not mutate the caller's seed (probes run on a clone;
callers reuse one `StarDetectorParams`, and an in-place write is the wave-3 seed leak). And it does not remove
the refusal: a sweep no probed gate can fit is still refused, which is what the guard was written for.

**LIVE only, and the replay case is left open on purpose.** Replay gates on the user's CURRENT settings because a
saved run exists only because those settings could already focus — a decision locked by
`SeedGuard_ReplayMode_GatesOnBaseline`, which caught a first version of this fix that extended the probe to both
modes. Probing the seed there would quietly convert replay into a seed-gated path. **Open question:** whether a
saved run whose current settings cannot fit deserves the same rescue. The circularity argument applies equally;
the counter-argument is that such a run should not have been captured. Not changed as a side effect of this one.

> **REPLAY DECIDED (wave 6): extend the rescue, but not as a drive-by — the premise it rests on is measurably
> false, and the change needs a UI answer the wizard does not have yet.**
>
> *The premise.* `SeedFitIsUsableAsync` gates replay on `r.Baseline` (`:2882`) because "a saved run exists because
> those settings could already focus". Three populations falsify that, and all three are real today:
>
> | population | why the premise fails | evidence |
> |---|---|---|
> | **failed AF runs** | `KeepFramesForReview` saves sweeps that did NOT focus — a failed AF is precisely what a user brings to the optimizer | `KeepFramesForReview = True` in the shipped AF options |
> | **settings changed since capture** | the wizard's whole purpose is changing detection settings; "current" need not be what captured the run | — |
> | **runs never captured by this profile at all** | bank folders and other people's data are loaded routinely | the live app's own `LastSelectedLoadPath` read `D:\SyntheticAutofocusBank\D01_ultrawide_40mm\attempt01` — a dataset that yields **zero** in-focus stars at the default gate |
>
> So the asymmetry is not "live is the hard case and replay is the easy one"; it is that replay refuses a case it
> demonstrably receives. `D01` is the counterexample in the bank itself.
>
> *What to implement.* Keep the baseline gate FIRST — when the current settings fit, nothing changes, so the
> premise case stays exactly as it is. Only on the refusal path, probe the same `TryRescueSeedAsync` ladder that
> Live uses, and refuse only when no probed gate produces a **scorable** curve. `SeedGuard_ReplayMode_GatesOnBaseline`
> then needs rewriting rather than deleting: its contract becomes *"replay refuses when neither the baseline nor any
> probed gate can fit"*, and it must still fail when the ladder is removed.
>
> *Why this is NOT shipped in wave 6.* A replay whose baseline cannot fit has `BaselineJ = 0` — measured directly:
> every `D01`/`D02` arm this wave printed `Current settings J: 0`. The wizard's headline is an improvement
> *percentage against the baseline*, and a percentage against zero is not a number. So the change needs a decision
> about what the summary says when there is no baseline to improve on ("no usable curve at your current settings"
> rather than a ratio), which is UI work with its own review. Implementing the guard without it would replace a
> confusing refusal with a confusing result.

**EXTENDED 2026-08-05, same session, after the reporter tried it and it STILL failed.** The first fix was right
about the circularity and wrong about the bar. Lowering `MinHFR` made the reporter's curve **fittable** but not
**scorable**: frames still fell under the objective's `NHard` floor, so `J` was identically 0 and the search had
no gradient. Handing the search that seed is indistinguishable, to the user, from refusing outright — the wizard
appears to run and produces nothing.

**Measured on the reporter's own 61 MP sweep** (`FOCALLEN 40.0`, `XPIXSZ 3.76` → **19.4 arcsec/px**, 4 s, gain
100, step 250, `FOCPOS 25000` = true focus). Accepted stars per position:

| seed | 23750 | 24000 | 24250 | 24500 | 24750 | **25000** | 25250 | 25500 | 25750 | 26000 | 26250 | min | `J` |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| defaults (`MinHFR` 1.2) | 0 | 2 | 2 | 6 | 103 | **0** | 95 | 3 | 1 | 2 | 0 | 0 | **0** |
| `MinHFR` 0.3 (first fix) | 0 | 2 | 2 | 6 | 103 | **14** | 95 | 3 | 1 | 2 | 0 | 0 | **0** |
| + `Sensitivity` → 0 | — | — | — | — | — | — | — | — | — | — | — | 0 | **0** |
| + `NoiseClippingMultiplier` → 1.0 | — | — | — | — | — | — | — | — | — | — | — | **3511** | **0.2477** |

(The last two rows are reported by their minimum because the point is the `NHard` floor, which is what `J`
turns on; the per-position detail is in `D:\hf_w5\user40mm\*/optimize_summary.txt`.)

Two things that only measurement would have given: **`MinHFR` is real but not sufficient** (0 → 14 stars at
focus, still unscorable), and **the binding gate is `NoiseClippingMultiplier`, not `Sensitivity`** — relaxing the
acceptance gate alone leaves `J` at 0. From the `MinHFR`-only seed, **80 evaluations could not move `J` off 0**;
from the relaxed seed the optimizer converged immediately (`J` 0.246 → 0.262, min 43 stars).

**So the rescue now walks a LADDER and its bar is SCORABLE, not merely fittable:** `MinHFR` → `SeedFloor`, then
its search-space lower bound, then `+ Sensitivity` lower, then `+ NoiseClippingMultiplier` lower — least
aggressive first, every bound read from `OptimizerVariable.CreateCuratedSet` so the guard can never seed the
search outside what it may reach. Acceptance is `JTotal > 0`, computed with the wizard's own
`objectiveConstants`, so "the search has something to climb" is decided by the SAME number the search maximizes.
The winning configuration becomes the fresh pass's **seed**.

**Two false trails worth recording, both killed by checking the instrument rather than the theory.** D01, the
bank's own 40 mm dataset, looked like the obvious proxy and is not one: its sweep never reaches the 30 px
candidate size where the defocus-aware distortion relaxation engages, so `--defocus-gates` is **bit-identical**
there while the reporter's frames are far more defocused. And four probe runs returned **identical** star counts
across four supposedly different configurations — the profile had `UseAdvanced = False`, so Simple mode was
ignoring every knob being set. Identical results across different inputs is the tell that the instrument, not
the subject, is the thing being measured.

**Still open for this rig class.** The bank has no dataset resembling it (40 mm at heavy defocus, signal-starved
short exposure): D01 is 40 mm but nowhere near that defocus, so nothing regression-tests this population. And the
step size is its own problem — at 250 the usable band is about one step wide (103 stars at 24750, **0** at 25000,
95 at 25250), which is [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see).

Tests: 5, of which **3 fail** when the fix is removed (2 for the probe, 1 for the scorable bar); the other two are
labelled guards (inertness on a healthy sweep, and the floor-combination helper).

### F44 — The synthetic camera queries the catalog for at most ONE CELL, so a wide field is rendered starless outside a small central patch
**Status:** **Done (wave 6)** — code fixed wave 5, both rows re-baselined wave 6 and `suspect` cleared; **`D01` was
the only affected row** ·
found 2026-08-05 from a user report: "why is only a portion of the sensor getting rendered with stars?"

`StarFieldCompositor` computes the field correctly — `DiagonalFovDegrees × 1.05` — and hands it to
`AstapCatalogReader.Query`, which calls `AstapCellGeometry.FindAreas`. That method does two things which are
right for ASTAP and wrong for a renderer:

1. **It clamps the field**: `var fov = Math.Min(fovRadians, MaxFovRadians(partitioning))`, where `MaxFovRadians`
   is **5.142857°** (1476-cell) or **9.53°** (290-cell) — i.e. exactly one cell width.
2. **It then samples only the FOUR CORNERS** of that clamped box and returns the ≤4 cells they fall in. Its own
   summary says so: *"the 1–4 cells whose union covers the square field of view"*.

Both are a faithful port of ASTAP's `find_areas` (the doc comment says "reference behavior"), and both are
correct **for plate-solving**, where fields are a few degrees and 4 corner cells genuinely cover them. They are
not correct for rendering a wide field.

**Measured on the reporting rig** (9576 × 6388 at 3.76 µm, `FOCALLEN 40.0`):

| quantity | value |
|---|---|
| frame | **48.5° × 33.4°** |
| diagonal FOV the compositor requests | **59.67°** |
| what `FindAreas` clamps it to | **5.142857°** |
| clamp factor | **11.6×** |
| union of the ≤4 selected cells | at most ~10.3° × 10.3° |

So the catalog is queried over roughly a 10° patch of a 48.5° × 33.4° frame, and everything outside it renders
**starless** — which is exactly the bounded rectangle of stars the reporter saw, sitting in an otherwise empty
(noise-only) sensor.

**Why nothing caught it.** The compositor warns only when the query returns **no** stars
(`StarFieldCompositor.cs:303`); a partially-covered field returns plenty, so no warning fires and the frame looks
plausible. **The bank does exercise it — on two rows — and nothing checked.** `D01_ultrawide_40mm`'s own spec
description even names the hazard: *"capped at mag<=10.5 so the ~39deg diagonal FOV catalog query (design risk
R5) stays tractable"*. R5 was recorded as a **tractability** risk (too many stars); the actual failure was the
opposite — the query silently returns too few, over too small a patch.

### Affected bank datasets — SUSPECT, refresh required

Diagonal FOV × 1.05 against the cap, computed over all 20 rows (sensor dimensions from `SensorRegistry`,
binning applied). The verdict is the same under either partitioning (5.14° or 9.53°):

| dataset | sensor | FL | diagonal FOV | requested | × over the 5.14° cap | status |
|---|---|---|---|---|---|---|
| **`D01_ultrawide_40mm`** | IMX571 | 40 mm | **38.91°** | 40.85° | **7.9×** | **SUSPECT — refresh** |
| **`D02_rich_135mm`** | IMX571 | 135 mm | **11.95°** | 12.55° | **2.4×** | **SUSPECT — refresh** |
| `D03_redcat_250mm` (next widest) | IMX533 | 250 mm | 3.66° | 3.85° | 0.7× | ok |
| the remaining 17 | — | ≥ 250 mm | ≤ 2.94° | ≤ 3.09° | ≤ 0.6× | ok |

**Only D01 and D02 are affected**, and both must be **re-rendered in the next wave** once the query is fixed.
Everything from D03 down sits comfortably inside one cell and is unaffected.

**What this does and does not invalidate on those two rows.** The stars that ARE rendered are rendered correctly
— right PSF, right defocus, right photometry — so results about **gate thresholds and HFR-versus-focus behaviour**
survive. What does not survive is anything reading **counts, recall, precision, or position**: the field is
spatially truncated, so star totals are low by an unknown factor and the surviving stars occupy one patch of the
sensor.

Specifically at risk, and to be re-checked after the refresh:

- **[F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it)'s
  headline validation rests on exactly these two rows** — "D01 and D02 go from `FinalJ` exactly 0 to a real
  landing". The *mechanism* (the `MinHFR` gate zeroing an undersampled rig's fit, and seeding rescuing it) is a
  threshold result and should hold; the `FinalJ` values and star counts are measured on a truncated field.
- **[F43](#f43--the-optimizer-wizard-refuses-to-start-unless-the-default-settings-already-produce-a-usable-curve)**
  cites D01's per-frame counts (0 stars at focus, ~1700 in the wings). Its conclusion was independently confirmed
  on the reporter's own real 40 mm frames, so it stands, but the D01 figures are indicative rather than exact.
- The **wave-5 [F24](#f24--donut-detection-costs-precision-even-where-donuts-exist-and-badly-where-they-do-not--it-costs-recall-where-it-is-not-needed)
  arms** covered all 20 datasets; the D01 and D02 rows of that table are suspect. The verdict is unaffected — it
  turned on D06/D09/D14, none of which are.
- Any **sensor-model, tilt, or region-based** result derived from D01/D02, since those read star *position*.

**Consequences beyond the missing stars.** The rendered field is not just sparse but *spatially truncated*, so
anything that reads position — the aberration inspector's sensor model, tilt calibration, region-based AF, the
golden-set geometry — sees a synthetic frame whose stars occupy one corner-ish patch of the sensor. Any result
derived from a wide-field simulator frame is suspect until this is fixed.

**FIXED 2026-08-05 (wave 5).** `AstapCellGeometry.FindAreasCovering` enumerates **every** cell intersecting a
cone of the requested radius, walking the band table directly: per dec band, the RA half-span comes from the
spherical law of cosines evaluated at both band edges and at the cone centre, padded by one whole cell each side.
`AstapCatalogReader.Query` uses it **only when the field exceeds the cap** — below it the two agree by
construction (a box at most one cell across touches at most four cells, and its corners hit all four), so every
existing narrow-field result stays bit-identical and `FindAreas` remains an untouched, honest ASTAP port.

**Measured on `D01_ultrawide_40mm`, re-rendered:**

| | truth stars on frame | x extent | y extent | sensor grid occupancy |
|---|---|---|---|---|
| before | 8107 | 431 – 5921 | **741 – 2965** | **125/256 (49%)** |
| after | **19212** | −1 – 6249 | **0 – 4176** | **256/256 (100%)** |

Half the sensor was empty; it now fills edge to edge with 2.4× the stars.

Tests: an independent brute-force oracle (dense sphere sampling, no shared code with the routine under test)
asserts no cell inside the cone goes unqueried, across six pointings including RA-wrap and both poles, on both
partitionings; plus whole-sky, filename-encoding and supersets-the-reference checks. A `touchesPole` fast path
was written, measured against those tests, found to change nothing (`MaxRaHalfSpan` already returns π there) and
**removed** rather than left as an untested branch.

**RE-BASELINED 2026-08-06 (wave 6) — and only ONE of the two rows was ever affected.** Both were re-rendered into
the bank with the fixed query. Every derived parameter is unchanged on both (step 9 / 6, exposure 0.5 s,
`detectionBinning` 1, donut off, identical sweep positions), so the star field is the *only* thing that moved:

| row | in-focus truth stars | x extent | y extent | 16×16 grid occupancy | frames |
|---|---|---|---|---|---|
| `D01` before | 8107 | 431–5921 | **741–2965** | **125/256 (49%)** | — |
| `D01` after | **19212** | −1–6249 | 0–4176 | **256/256 (100%)** | changed |
| **`D02` before** | **3594** | −2–6247 | −1–4176 | **256/256 (100%)** | — |
| **`D02` after** | **3594** | −2–6247 | −1–4176 | **256/256 (100%)** | **BIT-IDENTICAL** |

**`D02_rich_135mm` was never affected**: its re-rendered FITS are byte-for-byte identical to the old ones, and its
golden star set is the same set (3536 stars, 25 unresolved, reordered only — the new query enumerates cells in a
different order). **So no number derived from D02 ever needed re-measuring**, including F35's D02 landing.

**The suspect criterion over-predicted, and this is the useful correction.** The table above marked D02 suspect on
*diagonal FOV ÷ one-cell cap* = 2.4×, which is a proxy for the real question: *do the ≤ 4 corner cells `FindAreas`
returns cover the field?* Cells are equal-area, so at `D02`'s **Dec +61.45°** one cell spans ≈ 1/cos(61.45) ≈ 2.1×
more RA degrees than at the equator, and the four corner cells covered an 11.95° field comfortably. **The cap ratio
is not the criterion; cell coverage at the field's declination is.** A ratio-based rule flags rows that are fine
(D02) and would keep flagging them forever. D01 at 7.9× is far enough over that no declination saves it.

`suspect` is now cleared on both rows, so `synth-bank` no longer warns. What was re-measured on D01, and what it
changed, is in [`docs/synthetic-af-bank-followups-wave6-results.md`](synthetic-af-bank-followups-wave6-results.md).

### F35 — `MinHFR` should be seeded from the sweep WINGS, and neither available HFR statistic can size it
**Status:** Done (wave 3) · found 2026-08-03 answering "how far can `MinHFR` safely come down?" for
[F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic)

**Shipped 2026-08-03** — `MinHfrSeed.Resolve` + `OptimizerSettings.MinHfrSeedFloor`, applied at the engine seam
ahead of θ0. Two of this entry's own premises were wrong and are corrected below.

Synthetic bank, 17/17, `EXIT=0` where the wave-1 arm exited 3. The seed fires on exactly **three** datasets —
D01 (fit vertex 0.762 px), D02 (0.749) and D03 (1.015), i.e. precisely the undersampled rigs this entry and F20
name:

| dataset | seed? | `MinHFR` new / control | `FinalJ` new / control |
|---|---|---|---|
| `D01_ultrawide_40mm` | **YES** | 0.100 / 1.200 | **0.99018 / 0.00000** |
| `D02_rich_135mm` | **YES** | 0.550 / 1.200 | **0.99656 / 0.00000** |
| `D03_redcat_250mm` | **YES** | 0.300 / 0.450 | 0.99549 / 0.99486 |
| the other 14 | no | **identical** | **identical to 5 dp** |

> **RE-MEASURED 2026-08-06 (wave 6) on the F44-corrected frames — the headline survives intact.** `D01`'s field
> was spatially truncated when the table above was produced ([F44](#f44--the-synthetic-camera-queries-the-catalog-for-at-most-one-cell-so-a-wide-field-is-rendered-starless-outside-a-small-central-patch));
> `D02`'s, it turns out, was not (its re-rendered frames are bit-identical). Re-run on **one binary**, with the
> control produced by the new `optimize --no-min-hfr-seed` rather than by an older commit ([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)):
>
> | dataset | frames | fitted vertex | `FinalJ` seed OFF | `FinalJ` seed ON | landed `MinHFR` |
> |---|---|---|---|---|---|
> | `D01` | **old (truncated)** | 0.7621 | **0.000000** | 0.990358 | 0.100 |
> | `D01` | **new (full field)** | 0.7712 | **0.000000** | **0.991551** | 0.100 |
> | `D02` (frames bit-identical) | — | 0.7488 | **0.000000** | 0.996510 | 0.3625 |
> | `D03` | — | 1.0154 | 0.995880 | 0.996543 | 0.300 |
> | **`D05` control** | — | 1.7882 | **0.999706** | **0.999706** | 1.48125 |
>
> **"`FinalJ` exactly 0 → a real landing" is unchanged on a field with 2.4× the stars**, which is what a
> gate-threshold result should do. `BaselineJ` is identical between the two arms of every dataset (F41's free
> check), and `D05` is bit-identical seed-on vs seed-off — the boring control that caught the wave-3 seed leak,
> still boring.
>
> **The drift is bounded by a dataset that could not move.** `D02`'s frames are provably identical to wave 3's, so
> its `FinalJ` 0.99656 → 0.996510 is **pure wave-3→wave-6 binary drift: 5×10⁻⁵**. `D01`'s old-frames landing moved
> 0.99018 → 0.990358 (+1.8×10⁻⁴) — same order. So `D01`'s **+1.2×10⁻³** from the re-render is roughly 6× the
> drift and is attributable to the frames, not the binary.

**D01 and D02 go from `FinalJ` exactly 0 to a real landing — the whole of F20's defect — and 14 of 17 landings
are bit-identical to the control**, including D05 at 1.450. On every rig the change was not designed for it is a
no-op, not a small perturbation. Zero datasets that did not trigger landed at the seed constant.

**Real bank: 19/19 landings bit-identical to the control, seed fired on ZERO runs** (per F33's "both banks"
rule). Exactly inert, not "within noise". It also disproves F20's claim that its real-bank runs are the same
defect — see the correction in [F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic).
**The fix is real and measured, but its real-bank population on this bank is empty**, so the user-facing benefit
is unproven on real frames until an undersampled short-focal-length rig enters the bank. Do not claim otherwise.

Two things the run adds. **`BaselineJ = 0` is not by itself evidence of this defect**: D17 reads 0 like D01/D02,
but its vertex is above the gate, the seed correctly declines, and its landing is unchanged — it was already
escaping on its own. And **the censoring bias is visible but survivable**: D01's fit read 0.762 px against a
0.238 truth vertex (3.2× high) and still cleared 1.2, which is exactly why a *trigger-only* use of the fit works
where a *sizing* use cannot — `0.8 × predicted` would have given 0.61 and re-gated the rig completely.

> **The validation run caught a bug in the fix, and the D05 control is what caught it.** The first bank pass
> read 17/17 hard-floor PASS — and was wrong. D05 landed `MinHFR` **0.300** (the seed constant) despite a
> 1.804 px truth vertex that must never trigger; 14 of 17 datasets landed at exactly 0.300 while the log printed
> the seeding line exactly **once**. Cause: `OptimizeAsync` wrote `seed.MinHFR` **in place**, and callers reuse
> one `StarDetectorParams` — TestApp builds its context once *outside* the per-dataset loop
> (`OptimizationDiagnosticRunner.cs:306` vs `:426`), and the wizard passes a live reference to `runs[0].Seed`.
> So D01's genuine firing permanently re-gated the other sixteen, each of which then saw a seed already at the
> floor and correctly declined to trigger. **One real firing, sixteen silent ones, order-dependent results.**
> Fixed by cloning the seed; the regression test was confirmed to FAIL without the fix. Note the first unit test
> asserted `seed.MinHFR == SeedFloor` — it codified the bug and passed. **D05 exists purely to be boring, and a
> boring reading was the only thing that separated a working fix from one that had re-gated the whole bank.**

> **Correction 1 — the trigger statistic is not available where this entry says it is.** `BestFit.Minimum.Y` is
> "already read by the wizard as `baselineInFocusHfr`" only **after** the search, in `BuildSummaryAsync`
> (`StarDetectionOptimizerWizardVM.cs:3115`). The shared engine seam cannot see it at all:
> `RunEvaluationMetrics` carries the vertex **X only** (`BestFocusPosition`, `RunEvaluationData.cs:817-818`) and
> has no `Minimum.Y` field of any kind. Resolved by splitting the rule — the **caller** computes the trigger
> (the wizard and TestApp `optimize` both already hold a pre-search `RunEvaluationResult`), the **engine**
> applies the clamp. `synth-validate` and `tilt` fit no curve before optimizing, so they deliberately do not opt
> in and stay bit-identical.
>
> **Correction 2 — step 3 rests on a grid that does not exist, so it is NOT a fix.** `Continuous` is not
> quantized: `OptimizerVariable.Quantize` is *identity + clamp* for it (`:83-92`), and `InitialStep` is the
> **initial pattern-search stride**, which Phase B halves on every non-improving sweep down to
> `InitialStep × StepFloorFraction` = **0.03125**. So the axis already resolves to ~0.03 px — the "0.45 → 0.20
> is a 2.25× jump" framing describes only the first descent stride. And on the motivating rigs it is moot in
> both directions: `MinHFR` is absent from Phase A (`CoarseGrid` is `Sensitivity × StarClippingMultiplier` only,
> `:299-303`), so it moves solely through strictly-improving Phase-B moves — which on D01/D02's flat `J` never
> happen **at any stride**. Retuning `InitialStep` would perturb every rig that *does* have a gradient while
> doing nothing for the ones this entry is about. **Not shipped, deliberately.**

[F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic)'s proposed fix —
"if the median in-focus HFR is at or below `MinHFR`, seed `MinHFR` beneath it" — **cannot be implemented as
written, because it is circular.** On `D01_ultrawide_40mm` the in-focus frame detects **zero** stars at the
default gate, so there is no median in-focus HFR to read. The measurement that would trigger the fix is the one
the gate has destroyed.

**The wings break the circularity, and cost nothing.** Far from focus the PSF is large, so those frames are
unaffected by the gate and richly populated — D01's four outer frames carry **816–2002** accepted stars each at
`MinHFR` 1.2. The hyperbola fit already succeeds on them (**R² = 0.911**), and `BestFit.Minimum.Y` is already
computed and already read by the wizard as `baselineInFocusHfr`. So the trigger is available today with no new
measurement: *fit the curve from whatever frames produce stars, and compare the predicted vertex against
`MinHFR`.*

**But the fit must TRIGGER the adjustment, not SIZE it — both available statistics are biased the same way.**

| statistic | reads | truth | error |
|---|---|---|---|
| wing-only hyperbola vertex (frames with ≥100 stars) | **0.548 px** | 0.238 px | **2.3× high** |
| median measured HFR of survivors at focuser 5991 | **1.33 px** | 0.606 px | **2.2× high** |
| median measured HFR of survivors at focuser 6009 | **1.68 px** | 0.606 px | **2.8× high** |

The second and third are **left-censored at `MinHFR` itself** — only stars measuring above the gate can be
seen, so the surviving sample's median is bounded below by the very knob being tuned. This is structurally the
same trap [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)
wave 1 hit, where the marginal-SNR statistic was left-censored at the Sensitivity gate. The first is a
different mechanism (a hyperbola's vertex parameter is the one the wings constrain worst) with the same sign.

**Both biases understate how far the gate must come down**, so a plausible rule like `MinHFR = 0.8 × predicted`
gives 0.44 on D01 — which still gates its true 0.238 px vertex completely. The fix would have looked applied and
changed nothing.

**How low is safe: measured, and the answer is "as low as you like".**
`golden eval --params default --min-hfr <M>`, exact precision against synthetic truth, 8 values from 1.2 to 0.1:

recall@high per dataset, **FP = 0 in all 32 configurations**:

| `MinHFR` | D01 (40 mm) | D02 (135 mm) | D03 (250 mm) | D05 control (1000 mm) | D01 vertex-frame stars |
|---|---|---|---|---|---|
| 1.2 (default) | 0.129 | 0.451 | 0.393 | 0.985 | **0** |
| 0.9 | 0.152 | 0.507 | 0.535 | 0.985 | **0** |
| 0.7 | 0.160 | 0.567 | 0.581 | 0.985 | 2 |
| **0.5** | 0.164 | 0.588 | 0.585 | 0.985 | **5** |
| 0.35 | 0.164 | 0.594 | 0.585 | 0.985 | 5 |
| 0.25 | 0.165 | 0.595 | 0.585 | 0.985 | 5 |
| 0.15 | 0.165 | 0.595 | 0.585 | 0.985 | 5 |
| 0.1 | 0.165 | 0.595 | 0.585 | 0.985 | 6 |

> **The D01 column re-measured on the F44-corrected frames (wave 6).** The old-frames column above reproduces
> **exactly, to three decimals, on the wave-6 binary** — a free F41 check that this detector has not drifted for
> this measurement — and the corrected field changes the conclusion in no respect:
>
> | `MinHFR` | D01 old (truncated) | **D01 new (full field)** | FP, both | D01 vertex-frame stars, old → new |
> |---|---|---|---|---|
> | 1.2 | 0.129 | **0.127** | 0 | 0 → **0** |
> | 0.9 | 0.152 | 0.150 | 0 | 0 → 0 |
> | 0.7 | 0.160 | 0.157 | 0 | 2 → 4 |
> | 0.5 | 0.164 | 0.161 | 0 | 5 → 6 |
> | 0.35 | 0.164 | 0.162 | 0 | 5 → 7 |
> | 0.25 – 0.15 | 0.165 | 0.162 | 0 | 5 → 7 |
> | 0.1 | 0.165 | 0.162 | 0 | 6 → 7 |
>
> **Zero false positives at every value on the corrected field too, and the gain still saturates by 0.5.** Recall
> is fractionally *lower* because the golden grew with the field (TP 8253 → **19039**, and the stars the fix added
> are predominantly faint edge-of-frame ones), not because detection got worse: the wing frames go from ~816/1670
> accepted stars to ~1859/3758. The claim this table exists to support — *how low is safe* — is unchanged.

**Zero false positives at every value on every dataset**, and the recall gain saturates by 0.5 at the latest.
`MinHFR` is a second line of defence — hot-pixel filtering is separate and enabled by default — and on this bank
it is not carrying any of the load. **0.25–0.35 captures all the available gain on every class.**

**`D05_tec140_1000mm` is the control that makes a GLOBAL seed defensible.** Its in-focus HFR is 1.77 px, well
clear of every gate value tested, and its recall is **flat at 0.985 from 1.2 all the way to 0.1** — not one star
gained or lost. A rig that does not need the seed is provably unperturbed by it, so the adjustment does not have
to be conditioned on first detecting the wide-field case (which is the detection the gate has already
prevented). Without this row the seeding rule would need a trigger it cannot evaluate.

**The largest gain is D03, not D01.** `D03_redcat_250mm` moves **0.393 → 0.585 (+0.192)** against D01's +0.036.
The fix helps *marginally* undersampled rigs most, not the extreme one — D01 is dominated by candidate-formation
losses that this gate does not touch (see the attribution table below). Anyone scoping this work off D01 alone
will both underestimate the benefit and misattribute where it lands.

**The mechanism is the hard floor, not recall — and that reframes F20.** Lowering the gate moves D01's recall
only 0.129 → 0.165. What it actually does is put stars back on the **vertex frame**: 0 → 2 at 0.7, and **0 → 5
at 0.5**, which is the first value that clears the objective's `NHard` = 3 stars-per-frame requirement. That is
the whole of `FinalJ = 0.00000`. So the fix is real and worth shipping, but its success criterion is *"the hard
floor passes and the search gets a gradient"*, **not** *"recall recovers"*.

**And `MinHFR` is only 5% of D01's recall problem.** False-negative attribution at the two extremes:

| gate | at `MinHFR` 1.2 | at 0.1 |
|---|---|---|
| NO CANDIDATE (structure gap) | **18801** | **18800** |
| TooSmall | 6398 | 6398 |
| LowSensitivity | 6301 | 6301 |
| TooDistorted | 2387 | 2387 |
| NotCentered | 2228 | 2228 |
| **TooLowHFR** | **1877** | **1** |

`TooLowHFR` is 1877 of ~38500 missed golden stars. Removing it entirely leaves every other gate untouched.
**The W class's recall is lost in candidate FORMATION** — the structure map never proposes 49% of them, and
`MinimumStarBoundingBoxSize` (default 5 px) rejects another 17% as `TooSmall` — so anyone expecting the
`MinHFR` fix to lift D01 toward the 0.90 W-class band will be disappointed, and the band stays missed for a
reason this entry does not address.

**Why it matters.** F20 is the strongest surviving finding on this bank and its fix was one circular statistic
away from being unimplementable. The censoring point generalises past `MinHFR`: **any gate whose threshold is
tuned from the surviving sample is tuning against a distribution it truncated.** That has now bitten twice
(Sensitivity in F23, HFR here), which makes it a review question rather than a coincidence.

**Next step.** Three pieces.
1. Seed `MinHFR` when `BestFit.Minimum.Y` (from the wing-populated fit) sits at or below it — **trigger only**.
2. Size the seed from **pixel scale and sampling**, not from either censored statistic. The measured floor is
   0.25–0.35 px with no precision cost on this bank; validate on D01–D03 where the truth vertex is known
   (0.238 / 0.280 / ~0.97 px). Note the gate is `star.HFR <= p.MinHFR`, inclusive, so the seed must be strictly
   below the HFR to be kept.
3. Widen the search where it matters: `OptimizerVariable`'s `MinHFR` axis is `Continuous(0.1, 5.0, step 0.25)`,
   so from 1.2 the reachable grid is 0.95 → 0.70 → 0.45 → 0.20. Only 0.20 clears D01, it is four steps away with
   `J` flat the whole way (F20's cold-start plateau), and a 0.25 step is far too coarse in a sub-pixel regime —
   0.45 → 0.20 is a 2.25× jump. A seed makes the walk unnecessary; a finer low-end step makes it survivable.

**The risk to design against, unchanged from F20:** the objective rewards star count, so nothing pulls a seeded
`MinHFR` back up. It is a knob the search cannot climb out of, which is why the seed must come from geometry
rather than from a measurement the gate itself shaped.

### F36 — Which pre-wave-2 entries actually rested on the broken precision metric: audited, and it is none of them
**Status:** Done (wave 3) · raised 2026-08-03 as "F1–F8, F18, F21, F25, F26 have not been re-verified"

After [F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars) voided every `/3` precision
number and [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)/[F24](#f24--donut-detection-costs-precision-even-where-donuts-exist-and-badly-where-it-does-not)
were re-measured, the open question was whether the *other* entries measured on those same landings needed the
same treatment. That was an inference from what changed, not a check. **It has now been checked, entry by entry.**

**Result: no entry's conclusion depends on the repaired precision metric.** Every one of F1–F8, F18, F21, F25 and
F26 rests on σ_focus, recall, R², or landed parameter values — and `recallHigh` derives only from `match.Pairs`
(`BankVerifyRunner.cs:488`), which the `/5` repair never touched. One clause is the exception, below.

| entry | rests on | verdict |
|---|---|---|
| F1, F2 | donut-flag thresholds (`frac`, HFR, bbox) | unaffected |
| F3 | recall@≥12 on the real bank | unaffected — but see below |
| F4 | σ_focus, `J`, sensor-model stars/R² | unaffected |
| F5, F6, F18, F21, F25 | σ_focus, R², curve geometry | unaffected |
| F7 | σ_focus, and recall "essentially unchanged" | unaffected |
| F8 | landed Sensitivity/StarClip corners | unaffected — but see below |
| F26 | binning/convergence, **plus one precision clause** | ~~one clause unverified~~ → **verified and REFUTED at `/5` (wave 5)** |

**The one genuinely unverified clause.** F26 states that with the F23 marginal-SNR term enabled "D12 S6 converges
… even though it **fails its own precision gates**." Those gates were the wave-1 `/3` ones, and F23's real
precision effect turned out to be roughly one fifth of the artifact that hid it. The convergence result stands;
the "fails its precision gates" half is not evidence until re-scored at `/5`.

> **Closed 2026-08-05 (wave 5): re-scored, and the clause is REFUTED.** Arm (a) scores precision **1.000** on all
> three datasets it was recorded as failing the ≥ 0.90 gate on (D09, D12, D15), beating the control on two. The
> full table and what it cost instead (recall) are in [F26](#f26--a-stuck-binning-recommendation-starves-the-step-update-indefinitely).
> **This audit's own verdict is unchanged** — no entry's conclusion depended on the repaired metric — and the one
> exposure it identified has now been measured rather than left as a caveat. Cost: six `golden eval` arms, four
> minutes.

**Two entries are changed by [F33](#f33--the-synthetic-bank-does-not-reproduce-the-real-banks-optimizer-failure-mode)'s
effective-gate correction instead — a different repair than the one being audited for.**

- **F8** reads three landings as `sens 0 / clip 0.25`, `sens 0 / clip 6.875`, `sens 30.1 / clip 3.44` and calls
  them non-reproducible. By **effective** gate they are further apart still — roughly **0.19**, **5.2** and
  **30.1** — so the entry's conclusion is not merely intact but understated. Two landings that read as the same
  "sens 0" corner are enforcing gates 27× apart.
- **F3** cites `mccomiskey` A shedding to recall 0.079 as evidence `Wtie` does not prevent star-shedding. That
  run's gate is now known to be **9.81**, not the floor its Sensitivity 0.0 suggests, which identifies the
  shedding *mechanism* (the clip escape route) the entry left unnamed.

**Why it matters.** The blanket assumption — "these were measured on suspect landings, so they are all suspect" —
would have cost a full re-measurement pass and found nothing. The actual exposure was one clause in one entry.
Recording the audit so it is not re-opened on the same inference next wave.

### F45 — The Grubbs test rejects the IN-FOCUS point of a near-perfect curve, and the blind walk then buys an extra exposure
**Status:** Open · found 2026-08-06 (wave 6) from a real 40 mm simulator run · **mechanism reproduced offline
from the run's own report**, not inferred

**The observation.** A blind sweep logged its queue decision against a minimum its own data does not support:

```
21:47:08 Enough left trend points (4) with an established minimum (25015) to queue remaining right focus points up to 25075
```

`AutoFocusEngine.cs:1485` sets `targetMaxFocuserPosition = trendlineFit.Minimum.X + (failedRightPoints +
offsetSteps) · stepSize`. `TrendlineFitting.Minimum` is **not a fitted vertex** — NINA core's
`TrendlineFitting.Calculate` sets it to `argmin(Y + ErrorY)` over the points it is HANDED. The run's own report
(`2026-08-05--21-47-43--90d513b9….json`, step 15, `offsetSteps` 4) makes the anchor look plainly wrong:

| position | HFR | error | `Y + ErrorY` |
|---|---|---|---|
| **25000** | 0.6941 | 0.1736 | **0.8677 ← argmin of the measured set** |
| 25015 | 0.7709 | 0.2459 | 1.0168 |

with `CalculatedFocusPoint = 24999.996` and hyperbolic R² = **0.99928**. Anchored at 25000 the target is 25060,
which had already been sampled ⇒ the walk stops; anchored at 25015 it is 25075 ⇒ **one extra step and one extra
exposure**, on every run that hits this.

**The mechanism, measured.** Feeding the report's own points back through the engine's fitting loop
(`WeightRegularization.Regularize` → `AlglibHyperbolicFitting` → `MathUtility.RejectionTest` → drop → refit,
`MaxOutlierRejections = 1`, confidence 0.99, `WeightedHyperbolicFitEnabled`):

| set | rejected | resulting `Minimum.X` |
|---|---|---|
| the 10 points present at the decision (24925–25060) | **25000** | **25015** |
| the final 11 points | **25000** | 25015 |
| the same 10 points, rejection disabled | — | **25000** |

**So `Minimum` is computed on the POST-REJECTION set, and the point rejected is the in-focus one.** σ is
regularized but untouched here (the floor is 0.2 × median = 0.023; no σ is that small), so regularization is not
the cause.

**And the rejection itself is the more serious half.** The weighted residuals the Grubbs test ranks, on a fit with
R² = 0.9994:

| position | residual (px) | weighted | Grubbs z |
|---|---|---|---|
| **25000** | **+0.0345** | +0.1987 | **2.5804** ← rejected |
| 24940 | +0.0116 | +0.1934 | **2.5277** ← also over the limit |
| 24970 (next highest) | −0.0321 | −0.1728 | 1.1714 |
| every other point | ≤ 0.034 | ≤ 0.14 | ≤ 0.84 |

The limit at N = 10, confidence 0.99 is **2.4821**; the scale is `median = −0.0569`, `MAD = 0.0990`. Two points
clear the limit and the larger wins. The scale is the **MAD of the weighted residuals** (`MathUtility.cs:163`) —
deliberately robust, and correctly so against a gross outlier — but on an *excellent* fit the residuals are both
tiny and tightly clustered, so the MAD collapses and ordinary scatter reads as a 2.58σ outlier. The rejected
point's residual is **0.0345 px against its own measurement error of 0.174 px**: it is consistent with the curve
to a fifth of its own error bar, and is discarded as an outlier anyway.

**Why it matters, beyond one exposure.**
- The discarded point is the **most informative one in the sweep** — the in-focus measurement the whole run exists
  to obtain. Here it costs little (the hyperbola vertex is pinned by 10 other points, and it still landed at
  24999.996); on a sparser sweep, or a rig where the near-focus frames are the only ones with stars, discarding it
  is not free.
- A **robust scale estimator applied to a near-perfect fit inverts its own purpose**: the better the fit, the
  smaller the MAD, and the more aggressively the test fires. The guard is loosest exactly where it is needed and
  tightest where it is not.
- The blind walk's cost is deterministic and per-run: one exposure, on a rig class whose exposures are the
  expensive part.

> ### WAVE 13: MEASURED ON A POPULATION, AND THE COMPLAINT IS REAL BUT RARE AND SMALL
>
> `af-fit`'s rejection-budget table (this entry's own reproducer, printed since wave 6 and read for exactly one
> run) was run over **20 synthetic datasets** and **19 real bank runs**, at the pinned
> `OutlierRejectionConfidence` 0.95 — which fires MORE readily than the 0.99 of the field case above (the Grubbs
> limit at N=9 is 2.2150 at 0.95 against 2.3868 at 0.99).
>
> | | fires | silent |
> |---|---|---|
> | 20 synthetic datasets | **4** | 16 |
> | 19 real bank runs | **3** | 16 |
>
> **`D16_esprit550_ha3` is this entry reproduced with GROUND TRUTH.** The point the Grubbs test discards is
> position **8000 — the generator's true focus** — on a fit with R² = 0.9954, and the vertex then moves *away*
> from it (error 0.00067 → 0.00200 step). F45 found this once, on one field run, and could only argue that the
> discarded point "is the most informative one in the sweep". It is now reproduced where the right answer is
> known by construction.
>
> **But "the rejected point is the IN-FOCUS one" is one case, not the rule.** Across the four synthetic firings
> the rejected point sits at **0, 1, 2 and 3 steps** from truth; on the real bank the median distance is
> **1.25 steps**. And the displacement is tiny: median **0.00128 step**, largest **0.0099 step** — far below
> anything that could cost the blind walk an exposure on these sweeps.
>
> **What IS serious is what happens with a bigger budget.** At `MaxOutlierRejections` = 3, `caboose` — a real
> run whose fit has R² = **0.99987** — degrades to σ_focus **1.4325 → 18.185 (12.7×)**, R² → 0.9235, reduced χ²
> **0.00427 → 3.342**.
>
> > **The DIRECTION carries this, not the factor** (wave-14 [F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)
> > audit, [`docs/wave14-f62-audit.md`](wave14-f62-audit.md) H4). F62's arithmetic predicts a rejection makes
> > σ_focus *fall*; here it rose, so the effect cannot be the fit grading itself. But σ_focus also grows
> > **mechanically** as the surviving point count approaches the parameter count — `s² = weighted RSS/(n−p)` and
> > `(JᵀWJ)⁻¹` both grow, and this is 5 points against 4 parameters — so **"12.7× worse σ_focus" is not an
> > accuracy factor and must not be quoted as one**, and no out-of-sample vertex check was ever run at budget 3.
> > **Reduced χ² 0.00427 → 3.342 and R² 0.99987 → 0.9235 carry no `n − p` term**: the refit fails on the points
> > it *kept*. *(The same catastrophe measured through `af-fit` instead of `bank-verify` gives σ_focus
> > 0.364344 → 16.4599 — 45.2×. Two pipelines, not two measurements of one number. Never quote one for the
> > other.)*
>
> That is this entry's own mechanism running to completion: the better the fit, the smaller
> the MAD, the more aggressively the test fires, and each removal tightens the scale for the next. **The shipped
> default of 1 is the BOUND, not merely a safe value** — the smallness of the budget is the only thing containing
> it. See [F63](#f63--the-optimizers-landing-moves-on-6-of-8-runs-under-a-knob-that-is-nearly-inert-at-the-seed-so-every-landing-waves-5-12-published-was-produced-at-a-non-default-value)
> for the one place the knob is NOT inert: the optimizer's landing moves on 6 of 8 runs.
>
> Reproduce: `D:\hf_w13\affit_w13.sh`, `D:\hf_w13\affit_syn_score.txt`, `D:\hf_w13\bv_compare_mor3.txt`.

> ### WAVE 14: SUB-QUESTION (b) MEASURED ON A SIX-RUNG LADDER — **RULE F14 RETURNS NO VERDICT**, and the sub-question turns out to be written in the WRONG UNITS
>
> An off-by-default scale floor was added to `MathUtility.RejectionTest` (`--mad-floor f` / `--round1-floor α`
> on `af-fit`) and all 39 runs were re-fitted at six rungs: family **A** `scale_k ← max(scale_k, f)` for
> `f ∈ {0.00, 0.25, 0.50, 1.00}`, family **B** `scale_k ← max(scale_k, α·scale_1)` for `α ∈ {0.50, 1.00}`.
> The floor is applied **after** the existing degenerate-scale guards, so it can only ever **suppress** a
> rejection, never add or redirect one. 66 m 44 s for six rungs.
>
> **The verdict is NO VERDICT and the failing gate is V4** — the clause that tests the counterfactual's own
> prefix lemma. It failed on `Panos_attempt01` for a reason that is a real property of the shipped fit and not a
> defect in the floor: see [F65](#f65--the-hybrid-consensus-is-an-intersection-over-four-models-that-can-reject-the-same-points-in-a-different-order-so-the-rejected-set-at-budget-b-is-not-a-prefix-of-anything).
> **Everything below is therefore raw numbers, not a recommendation, and no rung is named.**
>
> | rung | max ρ = σ_focus(b3)/σ_focus(b0) over the 13 firing runs | `N_fire@1 / @2 / @3` of 39 | `caboose` at budget 3 |
> |---|---|---|---|
> | `A0.00` (control) | **45.1768** (`caboose`) | 7 / 13 / 13 | Symmetric, R² 0.91475, minPos 7112.74 |
> | `A0.25` | 1.1304 (`D12`) | 3 / 5 / 5 | **fixed** — TiltedHyperbola, R² 0.99999, minPos 7101.58 |
> | `A0.50` | 1.0000 | 2 / 2 / 2 | **fixed** |
> | `A1.00` | 1.0000 | **0 / 0 / 0** | **fixed** |
> | `B0.50` | 45.1768 | 7 / 13 / 13 | unchanged |
> | `B1.00` | 45.1768 | 7 / 11 / 11 | unchanged |
>
> **What the ladder does establish.** (1) Family A **reaches** `caboose` at every rung ≥ 0.25 and family B
> reaches it at neither — pre-registered before the arms and confirmed, because `caboose`'s scale is already
> collapsed at round 1 (1.48e-5) so an anchor at round 1 is an anchor to the collapse. (2) The fix is never
> **selective**: every rung that repairs budget 3 also suppresses budget 2's *benign* rejection (σ 0.364 →
> 0.172, R² → 0.999999). (3) The window where containment and selectivity coexist is **one rung wide** on this
> ladder. (4) The out-of-sample arbiter cannot decide it: median `|Δe|` is **0.00000 step** at every rung, and
> the largest movement reachable anywhere in the synthetic bank is **0.00993 step** against a 0.10-step
> materiality floor. (5) The added parameter is **inert at its default**, measured — all 39 budget tables
> reproduce wave 13 field-for-field at `f = 0.00`.
>
> ### AND THE SUB-QUESTION'S OWN WORDING IS WRONG ABOUT THE UNITS
>
> (b) asks that a point not be an outlier *"while sitting well inside its own error bar"*. `AlglibHyperbolicFitting`
> documents that its per-point σ is the **star-ensemble scatter** (1.483·MAD), which *"overstates the uncertainty
> of the plotted median HFR by roughly √(detected stars)"*. So the standardized residual `r` is ≈√N\* smaller
> than a unit-normal residual **by construction**, and an **absolute** floor in `r` units has an aggressiveness
> set by the star count — the wrong dependency. Across this bank √N\* spans 10× while the ladder spans 4×:
> **the ladder measures behaviour at fixed `f` and cannot select `f`.**
>
> The right quantity is free — `af_fit_points.csv` prints `Stars` per position — and it separates cleanly.
> **`s = |r|·√N\*` is the residual in units of the standard error of the plotted median.** Over all 42
> rejections in the 39 wave-13 runs:
>
> | | measured |
> |---|---|
> | rejections on the one run with ρ > 2 (`caboose`, 45.18) | **3 of 3 at `s` < 1** — 0.1010, 0.0403, 0.0000 |
> | rejections on runs with ρ ≤ 1.10 | **32 of 33 at `s` ≥ 1** (97 %); the exception is `D08`'s third, `s` = 0.3883 |
> | spread over all 42 | min 0.0000, median 3.71335, max 79.7824 |
>
> **The pathological rejections sit at a tenth of a standard error and the benign ones at three to eighty. No
> floor in raw `r` units can tell them apart** — `caboose` round 1's scale is 1.48e-5 and `D01`'s is 0.9885, five
> orders of magnitude apart on the same ladder — **and a floor in SEM units can, with one threshold.**
>
> **It does NOT vindicate this entry's original complaint, and that is the honest half.** `D16_esprit550_ha3` —
> the case where the discarded point **is** the generator's true focus — sits at `r` = +1.3307, N\* = 43,
> **`s` = 8.7260**. A SEM-unit floor near 1 leaves that rejection untouched. *A point can be a genuine large
> deviation in its own error bar and still be the one you must keep, and no scale floor of any kind knows which.*
>
> **ρ is a CONTAINMENT measure, not an accuracy factor** — it is legitimate above only because `ρ = 1` exactly
> when nothing was removed. And `ρ > 2` is `caboose` and nothing else: n = 1 on the benefit side, stated here
> rather than discovered later.
>
> Reproduce: `bash /mnt/d/hf_w14/affit_w14.sh <family> <rung>`, `/mnt/d/hf_w14/stageA/sem_audit.tsv`,
> `python3 /mnt/d/hf_w14/score_affit_w14.py --root /mnt/d/hf_w14 --w13 /mnt/d/hf_w13 --stagea /mnt/d/hf_w14/stageA`,
> `docs/synthetic-af-bank-followups-wave14-results.md` §3.

> ### WAVE 16: THE SEM CRITERION IS BUILT AND MEASURED ON SIX RUNGS — **RULE S16 returns NO RECOMMENDATION, MECHANISM RECORDED**, and no rung is named
>
> `SemScaleSpec` (commit `9e95446`), modelled line-for-line on `MadFloorSpec`: `default` is `None`, private ctor,
> `Veto(t)` and `Rank()` the only factories. Threaded `SelectBestModel → FitWithOutlierRejection → RejectionTest`
> with `N*` as a `Func<double,double>` side map keyed by `p.X` — **not** on `ScatterErrorPoint.Tag`, because
> `WeightRegularization` rebuilds every point and forwarding `Tag` means editing a production class on the
> production path. **Off by default; 28 tests; six rungs × 39 runs in 66 m 19 s.**
>
> **Rungs: `N` (control), `V0.07`, `V1.00`, `V2.00`, `V4.00` (family V — veto the selected rejection when its
> `s = |r|·√N*` is below `t`), `R` (family R — rescale `errors_i ← r_i·√N*_i` before the median/MAD).** Only the
> **veto** is licensed by wave 14's measurement, and **family R was excluded from V4′ and from RECOMMEND by
> pre-registration**: wave 14 measured `s` at the point the *current* rule selects, and a re-rank changes which
> point is selected.
>
> **All five validity gates PASS.** W1 — the control rung's 39 `af_fit_summary.txt` are **byte-identical** to wave
> 14's `affit_A0.00`, 0 field differences over 39 × 4 × 7 — is the only clause that can catch a parameter that is
> not inert at its default, and it is measured **where the rejection executes** (the gate runs at
> `MaxOutlierRejections = 0`, where `RejectionTest` is never called). W3 ties three independently printed
> quantities — `s`, `|r|`, `N*` — to the CSV on every round: **0 mismatches, 0 could-not-look.** V4′ 468 triples /
> 0 violations; V4′-CONFORM reproduces wave 15's 585 / 0 / 2 / 195.
>
> | clause | threshold, fixed before the data | measured |
> |---|---|---|
> | **S16-A(a)** | at `V1.00`, rejections surviving on runs with `ρ > 2` **== 0** | **0 of 2** — *n = 1 run by construction: `caboose`* |
> | **S16-A(b)** | at `V1.00`, rejections surviving on runs with `ρ ≤ 1.10` **≥ 30 of 33** | **12 of 12** — 100 % of the property, **FAILS an unsatisfiable bar**. [F68](#f68--a-threshold-stated-as-a-count-carries-a-denominator-and-three-consecutive-satisfiability-analyses-have-checked-the-value-a-clause-can-reach-without-checking-the-population-it-is-computed-over) |
> | **S16-B** `ΔR² ≥ −0.005` and `κ ≤ 3.0` vs each run's own budget-0 row at `N` | per rung | `V0.07` / `V1.00` / `V2.00` **−0.000053 / 1.246** (`D17`) · `V4.00` **+0.000000 / 1.000** · **`R` −0.085240 / 1.243e+04 FAIL** (`caboose`, identical to the control's own failing values) |
> | **S16-C** | `N_fire@1 ≥ 1` and `N_fire@3 ≥ 1` | `V0.07` 7/13 · `V1.00` 7/12 · `V2.00` 4/7 · `V4.00` 4/4 · `R` 9/15 — **a criterion, not an off-switch, at every rung** |
> | **S16-D** | three-valued on `caboose` at budget 3 | **`V0.07` CONTAINS-SELECTIVELY** · `V1.00` / `V2.00` / `V4.00` **CONTAINS-BLUNTLY** · `R` **DOES NOT CONTAIN** |
> | **S16-E** | veto at median \|Δe\| ≥ 0.10 step; alarm at any `e` ≥ 1.0 step | **no veto anywhere** (median \|Δe\| = 0.00000 at every rung); **alarm never fires** (max `e` 0.03000 step on V, 0.13150 under `R`) |
>
> **S16-B deliberately uses statistics with no `n − p` term**, against each run's own budget-0 row at rung `N` —
> a reference the treatment **cannot move**, because budget 0 never calls `RejectionTest`. σ_focus and `ρ` are
> reported and are never a bar
> ([F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)).
>
> **What is established.** (1) The separation is real at the **production consensus level over all four candidate
> models** for the first time — the gap that refuted wave 14's V4: at `V1.00`, `caboose`'s budget-3 catastrophe is
> fully suppressed and **16 of the 18 consensus rejections on the bank survive**. (2) **Selectivity is attainable
> in this family and no MAD floor could reach it**: `V0.07`'s budget-3 row for `caboose` is `N`'s budget-2 row
> exactly — the damaging rejection of `6975` (s = 0.0403) gone, the benign rejection of `7275` (s = 0.1010) kept,
> R² 0.999999, κ 0.223. Wave 14 measured **every** floor rung that repaired budget 3 as also suppressing budget 2.
> **`V0.07` is a `caboose`-derived probe rung, `n = 1` by construction, and outcome 2 was defined on `V1.00` alone
> so it could never become a recommendation.** (3) The parameter is **inert at its default, to the byte**. (4) Out
> of sample, family V moves two datasets and **both move toward truth** — `D08` −0.00122 step, `D12` −0.00993 —
> both exactly the maximum the pre-registration computed as reachable.
>
> **FAMILY R: R-REDIRECTS on 12 of 39 runs**, each rejecting positions rung `N` never rejects at **any** budget.
> R rejects **more**, not fewer — 30 consensus rejections at budget 3 against the control's 18 — and on
> `CWhiteFocus`, where the four models disagree so completely that the intersection is empty at every budget, the
> rescaling **manufactures a consensus** on 20350 and 20650. It fails containment by four orders of magnitude and
> produces the only out-of-sample movement in the wave above the materiality floor:
>
> > **`D13_apo200_1800mm` under `R`: R² 0.998952 → 0.999834, and the vertex lands 16.7 focuser counts from the
> > generator's truth having been 0.1 counts from it — Δe = +0.13071 step, 3.9× the sum of the five improvements
> > R does produce** (truth 12000, step 127). The fit got
> > better and the answer got worse: [F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)
> > reproduced on a mechanism F62 never touched. **Re-ranking should not be pursued.**
>
> **The honest half, unchanged.** `D16_esprit550_ha3` — this entry's headline case, where the discarded point
> **is** the generator's true focus — sits at **`s` = 8.7260**. No threshold anywhere near 1 touches it and none
> is proposed that would.
>
> ### AND IT SHIPS NOTHING — the harness change is done, the product change is not, and the price is recorded
>
> `N*` is available where σ is **computed** (`HocusFocusStarDetection.cs:804-806`, where `hfrStars.Count` and
> `result.DetectedStars` both sit beside the `MedianMAD`) and **gone by the time the fit's points are built**
> (`AutoFocusEngine.cs:1071`), because the only carrier between them is `MeasureAndError` — a NINA NuGet `struct`
> with exactly two `double`s, not subclassable. **There is no `MeasurePoint` type in this repo**; the harness's
> row type is `AfFitDiagnosticRunner.PointRow`, which already carries `Stars`, which is why the harness change
> needed no production plumbing at all.
>
> **Shipping needs ~3–4 h plus tests**: (a) a count map on `AutoFocusRegionState` cleared with the measurements;
> (b) a **pooling rule** for multi-frame points that does not double-divide against `AverageMeasurement`'s
> existing `/√frames` (`CvImageUtility.cs:891-921`); (c) the same again in `RunEvaluationData` (`:745, :783,
> :788`), the optimizer's independent point-building path; (d) a decision between `DetectedStars` and
> `hfrStars.Count`, which differ whenever saturated stars are excluded and only the second of which is σ's actual
> denominator. **The ~2 h the register carried was right for the HARNESS and wrong for the PRODUCT.**
>
> **The wave's best reachable outcome was a costed recommendation, never a ship — and it reached outcome 4
> instead, so there is not even that.** The price above is recorded so a future wave does not re-derive it.
>
> Reproduce: `bash /mnt/d/hf_w16/affit_w16.sh <rung>` (the driver refuses every rung but `N` until
> `/mnt/d/hf_w16/W1_PASSED` exists, and only the scorer writes it);
> `python3 /mnt/d/hf_w16/score_sem_w16.py --w1 /mnt/d/hf_w16/affit_N --w14 /mnt/d/hf_w14/affit_A0.00` (clause W1);
> `python3 /mnt/d/hf_w16/score_sem_w16.py --conform --w14root /mnt/d/hf_w14` (V4′-CONFORM);
> `python3 /mnt/d/hf_w16/score_sem_w16.py --root /mnt/d/hf_w16 --w13 /mnt/d/hf_w13 --w14root /mnt/d/hf_w14 --out
> /mnt/d/hf_w16/s16_score.txt`; `python3 /mnt/d/hf_w16/score_sem_w16.py --self-test` (both directions, every
> clause); `docs/synthetic-af-bank-followups-wave16-results.md` §2.

**Next step, and what NOT to do.** Three separable questions, deliberately not answered here:
(a) should the queue anchor be a *fitted* vertex (`HyperbolicFitting.Minimum.X`, or `Intersection`) rather than a
post-rejection argmin data point — note `Minimum` is also the anchor for the left/right trend split, so changing
its meaning is not local; (b) should `RejectionTest`'s MAD scale carry a **floor tied to the points' own measured
σ**, so a point cannot be an outlier while sitting well inside its own error bar — **wave 13 raises the priority
of (b) and lowers (a) and (c)**: at budget 1 the symptom is real but costs ≤ 0.01 step of vertex, while (b) is
what stops the `caboose` cascade at budget 3, and (b) is also the only one of the three that would let the
budget be raised safely; (c) should the walk's
`while (rightMostPosition < targetMaxFocuserPosition)` compare with a half-step tolerance, which bounds the
symptom without touching either fit. **(b) changes every AF fit in the product** and must be measured on the bank
before it is contemplated — the AF-bank σ_focus/R² arms are the instrument.

**Wave 14 re-shapes (b) and prices its successor.** (b) as literally written — an absolute floor in `r` units —
is measured above and returns NO VERDICT; it is also the wrong parameterisation, because `r` carries a ≈√N\*
factor the ladder cannot span. **The named successor is the SEM-unit floor**: compute `z` on `r·√N\*` rather
than on `r`. **Price ~2 h** — `ScatterErrorPoint` carries `(X, Y, ErrorX, ErrorY)` and no star count, so `N*`
must be threaded from `MeasurePoint` construction through the fit to `RejectionTest`, plus tests. **The cheapest
unfinished piece is not that**: repairing RULE F14's V4 ([F65](#f65--the-hybrid-consensus-is-an-intersection-over-four-models-that-can-reject-the-same-points-in-a-different-order-so-the-rejected-set-at-budget-b-is-not-a-prefix-of-anything))
and re-scoring the six rungs already on disk costs **minutes of Python and zero `TestApp` time**.
**And after wave 14's item 1 the ship value of any floor is contingent**: with `MaxOutlierRejections` defaulting
to 0 the rejection does not run at all, so a floor reaches only profiles that explicitly store ≥ 1, and any
future decision to raise the budget.

**Wave 16 BUILT the named successor and measured it** — the block above. Two corrections to the pricing carried
here for six waves: **there is no `MeasurePoint` type in this repo** (the harness's row type is
`AfFitDiagnosticRunner.PointRow`, which already carries `Stars`; production's carrier is `MeasureAndError` →
`ScatterErrorPoint`), and **~2 h is right for the HARNESS change and wrong for the PRODUCT change**, which is
**~3–4 h plus tests** for reasons (a)–(d) above. **The measurement is now done and the criterion still ships
nothing**, on the pre-registered branch table's own terms.

**Reproduce (offline, no rig, ~1 min):** feed the eleven `MeasurePoints` above through
`WeightRegularization.Regularize` → `AlglibHyperbolicFitting.Create(…, TiltedHyperbola, pts, stepSize: 15,
useWeights: true)` → `MathUtility.RejectionTest(pts, fit.Fitting, 0.99, BuildResidualWeights(pts, true))`, then
`new TrendlineFitting().Calculate(remaining, "STARHFR").Minimum.X`. Run log: `20260805-214043`.

### F46 — Detection binning buys faint stars and quietly sells bright ones to the shape/size gates
**Status:** Open · found 2026-08-06 (wave 7) running the F39(b) binning arm · **measured with attribution, not inferred**

Scoring the seven `detectionBinning = 2` datasets at their own factor for the first time
([F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(b))
improves overall recall on **all seven** — and moves `recall@high` the WRONG WAY on **four** of them:

| dataset | recall@all 1 → 2 | **recall@high 1 → 2** | non-`LowSensitivity` FN 1 → 2 |
|---|---|---|---|
| `D12_c14_585_afbin2` | 0.562 → 0.675 | 0.879 → **0.813** | 30 → **73** |
| `D15_cdk20_3454mm_e47` | 0.764 → 0.917 | 0.931 → **0.874** | 8 → **21** |
| `D08_c11_2800mm` | 0.870 → 0.962 | 1.000 → **0.970** | 0 → **9** |
| `D10_rc16_3250mm_sparse` | 0.852 → 0.939 | 0.966 → **0.948** | 2 → **6** |

**Where they go, from `golden eval`'s own FN attribution.** At factor 1 the misses are almost entirely
`LowSensitivity` (41 / 49 / 15 / 114 / 184 / 52 / 26 across the seven). At factor 2 those collapse (3 / 7 / 1 / 34
/ 4 / 0 / 2) and a different set appears that was **absent or near-absent at factor 1**: `NO CANDIDATE (structure
gap)`, `TooSmall`, `NotCentered`, `OnBorder`, and more `TooDistorted`. That is the trade named exactly: **binning
buys SNR with linear resolution**, and at half the resolution a compact star can fall out of candidate FORMATION
or out of a shape gate that is calibrated in pixels.

**Why it matters.** `DetectionBinningResolver` recommends the factor from measured in-focus HFR alone, and its
whole rationale is that pixel-unit knobs are calibrated for HFR near 3 px. The recommendation is delivered as an
unqualified improvement, and on `D12` it costs 6.6 points of recall on the brightest tier — the stars the AF fit
weights most. Nothing in the product tells the user that half of a knob's effect points the other way.

**Not F38.** The obvious explanation is
[F38](#f38--the-minhfr-seed-trigger-compares-a-captured-pixel-vertex-against-a-binned-pixel-gate)'s space
mismatch: `MinHFR` gates inside the binned raster, so factor 2 would effectively gate at 2.4 captured px. The
attribution refutes it — **`TooLowHFR` appears in none of the fourteen runs.**

**This BLOCKS [F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(b)'s
adoption wave, which is what raises it from an observation to a gate.** At binning 2 the bright-tier loss puts
**two datasets below their checked-in `recallHighMin = 0.90` band**: `D12_c14_585_afbin2` at **0.813** (L34) and
`D15_cdk20_3454mm_e47` at **0.874** (L47). `synthetic-af-bank-expectations.json`'s own rule forbids fixing that by
widening the band, so adopting the correct binning factor requires either reducing this loss or triaging the
breach on its merits with a stated re-derivation.

**Next step.** Two separable questions. (a) Are `MinimumStarBoundingBoxSize` and the structure-map layers
calibrated for the BINNED raster, or are they carrying captured-pixel values into a half-resolution image? They
are the two knobs whose units the factor changes, and `ApplyFactor` rescales only `PixelScale`. (b) Should the
binning recommendation be presented with its cost — "+13 points of overall recall, −6.6 on the bright tier" — or
should the factor be chosen to maximize something that sees both? Cheap to start: re-run the arm with
`--min-box` swept, which `golden eval` already exposes and which needs no code.
Reproduce: `D:\hf_w7\f39b_golden.sh`; per-dataset outputs in `D:\hf_w7\golden`.

**ANSWERED 2026-08-06 (wave 8): question (a) is YES — the knob is `StructureLayers`, not `MinimumStarBoundingBoxSize`
— and NO blanket scaling ships, because the correction does not generalize.** Full working in
[`docs/synthetic-af-bank-followups-wave8-results.md`](synthetic-af-bank-followups-wave8-results.md) §1.

**First, this entry's own account of WHERE the bright stars go is corrected.** The list above (`NO CANDIDATE
(structure gap)`, `TooSmall`, `NotCentered`, `OnBorder`, more `TooDistorted`) was read off the ALL-TIER
attribution, which on `D12` explains 107 false negatives of which only 20 are bright. Wave 8 shipped a
**high-tier-only** attribution and a per-star `false_negatives_f<focuser>.csv`, and the bright tier says something
else. At binning 2, shipped defaults:

| gate, HIGH TIER only | `D15` | `D12` |
|---|---|---|
| `REJECTED:Contaminated` | **10** | 3 |
| `REJECTED:TooFlat` | 0 | **8** |
| `NO CANDIDATE (structure gap)` | **0** | 2 |
| `REJECTED:TooSmall` | **0** | **0** |

**`D15`'s bright-tier loss is `Contaminated`, and its structure-gap count is zero.** `TooSmall` does not appear in
the bright tier at all.

**`MinimumStarBoundingBoxSize` is REFUTED, and inertly.** `--min-box ∈ {2,3,4,5}` at binning 2 is **byte-identical**
on both datasets. The flag works — `TooSmall: 6` disappears from `D12`'s all-tier table at `--min-box 2` — but
those six stars are **not recovered**: they fail the next gate instead (`TooDistorted` 19→22, `LowSensitivity`
34→37). *A gate's FN count is an UPPER BOUND on what relieving it buys, never an estimate* — filed as
[F50](#f50--a-false-negative-gate-count-is-an-upper-bound-on-what-relieving-that-gate-buys-not-an-estimate).

**Blending was also refuted** (`D:\hf_w8\p0\blend_check.py`, no detector run): the lost bright stars are ISOLATED —
median nearest golden neighbour 79 px on `D12` and 829 px on `D15`, i.e. 15× and 139× the in-focus HFR — and there
is no binning-2 detection within a match radius of any of them. But that arm produced the clue: **the lost stars
are LARGER than the kept ones** (`D15` 66×66 vs 42×42, several at 80×80), concentrated at the sweep wings. Big
defocused donuts, not small stars.

**The mechanism.** `StarDetector` step 4 subtracts an à-trous B3-spline residual to erase large-scale structure,
and its scale is `2^layers` **pixels**. Binning halves every structure's pixel extent while the layer count stays
fixed, so a donut that survived the subtraction at binning 1 is inside the residual at binning 2 and is erased
before candidate formation. The source's own comment predicts it: *"if the pixel scale is very small … you may
need to increase the number of layers to keep stars from being excluded."* `ApplyFactor` rescales only
`PixelScale`.

**The binning-1 control discriminates:** at binning 1 the shipped `StructureLayers = 4` is at or above the optimum
(every deeper setting is worse on `recall@all`, on both datasets); at binning 2 deeper is strictly better up to a
peak. `D15` goes 0.874 → **0.977** at layers 6, above even its binning-1 0.931.

**And the rule was refuted by the population that did not motivate it.** Measured on all seven (`R6`, fixed before
that sweep ran): `StructureLayers += log2(factor)` improves `recall@high` on **3 of 7** — not a majority — and
costs `D09` 0.011. `layers+2` fares no better. **Had this been run on `D12`+`D15` alone it would have looked like a
clean 2-for-2 win** and a hard-coded scaling would have shipped to every user on evidence drawn entirely from the
two cells that motivated it.

**So the defect is the DEFAULT, not the code path** — and every number here is measured at `--params default`, a
configuration no user keeps: `OptimizerVariable` already searches `StructureLayers` over `[1, 8]`, so a per-run
`optimize` finds the right depth. The product change is deliberately not made; like
[F47](#f47--a-focus-recovery-step-can-be-placed-where-nothing-is-detectable-and-now-there-is-a-number-that-says-so)
it changes live detection behaviour and wants its own before/after.

**Question (b) is still open** and is now the more interesting half.
Reproduce: `D:\hf_w8\p0\blend_check.py`, `D:\hf_w8\p1\knob_sweep.sh`, `D:\hf_w8\p2\layers_control.sh`,
`D:\hf_w8\p2\seven_sweep.sh`.

### F47 — A focus-recovery step can be placed where nothing is detectable, and now there is a number that says so
**Status:** Open · found 2026-08-06 (wave 7) closing F18's open decision (a)

[F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see) measured a
3800 mm run whose focus-recovery step landed at 8850 — **4.4× the minimum HFR, and past the last position that
detected anything** (5310). Wave 7 closed F18's decision (a) as *recovery steps may extend past the 3× band —
that is their purpose, and `JRun` exempts them from its hard floor — but never past detectability*, and shipped
the measurement half: `StepSizeRecommendation.MaxUsefulHalfSpan`, the outermost non-recovery frame that still
cleared `NHard`.

**Reported, not enforced.** Nothing clamps where `AutoFocusEngine` places a recovery step. Sizing the base step
for the recovery path instead was rejected as the default because it costs **20% of the lever arm on every
successful run** (divisor 3.5 → 4.375 at 4 offset + 1 recovery) to bound a path that only executes when a run has
already failed — a trade wave 7 settles by measurement rather than by argument.

**Why it matters.** A recovery step beyond `MaxUsefulHalfSpan` buys no measurement at any step size: it is one
exposure, one focuser move, and a frame that contributes nothing but a starved-frame rejection. On the rig F18
measured it is where all the flat-topped rejections came from.

**Next step.** Clamp the recovery step's placement to `MaxUsefulHalfSpan` when the value is finite, leaving it
unchanged when it is not (an unmeasurable bound is absent, never zero — the same rule F18's `W_detect` uses). This
is engine-side, changes live AF behaviour, and wants its own before/after; it is deliberately not bundled with
F18's recommender change.

### F48 — The executed-sweep step sizing is BIMODAL (6 cells better, 2 worse), and the median hides both
**Status:** Open · found 2026-08-06 (wave 7) running F18's arm S · **the pre-registered rule was applied as written and rejected it**

F18's arm S sizes the step for the points the sweep ACTUALLY visits (offset + focus-recovery per side) rather than
the offset steps alone. Over 26 scorable (dataset, scenario) cells it is:

| vs the control | cells |
|---|---|
| **>20% BETTER** | **6** — `D05` S1 0.2296 → **0.0055**, `D15` S1 0.1622 → **0.0471**, `D09` S1 0.0945 → **0.0406**, `D16` S3 0.1332 → **0.0465**, `D06` S1 0.0861 → **0.0154**, `D05`/`D06` others |
| within ±20% | 18 |
| **>20% WORSE** | **2** — `D15` S6 0.0457 → 0.0978, `D16` S6 0.0651 → 0.1433 |

**Median ratio: 1.0000.** F18's rule 2 ("arm S ships only if it beats arm D by >5% median") therefore did not
fire, and arm S does not ship. That verdict stands — the rule was fixed before the arm ran, and a pre-registered
statistic that turns out to be poorly matched to the data is a lesson for the next rule, not a licence to pick a
better statistic once the numbers are in.

**But the median is the wrong statistic here and that is worth fixing for next time.** 18 of the 26 ties are
STRUCTURAL: S0 converges in a single round on most datasets, so only round 0 is ever rendered and no arm can
differ. A median over a set dominated by forced ties reports "no effect" for a distribution with six clear wins
and two clear losses in it.

**Why it matters.** The wins are concentrated in the ×0.25-step scenarios (S1), i.e. runs recovering from a
too-narrow sweep, and the losses in S6 (step AND exposure both wrong). That looks like a structured signal — a
narrower executed sweep helping when the fit is being rebuilt and hurting when the run is also photon-starved —
and the median threw it away.

> **WAVE 14 — the "real, structured signal" reading is INVALIDATED, and the DENOMINATOR lesson is not**
> ([F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)
> audit H2/H5). **Arm S changes the STEP, so the two arms fit different focuser positions**, and σ_focus is
> `√(mgᵀ·s²·(JᵀWJ)⁻¹·mg)` in raw focuser units over the points actually fitted — a joint property of the curve
> **and of the sampling geometry**. Comparing it across arms that sampled different positions cannot separate
> *"the vertex is better determined"* from *"the abscissa was rearranged"*, and a **40×** ratio on `D05` S1 is
> far outside anything the register attributes to a real focus improvement anywhere else. There is no other
> support: no recall, no assertion count, and — the sharp part — **these are synthetic datasets, whose
> `renderRequest.OptimalFocuserPosition` was available the whole time and was never consulted.**
>
> **What stands.** The wave-7 verdict (the rule was applied as written, arm S does not ship) and **the
> methodological finding — *"the choice of denominator was worth more than the entire effect being measured"*** —
> which is about the arithmetic of medians over forced ties and which F62 cannot touch: the verdict really would
> have flipped on the movable denominator. **What does not stand is the reading underneath it**, that arm S
> produces *better* fits by 10.2 %. **No behaviour was changed on the strength of the number**, which is why this
> is a correction rather than a retraction. Arm D's half is untouched — *"arm D is 1.0000 on all twelve"* is a
> **non-identity** reading, and it never acted.
>
> **To close it:** re-score both arms on `|fitted vertex − renderRequest.OptimalFocuserPosition|`, which the bank
> has carried since it was built. Until then the S1-helps / S6-hurts reading is a hypothesis.

**Next step.** Two separable pieces. (a) Re-score arm S on the cells where the arms CAN differ (more than one
round rendered), which needs no new runs — the reports are on disk at `D:\hf_w7\f18arms`. (b) For any future
arm rule, state the statistic over the cells that can move, and say up front how many cells are expected to be
structural ties; a rule whose denominator is mostly ties cannot fire in either direction.
Reproduce: `D:\hf_w7\f18_arms.sh`, scored by `D:\hf_w7\score_f18.py`.

**PART (a) DONE 2026-08-06 (wave 8), offline, no new runs — and the denominator alone decided the verdict.**

| denominator | n | median σ_focus **S/C** | S >20 % better | S >20 % worse |
|---|---|---|---|---|
| all scorable cells (wave 7's) | 28 | **1.0000** | 6 | 2 |
| **movable cells only** (>1 round rendered) | **12** | **0.8977** | 6 | 2 |

Excluded as structural ties: **16 of 28** — `S0` = 6, `S2` = 6, `S3` = 4.

**Over the cells that can move, arm S beats the control by 10.2 %.** F18's rule 2 was *"arm S ships only if it
beats arm D by >5 % median"*, and arm D is **1.0000 on all twelve movable cells** — so on the movable denominator
**the rule would have FIRED and arm S would have shipped.** The data did not change; the denominator did.

**The wave-7 verdict still stands and this does not re-open it** — the statistic was fixed before the arm ran and
was applied as written. What this buys is the quantified lesson: **the choice of denominator was worth more than
the entire effect being measured.**

**It also sharpens arm D.** Wave 7 reported "byte-identical on 26 of 28, bound in exactly one". Restricted to the
cells where an arm COULD act, **arm D is 1.0000 on all twelve** — its one binding cell (`D16` S2) is a one-round
cell that could not have differed anyway, and its σ_focus there is NaN. **Arm D never acted on a cell where acting
was possible.** [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see)'s
flag staying default OFF is better supported than wave 7 stated, not worse.

Part (b) — state the statistic over the cells that can move — is now a standing rule in the wave design template.
Reproduce: `D:\hf_w8\f48_rescore.py`.

### F51 — "Capture a new sweep and optimize" is gated on the EXPOSURE recommendation, so it hides exactly when the run needs re-running — and it would not carry the new step size anyway
**Status:** Open · found 2026-08-06 (wave 8) from the same field session as
[F49](#f49--the-star-signal-block-fires-on-a-floored-gate-but-every-remedy-it-owns-is-an-exposure-remedy-so-a-rich-well-exposed-field-gets-a-diagnosis-with-no-instruction) ·
mechanism confirmed in source

The user's session, from the NINA log — four wizard runs, each one told to widen by the previous one:

| run | step | exposure | recommended next | outcome |
|---|---|---|---|---|
| 12:30 | 100 | 2 s | **214** | accepted, ~38 min |
| 13:10 | 214 | 2 s | **459** (*"capped by this sweep's width"*) | accepted at **Sensitivity 0.000**, ~52 min |
| 14:03 | 459 | **5 s** | 474 | good landing, Sensitivity 2.5, ~30 min |
| 14:36 | 459 | 2 s | 482 | **~2 hours** |

**The user had to Accept a landing they did not want, twice, in order to re-run with a wider sweep.** Their words:
*"Ideally that shows up as an option to re-run entirely with the updated value without having to accept or go back
(which is what happened here)."*

**The action they wanted EXISTS — `CaptureNewSweepCommand` — and was hidden.**

```csharp
public bool ShowCaptureNewSweep =>
    HasExposureBlock && lastRunWasLive && !IsUseCurrentMode
    && (SelectedSummary?.ExposureAdvice?.IncreasesExposure ?? false);
```

On run 2 the first three conjuncts were true (the gate was floored at 0.000, the run was live, the mode was
Optimize) and **`IncreasesExposure` was false** — the exposure row read *"2 s (unchanged; measured star S/N
1438.6; target 10)"*. So the button was hidden. **That is [F19](#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one)
on live hardware:** `S_now = 1438.6` against a target of 10 is the same saturation wave 8 measured on `D02`
(991.8 vs 10). F19's defect does not merely silence a *recommendation* — it removes the *button*.

**And even had it been visible it would not have solved this.** `RunLiveAttemptAsync` overrides only
`OverrideAutoFocusExposureTime` (plus focus-recovery steps) and takes everything else from
`autoFocusEngine.GetOptions()`, i.e. **the profile's step size**. So the re-capture path carries the exposure
recommendation and **not the step-size recommendation** — the one that was actually asking to change on every run
of this session. There is no re-run-at-the-recommended-step path at all; Accept-then-restart is the only one, which
is exactly what the user did.

**Why it matters.** The step recommender is explicitly a converge-over-runs mechanism (its own copy says *"re-run
auto-focus to refine"*), and the wizard has no affordance for the iteration it prescribes. Each cycle costs a full
sweep plus an optimization — here 30–120 minutes — and forces the user to write a landing they may not want into
their profile to get it. Note run 2's Accept is how `Sensitivity 0.000` reached the profile in the first place,
which is why runs 3 and 4 both show `Current` Sensitivity 0.000.

**Next step.** (a) Gate `ShowCaptureNewSweep` on *any* material recommendation — exposure **or** step size — rather
than on `IncreasesExposure` alone. (b) Make the re-capture apply the recommended **step size** as well as the
exposure, or say plainly that it will not. (c) Neither should require Accept: re-capturing is not adopting.
Reproduce: NINA log `20260806-122836-3.3.0.1048.73484-202608.log`; frames in `E:\AutoFocusSaves\`.

**ALL THREE SHIPPED 2026-08-07 (wave 9), and (a) turned out to have TWO causes rather than one.** The entry
names `IncreasesExposure`; the property also required `HasExposureBlock`, which is
`OptimizationSummary.HasLowStarSignal` — **Sensitivity ≤ 1.0**. So the button was hidden by the exposure
condition on run 2 *and* by the BLOCK's own condition on runs 1, 3 and 4, whose gates were healthy and which
therefore never had a re-run affordance at all. The entry's table records those three runs recommending 214, 474
and 482 with no way to act on any of them, and nothing in the entry had noticed the second gate.

| part | what shipped |
|---|---|
| **(a)** | `ShowCaptureNewSweep` = `lastRunWasLive && !IsUseCurrentMode && (IncreasesExposure ‖ StepSizeOrOffsetChanged)`. The `HasExposureBlock` conjunct is **gone**, and the row **moved out of the Star signal block** into the Auto-focus block — beside the step-size and offset rows, which are the values it now carries. The two MODE conditions stay (Replay has no rig; use-current has nothing to re-tune), because those are fixed for the life of the Summary |
| **(b)** | `ApplyRecaptureGeometry(options, stepSize)` in `RunLiveAttemptAsync`, applied **before** `ApplyFocusRecovery`. Set from the SELECTED summary by `CaptureNewSweepAsync` and cleared on **every** exit path, so an ordinary Live Start is byte-identical |
| **(b), the "or say plainly" half** | shipped **as well as** carrying it: `CaptureNewSweepCarriesText` states the geometry the next sweep will use (`"step size 214 → 459 … at the exposure below"`) and that nothing is written to the profile. A sweep geometry the user cannot see is how this defect stayed invisible for a whole session |
| **(c)** | already true and now reachable — the command persists nothing and Accept remains the only writer. What forced the Accepts was (a) hiding the control, not (c) |

**The house rule survives in both directions**, which is what the row's relocation had to preserve:
`StarSignalCopy`'s Live sentence naming the button fires on `increases && live && !useCurrent`, which is a strict
subset of the new visibility — so the copy can never name a hidden button. And in the new step-only state, where
the Star signal block is not on screen at all, the row carries its own sentence.

> **CORRECTION, caught by an existing test on the full-suite run.** The first version of (b) also carried the
> recommended **offset steps**, and `CaptureNewSweep_UsesTheSnapshottedRecoverySteps_NotTheLiveBox` failed with a
> re-capture widened to **5** where the recovery snapshot alone gives **1**. It is wrong for two independent
> reasons, and the test found both: `StepSizeRecommender` derives its step from the desired half-width over the
> **current** points-per-side, so the recommended STEP already expresses the whole geometry change at the existing
> offset — carrying the offset too widens the sweep twice; and `ApplyFocusRecovery` **owns** the offset axis and
> ADDS to whatever it is handed. **The re-capture now carries the step size and nothing else**, and the copy says
> so rather than promising a sweep it does not take.

**Tests: 3 discriminating on `ApplyRecaptureGeometry`** (carries the step; complete no-op at non-positive, so the
Start path is unchanged; composes with `ApplyFocusRecovery` without double-widening — which is the assertion the
correction above turned into a permanent guard).

**Still open, and it is the entry's real subject:** the step recommender asking to widen on **every** run because
the sweep never reaches `3 × HFR_min`. (a)–(c) make the iteration cheap; they do not make it terminate. That is
[F21](#f21--stepsizerecommenders-half-width-is-not-stable-against-noise-even-at-r--10000)/F49(c).

### F52 — A two-hour optimization logs ONE line and offers no cost context, and the search is not cost-aware
**Status:** **(a) and (b) DONE 2026-08-06 (wave 8); (c) DONE 2026-08-07 (wave 9) once F19's remainder
landed; (d) open** · found 2026-08-06 (wave 8) from the same field session · partly measured, partly **unmeasurable after
the fact, which is the finding**

Run 4 (step 459, 2 s) ran from 14:38:11 to past 16:30 — **over two hours**. Run 3, on the **same step size, same
sweep geometry, same 11 frames, same rig, same night, differing only in exposure (5 s)**, took ~30 minutes.

**Between `Loaded saved AF run …` at 14:38:11 and the end of the session there is exactly ONE log line.** Nothing
records how many evaluations ran, which phase it was in, or how much of the budget remained. Asked *"should I
abort and try again with a longer exposure?"*, neither the log nor the UI can answer.

**What IS measured, on the user's own frames** (`optimize --max-evals 1`, one settings file per arm, `UseAdvanced=True`
so the Simple-mode presets do not overwrite the knobs — [F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)):

| arm | config | wall |
|---|---|---|
| cheap | `StructureLayers 4`, `DefocusAwareStructure off`, `NR 3` | **33 s** |
| expensive | `StructureLayers 6`, `DefocusAwareStructure ON`, `NR 5`, hotpixel-thresholding off — **the config run 4 actually landed on** | **47 s** |

Only one of the two evaluations differs between arms, so the differing evaluation roughly **doubled**. Corroborated
on the synthetic bank, where the scaling is stark — `D15_cdk20_3454mm_e47`, 9 frames, binning 1:

| `StructureLayers` | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|
| seconds | 27 | 46 | **117** | 254 | **526** |

**≈2× per additional layer.** The à-trous residual is recomputed at `2^layers`, and `DefocusAwareStructure` adds
`StructureLayerBoost` on top of that.

**A hypothesis that was checked and is REFUTED: the floored gate costs nothing.** `Sensitivity 0 / StarClip 0.25 /
PeakResponse 0.55` vs `10 / 2 / 0.75` on the same frames measured **42 s against 42 s**. The acceptance gates are
LATE filters — they discard candidates after formation and PSF fitting — so admitting 5766 stars instead of 834
does not cost time. The obvious explanation for the slowness is wrong.

**So ~2× is attributed and the remaining ~2× is NOT**, and it cannot be recovered after the fact. The available
circumstantial evidence is the landings themselves: run 4 moved **13** parameters including four structural/
expensive axes (`StructureLayers`, `DefocusAwareStructure`, `NoiseReductionRadius`, `HotpixelThresholdingEnabled`)
while run 3 moved **9** and touched none of them. More axes explored ⇒ more evaluations. **Headless at a FIXED
budget the ordering even inverts** — 120 evals took 108 s on the 2 s frames and 150 s on the 5 s frames — which
rules out per-evaluation cost as the whole story and points at evaluation COUNT.

**Why it matters.** The objective is blind to cost: two candidate configurations scoring identically can differ by
an order of magnitude in wall time, and nothing prefers the cheap one or reports that the search has moved into an
expensive region. A user watching a progress bar for two hours has no way to know that a 2.5× longer exposure would
likely have finished in a quarter of the time.

**What the wizard ALREADY surfaces while it runs, checked before proposing to add any of it:** `Phase`, a
`current/total` counter (`SetProgress`), and live `ProgressSeedJ` / `ProgressBestJ` / `ProgressSeedSigma` /
`ProgressBestSigma` — so seed-vs-best `J` AND σ are already on screen. `CancelCommand` exists, so there is a real
control for any abort advice to name (the house rule at `ShowOptimizeAgainAtRecommendedBinning`: never describe an
action whose control is hidden). **What is absent is not the progress readout — it is TIME and WHY.**

**Next step, in three separable pieces. (b) and (c) are the user's actual request and they have different
readiness.**

**(a) DONE 2026-08-06 (wave 8).** `StarDetectionOptimizer` emits one INFO line every **10 completed evaluations**
(`SearchContext.ProgressEvaluationInterval`), carrying phase, evaluation count against the budget, elapsed,
seconds-per-evaluation and the incumbent `J` — plus the cost note when one applies. Verified on the field session's
own frames:

```
Optimizer progress: phase 'CoarseGrid',    evaluation 20/60, elapsed 00:00:23, 1.2s/evaluation, best J 0.998372
Optimizer progress: phase 'PatternSearch', evaluation 50/60, elapsed 00:00:41, 0.8s/evaluation, best J 0.99849
```

Cache hits are excluded from the rate (they cost nothing and would flatter it), and the evaluator is timed rather
than the surrounding bookkeeping, so `s/evaluation × remaining` is a number that means something.

**(b) DONE 2026-08-06 (wave 8).** Three facts under the progress bar, none needing a statistic that does not
already exist:

1. **The RATE and a bound on what is left** — `"0.8 s per step · at most 4 min more, usually much less"`.
   **Elapsed was already there** and is deliberately not repeated: `ProgressCountElapsedText` renders
   `X / Y (M:SS)` one line above and re-raises every second. Checking that first is what kept this from shipping a
   duplicate. The remaining figure is stated as an **upper bound**, because the evaluation budget is a cap the
   search usually stops well short of (`OptimizerSettings.MaxEvaluations` records a bank-wide convergence study
   finding it self-terminates before 400 on most runs) — a plain ETA would read as a promise and would usually be
   far too long.
2. **Which knob made it expensive**, quantified against **this run's own seed**: *"Searching at 8 structure layers
   (2 deeper than this run started at), which costs roughly 4x per evaluation."* Derived from
   `StarDetector.EffectiveStructureLayers` — extracted in this wave as the single source of truth so the readout
   and the detector's step 4 cannot drift — against the ~2×-per-layer scaling measured above. Absent entirely when
   the search is in a cheap region, because a readout that always says "expensive" says nothing.
3. **What Cancel costs** — *"Cancel stops the search only. Nothing is written to your profile unless you click
   Accept, and the frames already captured stay on disk."* Verified against the code: `Cancel()` only cancels the
   token, and `Apply` is reachable only from Accept. The frames clause appears on live runs only.

**Tests: 5 discriminating + 1 guard**, each confirmed by neutralizing the change and re-running — including one
correction found that way: the "stays cheap" test originally claimed to discriminate against an absolute-keyed
note and did not, because its seed sat at the shipped default of 4 where the two implementations coincide. Its
seed now turns the donut master on (effective depth 6, `StructureLayers` still 4) so the absolute implementation
reports "2 deeper" for a search that has not moved, and the test fails as claimed.

> **(c) SHIPPED 2026-08-07 (wave 9), on the statistic it was waiting for.**
> [F19](#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one)'s
> remainder landed as `ExposureRecommendation.WingIsShedding` — the fraction of candidates the gate rejected on the
> sweep's WING frames, which is the one population in a run not floored by the gate. **The separation this entry
> drew is kept exactly: facts about COST need no new statistic and are already shown; ADVICE needs one.**
>
> `StarDetectionOptimizerWizardVM.SearchExposureAdvice` is computed in `ComputeBaselineJAsync` — i.e. from the
> **SEED evaluation, which runs BEFORE the search** — so the answer to *"should I abort and try again with a
> longer exposure?"* was available in the first minute rather than after two hours, which was the complaint.
>
> It is **absent when the wings are healthy** (a note that always fires says nothing), it **names Cancel**, which
> exists, and it says **what Cancel costs in the same sentence**, because not knowing that is why the user sat
> through the two hours. It quotes **no recommended exposure**: the wing probe is a probe precisely because the
> rejected candidates' SNRs are not recorded, and the Summary's exposure row is where a number belongs once the
> run finishes.
>
> **Tests: 4, three discriminating** — and the multi-run one is CORRECTED. Its first version paired a shedding run
> with a healthy one, which the aggregation skips entirely, so "worst" and "average" were identical and it passed
> under a deliberately-averaged implementation. It now pairs two shedding runs (0.75 and 0.30) and fails as
> claimed.

**(c) ~~Recommend whether to abort and re-run at a longer exposure~~ — was BLOCKED ON
[F19](#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one),
and shipping it before that would be actively harmful.** This is the piece the user asked for by name. It cannot
be built on the existing exposure statistic: on this very rig that statistic reported *"2 s (unchanged; measured
star S/N 1438.6; target 10)"*, and wave 8's arm X measured the same saturation on `D02` (`S_now` 991.8 against a
target of 10, raw ask **0.000 s**, while σ_focus improves 44 % at 8×). **An abort-and-re-expose prompt derived
from `S_now` would tell precisely the users who need a longer exposure that theirs is already fine** — F19's
defect promoted from a silent omission into an active instruction. What (c) needs is F19's outstanding half: a
statistic that can see the WING frames (their star counts / σ contribution) rather than the 20th-brightest star of
the whole field. Until then (b)'s facts are honest and (c)'s advice is not.

**(d) Consider a cost term or an eval-time budget.** The search should not spend its budget in a region that is 4×
the price for a fourth-decimal gain — the same shape as
[F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains), in wall
time instead of recall.
Reproduce: `D:\hf_w8\field\compare_runs.sh`, `D:\hf_w8\field\cost_*.json`, `D:\hf_w8\field\gate_*.json`.

### F56 — The à-trous wavelet residual convolves a DENSE kernel that is 99.5 % zeros, and that IS the `StructureLayers` cost curve
**Status:** **DONE — fixed properly in [PR #187](https://github.com/ghilios/hocus-focus/pull/187)
(`AtrousWaveletFast`, `StarDetectorVersion` 1→2), which validated this entry's DIAGNOSIS and refuted this entry's
first FIX.** The kernel really is 99.5 % zeros; the stage is memory-bandwidth bound, so wave 9's five-pass
rewrite ran +50 % SLOWER and was reverted, while the sparse 5-tap SIMD path that replaced it measures
**2.5× / 9.7× / 40×** at layers 4 / 6 / 8 · found 2026-08-07 (wave 9) answering a question about GPU/SIMD builds

`StarDetector` step 4's structure-removal residual is the detector's dominant cost.
[F52](#f52--a-two-hour-optimization-logs-one-line-and-offers-no-cost-context-and-the-search-is-not-cost-aware)
measured it on `D15_cdk20_3454mm_e47`: **27 / 46 / 117 / 254 / 526 s** for `StructureLayers` 4→8, *"roughly a
doubling per layer"*, and shipped a UI note explaining that cost to users.

**The doubling is an implementation artifact.** `CvImageUtility.GetB3SplineFilter(L)` builds a 1-D kernel of
length `(1 << (L+2)) + 1` in which **exactly five taps are non-zero** — the B3-spline `0.0625 / 0.25 / 0.375 /
0.25 / 0.0625` spaced `2^L` apart — and its own comment states the intent: *"Rather than copy the matrix to
convolve it, we can pad the separated filter with zeroes."* `Cv2.SepFilter2D` then performs a **dense** separable
FIR over the whole kernel.

| layer | kernel taps | useful taps | wasted multiply-adds |
|---|---|---|---|
| **4** (shipped default) | 65 | 5 | **13×** |
| 6 | 257 | 5 | **51×** |
| 8 | 1025 | 5 | **205×** |

Cost is linear in kernel length, and the kernel doubles per layer — which reproduces F52's measured curve exactly.

**"À trous" MEANS "with holes", and the algorithm exists to be O(1) per layer**: convolve 5 taps at stride `2^L`.
OpenCV's `sepFilter2D` has no dilation parameter, which is presumably why the zero-padding was chosen, but a
hand-written strided 5-tap separable pass has no such limit and is trivially vectorizable.

> ### IMPLEMENTED, MEASURED, AND REVERTED THE SAME DAY — the FLOP analysis above does not predict wall time
>
> The strided rewrite was built, CI-verified (3682 passed / 0 failed; equivalence green at layers 1–6), merged,
> and then **measured against the pre-F56 binary on the 8-run sequential gate — the same runs, same invocation,
> sequential both times.**
>
> | run | pre-F56 | F56 strided | change |
> |---|---|---|---|
> | `toml999` | 154 s | 232 s | **+51 %** |
> | `CWhiteFocus` | 495 s | 795 s | **+61 %** |
> | `uneven` | 339 s | 641 s | **+89 %** |
> | `muggsie` | 65 s | 101 s | **+55 %** |
> | `mccomiskey` | 326 s | 351 s | **+8 %** |
> | `D18_m24_deep_shed` | 334 s | 442 s | **+32 %** |
> | **total** | **1713 s** | **2562 s** | **+50 %** |
>
> **Uniformly slower. Not one run faster. REVERTED.**
>
> **Why the arithmetic was irrelevant.** The à-trous residual is **memory-bandwidth bound, not FLOP bound.**
> `sepFilter2D` makes ONE pass per axis with the kernel resident in registers; the strided version as implemented
> made **five full-image read-modify-write passes per axis**, plus two `CopyMakeBorder` allocations per layer. It
> traded 13× fewer multiply-adds for 5× the memory traffic, and at these image sizes memory wins. The 13–205×
> figures are correct as FLOP counts and **do not predict runtime** — which is the whole reason the entry was
> filed as "ANALYSIS, NOT MEASUREMENT".
>
> **What IS established, and is worth keeping:**
> 1. **The rewrite is LANDING-NEUTRAL.** RULE G on the F56 binary passes **8 of 8 to 6 dp** — bit-identical
>    landings, `BaselineJ` included. So the à-trous can be reimplemented freely without invalidating any prior
>    arm; only speed is at stake.
> 2. **The kernel really is 99.5 % zeros**, and F52's measured 2×-per-layer curve really is that padding. That
>    part of the diagnosis stands.
> 3. **The right implementation is a SINGLE FUSED strided pass**, not five composed `Cv2` calls: one hand-written
>    pointer loop per axis gathering 5 taps at stride `2^L`, giving `sepFilter2D`'s memory traffic (one read, one
>    write) with 5 multiply-adds instead of `2^(L+2)+1`. That is the version that should win at every layer, and
>    it is what "O(1) per layer" was always supposed to mean.
>
> **Lesson, and it is this entry's real content: a FLOP count is not a benchmark.** The waste was real and the
> conclusion was still wrong, because the bottleneck was somewhere the analysis never looked. Measure the thing
> you are about to optimise before optimising it — the gate that measured this cost 33 minutes and would have
> cost nothing to run first.

**Why it matters beyond speed.** (a) `OptimizerVariable` searches `StructureLayers` over `[1, 8]`, so every
optimizer run pays this, hundreds of times — it is a large share of the two-hour runs F52 was filed about.
(b) [F46](#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates) showed
deeper layers are the RIGHT answer at detection binning 2, and the cost is what makes that expensive to adopt.
(c) It reframes F52(d): a cost term in `J` would be penalising an artifact rather than physics.

> ### RESOLVED by PR #187 — and the shape it shipped in is the one wave 9's failure pointed at
>
> `Utility/AtrousWaveletFast.cs` replaces the dense `SepFilter2D` with a **sparse 5-tap SIMD path** — one fused
> pass per axis, taps gathered at stride `2^L`, exactly the "single fused strided pass, NOT composed `Cv2` calls"
> this entry's revised next-step called for. Design: [`docs/atrous-wavelet-fast-design.md`](atrous-wavelet-fast-design.md).
>
> | `StructureLayers` | legacy `SepFilter2D` | fast | speedup |
> |---|---|---|---|
> | 4 (shipped default) | 130 ms | 52 ms | **2.5×** |
> | 6 | 781 ms | 81 ms | **9.7×** |
> | 8 | 4267 ms | 107 ms | **40×** |
>
> **Legacy cost doubles per layer — the F52 curve — and the fast path is nearly flat** (52 → 81 → 107 ms), which
> is the entry's central claim confirmed by benchmark rather than by arithmetic. It wins **even single-threaded**,
> so the gain does not depend on core count.
>
> **Three of wave 9's conclusions are now settled by it.**
>
> 1. **The diagnosis was right and the implementation was wrong.** Wave 9 counted FLOPs, predicted 20–100×, and
>    shipped a five-pass version that was measured **+50 % slower**. The correct version is 2.5–40× faster. The
>    difference is entirely one fused pass versus five composed ones — i.e. memory traffic, which the FLOP count
>    never looked at.
> 2. **It confirms the memory-bandwidth reading**, from the other side: *"both passes stream at memory bandwidth
>    once the tap count is fixed, which is why 24 threads only add ~1.5× over one thread."* That is also the
>    strongest available argument against a custom SIMD or CUDA OpenCV build — the bottleneck is bandwidth, not
>    instruction width.
> 3. **[F52](#f52--a-two-hour-optimization-logs-one-line-and-offers-no-cost-context-and-the-search-is-not-cost-aware)'s
>    cost note is retired with it**, and [F52](#f52--a-two-hour-optimization-logs-one-line-and-offers-no-cost-context-and-the-search-is-not-cost-aware)(d)
>    (a cost term in `J`) loses most of its motivation: structure-layer depth is no longer a cost driver worth
>    narrating or penalising, which is exactly the "you would be penalising an artifact" risk wave 9 flagged.
>
> **`StarDetectorVersion` 1→2** ships it through the sanctioned door: the paths agree to ≤ 3e-8 but are not
> bit-identical, so the bump invalidates cached detections rather than letting a changed output pass silently.

**Superseded next steps (kept for the record).** (a) **BENCHMARK FIRST** — the 8-run sequential gate is 33 minutes and
is the instrument that settled this; run it before writing any further optimisation, not after. (b) If it is
attempted again, the shape is a **single fused strided pass** (one hand-written pointer loop per axis, 5 taps at
stride `2^L`, one read and one write), NOT composed `Cv2` calls — the composed form is what lost. (c) Find the
crossover: F52's 27 → 526 s curve means dense must lose *somewhere*, so a hybrid that keeps `sepFilter2D` at low
layers and switches at the crossover may be the only version that wins. (d) Equivalence is already established —
RULE G passed 8/8 on the strided binary — so a future attempt needs only a benchmark, not a re-validation.
**On custom SIMD / CUDA OpenCV builds:** the stock native build already dispatches AVX2/AVX-512, and this
result is the argument against assuming any build-level change helps — the stage is bound by memory, not by
instruction width, so wider vectors have nothing to recover. Establish the bottleneck by measurement before
buying or building anything.

### F74 — A driver and its scorer each rebuilt the artifact path from a template, disagreed, and cost a pre-registered rule its verdict — while the interlock marker between them already carried the answer

**Status:** **FIXED BY CONSTRUCTION 2026-08-11 (wave 21), and exercised by a real six-dataset arm — see the
wave-21 block at the end of this entry** · found 2026-08-11 (wave 20) when **RULE D20 returned `D-UNEVALUATED` on
a probe that ran successfully and measured exactly what it was built to measure** · **the measurement it lost was
NOT recovered by re-running the rule: RULE D20 is permanently fenced and RULE B21 answered the question on a new
population**

Wave 20's detected-probe ran, `TestApp.exe` exited 0 in 16 s, and the log it produced contains the wave's whole
result. The rule scored **nothing**, because three path assumptions were written independently and none of them
matched what the driver actually did:

| # | the driver did | the reader expected | consequence |
|---|---|---|---|
| 1 | console → **`<probe>/probe.log`** (`detected_probe_w20.sh:100`) | **`<probe>/<DS>/run.log`** (same driver, `:104`; and `score_d20_w20.py:281`) | the driver aborted **its own** post-check and **never wrote `D20_PROBE_READY`** |
| 2 | `--out "D:\hf_w20\dprobe"`, **without** the per-dataset component the sibling gate driver passes (`gate_w20.sh:207`: `--out "${OUT}\\${r}"`) | a per-dataset subdirectory | outputs landed flat: `dprobe/attempt01/`, `dprobe/aggregate_summary.json` |
| 3 | — | `find "$OUT" -mindepth 2 -name aggregate_summary.json`, and `<probe>/<DS>/attempt01/optimized_settings.json` for clause `D20-V2` | both miss at depth 1, so **patching (1) alone would still not have produced a verdict** |

**The interlock behaved perfectly and that is the point.** The driver refused to certify a log it could not
find; the scorer refused to score without the certificate; the controller copied the log to the expected path
(byte-identical, sha256 `c61ef1ec…`) and the scorer **still** refused, correctly, because the marker is the
driver's to write. **No marker was hand-written.** The machinery did its job on a wave that had 3 h 52 m of
slack, a working binary and a correct measurement sitting in a file.

**Why it is a register entry and not a wave-20 Lessons line.**

- **It is the fourth instance in one run** — after `exeB1`/`exe_b1`, a mis-named fingerprint file, and a progress
  counter with no could-not-look state. One is an incident; four is a class, and classes belong where the next
  wave's author reads them.
- **It cost a pre-registered rule its verdict** — the same cost
  [F69](#f69--f39bs-flag-names-and-its-own-comment-state-the-opposite-of-its-default-and-that-cost-a-pre-registered-rule-its-verdict)(a)
  is in the register for. A wrong path and a wrong comment are the same failure with different syntax.
- **The wrong path is not even the wave's own convention.** `<root>/<dataset>/run.log` is **wave 19's** arm
  layout (`redo_w19.sh:157`). Wave 20's gate writes `<gate>/<name>.log` beside a `<gate>/<name>/` output root;
  wave 20's probe writes `<probe>/probe.log` beside a flat one. **Three layouts across two waves, and the scorer
  was written against the one that appears in neither of this wave's drivers.**
- **The fallback inherited the drift.** `score_d20_w20.py:301-305` already carries an `os.walk` fallback for
  exactly this hazard — rooted at `<probe>/<DS>`, the directory that does not exist. *A fallback rooted at the
  same wrong parent as the primary path is not a second chance.*

### Next step

(a) **Make the scorer read the path the driver printed. ~5 lines each side, and it is the durable fix.** The
marker was *already specified* to carry it — wave 20's design §8 gives `D20_PROBE_READY`'s contents as *"the
resolved factor and the probe log path"*, and the driver does write `log=$LOG` into it. **The scorer never
opens the marker; it checks existence and rebuilds the path from a template.** Parse `log=` (and add
`landing=`) out of the marker, so the marker's existence and the marker's contents are one handoff instead of a
boolean plus a coincidence. *An interlock that transmits one bit where it could transmit the path is a
handshake with the payload thrown away.*

(b) **Weaker, worth doing anyway: make sibling drivers in the same wave lay out identically** — the probe's
`--out` and log naming should be the gate's. This re-establishes agreement **by convention**, which is what
just failed; (a) establishes it **by construction**.

(c) **~3 minutes to recover RULE D20's verdict** — the probe is 16 s of `TestApp` plus the path fix. It must be
re-run and re-scored by the instrument; **the diagnostic must not be promoted to a verdict by hand.**
See also [F68](#f68--a-threshold-stated-as-a-count-carries-a-denominator-and-three-consecutive-satisfiability-analyses-have-checked-the-value-a-clause-can-reach-without-checking-the-population-it-is-computed-over):
a satisfiability analysis that verifies a clause's *values* while never verifying that its *population is
addressable* has checked the easier half.

Reproduce: `sed -n '100p;104p' /mnt/d/hf_w20/detected_probe_w20.sh`; `sed -n '281p;300p;301,305p'
/mnt/d/hf_w20/score_d20_w20.py`; `sed -n '207p' /mnt/d/hf_w20/gate_w20.sh`;
`docs/synthetic-af-bank-followups-wave20-results.md` §3 and §4.

> ### FIXED BY CONSTRUCTION 2026-08-11 (wave 21) — the manifest carries the paths, and the scorer rebuilds none
>
> | half | what shipped | the check that reaches it |
> |---|---|---|
> | **strong — by construction** | `b21_arm_w21.sh` **asserts** each artifact exists, then **writes its path** into `b21_manifest.tsv` (`dataset  factor  log  landing`); `B21_ARM_READY` carries `manifest=<path>`; `score_b21_w21.py` opens the marker, reads the manifest path **out of it**, and reads every log and landing **out of the manifest's rows**. **There is no `os.walk` fallback, deliberately** — wave 20's was rooted at the same wrong parent as its primary path, and *a fallback rooted at the same wrong parent as the primary path is not a second chance* | `--self-test` branch **7**: a deliberately **FLAT** layout scores, because the path came from the manifest and not from a template. Branch **8**: an unresolvable manifest path is `COULD-NOT-LOOK` **and the manifest's own string is quoted**. Branch **10**: a marker carrying no `manifest=` is `COULD-NOT-LOOK`, not an existence check |
> | **weak — by convention** | one `layout_w21.sh` declares every layout the wave uses, and both drivers source it | both drivers' self-tests |
>
> **It was exercised, not merely compiled.** RULE B21's arm wrote 6 rows and the scorer resolved **6 of 6** logs
> and **6 of 6** landings on the first attempt, returning `B-DEMONSTRATED`. Wave 20's fix would otherwise have
> shipped with no arm behind it — which is [F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two)'s
> complaint about instruments never shown to return their own PASS.
>
> **Reinforcement 1 — an instance inside the pre-registration written to prevent instances.** Building
> `prov_w21.py`'s known-good direction needed a `copytree` of wave 20's gate with every `BuildId` rewritten. The
> first attempt **rewrote 6 of 8 landings and reported success**, because the glob
> `*/attempt01/optimized_settings.json` is wrong for the two real-bank runs, which nest one level deeper
> (`CWhiteFocus/AutoFocus_20220429_000244_attempt01/`). It was caught **only** by asserting the count. That is why
> `layout_w21.sh`'s landing helper is a `find` and not a `printf`. *A path template got it wrong again, inside the
> document written to fix path templates.*
>
> **Reinforcement 2 — and this is the first time the family was stopped instead of discovered.** Wave 21's gate
> aborted on its **first** launch, exit 3, because the chain wrote the class-3 fingerprint as
> `prior_arm_fingerprint_BEFORE.json` while `gate_w21.sh`'s `$FP_W18` reads `w18_arm_fingerprint_BEFORE.json`.
> The two files are **byte-identical in content**; only the names disagreed. The driver named the four files it
> wanted and refused to run an arm whose controls did not exist. **Cost: 40 seconds**, against the verdict wave
> 20 lost to the same class of defect. *A reader that refuses beats a reader that guesses, and both beat a reader
> that falls back to the same wrong parent.*
>
> Reproduce: `cat /mnt/d/hf_w21/B21_ARM_READY /mnt/d/hf_w21/b21_manifest.tsv`;
> `python3 /mnt/d/hf_w21/score_b21_w21.py --self-test` (branches 7–10);
> `cat /mnt/d/hf_w21/chain_w21.log`; `sed -n '137,140p;178,186p' /mnt/d/hf_w21/gate_w21.sh`;
> `docs/synthetic-af-bank-followups-wave21-results.md` §6 and §8.3.

### F73 — The gate has certified nine binaries by checking ONE of a landing's 35 fields, and the other 33 had never been looked at — they are identical, 660 of 660

**Status:** **MEASURED 2026-08-11 (wave 19, RULE R19 = `R-DETERMINISTIC`)** · the error term every cross-wave
landing comparison in this series carries is now measured, and it is **exactly zero** · **~53 m, already paid;
do not re-run it** ([F53](#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it)(c))

For nine waves the eight-value gate established that a new binary reproduces the coordinate system by checking
**one field** — `BestJ`, the landing's `FinalJ` — on eight runs. A landing carries **35 top-level keys**: 25
curated detector knobs, `RecommendedStepSize`, `RecommendedOffsetSteps`, `CreatedAtUtc`, `BaselineJ`, `FinalJ`
and a `Provenance` block. **Nobody had ever checked the other 33.**

The gap was not hypothetical. `J` is saturated near 1.0 on this bank
([F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)) and the
search crosses a flat valley ([F6](#f6--sensitivity-and-star-clip-act-only-in-combination),
[F8](#f8--optimizer-landings-are-not-reproducible-across-invocations)), so **two different knob vectors with the
same `J` to sixteen digits is exactly what a flat valley produces.**

### The measurement

Wave 18's 20-dataset arm A0 was re-run on a tenth binary, `--settings`- and `--profile-id`-pinned, one
`TestApp.exe`, sequential, `--max-evals 250`, and the **full 33-key landing vector** was diffed pairwise.

| clause | what it measures | result |
|---|---|---|
| **R19-A** | the whole landing, key by key | **20 of 20 datasets with zero differing keys; 660 of 660 key comparisons identical** |
| **R19-B** | the objective alone — what nine waves of gates have actually been checking | `FinalJ` **20 of 20**; `BaselineJ` **20 of 20** |
| **R19-C** | invocation alone (same binary, different `--out`) | **3 of 3** |

> **`R19-A` minus `R19-B` is zero. Nothing in the landing moves that `J` does not.**

The only differing members were the three excluded **in advance**: `CreatedAtUtc`, `Provenance.BuildId` (which
MUST differ) and `Provenance.CommandLine` (which MUST differ because `--out` differs). No exclusion was invented
after the data, and an independently-written differ sharing no code with the wave's scorer reproduced 660 of 660.

### What it licenses, and the boundary that matters

Cross-wave landing comparisons in this series treat a **code difference** as the treatment and everything else
as **error**. This measures that error term directly and finds it zero, so
[F63](#f63--the-optimizers-landing-moves-on-6-of-8-runs-under-a-knob-that-is-nearly-inert-at-the-seed-so-every-landing-waves-5-12-published-was-produced-at-a-non-default-value)'s
6-of-8, wave 15's 19-of-20 and wave 18's `N18-M` are retro-validated **against noise**: whatever moved in them
was not the harness moving under its own feet. Wave 18's 40 arm landings are a **measurement**, not a sample.

**And the population is narrower than the rule's own prose.** The wave intended the two roots to differ by
shipped code (its item D), and **item D never landed** — `git diff --name-only` between the two build trees over
`*.cs` / `*.csproj` / `*.props` / `*.targets` is **empty**. `BuildId` still separated them, because by its own
documented semantics (`OptimizedStarDetectionSettings.cs:386`) it is the assembly **MVID**, *"which the compiler
regenerates on every build even when the source is byte-identical."* **A differing `BuildId` proves a rebuild
happened; it has never proved the code differs.**

So what was measured is: two independent Release builds of the **same C# source**, two invocations, two output
directories, ~10 hours apart, 20 synthetic datasets, `--max-evals 250`, `ConcurrencyCheck = exclusive`,
`DetectorVersion` 2 on both roots. **Zero differences.** It is therefore:

- **NOT** evidence that a **code** change cannot move a landing while `J` stays fixed — nothing measures that,
  and the `R-J-ONLY` branch could only have fired here from run-to-run nondeterminism;
- **NOT** a claim under concurrency ([F55](#f55--optimize-is-not-reproducible-when-several-instances-run-at-once-and-the-seed-evaluation-is-what-moves)), across detector changes, on the real bank, or at other `--max-evals`;
- and `D01…D17` — 17 of the 20 — still have **no cross-code `J` check** at all, only this cross-build one.

**The accident improved the instrument.** For measuring an *error term* you want the treatment **absent**: as
executed this is a clean **null arm**, which is the correct control for "does a cross-wave comparison carry
noise?", and the binary/invocation confound the design worried about collapsed on its own. *Better control than
designed, weaker claim than written* — and the distinction is only visible because the tree diff was checked.

### Why it matters

- **A control on one field is a control on one field.** The gate's `BestJ` clause is not wrong; it was simply
  never established to stand in for the landing. Now it is, on this population, and the substitution can be
  cited instead of assumed.
- **The reachable-but-never-reached branch is the valuable one.** `R-J-ONLY` was reachable by construction and
  would have cost the register a great deal — every cross-wave landing comparison conditional, wave 18's 40
  landings demoted to a sample. **Fixing that consequence before the data is what made the null informative**;
  a null with no pre-registered alternative is just a shrug.
- **Determinism is a property of a pinned pipeline, not of the optimizer.** Every clause that made this hold —
  `--settings`, `--profile-id`, one `TestApp.exe`, exclusive concurrency — was *verified*, not assumed. Drop any
  of them and the measurement does not transfer.

### Next step
The honest missing arm is the **code** axis: re-run the same 20 datasets on a binary that differs by a change
believed inert on the search, and diff all 33 keys. It is the arm wave 19 thought it was running. Until then,
cite this entry for the **noise** floor only.

> **THE ~53 m PRICE IS WRONG, AND THE SHAPE WAS TOO (2026-08-12, wave 23).**
>
> **~53 m buys one 20-dataset pass, and it silently assumes wave 19's `/mnt/d/hf_w19/reA0` can serve as the
> "old" side. It cannot:** `reA0` was built from wave-19-era source (`e5be699`), so the treatment would be
> *"everything shipped between wave 19 and now"* —
>
> ```
> git diff --name-only e5be699..2623771 -- '*.cs'   ->  13 files  (7 outside the test project)
> git diff --name-only 2623771..3d370ff -- '*.cs'   ->  14 files  (8 outside the test project)
> ```
>
> — across three waves plus F76's eight rounds, F77 and the replay-report work. **That is a confounded arm, not
> an inert-change arm.** The honest form needs **two binaries built from one tree and two 20-dataset passes:
> 2 x 52 m 43 s ≈ 1 h 45 m, plus builds and provenance ≈ 1 h 50 m, plus a ~42 m gate.** Wave 19's own measured
> arm time (`04:32:00Z → 05:24:43Z` = 52 m 43 s) is the rate.
>
> **And the change must be a REAL SHIPPED DELTA, not an authored inert one.** An authored change — an unused
> method, a `Logger` line off the search path — reproduces this wave's own defect one level up: it is a code
> difference that **cannot execute**, so the expensive `R-J-ONLY` branch is again reachable only from noise, and
> R19 measured that noise at exactly **zero**. It would look like it answers this entry and would not. Only a
> real shipped delta **whose changed code executes inside the search loop** makes the branch reachable by
> construction.
>
> **A free 8-run x 1-field slice of it is already paid.** Wave 23's gate ran on a binary carrying PR #194's
> `StarDetector.ComputeEarlyCacheKey(p)` **SHA256 computed for every candidate inside the search loop**, and
> reproduced K8 **bit-identically at all sixteen digits on 8 of 8 runs** — the first measurement of the claim the
> source states at `RunEvaluationData.cs:919-925`. **That is 8 runs x 1 field; this entry's arm is 20 datasets x
> 33 fields.** It is a slice and must not be reported as the arm.
> Reproduce: `docs/synthetic-af-bank-followups-wave23-design.md` §1.2;
> `docs/synthetic-af-bank-followups-wave23-results.md` §1.2 and §9.3.
Reproduce: `python3 /mnt/d/hf_w19/score_r19_w19.py --new /mnt/d/hf_w19/reA0 --old /mnt/d/hf_w18/seedA0 --gate /mnt/d/hf_w19/gate --out -`;
`docs/synthetic-af-bank-followups-wave19-results.md` §2.

### F71 — A converted settings file carries every detector knob TWICE, and the control that checked the conversion read the copy the loader ignores

> **CORRECTED 2026-08-11 (wave 20): the repaired control IS demonstrated, in both directions.** Wave 19's
> results doc said `C19-D2` was never run and that ~1 minute was still owed. It had already been paid:
> `/mnt/d/hf_w19/C19_PROBE_PASSED` was written at **05:43:28Z**, 33 seconds before the results commit, and reads
> `C-DEMONSTRATED`. Comparing the **snapshot** rather than the dead base copy, the conversion **took** on `good`
> and **did not take** on `mutant`, corroborated on **8 fields**, in both the retro and the live half.
> See [`wave19-results`](synthetic-af-bank-followups-wave19-results.md) §3.5 for why the document was wrong at
> the moment of its own commit.
**Status:** Open · found 2026-08-11 (wave 18) when a control returned a demonstrated PASS and a demonstrated FAIL
**on the wrong inputs** · **it cost RULE N18 its verdict, and the evidence that it was wrong was in the same file
the control read**

`convert_landing_w15.py` turns an optimizer landing into a harness settings file through the production Accept
path. The output has the base file's `Options` block round-tripped **verbatim**, plus the landing written into
`Options["OptimizedSettingsJson"]` with `Options["UseOptimizedSettings"] = "True"`. So **every curated detector
knob appears twice in one file**, and on load in Simple mode the `Options` copy is **dead**:
`ConfigureSimpleSettings` takes the `UseOptimizedSettings && HasOptimizedSettings` branch and
`ApplyOptimizedSnapshotToLiveProperties` overwrites it.

Wave 18's `N18-V7` — *"the `af-fit/detector` dump carries the converted file's `NoiseReductionRadius`"* — and its
F66(a) probe both compared the detector against **`Options`**, the dead copy. On wave 18's probe:

```
good    settings NoiseReductionRadius=4   af-fit/detector NoiseReductionRadius=3   -> DID-NOT-TAKE
mutant  settings NoiseReductionRadius=4   af-fit/detector NoiseReductionRadius=4   -> TOOK
```

**Both verdicts are exactly inverted.** `good` (`UseOptimizedSettings=True`) genuinely took — the snapshot bound
and the detector ran at the landing's **3**. `mutant` (the same file with the flag flipped to `False`, asserted
by read-back) genuinely did not — the snapshot was ignored, `DerivePresetSettings` ran, and the detector came out
at **4**.

### The two copies disagree BECAUSE of F70, which is the defect the wave existed to fix

| | `Options.NoiseReductionRadius` | `OptimizedSettingsJson` → `NoiseReductionRadius` |
|---|---|---|
| the converted file | **4** — the base's stored value, which is `DerivePresetSettings`' `Typical ⇒ 3` plus the `+1` hotpixel compensation, persisted | **3** — `BuildDefaultStarDetectorParams`'s pre-compensation literal, followed by the search on 18 of 20 datasets |

And the `mutant` side is the same defect a second time: with the snapshot ignored, the derivation re-runs and
lands **back on 4**, so the dead stored copy agreed with the live value by arithmetic accident. **The control was
not merely reading the wrong field — it was reading a field that is inert in Simple mode and that coincided with
the live one only because of the `+1`.**

### The verification took no compute, from the files the control had already read

The same two `run.log` files carry **55 detector fields** each, of which **8 differ** between `good` and
`mutant`: `HotpixelThreshold`, `MaxDistortion`, `MinHFR`, `NoiseReductionRadius`, `Sensitivity`,
`StarCenterTolerance`, `StarClippingMultiplier`, `StructureLayers`. **All eight of `good`'s values are the
snapshot's; all eight of `mutant`'s are the base's.** Seven fields would have contradicted the verdict for free.

> **Correction, wave 19:** the dump carries **55** fields, not the 56 this entry and wave 18's results §5.3 both
> originally stated. Counted directly on both saved logs: 55 lines, 55 unique names, no duplicates, on `good` and
> on `mutant` alike. Nothing downstream moves — \|D\| is still 8 — and it is corrected rather than repeated.

> **The corrected control is built and half-demonstrated (wave 19, RULE C19 = `C-UNEVALUATED`).**
> `score_c19_w19.py` reads `Options["OptimizedSettingsJson"]` and **refuses to fall back** to `Options[<knob>]`
> (F71(a)); it requires corroboration on **\|D\| ≥ 3** independent fields rather than nominating a signature knob
> (F71(b)); and `convert_landing_w19.py` prints every curated knob's `(Options, snapshot)` pair in three named
> states — `BOTH-DIFFER`, `SNAP-ONLY`, `BOTH-AGREE` (F71(c)).
>
> **Demonstrated:** the retro direction on wave 18's own saved probe — **\|D\| = 8, good agrees with the snapshot
> on 8 of 8, mutant on 0 of 8** — reconciling exactly with the converter's independent table (5 `BOTH-DIFFER` +
> 3 `SNAP-ONLY` whose code default differs from the snapshot). Self-tests: scorer **9 of 9** (inverted pair,
> \|D\| = 0, \|D\| = 1, missing dump, missing snapshot, mismatched snapshots, unaliased field named), converter
> **11 of 11**.
>
> ~~**Still owed: the live direction, `C19-D2`, ~1 minute.** Wave 19 never ran `probe_w19.sh`, so the scorer's
> own interlock refused to write `C19_PROBE_PASSED` and downgraded to `C-UNEVALUATED`.~~ **FALSE — corrected
> 2026-08-11 (wave 20); see the block at the top of this entry.** `probe_w19.sh` *was* run, `C19_PROBE_PASSED`
> exists (572 bytes, `C-DEMONSTRATED`, `05:43:28Z`), and nothing is owed. *Struck rather than deleted, and
> struck at the sentence rather than at the entry's head, because
> [F69](#f69--f39bs-flag-names-and-its-own-comment-state-the-opposite-of-its-default-and-that-cost-a-pre-registered-rule-its-verdict)(a)
> is the standing demonstration of what a correction filed only at the top of an entry leaves behind.*
> *Why the bar is \|D\| ≥ 3 and
> not "one field matched": a two-directional demonstration proves the branches are DISTINGUISHABLE, not that they
> are CORRECTLY ASSIGNED — both directions are scored by the same definition, so a definition error moving both
> verdicts the same way reads as a clean PASS. Wave 18's was caught only because the labels came out swapped.*

### Why it matters

- **It cost a pre-registered rule its decisive clause.** The interlock refused to write `PROBE_PASSED`, both
  arms' `af-fit` passes aborted, `N18-V7` returned COULD-NOT-LOOK on 20 of 20 in both arms, `N18-P` was
  UNEVALUATED on an empty set, and **RULE N18 returned `N-UNEVALUATED`** on 1 h 55 m of completed arms. The
  arms are intact on disk; the ~8 m that would have consumed them was never spent.
- **Uncaught, it would have produced a validity gate correlated with the treatment.** `N18-V7` as coded reduces
  to *"did this dataset's landing radius happen to equal 4?"*. From the landings: **0 of 20 on the control arm
  and 15 of 20 on the treatment arm.** Neither reaches the 1.000 bar, so the verdict would have been the same —
  but the printed numbers read as *"the conversion bound on the treatment arm and not on the control arm"*, a
  conclusion about the arms drawn entirely from a defect in the instrument, pointing the way the wave wanted to
  go.
- **It is [F68](#f68--a-threshold-stated-as-a-count-carries-a-denominator-and-three-consecutive-satisfiability-analyses-have-checked-the-value-a-clause-can-reach-without-checking-the-population-it-is-computed-over)'s
  shape in a dimension F68 does not cover.** The clause's **P / S / A / E** columns are all filled in correctly:
  population (the `af-fit` dumps against the converted files), statistic (field equality), aggregation (rate per
  arm), empty answer (COULD-NOT-LOOK, named, out of both denominators). A denominator error is a *population*
  that is not what you think it is; **this is a *quantity* that is not what you think it is**, and no check the
  register currently prescribes can see it.
- **And it is [F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two)(a)
  honoured to the letter and still defeated.** Two inputs differing in one asserted-by-read-back byte, two runs,
  a demonstrated TOOK and a demonstrated DID-NOT-TAKE — and they came out on the wrong sides. *A two-directional
  demonstration proves the branches are DISTINGUISHABLE, not that they are CORRECTLY ASSIGNED*, because both
  directions are evaluated by the same definition. It was caught only because the labels were **swapped**; a
  definition error moving both verdicts the same way would have read as a clean PASS.

### Next step
(a) **Read the SNAPSHOT, not `Options`, in any check that asks whether a converted landing bound** — and say so
in the clause's own text, not only in the code. **~10 m** in `score_n18_w18.py` (`score_probe` and the `N18-V7`
block, both of which read `want.get("NoiseReductionRadius")` from `Options`). **Do not fold this into a repair of
RULE N18**: N18 is `N-UNEVALUATED` and is not re-decided after the data. It belongs to whatever wave next
pre-registers an out-of-sample clause on converted landings.
(b) **Corroborate any one-field control with a second field that must move the same way.** Wave 18's probe had
seven such fields sitting in the same dump at zero cost. **~5 m**, and it is the half that generalises past this
one clause.
(c) **Make `convert_landing_w15.py` say what it is doing** — its `--self-test` already asserts *"leaves
`UseAdvanced=False` (or the snapshot is ignored)"*, so it knows the snapshot is the live copy. Have the converter
print, for each moved knob, the pair `(Options value, snapshot value)` it is creating, so the two-copy structure
is visible to the next reader rather than latent. **~10 m.**
(d) **The 40 wave-18 arm landings are on disk and must not be re-run**
([F53](#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it)(c)):
`/mnt/d/hf_w18/seedA0` and `/mnt/d/hf_w18/seedA1`, 20 each, provenance-clean, two distinct `BuildId`s. A repaired
out-of-sample clause can score them for ~8 m of `af-fit`.
Reproduce: `python3 /mnt/d/hf_w18/score_n18_w18.py --probe /mnt/d/hf_w18/probe --probe-result
/mnt/d/hf_w18/gate/D18_m24_deep_shed/attempt01/optimize_result.csv`; the two copies are
`python3 -c "import json; d=json.load(open('/mnt/d/hf_w18/probe/good.json'))['Options']; print(d['NoiseReductionRadius'],
json.loads(d['OptimizedSettingsJson'])['NoiseReductionRadius'])"` → `4 3`;
`docs/synthetic-af-bank-followups-wave18-results.md` §5.

### F70 — `NoiseReductionRadius` has TWO shipped defaults, and the drift-guard test asserts the one path where they agree
**Status:** Open · found 2026-08-10 (wave 17) by reconciling two clauses of RULE D17 that appeared to
contradict each other · **(b) DONE in wave 18 (`93e366a`); (a) DECIDED IN SOURCE AND UNSHIPPED — see the wave-18
block below** · **~0 to measure (it is printed in every `optimize` log), and it is a PRODUCT observation, not a
harness one**

RULE D17 diffed the pinned settings file's detector bundle against the shipped code defaults over the 50 fields
`ApplyAfContext` does not set, on 8 wave-17 gate logs and 10 wave-16 logs. **Exactly one field differs, every
time:**

```
NoiseReductionRadius    optimize/seed (shipped default) = 3        optimize/baseline (loaded options) = 4
```

The same rule's `D17-4` reports the `UseAdvanced=False` warning saying the Simple-mode presets override **0**
recorded knobs. Both are correct, and reconciling them is where the finding is.

| where | value | why |
|---|---|---|
| `HocusFocusStarDetection.BuildDefaultStarDetectorParams()` — **the shipped Optimization Wizard's seed** (`:418`, used by `GetDefaultStarDetectorParams` at `:542`) and the harness's `optimize/seed` | **3** | a hardcoded literal |
| `StarDetectionOptions.ResetDefaultsImpl()` (`:348`) | **3** | the literal, assigned **after** the `Simple_*` properties, so it is the object's final state on that path |
| **any `StarDetectionOptions` constructed from an accessor** in Simple mode — the harness's, and the app's | **4** | `InitializeOptions()` → `ConfigureSimpleSettings()` → `DerivePresetSettings()`: `Typical` ⇒ 3, then `StarDetectionOptions.cs:221-224` adds **+1** whenever `HotpixelThresholdingEnabled && HotpixelFiltering` — *"Without thresholding, hotpixel filtering does a blur. To compensate, we increase the noise reduction radius"* — and **both are on by default** |

> **`ResetDefaults()` leaves the object in a state that constructing it never produces.** The drift guard,
> `StarDetectionOptionsTests.BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild`, does exactly
> `options.ResetDefaults(); BuildStarDetectorParams(options)` and asserts `NoiseReductionRadius` against
> `BuildDefaultStarDetectorParams()`. **It is green** (3824 of 3824 pass) — on the one path where the `+1` never
> fires. The path every real load takes disagrees by one, and no test looks at it.

**Why it matters.** `NoiseReductionRadius` is **live**, not cosmetic: it is in `StarDetector`'s early-key list
(`:153`), so a candidate that moves it forces a full re-detect, and it sets a Gaussian kernel of
`NoiseReductionRadius * 2 + 1` — **7 px against 9 px**. So the optimizer's seed — in the harness **and in the
shipped wizard** — starts its search from a smoothing radius that no Simple-mode options object with hotpixel
thresholding enabled ever holds. The wizard's own docstring calls it *"the fully-default detector params … so
the search starts from a clean, reproducible point"*, and it is clean and reproducible; it is just not the point
the user is at.

> **CORRECTED 2026-08-10 (wave 18), and the finding is STRONGER than this entry first stated.** The line above
> originally cited `StarDetector.cs:535` and called it *the measurement-image smoothing kernel*. **That call is
> gated by `StarMeasurementNoiseReductionEnabled`, which is `false` on every default path** —
> `ResetDefaultsImpl:347`, `BuildDefaultStarDetectorParams:421`, and the `Typical` preset at `:184` — so at
> defaults it never runs. The call that **does** run is **`:556`**, on `noiseReducedImage`, which is copied
> straight into `structureMap` at `:558`. The 7-vs-9 px difference is real and it acts on **the image that
> decides WHICH STARS ARE DETECTED**, not the one that measures their HFR. It also moves the
> `KappaSigmaNoiseEstimate` at `:562` that sets the binarization floor.
>
> **The user manual corroborates the correction one line above the line that carries the bug.**
> `documentation/docs/settings/preprocessing.md:22` describes `StarMeasurementNoiseReductionEnabled` as *"Also
> blur the **measurement** image, not just the structure-detection image"* — i.e. by default only the
> structure-detection image is blurred. Line 23 of the same table then gives this field's default as **3**.

> ### WAVE 18: (b) is DONE and shipped; (a) is DECIDED IN SOURCE, MEASURED ON TWO ARMS, AND NOT SHIPPED
>
> **Two further facts about the product, both measured at wave-18 pre-registration time.**
>
> **1. `ResetDefaults()` was not merely inconsistent with construction — it was NON-DETERMINISTIC.**
> `ResetDefaultsImpl`'s last statement is `UseOptimizedSettings = false` (`:394`), and that property is a member
> of `SimplePropertyNames`, so its setter's `RaisePropertyChanged()` re-entered
> `StarDetectionOptions_PropertyChanged` → `ConfigureSimpleSettings()` → `DerivePresetSettings()` → **4**. But
> every setter in the class is change-guarded, so the re-entry happened **only when the flag was already on**.
> *The same button produced two different detectors, decided by a checkbox the button itself clears.* Measured
> over the 9 `*.profile` files in `%LOCALAPPDATA%\NINA\Profiles`: **`ResetDefaults()` lands on 4 on 5 of 9 and on
> 3 on 4 of 9** — and the drift guard's own fixture (virgin `InMemoryPluginOptionsAccessor`,
> `UseOptimizedSettings` absent) took the **minority** branch, on a path no profile load takes. It was green
> twice over.
>
> **2. The `+1` does not compound.** `DerivePresetSettings`' switch assigns `NoiseReductionRadius` absolutely
> (`:175/180/185/190`) before the increment and the enum is covered exhaustively, so the method is idempotent at
> 4 however many times it re-enters. That hypothesis is ruled out, not left open.
>
> **(b) IS DONE — `93e366a`, wave 18 item A Part 1.** `ResetDefaultsImpl` now ends in an **unconditional**
> `ConfigureSimpleSettings()`, so reset-state == constructed-state **structurally**, for every property, from
> every entry state. The one-test drift guard is replaced by three: **T1** `BuildDefaultStarDetectorParams()`
> against `BuildStarDetectorParams(freshly constructed options)` over every option-derived field — *the assertion
> nothing in the repo previously made*; **T2** `ResetDefaults()` from four named entry states; **T3** the
> accessor-fallback / preset-base lockstep guard. **7 new tests, suite 3831.** Five fail against the pre-change
> product source, verified by reverting the one product line. *The re-entry was the bug and the value was only
> its symptom* — the cheaper fix of changing the accessor fall-back literal 3 → 4 was rejected in writing,
> because it makes the two branches coincide by arithmetic luck and re-arms for the next post-adjusted field.
>
> **(a) IS DECIDED AND UNSHIPPED. The answer is that 4 is the shipped default and 3 is the preset's
> pre-compensation base** — settled by counting, at zero compute: `ResetDefaultsImpl` and `DerivePresetSettings`
> share **20** properties and agree literal-for-literal on **19**, and the twentieth is the only one the
> derivation post-adjusts. Reinforced by three more: `BuildDefaultStarDetectorParams` sets `HotpixelFiltering`
> and `HotpixelThresholdingEnabled` **true** — exactly the configuration the `+1` exists for — and does not carry
> the compensation; every construction yields 4 and persists it; and `ResetDefaults()`'s 3 does not survive a
> restart, because the next construction re-derives 4 and writes it. *A value the next launch overwrites is not a
> default.*
>
> **It did not ship, because RULE N18 returned `N-UNEVALUATED`** — its conversion control was mis-specified and
> the wave's only out-of-sample clause never ran
> ([F71](#f71--a-converted-settings-file-carries-every-detector-knob-twice-and-the-control-that-checked-the-conversion-read-the-copy-the-loader-ignores)).
> The seed literal stays at 3, the manual line stays at 3, and the drift guard carries `NoiseReductionRadius` as
> **one named exception asserted in both directions** (3 on the seed bundle, 4 on the constructed one).
>
> **What the two 20-dataset arms measured before the rule stalled** — reported as diagnostics under an
> UNEVALUATED verdict, licensing nothing:
>
> | | A0, seed 3 | A1, seed 4 |
> |---|---|---|
> | landing `NoiseReductionRadius` == its arm's seed | **18 of 20 = 0.900** | **15 of 20 = 0.750** |
> | landings that moved on any curated knob | — | **20 of 20** |
> | `FinalJ` better | **7** | **13** (tie band 1e-9, 0 ties, n = 20; min −0.000581, median +0.000086, max +0.002993) |
> | out-of-sample `e` against the generator's truth | **NEVER MEASURED** | **NEVER MEASURED** |
>
> The two arms' `BaselineJ` is **bit-identical on 20 of 20 at seventeen digits**, so `J` is demonstrably one
> function evaluated at two landings — the clause that *licenses* the cross-arm comparison rather than assuming
> it. **The comparison it licensed is the one the wave could not consume.** *The anchoring is weaker at the
> treatment seed than at the status quo, and A0's two exceptions are not A1's, so the seed is not a constant
> offset on the landing.*
>
> **Reach, unchanged and computed at zero cost:** the shipped fix would change the **effective** radius on
> **0 of 9** profiles on this machine, because it edits `BuildDefaultStarDetectorParams` only and no profile's
> loaded detector reads it. What it changes is **where the wizard's search starts** — and the landing follows the
> seed on 18 of 20 synthetic datasets, so it is in effect the value the wizard recommends on ~90 % of this bank.

**Two things this does NOT say.** It does not say either value is wrong — the `+1` compensation is deliberate
and reasoned. And it does not say any prior measurement is invalid: every arm in this series pinned the same
settings file, so the baseline side has been a uniform 4 throughout and the seed side a uniform 3.

### Next step
(a) **DECIDED, NOT SHIPPED (wave 18).** The answer is **4**, established by counting in source; the one-literal
change to `HocusFocusStarDetection.cs:433` plus the manual line and the drift guard's exception is written and
was applied to a second binary, and it was **reverted** when RULE N18 could issue no verdict. **Whatever wave
takes it up needs a fresh pre-registration and an out-of-sample clause that reads the SNAPSHOT**
([F71](#f71--a-converted-settings-file-carries-every-detector-knob-twice-and-the-control-that-checked-the-conversion-read-the-copy-the-loader-ignores)(a)).
The expensive part is already paid for: **wave 18's two 20-dataset arms are on disk**
(`/mnt/d/hf_w18/seedA0`, `/mnt/d/hf_w18/seedA1`, two distinct `BuildId`s, provenance-clean) and must **not** be
re-run ([F53](#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it)(c)).
Scoring them costs **~8 m of `af-fit`**, not the 1 h 55 m the arms cost. **And note the debt that was NOT
created:** because Part 2 does not ship, the eight-value K8 coordinate system stays valid and no wave owes a
42 m re-baseline.
(a′) **The alternative that was rejected in writing, so it is not re-proposed as new.** *Move the `+1` out of
`DerivePresetSettings` into a shared build path* — rejected: an Advanced-mode user who explicitly sets 3 would
silently detect at 4, and `BuildDefaultStarDetectorParams` builds the *params* bundle, so it would have to carry
the compensation anyway; the seed moves either way and an extra class of user is surprised. *Remove the `+1`* —
rejected: it changes the effective detector for every Simple-mode user on every detection, everywhere in the
product, and deletes a deliberate, reasoned compensation on no evidence.
(b) **DONE (wave 18, `93e366a`).** The guard now asserts `BuildDefaultStarDetectorParams()` against a
**constructed** options object as well as a reset one, plus `ResetDefaults()` from four named entry states and an
accessor-fallback lockstep guard — 7 tests where there was 1. *A guard that only checks the path where two things
agree is a check that cannot fail*
([F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two)(a)).
(b′) **Residual hazard, flagged rather than silently left.** The 20 preset-owned literals in `ResetDefaultsImpl`
are now unobservable through any public path, so editing one is a silent no-op. Kept for a minimal diff,
documented in code. **~20 m** to delete them in favour of the derivation, or to pin them with a reflective test.
(c) **Do not re-pin the settings file for it.** See
[F63](#f63--the-optimizers-landing-moves-on-6-of-8-runs-under-a-knob-that-is-nearly-inert-at-the-seed-so-every-landing-waves-5-12-published-was-produced-at-a-non-default-value)(b):
the offset is published instead, and the eight-value coordinate system is not moved.
Reproduce: `python3 /mnt/d/hf_w17/score_d17_w17.py --gate /mnt/d/hf_w17/gate --w16 /mnt/d/hf_w16 --affit
/mnt/d/hf_w16/affit_N --out /mnt/d/hf_w17/d17_score.txt`; the raw evidence is
`grep -o "NoiseReductionRadius=[0-9]*" /mnt/d/hf_w17/gate/toml999.log` (baseline first, seed second);
`docs/synthetic-af-bank-followups-wave17-results.md` §4.4.

> ### Wave 19 (2026-08-11): (a) escalated to the owner as not decidable by measurement; (b′) and the manual line pre-registered to ship and DID NOT SHIP
>
> **(a) is a decision to be taken on the source argument, by the owner.** Every class of evidence that could
> decide the seed literal has been enumerated and each one is either complete-but-not-a-measurement, spent, or
> structurally unable to decide:
>
> | evidence class | state |
> |---|---|
> | source / counting argument | **COMPLETE and free** — the answer is 4. **Not a measurement**, so it cannot satisfy a measurement rule |
> | in-sample objective (`FinalJ`) | **SPENT** — published at A1-better 13, A0-better 7, 0 ties |
> | anchoring rate (`N18-A`) | **SPENT** — published at 0.900 / 0.750 |
> | out-of-sample `e` | **UNMEASURED and deliberately preserved.** Structurally weak even unspent: the largest landing-driven out-of-sample movement ever measured on this bank is **0.02584 step**, the largest of any kind **0.00993**, against a **0.10-step** materiality floor. **`e` can be a VETO; a veto is not a decider**, and a rule whose only measurement clause is a foreclosed veto cannot fail to ship |
> | reach on real profiles | **0 of 9, by construction.** Argues neither way |
> | the other 11 `BuildDefaultStarDetectorParams` call sites | unmeasured, ~30 m each, buys no decision |
>
> **The only clause with a live range was in-sample `FinalJ`, and it is spent.** The precedent exists: wave 14's
> `MaxOutlierRejections` default was set to 0 by the owner, overriding wave 13's pre-registration, and the
> register recorded it as such. Cost: one literal, plus [F69](#f69--f39bs-flag-names-and-its-own-comment-state-the-opposite-of-its-default-and-that-cost-a-pre-registered-rule-its-verdict)(b)'s
> dump so the next wave can see which value actually ran. *A diagnostic published under an UNEVALUATED verdict is
> not free — it SPENDS the population it reads, which is why RULE F14 is permanently NO VERDICT and why wave 19
> shipped no driver that could look.*
>
> **(b′) did not ship**, so the residual hazard stands exactly as wave 18 left it. When it is done, the shared set
> must be **derived from source** (the properties `DerivePresetSettings` assigns) and not guessed, one
> parametrized test must perturb each property and assert the derived value after `ResetDefaults()`, and the test
> must be demonstrated red against the named mutant **M-R1** — *remove the unconditional
> `ConfigureSimpleSettings()` at the end of `ResetDefaultsImpl`*. **Any literal the test cannot cover stays.**
>
> **The manual is still wrong, and this is now two waves old.**
> `documentation/docs/settings/preprocessing.md` gives `NoiseReductionRadius`'s default as **3** in the
> settings-at-a-glance table **and again** in the prose (`**Default:** \`3\``, eighteen lines further down, in the
> section a reader actually reads). **It is 4**, measured on 28 wave-19 logs: the `optimize/baseline` block reads
> `NoiseReductionRadius=4` on 8 of 8 gate logs and 20 of 20 arm logs. It is wrong under *both* answers to (a) —
> the page documents the **option**, whose derivation gives Typical's 3 plus the `+1` hotpixel compensation, and
> **since `93e366a` nothing in the product produces 3 for it**, because Part 1 made `ResetDefaults()` derive too.
> Wave 18 made the fix conditional on RULE N18, N18 returned `N-UNEVALUATED`, and the revert left a line the same
> wave's Part 1 had just made unambiguously wrong; wave 19 pre-registered it unconditionally and did not make it.
> *Two quantities sharing a name —
> [F71](#f71--a-converted-settings-file-carries-every-detector-knob-twice-and-the-control-that-checked-the-conversion-read-the-copy-the-loader-ignores)'s
> own defect, in the manual instead of a settings file.* **Both lines must change; wave 18's saved
> `part2_w18.patch` touches only the table row and is not a template for the documentation half.**

> ### WAVE 20 (2026-08-11): **the manual is FIXED, at BOTH sites** — `2678ecd`, committed before the build
>
> `documentation/docs/settings/preprocessing.md:23` (the settings-at-a-glance row) **and** `:41` (the prose
> `**Default:**`) both now read **4**, and the prose carries a new sentence saying **the default is *derived, not
> a literal***: the Simple-mode preset sets a base of `3` and adds `1` when hot-pixel filtering is on, which it
> is by default, so every settings object a user can construct starts at 4 — and the `3` a reader will find by
> grepping the source is the pre-compensation base, not what the detector receives. **Wave 18's
> `part2_w18.patch` was explicitly not used as a template**, because it fixes the row and leaves the prose: *a
> half-corrected manual is worse than an uncorrected one, because the two halves then disagree with each other.*
>
> **Arm-level corroboration on an eleventh binary:** `optimize/baseline.NoiseReductionRadius == 4` on **8 of 8**
> wave-20 gate logs and on the probe log, with `optimize/seed == 3` beside it (clause `G20-P1`) — the two
> quantities that share the name, printed in the same log, in two separately-tagged blocks.
>
> **This closes the documentation half only.** (a) the seed literal, (b′) the 20 preset-owned dead literals, and
> the out-of-sample pass over wave 18's two arms are **unchanged and still owed**;
> `/mnt/d/hf_w18/seedA0` and `seedA1` were fingerprinted and proven byte-identical (48 of 48) by a wave that read
> neither.

### (b′) — the partition, DERIVED FROM SOURCE at last, and it contains a trap

Wave 21 (2026-08-11) parsed `StarDetectionOptions.cs` by brace depth and intersected the public-property
assignments of `ResetDefaultsImpl` with those of `DerivePresetSettings`. **`ResetDefaultsImpl` assigns 53 public
properties. Exactly 20 are preset-owned; 33 are not; 0 are derived-but-missing.** The register's "20" was a
hand-count and it is now confirmed mechanically — *but the count was never the risky part, the membership was.*

**The 20 DEAD literals** — `DerivePresetSettings` assigns every one of them unconditionally, and
`ResetDefaultsImpl` ends with an unconditional `ConfigureSimpleSettings()`, so each of these is redundant:

```
BrightnessSensitivity  HotpixelFiltering  HotpixelThreshold  MaxDistortion  MinHFR
MinStarBoundingBoxSize  NoiseClippingMultiplier  NoiseReductionRadius  PSFFitThreshold  PSFFitType
PSFResolution  PixelSampleSize  StarBackgroundBoxExpansion  StarCenterTolerance  StarClippingMultiplier
StarMeasurementNoiseReductionEnabled  StarPeakResponse  StructureDilationCount  StructureDilationSize
StructureLayers
```

**THE TRAP, and it is why "derived from source, not guessed" was made a precondition.** Four properties *look*
preset-related and are **LIVE — deleting them breaks the class**:

| property | why it looks preset-owned | why it is NOT |
|---|---|---|
| `Simple_NoiseLevel` | named `Simple_*` | it is an **INPUT** to the derivation — the `switch` reads it |
| `Simple_PixelScale` | named `Simple_*` | **INPUT** — selects the `StructureLayers`/`MinStarBoundingBoxSize` deltas |
| `Simple_FocusRange` | named `Simple_*` | **INPUT** — selects the WideRange deltas |
| `HotpixelThresholdingEnabled` | sits beside `HotpixelFiltering`, which **is** owned | the derivation **READS** it at `:220` for the `NoiseReductionRadius += 1` compensation and never assigns it |

*A `Simple_*`-prefix heuristic would delete the derivation's own three inputs.* The remaining 29 live literals
(`DetectionBinning`, `SaturationThreshold`, `MeasurementAverage`, the `Defocus*`/`Donut*` family,
`ContaminationSensitivity`, `UseAutoFocusCrop`, `LocallyAdaptiveBinarization`, `AdaptiveNoiseBlockSize`,
`SaveIntermediateImages`, `IntermediateSavePath`, `PSFParallelPartitionSize`, `PSFPixelIntegration`,
`UsePSFAbsoluteDeviation`, `ExcludeSaturatedStarsFromHFR`, `RejectContaminatedStars`, `StructureLayerBoost`,
`ModelPSF`, `DebugMode`, `UseAdvanced`, `UseOptimizedSettings`, …) are ordinary defaults and must stay.

**The guard already exists and it is the one that matters.** Deleting the 20 is behaviour-preserving *iff*
reset-state still equals fresh-construction, and `StarDetectionOptionsTests.ResetDefaults_EqualsFreshConstruction_*`
asserts exactly that over **four entry states and every property**. It is red against mutant **M-R1** (remove the
unconditional `ConfigureSimpleSettings()`), which is why wave 21 Part 1 added it. **So the deletion is a
mechanically safe edit behind an existing test** — what is missing is not safety, it is a decision.

**Wave 21 did NOT delete them, deliberately.** `ResetDefaultsImpl:405-407` records an explicit prior decision —
*"The preset-owned literals above are retained as documentation of the intended defaults, but the derivation is
the AUTHORITY"* — and overturning a reasoned in-source decision is an owner's call, not a cleanup. Like (a), this
half of F70 is **not decidable by measurement**: both states pass every test.

### (b′) is CLOSED: the owner REJECTED the deletion, and the premise behind it was wrong

Proposed as PR #192 (branch `ghilios/f70-remove-dead-preset-literals`, now deleted) and **rejected 2026-08-11**.
The 20 literals stay. **Do not re-propose this.** The reason is not taste — the analysis the proposal rested on
had a defect, and it is the interesting part:

> **The Simple-mode preset system owns the detector configuration. All of it. That is its entire purpose —
> consistent settings across the board.**

**The "20 owned / 33 live" split is not a design boundary.** It is a snapshot of which values happen to **VARY**
between preset combinations *today*. `DerivePresetSettings` assigns 20 because only those 20 currently differ
across `Simple_NoiseLevel × Simple_PixelScale × Simple_FocusRange` — **not** because the other 33 sit outside the
preset system's ownership. A default that is identical for every combination is still preset-owned; it simply has
a constant value, so there is nothing for the derivation to compute.

Two consequences that kill the deletion:

1. **It would leave `ResetDefaultsImpl` stating an arbitrary subset** — exactly those defaults that happen not to
   vary right now — instead of the complete default configuration in one readable place.
2. **The line moves.** Make any one of the 33 preset-dependent and it silently joins the 20; make a varying one
   constant and it leaves. A boundary that shifts whenever someone edits an unrelated preset rule is not a
   boundary worth encoding in the source layout.

**The controller's error, named.** The partition was computed correctly and then **mislabelled**: *"assigned by
`DerivePresetSettings` today"* was reported as *"owned by the preset system"*. Those are not the same claim, and
the second does not follow from the first. Every artifact of that work inherited the wrong word — including the
test names `DerivationOwnedProperties_*` / `NotDerivationOwnedProperties_*`, which encode the conflation in the
codebase. **Owed: rename them to `DerivationAssignedProperties_*` / `NotDerivationAssignedProperties_*`**, which
is what they actually measure. The tests themselves are correct and stay — a mechanical fact about which
properties the derivation reassigns is worth pinning; it is only the name that overclaims.

*The mechanically-derived number was right, the English attached to it was wrong, and the English was what the
proposal was built on. A partition can be exact and still not mean what its label says.*

### Next step
Two things, and they are independent:
1. **Owner decision:** delete the 20, or keep them and change the comment to say they are *asserted-redundant*
   documentation rather than defaults. Either is defensible; the list above makes it a two-minute edit.
2. ~~The instrument that is genuinely owed~~ — **SHIPPED (wave 21), see below.**

### (b′) — the membership instrument is shipped, and the runtime probe corrected the source parse

`StarDetectionOptionsTests` gained **49 tests** (suite **3839 → 3888**, by COUNT):
`DerivationOwnedProperties_RevertWhenTheDerivationRuns` (20 cases),
`NotDerivationOwnedProperties_SurviveWhenTheDerivationRuns` (28 cases), and
`PresetOwnedPartition_CountsAndDisjointness_ArePinned`.

**No source parsing.** For each property it writes a sentinel, fires the derivation by toggling
`Simple_FocusRange` away and back, and asserts the value **reverts** (owned) or **survives** (not owned). So the
partition is now pinned by *behaviour*, and a future edit cannot move a property between the halves unnoticed.

**Demonstrated on known-bad, not just known-good** ([F66](#f66)). Mutant **M-P1** — delete
`StarPeakResponse = 0.75;` from `DerivePresetSettings` — was applied (the mutation asserted present in source),
and the result was **1 failed, 19 passed: exactly `StarPeakResponse`**. The test does not merely go red; it
*names the property that moved*. The product file was restored from a **byte backup taken before the mutation**
and verified sha-identical, because the tree carried uncommitted work and a VCS restore would have destroyed it
(wave 20's lesson).

**The probe corrected the source parse on its first run, which is the reason to prefer it.**
`DonutMaxStreakEccentricity` was classified LIVE by the brace-depth intersection and the probe reported it as
*not surviving*. The cause was neither: **its setter throws outside `[0.8, 1.0]`** (`StarDetectionOptions.cs:952-954`),
so the generic sentinel `d/2 = 0.5` was rejected and the membership was **UNMEASURED, not refuted**. Fixed by
offering *candidate* sentinels nearest-first (`d*0.95` lands inside the range) and failing with an explicit
**"could not look"** naming every rejected candidate if the setter accepts none. *A validating setter is the
generic-probe equivalent of a clamping instrument: without the candidate ladder it would have silently
misclassified a property, and without the could-not-look state it would have reported a guess as a measurement.*

**The uncovered set was four, and three of them were effort rather than mechanism — it is now ONE.** The first
pass excluded `UseAdvanced`, `UseOptimizedSettings`, `IntermediateSavePath` and `Simple_FocusRange`. Re-examined,
only one of those exclusions was real:

| property | first-pass reason | what it actually was |
|---|---|---|
| `Simple_FocusRange` | "it *is* the trigger" | **effort.** The probe now keeps **two** triggers and picks the one that is not the subject; `Simple_PixelScale` fires an equally complete re-derivation |
| `IntermediateSavePath` | "creates directories" | **wrong.** The directory creation lives in `ResetDefaultsImpl`, which the probe never calls |
| `UseOptimizedSettings` | "re-enters the derivation from its own setter" | **inert here.** `ConfigureSimpleSettings` requires `UseOptimizedSettings && HasOptimizedSettings`, and the second is false on a virgin object |
| **`UseAdvanced`** | switches out of Simple mode | **REAL, and mechanical.** It is the one write that stops the derivation running at all, so the probe cannot distinguish *"the derivation left it alone"* from *"the derivation never ran"* |

Suite **3888 → 3891**. `PresetOwnedPartition_CountsAndDisjointness_ArePinned` asserts `UseAdvanced` never
appears in either list, so the one uncovered literal cannot masquerade as a covered one. **A literal the test
cannot cover stays — but "cannot" has to mean mechanism, not the first reason that came to mind.**

**The recovered coverage is demonstrated, not assumed.** Mutant **M-P2** — insert
`Simple_FocusRange = FocusRangeEnum.Typical;` into `DerivePresetSettings`, making an *input* wrongly
derivation-owned — gives **1 failed, 30 passed: exactly `Simple_FocusRange`**. Restored from a byte backup,
verified sha-identical. *Three cases recovered from a could-not-look list are worth nothing until one of them is
shown to fail on a defect, which is the same bar F66 sets for a gate.*

### F69 — F39(b)'s flag NAMES and its own COMMENT state the opposite of its default, and that cost a pre-registered rule its verdict
**Status:** Open · found 2026-08-10 (wave 17) while deciding
[F67](#f67--af-fits-star-count-and-optimizes-are-not-the-same-number-so-the-control-built-on-their-equality-reports-could-not-look-on-exactly-the-datasets-where-the-intervention-bites-hardest)(c)
· **CLOSED 2026-08-11 (wave 21): (b) and (c) shipped in wave 20, (a) shipped in wave 21 — and (a) turned out to
be THREE copies across TWO files, not the one this entry named. See the wave-21 block at the end of this
entry** · **the behaviour is correct and deliberate; only its self-description was wrong, and the
self-description is what everyone read**

[F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(b)
was adopted as the **default** in wave 8. Wave 7's opt-in flag was kept working as a no-op, which was the right
call for reproducibility. What was not noticed is that **the surviving flag name now asserts the opposite of the
behaviour**:

```csharp
// OptimizationDiagnosticRunner.cs:216
bool applyRunDetectionBinning = !DiagnosticUtil.HasFlag(args, "--no-run-detection-binning");
```

`--apply-run-detection-binning` is still accepted and does **nothing**. A reader who greps for it — or who reads
`docs/followups.md`'s own F39 shipping note, *"`optimize --apply-run-detection-binning` (opt-in, absent ⇒
bit-identical)"*, written before the wave-8 adoption paragraph below it — concludes the behaviour is opt-in. It
is on.

**And the file says both things, 190 lines apart.** The comment at `:205-216` documents the default correctly
and at length. The comment sitting **directly over the two `ParamsDump.Write` calls** at `:405-409` reads:

```
// ... and, only under --apply-run-detection-binning, DetectionBinning + PixelScale together via
// ApplyRunDetectionBinningIfRequested. Neither the gate nor the P16 probe passes that flag.
```

**Every clause of that is backwards.** `ApplyRunDetectionBinningIfRequested` runs unless the *other* flag is
passed; the gate and the probe both ran it; and it mutates `ctx.Baseline`'s `DetectionBinning` **and**
`PixelScale` *after* the dump above it has already printed them.

### It is not cosmetic: it produced a wrong verdict in a pre-registered rule

Wave 16's **RULE P16** dumped both runners' full `StarDetectorParams` through one shared reflective formatter —
a genuinely good instrument — and concluded *"53 of 55 fields identical, therefore the disagreement is
downstream, narrowed by construction to two candidates."* It had compared `optimize`'s params **as constructed**
against `af-fit`'s **as detected**, because the comment told it the mutation could not happen. `DetectionBinning`
was **F-live**, both named candidates were dead, and the actual cause was the field the dump could not see.
Wave 16's results doc carries the correction; wave 17's `RULE C17` closed F67 by intervention instead.

**Why it matters.**

- **A comment that is wrong about control flow is worse than no comment**, because it is *evidence* — it was
  cited in a pre-registration, and the pre-registration was believed. The register's own habit of reading source
  to exclude candidates depends on the source being honest about itself.
- **A retained no-op whose name states the opposite of the default is a trap with a long half-life.** It was
  kept so wave 7's scripts *"keep working and keep meaning what they said"*, and it does keep them working. The
  flag name has been misleading since the **wave-8** adoption — nine waves — while the `:405-409` comment that
  finally cashed the trap in is only one wave old (it arrived with wave 16's `PARAMS-DUMP`, `4fd613d`). *The
  trap was laid long before the thing that stepped in it.*
- **This is [F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two)'s
  shape in a new place**: an instrument that cannot observe the thing it was built to observe.

### Next step
(a) **Fix the `:405-409` comment** — it is four lines and it is the piece that did the damage. **~5 m.** Do it in
the next wave that ships code; it changes no behaviour and needs no gate.
(b) **Print the resolved factor inside the dump block itself**, or emit a second dump *after*
`ApplyRunDetectionBinningIfRequested`, so the printed bundle is the one that detected. **~20 m + a test**, and it
is the durable fix: a comment asserting when a snapshot is taken is exactly the thing that goes stale.
(c) **Decide whether `--apply-run-detection-binning` still earns its keep.** Deleting it breaks wave 7's scripts;
keeping it keeps the trap. A third option is to make it *print* that it is a no-op and that the behaviour is on
by default — **~10 m**, and it converts a silent misnomer into a loud one.

> **(b) and (c) were pre-registered by wave 19 as item D, to ship unconditionally, and DID NOT SHIP
> (2026-08-11).** Both are still owed. The tag for (b) is fixed and should be reused: **`optimize/detected`**,
> chosen because it contains **no existing tag as a substring** (`optimize/baseline`, `optimize/seed`,
> `af-fit/detector`), so a prior wave's `grep -l "PARAMS-DUMP optimize/baseline"` cannot double-count it — a name
> like `optimize/baseline-resolved` would break wave 17's and wave 18's saved drivers. The test that makes it
> real: the block exists **and** its `DetectionBinning` *and* `PixelScale` differ from the pre-mutation block on a
> run whose resolved factor is not 1. *A dump identical to the one above it is a dump that has not been
> demonstrated.*
>
> **Measured absence, so no later wave reads the design as evidence the code exists:**
> `PARAMS-DUMP optimize/detected` appears on **0 of 8** wave-19 gate logs and **0 of 20** wave-19 arm logs; every
> one of the 28 carries exactly two blocks. That count was in the driver as a *reported number with no threshold
> attached*, and it is the only clause in the wave whose value depended on the shipped code — **it is what caught
> the non-delivery**, while the gate's ten thresholded clauses passed identically either way.
Reproduce: `OptimizationDiagnosticRunner.cs:205-216` against `:405-409` and `:570-586`; the artifact line that
said so all along is `/mnt/d/hf_w16/probe/D12_c14_585_afbin2.log:135`;
`docs/synthetic-af-bank-followups-wave17-results.md` §3.1.

> ### WAVE 20 (2026-08-11): **(b) and (c) SHIPPED and are measured on an eleventh binary. (a) is STILL FALSE, at an address this entry never named.**
>
> **(b) — the `optimize/detected` dump is in the product and in the logs.**
> `ParamsDump.OptimizeDetected = "optimize/detected"` (the tag this entry fixed in advance, chosen because it
> contains no existing tag as a substring), plus `ParamsDump.AllSources` so the non-collision property is
> asserted over the **set** rather than a list a later wave must remember to extend, plus a third
> `ParamsDump.Write` at `OptimizationDiagnosticRunner.cs:688` — **immediately after**
> `ApplyRunDetectionBinningIfRequested` (`:675`), inside `RunPerRun`.
>
> | measurement | value |
> |---|---|
> | clause **`G20-P2`**, wave 20's gate | **PASS, 8 of 8 on all five sub-clauses**: block exactly once; 55 field lines / 55 unique names; `detected.PixelScale` finite while `baseline.PixelScale` is the token `NaN`; `detected.DetectionBinning == 1` (an equality — the gate's eight datasets all resolve factor 1, so a "differs" clause there would be unsatisfiable, [F68](#f68--a-threshold-stated-as-a-count-carries-a-denominator-and-three-consecutive-satisfiability-analyses-have-checked-the-value-a-clause-can-reach-without-checking-the-population-it-is-computed-over)); `optimize/baseline` and `optimize/seed` still exactly once each |
> | the **same scorer** on wave 19's gate logs | **`G20-P2a` 0 of 8** — *"F69(b) IS NOT IN THIS BINARY"*. **The FAIL end is observed on real artifacts, in the same minute as the PASS end.** This is the shape wave 19's claim lacked: its ten thresholded clauses read identically with item D present or absent |
> | inertness | `G20-1` reproduces the eight-value coordinate system **bit-identically at sixteen digits** with the dump present, so its inertness is a **measurement, not an assumption** |
> | the post-mutation property, on a factor-2 run | **`optimize/baseline` `DetectionBinning=1 PixelScale=NaN` → `optimize/detected` `DetectionBinning=2 PixelScale=0.6296504611753467`**, difference set over all 55 fields **exactly `{DetectionBinning, PixelScale}`**; on the 8 factor-1 gate logs the same set is **exactly `{PixelScale}`, 8 of 8** |
>
> **That last row is a DIAGNOSTIC, not a clause result.** RULE D20 returned **`D-UNEVALUATED`** naming `D20-V1`,
> because the probe driver and its scorer disagreed on where the log lives
> ([F74](#f74--a-driver-and-its-scorer-each-rebuilt-the-artifact-path-from-a-template-disagreed-and-cost-a-pre-registered-rule-its-verdict--while-the-interlock-marker-between-them-already-carried-the-answer)).
> The instrument P16 lacked now exists and prints; **the rule that would certify it has not issued**, and
> recovering it costs ~3 minutes. Until then this entry claims *the dump is shipped, reached and inert* — all
> gate-measured — and **not** *the dump is demonstrably post-mutation*.
>
> **(c) — the no-op notice ships.** Behind the flag guard: *"`--apply-run-detection-binning`: ACCEPTED NO-OP.
> F39(b) was adopted as the DEFAULT in wave 8; the opt-OUT is `--no-run-detection-binning`. This flag is
> retained so wave 7's scripts keep running, and it is not read (F69(c))."* Measured in **both** directions:
> once on the probe log (flag passed), **zero times on all 8 gate logs** (flag absent). ASCII-only, deliberately
> — a Unicode character in a *redirected* log arrives as the single byte `0x1A` on this machine's console code
> page. The misnomer is now loud instead of silent, which is this entry's option (c) as written.
>
> **(a) IS STILL OWED, and the interesting part is where.** This entry said *"fix the `:405-409` comment — it is
> four lines and it is the piece that did the damage"*, and that site **was** fixed in wave 18 (`93e366a`);
> `:410-419` now carries an explicit *"THIS PARAGRAPH USED TO SAY THE OPPOSITE, AND WAS BELIEVED"*. **The same
> false sentence exists a second time**, as the XML doc on the method itself, `:591-594`, where it has sat since
> `9cd4c02` (the commit that introduced F39(b)):
>
> ```csharp
> /// No-op unless <c>--apply-run-detection-binning</c> was passed, so the flag's
> /// absence leaves the run bit-identical to before it existed (F41's one-binary-is-both-arms rule).
> ```
>
> It is backwards in exactly the way that cost RULE P16 its verdict, and it now sits **three lines above a body
> that prints the opposite at runtime**. It was outside wave 20's pre-registration and was therefore not
> touched. **Fix, ~2 minutes, no behaviour change, no gate:** *"Runs by DEFAULT (F39(b), wave 8); the opt-OUT is
> `--no-run-detection-binning`. `--apply-run-detection-binning` is an accepted no-op, retained for wave 7's
> scripts (F69(c))."*
>
> > **The generalisable half, and it is
> > [F71](#f71--a-converted-settings-file-carries-every-detector-knob-twice-and-the-control-that-checked-the-conversion-read-the-copy-the-loader-ignores)'s
> > lesson wearing documentation's clothes.** This entry named a **line range**. The defect was a **sentence**,
> > and it existed in two copies. Fixing the cited copy made the entry look discharged while the other copy went
> > on being true-looking and false for two more waves. *When a register entry names where a false statement
> > lives, the next step must be `grep` for the statement, not an edit at the address.*
>
> Also shipped alongside, and pinned by a test: **`ApplyRunDetectionBinningIfRequested` has exactly ONE call
> site** (`:675`, in `RunPerRun`), so the joint (non-`--per-run`) path never calls it and correctly carries no
> `optimize/detected` block. `ParamsDumpTests` asserts the call-site count is 1, so a later wave that adds a
> second site is told rather than left to read a missing block as a regression. **7 new tests, suite 3831 → 3838,
> verified by COUNT before the build.**

> ### CLOSED 2026-08-11 (wave 21) — (a) shipped, and there were THREE false copies across TWO files
>
> Commit **`9a49aa6`**, `2026-08-11T09:27:48Z`. Suite **3839** (`3838 + 1`), by COUNT.
>
> This entry named **one** surviving site. The test written to make the class mechanical found **two more**, one
> of them in a file this entry has never mentioned:
>
> | site | what it said | fixed to |
> |---|---|---|
> | `OptimizationDiagnosticRunner.cs:591-594` — the XML doc, the site this entry named | *"**No-op unless** `--apply-run-detection-binning` was passed…"* | *"Runs by DEFAULT (adopted in wave 8…); the opt-OUT is `--no-run-detection-binning` … `--apply-run-detection-binning` is still ACCEPTED and is NOT read (F69(c))."* |
> | `OptimizationDiagnosticRunner.cs:562` — the field comment | *"`--apply-run-detection-binning`: F39(b); **false => bit-identical**"* | the opt-out named as the thing that makes it false |
> | `HarnessSettingsStore.cs:419` — **a different file** | *"**Used by** `optimize --apply-run-detection-binning`"* | *"Used by `optimize` on EVERY run since wave 8 … the opt-OUT is `--no-run-detection-binning`"* |
>
> **All three are paraphrases whose only shared substring is the flag name.**
>
> > **THE LESSON, AND IT OVERRIDES WAVE 20's LESSONS #4.** That lesson read *"when a register entry names where a
> > false statement lives, the fix is `grep` for the sentence, not an edit at the address."* **A grep for the
> > sentence finds neither of the two extra copies.** The rule that works:
> > **grep for the IDENTIFIER the false claim is about, read every hit — then leave a test that does it for you.**
> > *An edit at an address fixes one site; a grep for a sentence fixes one wording; only a test fixes the class.*
>
> **The test:** `ApplyRunDetectionBinning_EveryCommentNamingTheOptInFlag_AlsoNamesTheOptOut`
> (`ParamsDumpTests.cs:721`). Every comment **block** in every `*.cs` under `TestApp/` that names the opt-in must
> also name the opt-out. It uses a string-literal-aware scanner (so a `Console.WriteLine` of the flag is not read
> as a comment), **prints its population — 5 blocks over 49 files — and asserts `>= 2`**, and a **zero**
> population FAILS with *"the identifier this test is about is gone from TestApp's comments; it can no longer see
> its subject"*. It is stated one-directionally on purpose: a comment may name only the opt-out, because that
> direction is never the backwards one.
>
> **Two RED demonstrations, run and observed rather than asserted.** (1) Restoring the `:591` sentence turns it
> red and names the block **with the population still 5** — the block *moves* from the satisfying set to the
> offending set rather than vanishing, which is the property that makes the printed count meaningful. (2) A
> one-line opt-in-only comment inserted into a previously uninvolved file (`DiagnosticUtil.cs`) also turns it red
> and names that file, so the test guards **future** additions, not just today's three. The mutant was removed
> and `git status` shows the file clean.
>
> **Reach, stated the way this series requires:** the gate does **not** reach item A and could not — a comment is
> not IL and a test does not ship. The wave's binary was in fact built *before* these edits existed
> (`docs/synthetic-af-bank-followups-wave21-results.md` §8.2), so the change is not even present in it. **The
> check that reaches this item is the suite, by COUNT.**

### F68 — A threshold stated as a COUNT carries a denominator, and three consecutive satisfiability analyses have checked the VALUE a clause can reach without checking the POPULATION it is computed over
**Status:** Open (a standing discipline entry) · found 2026-08-10 (wave 16) when a clause returned **100 % of the
property it was written to express and FAILED** · **the defect is arithmetically provable from the pre-registration
alone, before any wave-16 data**

Wave 16's **S16-A(b)** read:

```
S16-A(b) rho <= 1.1: 12 of 12 rejections SURVIVE at V1.00 (threshold: >= 30)   -> FAIL
```

**12 of 12 is 100 %. The bar's own implied rate is 30/33 = 90.9 %. It fails on the absolute count and only on the
absolute count**, because the maximum attainable value of its numerator on the instrument the clause reads is
**12**, and the bar was fixed at 30.

### The two populations, and why the denominator does not transfer

Both are views of the same 39 runs. They are not the same measurement.

| | wave 14's SEM audit (`/mnt/d/hf_w14/stageA/sem_audit.tsv`) | wave 16's S16-A (`score_sem_w16.py:461-492`) |
|---|---|---|
| a row is | one round of the **single winning model's** printed Grubbs trace that ended in a rejection | one position in the **budget-3 row of the production budget table** |
| the set is | one model's greedy rejection sequence | the **consensus** — the intersection of what all four Hybrid candidate models reject |
| total over 39 runs | **42** | **18** |
| in the `ρ ≤ 1.10` bucket — **the denominator** | **33** | **12** |

**All 18 consensus rejections are inside wave 14's 42 (the consensus is a strict subset), and the other 24 are
rejections production never performs.** `CWhiteFocus_AutoFocus_20220429_000244_attempt01` is the clean case: its
traced winner `Symmetric` rejects 20350, 20650 and 20800 in its own loop, while `TiltedHyperbola` on the same data
rejects **nothing**, so the intersection is `(none)` at every budget and the production budget table reads 0
rejections on all four rows. Wave 14's audit counted three. The 24 phantoms are concentrated at the **top** of the
s-distribution — 79.7824, 47.5143, 25.6755, 24.2482, 19.0993, 18.2715, 16.3351, 8.6853.

### The design knew, wrote it down, and carried the number across anyway

The pre-registration's **§4.1 is the section whose entire job is to compute every clause's maximum attainable
value with the arithmetic before the data.** Its table reads *"S16-A(b) | max attainable 33 of 33 | attainable?
yes"*. The scorer's own header, **one line above the threshold**, reads:

```
S16-A  SEPARATION, on a NEW population (all four candidate models' consensus, not one printed trace).
       (b) at V1.00, rejections surviving on runs with control-rung rho <= 1.10  >=  30 of 33
```

and [F65](#f65--the-hybrid-consensus-is-an-intersection-over-four-models-that-can-reject-the-same-points-in-a-different-order-so-the-rejected-set-at-budget-b-is-not-a-prefix-of-anything)
— the register entry that exists **because** the consensus is an intersection over models that need not agree — is
cited four times elsewhere in the same design. **The population change was known, named and given its own register
entry, and the absolute count was carried across it regardless.**

### The same error is harmless in one clause and fatal in its twin

**S16-A(a)** is a `== 0` bar: shrinking its denominator from 3 to 2 cannot break a clause demanding that *nothing*
survive, and it passed at **0 of 2**. **S16-A(b)** is a `≥ 30` bar: shrinking its denominator from 33 to 12 makes
it unreachable by 18. *A bar stated as a count is hostage to its denominator. A bar stated as a rate, or as a
maximum, is not.*

### And a second instance in the same section, in a different shape

**S16-E(i)** vetoes a rung when the **median** `|Δe|` over 20 synthetic datasets reaches 0.10 step. §4.1 proved it
foreclosed for family V by arithmetic — correct, and stated — and then declared it *"live for `R` and `R` alone"*
on the grounds that R's `|Δe|` is not bounded by the same table. That is true about the **quantity** and says
nothing about the **statistic**: moving a median of 20 requires at least **11** datasets to move by ≥ 0.10 step.
R moved **6**, one of them by **0.13071 step**. **A dataset cleared the materiality floor by 31 % and the veto
stayed silent.** Weaker than S16-A(b) — the veto is not provably unreachable for R, only unreachable for any
effect confined to fewer than half the bank — and the same omission: the range was computed for the value, never
for the aggregation.

### Three waves, three satisfiability sections, three defects of the section's own class

| wave | the section's stated job | the defect it contained |
|---|---|---|
| 14 | check every clause can fire | **C4** was decisive and arithmetic had already foreclosed it |
| 15 | check every clause's PASS value is attainable | **G-d** could never observe *"converted files identical"* — every landing carries `CreatedAtUtc`. *The section written to catch unreachable branches contained one.* |
| 16 | check every clause's **maximum attainable value**, with the arithmetic, before the data | **S16-A(b)** computed the maximum over a population the clause does not read; **S16-E(i)** was declared live for `R` without computing what its aggregation requires |

**And a third shape in the same wave, on a clause the both-branches table never reached.**
`P16-INSTRUMENT-vs-PRODUCT` reads *"if **every F-live difference** is a field that exists only because the harness
configures `af-fit` … then F67 is an instrument artifact; otherwise it is a product finding."* Under the **P-b**
verdict there **are** no F-live differences, so the universal quantifier is vacuously true and the clause returns
**INSTRUMENT** — while the **P-b row of the same rule** says P-b is *"Also a product finding, and the more
interesting one."* **Two clauses of one rule give opposite answers on the branch that actually occurred.** The
clause was written for the P-a branch and never asked what it returns when its domain is empty; §4.3's
both-branches table lists P16's outcomes as P-a / P-b / P-c / UNEVALUATED and never reaches the
instrument-vs-product test at all. *(Wave 16 recorded PRODUCT, on the specific clause written for the outcome that
happened rather than the general one written for the other, and gave the substantive reason: both surviving
candidates are production code paths, and the harness's configuration was measured identical to the product's on
all 53 live fields. `docs/synthetic-af-bank-followups-wave16-results.md` §3.4.)* **A quantifier whose domain can be
empty is a denominator wearing different clothes.**

**Each section was written to prevent the previous wave's failure, and each prevented it.** Wave 15's Lesson 4 —
*"ask of every clause: what input makes this return each of its outcomes?"* — was honoured: wave 16's §4.3 lists
*"3 constructed populations"* for S16-A. **Constructed populations cannot detect a denominator error, because the
constructor chooses the denominator.** The generic form of the question, which is this entry:

> **For every clause: name the POPULATION it is computed over, the STATISTIC computed on it, and the AGGREGATION
> applied to that statistic — then compute the bar's reachable range under all three, on the real artifacts, not
> on constructed input.** A threshold derived from a different instrument's view of the same runs must be
> re-derived on the instrument the clause reads, or restated as a rate.

### Why it matters

- **It cost a clause its verdict, on a measurement that succeeded.** S16-A(b)'s underlying property held at 100 %.
  RULE S16 reached outcome 4 (NO RECOMMENDATION) rather than outcome 2 with S16-A(b) as one of its conjuncts, and
  outcome 2's other six conjuncts all held at `V1.00`.
- **The rule was applied as written and the clause stands at FAIL.** No repaired form was evaluated anywhere in
  wave 16, and re-deciding it would have cost **zero minutes** of compute — it is Python over data on disk. *That
  is the price of the register meaning anything, and it is the cheapest thing in the wave to get wrong.*
- **It is the third wave in a row**, which makes it a property of the method rather than of any one design.

> ### REINFORCED 2026-08-10 (wave 17): (a) was taken, the template WORKED, and two new instances turned up outside it
>
> **(a) is DONE.** Wave 17's pre-registration carries the **P / S / A / E** columns on every clause of RULE G17,
> RULE C17 and RULE D17, and derived every denominator **on the artifacts the clause reads, at pre-registration
> time**. It paid twice:
>
> - **`C17-A`'s bar had both ends OBSERVED before the wave ran** — 0.000 of 63 at the status quo (measured in
>   waves 15 and 16 on the same pair of artifacts) and 1.0000 of 63 under the treatment. *That is the check
>   `S16-A(b)` never made.*
> - **`C17-D` and `D17-1` were written so an empty domain routes AWAY from the expected verdict**, not toward it:
>   `C17-D` empty prints **NOT ESTABLISHED**, and `D17`'s self-test distinguishes *"seed block absent everywhere"*
>   from *"union is empty"* so an empty population can never read as `D-NOOP`. That is
>   `P16-INSTRUMENT-vs-PRODUCT`'s defect closed twice in one wave, in two different rules.
> - **No median appears anywhere in the design**, so `S16-E(i)`'s aggregation defect had no way in.
>
> **The two new instances are both outside the pre-registered clauses, which is the useful part.**
>
> 1. **The controller's own progress counter had no *could-not-look* state.** It looked for the wrong filename in
>    the wrong directory and printed `summaries=0` for two arms that had each produced **10 of 10** landings, and
>    the wave was reported as failed twice on the strength of it. It could return "0 found"; it could not return
>    "the path I looked at does not exist." **The three-state rule held in all three pre-registered scorers and in
>    no improvised one** — which is the honest description of how a discipline fails.
> 2. **`query session` is an n = 1 sample of a time-varying quantity, quoted for eight waves as a standing
>    property of the machine.** Recorded as `Disc` at wave time and **`console ghili 1 Active`** forty minutes
>    later. Neither reading is wrong; the instrument simply has **no `when` and no population** attached to it.
>    The repair is to sample at the start and the end of an arm and report both — *the smallest possible clause
>    with the same defect.*
>
> **And one generalization worth carrying, from the control side rather than the clause side:** *a file class
> that becomes evidence must become a control in the same wave.* Item A's answer reads
> `synthetic_meta.json` and `harness_settings.json`, and `HarnessSettingsStore.ResolveForRun` **writes** the
> latter when it is absent — so wave 17 fingerprinted all 59 of them (39 + 20) before the gate and after every
> arm, alongside the 42 landings. **59 of 59 byte-identical.** Cost: one Python file.
> Reproduce: `docs/synthetic-af-bank-followups-wave17-design.md` §1.1/§2.5/§3.2 and §5;
> `docs/synthetic-af-bank-followups-wave17-results.md` §2.3, §6, §7.2.

> ### REINFORCED 2026-08-11 (wave 18): all four columns were right and the clause was still broken — the question missing is WHICH FIELD
>
> Wave 18's design is the most thorough satisfiability section this series has written. Every clause of RULE G18
> and RULE N18 carries **P / S / A / E**; every denominator was counted **on the artifact the clause reads** at
> pre-registration time; the six absolute-count bars are listed with their ranges (*"wave 17's §5.1 claimed it
> used no absolute counts while `C17-B`'s bar was literally 27 of 27; this design does not make that claim"*);
> and the design's one median has its foreclosure computed — *"moving a median of 20 requires ≥ 11 datasets to
> move by ≥ 0.10 step, i.e. eleven simultaneous movements each ~4× larger than any ever recorded"* — with the
> clause labelled a **VETO** everywhere it appears rather than the decider, which is `S16-E(i)`'s defect written
> out instead of repeated.
>
> **And `N18-V7` was broken anyway.** Its four columns read: population — the 2 × 20 `af-fit` `run.log` dumps
> against the 2 × 20 converted settings files; statistic — field equality; aggregation — a rate per arm; empty
> answer — COULD-NOT-LOOK, named, out of **both** of `N18-P`'s denominators, which are re-printed. **All four are
> correct.** The clause still could not measure what it claimed, because a converted settings file carries
> `NoiseReductionRadius` **twice** and the clause compared against the copy the loader ignores
> ([F71](#f71--a-converted-settings-file-carries-every-detector-knob-twice-and-the-control-that-checked-the-conversion-read-the-copy-the-loader-ignores)).
> The §5.2 both-branches table checked that `N18-V7` could return 1.000 and could return 0 — and it can, and
> neither value means what the clause says.
>
> > **A denominator error is a POPULATION that is not what you think it is. This is a QUANTITY that is not what
> > you think it is, and the P/S/A/E template cannot see it**, because *"field equality"* silently assumes the
> > two sides name the same thing. **The fifth question is: name the FIELD, and if the artifact contains more
> > than one field with that name, say which one and why.**
>
> **Two things wave 18 got right, recorded because they are the template working.** `N18-V4` — *"is `J` the same
> function in both arms?"* — was written as a **checked precondition** for the decisive clause rather than an
> assumption ([F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)),
> and it measured **20 of 20 bit-identical `BaselineJ`**. And the branch table routes **every** empty answer
> *away* from shipping: an unevaluated `N18-P` and an unevaluated `N18-J` both reach `N-UNEVALUATED`, and the
> scorer's self-test demonstrates all fifteen branches including *"a wholly inert treatment reaches `N-ALIGN` and
> is NOT reported as a pass on merit"*.
>
> **One more instance, from the improvised side, closing wave 17's item-C thread.** Wave 17 recorded that
> `query session` is an n = 1 sample of a time-varying quantity, quoted for eight waves as a standing property of
> the machine. Wave 18 can say what the eight `Disc` readings were masking: the session **is** connected, NINA
> **does** launch, and it then **crashes in `PluggableBehaviorSelector<,>..ctor`** — a NINA 3.3.0.1048 host
> loading a plugin built against `NINA.Plugin 3.2.0.2001-beta`. **The eight-wave `Disc` reading was never the
> whole blocker**, and the blocker underneath it is a **permission the agent does not hold** — one launch with
> the plugin folder moved aside — not a machine state. *An instrument with no `when` and no population does not
> merely report a stale value; it can conceal an entirely different obstruction for eight waves.*
> Reproduce: `docs/synthetic-af-bank-followups-wave18-design.md` §5 and §4;
> `docs/synthetic-af-bank-followups-wave18-results.md` §5.5 and §6.

### Next step

(a) **DONE (wave 17)** — the P/S/A/E columns are in the template and were applied to all three of wave 17's
rules. **Keep them**, and note the two places the wave still got caught: improvised instrumentation, and a
one-sample reading of a time-varying quantity.
(a′) **Add the population/statistic/aggregation triple to the satisfiability template**, as three named columns
beside "max attainable" — **~0**, a design-template change, and it should be taken by the next wave that writes a
pre-registration. **Add one more column while doing it: for any clause quantifying over a set, what does it return
when that set is EMPTY?** **DONE (waves 17 and 18)** — the P/S/A/E columns are on every clause of both waves.
(a″) **Add a FIFTH column: the FIELD.** For any clause that compares a value against an artifact, name the exact
field on each side, and — the part that would have caught wave 18's `N18-V7` — **say what happens if the artifact
contains more than one field with that name.** A converted settings file contains every detector knob twice
([F71](#f71--a-converted-settings-file-carries-every-detector-knob-twice-and-the-control-that-checked-the-conversion-read-the-copy-the-loader-ignores));
`ProfileId` is a name-plus-GUID string on one side and a bare GUID on the other
([F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two));
`af-fit`'s star count and `optimize`'s carry the same name and are not the same number
([F67](#f67--af-fits-star-count-and-optimizes-are-not-the-same-number-so-the-control-built-on-their-equality-reports-could-not-look-on-exactly-the-datasets-where-the-intervention-bites-hardest)).
**Three entries in this register are the same defect in the FIELD dimension, and the template has no column for
it.** **~0**, next pre-registration.
(b) **Prefer rates and maxima to counts wherever a clause can be phrased either way**, and where a count is
genuinely required, derive its denominator **on the artifacts the clause will read**, at pre-registration time —
which for S16-A(b) would have been one `load_rung` call over wave 14's `affit_A0.00`, already on disk. **~0.**
(c) **Do not re-score S16-A(b).** Recorded as a next step so that no later wave takes it as an oversight.
Reproduce: the two populations are `/mnt/d/hf_w14/stageA/sem_audit.tsv` (42 rows) and the budget-3 rows of
`/mnt/d/hf_w16/affit_N/*/*/af_fit_summary.txt` (18 positions); the clause is
`python3 /mnt/d/hf_w16/score_sem_w16.py --root /mnt/d/hf_w16 --w13 /mnt/d/hf_w13 --w14root /mnt/d/hf_w14 --out
/mnt/d/hf_w16/s16_score.txt`; `docs/synthetic-af-bank-followups-wave16-results.md` §2.4 and §2.7;
`docs/synthetic-af-bank-followups-wave16-design.md` §4.1.

> ### TWO MORE COLUMNS, added 2026-08-11 (wave 21). Both were paid for by wave 20, one verdict each
>
> The satisfiability template asks what value a clause can reach and over what population. Wave 20 lost a
> measurement to each of two questions it did not ask, and wave 21 added both as columns and had both fire.
>
> **(d) CHECK THE PRECISION OF THE INSTRUMENT THE CLAUSE READS.** A tolerance is only meaningful against the
> resolution of the thing being compared. Wave 20's `D20-B` compared a `double` against a console line *"to 6
> dp"*, implemented as `abs(Δ) < 5e-7` — but the console prints `v.ToString("G6", CultureInfo.InvariantCulture)`,
> **six SIGNIFICANT digits**. *Named by identifier rather than by address, per
> [F69](#f69--f39bs-flag-names-and-its-own-comment-state-the-opposite-of-its-default-and-that-cost-a-pre-registered-rule-its-verdict):*
> the `detecting at PixelScale …` line calls `F(ctx.Seed.PixelScale)`, and `F(double)` is the only `G6` formatter
> in `OptimizationDiagnosticRunner.cs` (at `:615` and `:1942/:1946` after wave 21's item A; wave 21's design cites
> the pre-item-A `:611` and `:1938`). Below 1.0 the tolerance **equals** the
> instrument's quantum; at or above 1.0 the quantum is `5e-6`, **ten times the tolerance**. This is not an
> argument, it is an **observation, made twice on real logs**: applying `D20-B` verbatim to wave 20's eight gate
> logs fails **4 of 8**, and applying it to wave 21's eight — a different binary, a day later — fails **the same
> 4**: `CWhiteFocus`, `D18_m24_deep_shed`, `D20_m24_bright_control`, `muggsie`, **exactly the four whose
> `PixelScale >= 1`**. A correct program, failed by its own clause.
> The corrected form rounds to the instrument's own precision — `round(v, 6 - floor(log10 |v|) - 1) ==
> float(console)` — and is **8 of 8** on the same logs, while still **failing** a constructed genuine
> disagreement (self-test 18) and agreeing with the broken rule below 1.0 (self-test 17). *A repair that only ever
> passes is a loosening; a repair is a correction when the clause it replaces still fails what should fail.*
>
> **(e) CHECK THAT THE POPULATION IS ADDRESSABLE, NOT ONLY THAT IT EXISTS.** Wave 20's RULE D20 had a population
> of exactly one dataset, which existed, which had been measured correctly, and which the scorer could not open —
> the driver and the scorer each rebuilt the path from a template and disagreed
> ([F74](#f74--a-driver-and-its-scorer-each-rebuilt-the-artifact-path-from-a-template-disagreed-and-cost-a-pre-registered-rule-its-verdict--while-the-interlock-marker-between-them-already-carried-the-answer)).
> *A satisfiability analysis that verifies a clause's values while never verifying that its artifacts can be
> opened has checked the easier half.* The executable form is a **validity clause that is itself the
> addressability check**: wave 21's `B21-V1` requires the marker to exist **and carry a `manifest=` line**, the
> manifest to parse, **every row's `log` and `landing` to resolve as written**, and `>= 4` of 6 to be scoreable —
> any could-not-look ⇒ `B-UNEVALUATED` with the manifest's own string quoted. It returned **6 of 6**.
>
> **The pairing is the point.** (d) is about the axis a clause measures along; (e) is about whether the clause can
> reach its data at all. Wave 20 lost `D20-B` to the first and RULE D20's whole verdict to the second, in the same
> wave, with the correct measurement sitting in a log on disk.
> Reproduce: `python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w20/gate`;
> `python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w21/gate` (`G21-P3a` 8 of 8, `G21-P3b` MATCH);
> `python3 /mnt/d/hf_w21/score_b21_w21.py --self-test` (branches 8, 10, 15–18);
> `docs/synthetic-af-bank-followups-wave21-results.md` §2.1 and §3.2.

### F67 — `af-fit`'s star count and `optimize`'s are NOT the same number, so the control built on their equality reports "could not look" on exactly the datasets where the intervention bites hardest

> ### **CLOSED 2026-08-10 (wave 17) BY INTERVENTION. The cause is DETECTION BINNING — and it refutes wave 16's OWN narrowing.**
>
> **The decisive clause is `C17-A`, and it is an intervention rather than a correlation.** Two arms, one binary,
> one settings file, one profile, sequential: **X0** adds `--no-run-detection-binning` and nothing else.
>
> | clause | pre-registered threshold | measured |
> |---|---|---|
> | **C17-A** *decisive* | rate **1.000 of 63** — X0's `currentStarCount` equals wave 16's `af-fit` `Stars` on the 7 factor-2 datasets | **63 of 63 = 1.0000**, 9 of 9 on every one of the seven. **The status quo measures 0.000 of 63, so BOTH ENDS OF THE BAR WERE OBSERVED** |
> | **C17-B** *control* | **27 of 27 on both equalities** — the flag is a no-op where the derived factor is already 1 | X0==X1 **27 of 27**; X0==`af-fit` **27 of 27**. (The three controls also took 167 s and 165 s of wall time in the two arms — 1.2 % apart) |
> | **C17-C** *by-construction, recomputed* | reported; never decisive alone | factor 1 **188 of 188**; factor 2 **0 of 63**; could-not-look **0** |
> | **C17-D** *inertness precondition* | every `PARAMS-DUMP` block reads a FULL `Region` **and** `MeasurementAverage=Median`; total asserted > 0 | **95 of 95** blocks |
> | **C17-V1/V2/V3** *validity* | status quo reproduces at **1.000 of 90**; 10 of 10 landings per arm; the flag demonstrably took | **90 of 90**; **10 / 10** both arms; `DISABLED` in **10 of 10** X0 logs and **0 of 10** X1 logs |
>
> **`RULE C17: C-BINNING`.** X0 equals `af-fit` position for position, and X1 — one flag away — is larger by up
> to **2.2×** at the sweep wings. **This entry's own recorded ratios are reproduced to three decimals**: it says
> *"`D08` … runs 0.46–0.52 of `optimize`'s count at the extremes against 0.82 at focus"*; measured under the
> intervention, **0.455–0.522** at the extremes and **0.819** at focus. `D11_rc10_585_afbin2` was chosen as a
> control deliberately — *its name says `afbin2` and its derived factor is 1.*
>
> **F39(b) is ON BY DEFAULT.** `OptimizationDiagnosticRunner.cs:216` is
> `!DiagnosticUtil.HasFlag(args, "--no-run-detection-binning")`, and `--apply-run-detection-binning` is a
> retained no-op. `ApplyRunDetectionBinningIfRequested` rewrites `DetectionBinning` and `PixelScale` on every
> `optimize` run that asks for it; **`af-fit` has no such step.**
>
> | evidence, all from artifacts already on disk | result |
> |---|---|
> | `expectedOptimal.detectionBinning == 2` in `synthetic_meta.json` | `{D08, D09, D10, D12, D14, D15, D17}` — **exactly this entry's disagreeing set** |
> | factor logged in wave 15's `land_mor0`, the arm F67 was measured on | 2 on those 7, 1 on the other 13, 0 could-not-look |
> | per-position `af-fit Stars` == `optimize currentStarCount`, **factor 1** | **188 of 188** (13 synthetic + 5 real gate runs, two waves) |
> | same, **factor 2** | **0 of 63** |
>
> **Wave 16's RULE P16 could not have seen it.** `PARAMS-DUMP` prints where the bundles are *constructed*, one
> statement before the mutation, and the comment directly above those calls asserts the mutation happens only
> under a flag — **reading the polarity backwards**. So P16 compared `optimize`'s params as constructed against
> `af-fit`'s as detected, and `DetectionBinning` is **F-live**.
>
> **Both wave-16 candidates are dead**, and neither needed a measurement to kill: candidate 1 names
> `RunEvaluationLoader`, the **wizard's** loader, which the measured path never calls (`TestApp optimize` calls
> the same `DiagnosticUtil.LoadRenderedImage` as `af-fit`); candidate 2 is arithmetically impossible in the
> observed direction, because the post-filter set is a **subset** and the measurement has the pre-filter side
> smaller. *A narrowing "by construction" is only as good as the construction.*
>
> **What this does NOT say.** The pre/post-filter distinction between the two counts is real and unchanged — it
> is simply **inert at S0** and is not what anyone was tripping over. On the 13 factor-1 synthetic datasets and
> all 5 real gate runs the two numbers **are the same number**.
>
> **PRODUCT or INSTRUMENT: the answer differs from wave 16's, and it is recorded rather than inherited.** Wave 16
> called F67 a PRODUCT finding because both surviving candidates were production code paths. F39(b) is
> **harness-only** — `optimize` applies a per-run factor by default, `golden eval` opt-in, **`af-fit` and
> `bank-verify` not at all**, and the shipped wizard reads the profile's `DetectionBinning` with no
> `synthetic_meta.json` and no per-run physics derivation. So this is **an INSTRUMENT fact with a real product
> consequence**: the instrument fact is that three harness commands disagree about what binning a bank run is
> detected at; the product consequence is the pre/post-filter distinction, which is real, unchanged, **inert at
> S0**, and *not* what caused this.
>
> **The register consequence is larger than F67.** Any wave-to-wave comparison of `af-fit` against `optimize` on
> `{D08, D09, D10, D12, D14, D15, D17}` has been comparing two detectors. Wave 15's RULE L15 lost its verdict to
> exactly this — G-c returned *could not look* on exactly those seven.
>
> **The self-description defect this exposed is [F69](#f69--f39bs-flag-names-and-its-own-comment-state-the-opposite-of-its-default-and-that-cost-a-pre-registered-rule-its-verdict) rather than an amendment here**, because
> F39's entry is correct about the *behaviour* and burying a live defect inside a mostly-closed entry hides it.
>
> Reproduce: `python3 /mnt/d/hf_w17/score_c17_w17.py --x1 /mnt/d/hf_w17/binX1 --x0 /mnt/d/hf_w17/binX0
> --affit16 /mnt/d/hf_w16/affit_N --gate16 /mnt/d/hf_w16/gate --w13 /mnt/d/hf_w13 --w15 /mnt/d/hf_w15
> --gate /mnt/d/hf_w17/gate --out /mnt/d/hf_w17/c17_score.txt`;
> `python3 /mnt/d/hf_w17/score_c17_w17.py --self-test` (13 demonstrations, every branch of the table reached, and
> every empty set returns UNEVALUATED); `docs/synthetic-af-bank-followups-wave17-results.md` §3.

**Status:** **CLOSED 2026-08-10 (wave 17)** — cause identified at zero compute and confirmed by a 39-minute
intervention with both ends of the bar observed · found 2026-08-10 (wave 15) when RULE L15's gate **G-c**
returned TOOK on 13 of 20 · **the test that settles it was free, already on disk, and predated the control by two
waves**

Wave 15 needed to prove that a converted **landing** (`optimized_settings.json`, lifted into a harness settings
file through the production Accept path) actually reached the detector, because
`af-fit --settings <landing>` fails **silently** — a landing has no `Options` member, so it deserialises to an
empty bag, uses code defaults, and `HarnessSettingsStore.Fingerprint` appends nothing, so **two different landings
of one dataset hash identically.** *An instrument that is not connected reports perfect agreement.*

The control chosen was free and already printed: `optimize --per-run` writes `optimize_result.csv` with
`currentStarCount` and `optimizedStarCount` per frame; `af-fit` writes `af_fit_points.csv` with its own `Stars`
per focuser position. **They were declared "the same number at the same settings" on the strength of one exact
9-of-9 match on `D18_m24_deep_shed`.** The control was then demonstrated in **both** directions on that same
dataset — TOOK on a converted file, DID-NOT-TAKE on a mutant with `UseOptimizedSettings` flipped back — and
quoted.

### They are two pipelines

| | `optimize_result.csv` `optimizedStarCount` | `af_fit_points.csv` `Stars` |
|---|---|---|
| producer | `RunEvaluationData.EvaluateAndFitAsync(BestParams).Metrics.FrameStarCounts[i]` = `FrameDetectionResult.StarCount` (`RunEvaluationData.cs:675`) | `img.DetectAsync(detector, baseParams).DetectedStars.Count` (`AfFitDiagnosticRunner.cs:173`) |
| detector entry | `HocusFocusStarDetection.BuildDetectionContext` + `GateAndMeasure` via `HocusFocusSplitFrameDetector` (`RunEvaluationLoader.cs:297`) | `StarDetector` directly, on an `IRenderedImage` from `DetectionSource.LoadAsync` |
| params | the `StarDetectorParams` the search produced | `BuildStarDetectorParams(options)`, rebuilt from `StarDetectionOptions`, then `ModelPSF = false` |

**The disagreement is a fixed property of the dataset and has nothing to do with any converter.** Wave 13 ran
`af-fit` on all 20 synthetic datasets at exactly `pinned_settings_w11.json` — the same settings whose evaluation
wave 15's `optimize` records as `currentStarCount`. Scoring one against the other:

| | datasets |
|---|---|
| wave-13 `af-fit` ≠ wave-15 `currentStarCount` at the **same pinned base**, **no converter present** | `D08`, `D09`, `D10`, `D12`, `D14`, `D15`, `D17` |
| wave-15 G-c non-TOOK, **arm 0** | `D08`, `D09`, `D10`, `D12`, `D14`, `D15`, `D17` |
| wave-15 G-c non-TOOK, **arm 1** | `D08`, `D09`, `D10`, `D12`, `D14`, `D15`, `D17` |

> **The three sets are EQUAL.** G-c is not measuring whether the conversion took; it is measuring whether the two
> pipelines agree on a given dataset, and they disagree on **7 of 20**. `af-fit` finds systematically **fewer**
> stars, worst at the sweep wings and near parity at focus — `D08` at the base runs 0.46–0.52 of `optimize`'s
> count at the extremes against 0.82 at focus.

### And "could not look" fires when the settings WORK

All four `COULD-NOT-LOOK` reads are *"focuser positions do not line up: af-fit 7, current 9, optimized 9,
common 7."* `AfFitDiagnosticRunner` drops a position when it yields ≤ 1 star (`AfFitDiagnosticRunner.cs:175`), and
the log names it: `pos 20564: 0 stars (skipped - Y would be 0)`. **In all four cases the dropped positions are
exactly the two sweep extremes** (`D12` `{19436, 20564}` from `[19436..20564]`; `D14`/`D17` `{11760, 12240}` from
`[11760..12240]`) — the most defocused frames, starved by an aggressive landing.

**So on the datasets where the landing's settings bite hardest, the control's answer is that it could not look.**
The "could not look" state was built (waves 12/13) so an empty thing never reads as agreement, and it did that job
— nothing was misread as a pass. But it was designed for *"the artifact is missing"* and it fires for *"the
artifact changed in the way the arm was testing for."*

### The control that DOES transfer was already in every log

`HarnessSettingsStore.SimpleModePresetOverrides` hands the file's own option bag to a real `StarDetectionOptions`
and diffs the bag afterwards, so its `UseAdvanced=False` WARNING prints **the value the live options object holds
after `InitializeOptions`** — measured, never listed, and printed on every `af-fit` run already.

> **205 of 205 overridden knobs equal the landing snapshot's value, across all 40 runs (20 datasets × 2 arms),
> including all seven G-c could not certify.** Zero mismatches, zero unreadable.

**And it falsifies DID-NOT-TAKE decisively.** `DerivePresetSettings()`'s inputs are `Simple_NoiseLevel`,
`Simple_PixelScale`, `Simple_FocusRange` and the pixel-scale pair, and **all 40 converted files carry one
identical `Simple_*` tuple**. Under *"the file was written and ignored"* all 40 runs would print the **same**
preset values; they print **39 distinct sets** (the only collision is `D13`'s two arms, whose landings are
identical), each equal to its own landing. Consistently, `DID-NOT-TAKE` occurred on **0 of 40** runs.

### Why it matters

- **It cost a wave its verdict.** RULE L15 returns **NO VERDICT** on a gate that is not testing what its name
  says. The rule was applied as written and the diagnostics are published as diagnostics — but the out-of-sample
  half of the wave, the half built to answer [F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)
  at the landing level, is gated.
- **Any future control on either count inherits this.** Neither number is wrong; they are different measurements
  with the same name, and nothing in either artifact says so.
- **Demonstrating a gate in both directions is necessary and not sufficient.** [F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two)(a)
  was promoted to a wave rule and honoured: the control was shown to PASS and to FAIL before it was quoted. It was
  shown on **one dataset out of twenty**, and the axis it varied was not the axis that decides its answer.

> ### WAVE 16: (a) AND (b) ARE DONE — **RULE P16 returns P-b**, and the two counts are confirmed to be DIFFERENT MEASUREMENTS WITH THE SAME NAME
>
> Both sides' **full** `StarDetectorParams` were dumped through **one reflective formatter** shared by
> `AfFitDiagnosticRunner` and `OptimizationDiagnosticRunner` (`TestApp/ParamsDump.cs`, commit `4fd613d`) —
> reflective rather than a hand-written field list, because a hand list would reproduce the very defect being
> measured, two printouts that drift apart. **Printing one side's params is not a diff**, which is why (b) as
> originally written could not have closed this: `optimize` printed **5** fields against what turns out to be a
> **55**-property object.
>
> | clause | pre-registered threshold | measured |
> |---|---|---|
> | coverage | both sides dumped | `af-fit` **39 of 39** logs, `optimize` **10 of 10** (8 gate + 2 probe), could-not-look **0** on both |
> | **P16-POP** | ≥ 1 dataset from the **disagreeing** set **and** ≥ 1 from the agreeing set | **`D12`, `D17`** (disagreeing) + **`D18`, `D19`, `D20`** (agreeing) — **met** |
> | PER-RUN | any field varying across runs is reported and never generalised over | **none vary**, on either side |
> | **P16-a** | one **F-live** difference ⇒ P-a | **0 F-live differences on all five datasets.** 55 fields per side, **53 byte-identical** |
> | the 2 that differ | must be F-inert-by-proof or be promoted | `PixelScale` (`1` / `NaN`) and `SuppressInfoLogging` (`False` / `True`) — **both inert by proof** |
> | conditionals | `Region` / `MeasurementAverage` / `ModelPSF` inert only while a **checked** condition holds | Full / Median / False on both sides — all three hold, nothing promoted |
>
> > **RULE P16: P-b.** The detectors are configured **identically** from one settings file and the counts still
> > differ. **So the disagreement is DOWNSTREAM of the params**, and (c) is narrowed from three candidates to
> > **two, by construction and not by hypothesis**: (1) the **frame loading** (`DetectionSource` /
> > `DiagnosticUtil.LoadRenderedImage` vs `RunEvaluationLoader`), (2) the **counting/gating stage**
> > (`StarDetectorResult.DetectedStars.Count`, **pre**-filter, vs `HocusFocusStarDetectionResult.DetectedStars`,
> > **post** ROI-crop and post-`MeanOutliers`, `HocusFocusStarDetection.cs:759`).
>
> **This is a PRODUCT finding, and the register records which number the user gets.** The
> `P16-INSTRUMENT-vs-PRODUCT` test was decided in advance: an instrument artifact requires an F-live difference
> that exists only because the harness configures `af-fit`, and **there are no F-live differences at all**. Both
> surviving candidates are production code — `HocusFocusStarDetection` is what the running app calls on every
> autofocus frame, and `RunEvaluationLoader` is the optimizer's. **The app reports the post-ROI,
> post-`MeanOutliers` count; `af_fit_points.csv`'s `Stars` — quoted in fourteen waves — is the pre-filter one.**
> Neither is wrong and nothing in either artifact says which definition it is using.
>
> **(a) is also done and it reproduces exactly**: wave 15's G-c re-scored on the knob-diff control gives
> **40 runs read, 205 overridden knobs, 0 could-not-look**, with 20 + 20 distinct preset sets. *It is a re-score
> of a previous wave's data and can never be quoted as new evidence.*
>
> **Two things worth carrying from the instrument itself.** The scorer's `Region` predicate, as pre-registered,
> matched **any** region with a null inner crop — true of a genuinely **cropped** outer boundary — so a cropped
> region would have been excused as inert instead of promoted to F-live. Fixed before any P16 verdict was read;
> it does not change this verdict (both sides are Full) and that is exactly why it was safe to fix there. **A
> condition that cannot promote is a check that cannot fail** ([F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two)(a)).
> It matters beyond this wave because `af-fit`'s **primary** params path is `LoadOriginalDetectorParams` — the
> run's own saved detection JSON, which **can** carry a non-Full region. And `F_INERT_BY_PROOF` listed
> `ModelPSFPixelScaleOnly`, which **is not a property of the object**: a dead exclusion entry that can never
> exclude anything, F66's shape again.
>
> Reproduce: `python3 /mnt/d/hf_w16/score_params_w16.py --gate /mnt/d/hf_w16/gate --probe /mnt/d/hf_w16/probe
> --affit /mnt/d/hf_w16/affit_N --out /mnt/d/hf_w16/p16_score.txt`;
> `python3 /mnt/d/hf_w16/score_params_w16.py --self-test` (13 demonstrations, every clause in both directions);
> `python3 /mnt/d/hf_w16/score_params_w16.py --gc-rescore /mnt/d/hf_w15`;
> `docs/synthetic-af-bank-followups-wave16-results.md` §3.

### Next step

(a) **DONE (wave 16)** — G-c re-scored on the knob-diff control: 40 runs, 205 knobs, 0 could-not-look.
(b) **DONE (wave 16), and the framing was wrong** — one side's params is not a diff. Both sides now dump all 55
fields through one shared reflective formatter; verdict **P-b**.
(c) **DONE (wave 17) — the cause is `DetectionBinning`, and it is NEITHER of P16's two candidates.** Both were
dead before wave 17 ran a command: candidate 1 names `RunEvaluationLoader`, the **wizard's** loader, which
`TestApp optimize` never calls (it calls the same `DiagnosticUtil.LoadRenderedImage` as `af-fit`); candidate 2 is
arithmetically impossible in the observed direction, because the post-filter set is a **subset** and the
measurement had the pre-filter side smaller — *a subset cannot be larger than its superset*. The defocus-graded
deficit this entry recorded as *"a hypothesis, not a result"* is now explained exactly: `StarDetector.cs:494`
resamples by `p.DetectionBinning`, so at factor 2 the gates see 4× the flux per binned pixel and admit fainter
stars, which is largest at the wings where the star is faint and spread.
**Nothing further is owed.** What remains is a *different* question, priced in
`docs/synthetic-af-bank-followups-wave17-results.md` §8: the seven datasets' published landings were produced at
factor 2 while thirteen waves of `af-fit` evidence about them was produced at factor 1, and comparing them needs
an out-of-sample arbiter at **both** factors (~30 m of `af-fit` plus a rule). No `BestJ` may be quoted across the
two factors ([F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)).
Reproduce: `python3 /mnt/d/hf_w15/score_land_w15.py --arm0 /mnt/d/hf_w15/land_mor0 --arm1 /mnt/d/hf_w15/land_mor1
--score0 /mnt/d/hf_w15/affit_mor0 --score1 /mnt/d/hf_w15/affit_mor1 --w13 /mnt/d/hf_w13 --gate /mnt/d/hf_w15/gate
--out /mnt/d/hf_w15/l15_score.txt` (G-c's four states, by name); the base-level test is
`/mnt/d/hf_w13/affit_syn/<D>/af_fit_points.csv` against `/mnt/d/hf_w15/land_mor0/<D>/attempt01/optimize_result.csv`
column `currentStarCount`; the transferring control is the `Overwritten by the presets:` line in every
`/mnt/d/hf_w15/affit_mor{0,1}/<D>/run.log`; `docs/synthetic-af-bank-followups-wave15-results.md` §3.2.

### F66 — Three of wave 14's checks could not return their own PASS, and the register has been reading `strings` as one instrument when it is two
**Status:** Open (a standing discipline entry) · found 2026-08-09 (wave 14), three of them in one wave, by
running each check against the state it was meant to **accept**

Every check below failed **closed** — the safe direction, and the one wave 12/13's "could not look" discipline
was built to produce. None of them could ever have said PASS.

| check | what it asserts | why its PASS branch is unreachable |
|---|---|---|
| `prov_w14.py`'s profile pin **(REPAIRED, wave 14)** | `ProfileId == "ce3f3e63-…"` | the landing records `astrodet (ce3f3e63-…)` — **name and GUID**. Unequal for every landing that can exist. Reported `RULE G14 free controls: **FAIL**` on a passing gate |
| `score_affit_w14.py`'s **V3** | `<stagea>/stageA_report.txt` exists | `stageA_w14.py` writes `rounds.tsv`, `predict.tsv`, `sem_audit.tsv` and prints its gate to **stdout**. The file is never produced |
| `score_affit_w14.py`'s **V4** | a production row equals one of wave 13's printed rows | plain tuple equality on rows containing `NaN`, so a row **fails to match itself**. `Panos`'s budget-2/3 σ_focus is the string `"NaN"`. The scorer's own `eq()` handles this and V4 does not call it |

**And the oldest instance is a probe, not a scorer.** `strings` scans for runs of ASCII bytes. A .NET assembly
stores **type and member names** in the `#Strings` heap as UTF-8 — findable — and **string literals** in the
`#US` heap as **UTF-16**, where every character is followed by a null and no ASCII run exists.

```
strings    D:\hf_w13\exe\TestApp.dll | grep -c -- '--profile-id'   ->   0   <-- on a binary that ACCEPTS the flag
strings -el D:\hf_w13\exe\TestApp.dll | grep -c -- '--profile-id'  ->  28
```

Wave 13's provenance probe (`grep AtrousWaveletFast`) works **because `AtrousWaveletFast` is a type name**.
`--profile-id` is a literal (`DiagnosticUtil.GetArg(args, "--profile-id")`). **Two probes, two heaps, and the
register has been treating them as one instrument.**

**And the type-name probe does not discriminate either — which is the fourth instance and the one that cost a
whole arm.** `strings <plugin>.dll | grep -c AtrousWaveletFast` returns **1** on `D:\hf_w10\exe`,
`D:\hf_w10\exe_v1wav` *and* `D:\hf_w14\exe`. A type is compiled into the assembly wherever it exists in source,
including a build that selects a different implementation at run time, so **this probe reports "v2" for every
binary this project has produced.** Wave 13's provenance line cites it beside the `DetectorVersion` FIELD; the
field carried the claim and the probe was decoration. Wave 14's RULE W14-D then leaned on it to assert that two
wave-10 binaries were wavelet variants of one tree — and their own landings say otherwise
([F57](#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it)(d)).
*A probe that returns the same answer for every input is not evidence, whichever direction it points.*

### Why it matters

- **It cost an item four waves.** [F57](#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it)(d)'s
  landing-level wavelet bisect was deferred as *"not constructible — `exe_v1wav` cannot be pinned"*, on this
  probe returning 0. The current binary returns 0 too. **A test that reports "absent" for a binary known to have
  the flag is not measuring presence**, and the control that catches it is one command.
- **Failing closed is better and it is still broken.** A control that always says FAIL trains its reader to skip
  it, which is how a real failure gets through. Wave 13's `RX_BUDGET` defect printed **"PASS — 0 of 0 rounds
  reproduced"** and failed *open*; that is worse, and both are the same omission.
- **Smoke-running demonstrates one branch.** Wave 14's scorers were smoke-run during authoring and that found
  two real defects. It exercised the failure path.

### Next step

(a) **Every gate must be demonstrated to PASS on a known-good input and to FAIL on a known-bad one before it is
quoted.** Wave 13's `stageA_w14.py` already carries the pattern — a self-test that must *flip* the verdict.
(b) **When a probe reports absence, run it against a known positive.** For .NET binaries: `strings -el` for
literals, plain `strings` for type names, and say which you are looking for.
(c) **ADDED 2026-08-11 (wave 18): a two-directional demonstration proves the branches are DISTINGUISHABLE, not
that they are CORRECTLY ASSIGNED.** Wave 18's conversion control honoured (a) completely — two settings files
differing in one byte, the mutation asserted by read-back, two real `af-fit` runs, a demonstrated TOOK and a
demonstrated DID-NOT-TAKE — and **both verdicts came out on the wrong inputs**, because both directions are
evaluated by the same definition and the definition named the wrong field
([F71](#f71--a-converted-settings-file-carries-every-detector-knob-twice-and-the-control-that-checked-the-conversion-read-the-copy-the-loader-ignores)).
It was caught only because the labels were **swapped**; a definition error moving both verdicts the same way
would have read as a clean PASS. **The cheap addition: when a control reduces a property to ONE field,
corroborate with a second field that must move the same way.** Wave 18's probe had **seven** such fields in the
same dump, all agreeing with each other and disagreeing with the verdict — **at zero compute, in files the
control had already read**. **~5 m per control**, and it is the half that generalises past any one clause.
**One of the three was repaired in the same wave, and the repair was then demonstrated in BOTH directions** —
`prov_w14.py` now tests the pin by **containment** of the GUID plus a separate "exactly one distinct `ProfileId`
across the arm" clause, and it was shown to **PASS** on the real 8-landing gate and to **FAIL** on a copy with
one landing's `ProfileId` rewritten to `Default (b10b1d6d-…)`. *The first attempt at that demonstration wrote its
mutation to a path that does not exist, so nothing was mutated and the scorer reported PASS on an unmodified
copy — which looks exactly like a control that cannot discriminate. A test that a control can fail is itself a
check that can silently not run.* The other two are left as-is and named in §7.3's costed list, because
repairing V4 after it failed is the one repair this wave must not make.

Reproduce: `python3 /mnt/d/hf_w14/prov_w14.py /mnt/d/hf_w14/gate` (PASS since the repair; it exited 1 on this
same passing arm before it);
`python3 /mnt/d/hf_w14/score_affit_w14.py --root /mnt/d/hf_w14 --w13 /mnt/d/hf_w13 --stagea /mnt/d/hf_w14/stageA`
(V3 ABSENT, V4 four rows of which two are the NaN artifact); `docs/synthetic-af-bank-followups-wave14-results.md`
§1.1, §1.3, §3.4, §3.5.

> ### WAVE 15 — (a) was promoted to a wave rule and it worked; and here is the shape it does not cover
>
> **The rule held.** Every wave-15 instrument carried a `--self-test` that was run before its numbers were quoted:
> `prov_w15.py` PASSED on the real 8-landing gate and FAILED on both clauses against a copy with one landing's
> `ProfileId` rewritten — **and it refused to certify itself when pointed at wave 14's gate**, because that
> `BuildId` is on the prior-wave list. *A self-test bound to the arm it certifies is the point; one that passes
> against any input certifies nothing.* `score_f14_w15.py`, `score_land_w15.py` and `convert_landing_w15.py`
> each returned **every** one of their outcomes on constructed input, including every refusal.
>
> **And a clause still shipped with an unreachable branch, inside the section written to prevent exactly that.**
> RULE L15's **G-d** asserts *"the two converted files differ **iff** the landings differ."* The converter embeds
> the landing's **raw text**, and every landing carries `CreatedAtUtc` plus **F30**'s
> provenance stamp — which records the arm's own `--out` path, its own `--settings` path, that file's fingerprint
> and its `FitInputs`, all of which *must* differ between arms. **So "the converted files are identical" is
> unreachable, and G-d must fire on every dataset whose landing does not move.** It fired on
> `D13_apo200_1800mm`, whose two landings agree on all 25 curated knobs, on `RecommendedStepSize`, and on
> `BaselineJ`/`FinalJ` to all sixteen digits. The wave's satisfiability table listed G-d as *"20 pairs |
> consistent | yes"*: it checked that the PASS value was attainable and never that both branches were.
>
> *This is not the F66 shape (a check that can never say PASS) — G-d says PASS on 19 of 20. It is the adjacent
> one: a check with **one input class** whose correct answer it cannot give.* **Ask of every clause what input
> makes it return EACH of its outcomes, not only the expected one.** The correct G-d compares the converted files'
> `OptimizedSettingsJson` **outside `CreatedAtUtc` and `Provenance`**; on that clause wave 15 passes 20 of 20.
> Reproduce: `/mnt/d/hf_w15/l15_score.txt` (G-d's one problem, named);
> `docs/synthetic-af-bank-followups-wave15-results.md` §3.3, and §3.2 for
> [F67](#f67--af-fits-star-count-and-optimizes-are-not-the-same-number-so-the-control-built-on-their-equality-reports-could-not-look-on-exactly-the-datasets-where-the-intervention-bites-hardest),
> the wave's other confounded control.

> ### THE APPHOST COUNTER-EXAMPLE IS NOW EIGHT DEEP (2026-08-13, wave 25)
>
> `/mnt/d/hf_w24/exe/TestApp.exe` and `/mnt/d/hf_w25/exe/TestApp.exe` are **byte-identical**
> (`dd7103c28cc610e72671534cf23fb9a58b5a303a19d8779545cb7418d4ce6ff7`) across two binaries whose `TestApp.dll`
> differs (`5398582d…` vs `2fb0c8fd…`) and whose `BuildId` differs (`dd6ca32e…` vs `d79dae73…`). **The apphost is
> a launcher stub; it does not carry the managed code.** Eighth instance. **Binary identity in this series is the
> dll sha256 PAIR, never the `.exe`** — and wave 25's `--self-test` demonstrated the check in **both** directions
> on real artifacts before it was quoted, per this entry's own rule: the real arm PASSes, a copy with one
> landing's `ProfileId` rewritten FAILs on **both** the pin clause and the cardinality clause.
> Reproduce: `/mnt/d/hf_w25/binary_provenance_w25.txt` vs `/mnt/d/hf_w24/binary_provenance_w24.txt`;
> `/mnt/d/hf_w25/prov_w25.txt`.

### F65 — The Hybrid consensus is an INTERSECTION over four models that can reject the same points in a DIFFERENT ORDER, so the rejected set at budget B is not a prefix of anything
**Status:** Open · found 2026-08-09 (wave 14) when RULE F14's V4 failed on `Panos_attempt01` · **the mechanism
is printed in wave 13's own artifact and had been read three times for other purposes**

`AlglibHyperbolicFitting.SelectBestModel` runs the Grubbs cascade **per candidate model** and removes only the
**intersection** across all four — *"a point only some models reject is model-misfit (tilt signal), not a true
outlier; consensus keeps it."* Wave 13's `af_fit_summary.txt` for `Panos_attempt01` prints the orders:

```
model            | would-reject          |  budget | #rej | rejected
Symmetric        | 38396 40396           |    0    |  0   | (none)
UnevenBlend      | 38396 40396           |    1    |  0   | (none)     <-- the models disagree on round 1
TiltedHyperbola  | 40396 38396   <--     |    2    |  2   | 38396 40396
SmoothBlend      | 38396 40396           |    3    |  2   | 38396 40396
```

**The budget-1 consensus is EMPTY because the four models pick different first points; the budget-2 consensus is
BOTH because they agree on the pair.** So the sequence of consensus sets is `{} , {} , {38396, 40396}` — it
jumps by two, and no budget of this run ever produces a one-element consensus.

**What made it visible.** Wave 14 measured a round-2 scale floor (family B, `α = 1.00`). It suppresses
`TiltedHyperbola`'s second round only, leaving that model at `{40396}` while the other three keep both — and the
intersection becomes **`{40396}`, a set no budget of the unfloored run produces**:

```
budget | winner    | #rej | rejected | sigma(focus) | redChi^2 | R^2      | minPos
  2    | Symmetric |   1  | 40396    |   139.097    | 0.12256  | 0.998856 | 33276.8
```

### Why it matters

- **A per-model prefix property does NOT lift to the consensus.** Each model's floored rejection set really is a
  prefix of its unfloored set, and `consensus^f_B ⊆ consensus_B` really does hold (**585 of 585** (run, rung,
  budget) triples in wave 14). But **the intersection of prefixes is not a prefix of the intersection** when the
  orders differ, so "the outcome must be one of the printed budget rows" is false. Wave 14's V4 asserted the
  false version, failed, and returned **NO VERDICT** on an item whose measurement was otherwise complete.
- **Any counterfactual over the rejection cascade must be phrased over the consensus, not over the printed
  trace.** `af-fit` prints the per-round detail for **one** model (the budget-0 winner) and the production table
  for **all four**. Those are different objects, and the register now has a case where reading one as the other
  changed a verdict.
- **It is also a statement about the shipped fit.** Raising `MaxOutlierRejections` by one does not necessarily
  remove one more point: on `Panos` it removes two, and there exist reachable consensus sets that no budget
  reaches. Anyone reasoning about "budget N means at most N rejections" should read that as an upper bound on
  each model, not a description of the consensus trajectory.

### Next step

(a) **Repair RULE F14's V4** to test what the design proved — `consensus^f_B ⊆ consensus_B` plus a cardinality
bound — and to compare rows NaN-safely, then re-score wave 14's six rungs. **Cost: minutes of Python, zero
`TestApp` time**; all six rungs are on disk.
(b) **Print the per-model rejection ORDER in `af_fit_summary.txt`'s consensus block**, or at minimum the
per-model round-by-round scales, so a future counterfactual does not have to infer four models from one trace.
Reproduce: `/mnt/d/hf_w13/affit_real/Panos_attempt01/af_fit_summary.txt` (the per-model `would-reject` column),
`/mnt/d/hf_w14/affit_B1.00/real/Panos_attempt01/af_fit_summary.txt`,
`docs/synthetic-af-bank-followups-wave14-results.md` §3.5.

### F63 — The optimizer's LANDING moves on 6 of 8 runs under a knob that is nearly inert at the seed, so every landing waves 5-12 published was produced at a NON-DEFAULT value
**Status:** Open · found 2026-08-09 (wave 13) by RULE M13's D5 — **the clause that asked whether the
RECOMMENDATION moves, which is the one question nobody had asked**

`MaxOutlierRejections` decides whether the AF fit may drop one Grubbs outlier. Wave 13 measured it four ways and
three of them said "inert or nearly so": the out-of-sample vertex error is unmoved on 16 of 20 synthetic
datasets (D1), `bank-verify`'s run-level AF fit is unmoved on 19 of 19 (D2), and the seed evaluation moves on
8 of 39 population runs (D4).

**Then D5 asked a different question and got a different answer.** Re-running the eight-run gate at
`MaxOutlierRejections` = 1 against the gate's own `= 0` landings, at `--max-evals 250`:

| run | landing | `RecommendedStepSize` | `BrightnessSensitivity` |
|---|---|---|---|
| `CWhiteFocus` | **moved** | **101 → 118** | 49.875 → 50.0 |
| `D18_m24_deep_shed` | **moved** | 18 → 17 | **14.67 → 32.83** |
| `D19_cygnus_deep_shed` | **moved** | 35 → 35 | |
| `D20_m24_bright_control` | **moved** | | |
| `mccomiskey` | **moved** | 31 → 32 | |
| `toml999` | **moved** | **16 → 19** | **16.67 → 33.33** |
| `muggsie` | *(none)* | 550 → 550 | |
| `uneven` | *(none)* | 488 → 488 | |

**6 of 8 — and the SEED evaluation moved on exactly ONE of those eight** (`toml999`). A search follows `J`, and
`J` shifts wherever the rejection fires **anywhere in the explored space**, not merely at the starting point.
**The search amplifies a knob that is nearly inert on any single fit.**

### Why it matters

- **The recommended AF step size changes by up to 17 %** (`CWhiteFocus` 101 → 118) and the brightness
  sensitivity by a **factor of two** on two runs. These are the numbers the wizard hands the user.
- **Every landing waves 5–12 published was produced at `MaxOutlierRejections` = 0**, because that is `astrodet`'s
  value and `astrodet` is the pinned profile — and the SHIPPED default is **1**
  (`AutoFocusOptions.cs:62`). At the shipped default the optimizer would have recommended different settings on
  6 of these 8 runs.
- **"Moved" is NOT "worse".** `BestJ` is computed by the fit under test, so neither landing can be called
  better, and wave 13's scorer deliberately refuses to print a cross-arm `BestJ` delta so nobody quotes one.
  What is established is dependence, not direction.

### Next step

(a) **Extend D5 to the 39-run population** — priced at ~3 h sequentially (~2 ¼ h fanned out, [F60](#f60--fan-out-is-now-safe-and-it-is-barely-worth-doing-optimize-already-saturates-the-machine)); it is the
most interesting thing wave 13 left undone, because D5 is the clause that fired.
(b) **Decide whether the harness should pin the SHIPPED default rather than `astrodet`'s.** That moves the
coordinate system RULE G13 has now reproduced four times, so it needs a new baseline and is a decision, not a
cleanup — the same price as [F59](#f59--the-settings-export-drops-every-knob-whose-setter-validates-so-pinned_settingsjson-has-been-missing-five-detector-knobs-since-wave-5)'s five knobs.
Reproduce: `D:\hf_w13\land_w13.sh`, `D:\hf_w13\land_score.txt`.

> **WAVE 14 — this entry is now the user-visible cost of a SHIPPED change, and (b) is cheaper than it was.**
> The code default flipped **1 → 0** (`AutoFocusOptions.cs:62` and `:81`), as an owner's product-coherence
> decision that **overrides** wave 13's RULE M13 — whose D1 did not fire and whose pre-registered outcome was NO
> CHANGE. Profiles that store the key keep their value; the fallback is what moved.
>
> **Measured reach on this machine's nine profiles:** the partition flips from **2 at 0 / 7 at 1** under the old
> default to **6 / 3** under the new one — and the parse reproduces
> [F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections)'s
> published 2 / 7 exactly under the old default, which is a free control on the parse before either number is
> quoted. **Five profiles set the key explicitly and do not move** (`astrodet` 0, `AA1600MM Copy` 0, `Default` 1,
> `AA1600MM` 1, `40mm` 1); **four have it absent and therefore move**, all `Default-*` snapshots.
>
> **So this entry's "6 of 8 landings move" is what those four profiles just did**: every profile with the key
> absent silently changes what the wizard recommends, by up to **17 %** on the AF step size and a **factor of
> two** on brightness sensitivity. That belongs in the PR body, and *"moved is not worse"* still holds — `BestJ`
> is not comparable across the two values and this wave did not compute one.
>
> **(b) is re-priced downward:** after the change the shipped default **is** `astrodet`'s value, so pinning the
> shipped default no longer moves the coordinate system RULE G14 has now reproduced a **fifth** time. It should
> be re-priced properly next wave rather than carried at F59's price.
> Reproduce: `/mnt/d/hf_w14/profiles_after_default_change_w14.txt`,
> `docs/synthetic-af-bank-followups-wave14-results.md` §2.3.

> ### WAVE 15 — (a) IS ANSWERED AT POPULATION SCALE: **19 of 20**, and the search noise is measured at ZERO
>
> RULE L15's clause **L15-S** re-ran D5's question on the **20 synthetic datasets**, both values pinned
> explicitly, paired-interleaved, one binary, `--max-evals 250`, on D5's own definition of "moved" (any curated
> knob, or the recommended step):
>
> > **`M` = 19 of 20.** One unmoved (`D13_apo200_1800mm`), 0 UNEVALUATED by name. D5 measured 6 of 8 on a
> > different, mostly-real population.
>
> The movements are large in knob terms: `D18` moves **11 fields** (`BrightnessSensitivity` 14.67 → 32.83,
> `StarClippingMultiplier` 0.25 → 6.25); `D01` and `D14` move 10; `D07` moves `BrightnessSensitivity` 8.0 → 0.0
> and `StarClippingMultiplier` 2.0 → 7.125. The smallest mover is `D05` at 3 fields.
>
> **And "the search is just noisy" is now excluded rather than assumed.** Clause **G-e** required arm 0's
> `D18/D19/D20` landings to reproduce the same wave's RULE G15 landings — same binary, same command, same settings
> file. They do, **3 of 3**, and totally: each pair differs in `CreatedAtUtc` and F30's provenance stamp **and in
> nothing else**, including `BaselineJ` and `FinalJ` to all sixteen digits. *At `--max-evals 250` on this bank,
> two runs at identical settings land bit-identically, so `M` = 19 is attributable to the knob and is not an upper
> bound inflated by search noise.* Three is a small control and it is stated as three.
>
> **`D13` is the one dataset where the knob is inert end-to-end**: both arms produced identical `BaselineJ` **and**
> identical `FinalJ`, so a 250-evaluation search never once visited a point where the rejection changed `J`.
>
> ### And *"moved is not worse"* is now **asked** out of sample — and the answer is gated
>
> **L15-P** scored each arm's landing against `renderRequest.OptimalFocuserPosition` at **budget 0 fixed for both
> arms**, so the fit machinery is identical and the detector is the only difference — the first time in this
> series that a landing has been scored against truth at all. Over the 19 movers: **arm 0 wins 7, arm 1 wins 7,
> 5 exact ties**; median `|Δe|` **0.00167 step**, max **0.02584**, against a pre-registered **0.10-step**
> materiality floor and a direction clause needing **≥ 15** wins with 0 against. **NO DOMINANCE.**
>
> **This is reported as a DIAGNOSTIC, not a result.** RULE L15 returned **NO VERDICT**: validity gate **G-c** —
> *"the conversion took"* — passed on only 13 of 20 per arm. Wave 15 §3.2 shows G-c is confounded (it returns
> non-TOOK on exactly the 7 datasets where `af-fit` and `optimize` already disagreed at the pinned base two waves
> earlier, with no converter present) and that an independent control puts the landing's knobs on the live options
> object on **40 of 40** runs — see [F67](#f67--af-fits-star-count-and-optimizes-are-not-the-same-number-so-the-control-built-on-their-equality-reports-could-not-look-on-exactly-the-datasets-where-the-intervention-bites-hardest).
> **The gate is the gate.** The out-of-sample half of the question stays open, and the register must not quote
> 7–7 as a measured null.
>
> **The 19-of-20 count does NOT depend on that gate.** It is read from the landing files; G-c gates only the
> out-of-sample scoring pass.
>
> **(b) is now priced at ~0** — after wave 14 the shipped default **is** `astrodet`'s value, and wave 15 pinned
> both values explicitly on every arm, so nothing is left to move. It should close next wave.
> Reproduce: `/mnt/d/hf_w15/land_w15.sh`, `/mnt/d/hf_w15/l15_score.txt` (L15-S's per-dataset table, G-e, and the
> NO VERDICT with its failing gate named), `docs/synthetic-af-bank-followups-wave15-results.md` §3.4–§3.6, §3.8.

> ### (b) CLOSES 2026-08-10 (wave 17) AS A COSTED RECOMMENDATION: **DO NOT RE-PIN.** The offset is ONE FIELD, and it cost zero minutes of compute to measure
>
> **(b) split in two and only half was ever cheap.** The **fit inputs** half is a no-op: all four values in
> `pinned_settings_w11.json` equal the shipped code defaults. The **detector knobs** half was the open one — the
> file was exported from `Default-2026-08-05T10:54:36` (**not `astrodet`**; the register's old shorthand was
> wrong) with `UseAdvanced=False` and `Simple_*` all `Typical`, so its advanced knobs are **preset-derived**, and
> nobody had measured how far that is from the shipped default. **The measurement was already printed**: since
> wave 16 every `optimize` log carries `PARAMS-DUMP optimize/seed` — `BuildDefaultStarDetectorParams()`, the
> shipped code defaults — beside `PARAMS-DUMP optimize/baseline`, the pinned file's bundle.
>
> **RULE D17, over the 50 fields `ApplyAfContext` does not set (the other 5 are equal by construction, and a
> check that cannot fail is not a check):**
>
> | clause | pre-registered threshold | measured |
> |---|---|---|
> | **D17-1** | report the **union** of differing field names with both values, per-log counts printed beside it | union size **1**, the same field on each of the 8 logs |
> | **D17-2** *domain* | 50 fields compared, 55 parsed, per log | 8 of 8 logs, **0 could-not-look** |
> | **D17-3** *reproduction control* | the same union on wave 16's 8 gate + 2 probe logs | 10 of 10 read, **SAME SET** |
> | **D17-4** *the `UseAdvanced` trap* | the override warning present, `N` reported | **N = 0 on every log** |
>
> > **`D-SMALL`. The whole offset is `NoiseReductionRadius`: shipped default 3, pinned file 4.**
>
> **DO NOT RE-PIN, and this was fixed before the data.** The eight-value coordinate system has reproduced across
> **eight binaries** and two settings files and is the most valuable instrument this series owns. Moving it costs
> a fresh **~42 m** baseline plus the re-derivation of every cross-wave comparison, and buys an alignment no open
> question depends on. **Publishing the offset is the deliverable.**
>
> **And the offset is not the pinned file drifting — it is two shipped defaults**, which is
> [F70](#f70--noisereductionradius-has-two-shipped-defaults-and-the-drift-guard-test-asserts-the-one-path-where-they-agree)
> and is a *product* observation. D17-4's `N = 0` and D17-1's union of 1 look contradictory and are both correct:
> the presets compute **4** (Typical ⇒ 3, then a `+1` hotpixel compensation), so the file's recorded 4 is exactly
> what they produce, while the seed's 3 is the value one step **before** that compensation.
>
> Reproduce: `python3 /mnt/d/hf_w17/score_d17_w17.py --gate /mnt/d/hf_w17/gate --w16 /mnt/d/hf_w16 --affit
> /mnt/d/hf_w16/affit_N --out /mnt/d/hf_w17/d17_score.txt`; `python3 /mnt/d/hf_w17/score_d17_w17.py --self-test`
> (7 demonstrations; **both** ways of producing an empty union are distinguished, so an empty population can
> never read as `D-NOOP`); `docs/synthetic-af-bank-followups-wave17-results.md` §4.

### F62 — σ_focus is ANTI-INFORMATIVE when an outlier rejection is what changed it: it improves by up to 88 % while the distance to a known truth improves on none
**Status:** Open (a standing warning about the register's own favourite quantity) · found 2026-08-09 (wave 13)
by RULE M13's D1, whose arbiter was deliberately put out of sample

`RunEvaluationData` sets `SigmaFocus = bestFit.MinimumStdError` — the parametric standard error of the fitted
minimum, computed **on the points that SURVIVED the rejection**. So a rejection budget that is allowed to
discard its worst-fitting point is then graded on what remains. **It must improve. That is arithmetic, not
evidence.**

The synthetic bank can check it, because `renderRequest.OptimalFocuserPosition` is the generator's TRUE focus
and the sweep is symmetric about it. On the four datasets where the rejection fires:

| dataset | σ_focus 0 → 1 | Δσ | \|vertex − truth\| 0 → 1 | truth says |
|---|---|---|---|---|
| `D01_ultrawide_40mm` | 0.6144 → 0.5264 | **−14.3 %** | 0.00111 → 0.00111 | unchanged |
| `D08_c11_2800mm` | 0.7897 → 0.4971 | **−37.0 %** | 0.01829 → 0.01951 | **worse** |
| `D12_c14_585_afbin2` | 1.5717 → 1.7766 | +13.0 % | 0.00142 → 0.01135 | **worse** |
| `D16_esprit550_ha3` | 0.6378 → 0.4069 | **−36.2 %** | 0.00067 → 0.00200 | **worse** |

> **σ_focus improves on 3 of 4. The distance to truth improves on 0 of 4.** They agree on one dataset, and on
> `D08` and `D16` they point in OPPOSITE directions — the reported uncertainty falls by more than a third while
> the actual error grows. Over the 39-run population the same change reaches **−88.3 %** on `D17_cdk14_oiii5`.

### Why it matters beyond this knob

`J`, R² and reduced χ² have the same shape — every one is computed by the fit under test. **Any arm that scores
a REJECTION change on a within-fit statistic is measuring the arithmetic of its own denominator.** A wave that
had scored wave 13's item 1 on σ_focus would have concluded that allowing the rejection improves the fit by a
third, on data where it never once moved the answer closer to the truth.

**This does not retract σ_focus as an objective term.** It is a fine comparator when what changed is the
DETECTOR (the point set is then common to both arms). The failure is specific to changes that alter **which
points are fitted**.

> ### WAVE 14: THIS ENTRY WAS AUDITED AGAINST THE WHOLE REGISTER, AND TWO OF ITS OWN CLAUSES ARE WRONG
>
> The audit is [`docs/wave14-f62-audit.md`](wave14-f62-audit.md) — 12 hits over the register and waves 2–13:
> **2 INVALIDATED** ([F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections)'s
> *"allowing one Grubbs rejection improves a fit"*, [F48](#f48--the-executed-sweep-step-sizing-is-bimodal-6-cells-better-2-worse-and-the-median-hides-both)'s
> *"a real, structured signal"*), **3 WEAKENED** ([F61](#f61--f58ds-real-consumer-was-the-sensor-model-not-the-af-fit-the-per-star-paraboloids-rejection-budget-came-from-the-active-profile)'s
> `sChi`, [F45](#f45--the-grubbs-test-rejects-the-in-focus-point-of-a-near-perfect-curve-and-the-blind-walk-then-buys-an-extra-exposure)'s
> `caboose` magnitude, F48's 10.2 %), **7 SURVIVES**, **2 UNEVALUATED**. The prediction *"other conclusions may
> rest on this"* is confirmed, and two of the five were being cited the same wave in support of changing a
> shipped default.
>
> **(1) The DETECTOR carve-out is FALSIFIABLE, and the register falsifies it twice.** *"The point set is then
> common to both arms"* is true of the **stars** and not guaranteed of the **positions**: `JRun` applies a hard
> `NHard` floor per frame, so a detector knob that starves one frame removes a **focuser position** from the fit
> outright. [F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic)/[F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it)
> document exactly this for `MinHFR` (*"5 stars back on the vertex frame … clears the `NHard` = 3 stars-per-frame
> requirement"*), and wave 8 records `D17`'s `BaselineJ` going **0.000000 → 0.994830** under a *binning* change.
> **The test is not "was it the detector"; it is "did a frame or a star enter or leave the fit."** The most
> exposed ruling under the old wording is wave 4's `mccomiskey` shedder — 3606 → 43 detections across nine
> frames, where a frame crossing `NHard` is plausible and unrecorded.
>
> **(2) "It must improve" is NOT a theorem, and this entry's own table shows it.** `D12_c14_585_afbin2` above is
> **+13.0 %** — worse — at budget 1. σ_focus is `√(mgᵀ·s²·(JᵀWJ)⁻¹·mg)` with `s² = weighted RSS/(n−p)`, so
> dropping a point shrinks the residual sum **and** the degrees of freedom **and** the information matrix, and
> the last two can dominate. Read it as **biased to improve**, not obliged to.
>
> > **The corollary is asymmetric and is worth more than the correction: a within-fit statistic that DEGRADES
> > under a rejection is evidence; one that IMPROVES is not.** That is precisely why F45's `caboose` result is
> > readable — a bias cannot manufacture its own opposite — and why F58's *"it improves"* is not.
>
> **(3) The warning has a second consumer nobody named.** The sensor paraboloid weights by `1/σ²` where σ is the
> **per-star hyperbola's own `MinimumStdError`** (`SensorModel.cs:838-841` → `SensorParaboloidDataPoint.RegularizeStdDev`
> → `OutputStdDevs`), so `sChi` and `sR2` are contaminated by a per-star rejection budget through a route that is
> not the paraboloid's own point set — and F61's like-for-like control (hold `sStars` fixed) does not close it.
> See [F61](#f61--f58ds-real-consumer-was-the-sensor-model-not-the-af-fit-the-per-star-paraboloids-rejection-budget-came-from-the-active-profile).

### Next step

None required for the original finding. Recorded so the next arm over a rejection, a point-pruning rule, or any
change that alters the fitted point set puts its arbiter out of sample — on the synthetic bank, which has had
known focus positions since it was built and had never been scored against them.

**(a) Two hits are UNEVALUATED and each names the artifact that would close it** (audit §3): wave 12's exposure
ladder anchors (needs per-rung `HardFloorPassed`/`WorstFrameCount` for the 13 usable rungs), and wave 9's φ
verdict, whose **pre-registered out-of-sample clause R1(b) was never reported** in `wave9-results.md` — a
*permanent* default decision on a star-keeping constraint resting on `J` and σ_focus with its one external
clause silently dropped.
**(b) A standing line for the wave design template, from the audit's §6(h):** *say which class of change an arm
is, in the design, before it runs.* If the arm can move a point into or out of the fit, then σ_focus, `J`, R²,
reduced χ², `sChi` and `sR2` are **magnitudes, not votes**, and the design must name the out-of-sample arbiter
it will use instead. Wave 13's `score_pop_w13.py` is the reference implementation: the quantity that must not be
compared is **not computed**.
Reproduce: `D:\hf_w13\affit_syn_score.txt`, `D:\hf_w13\score_affit_w13.py`,
[`docs/wave14-f62-audit.md`](wave14-f62-audit.md).

> ### WAVE 15 — THE LANDING-LEVEL VERSION OF THIS QUESTION IS NOW **ASKED** WITH AN OUT-OF-SAMPLE ARBITER, AND THE ANSWER IS GATED
>
> [F63](#f63--the-optimizers-landing-moves-on-6-of-8-runs-under-a-knob-that-is-nearly-inert-at-the-seed-so-every-landing-waves-5-12-published-was-produced-at-a-non-default-value)'s
> *"moved is NOT worse — `BestJ` is computed by the fit under test, so neither landing can be called better"* is
> this entry at the **landing** level, and it had stood unresolved since wave 13. Wave 15's RULE L15 built the
> instrument that escapes it, and §6(b)'s standing line was followed: the design named the class of change and the
> arbiter **before** the arm ran.
>
> **The construction, and it is the transferable part.** Compare two landings by running the detector at each
> landing's own knobs and scoring the resulting fit's vertex against `renderRequest.OptimalFocuserPosition` — with
> the rejection **budget fixed at 0 for both arms**, so the fit machinery is identical and the *detector* is the
> only difference. Nothing in the comparison is computed by the fit under test: not `J`, not σ_focus, not R².
> `truth` and `step` are read from wave 13's stored table rather than re-derived.
>
> **The blocker was real and it is worth carrying.** `af-fit --settings <landing>` fails **silently**: a landing
> (`optimized_settings.json`) is a flat DTO with no `Options` member, so `HarnessSettingsStore.ResolveAt` yields
> an **empty option bag** and the run uses code defaults — and `HarnessSettingsStore.Fingerprint` appends nothing
> when `Options` is absent, so **two different landings of one dataset hash IDENTICALLY** and `af-fit` prints no
> fingerprint at all. *An instrument that is not connected reports perfect agreement.* The working route is two
> keys through the production Accept path — `Options["OptimizedSettingsJson"] = <the landing's RAW TEXT>`,
> `Options["UseOptimizedSettings"] = "True"`, `UseAdvanced` staying **`False`** (load-bearing: `ConfigureSimpleSettings`
> returns immediately when it is true) — which also carries
> [F59](#f59--the-settings-export-drops-every-knob-whose-setter-validates-so-pinned_settingsjson-has-been-missing-five-detector-knobs-since-wave-5)'s
> six validating-setter knobs for free, because they ride inside the snapshot rather than as option keys.
>
> **The answer is NOT delivered.** RULE L15 returned **NO VERDICT** on validity gate G-c
> ([F67](#f67--af-fits-star-count-and-optimizes-are-not-the-same-number-so-the-control-built-on-their-equality-reports-could-not-look-on-exactly-the-datasets-where-the-intervention-bites-hardest)),
> so the measured 7–7–5 over 19 movers is a diagnostic. **What this entry gains is the instrument and the
> statement that the question is answerable for ~8 minutes of arm time once the gate is fixed** — not a direction.
> Reproduce: `/mnt/d/hf_w15/convert_landing_w15.py --self-test`, `/mnt/d/hf_w15/affit_w15.sh`,
> `/mnt/d/hf_w15/l15_score.txt`, `docs/synthetic-af-bank-followups-wave15-results.md` §3.1, §3.6.

### F61 — F58(d)'s real consumer was the SENSOR MODEL, not the AF fit: the per-star paraboloid's rejection budget came from the active profile
**Status:** **Fixed (wave 12)** for every harness runner · found 2026-08-09 by RULE B12-D, **the control written
to prove the fix was a no-op** · **the numbers it moved are TILT numbers**

Wave 12 converted the five remaining `new AutoFocusOptions(profileService)` sites
([F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections)(d))
and then asked the discriminating question: **does switching the profile still move anything?** Three real-bank
runs, the same pinned settings file, scored under `astrodet` (`MaxOutlierRejections` = 0) and under `Default`
(= 1), on the pre- and post-conversion binaries:

| binary | scored quantities that MOVED between the two profiles |
|---|---|
| pre-conversion | **15 of 24** |
| post-conversion | **0 of 24** |

**And `sigmaFocus` — the AF fit, the thing everyone assumed was at risk — never moved on any of the three runs.**
What moved was the sensor model:

| run | quantity | `astrodet` | `Default` | |
|---|---|---|---|---|
| `toml999` | `sStars` | 613 | **616** | stars admitted to the paraboloid |
| | `sR2` | 0.14883143 | **0.14580555** | |
| `muggsie` | `sTheta` | 0.12407606 | **0.11525545** | **tilt θ, 7.6 % apart** |
| | `sRMS` | 6.7519583 | **6.7712252** | |
| `mccomiskey` | `sStars` | 2800 | **2856** | |
| | `sChi` | 0.11344971 | **0.12177990** | |

**The mechanism is one read.** `SensorModel.cs:714–715` takes `MaxOutlierRejections` and
`OutlierRejectionConfidence` off `IAutoFocusOptions` for the **PER-STAR** curve fit, so the rejection budget
decided *which stars produced a `SensorParaboloidDataPoint` at all* — and the surface fit, its R², its RMS and
its **tilt angle** follow from that population.

### Why this matters beyond the harness

`BankVerifyRunner` was flagged in F58(d) as *"twice"*, and the second instance — the `SensorModel` construction —
was treated as an afterthought. **It was the one that mattered.** `InspectAlignRunner` and
`TiltCalibrationRunner` fit sensor models from the same options, so **every tilt number those harnesses produced
before this commit carries an input nothing recorded**, and two such numbers measured under different active
profiles were never comparable. The effect is small on R²/RMS and up to **7.6 % on θ** in this sample; it is not
a claimed error bar on any specific published tilt result, and it is not retro-fitted to one either.

**It also sharpens [F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections)'s
own framing.** F58 called `AutoFocusOptions` "the four values that reach the AF **fit**". They reach the sensor
model too, through a completely different consumer, and on `bank-verify` that consumer is the only one that was
sensitive.

### Next step

(a) **When a tilt result must be compared across sessions, the comparison needs `FitInputs`** — now printed by
every converted runner and stored in `bank-verify`'s and `synth-validate`'s reports.

> **WAVE 13 measured this on a population, with ONE variable changed, and the warned-of asymmetry did NOT
> materialise.** Wave 12's 7.6 % on `muggsie` compared `astrodet` against `Default`, and those two profiles
> differ in `OutlierRejectionConfidence` as well (0.95 vs 0.99) — so it conflated two changed fit inputs. Moving
> **one key in one settings file** on the same run gives **7.2 %**, which pins the cause to the integer rather
> than to the profile machinery that carried it.
>
> Over all 19 real-bank runs the sensor fit moves on **15**, `sStars` on 5, and θ by up to **13.4 %**
> (`fmeschia_Focus`), median 0.67 %. On the **14 runs whose star population is unchanged** — the only subset
> where the goodness-of-fit statistics compare like with like — **`MOR`=0 is better on 10 of 10 moved `sChi`**
> and 9 of 10 `sRMS`: *the sensor paraboloid fits WORSE when each star's own curve is allowed a rejection.*
>
> > **WAVE 14: THE `sChi` HALF IS [F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)
> > ARRIVING ONE LEVEL UP, AND ONLY `sRMS` CARRIES THIS.** Verified in code, not conjectured
> > ([`docs/wave14-f62-audit.md`](wave14-f62-audit.md) H3): the paraboloid is weighted by `1/σ²` where σ is
> > **the per-star hyperbola's own `MinimumStdError`** — `SensorModel.cs:838-841` sets
> > `bestFocusStdDevMicrons = fitting.MinimumStdError × focuserSizeMicrons`, `:869` passes it through
> > `SensorParaboloidDataPoint.RegularizeStdDev` (a 2 µm quadrature floor), and `NonLinearLeastSquaresSolverBase`
> > turns it into the weights behind `ReducedChiSquared` and `GoodnessOfFit`. **Where the per-star rejection
> > fires, σ falls, the weight rises, and reduced χ² rises — the observed direction, for reasons that are not
> > about the surface fitting worse.** `sR2` inherits the same weighting; the 2 µm floor damps the effect and
> > does not remove it. **The like-for-like control fixes the OUTER point set (which stars enter the paraboloid)
> > and cannot close this one, because the contamination arrives through the WEIGHTS.**
> >
> > **`sRMS` is the uncontaminated half** — `NonLinearLeastSquaresSolver.RMSError` is an **unweighted** RMS of
> > the µm residuals — so **9 of 10 is what this conclusion should be quoted from**, and it is what wave 14's
> > item 1 cites. It is insulated, not immune: the surface those residuals are measured against was still solved
> > with the contaminated weights. **"10 of 10 `sChi`" is not independent corroboration and should not be quoted
> > as any.** To make `sChi` readable, re-score both arms with the `MOR`=0 weights **held fixed**.
>
> **So the AF fit and the sensor fit do NOT prefer opposite values.** The AF fit is silent, the sensor fit
> prefers 0, and the out-of-sample arbiter never prefers 1 ([F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)).
> There is nothing to average — which is worth stating precisely because the opposite was pre-registered as the
> outcome that would have blocked a decision. Reproduce: `D:\hf_w13\bv_compare.txt`.
(b) **Not done, and priced:** re-running any historical tilt calibration under pinned fit inputs to see whether a
published θ moves. Nothing currently depends on it, and the honest statement is that those numbers have an
unrecorded input rather than a known error.
Reproduce: `D:\hf_w12\bankverify_disc_w12.sh`, `D:\hf_w12\b12d_score.txt`.

### F60 — Fan-out is now SAFE and it is barely worth doing: `optimize` already saturates the machine
**Status:** Open (recorded as a standing cost fact) · found 2026-08-09 (wave 12) measuring the payoff of the
authorisation the same arm had just granted

Wave 12's RULE A12 authorised fan-out at degree 4 ([F55](#f55--optimize-is-not-reproducible-when-several-instances-run-at-once-and-the-seed-evaluation-is-what-moves)).
The arm was justified on the grounds that it would make every future pass **~4× cheaper**. **It does not.**

| | sequential | at fan-out 4 |
|---|---|---|
| the same eight runs, wall clock | **43 m 10 s** | **32 m 28 s** |
| sum of per-run busy time | 43 m 10 s | **76 m 03 s** |

**Speedup 1.33× — 25 % of the wall clock — and every individual run took 1.45–2.55× longer** (`toml999`
1 m 58 s → 5 m 01 s; `D18` 6 m 36 s → 13 m 49 s). The optimize path is already multi-threaded across the whole
machine (`Cv2.GetNumThreads()` = 48 here), so four processes **contend** rather than parallelise: the arm spent
33 extra minutes of busy time to buy 11 minutes of wall clock.

### What follows

- **Sequential remains the DEFAULT.** Fan-out is for a pass long enough that 25 % is worth losing `--profile-id`
  for (pinning and fan-out are still mutually exclusive — wave 11's K3).
- **A 39-run population pass costs ~2 ¼ h, not ~45 m.** Any future plan budgeting from "4× cheaper" is wrong;
  budget from this measurement.
- **Degree 8 is not obviously better and was not measured.** With per-run inflation already at 1.45–2.55× at
  degree 4, more workers plausibly buy nothing; if it matters, measure it rather than extrapolating.

**Why it is filed rather than folded into the authorisation.** The two facts point in opposite directions and
both are true: *fan-out is now safe, and it is barely worth doing.* A wave that records only the half that
justified the work is how a project acquires a belief it never measured — which is the same failure as
[F55](#f55--optimize-is-not-reproducible-when-several-instances-run-at-once-and-the-seed-evaluation-is-what-moves)'s
own "a measured gain is not a measured cause", one level up in the planning rather than the analysis.
Reproduce: `D:\hf_w12\gate_w12.log`, `D:\hf_w12\fanout_w12.log`.

### F59 — The settings export drops every knob whose setter VALIDATES, so `pinned_settings.json` has been missing five detector knobs since wave 5
**Status:** **Fixed (wave 11)** · found 2026-08-08 by a unit test written for
[F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections)'s
fit inputs, which failed on a property nobody was looking at

`HarnessSettingsStore.ExportFromProfile` snapshots the options **through the class's own property surface**, so
the class emits the right keys with the right types and the snapshot cannot drift from what it reads. To make the
snapshot DENSE it writes a *poison* value first — a value equal to the shipped default would otherwise never fire
the change-detecting setter, never reach the file, and leave the file inheriting whatever the default is **at load
time**.

**The poison was `current + 1`, and these setters THROW.**

```
MaxDistortion              must be within [0, 1]        0.65  -> poison 1.65 -> ArgumentException
OutlierRejectionConfidence must be in (0.5, 1.0)        0.95  -> poison 1.95 -> ArgumentException
```

One `try` wrapped both the poison write and the real write, so the exception was caught by a `catch` whose comment
says *"a property that refuses a round trip … skip it"* — **and the property was skipped entirely. Neither the
poison NOR the value was written.** A poison-only failure is not a refusal to round-trip: the real value would
have been accepted.

**`StarDetectionOptions` has 28 validating setters.** Measured against `D:\hf_w11\pinned_settings.json` — the file
that is byte-identical across waves 5–11 and that every arm has called "the pinned detector":

| knob | in the pinned file? |
|---|---|
| `MaxDistortion`, `StarCenterTolerance`, `SaturationThreshold`, `HotpixelThreshold`, `Sensitivity` | **NO** |
| `MinHFR`, `StarPeakResponse`, `BrightnessSensitivity`, … | yes |

### What it does and does not invalidate

- **Waves 5–11 are internally valid.** The missing keys resolve to **code defaults**, the same file and the same
  defaults were used throughout, so every arm ran the same detector as every other arm.
- **What is false is that the file DESCRIBES the detector.** It does not, for those five knobs — and
  `ExportFromProfile`'s own docstring promises the bootstrap is *"behaviour-preserving, or the first run after
  this change would silently differ from the last one before it"*, which for a validated knob it was not.
- **The hazard is live rather than historical:** change any of those five code defaults and every pre-wave-11
  pinned file silently starts describing a different detector. That is precisely the drift
  [F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)
  exists to prevent, defeated inside the mechanism written to prevent it.

### Fixed

Poison candidates are **tried in turn** until one lands inside the property's own valid range (the numeric spread
runs both directions and through the unit interval), the poison is **verified by read-back** rather than assumed,
and **the real write has its own `try`** so a poison failure can never cost it again.

**The wave-11 pinned file deliberately does NOT gain the five recovered knobs.** Adding them would change the
detector and move the coordinate system RULE G11 was just established on; they stay at code defaults, as they
have been for seven waves, consistently. The fix changes what a FUTURE bootstrap exports.

**How it was found is the point.** The test that failed was written to assert **density** — *"a value equal to the
code default must still reach the file"* — and not presence. Presence would have passed: the four fit inputs it
was written for include one (`MaxOutlierRejections = 1`) that IS the code default. *A test written for the
property you care about finds the bug you were not looking for; a test written for the happy path does not.*

### Next step

None for the export. **But the five knobs are now known to have been at code defaults for waves 5–11**, which is
worth stating in any write-up that describes `pinned_settings.json` as a full snapshot. Reproduce:
`Joko.NINA.Plugins.HocusFocus.Tests/Harness/HarnessFitInputsTests.cs`
(`CopyOptionSurface_CarriesAKnobWhoseSetterTHROWSOnTheObviousPoison`).

> **WAVE 15 — this entry is now load-bearing for more than provenance.** Wave 15 had to feed an optimizer
> **landing** back into the detector, and the six curated knobs an exported bag has no key for — `MaxDistortion`,
> `StarCenterTolerance`, `HotpixelThreshold`, `DefocusDistortionMinFactor`, `DonutMinAnnularityHoleFraction`,
> `DonutMaxStreakEccentricity` — are **exactly** the six a lift-by-key converter would silently drop. The wave
> routed around them by embedding the landing's raw snapshot (`Options["OptimizedSettingsJson"]`) instead of
> lifting knobs, so the converter never needs a knob's option-key spelling and F59's six ride along. *The design
> defect is not only a provenance gap: it is the reason the obvious converter is the wrong one.*
> Reproduce: `docs/synthetic-af-bank-followups-wave15-results.md` §3.1.

> **WAVE 21 — RE-AFFIRMED AS REJECTED, on wave 20's measurement, and the price now has a measured number.**
> Wave 21's pre-registration considered repairing the exporter and **declined, without making a new
> measurement**, because wave 20 had already made the one that decides it:
>
> - **Four of the five are preset-owned.** `DerivePresetSettings()` assigns `MaxDistortion`,
>   `StarCenterTolerance`, `HotpixelThreshold` and `Sensitivity`, so under the pinned file's `UseAdvanced=False`
>   they are **overwritten on load**. Repairing the exporter changes the detector by **exactly nothing** for
>   those four. (The pinned file says so itself at every run: *"UseAdvanced=False; Simple-mode presets override
>   … recorded advanced knob(s)"*.)
> - **The fifth, `SaturationThreshold`, is NOT preset-owned and WOULD bind.** That makes repairing it a
>   **coordinate-system move**: every `J` in this series was measured at the current value, so the repair owes a
>   **fresh eight-value baseline** before anything measured after it can be compared with anything measured
>   before it.
> - **The price of that baseline is a full gate. Wave 21 measured it: `G21_START 09:12:51Z → G21_DONE 09:55:18Z`
>   = 42 m 27 s** for eight runs at `--max-evals 250` on one `TestApp.exe`. The ~42 m estimate is no longer an
>   estimate. **That is the whole objection**: one wave's entire gate budget spent a second time, to make four
>   knobs that cannot bind honest and one knob that can bind different.
>
> *Recorded so the next wave starts from "4 of 5 change nothing and the 5th costs 42 m" rather than re-deriving
> it — and so that rejecting it stays a costed decision rather than an oversight.*
> Reproduce: `docs/synthetic-af-bank-followups-wave21-design.md` §7;
> `docs/synthetic-af-bank-followups-wave20-results.md` §10 (item 5); `/mnt/d/hf_w21/gate_w21.log`.

### F58 — Concurrent `optimize` processes each acquire a DIFFERENT NINA profile, and the profile decides the fit: F55's "two attractors" are two values of `MaxOutlierRejections`
**Status:** Open · found 2026-08-08 (wave 11) **at zero compute, out of logs wave 9 left on disk** ·
**this is the MECHANISM behind [F55](#f55--optimize-is-not-reproducible-when-several-instances-run-at-once-and-the-seed-evaluation-is-what-moves)
and [F57](#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it),
which are one defect seen from two directions**

Wave 10 established that the active NINA profile moves `BaselineJ` and left *which quantity* (F57(c)) and *the
nondeterminism's trigger rate* (F55(b)) open. Both close on the same reading, and **no optimization had to be run
to find it** — the evidence was already printed in a `Profile:` line that two waves had scrolled past.

### The mechanism, read out of `NINA.Profile`

1. **`Profile.Load(path)`** opens the `.profile` with `FileAccess.ReadWrite, FileShare.Read` and **holds the
   stream for the profile's lifetime**. A second process's `Load` on the same file throws `IOException`
   (`ERROR_SHARING_VIOLATION`), and `Load` **rethrows it** — the journal/backup recovery path is deliberately
   skipped for the in-use case (`catch (IOException ex) when (IsFileInUse(ex)) { throw; }`).
2. **`ProfileService.SelectProfile`** catches, logs, and returns **`false`**.
3. **`ProfileService.TryLoad(id)`** orders candidates `OrderByDescending(x => x.LastUsed)` and then
   `.SkipWhile(p => !SelectProfile(p)).FirstOrDefault()` — **so a locked profile is silently SKIPPED and the next
   profile by `LastUsed` is loaded instead.**
4. **`Profile.Load` also sets `LastUsed = DateTime.Now` and SAVES.** Loading a profile rewrites the ordering that
   decides which profile the *next* unpinned run gets.

**So N concurrent unpinned `optimize` processes acquire N DIFFERENT profiles**, and *which* process gets which is
a race — the SET is determined (the top N by `LastUsed`), the assignment is not. That is F55's *"sporadic rather
than per-arm"* exactly.

### The observation, from `D:\hf_w9\det\{S,C}_{1..5}.log` — the 40-second reproducer, re-read

`D16_esprit550_ha3`, `--max-evals 1`, so `BaselineJ` is the whole measurement:

| repeat | `Profile:` printed by the run | `BaselineJ` |
|---|---|---|
| S_1 … S_5 (**sequential**) | `AA1600MM Copy (1bf0efaf-…)` — **all five the same** | `0.979173` ×5 |
| C_1 (**concurrent**) | `AA1600MM (4cf31cda-…)` | **0.988555** |
| C_2 | `Default (b10b1d6d-…)` | **0.988555** |
| C_3 | `astrodet (ce3f3e63-…)` | `0.979173` |
| C_4 | `Default-2026-08-05T10:57:42 (1a120eb1-…)` | **0.988555** |
| C_5 | `AA1600MM Copy (1bf0efaf-…)` | `0.979173` |

**Five concurrent processes, five different profiles**, and C_5 — which drew the same profile the sequential
phase drew — returned the sequential phase's value.

### And `J` partitions on ONE field

The optimize path feeds exactly four profile-sourced values into the fit
(`OptimizationDiagnosticRunner.cs:673–679`: `UseWeights`, `MaxOutlierRejections`, `RejectionConfidence`,
`PreferredModel`). Across those five profiles three of the four are constant:

| profile | `BaselineJ` | `MaxOutlierRejections` | `OutlierRejectionConfidence` | `WeightedHyperbolicFitEnabled` | `HyperbolicFitModel` |
|---|---|---|---|---|---|
| `AA1600MM Copy` | `0.979173` | **0** | *(absent ⇒ 0.95)* | *(absent ⇒ true)* | Hybrid |
| `astrodet` | `0.979173` | **0** | *(absent ⇒ 0.95)* | true | *(absent ⇒ Hybrid)* |
| `AA1600MM` | **0.988555** | **1** | *(absent ⇒ 0.95)* | true | *(absent ⇒ Hybrid)* |
| `Default` | **0.988555** | **1** | **0.99** | true | *(absent ⇒ Hybrid)* |
| `Default-2026-08-05T10:57:42` | **0.988555** | **1** *(absent ⇒ 1)* | *(absent ⇒ 0.95)* | *(absent ⇒ true)* | *(absent ⇒ Hybrid)* |

> **`MaxOutlierRejections` = 0 ⇒ 0.979173. = 1 ⇒ 0.988555. Five of five.** `OutlierRejectionConfidence` varies
> *within* the firing group (0.99 vs 0.95) and does not move `J` — which is what a rejection budget of one
> predicts and a coincidence does not.

**The machine holds 9 profiles and they partition 2 / 7 on this field** (`D:\hf_w11\profiles_before.txt`). *That
is the bimodality.* Two discrete attractors because a small integer takes two values in the wild — never a
floating-point race, which is the one property of F55 that never fit summation order.

### Confirmed independently on three more datasets, also at zero compute

Wave 9's **four-way concurrent control trial** (`D:\hf_w9\ctl_conc_*.log`) also printed its profiles:

| log | profile loaded | `MaxOutlierRejections` | `BaselineJ` |
|---|---|---|---|
| `ctl_seq_toml999` (**sequential**) | `Default-2026-08-05T10:57:42` | 1 | 0.997840 |
| `ctl_conc_toml999` | **the same profile** | 1 | **0.997840** |
| `ctl_conc_bobp` | `astrodet` | 0 | 0.993122 |
| `ctl_conc_caboose` | `Default` | 1 | 0.994753 → landed **0.996300** |
| `ctl_conc_mufti` | `AA1600MM Copy` | 0 | **0.957603** |

**Four processes, four different profiles** again. And two of these close open items in F55's own tables:
`mufti`'s 0.957603 is arms **B/C**'s value (arm A's 0.957087 must therefore have drawn a `= 1` profile), and
`caboose`'s 0.996300 is one of that run's two recorded landings.

**The sharpest of them is the anomaly F55 had to hedge about.** F55 records *"one four-way concurrent trial did
NOT reproduce the deviation"* and correctly refuses to read it as evidence against the fan-out, on the grounds
that *"the effect appears on ~15 % of runs, so a single trial has no power to exclude it."* The hedge was right;
the reason is now visible in the log rather than left to probability. **`ctl_conc_toml999` drew the same profile
as `ctl_seq_toml999`**, so it was not a 15 % coin landing the same way — it was the same fit inputs, and the
values are identical rather than merely close.

### What it explains, and every one of these was paid for by measurement

| eliminated by waves 9–10 | why F58 is consistent with it |
|---|---|
| the plugin's `Parallel.For` degree (1/2/4/8/48), swept, inert | this is not a thread race |
| the BUILD (15 runs, 3 binaries, identical to 10 dp) | those fifteen ran in ONE session under ONE **pinned** profile |
| folder state, one artifact at a time | irrelevant to which file the process opened at startup |
| the per-run `optimized_settings.json`, the detection cache, OpenCL/`UMat`, `Merge`, rented sorts, `MedianInPlace`, timeouts | same |
| **"stable within a process/session, variable across them … does fit some process-level state … acquired once"** | **a profile is acquired once, at startup.** Wave 9 named the shape of the answer and the search kept looking below the fit |

**And it explains F57 without a second cause.** Wave 9's gate ran under `Default` (`MaxOutlierRejections = 1`),
wave 10's under `astrodet` (`0`); `toml999`'s `BaselineJ` went 0.997840 → 0.983477, and the group that may drop a
Grubbs outlier is the group with the higher `J`. **F57(c)'s answer is a named field**, and it is
[F45](#f45--the-grubbs-test-rejects-the-in-focus-point-of-a-near-perfect-curve-and-the-blind-walk-then-buys-an-extra-exposure)'s
outlier rejection deciding the objective from machine state that nothing recorded.

> **WAVE 14 — the clause this sentence used to end with is INVALIDATED, and the entry's answer is not.** It read
> *"and allowing one Grubbs rejection improves a fit, which is the observed direction"*, offered as corroboration.
> **It is a tautology** ([F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)
> audit H1): `J` is computed by the fit **after** the fit was allowed to discard its worst point, so "it improves"
> was guaranteed before the data were seen — and wave 13 measured that same improvement buying accuracy on
> **0 of 4** datasets against the generator's truth, opposing it on three. *A prediction that cannot fail is not
> corroboration.*
>
> **The attribution does not depend on it.** It rests on the **5-of-5 partition**, on `OutlierRejectionConfidence`
> varying *within* the firing group and moving nothing, and on
> [F57](#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it)'s
> later one-key intervention with an explicit negative control. Those are claims of **non-identity** and of
> **attribution**, which F62 cannot threaten — it says the number moves for a reason that is not quality, which
> is precisely this entry's point.

### Two operational consequences that are not obvious

- **A pinned run rewrites the default for the next unpinned run.** `LastUsed` is stamped by the act of loading,
  so `--profile-id X` today makes X the active profile tomorrow. **Measured 2026-08-08:** wave 10's own F57 probe
  pinned `Default` last, and an unpinned `optimize` on `toml999` today prints
  `Profile: Default (b10b1d6d-…)` and returns `currentJ = 0.99784` — **wave 9's value, not the wave 10 the banks
  were measured under.** An unpinned wave-11 gate would have reproduced the wrong wave and called it a pass.
- **`--profile-id` and fan-out are mutually exclusive as things stand.** With the id filter the candidate list has
  one entry, so a second concurrent process runs `SkipWhile` over an empty remainder, `TryLoad` returns `false`,
  and `optimize` throws *"No active NINA profile could be loaded"*. **Pinning converts a silent wrong answer into
  a loud failure**, which is the right trade and is not a fix.

### The fix, and why it is the harness's and not the plugin's

`HarnessSettingsStore` exists precisely so that *"a profile-sourced seed is mutable machine state nothing
records"* cannot reach a run: its `FileOptionsAccessor` reads the pinned file and falls back to **code defaults**,
never to the profile. `StarDetectionOptions` is built on it. **`AutoFocusOptions` is not** —
`new AutoFocusOptions(profileService)` binds a `PluginOptionsAccessor` to the ACTIVE profile. So the detector is
pinned and the fit is not, and `--settings` has been read as pinning the arm for six waves. **The asymmetry is
the bug.** The remedy is to build the harness's `AutoFocusOptions` on the harness accessor (the seam already
exists), carry the four fit inputs in the pinned file, and print them in provenance as **values, not a hash** —
a hash says something moved, values say **which**. The live plugin is unchanged: the wizard and the AF engine
must keep reading the profile, because there those *are* the user's settings.

### Next step

(a) **Intervene, do not stop at correlation** — five pre-existing profiles agreeing is not an experiment. Bisect
`toml999` on synthetic single-field profiles under RULE C, whose **negative control** is that
`OutlierRejectionConfidence` alone must move nothing when the rejection budget is 0.
(b) **Pin the fit inputs** as above, then re-run the 40-second reproducer and require that the concurrent phase
returns ONE value **while still loading five different profiles** — a fix that merely pinned the profile would
pass a weaker test and prove nothing.
(c) **Then say what is left.** `KappaSigmaNoiseEstimate`'s measured gain (F55) stays as a fact about the
function; it is withdrawn only as *this* defect's explanation, and only once the residual check has run.
(d) ~~**FOUR OTHER HARNESS RUNNERS HAVE THE SAME DEFECT AND ARE NOT FIXED HERE**~~ — **DONE 2026-08-09
(wave 12): all five remaining call sites converted, behind ONE seam
(`HarnessSettingsStore.BuildFitOptions`) with a source guard on the CONSTRUCTOR so it holds for a site nobody has
written yet. Validated by RULE B12 (692 leaf values identical) AND its discriminating half RULE B12-D (the
profile moved 15 of 24 scored quantities BEFORE the conversion and 0 of 24 after). AND IT WAS THE SENSOR MODEL,
NOT THE AF FIT — see
[F61](#f61--f58ds-real-consumer-was-the-sensor-model-not-the-af-fit-the-per-star-paraboloids-rejection-budget-came-from-the-active-profile).**
The original text, for the record:
(d) **FOUR OTHER HARNESS RUNNERS HAVE THE SAME DEFECT AND ARE NOT FIXED HERE**, flagged rather than swept in
because each needs its own validation and wave 11's budget went to `optimize`:
`BankVerifyRunner` (twice — the recall/precision harness whose numbers feed the golden audits),
`SynthValidateRunner`, `InspectAlignRunner`, `TiltCalibrationRunner`. Every one builds
`new AutoFocusOptions(profileService)` and therefore takes its fit from whichever profile is ACTIVE. Until they
are converted, **they must be run with `--profile-id`**, and two of their results measured under different
profiles are not comparable. *(`HocusFocusPlugin.cs` also constructs it from the profile and is CORRECT — in the
live app those are the user's settings.)*
Reproduce: `D:\hf_w9\det\{S,C}_{1..5}.log`, `D:\hf_w9\ctl_{seq,conc}_*.log`, `D:\hf_w11\profiles_before.txt`,
`D:\hf_w11\pregate\pregate.log`.

### F57 — A `--settings`-pinned arm is NOT pinned: the active NINA profile moves `BaselineJ` by 0.014, and every cross-wave comparison inherits it
**Status:** **CLOSED 2026-08-09 (wave 13) BY INTERVENTION** · found 2026-08-08 (wave 10) while running the
pre-registered control for a DIFFERENT hypothesis, which it refuted · **this is [F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)'s
warning, measured for the first time** · **(c) ANSWERED 2026-08-08 (wave 11): the quantity is
`AutoFocusOptions.MaxOutlierRejections`, and the profile is acquired per PROCESS — see
[F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections)**

> ### CLOSED: one key in one file recovers BOTH historical numbers
>
> Wave 11 identified `MaxOutlierRejections` from **five pre-existing profiles that happened to agree** — a
> correlation over machine state nobody controlled. Wave 13 ran the intervention: two settings files differing
> in **exactly one line**, the profile pinned to `astrodet` on both arms, `optimize --max-evals 1` so no search
> runs and the seed evaluation is a pure function of (frames, detector, fit inputs).
>
> | `toml999` `BaselineJ` | value | which wave this is |
> |---|---|---|
> | `MaxOutlierRejections` = 0 | **0.98347680** | **wave 11's** — and RULE G13's free control in wave 13 itself |
> | `MaxOutlierRejections` = 1 | **0.99784046** | **wave 9's**, the number F58 quotes as `0.997840` |
> | Δ | **+0.01436366** | F57 stated the gap as **0.0144** |
>
> **Both historical values are recovered from one integer, with the profile held constant.** The profile was
> never the cause; it was the carrier. 8 of 39 population runs move at all, and the same eight move
> `BaselineSigmaFocus`. *Five profiles agreeing is a correlation; one integer and both numbers back is the
> experiment.* Reproduce: `D:\hf_w13\pop_w13.sh`, `D:\hf_w13\pop_score.txt`.

**How this was found is the point, so it is told in order.**

Wave 10 re-ran wave 5's eight-run comparability gate on `StarDetectorVersion` 2 and found **6 of 8 landings
moved**, plus `toml999`'s `BaselineJ` — *a single evaluation of the pinned seed, no search, nothing to amplify
it* — moving from **0.997840 to 0.983477**. Every printed seed input was identical: same settings file and export
stamp, `Sensitivity=10`, `StarClippingMultiplier=2`, `NoiseClippingMultiplier=4`, `StructureLayers=4`, inferred
step 21, exposure 5 s, `PixelScale 0.73944` from the frame header, detection binning 1. The obvious reading was
that [PR #187](https://github.com/ghilios/hocus-focus/pull/187)'s ≤ 3e-8 wavelet change had moved a product
answer by six orders of magnitude more than its bound, and that was written up as this entry.

**The pre-registered control refuted it.** A three-way probe ran `toml999` **five times each** on wave 9's v1
binary, wave 10's v2 binary, and a **bisect build** — wave 10's tree with `StarDetector.cs`'s two call sites
reverted to the legacy dense wavelet — interleaved, in one session, on identical folder copies:

> **All fifteen runs returned `0.9834767969`. Identical to ten decimal places.**

So the binary does not decide it, and **the wavelet is exonerated outright**: v2 and the bisect differ only in the
wavelet and agree exactly. Folder state was then excluded one artifact at a time — removing
`optimized_settings.json`, `hocusfocus_star_detection.json`, `autofocus_report_Region0.json` and `run_meta.json`
each changed nothing, and the frames and `harness_settings.json` predate both waves.

**What was left was the one input nobody compares.**

| | wave 9's gate | wave 10's gate |
|---|---|---|
| `--settings` | `hf_w9\pinned_settings.json` | `hf_w10\pinned_settings.json` — **byte-identical**, `md5 df7c7cd1…` |
| **`Profile:`** | **`Default (b10b1d6d-…)`** | **`astrodet (ce3f3e63-…)`** |

**Re-running `toml999` today with `--profile-id b10b1d6d-…` returns `0.9978404571` — wave 9's value, to 6 dp.**

### What it means

`--settings` pins the DETECTOR knobs, and this project has treated that as pinning the arm — [F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)
even says so in the run instructions. **It does not.** The harness also loads whichever NINA profile happens to be
ACTIVE (`profileService.TryLoad("")`), and that profile moves `BaselineJ` — the objective of a fixed seed on fixed
frames — by **0.0144**, which is larger than the entire Δ`J` any wave has ever argued about.

F42's own text predicted this exactly — *"`TryLoad("")` picks whichever profile is ACTIVE — two runs of the same
data minutes apart were seeded from different telescopes"* — and the fix it prompted was to pin the detector
settings, which does not close it. **The prediction was right, the remedy was incomplete, and nobody measured
the residue until an unrelated control forced it.**

### What it invalidates

- **The wavelet claim this entry originally made: withdrawn in full.** PR #187 is not implicated in anything.
- **Wave 10's RULE G10-B ("6 of 8 landings moved") is VOID**, not merely uncertain: it compared two waves that
  ran under different profiles, so it measures the profile at least as much as the binary.
- **Every cross-wave `BaselineJ`/landing comparison in this register is exposed**, including wave 6's arm-R
  round-0 control and wave 9's gate, unless the active profile happened to match. Those controls PASSED, which
  is evidence the profile was stable across waves 5–9 — it is not evidence that it was pinned.
- **Wave 10's RULE G10-A is NOT affected** and still passes 8 of 8: it compares two runs *within* wave 10, under
  one profile, and it is what makes this wave's own measurements readable.

### Next step

(a) **`optimize` must record the profile id and name in `OptimizerProvenance`**, beside the `BuildId` and
`DetectorVersion` this wave added — the same argument, one input further out: a reader diffs a field.
(b) **Every arm must pass `--profile-id` explicitly**, and the run instructions must say so beside F42's
`--settings` rule; an arm that does not is pinned in one dimension and floating in another.
(c) ~~**Find WHICH profile-sourced quantity moves the objective.**~~ — **ANSWERED 2026-08-08 (wave 11):
`AutoFocusOptions.MaxOutlierRejections`, 1 under `Default` and 0 under `astrodet`, with
`OutlierRejectionConfidence` (0.99 vs an absent 0.95) behind it and unreachable while the budget is 0. The guess
recorded here — *"`AutoFocusOptions` … is the obvious next place"* — was right, and the read-level audit that
confirmed it also excluded everything else: the detector knobs are genuinely pinned (`FileOptionsAccessor` falls
back to CODE defaults, never the profile, so `SaturationThreshold` 0.99-vs-0.9 and `DetectionBinning` are inert),
`ImageSettings` is byte-identical between the two profiles, `AutoFocusBinningConflict` only feeds a prompt, and
the harness infers the step from the frames rather than from `FocuserSettings.AutoFocusStepSize`. **The profile
is also acquired PER PROCESS, which is the same defect as F55** — see
[F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections).
(d) ~~**Re-open the wavelet question properly, uncofounded**: run the eight gate runs on `exe_v1wav` against
`exe`~~ — **RECOMMENDED FOR CLOSURE 2026-08-09 (wave 13), UNRUN, and the recommendation is priced.** The build
`D:\hf_w10\exe_v1wav` has now sat unused for three waves, and the question it was built for is bounded from
three sides:
 1. **the seed-level answer is already exact and identical** (wave 10), so only amplification by the search was
    ever open;
 2. **wave 12's RULE A12** showed eight landings surviving a deliberately perturbed *process* environment
    bit-identically at fan-out 4 — the search did not amplify an arbitrary perturbation present there;
 3. **RULE G13 has now reproduced the same eight landings on a FOURTH binary**, bit-identical to sixteen
    digits, across four separate builds of the same tree.
A search that were sensitive to a ≤3e-8 numerical difference would have had four independent chances to show it
and took none. **Cost to run it anyway: ~42 minutes** (one 8-run gate, measured in wave 13). **Cost of leaving
it open: a build directory that every future wave has to explain.** Recommend closing (d) and deleting
`exe_v1wav`, or running it once and closing it either way — what should not continue is carrying it.
Reproduce: `D:\hf_w10\crossbuild_probe.sh`, `D:\hf_w10\crossbuild.log`, `D:\hf_w10\xb_prof.log`.

> **WAVE 14 — TWO REASONS TO SKIP (d) WERE OFFERED AND BOTH WERE WRONG, IN OPPOSITE DIRECTIONS, FROM THE SAME
> HABIT. The arm is RUNNING.** *(This block records what was fixed before any wave-14 measurement existed; the
> arm's outcome and RULE W14-D's verdict belong in
> [`docs/synthetic-af-bank-followups-wave14-results.md`](synthetic-af-bank-followups-wave14-results.md) §6 and
> are not stated here.)*
>
> - **"Not constructible — `exe_v1wav` cannot be pinned" was produced by a probe that cannot fail.**
>   `strings … | grep -c -- '--profile-id'` returns **0** on both wave-10 binaries *and* on the current one,
>   which demonstrably accepts the flag on every arm of waves 11–14. `strings -el` returns **28** on all three:
>   **both wave-10 binaries support `--profile-id`. The arm is CONSTRUCTIBLE and PINNABLE.** See
>   [F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two).
> - **"Confounded" does not survive either.** The only comparison that matters is `exe_v1wav` against
>   `D:\hf_w10\exe` — the **same tree, one variable**. Every wave-11+ change (F58's `HarnessFitInputs`, F15,
>   F61) is absent from *both* arms identically and cannot confound an A/B between them. The arm never needs to
>   be compared to a modern number at all, which makes it **cleaner than the reason given for skipping it**.
> - **The three bounds quoted above are also weaker than they read**, and only one bears on the question: wave
>   10's crossbuild probe covers the **seed** (on point); RULE A12 perturbs *timing*, not arithmetic (close to
>   tautological for a deterministic pipeline); RULE G13 measures **build-to-build determinism of the same
>   source**, which cannot produce a wavelet difference at all.
> - **Both halves are re-run**, because wave 10's stored landings are VOID (this entry voided RULE G10-B: they
>   ran under a different active profile and were never `--profile-id` pinned). **~84 m, not 42.** The
>   comparison is arm-to-arm, **never against wave 11's table** — a wave-10 binary is not expected to reproduce
>   the modern gate and a mismatch there would mean nothing.
>
> *The item survived four waves of deferral on unchecked reasons. It was cheaper to run it than to keep arguing
> about it.*

> ### **(d) CLOSED 2026-08-10 (wave 14): NOT CONSTRUCTIBLE — and for the first time that is a MEASUREMENT.**
>
> RULE W14-D ran both arms, pinned, 8 of 8, `UNEVALUATED` 0, in **81 minutes**. The landings are **bit-identical
> on all eight runs to all sixteen digits**. *And the premise is refuted*: the two binaries are **not**
> one-variable variants of the same tree.
>
> | | `D:\hf_w10\exe` | `D:\hf_w10\exe_v1wav` |
> |---|---|---|
> | `BuildId` / `ConcurrencyCheck` / `DetectorVersion` | **all absent** | `6e93a6d5…` / `exclusive` / **2** |
> | `TestApp.dll` mtime | 2026-08-08 09:03 | 2026-08-08 **12:41** |
>
> Those three provenance fields were added by later waves ([F53](#f53--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)(a),
> this entry's (a), F58). **One arm records them and the other does not, so `exe_v1wav` is a LATER tree, and the
> pair differs by an unknown amount of source.** The arm that was supposed to be *v1* reports
> `DetectorVersion` **2** — what the modern gate reports. **And the detector probe cannot separate them either**:
> `strings <plugin> | grep -c AtrousWaveletFast` returns **1 on all three binaries**, because it is a *type
> name* and the type is compiled in wherever it exists. See [F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two).
>
> **So the bullet above — "the same tree, one variable" — is WRONG, and it was the controller's own correction.**
> What the 81 minutes bought instead is real and broader: *no difference between these two binaries, whatever it
> is, reaches the landing*; and those sixteen digits are also wave 11's, so **the eight gate values now reproduce
> across SEVEN binaries spanning waves 10–14.**
>
> **Refuting an argument is not refuting its conclusion.** The pre-registration's *reason* for skipping (d) was
> false and the controller demonstrated it. Its *conclusion* — do not spend the time — was right, on a ground
> neither party gave. The check that would have settled it is a `diff` of two provenance blocks and costs
> seconds; nobody asked for it, in an item that had already consumed four waves of argument.
>
> **`D:\hf_w10\exe_v1wav` can now be archived or deleted.** Nothing further is owed to (d): a genuine wavelet
> bisect would need two builds of ONE tree differing only in the wavelet selection, which is a **new build pair**
> (~20 m to build, ~84 m to run) and answers a question the seed-level probe already answered exactly. **Do not
> reopen it without that build pair and a control that the two binaries differ where they are claimed to.**
> Reproduce: `/mnt/d/hf_w14/w14d_w14.sh`, `/mnt/d/hf_w14/score_w14d.py`, `/mnt/d/hf_w14/w14d_w14.log`,
> [`docs/synthetic-af-bank-followups-wave14-results.md`](synthetic-af-bank-followups-wave14-results.md) §6.

### F55 — `optimize` is NOT reproducible when several instances run at once, and the SEED evaluation is what moves
**Status:** Open · found 2026-08-07 (wave 9) when the confirmation arm's own pre-registered control fired ·
**this voids the wave-9 F32 arm and constrains every future arm's design** ·
**CLOSED AT THE LANDING LEVEL 2026-08-09 (wave 12): eight landings at fan-out 4, with four workers on FOUR
DIFFERENT PROFILES split 4/4 on `MaxOutlierRejections`, reproduced the sequential values BIT-IDENTICALLY.
FAN-OUT IS AUTHORISED AT DEGREE 4 — and it buys only 1.33×, see
[F60](#f60--fan-out-is-now-safe-and-it-is-barely-worth-doing-optimize-already-saturates-the-machine).** ·
**MECHANISM IDENTIFIED 2026-08-08 (wave 11): the concurrent processes were running under DIFFERENT NINA
PROFILES, and the two attractors are two values of `AutoFocusOptions.MaxOutlierRejections` —
[F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections).
Every measurement in this entry stands; the `KappaSigmaNoiseEstimate` amplifier below is withdrawn as this
defect's EXPLANATION (its measured gain stays as a fact about the function) pending F58's residual check.**

Wave 9 ran F32's confirmation arm with a **fan-out of 4** — four `optimize --per-run` processes on distinct bank
folders — to bring ~28 h of sequential compute down to ~7 h. The design pre-registered a free control for exactly
this: arm A re-measures the 8 runs the sequential gate had already pinned.

> **RULE G fired. 2 of the 8 do not reproduce.**
>
> | run | sequential gate | arm A, fan-out 4 |
> |---|---|---|
> | `toml999` | **0.997993** | **0.995784** |
> | `D18_m24_deep_shed` | **0.999822** | **0.999882** |
> | the other six | *(exact)* | *(exact)* |

**And the second control says where it comes from.** `BaselineJ` is the objective of the run's CURRENT settings —
**a single evaluation of the pinned seed, with no search involved**. The three arms differ only in `--keep-floor`
and `--continue-rounds`, neither of which touches that evaluation, so it must be identical across all three. On
**6 of 39 runs it is not**:

| run | A | B | C |
|---|---|---|---|
| `D16_esprit550_ha3` | 0.988555 | **0.979173** | **0.979173** |
| `D10_rc16_3250mm_sparse` | 0.958929 | **0.957201** | **0.957201** |
| `D17_cdk14_oiii5` | 0.993062 | **0.994830** | 0.993062 |
| `D14_cdk14_2563mm_e47` | 0.997901 | **0.997500** | 0.997901 |
| `mufti` | 0.957087 | **0.957603** | **0.957603** |
| `D15_cdk20_3454mm_e47` | 0.994703 | 0.994703 | **0.994474** |

**The gaps are far too large to be float-summation order** (`D16` moves by 0.0094), and the pattern is sporadic
rather than per-arm — A is the odd one out on three runs, B on two, C on one. This is
[F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s tell firing at scale, and
[F8](#f8--optimizer-landings-are-not-reproducible-across-invocations) one level deeper: not "a landing is a
property of the trajectory", but **the seed evaluation itself is not a function of (frames, settings) alone.**

**Two causes EXCLUDED by measurement, not by argument.** Re-running `toml999` today at arm A's exact invocation:

- **Not the F15 folder state.** Run **sequentially and alone**, on folders that then held arm A's landing, it
  returns **0.997993** — the gate's value. What a previous arm left in the run folder does not decide this.
- **Not a build or settings difference.** Same binary, same pinned file, same `[1/1] optimizing attempt01`, same
  `PixelScale 0.73944`, same resolved binning factor.

**One four-way concurrent trial did NOT reproduce the deviation** (`toml999` returned 0.997993 again with three
other `optimize` processes in flight). **That is not evidence against the fan-out**, and it is recorded here so it
is not read as such: the effect appears on ~15 % of runs, so a single trial has no power to exclude it. Waves 5–8
all ran their arms **sequentially** and all reported bit-identical controls; wave 9 is the first to fan out and
the first to lose them.

**What it costs.** The arm ran for **7 h 10 m** across three arms and 117 optimizations and **cannot be read
against wave 5's φ table**. RULE G was written as *"a partial reproduction is a failure, not a warning — the
eight are one instrument"*, and it is applied as written: **no φ verdict is published from this arm.**

> ### CONFIRMED 2026-08-07 by a 40-SECOND reproducer, and it is BIMODAL
>
> Rather than spend ~28 h to find out, the question was asked directly: **is the seed evaluation a pure function
> of (frames, settings)?** `D:\hf_w9\det\determinism_probe.sh` runs `optimize --per-run --max-evals 1` — so the
> search does nothing and `BaselineJ` is the whole measurement — on `D16_esprit550_ha3`, the arm's biggest mover,
> **five times sequentially and then five times at once**, each repeat on its own copy of the run folder reset to
> an identical state. The banks are never touched.
>
> | phase | `BaselineJ` |
> |---|---|
> | **5× SEQUENTIAL** | `0.9791727071693058` five times — **identical** |
> | **5× CONCURRENT** | `0.9885546719484486` ×3 and `0.9791727071693058` ×2 — **two values in one batch** |
>
> **Those are exactly arm A's 0.988555 and arm B/C's 0.979173.** The probe reproduces the entire 7-hour
> discrepancy from identical inputs in 40 seconds.
>
> ### THE FULL RATE, from a complete identical arm: 44 % of landings, not 15 %
>
> Wave 9's sequential re-run reproduces arm B (`--keep-floor 0.50`) in full, so the fan-out and sequential
> versions of **the same 39 runs, same flag, same binary, same pinned settings** can be diffed directly:
>
> | quantity | differing |
> |---|---|
> | **`BaselineJ`** — one evaluation of the pinned seed, no search | **6 of 39 (15 %)** |
> | **`BestJ`** — the landing the arm is actually read on | **17 of 39 (44 %)** |
>
> **The landing rate is triple the seed rate, and that is the important part.** Many runs have a
> *bit-identical* `BaselineJ` and a *different* landing — `CWhiteFocus` 0.999740 vs 0.999867, `standard_example1`
> 0.999846 vs 0.996456, `caboose` 0.996300 vs 0.994857, and `LinwoodFocus` **0.952882 vs 0.917207**, a gap of
> 0.036. So the nondeterminism is not confined to the seed evaluation: it perturbs evaluations *during* the
> search, and the pattern search amplifies one perturbed evaluation into a different landing.
>
> **RULE G caught 2 of 8 and the true rate is 44 %** — the gate was, if anything, lucky. Any arm run at fan-out
> is not "mostly fine with a couple of outliers"; it is close to a coin flip per run.
>
> *(The two arms also differ in what a previous pass had left in the run folders. That was tested separately on
> `toml999` — sequential and alone, on folders holding a contaminated landing, it returns the correct value — so
> folder state is excluded as the cause.)*
>
> **Three things follow.**
>
> 1. **Sequential execution is deterministic** (5 of 5, and the wave-9 gate independently reproduced wave 5's
>    eight values across two waves and two binaries). **Waves 5–8 stand**, and the ~28 h sequential re-run is
>    valid — it is now RUNNING.
> 2. **The defect is BIMODAL, not drift.** Two discrete attractors, not a spread — so it is a race between two
>    code paths, not floating-point summation order. That also means it can flip a *sequential* run if the machine
>    is busy for another reason, which is why this outranks the re-run in importance.
> 3. **A 40-second reproducer exists**, which is the thing that was actually missing. Any candidate fix is now
>    testable in a minute instead of a wave.
>
> **Excluded by reading the source (no runs — nothing may execute beside the sequential arm):** there is no
> OpenCL/`UMat` path at all; `AlglibHyperbolicFitting`'s parallel candidate-model pass writes into **indexed
> arrays** and resolves consensus by intersection + `Array.FindIndex`, so `Parallel.For` completion order cannot
> change it (the comments say so deliberately); and there is no load-sensitive timeout in the
> detection/evaluation/fit path — the only `Stopwatch` there is trace-only. **The mechanism is still open.**
> Degree-of-parallelism knobs derive from `Environment.ProcessorCount`, which does not change when other
> PROCESSES run, so the cause is more likely a shared cross-process resource or a genuine order dependence
> somewhere below the fit.

> ### INVESTIGATED 2026-08-07, NOT FIXED — but the search space is much smaller, and the reproducer is cheap
>
> Everything below was run with the machine otherwise idle, against the 40-second reproducer.
>
> **ELIMINATED, each by measurement rather than by reading:**
>
> | candidate | how it was excluded |
> |---|---|
> | the star-evaluation `Parallel.For` degree | swept **1, 2, 4, 8, 48** sequentially — **all identical** to 16 dp |
> | the per-run `optimized_settings.json` (F54's lead) | run **with and without** the file present — identical |
> | the disk detection cache (`<image>_star_detection_result.json`) | `AutoFocusEngine` reads it; the optimizer path does **not**, and the probe's copies have none |
> | OpenCL / GPU dispatch | there is **no** `UMat`/OpenCL path anywhere in the plugin |
> | the frame-level fan-out | each task writes **only its own index**; no shared mutable state |
> | `StarDetectorMetrics.Merge` + thread-locals | additive over ints, and `SortBounds()` normalizes bounds order afterwards |
> | rented-array sorts | both use the **bounded** `Array.Sort(a, 0, count)` overload |
> | `MedianInPlace` over a rented array | both callers pass freshly-allocated `new double[n]` |
> | a load-sensitive timeout | none in the detection/evaluation/fit path; the only `Stopwatch` is trace-only |
>
> **A METHODOLOGICAL TRAP WORTH RECORDING.** The parallelism sweep was first run by setting `HF_STAREVAL_PAR` from
> WSL — and **WSL environment variables do not reach a Windows process without `WSLENV`**, so the first sweep
> silently measured the SAME configuration five times and would have "proved" that degree does not matter. It was
> caught by noticing the result contradicted a hardcoded build, and re-run with `WSLENV` set. *An instrument that
> is not connected reports perfect agreement* — the same failure family as wave 8's three, in a new disguise.
>
> **STILL UNEXPLAINED, and it is the sharpest remaining clue:** across sessions the "sequential" value has been
> observed at BOTH attractors — many repeats at `0.9791727072`, and five consecutive repeats at `0.9885546719`
> from one build. Within any one session sequential runs are perfectly self-consistent. So whatever selects the
> attractor appears to be **stable within a process/session and variable across them**, which does not fit a
> per-iteration data race and does fit some process-level state (a warm-up path, a static initialised from
> observed load, a JIT/tiering effect, or something else acquired once).
>
> ### (b) NARROWED 2026-08-08 (wave 10): the BUILD does not select the attractor
>
> Wave 9's sharpest open clue was that the "sequential" value had been seen at BOTH attractors across sessions,
> with five consecutive repeats at one of them **"from one build"** — raising the possibility that part of what
> this entry calls nondeterminism is build-to-build variation. **It is not.** Fifteen runs — five each on wave
> 9's binary, wave 10's, and a wavelet-bisect build, interleaved in one session on identical folder copies —
> returned `0.9834767969`, identical to ten decimal places. Two binaries a wave apart agree exactly.
>
> **And the seed evaluation is a pure function of (frames, settings, PROFILE).** Under a fixed profile it is
> perfectly reproducible across binaries, folder states and sessions; the profile term is
> [F57](#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it),
> and it explains the cross-SESSION observation wave 9 could not place. **What remains is only the behaviour
> under CONCURRENCY**, which none of these controls touch — so this entry is narrower and no less real.
>
> ### The specific mechanism, named and half-measured — ***and it is NOT this defect's mechanism***
>
> **Superseded 2026-08-08 (wave 11) by [F58](#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections):
> the concurrent processes were running under different NINA PROFILES, and the two attractors partition exactly
> on `MaxOutlierRejections`.** Everything below remains TRUE about the function — the gain is measured and a unit
> test pins it — and it remains a plausible amplifier for some *other* perturbation. It is withdrawn only as the
> explanation of the runs in this entry. **The trigger RATE this section says is owed was owed for a mechanism
> that was not firing**, which is why F58's residual check runs even after its own fix passes: a measured gain is
> not a measured cause, and it is easy to mistake one for the other after paying to measure it.
>
> `CvImageUtility.KappaSigmaNoiseEstimate` is a **bimodal amplifier by construction**: an OpenCV parallel
> reduction (`Cv2.MeanStdDev`) feeds a convergence test at `|Δσ| ≤ 1e-5`, so an arbitrarily small change in σ can
> flip the comparison, buy one more iteration, and move `threshold` — and therefore σ — macroscopically. Two
> discrete outcomes from an infinitesimal perturbation, load-sensitive (OpenCV's pool sees machine load), stable
> within a process, and invisible to the plugin's own `Parallel.For` degree, which wave 9 swept and found inert.
> σ feeds noise clipping ⇒ the star gate ⇒ the star list ⇒ `J`.
>
> **The GAIN is now measured and permanently pinned**: a unit test asserts that one extra kappa-sigma iteration
> moves σ by **≥ 100×** the tolerance that decides whether to take it, and says in as many words that if it ever
> stops being true, this mechanism is no longer plausible and the entry should say so. **The trigger RATE is
> still owed** — that is what the 40-second probe measures, and it is the cheapest remaining step.
>
> **Eliminated by reading, and recorded because it is the most F55-shaped thing in the path:**
> `CvImageUtility.CalculateStatistics` picks its median with a **quickselect whose pivot comes from
> `Random.Shared`** — process-level shared state consumed in a thread-interleaving-dependent order, exactly the
> signature. It is **inert**: quickselect returns the value at rank *n* whatever the pivots were, and
> `Mat.GetArray` hands back a **copy**, so the in-place permutation never reaches the image.

> **Next step.** (a) ~~Re-run the arm sequentially~~ — **DONE**, and it passed both controls (see F32).
(b) **Find the nondeterminism — this now outranks (a) in value, because the reproducer makes it cheap and
because a bimodal race can flip a sequential run too.** Bisect with `determinism_probe.sh`: it is 40 s per trial. (c) Until (b), **treat concurrent `optimize` as invalid for any arm whose
conclusion rests on comparing landings**, and say so in the run instructions beside
[F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)'s
settings-pinning rule. (d) Add a cheap standing guard: have `optimize` print `BaselineJ` where a scripted arm can
diff it, so this control is free on every future arm rather than only when someone writes it down.
Reproduce: `D:\hf_w9\f32_arms.log`, `D:\hf_w9\score_f32.py`, `D:\hf_w9\ctl_seq_toml999.log`,
`D:\hf_w9\ctl_conc_toml999.log`.

> ### (c) IS NOW MECHANICAL, because the written rule was NOT ENOUGH (2026-08-08, wave 10)
>
> (c) said: *"treat concurrent `optimize` as invalid for any arm whose conclusion rests on comparing landings,
> and say so in the run instructions"*. Wave 10 wrote that rule into its own design, into its plan, and into the
> header of the script that ran the pass — **and then ran a 39-run population pass TWICE AT ONCE anyway.**
>
> The mechanism is worth recording exactly, because nothing about it was careless. A background launcher had
> been started to chain the pass behind the gate; it reported **"completed"**, and a `ps` check showed no
> surviving process, so a second launch was issued. The launcher's *shell* had exited; its `nohup`'d driver had
> not, and it was in a process namespace the checking shell could not see. Two drivers, two `optimize`
> processes, the same bank folders.
>
> **It was caught by an instrument built for something else.** A trace diff — written to find the FIRST
> divergent evaluation between two runs of one invocation — reported an impossible ladder, and the log turned
> out to contain two complete optimizations. *(That same instrument had to be fixed first: its initial version
> diffed raw progress lines, which come partly from a wall-clock timer, so it reported divergence on every pair
> of runs whether or not the search diverged. Asked "what would this do if the thing it checks were completely
> broken?", the answer was "exactly the same thing".)*
>
> ### (c)'s FIRST POSITIVE CONTROL, 2026-08-08 (wave 11) — and it found a real limit
>
> Wave 11's probe ran the guard in **both** directions for the first time: five sequential runs (must read
> `exclusive`) and five concurrent ones (must read `concurrent`). **Both fired**, so the field works. But the
> concurrent phase returned **four `concurrent` and ONE `exclusive`** — and that is correct behaviour, not a bug:
> `WaitOne(0)` is won by exactly one of *N* contending processes, so **in any fan-out precisely one landing
> truthfully reports `exclusive`.**
>
> **Therefore `ConcurrencyCheck == "exclusive"` on a SINGLE landing is not evidence that the machine was quiet.**
> It says *this process won the mutex*. The check must be read **across a whole arm** — one `concurrent`
> anywhere condemns all of it — and in wave 10's own two-driver contamination the first driver's landings would
> have read `exclusive` throughout, so an operator sampling that driver's output would have seen a clean bill of
> health. *An instrument that is right about the wrong scope is a new way to be wrong.* Recorded here rather than
> "fixed", because the field is honest and it is its INTERPRETATION that needed pinning down.
>
> **What ships.** `optimize` claims a named mutex at startup and records `ConcurrencyCheck` —
> `"exclusive"` / `"concurrent"` / `"unknown"` — into `OptimizerProvenance`, i.e. into **every landing it
> writes**, and prints a warning naming F55. **Three values and not two**, for the same reason
> `WingRejectedFraction` is NaN-never-0: a check that could not run must not read as a check that ran and found
> nothing. A scorer can now refuse a contaminated arm **after the fact**, without anyone having been watching.
>
> **The lesson generalises past this entry.** A rule that depends on the operator noticing a violation is not a
> control; it is a hope. This project's own register is full of the mechanical version of the same move —
> `BaselineJ` as a free control (F41), `DetectionBinningSource` as a field rather than a sentence (F39(a)),
> `BuildId` rather than a version string (F53). *If a rule matters, make the violation visible in the output.*
>
> *(The pass's own driver script also gained a pre-flight guard — and its first version was broken in the most
> on-the-nose way available: `grep -c` prints `0` and exits `1`, so `$(... || echo 0)` produced the string
> `"0\n0"`, the numeric comparison errored, and the guard fell through reporting SAFE unconditionally. It was
> then re-validated against a positive control — a deliberately-started `optimize` — before being trusted, which
> is the check the wave-9 lesson asks for and which the first version would have failed.)*

> ### CLOSED AT THE LANDING LEVEL (2026-08-09, wave 12) — RULE A12, and the hazard was fully present
>
> Wave 11's K5 proved agreement at the **SEED** level only (`--max-evals 1`). This entry's headline number is a
> **LANDING** rate of **44 %**, triple the seed rate, and nothing had measured landings under concurrency with
> the fix in place. Wave 12 ran wave 9's arm A again — the eight gate runs, **at fan-out 4** — and required RULE
> G12's eight values.
>
> | clause | verdict | evidence |
> |---|---|---|
> | **A1** the eight values to 6 dp | **PASS** | 8 of 8, and **bit-identical to all sixteen digits** |
> | **A2** ≥2 profiles spanning both `MaxOutlierRejections` groups | **PASS** | **FOUR** distinct profiles; **4 runs at `MOR`=0, 4 at `MOR`=1** |
> | **A3** the fan-out actually OVERLAPPED | **PASS** | exactly ONE `exclusive` per batch, three `concurrent` — what `WaitOne(0)` must produce at degree 4 |
> | **A4** `FitInputs` identical on all eight | **PASS** | one distinct value across four different profiles |
>
> **A2 is the clause that makes this mean anything.** Four processes drew four different profiles and split 4/4
> on the integer that used to decide `J` — **pre-fix these eight landings would have partitioned into two
> groups.** The hazard was fully present and did not reach a single digit. At the measured 44 % per-run rate,
> `P(8 of 8 | the defect is live) = 0.56⁸ ≈ 0.0097`.
>
> **THE AUTHORISATION AND ITS TWO LIMITS.** Fan-out is authorised **AT DEGREE 4**, and only **UNPINNED**:
> `--profile-id` + fan-out still fails loudly (wave 11's K3), so a fanned-out arm relies on `--settings`
> carrying the fit inputs. **An arm that needs a specific profile still runs sequentially.** Nothing here says
> anything about degree 8 or 48.
>
> **And it is worth 1.33×, not 4× — see
> [F60](#f60--fan-out-is-now-safe-and-it-is-barely-worth-doing-optimize-already-saturates-the-machine).**
> Reproduce: `D:\hf_w12\fanout_w12.sh`, `D:\hf_w12\score_w12.py`, `D:\hf_w12\fanout_score.txt`,
> `D:\hf_w12\profiles_before_w12.txt`.

### F54 — F39(b)'s default flip MOVES a landing at a resolved factor of 1, where it is documented as a no-op
**Status:** Open · found 2026-08-07 (wave 9) chasing [F53](#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it)'s
remainder · **measured and reproducible; the MECHANISM is not identified and is not claimed**

Wave 8 flipped `optimize --per-run`'s default to apply each run's derived detection binning, and recorded the
opt-out as leaving prior arms reproducible. On `D:\hf_w7\armE\t0.5\D02_rich_135mm` — **one binary, one pinned
settings file, one set of frames, and a resolved factor of 1** — the two paths land in different places:

| | landed gate | σ_focus | `bestJ` |
|---|---|---|---|
| default (flip ON), factor **1** from `synthetic_meta.json` | **33.333** | **0.10063** | 0.996486 |
| `--no-run-detection-binning` | **16.667** | **0.10927** | 0.996328 |

The second row reproduces wave 7's arm E and wave 8's arm X **exactly**, which is what makes this the explanation
for F53's residue rather than a second unexplained thing.

**Everything the harness prints about the seed is IDENTICAL between the two runs** — `Sensitivity=10`,
`StarClippingMultiplier=2`, `NoiseClippingMultiplier=4`, `StructureLayers=4`, and the per-run
`PixelScale 5.74486 arcsec/px (frame header)`. The only difference is the two `DetectionBinningResolver.ApplyFactor(_, 1)`
calls the flip enables.

**Why that is surprising.** `ApplyFactor(p, 1)` reads as the identity when `p.DetectionBinning` is already 1:
`unbinned = PixelScale / max(1, 1)`, then `PixelScale = unbinned × 1`. `StarDetectorParams.DetectionBinning`
**defaults to 1** (`IStarDetector.cs:495`), so the 0→1 hypothesis is dead. And
`HocusFocusStarDetection.ApplyDetectionImageContext` **overwrites** `detectorParams.DetectionBinning` from the
options override at detect time anyway, so the field the resolver writes is not even the one the detector uses.

**What is NOT the cause**, each excluded rather than assumed:

- **Not concurrency** — the two runs bracket each other under identical load, and a third control (F53) matched a
  different binary to 6 dp with three `optimize` processes in flight.
- **Not this wave's code** — `D:\hf_w8\exe` and wave 9's `exe2` agree with each other on the flip-ON value.
- **Not `EffectiveStructureLayers`** — wave 8's F52(a) extraction is a faithful one (identical branch order and
  expressions), and its `CostNoteFor` calls `Materialize`, which **clones** the seed and has no side effects.

**Why it matters, and it is not academic.** Wave 8's adoption control reported *"every binning-1 dataset is
bit-identical between arm A and arm B … 0 control violations of 13"* — **the same comparison this entry runs, with
the opposite result.** The two differ in one respect worth chasing first: wave 8's control ran on the BANK folders
while this runs on wave 7's rendered `armE` copies, and those copies already contain an `optimized_settings.json`
written by a previous `--per-run` pass ([F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings)),
which `HarnessSettingsStore.ResolveForRun` reads. If a per-run settings file can change what the flip does, that is
the F42 shadowing concern arriving through a different door.

**This wave's F32 arm is NOT affected, and that is measured rather than assumed.** RULE G reproduced all 8
comparability runs to 6 dp **with the flip ON, on the bank folders**, against wave 5's values.

**Next step.** (a) Reproduce on a BANK folder with and without the flag, to separate "the flip moves landings" from
"a stale per-run `optimized_settings.json` moves landings". (b) If it is the per-run file, that is F15 causing a
measurement error rather than merely destroying provenance, and it raises F15's priority sharply. (c) Either way,
`ApplyFactor` at factor 1 should be provably inert or should not be called — a normalization step that changes an
answer is worse than no normalization.
Reproduce: `D:\hf_w9\wing\ctl_nobin.log` vs `D:\hf_w9\wing\opt_0.5_D02_rich_135mm.log`.

### F53 — Wave 8's arm X does not reproduce from wave 8's own `exe`, because the arm ran on an EARLIER build of it
**Status:** Open · found 2026-08-07 (wave 9) re-running arm X's exact command to build the wing instrument ·
**the reproduce line is stale, and nothing in the artifact says so**

Wave 8's F19 arm X is recorded with `Reproduce: D:\hf_w8\armX\arm_x.sh`, which invokes
`D:\hf_w8\exe\TestApp.exe`. **Running that script's exact command on that exact binary today does not reproduce
the write-up's numbers.**

| | `D02_rich_135mm` @ 0.5 s, `--max-evals 120`, same pinned settings |
|---|---|
| wave 8's arm X log | `bestJ = 0.996328`, effective gate **16.667**, σ_focus 0.10927 |
| `D:\hf_w8\exe` **today** | `bestJ = 0.996486`, effective gate **33.333**, σ_focus 0.10063 |
| wave 9's `exe2` (this branch) | `bestJ = 0.996486` — **identical to the line above** |

**The tell is in the log, and it is unambiguous.** Wave 8's arm X log contains **no `detection binning (F39b)`
line at all**; both of today's runs print one. That line is unconditional once wave 8's own F39(b) default flip
landed, so **arm X was executed against a build of `D:\hf_w8\exe` that predates the flip**, and the directory was
rebuilt later in the wave. The artifact directory keeps only the LAST build, so the script and the numbers it
produced now disagree with nothing recording that they should.

**Three things this is NOT.**

1. **Not concurrency.** The control above was run with three `optimize` processes already in flight and matched a
   run of a different binary to six decimal places. If a fan-out perturbed landings, these two could not agree.
2. **Not this wave's code.** `D:\hf_w8\exe` and wave 9's `exe2` — different binaries, one of them predating every
   line of wave 9 — give the same answer.
3. **Not `ApplyFactor`.** `DetectionBinningResolver.ApplyFactor(p, 1)` is the identity when `p.DetectionBinning`
   is already 1 (`unbinnedPixelScale = PixelScale / 1`, then `PixelScale = unbinnedPixelScale × 1`), which the
   pinned settings guarantee. That is also why wave 8's own adoption-arm control found the 13 binning-1 datasets
   bit-identical. The flip is a marker for WHICH BUILD ran, not the cause of the movement.

**Does it overturn wave 8's F19 verdict? No, and the reason is worth stating.** Arm X's finding was that `D02`
reports `ExposureIsNotTheLimit` with a raw ask of 0.000 s at every rung. Both landings put the EFFECTIVE gate
(16.667 and 33.333) far above `TargetSensitivity = 10`, and every accepted star's gate statistic strictly exceeds
that gate — so `ExposureIsNotTheLimit` is true by construction in both. **R5 fired for a structural reason, not a
numerical one, so a different landing reaches the same verdict.** What is void is the reproducibility of the
specific table, not its conclusion.

**Why it matters.** [F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary) says a prior
wave's control arm is not a control for a later wave's binary. This is the sharper case: **a prior wave's arm is
not a control for its OWN recorded binary either**, when the arm directory is a build output that later steps in
the same wave overwrite. Every `Reproduce:` line in `docs/` that names a `D:\hf_w*\exe` inherits this.

> ### (a) SHIPPED 2026-08-08 (wave 10) — and the field that CLAIMED to identify the build did not
>
> `OptimizerProvenance.ProducerVersion`'s own doc said a landing "also identifies the build it came from". It
> cannot: two builds of one version share it, which is precisely how wave 8's arm X came to be irreproducible
> from its own recorded `exe`. **`BuildId` — the assembly MVID, which the compiler regenerates on EVERY build —
> is the field that answers "which build",** and `DetectorVersion` says which output contract produced the
> numbers (wave 9's entire results doc needed a hand-written banner for want of it). Both are printed by
> `optimize` and stored in every landing. [F57](#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it)
> then added `ProfileId` for the same reason, one input further out.
>
> **A RETROACTIVE instrument for the artifacts that predate the stamp:** `strings <dll> | grep AtrousWaveletFast`
> distinguishes a `StarDetectorVersion` 1 build from a 2 build **without running it**. Wave 10 used it to
> establish that all four of wave 9's build directories are v1 rather than assuming it — which is this entry's
> lesson applied to itself.
>
> **(b) still stands unchanged**: a `Reproduce:` line names a COMMAND, not a result. Stamping the build removes
> the need to INFER which binary ran; it does not make an old number reproduce.

**Next step.** (a) ~~Stamp the build into the run~~ — **DONE, see above**. (b) Until then, treat a `D:\hf_w*\exe` reproduce line
as naming a COMMAND, not a result — re-derive the numbers rather than quoting them across waves. (c) Prefer
per-arm build directories that are never rebuilt mid-wave, which
[F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)
already asks for on a different ground.
Reproduce: `D:\hf_w9\wing\ctl_w8bin.log` against `D:\hf_w8\armX\opt_0.5_D02_rich_135mm.log`.

### F50 — A false-negative gate count is an UPPER BOUND on what relieving that gate buys, not an estimate
**Status:** Open · found 2026-08-06 (wave 8) refuting F46's `MinimumStarBoundingBoxSize` hypothesis · **a reading
error, not a code defect — but every prior wave has read these tables the other way**

`golden eval`'s false-negative attribution names the **first** gate a missed golden star hits. Wave 8 relieved one
of them and measured what came back:

| `D12_c14_585_afbin2` at binning 2 | `--min-box 5` (shipped) | `--min-box 2` |
|---|---|---|
| `REJECTED:TooSmall` | **6** | **0** |
| `REJECTED:TooDistorted` | 19 | **22** |
| `REJECTED:LowSensitivity` | 34 | **37** |
| **recall@all / recall@high** | 0.675 / 0.813 | **0.675 / 0.813** |

The six `TooSmall` rejections vanish and **not one star is recovered** — they fail the next gate instead. The
result is byte-identical on every tier.

**Why it matters.** Wave 7's F39(b) table, F46's entry, and the noise-clip sweep write-ups all read a gate's FN
count as "what this gate is costing us". It is not: it is the count of stars that reach that gate first, which
bounds the gain from above and can be an arbitrarily loose bound. A candidate that is too small is usually also
distorted and faint — the gates are correlated because they are all measuring the same marginal blob.

**Why it matters MORE than a reading habit.** `GateRecommender` recommends a threshold *per gate* from exactly
these counts (`GateRecommender.cs:285` maps `RejectionGate.TooSmall` → `MinimumStarBoundingBoxSize`). A
recommender driven by a bound it treats as an estimate will happily loosen a gate for no gain — and each such
loosening is a real precision cost on some other frame.

**Next step.** Attribute a false negative to the SET of gates it would have to clear, not to the first one — the
detector already evaluates them in sequence, so the cheap version is "re-run with this gate disabled and see what
comes back", which is what wave 8 did by hand. Until then, treat every FN-by-gate number in
`docs/` as an upper bound and say so where it is quoted.
Reproduce: `D:\hf_w8\p1\knob_sweep.sh` (the `p1_minbox*` arms).

### F49 — The Star signal block fires on a floored gate but every remedy it owns is an EXPOSURE remedy, so a rich, well-exposed field gets a diagnosis with no instruction
**Status:** Open · found 2026-08-06 (wave 8) from a **field report on the shipped `Default` profile**, not from the
bank · mechanism confirmed in source

The user ran the optimization wizard twice on their own rig. The second landing: **Sensitivity 15.667 → 0.000**,
**StarClippingMultiplier 6.750 → 0.250**, stars per frame **834 → 5766** (≈ 7×), σ_focus 6.12 → 0.55. The Star
signal block fired and said:

> *"Brightness Sensitivity is at the bottom of its range (0.000): this focus result rests on low-confidence
> detections. Star brightness is not the problem: your brightest stars measure S/N 1438.6, meeting the default S/N
> target of 10. Brightness Sensitivity is low, so far fainter candidates are being admitted below them."*

…and then stopped. **No remedy sentence at all**, on a page whose whole contract is "diagnosis plus exactly one
instruction". The user's words: *"there's no guidance regarding how to get a better optimization with a non-zero
sensitivity."*

**Mechanism — this is structural, not a missing string.** `StarSignalCopy.RemedyFor` has exactly three ranked
branches, and **all three are about exposure or binning**:

| # | remedy | gate |
|---|---|---|
| 1 | change the detection binning factor first | `DetectionBinningDiffers && IncreasesExposure` |
| 2 | auto-focus through a broadband filter instead | `HasRecommendation && !ExposureIsNotTheLimit && !IncreasesExposure` |
| 3 | get frames at the longer exposure | `IncreasesExposure` |

On this run `ExposureIsNotTheLimit` is **true** (S/N 1438.6 against a target of 10; the row reads "2 s
(unchanged)"), so `IncreasesExposure` is false and branch 2's `!ExposureIsNotTheLimit` is false. **Every branch
falls through and the method returns `string.Empty`.**

**Why it matters.** The block's TRIGGER is `OptimizationSummary.HasLowStarSignal` — the Sensitivity gate alone —
while its CONTENT is exposure-only. So on a **rich, well-exposed field where the optimizer CHOSE a floor gate**,
the trigger fires, the copy correctly says brightness is not the problem, and then the block has structurally
nothing to offer. Worse, **there is no user lever even if it had one to name**:
[F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
`OptimizerSettings.MinDetectionKeepFraction` has **no XAML binding anywhere** — it is `--keep-floor` on the harness
only — and the search's Sensitivity lower bound is not exposed either. (Note the keep floor would not have bound
here in any case: it rejects candidates keeping LESS than φ of the seed's stars, and this landing keeps ~7× MORE.
The pathology is admission, not shedding — the *other* end of the same axis, which F32 has never measured.)

**This is the exact MIRROR of [F19](#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one).**
F19 is the block staying **silent** on a population that would benefit from what it knows; this is the block
**speaking** to a population it cannot help. Both say the same thing one level up: **the block's trigger and the
block's content are keyed to two different quantities**, and any fix to F19's trigger must not deepen this.

**Also in the same report, and separable: the step recommender widened again, from a NARROW sweep.** 214 → **459**,
labelled *"capped by this sweep's width; re-run auto-focus to refine"*. The arithmetic checks out from the
screenshot's own focuser axis — 11 points at spacing 214 ⇒ sampled half-span 1070; `MaxHalfWidthSampledHalfSpanMultiple
= 1.5` ⇒ 1605; 1605 / 3.5 = 458.6 → **459** — so the cap BOUND, i.e. the recommender wanted more still. The cause
is visible in the curve: HFR runs from ≈1.9 px at minimum to ≈3.5 px at the edges, so the sweep never reaches
`3 × HFR_min ≈ 5.7 px` and `FindHalfWidth` extrapolates on every run. It will therefore ask to widen on **every**
run until the sweep actually reaches 3×, and **nothing on the page says that this terminates, or after how many
runs**. This is the same instability
[F21](#f21--stepsizerecommenders-half-width-is-not-stable-against-noise-even-at-r--10000) names, approached from
the narrow side rather than [F25](#f25--from-a-far-too-wide-sweep-the-step-recommender-widens-it-further-instead-of-recovering)'s
wide side, and the user experienced it as a runaway rather than as convergence.

> ### (a) SHIPPED 2026-08-07 (wave 9) — and the branch is narrower than this entry proposed, on purpose
>
> `RemedyFor` gains a fourth, last-ranked branch for the floored-gate + `ExposureIsNotTheLimit` state:
>
> > *"The gate was lowered to admit more candidates, not because star brightness was missing. Set Brightness
> > Sensitivity by hand in the star detection options and run this wizard again in "use current settings" mode to
> > compare that landing against this one."*
>
> **It names only controls that EXIST**, per the house rule at `ShowOptimizeAgainAtRecommendedBinning`.
> `BrightnessSensitivity` is bound in `OptionsDataTemplates.xaml`
> (`StarDetectionOptions.BrightnessSensitivity`); `MinDetectionKeepFraction` is **not named**, because it has no
> XAML binding anywhere — verified, not assumed.
>
> **It REQUIRES a measurement, which this entry's proposed wording did not.** The sentence CLAIMS that brightness
> was not what was missing, and that is knowable only from `ExposureIsNotTheLimit` (S_now ≥ the default target).
> On a run with no derivable recommendation at all — too few usable frames, no per-star SNRs, an unknown exposure
> to scale from — all that is known is that the gate is floored, so those runs keep their diagnosis-only body.
> **Asserting it there would break the entry's own "no star-poor claim without evidence" rule from the opposite
> direction**, and two existing golden tests caught exactly that on the first attempt.
>
> **Tests: 4, three of them discriminating.** The pre-existing whole-body golden for this state is the sharpest —
> it asserted a string that ENDED after the admission sentence, which is the defect written down as an
> expectation. Plus: the remedy is last-ranked and does not displace an available exposure remedy; an unmeasured
> run gets no gate remedy; and the copy names no control that does not exist.

> ### (c) SHIPPED 2026-08-08 (wave 10), with an EXACT ratio and no projected run count
>
> A capped step recommendation now says it is a **partial step**, names what it converges toward
> (`3 × HFR_min`), reports the HFR dynamic range **the sweep actually measured**, and quotes the exact factor the
> next capped run will apply: `MaxHalfWidthSampledHalfSpanMultiple × P / PointsPerSide` — **1.714× at 4 offset
> steps, 2.143× at 4 offset + 1 recovery**.
>
> **The ratio is a property of the ALGORITHM, not of the fit**, so nothing in it depends on the extrapolated
> half-width the cap exists to distrust. That is why it is quotable and **a projected run COUNT is not** — that
> would have to come from the very extrapolation being distrusted.
>
> **Measured against 22 capped rounds already on disk**, at zero compute: wave 7's F18 control arm lands every
> one of them at a realized **1.667–1.750** (integer-step rounding). This entry's own field session ran
> 100 → 214 → 459 at P = 5, and 100 × 2.143 = 214.3, 214 × 2.143 = 458.6 — matching the 459 this entry derives
> independently from the screenshot's focuser axis.
>
> **It does terminate**, which is the half of the user's experience that is not a defect: the widening is
> geometric until the sweep contains the band, and their runs 3 and 4 asked +3 % and +5 %. A unit test replays
> the sequence and asserts the cap releases.

**Next step.** Three separable pieces. **(a)** ~~Give the floored-gate + `ExposureIsNotTheLimit` state a remedy of
its own~~ — **DONE**; the honest one names the gate, not the exposure. **(b)** Decide
whether a user-facing floor on the search's Sensitivity (or F32's keep fraction, exposed) is the right lever, since
today there is none. **(c)** ~~When the step recommendation is capped by the sweep width, say what it is converging TOWARD~~ —
**DONE, see above**, and with a measured range and an exact ratio rather than only a sentence.
Reproduce: field report, `Default` profile, 2026-08-06; wizard screenshot in the wave-8 thread.

---

## Harness / tooling

### F79 — A single non-ASCII byte in a redirected log makes `grep` report ZERO matches for strings elsewhere in the file
**Status:** Open (fix identified and priced; deliberately **not** shipped in wave 23) · found 2026-08-12, wave 23,
when the gate driver and its scorer disagreed 0-of-8 against 8-of-8 over the same eight files

The register already records that *"a Unicode character in a redirected log arrives as the single byte `0x1A` on
this machine's console code page."* **That is one of two failure modes, and it is the harmless one.** This is the
other, and it fails **closed to zero**: it reports *"the feature is absent"* where the honest answer is *"I could
not look."*

**Evidence.** `gate_w23.sh` and `score_v23_w23.py --gate` ran eight minutes apart over the same 8 gate logs:

| clause | the driver, via `grep` | the scorer, via Python `bytes` |
|---|---|---|
| exactly one `PARAMS-DUMP optimize/detected BEGIN` per log | **0 of 8** | **8 of 8** |
| exactly one `optimize/baseline` | **0 of 8** | **8 of 8** |
| exactly one `optimize/seed` | **0 of 8** | **8 of 8** |

The scorer is right. `/mnt/d/hf_w23/gate/toml999.log` is 29 041 bytes and contains **exactly one** byte `0xE5`,
at offset 7975, in the line

```
  objective: marginalSnr strength=0 floor=6<0xE5> threshold=0.05; searchable Sensitivity lower bound=0
```

**Mechanism.** `0xE5` is `σ` in **CP437** — `'σ'.encode('cp437') == b'\xe5'`, while CP850 and CP1252 have no `σ`
at all and would have emitted `?`. So `Console.OutputEncoding` is the OEM console code page 437 (there is **no**
encoding configuration anywhere in this repo: no `Console.OutputEncoding` assignment, no `Console.SetOut`, no
`StreamWriter` encoding argument, and the referenced `Serilog.Sinks.Console` package is unused). The emitter is a
bare `Console.WriteLine` at `Joko.NINA.Plugins/TestApp/OptimizationDiagnosticRunner.cs:866` (`floor={…}σ`), with a
second `σ` at `:875`. A lone `0xE5` is **invalid UTF-8**, so `grep` classifies the whole file as binary.

**And it does not say so.** `grep -c 'PARAMS-DUMP optimize/baseline BEGIN' toml999.log` prints **nothing** and
exits **1** — "no lines selected" — for a string that is demonstrably present; `grep -ac` prints `1` and exits 0.
There is no *"Binary file matches"*, no `0`, and no warning. The driver idiom `grep -c "…" || true | grep -c '^1$'`
turns that silence into the number **0**, indistinguishable from a genuinely absent block.

Two further properties make it worse than it looks:

- **Position does not protect you.** All three `BEGIN` lines sit at offsets 1813, 3551 and 6179 — *before* the
  offending byte at 7975 — and are still not found. The file is smaller than one read buffer, so the whole file is
  classified. **On a larger log, matches in earlier buffers survive**, so the same clause can silently start
  working again as a log grows. It is size-dependent.
- **Two bytes, two consequences, one root cause.** A character that CP437 *can* represent (`σ`, `°`, `µ`, `²`,
  `±`, `≥`) becomes a high byte and breaks `grep` entirely; a character it *cannot* (`—` U+2014, the recorded
  case) becomes `0x1A`, which is valid ASCII, so the text is corrupted but `grep` keeps working. **The recorded
  trap is the survivable one.**

**Scope, measured rather than assumed.** The byte is present in **8 of 8 gate logs in every wave root that has
one — waves 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 and 23** (twelve roots; wave 22 built no binary). It has
been latent since **wave 11**. It is **not** a regression from any recent merge, and **no pre-registered clause
was ever harmed**: in wave 23 the clause passed on its designated instrument, which reads bytes in Python.

**Why one clause survived and its neighbour did not, in the same script.** `G23-P1` extracts its block with
`awk '/BEGIN/,/END/'` **first** and pipes the result to `grep`; `awk` performs no binary classification and the
offending byte lies outside the extracted range, so `grep`'s stdin is clean. `G23-P2` greps the raw file.
*One clause was written awk-first by accident of style and is immune; the other is not.*

> **The trap caught the write-up too.** The first per-wave census written for wave 23's results used
> `grep -c $'\xe5'` and reported **0 logs containing the byte across all twelve waves** — including the ones it
> was reading. The number was produced by the instrument, not by the data. Re-run in Python, on bytes, it is 8 of
> 8 everywhere.

**Next step — the fix is two lines and it is log-only.** The `σ` at `:866` sits inside a bare `Console.WriteLine`;
the statements that mutate search inputs are above it and the optimizer is constructed after it (`:869`). Nothing
parses stdout and no test or doc asserts on the string, so **no option below can perturb a measured result.**

| option | change | reach | risk |
|---|---|---|---|
| **(i)** ASCII the two literals (`floor=6 sigma`) | `OptimizationDiagnosticRunner.cs:866,875` — **2 lines** | the `optimize` console stream, i.e. every gate log | none |
| **(ii)** `Console.OutputEncoding = new UTF8Encoding(false)` in `Main` | `TestApp/Program.cs` — **1 line** | all **32** non-ASCII console sites across 9 files, and every future one | the setter throws `IOException` with no valid console handle, so it needs `try`/`catch`; existing CP437 logs stay broken |

**(i) is recommended, and it must land BEFORE a wave's build, not during one** — a binary that differs from the
one under test is not the one under test ([F53](#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it)(c)).
**Until it ships: any driver clause that greps a redirected `TestApp` log must pass `-a`, or extract with `awk`
first, or read bytes.** A `grep` that returns rc=1 on a log is not evidence of absence.

> ### SHIPPED IN WAVE 23 — and BOTH options above were UNDER-SCOPED by the same defect they describe
>
> **Status: FIXED (2026-08-12, wave 23).** Option (i) shipped, but **not at the size this entry priced it.**
>
> | | this entry said | what shipped |
> |---|---|---|
> | option (i) | **2 lines** (`:866,875`) | **120 lines, 158 characters, 20 files, 19 distinct characters** |
> | option (ii)'s reach | "**32** non-ASCII console sites across **9** files" | the same 120 / 20 |
>
> **Why both counts were low, and it is the entry's own subject.** Every one of them came from a *line-keyed*
> `grep` for `Console.Write`-shaped text. Two classes of violation carry no such text on their own line:
> **continuation lines** of a multi-line `Console.WriteLine(… + …)`, and **interpolation holes** such as
> `{"tilt°",8}`, where a quote-parity scan flips polarity at the nested `"` and stops seeing the literal.
> **`OptimizationDiagnosticRunner.cs:866` — the line this entire entry is about — is a continuation line, and
> the controller's own scoping grep did not return it.** A line-keyed grep hunting a defect whose signature is
> *failing closed to zero* failed closed on the defect's own line, twice, in the entry that documents it.
>
> **The guard therefore asserts the property, not the shape.** `TestAppOutputAsciiTests` requires that **outside
> a comment, every character in `TestApp`'s sources is ASCII** — deliberately not "on a line that looks like it
> prints". A guard against a fails-closed defect must not be able to fail the same way. It carries **four**
> separate could-not-look failures (directory missing, zero files, fewer than 40 files, four named sentinels
> absent, under 200 000 non-comment characters scanned), each asserted **before** the violation list is read,
> and a **second** test that pins the scanner against a synthetic snippet so the property cannot pass vacuously.
> Shown to FAIL against pre-change source, verified by the controller independently of the agent that wrote it:
> `Failed: 1, Passed: 1`, naming `OptimizationDiagnosticRunner.cs:866 col 143: 'sigma' (U+03C3)`.
>
> **F53(c) was honoured.** The fix landed **after every measurement in wave 23 was complete** — gate, both arms
> and all fingerprint checks — so the binary under test never contained it, and `D:\hf_w23\exe\TestApp.dll` was
> re-hashed to `82491ac79ce3d9216b4fa0d3…` afterwards to prove it. Wave 24 is the first wave whose build carries
> the fix, and is therefore the first whose logs are greppable without `-a`.
>
> **One operational fact found on the way, worth more than it looks:** `dotnet test <sln>` **does not build
> `TestApp`**. A compile break in any runner would not surface in a full-suite run. Build it separately.
>
> **`ParamsDump.cs` is a NEAR-MISS, not a self-refuting file** — recorded because the controller first reported
> it as one. Its class doc's *"every character this class prints is printable ASCII"* was true **as literally
> worded**: `Lines`/`Write` were already guarded by an `Ascii()` helper with a test. Its single violation is at
> `:182`, in an `ArgumentException` message, which is **thrown, not printed**. The doc now covers every string
> literal in the class including that throw.

> ### VERIFIED IN THE FIELD (2026-08-12, wave 24, `G24-P4`) — AND THE FIX EXPOSED A SECOND DEFECT IT HAD BEEN MASKING
>
> **Status: the ASCII fix HOLDS.** Wave 24 is the first wave whose binary carries `e7a5ee6`, and its gate is the
> cheapest possible verification. The clause was pre-registered as **non-blocking** (a predecessor's cosmetic
> deliverable must not cost a wave its arms) with **both** branches reachable, and its FAIL end was measured
> **first, on real published artifacts**:
>
> | gate | files with >= 1 byte >= 0x80 | total | verdict |
> |---|---|---|---|
> | `/mnt/d/hf_w23/gate` (FAIL end, run first) | **8 of 8** | 8 — one `0xE5` each, offsets 7975–8153 | `P4-NON-ASCII-PRESENT` |
> | `/mnt/d/hf_w24/gate` (the first gate built after the fix) | **0 of 8** | **0** | **`P4-ASCII-CLEAN`** |
>
> Counted in **Python on bytes**, never `grep` — *a clause about a fails-closed defect must not use the instrument
> that fails closed.*
>
> **AND THE SAME DRIVER PRINT STILL READS `0 of 8`, FROM A COMPLETELY DIFFERENT CAUSE.** On a gate that had just
> reproduced K8 bit-identically, `gate_w24.sh` printed `[G24-P2] logs with EXACTLY ONE 'optimize/detected' block:
> 0   expected: 8` — and the same for `optimize/baseline` and `optimize/seed`. **Character-for-character what wave
> 23 printed, and not one byte of the cause is shared.**
>
> | wave | what `grep` sees | driver prints | why |
> |---|---|---|---|
> | 23 | **0** hits (whole file classed binary by one `0xE5`) | `0 of 8` | this entry: fails **closed** |
> | 24 | **2** hits per log | `0 of 8` | the driver counts **LINES MENTIONING** the string; a block is a `BEGIN`/`END` **pair**, so the true count is 2 and *"exactly one"* is **never satisfiable** |
>
> Measured on `/mnt/d/hf_w24/gate/toml999.log`: `136:PARAMS-DUMP optimize/detected BEGIN`,
> `192:PARAMS-DUMP optimize/detected END`. **The pre-registered clause was unaffected and needed no repair** —
> `score_g24_w24.py` parses **blocks** and returns **8 of 8 = PASS** on the same eight files in the same minute.
>
> **Four durable points, and they are why this extends F79 rather than opening its own entry.**
>
> 1. **A wrong number can have more than one cause, and fixing one cause does not validate the instrument.** The
>    `σ` had been masking a second bug **in the same statistic, in the same script**, for that driver's whole life.
> 2. **Wave 23's diagnosis — *"it's the σ"* — was TRUE and INCOMPLETE.** Had the `σ` never existed, that line
>    would still have read `0 of 8`, on all three blocks, on every wave from 11 to 24.
> 3. **Both defects emit the identical string, so the output alone could never separate them.** The only reason
>    this was caught is that **the scorer and the driver compute the same clause by different routes and were
>    compared.** That comparison is now a load-bearing control and should be named as one in any wave that keeps
>    a convenience print beside a scored clause.
> 4. **And the two must not be assumed to compute the same statistic.** Here one counts **blocks** and the other
>    counts **lines** — a difference completely invisible in the output.
>
> Reproduce: `/mnt/d/hf_w24/g24_ascii.txt` (clean), `/mnt/d/hf_w24/g24_asciifailend.txt` (the FAIL end),
> `/mnt/d/hf_w24/g24_p2p3.txt` (the scorer's 8 of 8) against `/mnt/d/hf_w24/gate_w24.log` (the driver's 0 of 8);
> `docs/synthetic-af-bank-followups-wave24-results.md` §2.

Reproduce: `python3 -c "d=open('/mnt/d/hf_w23/gate/toml999.log','rb').read(); print(d.count(b'\xe5'), d.find(b'\xe5'))"`;
`grep -c` vs `grep -ac` on the same file; `docs/synthetic-af-bank-followups-wave23-results.md` §2.

### F80 — Instruments derived by textual substitution keep their predecessor's prose, and it silently stops describing them
**Status:** Open (remedy specified and priced) · found 2026-08-12, wave 24, when a scorer refused its own PASS end
because a `sed` line renamed the paths but not the module

Every wave in this series carries its measurement instruments forward by `sed`-ing the previous wave's copies —
deliberately, and for a good reason: *editing an instrument mid-series is how it stops being the same instrument,*
and the gate must be read by the same code path as the thirteen binaries before it. **The derivation is the
problem, not the carrying-forward.** `sed` rewrites what it is told to rewrite; **everything else keeps saying
what the previous wave said**, and nothing checks that an instrument's output still describes the instrument.

**Evidence — eight prose-vs-mechanism disagreements across five instruments in ONE wave, one load-bearing.**

| # | instrument | the drift |
|---|---|---|
| 1 | `prov_w24.py` | active log labels read `RULE G23` while the wave's rule is `G24` |
| 2 | `prov_w24.py:75` | a **historical** label reads *"wave 21 (RULE G23's …)"* — wave 21's rule was **G21**; wave 23's own `sed` rewrote its predecessor's history. **Each derivation corrupts the labels of the ones before it** |
| 3 | `gate_w24.sh:221` | header prints *"ONE binary, the **thirteenth**"* on the **fourteenth** |
| 4 | **`score_g24_w24.py:275`** | **resolves `os.path.join(HERE, "prov_w23.py")`, a file that does not exist. LOAD-BEARING — see below** |
| 5 | **`prov_w24.py:179`** | prints a hardcoded *"differs from waves 11/12/13/14/15/16/17/18-B1/18-B2: YES"* — **NINE waves named while THIRTEEN are checked** |
| 6 | `prov_w24.py:227,304` | the self-test banner and usage string print **`prov_w23.py`**; the published artifact `prov_w24.txt` therefore opens by naming a different file |
| 7 | `v23_fingerprint_w24.py:2,171` | docstring says *"the **wave-21** synth-validate reports"* and the PASS line prints **"38 of 38 wave-21 f21 files BYTE-IDENTICAL"** — the population is **wave 23's** 38 reports. **The published class-4 control output names the wrong wave's evidence** |
| 8 | `gapprobe_w24.sh:3` | header reads *"WAVE 23 — ITEM L"* |

**Why #5 is the sharpest evidence, and why this needs its own entry.** Wave 20 shipped a `prov_w20.py` that
printed *"novel against NINE recorded ids"* while checking ten. That was recorded, fixed, and written into the
charter's standing discipline as *"the report and the exit status are two channels — wire BOTH to the verdict,
and self-test the message against the mechanism it describes,"* with the fix being that the count is computed
from `len(PRIOR_BUILD_IDS)`. **The count line was fixed and self-tests AGREE (`printed=13`, `len=13`). A second
line twenty lines away still hardcodes a nine-wave list, and has been carried forward through four
derivations.** *Fixing the instance is not fixing the class.*

**Why #4 was load-bearing, and why the instrument still behaved perfectly.** After wave 24's gate PASSED 8 of 8
bit-identical, `score_g24_w24.py --arm-gate` returned **rc=1** and wrote **no marker**, blocking the arm:

```
[4] prov_w23.py free controls exit=2   (BuildIds seen: ['dd6ca32ef7f941c2a54753398cc2cf6b'])
    >>> REFUSING: the free controls did not pass.
>>> NO MARKER WRITTEN.
```

The *gate driver's* `sed` carried `s#prov_w23#prov_w24#g`; the *scorer's* did not. So the scorer called a
nonexistent module, caught the exception, recorded `exit=2` — **a could-not-look correctly refusing to certify.**
That is [F75](#f75--the-wave-held-two-standards-for-its-two-interlocks-one-driver-written-and-explicitly-protected-from-hand-writing-the-other-specified-as-a-controller-printf)'s
discipline working exactly as designed: *the writer is the code that computes the verdict, and it wrote nothing
because it could not compute one.* **A hand-written marker here would have licensed an arm on an unrun control.**

**Why it is not [F74](#f74--a-driver-and-its-scorer-each-rebuilt-the-artifact-path-from-a-template-disagreed-and-cost-a-pre-registered-rule-its-verdict--while-the-interlock-marker-between-them-already-carried-the-answer),
[F66](#f66--three-of-wave-14s-checks-could-not-return-their-own-pass-and-the-register-has-been-reading-strings-as-one-instrument-when-it-is-two) or F75.** F74 is two components independently rebuilding one path; F66 is a check that
cannot return its own PASS; F75 is who writes an interlock. **This is a *generative* defect: the mechanism that
produces each wave's instruments also produces prose that no longer describes them, and it does so silently,
repeatedly, and in a way that self-tests pass through** — every instrument above passed its own `--self-test`
(24 of 24, 21 of 21, 5 of 5) with the drift present, because a self-test checks behaviour and the drift is in
the labels.

**Next step — the cheap half first, and it is ~15 m.** A `--verify-derivation` pre-flight, run **before the
gate**, asserting for every instrument under `/mnt/d/hf_w<N>/`:

1. every `os.path.join(HERE, …)` target and every imported sibling **exists on disk** (catches #4 in one second,
   before any measurement rather than after the gate); and
2. the file contains **no reference to the previous wave's id** (`w<N-1>`, `G<N-1>`, `prov_w<N-1>`,
   `hf_w<N-1>`) outside an explicitly-declared allow-list of intentional cross-wave paths — the BEFORE-side roots
   are legitimate and must be declared, which is the point (catches #1, #2, #3, #5, #6, #7, #8).

**The better end state, and it should NOT be attempted in the same wave:** parameterise the wave id **once** at
the top of each instrument and derive nothing. It requires editing five instruments that are deliberately carried
forward unchanged, which is the very thing the derivation exists to avoid, so it needs a wave that can re-baseline
them. **And the durable rule, which costs nothing: any ordinal or count in a derived instrument's output must be
COMPUTED, never typed.**
Reproduce: `/mnt/d/hf_w24/prov_w24.py:75,179,227,304`; `/mnt/d/hf_w24/gate_w24.sh:221`;
`/mnt/d/hf_w24/v23_fingerprint_w24.py:2,171` against `/mnt/d/hf_w24/fp_v23_AFTER.txt`;
`docs/synthetic-af-bank-followups-wave24-results.md` §9.2.

> ### THE PRE-FLIGHT SHIPPED AND IT WORKS. IT ALSO HAD THE SAME BLIND SPOT TWICE: `\b` CANNOT SEE `_` (2026-08-13, wave 25, RULE V25)
>
> **The cheap half of this entry's next step is built and blocking.** `verify_derivation_w25.py <dir>` runs
> **before the gate**, over every `*.py` and `*.sh` in the wave's root, with three clauses (sibling targets
> resolve on disk; no unlicensed previous-wave token outside a declared `ALLOW` list; no **typed** ordinal or
> count in a printed literal). `V25-A < 100 %` **or** `V25-B > 0` **or** `V25-C > 0` → **`V-DRIFT`, BLOCKING —
> fix and re-run, no measurement starts.**
>
> **FAIL end, on this entry's own evidence, in one second.** Run against `/mnt/d/hf_w24` it returns **`V-DRIFT`,
> 170 findings** over 10 files — 168 surviving tokens and **2 typed ordinals**, including both of the rows this
> entry singles out (`gate_w24.sh:221`'s *"thirteenth"* on a fourteenth binary, `prov_w24.py:179`'s hardcoded
> nine-wave list while thirteen are checked). It also counts and does **not** flag **86** references inside pure
> non-printing comments: documentation is not drift, and a checker that cannot tell them apart is unusable.
>
> **PASS end, and what it cost: 21 findings on the wave's own instruments, all real, one load-bearing.** First
> run against `/mnt/d/hf_w25` returned `V-DRIFT`: `V25-A` **5 of 15** siblings resolving, `V25-B` **9** tokens,
> `V25-C` **2** typed ordinals. **The largest group was a `sed` ORDERING bug in the plan** — the list runs
> `s#g24_#g25_#g` *before* `s#score_g24_w24#score_g25_w25#g`, so by the time the second rule fires the text
> already reads `score_g25_w24` and it never matches; **ten of fifteen sibling references pointed at a file that
> does not exist.** It also caught surviving `G24_START`/`G24_DONE` markers, two bare `_w24` filenames, the
> *"thirteenth"* ordinal on a **fifteenth** binary, and **wave 20's hardcoded-enumeration defect still alive four
> derivations later**. `G24_START` was load-bearing: the driver would have announced it while the controller's
> `until` loop grepped for `G25_START`, hanging the wave on a completed arm.
>
> ---
>
> **AND NOW THE DURABLE FINDING, which is worth more than the pre-flight itself. `_` IS A WORD CHARACTER, SO
> `\b` CANNOT SEE IT — and every marker, function and path in this series is underscore-joined.**
>
> The checker had **two** gaps, same root cause, **opposite affixes**, both found in one wave:
>
> | gap | pattern | what it could not see | how it surfaced |
> |---|---|---|---|
> | **suffix** | `\bG24\b` | `G24_START`, `G24_DONE` | **BY ACCIDENT.** `V25-C`'s typed-ordinal clause happened to fire on the same physical line (`gate_w25.sh:221` carried both `G24_START` and the word *"thirteenth"*). Had that line not also carried an ordinal, the load-bearing marker would have passed **silently** |
> | **prefix** | `\bw24\b` | `w24_gate_log_wsl`, `w24_gate_out_win`, `w24_layout_self_test` | **The file was passed `V-CLEAN` and then could not run**: `gate_w25.sh: line 169: w24_layout_self_test: command not found` |
>
> ```python
> PREV_TOKENS = (
>     r"\bw%d(?:_[a-z][a-z0-9_]*)?\b"        # prefix form: w24_gate_log_wsl
>     r"\b[GBRD]%d(?:_[A-Z][A-Z0-9_]*)?\b"   # suffix form: G24_START
> )
> ```
>
> **A checker that finds a defect by accident has not checked for it.** The register's standing rule is that
> "could not look" needs its own state; this is the neighbouring case — **"looked with an instrument that could
> not resolve the thing"** — and it was visible only because two independent clauses overlapped on one line.
> Both closures were **demonstrated** on synthetic files that now report, with the checker's own `--self-test`
> still exiting 0. Byte backups before each edit, no VCS revert.
>
> **The prefix gap also exposed something no renaming scheme could have fixed.** The three missing functions were
> not a rename: wave 25's `layout_w25.sh` was written for a different arm and exposes a **different API**
> (`w25_layout`, `w25_cell_dir`, `w25_resolve_deadline`), so they did not exist under any name. **The `sed` that
> repointed the `source` line could not have helped — the two files were never the same interface.** *Derivation
> assumes the predecessor's interface survived; check that, not just its spelling.*
>
> **Two more, recorded rather than quietly fixed:**
>
> 1. **`gate_w25.sh` still printed `=== gate_w23.sh --self-test ===`** — a **w23** label surviving **two**
>    generations. **`V25` checks only the immediately previous wave's tokens: its `PREV` is a single wave, and
>    drift is not.** A future version should check the whole prior series, not `N-1`.
> 2. **The controller's own added guard tripped the checker, correctly.** It asserted a ported path did *not*
>    contain `hf_w24` — itself a surviving previous-wave literal. Rewritten to assert **positively** that the
>    path contains this wave's root: a stronger assertion that needs no reference to the past at all. **A
>    negative assertion about the previous wave is itself previous-wave drift.**
> Reproduce: `/mnt/d/hf_w25/verify_derivation_w25.py`, `/mnt/d/hf_w25/v25_failend.txt`,
> `/mnt/d/hf_w25/v25_passend.txt`; `/mnt/d/hf_w25/CONTROLLER_DEVIATIONS.md` D1, D2, D8;
> `docs/synthetic-af-bank-followups-wave25-results.md` §2.

> ### A THIRD SHAPE — THE RULE LETTER — AND A CONTROL THAT EXITED ITS CALLER WHILE PRINTING PASS (2026-08-13, wave 26, RULE V26)
>
> **The underscore gap had a third instance, and the enumeration that hid it is the same defect this entry is
> about.** Wave 25 closed the prefix and suffix forms; the pattern it shipped still read
>
> ```python
> r"(?:\bw%d(?:_[a-z][a-z0-9_]*)?\b|\bW%d\b|\b[GBRD]%d(?:_[A-Z][A-Z0-9_]*)?\b|prov_w%d|hf_w%d|score_\w*_w%d|_w%d\b)"
> ```
>
> — the uppercase alternation **enumerates the rule letters `[GBRD]`**, the ones waves 19–24 happened to use, and
> the bare `\bW%d\b` arm has **no suffix arm at all**. Waves 21–25 used `W`, `N`, `M`, `V`, `P` and `S`. Measured
> against wave 25's own pattern, not asserted:
>
> | token | a real artifact of wave 25? | wave 25's pattern |
> |---|---|---|
> | `G25_START` | yes | matches |
> | `w25_layout_self_test` | yes | matches |
> | **`W25_BEFORE_READY`** | **yes — wave 25's own arm interlock marker** | **MISS** |
> | `g25_score.txt`, `v25_passend.txt`, `n25e_score.txt`, `p3c_score.txt` | yes, all four on disk | **MISS** |
>
> `W25_BEFORE_READY` is **the load-bearing class on a different letter** — a surviving `*_READY` marker is what
> hangs the controller's waiter, which is exactly what `G24_START` would have done in wave 25. And `n25e_score`
> adds a second requirement: a letter follows the digits with **no separator**, so an optional `_SUFFIX` is not
> enough. The pattern is now matched by **SHAPE**, not by an enumeration of the letters that have been used:
>
> ```python
> r"(?:\b[a-z]%d[a-z0-9_]*\b|\b[A-Z]%d(?:_[A-Z0-9][A-Z0-9_]*)?\b|prov_w%d|hf_w%d|score_\w*_w%d|_w%d\b)"
> ```
>
> **Demonstrated in both directions**: all five shapes report, and wave 26's own affixed names (`Q26_START`,
> `w26_layout_self_test`) do **not**, so it discriminates rather than merely firing. **The durable rule: an
> alternation that enumerates the values a field has TAKEN is a hardcoded list wearing a regex, and it belongs
> with this entry's "any ordinal or count must be COMPUTED, never typed."**
>
> **The FAIL end had to be PINNED, because a checker's known-bad input expires the moment it works.** Wave 25's
> self-test used *"the previous wave's root"* as its known-bad input — then repaired all 21 findings, making
> `/mnt/d/hf_w25` `V-CLEAN`. A wave-26 checker pointed there would have found nothing and reported **itself**
> broken. The known-bad root is now **named in the source** (`/mnt/d/hf_w24`, in `ALLOW` with its reason).
> *A regression fixture that is "whatever came before" has a shelf life of one wave.*
>
> ---
>
> **AND A NEW MEMBER OF THE "CONTROL THAT CANNOT FAIL" FAMILY: A SOURCED FILE SEES ITS PARENT'S `$1`.**
>
> `layout_w26.sh`'s trailing `--self-test` dispatcher **fired when the file was SOURCED.** `q26_arm_w26.sh
> --self-test` sourced the layout; the layout's own dispatcher matched the **parent's** `$1`; `w26_layout_self_test`
> ran; and **`exit "$?"` terminated the arm driver inside its own `source` line** — printing a clean passing
> layout self-test and running **not one line** of the arm's own checks. **It looked exactly like a pass.** Fixed
> with `[ "${BASH_SOURCE[0]}" = "$0" ]`.
>
> **This is wave 24's `[ now > "23:59" ]` in a new costume**, and it was found the same way: by **reading the
> output** of the self-test rather than its exit code. The charter's *"READ LOGS, NOT EXIT CODES"* was written
> for arms; it applies to instruments, and this is the second wave running in which an instrument's PASS was the
> thing that needed reading.
>
> Two smaller ones from the same wave, recorded because each is a shape rather than an instance:
>
> 1. **A guard that greps its own file reported the defect it is made of.** The arm's F15 check (*"the
>    bank-writing flag must appear on no invocation line"*) matched its own three lines on first run. Excluded by
>    an inline marker — but *a checker whose pattern appears in its own source needs a self-exclusion, and the
>    self-exclusion is itself a thing to demonstrate.*
> 2. **`xargs` split a path on its space and the command still succeeded.** The plan's class-1 fingerprint reads
>    `find "/mnt/d/Autofocus Bank" … | sort | xargs sha256sum`; `/mnt/d/Autofocus` and `Bank/…` became two
>    arguments and it produced **20 landings instead of 42**. Both `find` and `sha256sum` exited 0 on the paths
>    they could resolve. **It was caught only by the population assertion** (`wc -l` against the expected count),
>    which is the same instrument that catches everything else in this family. *Assert the population size of
>    every control, including the ones that "obviously" worked.*
>
> Reproduce: `/mnt/d/hf_w26/verify_derivation_w26.py` (self-test section `[4]`), `/mnt/d/hf_w26/layout_w26.sh`,
> `/mnt/d/hf_w26/q26_arm_w26.sh`, `/mnt/d/hf_w26/fingerprints/bank_landing_BEFORE.txt` (42 rows);
> `docs/synthetic-af-bank-followups-wave26-design.md` §4;
> `docs/synthetic-af-bank-followups-wave26-results.md` §2.

### F9 — `bank-verify` cannot pin C0's detector knobs
**Status:** Open

C0 consumes `BuildDefaultStarDetectorParams()` verbatim with no `--sensitivity` / `--star-clip` override. When a
shipped default changes (as in `59d5e59`, sensitivity 2.0 → 10.0), old and new reports are no longer comparable
and there is **no way to reproduce the old configuration** without a scratch build. That is what made the
2026-06-24 anchor comparison untestable as written.

**Next step.** Add explicit overrides mirroring `golden eval`'s, so a historical config can be re-measured.

### F10 — Golden bbox is not a valid donut proxy
**Status:** Done (recorded so it is not re-attempted)

Using the golden star bbox to detect donuts fails: `Panos` is a genuine donut run but its most-defocused golden
frame measures a median bbox of only 12 px, because the SNR reference detects **fragments** of a thin ring rather
than the ring. This is the documented under-counting of defocused donuts in `.claude/docs/golden-star-set.md`.
Use the heuristic's own donut statistics, or render the pixels and classify them.

### F34 — `synth-validate` scored a stalled run as converged, at a step 4× outside the band its own assertion failed it on
**Status:** **CLOSED** (2026-08-13, wave 25 — the stall message now names a cause it checks, `RULE M25` = `M-CORRECTED`; original reporting fix 2026-08-03, wave 2) · found 2026-08-03 re-measuring [F25](#f25--from-a-far-too-wide-sweep-the-step-recommender-widens-it-further-instead-of-recovering)

The fourth harness-calibration bug on this bank, and the third in the convergence-predicate family. The round
loop set `stoppedReason = "converged (round applied nothing)"` unconditionally, and `ScenarioTerminal.Converged`
was then derived by string-matching that reason for a `"converged"` prefix.

**Evidence.** `D05_tec140_1000mm` S2:

```
converged: true
stoppedReason: "converged (round applied nothing)"
finalStepSize: 140     stepBehavioral: 35     deltaStepVsExpected: +105
assertions: A3 verdict=FAIL  "final step 140 outside [21,56] = [0.6,1.6]x step_behavioral (35)"
```

Two verdicts contradicting each other inside one JSON object.

**Mechanism.** `StepSizeRecommender.Degenerate` **holds the current step** whenever the fit is unusable — no
`Fitting`, a non-finite vertex, a non-positive minimum HFR, or the 3× band never crossed. The driver applies a
step only when it differs, so a degenerate fit produces `AppliedAnything == false` that is byte-identical to the
recommender genuinely agreeing. The loop read "nothing changed" as success, which inflates convergence counts on
exactly the runs that are most broken.

**Fixed.** A no-op round still STOPS the loop — the recommender is not going to move on its own — but it counts
as convergence only when the step is inside the tolerance band, and reports `stalled` otherwise. The band moved
into `ConvergenceBand` so both stop branches share one definition, and it is a strict subset of A3's `[0.6,1.6]×`
sharing its lower bound, so the two verdicts cannot contradict each other again. `Converged` is now set
explicitly at each stop site instead of parsed out of prose, and `stepToleranceBand` is recorded on the terminal
so the claim is checkable from the report alone.

**Verified on the motivating case.** `synth-validate --datasets D05_tec140_1000mm --scenarios S2 --max-rounds 4`
re-run after the fix:

```
converged:         false
stoppedReason:     "stalled (round applied nothing, but step 140 is outside the 14 tolerance band of
                    step_behavioral 35) — a no-op recommendation from a degenerate fit, not convergence"
finalStepSize: 140    stepBehavioral: 35    stepToleranceBand: 14
assertions:    A3 verdict=FAIL  "final step 140 outside [21,56] = [0.6,1.6]x step_behavioral (35)"
```

Same run, same step, same A3 failure — the report no longer contradicts itself, and the terminal now publishes
the band its verdict was reached with.

**Why it is worth a numbered entry.** Four calibration bugs have now been found on this harness — three in this
family — every one by someone happening to look rather than by anything failing. A harness that reports its own
success is load-bearing for every conclusion drawn from it; see also the `truthViolations` /
`precisionNull` guards added to `bank-verify` for the same reason ([F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)).

> ### THE REPORTING FIX IS CORRECT. THE STOP POLICY IT SHIPPED IS WRONG AT THE NARROW END (2026-08-12, wave 23)
>
> This entry's fix — *"a no-op round still STOPS the loop — the recommender is not going to move on its own"* —
> is measured with a denominator for the first time, and the reasoning holds only when the no-op means
> **agreement**. When it means the fit was **unusable**, stopping is exactly the wrong response, and wave 23's
> RULE V23 caught three cells doing it (`docs/synthetic-af-bank-followups-wave23-results.md` §4.1):
>
> | cell | truth | final | ratio | rounds used, of 4 permitted | the fit |
> |---|---|---|---|---|---|
> | `D01_ultrawide_40mm`/S1 | 9 | **2** | 0.22x | **1** | `\|vertex-center\| = 8` against a tolerance of 4 |
> | `D02_rich_135mm`/S1 | 6 | **2** | 0.33x | **1** | **R2 = -0.2741** — worse than a horizontal line |
> | `D03_redcat_250mm`/S1 | 16 | **4** | 0.25x | **1** | `halfWidth` = `NaN` |
>
> All three stop on this entry's own branch, with this entry's own message, **having spent one round of four.**
>
> **The chain.** Scenario S1 starts the sweep at 0.25x the correct step; with `DefaultOffsetSteps = 4` the modelled
> HFR then spans only ~1.28x the minimum across the whole sweep, so the vertex is not identifiable
> ([F21](#f21--stepsizerecommenders-half-width-is-not-stable-against-noise-even-on-a-perfect-fit)'s wave-10
> diagnosis: *"R2 measures fit to the SAMPLED points and says nothing about whether the vertex is identifiable
> from them"*). `StepSizeRecommender.Recommend` therefore returns `Degenerate(currentStepSize, …)`
> (`StepSizeRecommender.cs:410-416`, reached from `:233`, `:239`, `:252` or the `NaN` at `FindHalfWidth:470`),
> which **holds the step that produced the unusable fit**. The driver applies a step on a bare integer inequality
> — `if (stepRec.StepSize != state.StepSize)` (`SynthValidateRunner.cs:843-847`), **no deadband, no hysteresis** —
> so `AppliedAnything` is false and the loop breaks at `:532-548`. **Holding guarantees the next sweep is exactly
> as narrow as the one that just failed.** The recommender refuses to widen precisely when widening is the only
> thing that can help.
>
> **This is the mirror of [F25](#f25--from-a-far-too-wide-sweep-the-step-recommender-widens-it-further-instead-of-recovering),
> and the two together define the fix.** F25 is the too-wide end, where *widening* is the harm and its owed
> fit-quality gate says a bad fit *"should either hold the current step or shrink it, never widen it."* Wave 23
> measures the too-narrow end, where **holding is the harm**. So the gate must be **directional**, keyed on
> whether the sweep sampled too little curve or too much — and `stepRecommendation.sampledHfrRange` is already in
> the report to key it on. Shipping only one half trades one entry's defect for the other's.
>
> **Priced:** ~15-20 lines across `StepSizeRecommender.cs` and the runner's stop branch, plus 2 unit tests,
> **~1 h**. Re-running the three named cells is **~5 m**; the S1 rate needs the full arm (~1 h, see
> [F21](#f21--stepsizerecommenders-half-width-is-not-stable-against-noise-even-on-a-perfect-fit)'s corrected
> price). Pre-registrable bar: `V23-G` on S1 above **13 of 17**, and all three cells reaching `roundsUsed > 1`.
> **A separate, cheap improvement this measurement argues for:** a no-op from *agreement* and a no-op from
> *degeneracy* are currently byte-identical to the loop, which is this entry's own recorded observation one level
> up. Recording which branch `Recommend` took would make the two distinguishable from the report alone.
> Reproduce: `/mnt/d/hf_w23/v23/D0{1,2,3}_*__S1/synth_validate_report.json`; `/mnt/d/hf_w23/v23_score.txt`.

> ### THE "SEPARATE, CHEAP IMPROVEMENT" SHIPPED — AND ON ITS FIRST USE IT REFUTED THIS ENTRY'S OWN ATTRIBUTION (2026-08-12, wave 24)
>
> This entry asked for exactly one thing beyond its fix: *"a no-op from **agreement** and a no-op from
> **degeneracy** are currently byte-identical to the loop … Recording which branch `Recommend` took would make the
> two distinguishable from the report alone."* **Wave 24's P1 recorded it** (`stepRecommendation.degenerateReason`,
> on all three exits, in shipping plugin code). **The first thing it distinguished was a mis-attribution in the
> block above.**
>
> **The correction.** The table above lists `D01_ultrawide_40mm`/S1, `D02_rich_135mm`/S1 and
> `D03_redcat_250mm`/S1 as three cells that *"all stop on this entry's own branch"* from degenerate fits.
> **`D01` IS NOT DEGENERATE.** Read from the AFTER arm on the fourteenth binary:
>
> ```
> D01_ultrawide_40mm/S1  r0: bootstrapStep=2  stepRec=2
>     halfWidth = 6.0877461433410645     <- RESOLVED, not NaN
>     degenerateReason = null            <- no degenerate exit was taken
>     sampledHfrRange = 1.4628           R^2 = 0.9999999999999397
> terminal: converged=false roundsUsed=1 finalStepSize=2 expectedStepSize=9 stepBehavioral=8.0 band=3.2
> stoppedReason: 'stalled (round applied nothing, but step 2 is outside the 3.2 tolerance band of
>                 step_behavioral 8) -- a no-op recommendation from a degenerate fit, not convergence'
> ```
>
> **The half-width resolved, the fit is essentially perfect, and there is no degenerate reason — and the
> recommender still answered "keep 2" against a truth of 9, so the loop applied nothing and the run stalled after
> one round of four.** That is a **non-degenerate, high-confidence recommendation to hold a step 4.5× too
> narrow**: a second mechanism, not this one.
>
> **A second defect the same field exposes, in this entry's own runner.** The `stoppedReason` above asserts
> *"from a degenerate fit"* **unconditionally** — `SynthValidateRunner.cs:543-545` never checks whether the fit
> was degenerate, and now it can, because `degenerateReason` is a field it can read. **The message names a cause
> it does not check**, and it named the wrong one on `D01` in wave 23's published results and in this entry.
> **~5 lines and one test.**
>
> **So the stall has AT LEAST TWO distinct causes and neither of them is "the fit was bad."** Combined with
> [F25](#f25--from-a-far-too-wide-sweep-the-step-recommender-widens-it-further-instead-of-recovering)'s finding
> that `D03` is degenerate at **R² = 0.9835**, the design consequence for the owed gate is sharp: **a fit-quality
> gate — keyed on R² or on degeneracy — addresses neither `D03` nor `D01`.** A wave that ships one and scores it
> on these three cells would report a bridge it did not build.
> Reproduce: `/mnt/d/hf_w24/after/D01_ultrawide_40mm__S1/synth_validate_report.json`;
> `/mnt/d/hf_w24/d24_score.txt`; `docs/synthetic-af-bank-followups-wave24-results.md` §5.2.

> ### CLOSED — THE MESSAGE NOW NAMES A CAUSE IT CHECKS, AND THE FIX WAS MEASURED AGAINST A KNOWN ANSWER FIRST (2026-08-13, wave 25, P5 + RULE M25)
>
> This entry's tail asked for exactly one thing: *"the message names a cause it does not check … ~5 lines and one
> test."* **Wave 25's P5 shipped it**, and `RULE M25` measured it in both directions.
>
> **The fix.** `SynthValidateRunner`'s stall path now reads `roundReport.StepRecommendation.DegenerateReason` and
> branches, via a new pure `TestApp/SynthBank/StallReason.cs` (`SynthValidateRunner.cs` renders frames and runs
> the optimizer and therefore cannot be source-linked into the test project — the same wall wave 24's P2 hit, so
> the testable half was lifted out). Non-null keeps today's wording with the reason token appended; null says
> what is actually true and **quotes the number that explains it**.
>
> **The FAIL end was run FIRST, on the published artifact that motivated this entry.** Over
> `/mnt/d/hf_w24/after/*/synth_validate_report.json` — **25 cells scanned, exactly 1 contradiction, and it is
> `D01_ultrawide_40mm`/S1 by name.** That is the instrument validated against a known answer before being pointed
> at unknown data. Then the AFTER arm: **0 contradictions of 18 paired cells.** `RULE M25` = **`M-CORRECTED`**.
>
> **Before and after, on the cell that motivated the entry:**
>
> ```
> BEFORE (B14):  stalled (round applied nothing, but step 2 is outside the 3.2 tolerance band of
>                step_behavioral 8) -- a no-op recommendation from a degenerate fit, not convergence
>                                                              ^^^^^^^^^^^^^^ degenerateReason was NULL
>
> AFTER  (B15):  stalled (round applied nothing, but step 3 is outside the 3.6 tolerance band of
>                step_behavioral 9) -- a no-op recommendation from a NON-degenerate fit
>                (sampled HFR range 1.459x, band 3x), not convergence
> ```
>
> **Why this closes rather than extends.** The defect was *a message asserting a cause it never checked*, and it
> is checked now, on every stall, with the FAIL end measured on the real prior artifact rather than on a fixture.
> **The register consequence is already recorded**: a wrong message became a wrong register entry when wave 23
> grouped `D01` with `D02` and `D03` on the strength of it, and wave 24's correction block above is what caught
> it. The stop-policy half this entry re-opened in wave 23 is **not** closed here — it is
> [F81](#f81--maxhalfwidthsampledhalfspanmultiple-has-been-a-ceiling-with-no-floor-and-the-half-width-unresolved-exit-returned-before-the-ceiling-was-consulted-at-all)'s,
> and it shipped in the same wave.
>
> **F34's own pre-registrable bar, scored honestly.** The entry proposed *"`V23-G` on S1 above 13 of 17, and all
> three cells reaching `roundsUsed > 1`."*
>
> | half | verdict |
> |---|---|
> | *all three cells reaching `roundsUsed > 1`* | **MET, and it was strengthened before the data** to `converged == true` inside the tolerance band in `roundsUsed <= 4`. All three reached `roundsUsed = 2`; **`D02` and `D03` MET the strengthened bar** (final 5 of truth 6, final 12 of truth 16); **`D01` MISSED** it (final 3 of truth 9) for the reason in [F82](#f82--the-half-width-floor-is-not-sticky-across-rounds-and-the-cap-recomputed-from-a-shrunken-fit-pulls-it-back-down) |
> | *`V23-G` on S1 above 13 of 17* | **NOT SCORED, by pre-registration.** `13 of 17` is a B13 number and wave 24's P2 changed the round loop in between; `N25-E`, the clause that would have measured whether the comparison survives, was **dropped on the clock** and is a ~4 m item for wave 26 |
> Reproduce: `/mnt/d/hf_w25/after/D01_ultrawide_40mm__S1/synth_validate_report.json` vs
> `/mnt/d/hf_w25/before/...`; `docs/synthetic-af-bank-followups-wave25-results.md` §6, §7.

---

## Bank data quality

### F11 — Precision is a lower bound on runs whose faint tier was budget-truncated → **re-run these with more montages**
**Status:** Open · the largest known data-quality gap in the bank

`build_goldens` auto-confirms the SNR≥12 tier and LLM-QAs only a bounded slice of the uncertain tier, capped by
`golden_prep --budget-montages`. On deep fields that slice is a few percent, so real faint detections HocusFocus
finds are scored as **false positives**. Precision on those runs is a weak lower bound, not a measurement.

**recall@SNR≥12 is unaffected everywhere** — the high tier is auto-confirmed in full — and remains the trustworthy
headline. Only precision (and recall@all) is compromised.

**Measured coverage** (rendered ÷ actual uncertain candidates):

| run | frames | rendered | uncertain | coverage | montages | budget used |
|---|---|---|---|---|---|---|
| `bobp_m101` | 10 | 2,303 | 2,303 | **100%** | 67 | 20/frame |
| `bobp` | 9 | 1,870 | 1,870 | **100%** | 56 | 20/frame |
| `vsn07` | 7 | 1,362 | 1,362 | **100%** | 42 | 20/frame |
| `FlyData` | 9 | 6,480 | 17,215 | **37.6%** | 180 | 20/frame |
| `timmer` | 9 | 6,480 | 117,107 | **5.5%** | 180 | 20/frame |
| `SorenVance` | 6 | 4,320 | 130,932 | **3.3%** | 120 | 20/frame (golden invalid — still unscored, see F17) |
| `lumos` | 23 | 16,560 | 564,813 | **2.9%** | 460 | 20/frame (golden invalid — still unscored, see F17) |

**Every pre-existing run in the bank is worse still** — all were built at the skill-default 4 montages/frame,
which hard-caps the faint tier at 144 cells/frame. Their goldens show exactly that fingerprint:

| run | faint stars/frame | cap |
|---|---|---|
| `mccomiskey` | 139 | 144 |
| `uneven` | 133 | 144 |
| `muggsie` | 129 | 144 |
| `toml999` | 124 | 144 |
| `FlyData` | 102 | 144 |

On `mccomiskey` that is ~2% of its ~6,900 uncertain candidates per frame and **nothing at all below SNR 8**, which
is why its C0@nc2 "6,871 false positives" at precision 0.8325 is an artifact rather than a finding.

**Next step — re-run the deep runs with a much larger `--budget-montages`.** Cost scales linearly with coverage:
the 236-montage QA pass took 27 min at chunk=4, so ~7 min per 60 montages.

| target | montages (`timmer`) | QA time | coverage |
|---|---|---|---|
| current (20/frame) | 180 | ~20 min | 5.5% |
| 60/frame | 540 | ~65 min | 17% |
| 200/frame | 1,800 | ~3.5 hr | 55% |
| full | 3,253 | ~6 hr | 100% |

Full coverage on every deep run is ~6 hr each and probably not worth it. Suggested: **60–200 montages/frame for
any run whose precision is quoted in a report**, prioritised as `mccomiskey` (its precision has already been cited
and is the most misleading), then `timmer`, `SorenVance`, `lumos`, then the remaining default-budget runs.

Independently of any re-run, **record the coverage fraction in the golden sidecar** so a consumer can tell whether
a precision figure is a measurement or a bound. Today nothing in `<frame>.golden.json` says how much of the
uncertain tier was examined, which is why this went unnoticed for so long.

**The coverage-recording half of this is done** (schema v2, `docs/golden-tier-plausibility-design.md` §4.5):
goldens now carry per-tier `examined`/`total`, and candidates the budget never reached are `unresolved` and
excluded from **both** denominators instead of being counted as false positives. That removes the artifact at
its source for any regenerated run — the "6,871 false positives" shape cannot recur. The remaining work here is
purely the larger `--budget-montages` re-runs on the runs still carrying v1 sidecars.

### F16 — The SNR≥12 auto-confirm gate inverts on heavily-defocused runs
**Status:** **Tooling fixed and merged** (PR #163) · the bank rebuild it enables is incomplete — see **F17**

**Two root causes were found beyond the original diagnosis, both by rendering real output rather than trusting
the metrics:**

1. **`donut_k` = 6.0 sat inside the noise.** The matched-filter response distribution's median was 6.41 against
   a 6.0 threshold, so ~98% of the candidate pool was junk and the bounded QA budget was spent rejecting it.
   Confirmed real donuts had min 7.48 / median 14.46. Now 8.0: candidates/frame fell 5,000–7,300 → 649–919 on
   `LinwoodFocus` and QA coverage rose ~14% → 78–100% at unchanged cost.
2. **The tier assignment still mixed two quantities.** `tier()` buckets on `snr`, which was peak/σ for connected
   components but the disk-integrated response for the matched filter — the same confusion F16 is about,
   surviving in a place the first fix didn't reach. It only shows at low coverage. Matched-filter candidates now
   carry a measured peak.

The donut matched filter shreds each ring into many tiny high-SNR fragments, which then pass `build_goldens`'
SNR≥12 **auto-confirm** gate without QA. The gate assumes a high-SNR candidate is a real star — sound for the
per-pixel-SNR path it was validated on, **not** for the matched-filter path.

**Evidence.** Golden star widths, donut runs old vs newly generated:

| run | mode | stars | median W | p90 W | **≤4 px** |
|---|---|---|---|---|---|
| `lumos` | donut, new | 60,348 | 3 px | 4 px | **91%** |
| `SorenVance` | donut, new | 28,935 | 3 px | 36 px | **67%** |
| `FlyData` | donut, new | 5,462 | 11 px | 22 px | 26% ✅ |
| `mufti` | donut, existing | 5,612 | 18 px | 36 px | 1% |
| `Panos` | donut, existing | 7,178 | 12 px | 36 px | 11% |
| `LinwoodFocus` | donut, existing | 1,640 | 20 px | 36 px | 19% |

`lumos`'s donuts measure ~21 px and `SorenVance`'s ~26 px per the donut heuristic, so a golden whose stars are
median **3 px** is fragments, not stars. The scored consequence: HocusFocus finds 8–813 stars/frame (plausible for
these fields) against a reference claiming ~4,600/frame, giving recall@≥12 of **0.036** (`SorenVance`) and
**0.013** (`lumos`) — and **config B (donut-on) is no better than C0**, which is the tell. A real donut-detection
gap would show B recovering; it doesn't, because the reference is wrong rather than the detector blind.

**ROOT CAUSE (investigated 2026-07-30) — the tiering, not the matched filter, and NOT bad data.**
`snr_ref`'s SNR is a **per-pixel/peak** measure, so it scales inversely with how far a star's flux is spread. A
2 px noise spike concentrates its signal and scores high; a 36 px defocused donut spreads the same flux over
~1,000 px and scores low. `build_goldens` then auto-confirms everything at SNR≥12 **without QA**, assuming high
SNR implies real. On a heavily-defocused run that assumption **inverts** — the gate keeps the noise and discards
the stars:

| run | extreme HFR | auto-confirmed (SNR≥12) | discarded (SNR<12) | corr(bbox, SNR) |
|---|---|---|---|---|
| `lumos` | 10.7 | n=2,268, median **2 px** | n=24,499, median **36 px** | **−0.495** |
| `SorenVance` | 7.5 | n=4,506, median 3 px | n=21,601, median 12 px | −0.082 |
| `FlyData` | 5.0 | n=544, median 6 px | n=1,991, median 28 px | −0.054 |

Severity tracks defocus exactly, which is the confirming signature. `FlyData` is mild enough that the ordering
survives, which is why it produced a good golden.

**The `--donut` matched filter is not at fault** — it *found* the donuts; they sit in the candidate list at 36 px
(13,170 of them ≥30 px on one `lumos` frame). What fails is the SNR tiering applied afterwards.

**The data is good.** Cropping `lumos`'s large low-SNR candidates shows genuine faint diffuse defocused discs at
SNR 15–44. These are legitimate low-SNR, heavily-defocused runs — exactly what a validation bank should contain.
**Do not delete them.**

**Designed 2026-07-30 → `docs/golden-tier-plausibility-design.md`** (plan: `plans/golden-tier-plausibility-plan.md`).

**The integrated-SNR next step proposed here was measured and does not work** — on `lumos@209735` it keeps 2,445
candidates at 88.0% ≤4 px versus peak SNR's 2,390 at 88.1%, i.e. marginally worse, and on `FlyData@889` it takes
≤4 px from 16.7% to 29.0%. A 3 px spike at 12σ peak has integrated significance ≈20; a 36 px donut at 0.25σ/px
has ≈8. Integrated SNR is the *correct* significance ordering and still ranks the spike higher. No
significance-based statistic can fix this — the gate's error is using significance as a proxy for "is a star".

The design instead keeps the `snr >= 12` tier definition (so `recall@SNR≥12` keeps its meaning), adds a
frame-relative **star-plausibility** measure (candidate size ÷ the frame's own star scale) that reorders the QA
worklist, drops auto-confirm entirely on `--donut` runs, and adds an **unresolved** state excluded from both
recall and precision denominators.

Two findings from that work belong here regardless of when it lands:

- **`LinwoodFocus` is also contaminated** (per-frame high/QA tier width ratio **0.71**: high tier median 13 px vs
  QA tier 28 px). Its historical `recall@SNR≥12` measures the wrong star population and cannot be rescued by
  rescoring. `Panos` is inconclusive; `mufti` and `FlyData` are clean.
- **Not hot pixels, and σ is not mis-estimated.** Zero auto-confirmed `lumos` sites recur in ≥11 of 23 frames
  (68.3% appear in exactly one), and block-MAD σ agrees with an adjacent-difference estimate (99.33 vs 103.79) at
  unit z-width. Do not re-investigate either.

`verification_20260730T141918Z`'s rows for `lumos` and `SorenVance` must still be disregarded.

### F17 — The bank rebuild F16 enables is only 2 runs deep
**Status:** Open · **`lumos` and `SorenVance` are still unscored — this is the remaining gap in the bank**

F16's tooling is merged and tested, but the rebuild it exists to enable ran out of LLM budget at **396 of 1,040
montages**. Two of six donut-aware runs are rebuilt; the rest are untouched or restored. Nothing is corrupted —
`build_goldens` never ran for the incomplete runs — but the bank is now of **mixed provenance**, and any report
that mixes these rows must say so.

| run | state | schema | action needed |
|---|---|---|---|
| `LinwoodFocus` | rebuilt, validator clean (widthRatio 0.71 → **3.00**, 99% high-tier coverage) | v2 | regenerate — predates the peak-SNR fix |
| `FlyData` | rebuilt, validator clean (ratio 1.50, coverage recorded) | v2 | regenerate — predates the peak-SNR fix |
| `Panos` | rebuild came out `TIER-INVERSION` (0.75) at 10–25% coverage → **restored from backup**; still `WIDTH-FLAT` | v1 | full rebuild |
| `mufti` | untouched, QA never ran | v1 | full rebuild |
| `lumos` | **quarantined, unscored** | — | full rebuild |
| `SorenVance` | **quarantined, unscored** | — | full rebuild |

**Do not compare a v2 row against a v1 row.** The v2 goldens exclude `unresolved` candidates from both
denominators and record per-tier coverage; the v1 goldens auto-confirmed their high tier without QA. The 11
non-donut runs are all v1 and remain internally comparable, exactly as before.

**To resume.** Salvaged QA for `FlyData` (9/9 frames), `Panos` (7/7) and `SorenVance` (3/6) is preserved at
`_prior_reports/salvaged_qa_20260730/` — **reuse it rather than re-paying for those montages**. Per-run backups of
every overwritten golden are at `_prior_reports/<run>_pre_plausibility_20260730/`. The pipeline is
`golden_prep.py --donut --budget-montages N` → `build_qa_worklist.py` → `qa_workflow.js` → `persist_qa.py` →
`build_goldens.py` → `golden_health.py`, per `.claude/docs/golden-star-set.md`.

**Cost, measured rather than estimated:** montages = `budget-montages × frames`, at ~8.74 montages/min. The five
outstanding runs at 20/frame are ~1,040 montages ≈ **2 hr at one vote**. Coverage at that budget was 78–100% on
`LinwoodFocus` but only ~10% on `lumos` and ~4% on `SorenVance`, which have 7k and 18k candidates per frame.

**Two things to fix before the next attempt, since they change the yield:**

- **`donut_radii` is hardcoded `(6,10,14,18)`**, capping the matched filter at a 36 px box. `mufti` measures star
  scales of **42–52 px**, so its largest donuts exceed what the reference can represent, and on near-focus frames
  r=18 kernels can only return noise. The per-frame star scale that `plausibility.frame_star_scale` already
  computes is the natural input for sizing these.
- **Single-vote QA is not reproducible.** Two independent passes over the same `LinwoodFocus` frames agreed on
  only 62% of confirmed stars (Jaccard 0.6202) while auto-confirm reproduced bit-for-bit. Everything rebuilt so
  far records `qaVotes: 1`. If `recall@SNR≥12` on donut runs is going to be quoted, the high tier wants ≥3 votes.

### F12 — `mccomiskey` is a low-SNR run
**Status:** Open · data-quality note, no action needed

7.0 s subs, Green filter, 0.2885 "/px. Median candidate SNR **11.3** at best focus and **8.1** at the wings; only
47% and 24% of candidates clear SNR 12. Best-focus HFR 3.43 px is inside the detector's calibrated 2–4 px band, so
this is **not** an oversampling problem and binning is not the remedy (2×2 would push HFR to ~1.7 px, below the
band, breaking every pixel-unit knob). Longer exposure is the only lever. Worth knowing when its numbers look
noisy.

### F13 — `SorenVance` was the one bayered run never scored
**Status:** Blocked by **F17** (F16's tooling fix is merged; the rebuild is not done)

Bayered, 6 frames, and had no goldens or linear exports at all, so it produced NaN in every report. Linear exports
now exist and the donut heuristic flags it (frac 12.00, bbox 26 px), but its generated golden proved invalid — see
F16 — so it remains unscored. `lumos` (23 frames, mono, donut-aware) is in the same position. `vsn07` (7 frames,
mono, no donut) succeeded and is now scored, at 100% uncertain coverage.

Post-F16 prep is encouraging for both: `lumos`'s star scale now traces a proper focus curve
(36…36,35,27,17,28,**8**,16,27,36,36) where it was previously flat at 3 px on all 23 frames — the flatness that
first proved its reference was measuring noise. `SorenVance` is the harder case: its candidate pool barely shrank
under `donut_k=8` (19,752/frame vs `lumos`'s 7,000), so it lands at ~4% coverage and may need a larger budget or
the `donut_radii` work in F17 before it yields a usable denominator.

### F14 — `astrodet` is frameless
**Status:** Won't fix

Holds artifacts but zero `Focuser*` frames, so run discovery skips it and no golden can be built. Documented so it
is not repeatedly investigated.

---

## Process

### F75 — The wave held TWO standards for its two interlocks: one driver-written and explicitly protected from hand-writing, the other specified as a controller `printf`
**Status:** **open** · found 2026-08-11 (wave 21) when the arm aborted on a marker nothing in `*.sh`/`*.py` writes · **one-line repair, named below**

Wave 21 has two interlock markers, and the plan treats them oppositely:

| marker | written by | the plan says |
|---|---|---|
| `B21_ARM_READY` | the **driver**, last, and only if `>= 4` manifest rows exist | *"**Never hand-write the marker.** If the driver refuses, that refusal is the result"* |
| `G21_PASSED` | the **controller's hand**: `printf 'G21 PASS %s BuildId=%s\n' … > /mnt/d/hf_w21/G21_PASSED` | *"If `G21-1` is 8 of 8, write the marker the arm requires"* |

The second is the one the arm blocks on. So the strongest gate in the wave — the stopping gate, the control on
the binary every arm runs — hands its verdict forward through **the only artifact in the chain that no code
produces**. `grep -rn G21_PASSED /mnt/d/hf_w21/*.sh /mnt/d/hf_w21/*.py` returns exactly one hit: the arm's
`[ -f … ] || ABORT`. A reader and no writer.

**Nothing mechanically couples the marker to the check it attests.** At 09:56Z the arm aborted on the absent
marker. At that moment the controller had scored `G21-1` 8 of 8 and could have written the marker immediately —
*before* running `score_b21_w21.py --gate /mnt/d/hf_w19/gate` (the FAIL end) and the `--self-test`. Nothing
would have caught it, and the arm would have run behind an interlock that attested to a demonstration that had
never happened. The checklist was in fact run first (self-test 20 of 20; w19 exit=1; w21 exit=0), and the
marker written at `10:00:12Z` after — but that ordering was the controller's discipline, not the design's.

*This is the same defect as F66 seen from the other side.* F66 says a gate never shown to FAIL is not a gate.
F75 says **an interlock whose writer is a human is not an interlock — it is a note.** The marker records a
belief about a check; only a writer that runs *inside* the check records the check.

**The repair is one line and it belongs in the scorer that already knows the answer:** `score_b21_w21.py --gate`
computes `rc = 1 if (fails or cnl) else 0` and knows the gate BuildId it just read for the arm's V2 comparison.
It should write `G21_PASSED` itself, carrying that BuildId, on and only on `rc == 0` — exactly as
`b21_arm_w21.sh` writes `B21_ARM_READY` last and only on `>= 4` rows. Then the FAIL end (`--gate
/mnt/d/hf_w19/gate`, exit 1) *cannot* leave a marker behind, which is a two-directional demonstration of the
interlock itself and costs nothing to run.

**Do not treat wave 21's `G21_PASSED` as forged.** It was written after the full pre-registered checklist, and
the checklist's three exit codes are quoted in the results doc. The finding is that the design permitted a
marker that could have been written without them, in a wave whose sibling marker is protected by an explicit
prohibition — and that the controller's first read of the situation ("an interlock with no writer") was itself
wrong, because the writer was named in the *plan* at line 173 and the grep had covered only `*.sh` and `*.py`.
**Grep the plan too: a step a human performs is still a writer, and it is the weakest kind.**

**Corroboration that the ordering held, independent of anyone's word** (added at wave 21's write-up): the three
scorer outputs are stamped `st.txt 09:59:30.080Z` (`--self-test`, 20 of 20), `w19end.txt 09:59:30.240Z` (the FAIL
end on `/mnt/d/hf_w19/gate`, exit 1) and `g21_p2p3.txt 09:59:30.366Z` (the PASS end, exit 0); `G21_PASSED` is
stamped **`10:00:12.974Z`**, 42 seconds later, and carries the `BuildId`. The mtimes say what the notes say. *That
is the difference between a claim and a check, and it is also exactly why the marker should have been written by
the code that produced those three exit codes.*

### RULE D20 IS PERMANENTLY `D-UNEVALUATED`. DO NOT HARVEST IT.
**Status:** **FENCED 2026-08-11 (wave 21)** — the **third** permanent fence in this series, after **RULE F14**
(charter §3) and **RULE S16** (charter §3a) · the wave-20 measurement stays published as a **labelled diagnostic**
· **RULE B21 is what a later wave may cite instead**

The fence text below is `docs/synthetic-af-bank-followups-wave21-design.md` §1.4, **verbatim**, as committed in
`3a4db7c` at `2026-08-11T09:11:17Z` — *before* any wave-21 measurement existed:

> **RULE D20 IS PERMANENTLY `D-UNEVALUATED`. DO NOT HARVEST IT.**
>
> 1. Its measurement is published as a diagnostic; no re-run of the same probe on the same dataset can be blind.
> 2. `D20-B`'s tolerance is finer than its own instrument's resolution and **is observed to fail a correct
>    program on 4 of 8 real logs** — so repairing it now is repairing a rule after the data.
> 3. A wave that repairs a failed gate and immediately harvests its verdict teaches the next wave to do the same.
>
> **The diagnostic is never promoted to a verdict by hand, by this wave or any later one.** What a later wave
> may cite is RULE B21's verdict, on RULE B21's population.

**What was done instead, and it is the remedy the F14 fence already prescribed:** fence the old rule, write a
**new** one whose clauses are correct, and apply it **prospectively** to a population measured after the fix.
**RULE B21** returned **`B-DEMONSTRATED`**, 6 of 6 on `V1`/`V2`/`V3`/`A`/`B`/`D`, over the **six factor-2 datasets
RULE D20 never touched** (`D10, D17, D09, D08, D15, D14` — the bank's seven factor-2 datasets minus `D12`).
`optimize/detected` **is** the post-mutation bundle: binning `1 → 2`, `PixelScale` `NaN →` finite and equal to the
console at its own precision, and the difference from `optimize/baseline` **exactly** `{DetectionBinning,
PixelScale}` across 55 fields. `B-REFUTED` was reachable by construction and did not occur.

**Reason 2 stopped being an argument during wave 21.** `G21-P3b` applied wave 20's `D20-B` predicate to wave 21's
own gate logs and it failed on exactly the four pre-registered datasets — so the defect is an **observation on two
independent sets of real artifacts**, not a reading of the source.

**What a later wave may and may not do.**

- **May:** cite RULE B21's verdict on RULE B21's population; cite wave 20's `D12` numbers **as a labelled
  diagnostic**; write a *new* rule over a *new* population.
- **May not:** re-score RULE D20, re-run its probe on `D12`, repair `D20-B` and apply it retroactively, or report
  the wave-20 diagnostic as a rule result. **A repaired clause scored against the data that motivated the repair
  is not a measurement.**

Reproduce: `docs/synthetic-af-bank-followups-wave21-design.md` §1 and §1.4;
`docs/synthetic-af-bank-followups-wave21-results.md` §3 and §4;
`docs/synthetic-af-bank-followups-wave20-results.md` §3.2 (the diagnostic, labelled);
`cat /mnt/d/hf_w21/b21_score.txt`.

### F72 — The A1–A9 UI check was blocked for NINE waves by a crash that does not reproduce, and the "decisive" isolation test would have produced a confident WRONG attribution
**Status:** **item C UNBLOCKED 2026-08-11 (wave 18/19 boundary)** · found by re-running the same configuration, which nobody had done · **the plugin loads cleanly and the UI renders**

Waves 8–17 recorded item C as blocked. Wave 17 got a connected session at last, cleared every previously-named
blocker — composited desktop, **deploy verified byte-for-byte before launch**, NINA launched with a non-zero
hwnd and a real title — and then NINA crashed during startup:

```
CompositionRoot.cs|Compose|145  System.NullReferenceException
  AsyncObservableCollection`1.RunOnSynchronizationContext (:44) / InsertItem (:49)
  PluggableBehaviorSelector`2..ctor (:39)
```

The controller reported this twice: first as "entirely NINA core, so probably not our plugin", then — after the
wave-18 pre-registration correctly pointed out that `PluggableBehaviorSelector<,>..ctor` is **exactly** where
MEF-imported plugin behaviours are inserted and HocusFocus exports two — as "the blocked isolation test is
DECISIVE". **Both readings were over-confident, and the second would have been actively wrong.**

| configuration | outcome |
|---|---|
| 16:06 local, NINA 3.3.0.1048 **with** plugin | **ran fine** — 169 KB log, **148 Hocus mentions**, 0 NREs |
| 18:12 local, same NINA **with** plugin | **crashed** — 3 `PluggableBehaviorSelector` NREs, 0 Hocus mentions |
| 23:41 local, same NINA **without** plugin (the authorised isolation test) | alive |
| 23:43 local, same NINA **with** plugin | **alive** — `Successfully loaded plugin Hocus Focus version 4.0.0.12`, **0 ERROR, 0 NRE** |

> **The crash does not reproduce.** Run alone, the isolation test reads as decisive — *works without the plugin,
> crashes with it* — and it would have convicted the plugin. **The control that mattered was REPETITION, not
> isolation**, and it costs one extra launch. *n = 1 on a crash is not an attribution*, and a blocker recorded
> from a single sample held an item for nine waves.

**The host is `3.3 NIGHTLY #048`**, which makes a transient startup race far likelier than a plugin defect.

### What the UI check then established, as rendered pixels
- **The plugin loads in NINA 3.3.0.1048** — `PluginLoader.cs:410 Successfully loaded plugin Hocus Focus version
  4.0.0.12`, and `Found 1 Hocus Focus Cameras`. Final log: **0 ERROR, 0 NRE**.
- **The options UI renders unclipped**, all four tabs present (`AF AutoFocus`, `Star Detector`, `Star Annotator`,
  `Camera Sim`), every control bound.
- **`Max Outlier Rejections: 0` is visible in the rendered AF panel** — wave 14's product change confirmed in the
  running app for the first time. *(`astrodet` stores 0 explicitly, so this shows the control renders and binds,
  not that the code default was consulted.)*
- **A3/A9's exposure-recommendation row renders**: *"Run an auto-focus to get a recommendation"*, in the right
  panel, unclipped.

### Wave 21 — **A1 and A2 CONFIRMED as rendered pixels**, against the source file

Attempted a second time (2026-08-11, after the wave's arms finished, so nothing was competing). **NINA loaded
cleanly again — the nine-wave crash did not reproduce on a second independent attempt**, which is what turns
wave 18/19's single non-reproduction into a repeated one.

The Imaging tab's HocusFocus **Autofocus dock renders a LOADED report** — `2026-07-24--17-43-20.json`, selected
in the dock's own dropdown, with **no auto-focus having run in the session**. So the info rows can only have
come from the round-tripped file, which is A1, and the lookup that populates them is A2. Every rendered row was
checked against the JSON on disk:

| rendered row | rendered | the file |
|---|---|---|
| Time | `2026-07-24 17:43-20` | `Timestamp 2026-07-24T17:43:20.234…` |
| Position | `26294 -> 22089` | `InitialFocusPoint.Position 26294.0` → `CalculatedFocusPoint.Position 22089.0` |
| HFR Change | `13.50 -> 1.70` | `13.5` → `1.7` |
| Estimated Final HFR | `1.70` | `FinalHFR 1.7` |
| Temperature | `1.16 °C` | `1.1599999999999966` |
| Duration | `05:14.40` | `00:05:14.4028412` |

**This is the check wave 8's defect demanded.** Wave 8 found the loaded-run info rows *"NEVER worked"* in the
field while their tests passed, because the fixtures omitted the three interface-typed option blocks. A1/A2 are
now confirmed **in the running app, against the bytes on disk** — not against a hand-built object.

**Honest limits of this observation.** `Hyperbolic σ(focus)` and `Best-focus stability (LOO)` render as
`± -- steps` — those fields are absent from this 2026-07-24 report, so the blank is correct behaviour and *not*
a confirmation that they round-trip. A report that carries them is needed to check those two rows.

### Wave 21 — the wizard was driven to completion for the first time; **A4 and A6 confirmed, and the run found [F76](#f76)**

The entry point is **not** the Imaging dock (which is collapsed to a sliver on the right edge). It is
**Plugins → Hocus Focus → Star Detector → "Optimize Star Detection"**, a button in
`Resources/OptionsDataTemplates.xaml:1002` bound to `OptimizeStarDetectionCommand`, which calls
`HocusFocusPlugin.LaunchStarDetectionOptimizer`. *Finding that in source cost two greps and saved rearranging
the user's saved dock layout.*

The run was pointed at a **copy** of `D11_rc10_585_afbin2` at `D:\hf_w21\uicheck_run`, never at the bank
itself — the 42 landings are fingerprinted and were re-verified byte-identical afterwards.

**A4 — in-run guidance: CONFIRMED, rendered.**
```
Refining settings
[========                    ]  64 / 400 (008)
Found better settings so far
49 ms per step - at most 17 s more, usually much less
```

**A6 — abort advice: CONFIRMED, rendered unclipped**, on the same in-run page:
> *"Cancel stops the search only. Nothing is written to your profile unless you click Accept."*

**A3/A9 upgraded:** the exposure row, previously only ever seen as the placeholder *"Run an auto-focus to get a
recommendation"*, rendered a **real** recommendation — `2.5 s (unchanged; measured star S/N 18.8; target 10)`.

**A5 and A7 — NOT confirmed, and the reason is the defect.** `Continue optimizing` (the re-run / F32 exposed
restart) and the rest of the action row **exist in the visual tree but have no on-screen location** — see
**[F76](#f76)**. They cannot be confirmed as rendered pixels until F76 is fixed.

**A8 — NOT exercised.** A8 is *"the capped step says what it converges toward"*. This run's step was
`55 → 53`, **not capped**, so the F49(c) text never had to appear. A8 needs a replay that actually hits a
capped step; it is not enough to reach the summary.

### Next step
**A5, A7 and A8 remain unconfirmed as rendered pixels.** A5/A7 are **blocked on [F76](#f76)**. A8 needs a saved
run whose recommended step is capped. Nothing was written to the profile by this session: the wizard was closed
via the title-bar X, which discards.
**Reproduce:** launch NINA → profile `astrodet` → Plugins → Hocus Focus → Star Detector → *Optimize Star
Detection*.


### F15 — `optimize --per-run` overwrites each run's stored settings
**Status:** **Fixed (wave 13)** · open for thirteen waves · the fix does BOTH halves of the next step, because
they answer different failures

The prepass writes `optimized_settings.json` back into the run folder as well as `--out`. That destroyed
`bobp_m101`'s historical "row (a)" settings (`sens 50 / clip 9.5`) mid-investigation, leaving only the two knobs
quoted in the write-up, so the baseline had to be reconstructed rather than reproduced.

**It happened again in wave 3, on both banks at once (2026-08-03).** The F35 validation ran
`optimize --per-run` over `D:\SyntheticAutofocusBank` and `D:\Autofocus Bank`, so every run's in-place
`optimized_settings.json` in **both** banks is now the wave-3 landing. Nothing comparative was lost this time —
the wave-1 control arms live in their own `--out` directories (`hf_f23\H_A`, `hf_f23\H_real_A`) and are
untouched — but that was luck of workflow, not a property of the tool. **Any validation pass silently
re-baselines the banks' stored settings**, and the only reason this one was harmless is that the comparison
never read them.

Note the interaction that makes this sharper than it looks: `bank-verify --opt-a/--opt-b` and
`golden eval --params optimized` both read the **run folder** copy by default. So a prepass and a later scoring
run that were meant to be independent can silently share an arm.

> **WAVE 14 — CONFIRMED IN AN ARM, not only in the unit tests written with the fix.** Wave 14's gate was the
> first arm in this series to run on a post-fix binary, where the write-back is opt-in behind
> `--update-run-folder` and no arm passes it. All **42** bank `optimized_settings.json` files (both banks plus
> `_prior_reports`) were fingerprinted by sha256 + size + mtime **before** any arm ran.
>
> **42 of 42 byte-identical after the 8-run gate AND after all six 39-run `af-fit` rungs. 0 changed, 0
> could-not-look.** For thirteen waves every `optimize --per-run` pass silently re-baselined both banks; this is
> the first arm that provably did not.
>
> *A fix confirmed only by the tests written with it is confirmed by its author*, and the fingerprint had to be
> taken **before** anything ran or there would have been nothing to compare against. **Read it against the
> pre-arm snapshot, not against the live bank**: `D:\hf_w14\bank_landing_snapshot_BEFORE_W14D` holds all 42 files
> as they stood after Stage B, because wave 14's item 5 runs **wave-10 binaries that predate this fix**.
> Reproduce: `/mnt/d/hf_w14/bank_landing_fingerprint_BEFORE.json` vs
> `/mnt/d/hf_w14/bank_landing_snapshot_BEFORE_W14D`.

### Fixed (wave 13)

**The write is now OPT-IN.** `optimize` writes its landing to `--out` unconditionally and into each run's own
source folder **only** with `--update-run-folder`. The exact invocation thirteen waves used —
`optimize --per-run --runs <bank> --out <dir>` — no longer touches either bank.

**And when it does not write, it SAYS SO.** The absent write prints a line naming the count of run folders left
untouched *and* the three readers that will therefore see whatever was there before
(`bank-verify --opt-a/--opt-b`, `golden eval --params optimized`, `review --runs <same>`). *An absent side
effect has to be visible, because the whole defect was that it was not.*

**The backup keeps the OLDEST displaced landing, not the most recent one**, and that is the load-bearing
choice. With the flag given, an existing `optimized_settings.json` is copied to
`optimized_settings.displaced.json` — but **only if nothing has been preserved there yet**. The file worth
keeping is the one nobody can reproduce (`bobp_m101`'s `sens 50 / clip 9.5` row); every landing written since is
reproducible from a recorded command line and survives in its own `--out` directory. **A rolling backup would
have lost the irreplaceable file on the second pass and kept a reproducible one in its place** — which is F15's
own failure, re-implemented one level down.

The backup's name is deliberately not `optimized_settings.json`: every bank reader matches that name **exactly**,
so a backup sharing it would be read back *as* a landing. That is the same reasoning
`SettingsHandoffFileName` already carries, and it is asserted rather than remembered.

**Why the capability is kept rather than deleted.** The three readers above locate the landing by the run-folder
copy. Deleting the write would break them; making it opt-in turns a silent side effect into something a command
line **says**. A prepass and a later scoring run can still share an arm — but now only when someone asked for it.

**Guarded by `LandingWritebackTests` (8 tests).** The policy lives in `TestApp/LandingWriteback.cs`, outside the
WPF-bound runner, so it is tested against a real filesystem rather than asserted about: the flag is matched
exactly (`--update-run-folder-never` and `--no-update-run-folder` must NOT opt in — a prefix match would let a
future flag silently re-enable the write, which is the shape of the defect); the backup preserves the oldest;
and a **source guard** requires the per-run write loop to stay gated on the opt-in. **The guard checks itself
first**, on literals in both directions, and it was verified to FAIL against `f9f2074`'s source before it was
called a test.

**What this does NOT undo.** Both banks' stored `optimized_settings.json` are still the last landing some wave
wrote into them; nothing here recovers wave 1's. Wave 13 snapshotted all 42 of them to
`D:\hf_w13\bank_settings_snapshot\` before its own arms ran, which is the first time that has been done.

### F37 — The CI test host crashes natively (`AccessViolationException`), aborting ~2000 tests with zero failures
**Status:** Open · found 2026-08-04 merging [PR #170](https://github.com/ghilios/hocus-focus/pull/170)

CI's `Run unit tests` job died with
`Fatal error. System.AccessViolationException: Attempted to read or write protected memory` — a **native crash of
the test host process**, not a test failure. The run reported `Passed: 1389, Failed: 0` out of a suite of
**3371**, so roughly 2000 tests never executed, and the job failed on exit code 1 rather than on any assertion.

**It is provably not caused by the code under test.** Two runs on the same branch, minutes apart:

| run | commit | contents | result |
|---|---|---|---|
| `30873901415` | `a29f6df` | **every code change in the PR** | **success** |
| `30874276311` | `b9095fa` | `a29f6df` **+ two markdown files** | **AccessViolationException** |
| `30874276311` (re-run) | `b9095fa` | unchanged | **success** |

A docs-only delta cannot cause a native memory violation, and re-running the identical commit passed. So this is
non-deterministic and environmental.

**The preserved evidence.** The failed attempt's TRX artifact survives (`8879261120`, attempt 1). It contains
1390 results — **1389 `Passed`, 0 `Failed`**, one `NotExecuted` (`SavedRuns_Benchmark`, skipped by design). The
last tests to complete finished at `03:35:05.622` and the host died at `03:35:06.004`. Tests run in parallel, so
the last-completed test is **not** necessarily the one that crashed — the TRX cannot identify the culprit, which
is exactly why the next step below is needed.

**The prime suspect is the coverage profiler, not the tests.** `.github/workflows/tests.yml:38` runs with
`--collect "XPlat Code Coverage"`, attaching the coverlet profiler to a test host that loads
**OpenCvSharp native** (`OpenCvSharpExtern.dll`). An instrumenting profiler over heavy native interop is a
well-known source of `AccessViolationException`, and it is the clearest difference between CI and local: local
full-suite runs use no `--collect` and passed **3371/3371 twice consecutively** on the same commit.

**Do not conflate this with the known flaky test.** The recorded flake
(`SendAsync_WritesOnABackgroundThread`, EAT serial transport) is an **assertion failure in one test**; this is a
**process crash with no failing assertion**. Different signature, and treating them as one thing would hide
whichever is real.

**One local observation that may or may not belong here.** During the same session, one local full-suite run
reported a single failure while two `optimize --per-run` bank passes were saturating the CPU. Its name was not
captured, and it did not recur across four subsequent full runs. Whether it is this defect, the EAT flake, or a
third thing is **unknown** — recorded so the next occurrence is not read as the first, not as evidence of a link.

**Why it matters.** A green suite is the merge gate. A failure mode that aborts two thirds of the suite while
reporting zero failures is the worst shape for a gate: it is indistinguishable at a glance from a real
regression, it costs a re-run every time, and — the real risk — **a crash that lands early enough would let a
genuine regression through in the ~2000 tests that never ran**, because nothing reports them as unexecuted.

**Next step.** Add `--blame-crash` to the CI test invocation so the run produces a sequence file and a crash
dump naming the test that was executing, and keep the existing `if: always()` artifact upload so it survives.
Then test the profiler hypothesis directly by running CI once **without** `--collect "XPlat Code Coverage"`; if
the crash stops reproducing, move coverage to a separate job so a coverage-only defect cannot fail the merge
gate. Cheap to start: the crash has now been seen once in CI, so the first step is instrumentation, not a fix.
