#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Logger = NINA.Core.Utility.Logger;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// The pre-computed detection result + paths for one reviewable frame. Accepted carry the detector's actual
    /// <c>StarBoundingBox</c> (so the overlay draws real-size boxes) + HFR + center; Rejected carries the per-reason
    /// bounding boxes so the reviewer sees what the detector did.
    /// </summary>
    public sealed class FrameReview {
        public string RunId { get; set; }
        public int FocuserPosition { get; set; }
        public string FramePath { get; set; }

        /// <summary>
        /// Produces the MTF-stretched background <see cref="BitmapSource"/> for this frame, asynchronously. This is
        /// the decoupling seam between the host and the VM: TestApp's <c>review</c> tool loads the frame from disk
        /// (via its profile-aware float-Mat loader) and runs <see cref="StarReviewImaging.BuildStretchedBitmap"/>;
        /// the in-NINA wizard provides images from already-loaded Mats. The VM never touches disk or a profile.
        /// </summary>
        public Func<Task<BitmapSource>> ImageProvider { get; set; }

        /// <summary>Accepted stars: the detector's real bounding box + its center (used for click hit-tests, the
        /// should-reject label box, and the overlay) + HFR.</summary>
        public List<(double CX, double CY, double HFR, Rect Bounds)> Accepted { get; set; } = new();
        public List<(string Reason, Rect Bounds)> Rejected { get; set; } = new();

        /// <summary>Rich per-rejected-candidate records (gate + measured value) for the "Optimize with feedback"
        /// analyzer. Populated by the production builder; may be empty for hosts that don't enable diagnostics.</summary>
        public List<Interfaces.RejectedCandidateRecord> RejectedCandidates { get; set; } = new();
    }

    /// <summary>A drawable accepted-star marker (the detector's real bounding box) in image-pixel coords.</summary>
    public sealed class AcceptedMarker {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double HFR { get; set; }

        /// <summary>Preformatted HFR for the optional on-overlay HFR label ("2.34", or "—" when unavailable).</summary>
        public string HfrText { get; set; }

        /// <summary>Foreground brush for the HFR label — normally white, recolored when this star's HFR is an outlier
        /// for the frame (more than <see cref="StarReviewHfrStats.OutlierDeviations"/> deviations from the center).</summary>
        public Brush HfrBrush { get; set; }
    }

    /// <summary>The detector's measured star position (the flux-weighted <c>Center</c>) for an accepted star, in
    /// image-pixel coords. Drawn as a small crosshair ON TOP of the green box so the marker tracks the actual star
    /// even when the structure bounding box (drawn verbatim from <c>StarBoundingBox</c>) is larger than, or
    /// off-center from, the bright core — which is what makes some boxes look "off" from their star.</summary>
    public sealed class CentroidMarker {
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>A drawable user-label box in image-pixel coords (top-left X,Y + W,H).</summary>
    public sealed class LabelBoxMarker {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        /// <summary>Preformatted HFR for the optional on-overlay HFR label ("2.34", or "—" when unavailable).</summary>
        public string HfrText { get; set; }
    }

    /// <summary>Which part of a drawn (missed) box is under the cursor: an edge or corner (resize), the interior
    /// (move), or nothing. Drives both the move/resize gesture and the cursor shown while hovering.</summary>
    public enum BoxHandle { None, Inside, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>A drawable rejected-candidate box in image-pixel coords, colored by reason.</summary>
    public sealed class RejectedMarker {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Reason { get; set; }
        public Color Color { get; set; }
    }

    /// <summary>One row of the on-screen color legend: a swatch brush, a plain-language caption, and whether the
    /// swatch is drawn dashed (user labels) or solid (detector boxes).</summary>
    public sealed class StarReviewLegendEntry {
        public Brush Brush { get; set; }
        public string Caption { get; set; }
        public bool Dashed { get; set; }
    }

    /// <summary>The kind of detector box (if any) found under a click point by <see cref="StarReviewVM.HitTestCandidate(IEnumerable{Rect}, IEnumerable{Rect}, double, double)"/>.</summary>
    public enum HitCategory {
        /// <summary>No detector box (accepted or rejected) contains the click.</summary>
        None,

        /// <summary>The click lands inside a detector ACCEPTED (green) box → a should-reject (false-positive) candidate.</summary>
        Accepted,

        /// <summary>The click lands inside a detector REJECTED box → a wrongly-rejected (recover-for-recall) candidate.</summary>
        Rejected
    }

    /// <summary>
    /// ViewModel for the interactive review window. Holds the queue of frames, the per-run labels, and the zoom/pan
    /// viewport. Labeling is MODE-LESS: the category is inferred from what is clicked — a click on an accepted box
    /// flags a should-reject, a click on a rejected box flags a wrongly-rejected, and a drag over blank space marks
    /// a missed star. All detection was done up front by <see cref="StarReviewRunner"/>; this VM only renders
    /// overlays and edits/persists labels. The screen↔image pixel mapping is delegated to the unit-tested
    /// <see cref="StarReviewViewport"/>; the label add/remove/merge logic to the unit-tested
    /// <see cref="StarReviewLabelStore"/>.
    /// </summary>
    public class StarReviewVM : BaseINPC {
        private readonly IReadOnlyList<FrameReview> queue;
        private readonly Dictionary<string, StarReviewRunLabels> labelsByRun;
        private readonly string labelsDir;

        // The profile's "Measurement Averaging" choice — drives whether the corner stats + per-star outlier test use
        // median + scaled MAD (Median) or mean + std-dev (MeanOutliers), matching the detector's HFR aggregation.
        private readonly MeasurementAverageEnum measurementAverage;

        // Per-reason overlay colors, matching T6's annotated-PNG legend (RGB here; BGR there).
        private static readonly Dictionary<string, Color> ReasonColors = new(StringComparer.Ordinal) {
            { "TooDistorted",   Color.FromRgb(255, 255, 0) },   // yellow
            { "Degenerate",     Color.FromRgb(255, 165, 0) },   // orange
            { "Saturated",      Color.FromRgb(255, 0, 0) },     // red
            { "LowSensitivity", Color.FromRgb(0, 255, 255) },   // cyan
            { "NotCentered",    Color.FromRgb(255, 0, 255) },   // magenta
            { "TooFlat",        Color.FromRgb(0, 0, 255) },     // blue
            { "Contaminated",   Color.FromRgb(128, 0, 128) },   // purple
        };

        // Plain-language captions for the rejection reasons, shown in the legend instead of the raw enum names.
        private static readonly Dictionary<string, string> ReasonCaptions = new(StringComparer.Ordinal) {
            { "TooDistorted",   "Too distorted" },
            { "Degenerate",     "Degenerate shape" },
            { "Saturated",      "Saturated" },
            { "LowSensitivity", "Below sensitivity" },
            { "NotCentered",    "Off-center" },
            { "TooFlat",        "Too flat" },
            { "Contaminated",   "Contaminated" },
        };

        // Fixed marker colors (the detector ACCEPTED box + the three user-label boxes). Centralized here so the
        // overlays AND the legend draw from one source and can't drift. Frozen brushes are safe to share/bind.
        private static readonly Color AcceptedColor = Color.FromRgb(0x00, 0xFF, 0x00);        // green
        private static readonly Color MissedColor = Color.FromRgb(0x00, 0xE5, 0xFF);          // cyan
        private static readonly Color ShouldRejectColor = Color.FromRgb(0xFF, 0x8C, 0x00);    // orange
        private static readonly Color WronglyRejectedColor = Color.FromRgb(0x39, 0xFF, 0x14); // bright green

        /// <summary>Detector ACCEPTED-box stroke (green). Bound by the accepted-marker overlay and the legend.</summary>
        public Brush AcceptedBrush { get; } = FrozenBrush(AcceptedColor);

        /// <summary>User "missed" label stroke (cyan, dashed).</summary>
        public Brush MissedBrush { get; } = FrozenBrush(MissedColor);

        /// <summary>User "should-reject" label stroke (orange, dashed).</summary>
        public Brush ShouldRejectBrush { get; } = FrozenBrush(ShouldRejectColor);

        /// <summary>User "wrongly-rejected / keep" label stroke (bright green, dashed).</summary>
        public Brush WronglyRejectedBrush { get; } = FrozenBrush(WronglyRejectedColor);

        /// <summary>Normal (non-outlier) HFR label color — white, matching the rest of the overlay text.</summary>
        public Brush HfrNormalBrush { get; } = FrozenBrush(Colors.White);

        /// <summary>Outlier HFR label color — bright red, to pull the eye to a star whose HFR is far from the frame's
        /// center (bloated or suspiciously tiny). Distinct from the green/orange/cyan box strokes.</summary>
        public Brush HfrOutlierBrush { get; } = FrozenBrush(Color.FromRgb(0xFF, 0x40, 0x40));

        private static SolidColorBrush FrozenBrush(Color c) {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public StarReviewVM(
            IReadOnlyList<FrameReview> queue,
            Dictionary<string, StarReviewRunLabels> labelsByRun,
            string labelsDir,
            MeasurementAverageEnum measurementAverage = MeasurementAverageEnum.Median) {
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.labelsByRun = labelsByRun ?? throw new ArgumentNullException(nameof(labelsByRun));
            this.labelsDir = labelsDir ?? throw new ArgumentNullException(nameof(labelsDir));
            this.measurementAverage = measurementAverage;

            Viewport = new StarReviewViewport();
            LegendEntries = BuildLegend();

            NextCommand = new RelayCommand(Next, () => CurrentIndex < queue.Count - 1);
            PrevCommand = new RelayCommand(Prev, () => CurrentIndex > 0);
            SaveCommand = new RelayCommand(SaveAll);
            FitCommand = new RelayCommand(RequestFit);
            UndoCommand = new RelayCommand(Undo, () => undoStack.Count > 0);
            RedoCommand = new RelayCommand(Redo, () => redoStack.Count > 0);

            CurrentIndex = 0;
            LoadCurrent(fitView: true);
        }

        // ---- Navigation / current frame --------------------------------------------------------------------

        private int currentIndex;
        public int CurrentIndex {
            get => currentIndex;
            private set {
                if (currentIndex != value) {
                    currentIndex = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(PositionLabel));
                }
            }
        }

        public int QueueCount => queue.Count;

        public string PositionLabel => $"{CurrentIndex + 1} / {queue.Count}";

        private string cursorPositionText;

        /// <summary>The image (sensor) pixel coordinates under the mouse cursor, shown in the header so a region can
        /// be described precisely. Null/empty when the cursor is off the image. Updated by the control on mouse move.</summary>
        public string CursorPositionText {
            get => cursorPositionText;
            set { if (cursorPositionText != value) { cursorPositionText = value; RaisePropertyChanged(); } }
        }

        private string frameHeader;
        public string FrameHeader {
            get => frameHeader;
            private set { frameHeader = value; RaisePropertyChanged(); }
        }

        private BitmapSource frameImage;
        public BitmapSource FrameImage {
            get => frameImage;
            private set {
                frameImage = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ImageWidth));
                RaisePropertyChanged(nameof(ImageHeight));
            }
        }

        public double ImageWidth => frameImage?.PixelWidth ?? 0;
        public double ImageHeight => frameImage?.PixelHeight ?? 0;

        public StarReviewViewport Viewport { get; }

        // All markers live inside a canvas whose RenderTransform scales everything (including stroke width). At
        // fit-to-window zoom (Scale ~0.1-0.3) a hardcoded stroke would scale down to nothing, so bind thickness
        // INVERSELY to the current scale to keep on-screen stroke width roughly constant. Clamp to MinScale so the
        // thickness never blows up at extreme zoom-out.
        public double MarkerStrokeThickness => 1.5 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);

        // Slightly heavier for the three user-applied label markers so they read above the detector overlay.
        public double LabelStrokeThickness => 2.5 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);

        // Inverse-zoom scale for the on-overlay HFR text so it stays a roughly constant on-screen size (the text
        // lives inside the zoomed canvas, like the markers). Clamped to MinScale so it can't blow up when zoomed out.
        public double MarkerTextScale => 1.0 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);

        // Vertical translate (image-space, applied AFTER the inverse-zoom scale) that lifts the HFR label up by one
        // text-height so it sits just ABOVE the detection box with no overlap. -15·MarkerTextScale image-units works
        // out to a constant ≈15 px on screen (one FontSize-10 line + a 1 px gap) at any zoom.
        public double HfrLabelOffset => -15.0 * MarkerTextScale;

        // Default ON — HFR is the primary signal the labeler uses to judge whether a flagged star has enough
        // signal to keep, so it should be visible the moment Review opens.
        private bool showHfr = true;

        /// <summary>Toggle: when on, every accepted star (and every labeled wrongly-rejected / missed box) shows its
        /// HFR on the overlay. Bound to a checkbox in the toolbar.</summary>
        public bool ShowHfr {
            get => showHfr;
            set { if (showHfr != value) { showHfr = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowFrameStats)); } }
        }

        private string frameStatsText = string.Empty;

        /// <summary>The corner-overlay caption summarizing the current frame's accepted-star HFRs — e.g.
        /// "Median HFR: 2.34 ± 0.12  (n=42)" (Median mode) or "Mean HFR: …" (MeanOutliers mode). Empty when the frame
        /// has no measured stars. Recomputed per frame in <see cref="LoadCurrent"/>.</summary>
        public string FrameStatsText {
            get => frameStatsText;
            private set { if (frameStatsText != value) { frameStatsText = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowFrameStats)); } }
        }

        /// <summary>Whether the corner HFR-stats overlay is shown: it follows the "Show HFR" toggle and hides when the
        /// frame has no stats text.</summary>
        public bool ShowFrameStats => ShowHfr && !string.IsNullOrEmpty(FrameStatsText);

        /// <summary>Raises the zoom-dependent marker-thickness/text bindings. The view calls this after any viewport
        /// change (wheel-zoom, fit, pan) so the stroke widths + HFR text size track the current scale.</summary>
        public void NotifyViewportChanged() {
            RaisePropertyChanged(nameof(MarkerStrokeThickness));
            RaisePropertyChanged(nameof(LabelStrokeThickness));
            RaisePropertyChanged(nameof(MarkerTextScale));
            RaisePropertyChanged(nameof(HfrLabelOffset));
        }

        private static string FormatHfr(double hfr) => double.IsNaN(hfr) || hfr <= 0.0 ? "—" : hfr.ToString("F2");

        // Markers in IMAGE pixel coords; the view applies the viewport transform to place them.
        public ObservableCollection<AcceptedMarker> AcceptedMarkers { get; } = new();

        // Dev/diagnostic switch — intentionally NOT a UI toggle, to keep the labeler uncluttered. When true, each
        // accepted star also gets a small crosshair drawn at the detector's measured Center (populated into the
        // CentroidMarkers collection below and rendered by the centroid ItemsControl in StarReviewControl.xaml).
        // Hidden by default; flip to true in code to surface the crosshairs — handy when diagnosing box-vs-centroid
        // placement, since the structure bounding box and the flux-weighted centroid are computed independently and
        // can legitimately differ for asymmetric stars.
        private static readonly bool ShowCentroidMarkers = false;

        /// <summary>Per-accepted-star measured-centroid markers (the detector's <c>Center</c>), in image-pixel
        /// coords. Drawn as a small crosshair on top of the green box so the marker reflects where the detector
        /// actually measured the star, independent of the structure bounding box the box layer draws. Populated only
        /// when <see cref="ShowCentroidMarkers"/> is enabled (off by default).</summary>
        public ObservableCollection<CentroidMarker> CentroidMarkers { get; } = new();
        public ObservableCollection<RejectedMarker> RejectedMarkers { get; } = new();
        public ObservableCollection<LabelBoxMarker> MissedMarkers { get; } = new();
        public ObservableCollection<LabelBoxMarker> ShouldRejectMarkers { get; } = new();
        public ObservableCollection<LabelBoxMarker> WronglyRejectedMarkers { get; } = new();

        /// <summary>The on-screen color legend (swatch + plain-language caption), built once. Detector boxes are
        /// drawn solid, the three user-label types dashed — matching the overlays. Generated from the same color
        /// source as the markers so the two can't drift.</summary>
        public IReadOnlyList<StarReviewLegendEntry> LegendEntries { get; }

        private IReadOnlyList<StarReviewLegendEntry> BuildLegend() {
            var entries = new List<StarReviewLegendEntry> {
                new() { Brush = AcceptedBrush, Caption = "Accepted star", Dashed = false }
            };
            foreach (var kv in ReasonColors) {
                var caption = ReasonCaptions.TryGetValue(kv.Key, out var c) ? c : kv.Key;
                entries.Add(new StarReviewLegendEntry { Brush = FrozenBrush(kv.Value), Caption = "Rejected: " + caption, Dashed = false });
            }
            entries.Add(new StarReviewLegendEntry { Brush = MissedBrush, Caption = "Missed (you marked)", Dashed = true });
            entries.Add(new StarReviewLegendEntry { Brush = ShouldRejectBrush, Caption = "Should reject (you marked)", Dashed = true });
            entries.Add(new StarReviewLegendEntry { Brush = WronglyRejectedBrush, Caption = "Keep / wrongly rejected (you marked)", Dashed = true });
            return entries;
        }

        /// <summary>Concise, wrapping interaction help shown under the image (the color key now lives in the
        /// legend panel to the right).</summary>
        public string HelpText =>
            "Left-drag over a missed star to mark it. Click an accepted box to flag a false positive, or a " +
            "rejected box to keep it. Drag inside a box you drew to move it, or its edges/corners to resize. " +
            "Right-click a label (or click it again) to remove it. Ctrl+Z / Ctrl+Y to undo / redo. Wheel to zoom, " +
            "right-drag to pan.";

        public string CountsLabel {
            get {
                var labels = CurrentRunLabels;
                var (missed, reject, wrongly) = StarReviewLabelStore.Counts(labels);
                var pos = CurrentPositionLabels;
                var posMissed = pos?.Missed?.Count ?? 0;
                var posReject = pos?.ShouldReject?.Count ?? 0;
                var posWrongly = pos?.WronglyRejected?.Count ?? 0;
                return $"this frame: {posMissed} missed, {posReject} should-reject, {posWrongly} wrongly-rejected | " +
                       $"run total: {missed} missed, {reject} should-reject, {wrongly} wrongly-rejected";
            }
        }

        public RelayCommand NextCommand { get; }
        public RelayCommand PrevCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand FitCommand { get; }
        public RelayCommand UndoCommand { get; }
        public RelayCommand RedoCommand { get; }

        /// <summary>Raised when the VM wants the view to re-fit the image (initial load / Fit button).</summary>
        public event EventHandler FitRequested;

        private FrameReview Current => queue[CurrentIndex];

        private StarReviewRunLabels CurrentRunLabels =>
            labelsByRun.TryGetValue(Current.RunId, out var l) ? l : null;

        private StarReviewPositionLabels CurrentPositionLabels {
            get {
                var run = CurrentRunLabels;
                return run?.Positions.FirstOrDefault(p => p.FocuserPosition == Current.FocuserPosition);
            }
        }

        // Navigation no longer writes to disk: labels live in-memory (labelsByRun) and are flushed only by SaveAll
        // (the wizard's Accept/Re-optimize, or the TestApp host's save-on-close), so a Cancel discards them.
        private void Next() {
            if (CurrentIndex < queue.Count - 1) {
                CurrentIndex++;
                // Keep the current zoom/pan when stepping frames so the same region of interest stays in view as
                // the user rotates through the sweep (all frames in a run share the same dimensions).
                LoadCurrent(fitView: false);
            }
        }

        private void Prev() {
            if (CurrentIndex > 0) {
                CurrentIndex--;
                LoadCurrent(fitView: false);
            }
        }

        private void RequestFit() => FitRequested?.Invoke(this, EventArgs.Empty);

        private void LoadCurrent(bool fitView) {
            var f = Current;
            FrameHeader = $"{f.RunId}  @ focuser {f.FocuserPosition}  |  {f.Accepted.Count} accepted";

            // Build the MTF-stretched background image (via the host-supplied provider, then marshal to the UI).
            FrameImage = null;
            _ = LoadImageAsync(f, fitView);

            // Overlays in image coords — accepted stars draw the detector's REAL bounding box (top-left + size).
            // The measured-Center crosshair is a dev-only overlay, hidden by default (see ShowCentroidMarkers).
            // Per-frame HFR center + deviation (over the accepted stars), per the profile's averaging mode. Drives the
            // corner stats overlay and recolors each star's HFR label when it is an outlier for this frame.
            var (hfrCenter, hfrDeviation) = StarReviewHfrStats.Compute(f.Accepted.Select(a => a.HFR), measurementAverage);
            FrameStatsText = StarReviewHfrStats.FormatStats(hfrCenter, hfrDeviation, f.Accepted.Count, measurementAverage);

            AcceptedMarkers.Clear();
            CentroidMarkers.Clear();
            foreach (var (cx, cy, hfr, b) in f.Accepted) {
                var hfrBrush = StarReviewHfrStats.IsOutlier(hfr, hfrCenter, hfrDeviation) ? HfrOutlierBrush : HfrNormalBrush;
                AcceptedMarkers.Add(new AcceptedMarker { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, HFR = hfr, HfrText = FormatHfr(hfr), HfrBrush = hfrBrush });
                if (ShowCentroidMarkers) {
                    CentroidMarkers.Add(new CentroidMarker { X = cx, Y = cy });
                }
            }
            RejectedMarkers.Clear();
            foreach (var (reason, b) in f.Rejected) {
                var color = ReasonColors.TryGetValue(reason, out var c) ? c : Colors.Gray;
                RejectedMarkers.Add(new RejectedMarker {
                    X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, Reason = reason, Color = color
                });
            }

            RefreshLabelMarkers();

            NextCommand.NotifyCanExecuteChanged();
            PrevCommand.NotifyCanExecuteChanged();
        }

        private async Task LoadImageAsync(FrameReview f, bool fitView) {
            var provider = f.ImageProvider;
            if (provider == null) {
                return;
            }
            try {
                var bmp = await provider().ConfigureAwait(true);
                FrameImage = bmp;
                // Only re-fit on the initial load / Fit button; navigating frames preserves the current viewport.
                if (fitView) {
                    RequestFit();
                }
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to load/stretch frame {f.FramePath}");
                Console.Error.WriteLine($"Failed to load frame {f.FramePath}: {ex.Message}");
            }
        }

        // ---- Labeling (delegates to the pure store) --------------------------------------------------------

        /// <summary>
        /// MISSED pass: adds a user-drawn box over a missed star (or toggles one off if the drag lands on an
        /// existing missed box's center). The view calls this on mouse-up of a rubber-band drag with the box
        /// already normalized to W,H ≥ 0; the view does NOT call it for a plain click on blank space (no
        /// click-to-create — that drops stray boxes). Recorded as an undoable edit.
        /// </summary>
        public void AddMissedBox(double imageX, double imageY, double width, double height) {
            var run = CurrentRunLabels;
            if (run == null) {
                return;
            }
            var pos = StarReviewLabelStore.GetOrAddPosition(run, Current.FocuserPosition);
            var cx = imageX + width / 2.0;
            var cy = imageY + height / 2.0;
            var idx = StarReviewLabelStore.NearestBoxIndexWithin(pos.Missed, cx, cy, StarReviewLabelStore.SamePointTolerancePx);
            if (idx >= 0) {
                var removed = pos.Missed[idx];
                pos.Missed.RemoveAt(idx);
                RecordEdit(run.RunId, Current.FocuserPosition, LabelKind.Missed, removed, wasAdd: false);
            } else {
                var box = new StarReviewLabelBox(imageX, imageY, width, height);
                pos.Missed.Add(box);
                RecordEdit(run.RunId, Current.FocuserPosition, LabelKind.Missed, box, wasAdd: true);
            }
            RefreshLabelMarkers();
        }

        // ---- Move / resize of drawn (missed) boxes ---------------------------------------------------------

        // In-progress edit state (valid between BeginMissedBoxEdit and EndMissedBoxEdit).
        private int editBoxIndex = -1;
        private BoxHandle editHandle = BoxHandle.None;
        private StarReviewLabelBox editBox;
        private double editStartX, editStartY, editStartW, editStartH; // geometry at grab (undo baseline + move base)
        private double editGrabX, editGrabY;                            // cursor image point at grab (move delta base)

        /// <summary>True while a move/resize gesture is active (the view routes mouse events to UpdateMissedBoxEdit).</summary>
        public bool IsEditingBox => editBoxIndex >= 0;

        /// <summary>Finds the drawn (missed) box under (<paramref name="px"/>,<paramref name="py"/>): its index and the
        /// handle (edge/corner = resize, interior = move) within <paramref name="edgePx"/> image pixels of an edge.
        /// Smallest box wins on overlap. Returns (-1, None) when no missed box is under the point. Pure query — drives
        /// both the gesture routing and the hover cursor.</summary>
        public (int Index, BoxHandle Handle) HitTestMissedBox(double px, double py, double edgePx) {
            var pos = CurrentPositionLabels;
            if (pos?.Missed == null || pos.Missed.Count == 0) {
                return (-1, BoxHandle.None);
            }
            var bestIdx = -1;
            var bestHandle = BoxHandle.None;
            var bestArea = double.MaxValue;
            for (var i = 0; i < pos.Missed.Count; i++) {
                var b = pos.Missed[i];
                double x = b.X, y = b.Y, w = b.W ?? 0.0, h = b.H ?? 0.0;
                if (px < x - edgePx || px > x + w + edgePx || py < y - edgePx || py > y + h + edgePx) {
                    continue;
                }
                bool nearL = Math.Abs(px - x) <= edgePx, nearR = Math.Abs(px - (x + w)) <= edgePx;
                bool nearT = Math.Abs(py - y) <= edgePx, nearB = Math.Abs(py - (y + h)) <= edgePx;
                BoxHandle handle =
                    nearL && nearT ? BoxHandle.TopLeft :
                    nearR && nearT ? BoxHandle.TopRight :
                    nearL && nearB ? BoxHandle.BottomLeft :
                    nearR && nearB ? BoxHandle.BottomRight :
                    nearL ? BoxHandle.Left :
                    nearR ? BoxHandle.Right :
                    nearT ? BoxHandle.Top :
                    nearB ? BoxHandle.Bottom :
                    BoxHandle.Inside;
                var area = w * h;
                if (area < bestArea) {
                    bestArea = area;
                    bestIdx = i;
                    bestHandle = handle;
                }
            }
            return (bestIdx, bestHandle);
        }

        /// <summary>Begins a move (Inside) or resize (edge/corner) of the missed box at <paramref name="index"/>,
        /// anchored at the cursor image point. Captures the starting geometry for the live update and the undo.</summary>
        public void BeginMissedBoxEdit(int index, BoxHandle handle, double grabX, double grabY) {
            var pos = CurrentPositionLabels;
            if (pos?.Missed == null || index < 0 || index >= pos.Missed.Count || handle == BoxHandle.None) {
                editBoxIndex = -1;
                return;
            }
            editBoxIndex = index;
            editHandle = handle;
            editBox = pos.Missed[index];
            editStartX = editBox.X;
            editStartY = editBox.Y;
            editStartW = editBox.W ?? 0.0;
            editStartH = editBox.H ?? 0.0;
            editGrabX = grabX;
            editGrabY = grabY;
        }

        /// <summary>Live-updates the in-progress move/resize to the current cursor image point and refreshes the
        /// overlay. No-op when no edit is active.</summary>
        public void UpdateMissedBoxEdit(double cursorX, double cursorY) {
            if (editBoxIndex < 0 || editBox == null) {
                return;
            }
            double x1 = editStartX, y1 = editStartY, x2 = editStartX + editStartW, y2 = editStartY + editStartH;
            if (editHandle == BoxHandle.Inside) {
                var dx = cursorX - editGrabX;
                var dy = cursorY - editGrabY;
                x1 += dx; x2 += dx; y1 += dy; y2 += dy;
            } else {
                if (editHandle is BoxHandle.Left or BoxHandle.TopLeft or BoxHandle.BottomLeft) x1 = cursorX;
                if (editHandle is BoxHandle.Right or BoxHandle.TopRight or BoxHandle.BottomRight) x2 = cursorX;
                if (editHandle is BoxHandle.Top or BoxHandle.TopLeft or BoxHandle.TopRight) y1 = cursorY;
                if (editHandle is BoxHandle.Bottom or BoxHandle.BottomLeft or BoxHandle.BottomRight) y2 = cursorY;
            }
            editBox.X = Math.Min(x1, x2);
            editBox.Y = Math.Min(y1, y2);
            editBox.W = Math.Abs(x2 - x1);
            editBox.H = Math.Abs(y2 - y1);
            RefreshLabelMarkers();
        }

        /// <summary>Finishes the in-progress move/resize: records it as one undoable edit when the geometry actually
        /// changed (a press that didn't move is dropped), then clears the edit state.</summary>
        public void EndMissedBoxEdit() {
            if (editBoxIndex < 0 || editBox == null) {
                editBoxIndex = -1;
                return;
            }
            var changed = Math.Abs(editBox.X - editStartX) > 1e-6 || Math.Abs(editBox.Y - editStartY) > 1e-6
                || Math.Abs((editBox.W ?? 0.0) - editStartW) > 1e-6 || Math.Abs((editBox.H ?? 0.0) - editStartH) > 1e-6;
            if (changed) {
                undoStack.Push(new LabelEdit {
                    RunId = Current.RunId, FocuserPosition = Current.FocuserPosition, Kind = LabelKind.Missed,
                    Box = editBox, IsModify = true,
                    OldX = editStartX, OldY = editStartY, OldW = editStartW, OldH = editStartH,
                    NewX = editBox.X, NewY = editBox.Y, NewW = editBox.W ?? 0.0, NewH = editBox.H ?? 0.0
                });
                redoStack.Clear();
                RaiseUndoRedo();
            }
            editBoxIndex = -1;
            editBox = null;
        }

        /// <summary>
        /// Mode-less click: hit-tests the click against BOTH the detector's ACCEPTED and REJECTED boxes and toggles
        /// THAT star's actual bounding box into the inferred label list — an accepted hit toggles a should-reject
        /// (false positive), a rejected hit toggles a wrongly-rejected (recover for recall). The view calls this on
        /// mouse-up of a click (no detector box under the press would have routed to the MISSED drag instead, via
        /// <see cref="AddMissedBox"/>), after mapping the click through <see cref="StarReviewViewport.ScreenToImage"/>.
        /// A click outside every candidate box is a no-op. On overlap the smallest-area box wins, and the category is
        /// whichever set owns that tightest box. Clicking an already-labeled star again removes it.
        /// </summary>
        public void ToggleLabelAt(double imageX, double imageY) {
            var run = CurrentRunLabels;
            if (run == null) {
                return;
            }

            var (category, hit) = HitTestCandidate(
                Current.Accepted.Select(a => a.Bounds), Current.Rejected.Select(r => r.Bounds), imageX, imageY);
            if (category == HitCategory.None) {
                return; // click outside every candidate box: no-op
            }

            var pos = StarReviewLabelStore.GetOrAddPosition(run, Current.FocuserPosition);
            var kind = category == HitCategory.Accepted ? LabelKind.ShouldReject : LabelKind.WronglyRejected;
            var list = category == HitCategory.Accepted ? pos.ShouldReject : pos.WronglyRejected;

            // Toggle semantics: if a label already covers this star (its center matches the hit box's center within
            // tolerance), remove it; otherwise record the star's ACTUAL bounding box. Either way it's an undoable edit.
            var existing = StarReviewLabelStore.NearestBoxIndexWithin(
                list, hit.X + hit.Width / 2.0, hit.Y + hit.Height / 2.0, StarReviewLabelStore.SamePointTolerancePx);
            if (existing >= 0) {
                var removed = list[existing];
                list.RemoveAt(existing);
                RecordEdit(run.RunId, Current.FocuserPosition, kind, removed, wasAdd: false);
            } else {
                var box = new StarReviewLabelBox(hit.X, hit.Y, hit.Width, hit.Height);
                list.Add(box);
                RecordEdit(run.RunId, Current.FocuserPosition, kind, box, wasAdd: true);
            }
            RefreshLabelMarkers();
        }

        /// <summary>
        /// Right-click delete: removes the smallest USER-LABEL box (missed / should-reject / wrongly-rejected)
        /// containing (<paramref name="imageX"/>,<paramref name="imageY"/>), across all three lists. Returns true
        /// if a label was removed (recorded as an undoable edit), false when the click was outside every label.
        /// The view calls this on a right-CLICK (a right-drag pans instead).
        /// </summary>
        public bool RemoveLabelAt(double imageX, double imageY) {
            var run = CurrentRunLabels;
            var pos = CurrentPositionLabels;
            if (run == null || pos == null) {
                return false;
            }

            LabelKind bestKind = default;
            List<StarReviewLabelBox> bestList = null;
            var bestIdx = -1;
            var bestArea = double.MaxValue;
            foreach (var (kind, list) in new[] {
                         (LabelKind.Missed, pos.Missed),
                         (LabelKind.ShouldReject, pos.ShouldReject),
                         (LabelKind.WronglyRejected, pos.WronglyRejected) }) {
                var idx = StarReviewLabelStore.SmallestContainingBoxIndex(list, imageX, imageY);
                if (idx < 0) {
                    continue;
                }
                var b = list[idx];
                var area = (b.W ?? 0.0) * (b.H ?? 0.0);
                if (area < bestArea) {
                    bestArea = area;
                    bestKind = kind;
                    bestList = list;
                    bestIdx = idx;
                }
            }

            if (bestList == null) {
                return false;
            }
            var removed = bestList[bestIdx];
            bestList.RemoveAt(bestIdx);
            RecordEdit(run.RunId, pos.FocuserPosition, bestKind, removed, wasAdd: false);
            RefreshLabelMarkers();
            return true;
        }

        /// <summary>
        /// Convenience query for the view: is ANY detector box (accepted or rejected) under (<paramref name="imageX"/>,
        /// <paramref name="imageY"/>)? Drives the click-vs-drag disambiguation on left-button-down — a box under the
        /// press is a click (toggle a label); blank space starts a missed rubber-band drag.
        /// </summary>
        public bool HitTestCandidate(double imageX, double imageY) {
            var (category, _) = HitTestCandidate(
                Current.Accepted.Select(a => a.Bounds), Current.Rejected.Select(r => r.Bounds), imageX, imageY);
            return category != HitCategory.None;
        }

        /// <summary>
        /// Pure combined hit-test + categorize: returns the SMALLEST-area detector box (in image coords) from EITHER
        /// <paramref name="accepted"/> or <paramref name="rejected"/> that contains the click, together with which set
        /// it came from. AABB containment; on overlap the smallest-area box wins regardless of set, and a tie in area
        /// resolves to the accepted box (the should-reject/false-positive case). Returns
        /// (<see cref="HitCategory.None"/>, default) when the click is outside every box. Pure geometry — unit-tested
        /// directly.
        /// </summary>
        public static (HitCategory Category, Rect Box) HitTestCandidate(
            IEnumerable<Rect> accepted, IEnumerable<Rect> rejected, double imageX, double imageY) {
            var category = HitCategory.None;
            Rect best = default;
            var bestArea = double.MaxValue;

            foreach (var b in accepted ?? Enumerable.Empty<Rect>()) {
                if (Contains(b, imageX, imageY)) {
                    var area = (double)b.Width * b.Height;
                    if (area < bestArea) {
                        bestArea = area;
                        best = b;
                        category = HitCategory.Accepted;
                    }
                }
            }
            foreach (var b in rejected ?? Enumerable.Empty<Rect>()) {
                if (Contains(b, imageX, imageY)) {
                    var area = (double)b.Width * b.Height;
                    // Strictly smaller so an equal-area accepted box (preferred above) wins the tie.
                    if (area < bestArea) {
                        bestArea = area;
                        best = b;
                        category = HitCategory.Rejected;
                    }
                }
            }
            return (category, best);
        }

        private static bool Contains(Rect b, double x, double y) =>
            x >= b.X && y >= b.Y && x <= b.X + b.Width && y <= b.Y + b.Height;

        private void RefreshLabelMarkers() {
            MissedMarkers.Clear();
            ShouldRejectMarkers.Clear();
            WronglyRejectedMarkers.Clear();
            var pos = CurrentPositionLabels;
            if (pos != null) {
                // Missed boxes were drawn where the detector found no candidate, so there is no measured HFR to show.
                foreach (var p in pos.Missed) {
                    MissedMarkers.Add(ToBoxMarker(p, "—"));
                }
                foreach (var p in pos.ShouldReject) {
                    ShouldRejectMarkers.Add(ToBoxMarker(p, null));
                }
                if (pos.WronglyRejected != null) {
                    // A wrongly-rejected label is the bbox of a rejected candidate; show that candidate's HFR.
                    foreach (var p in pos.WronglyRejected) {
                        WronglyRejectedMarkers.Add(ToBoxMarker(p, HfrTextForRejectedLabel(p)));
                    }
                }
            }
            RaisePropertyChanged(nameof(CountsLabel));
        }

        /// <summary>HFR text for a wrongly-rejected label: the HFR of the best-overlapping rejected candidate of the
        /// current frame (measured in the diagnostics detection pass), or "—" when none overlaps / HFR unavailable.</summary>
        private string HfrTextForRejectedLabel(StarReviewLabelBox b) {
            var records = Current.RejectedCandidates;
            if (records == null || records.Count == 0) {
                return "—";
            }
            var labelRect = new RectD(b.X, b.Y, b.W ?? 0.0, b.H ?? 0.0);
            double bestIoU = 0.0;
            double bestHfr = double.NaN;
            foreach (var r in records) {
                var iou = BoxMatcher.IoU(labelRect, r.Bounds);
                if (iou > bestIoU) {
                    bestIoU = iou;
                    bestHfr = r.Hfr;
                }
            }
            return bestIoU > 0.0 ? FormatHfr(bestHfr) : "—";
        }

        /// <summary>Maps a stored label box to a drawable marker (optionally carrying preformatted HFR text). A legacy
        /// label that somehow still lacks W/H is drawn as a small default box centered on its (x,y) (Normalize
        /// back-fills on load, so this is belt-and-suspenders).</summary>
        private static LabelBoxMarker ToBoxMarker(StarReviewLabelBox b, string hfrText) {
            if (b.HasSize) {
                return new LabelBoxMarker { X = b.X, Y = b.Y, Width = b.W.Value, Height = b.H.Value, HfrText = hfrText };
            }
            var side = 2.0 * StarReviewLabelStore.DefaultRadiusPx;
            return new LabelBoxMarker { X = b.X - side / 2.0, Y = b.Y - side / 2.0, Width = side, Height = side, HfrText = hfrText };
        }

        // ---- Undo / redo -----------------------------------------------------------------------------------

        private enum LabelKind { Missed, ShouldReject, WronglyRejected }

        /// <summary>One undoable label edit: a box added/removed, OR (when <see cref="IsModify"/>) a box moved/resized
        /// in a specific run/position/list.</summary>
        private sealed class LabelEdit {
            public string RunId;
            public int FocuserPosition;
            public LabelKind Kind;
            public StarReviewLabelBox Box;
            public bool WasAdd; // true: the edit ADDED Box (undo removes it); false: it REMOVED Box (undo re-adds).

            // Move/resize: when true, the edit changed Box's geometry (the Box stays in the list). Undo restores the
            // Old* geometry, redo restores the New*.
            public bool IsModify;
            public double OldX, OldY, OldW, OldH;
            public double NewX, NewY, NewW, NewH;
        }

        private readonly Stack<LabelEdit> undoStack = new();
        private readonly Stack<LabelEdit> redoStack = new();

        /// <summary>Records a label mutation for undo and clears the redo stack (a new edit forks the history).</summary>
        private void RecordEdit(string runId, int focuserPosition, LabelKind kind, StarReviewLabelBox box, bool wasAdd) {
            undoStack.Push(new LabelEdit { RunId = runId, FocuserPosition = focuserPosition, Kind = kind, Box = box, WasAdd = wasAdd });
            redoStack.Clear();
            RaiseUndoRedo();
        }

        private void Undo() {
            if (undoStack.Count == 0) {
                return;
            }
            var edit = undoStack.Pop();
            ApplyEdit(edit, forward: false);
            redoStack.Push(edit);
            NavigateTo(edit.RunId, edit.FocuserPosition);
            RefreshLabelMarkers();
            RaiseUndoRedo();
        }

        private void Redo() {
            if (redoStack.Count == 0) {
                return;
            }
            var edit = redoStack.Pop();
            ApplyEdit(edit, forward: true);
            undoStack.Push(edit);
            NavigateTo(edit.RunId, edit.FocuserPosition);
            RefreshLabelMarkers();
            RaiseUndoRedo();
        }

        /// <summary>Applies an edit in the forward (redo / original) or inverse (undo) direction to the in-memory
        /// label model. Add/remove are matched by box-center tolerance so a re-applied add doesn't duplicate.</summary>
        private void ApplyEdit(LabelEdit e, bool forward) {
            if (!labelsByRun.TryGetValue(e.RunId, out var run) || run == null) {
                return;
            }
            var pos = StarReviewLabelStore.GetOrAddPosition(run, e.FocuserPosition);
            var list = ListFor(pos, e.Kind);

            if (e.IsModify) {
                // The Box reference persists in the list across a move/resize; set its geometry to the target. If the
                // reference is somehow gone, match by the box's CURRENT (pre-this-step) center.
                var box = list.Contains(e.Box) ? e.Box : null;
                if (box == null) {
                    var (curCx, curCy) = forward
                        ? (e.OldX + e.OldW / 2.0, e.OldY + e.OldH / 2.0)
                        : (e.NewX + e.NewW / 2.0, e.NewY + e.NewH / 2.0);
                    var hit = StarReviewLabelStore.NearestBoxIndexWithin(list, curCx, curCy, StarReviewLabelStore.SamePointTolerancePx);
                    box = hit >= 0 ? list[hit] : null;
                }
                if (box != null) {
                    box.X = forward ? e.NewX : e.OldX;
                    box.Y = forward ? e.NewY : e.OldY;
                    box.W = forward ? e.NewW : e.OldW;
                    box.H = forward ? e.NewH : e.OldH;
                }
                return;
            }

            var doAdd = e.WasAdd ? forward : !forward;
            if (doAdd) {
                if (StarReviewLabelStore.NearestBoxIndexWithin(list, e.Box.CenterX, e.Box.CenterY, StarReviewLabelStore.SamePointTolerancePx) < 0) {
                    list.Add(e.Box);
                }
            } else {
                var idx = StarReviewLabelStore.NearestBoxIndexWithin(list, e.Box.CenterX, e.Box.CenterY, StarReviewLabelStore.SamePointTolerancePx);
                if (idx >= 0) {
                    list.RemoveAt(idx);
                }
            }
        }

        private static List<StarReviewLabelBox> ListFor(StarReviewPositionLabels pos, LabelKind kind) => kind switch {
            LabelKind.Missed => pos.Missed,
            LabelKind.ShouldReject => pos.ShouldReject,
            _ => pos.WronglyRejected
        };

        /// <summary>Moves the current frame to the one matching (runId, focuserPosition) so an undone/redone edit is
        /// visible. No-op if it is already current or not in the queue.</summary>
        private void NavigateTo(string runId, int focuserPosition) {
            if (Current.RunId == runId && Current.FocuserPosition == focuserPosition) {
                return;
            }
            for (var i = 0; i < queue.Count; i++) {
                if (queue[i].RunId == runId && queue[i].FocuserPosition == focuserPosition) {
                    CurrentIndex = i;
                    // Preserve the viewport when jumping to show an undone/redone edit (consistent with Next/Prev).
                    LoadCurrent(fitView: false);
                    return;
                }
            }
        }

        private void RaiseUndoRedo() {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }

        // ---- Persistence -----------------------------------------------------------------------------------

        /// <summary>Saves every run's labels (save-on-close / final flush from the wizard or the TestApp host).</summary>
        public void SaveAll() {
            foreach (var run in labelsByRun.Values) {
                try {
                    StarReviewLabelStore.PruneEmptyPositions(run);
                    StarReviewLabelStore.Save(labelsDir, run);
                } catch (Exception ex) {
                    Logger.Error(ex, $"Failed to save labels for run '{run.RunId}'");
                }
            }
            RaisePropertyChanged(nameof(CountsLabel));
        }
    }
}
