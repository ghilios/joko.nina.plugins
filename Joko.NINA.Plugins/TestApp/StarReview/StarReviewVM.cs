#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Logger = NINA.Core.Utility.Logger;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using Rect = OpenCvSharp.Rect;
using Window = System.Windows.Window;

namespace TestApp.StarReview {

    /// <summary>
    /// The pre-computed detection result + paths for one reviewable frame. Accepted carry the detector's actual
    /// <c>StarBoundingBox</c> (so the overlay draws real-size boxes) + HFR + center; Rejected carries the per-reason
    /// bounding boxes so the reviewer sees what the detector did.
    /// </summary>
    internal sealed class FrameReview {
        public string RunId { get; set; }
        public int FocuserPosition { get; set; }
        public string FramePath { get; set; }

        /// <summary>Accepted stars: the detector's real bounding box + its center (used for click hit-tests, the
        /// should-reject label box, and the overlay) + HFR.</summary>
        public List<(double CX, double CY, double HFR, Rect Bounds)> Accepted { get; set; } = new();
        public List<(string Reason, Rect Bounds)> Rejected { get; set; } = new();
    }

    /// <summary>A drawable accepted-star marker (the detector's real bounding box) in image-pixel coords.</summary>
    public sealed class AcceptedMarker {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double HFR { get; set; }
    }

    /// <summary>A drawable user-label box in image-pixel coords (top-left X,Y + W,H).</summary>
    public sealed class LabelBoxMarker {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// <summary>A drawable rejected-candidate box in image-pixel coords, colored by reason.</summary>
    public sealed class RejectedMarker {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Reason { get; set; }
        public Color Color { get; set; }
    }

    public enum LabelPass {
        Missed,
        ShouldReject,
        WronglyRejected
    }

    /// <summary>
    /// ViewModel for the interactive review window. Holds the queue of frames, the per-run labels, the zoom/pan
    /// viewport, and the current labeling pass. All detection was done up front by <see cref="StarReviewRunner"/>;
    /// this VM only renders overlays and edits/persists labels. The screen↔image pixel mapping is delegated to
    /// the unit-tested <see cref="StarReviewViewport"/>; the label add/remove/merge logic to the unit-tested
    /// <see cref="StarReviewLabelStore"/>.
    /// </summary>
    public class StarReviewVM : BaseINPC {
        private readonly IReadOnlyList<FrameReview> queue;
        private readonly Dictionary<string, StarReviewRunLabels> labelsByRun;
        private readonly string labelsDir;
        private readonly IProfileService profileService;

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

        private Window window;

        internal StarReviewVM(
            IReadOnlyList<FrameReview> queue,
            Dictionary<string, StarReviewRunLabels> labelsByRun,
            string labelsDir,
            IProfileService profileService) {
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.labelsByRun = labelsByRun ?? throw new ArgumentNullException(nameof(labelsByRun));
            this.labelsDir = labelsDir ?? throw new ArgumentNullException(nameof(labelsDir));
            this.profileService = profileService;

            Viewport = new StarReviewViewport();

            NextCommand = new RelayCommand(Next, () => CurrentIndex < queue.Count - 1);
            PrevCommand = new RelayCommand(Prev, () => CurrentIndex > 0);
            SaveCommand = new RelayCommand(SaveAll);
            MarkMissedCommand = new RelayCommand(() => Pass = LabelPass.Missed);
            MarkShouldRejectCommand = new RelayCommand(() => Pass = LabelPass.ShouldReject);
            MarkWronglyRejectedCommand = new RelayCommand(() => Pass = LabelPass.WronglyRejected);
            FitCommand = new RelayCommand(RequestFit);

            CurrentIndex = 0;
            LoadCurrent();
        }

        internal void AttachWindow(Window w) {
            window = w;
            if (window != null) {
                window.Closing += (_, __) => SaveAll();
            }
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

        /// <summary>Raises the zoom-dependent marker-thickness bindings. The view calls this after any viewport change
        /// (wheel-zoom, fit, pan) so the stroke widths track the current scale.</summary>
        public void NotifyViewportChanged() {
            RaisePropertyChanged(nameof(MarkerStrokeThickness));
            RaisePropertyChanged(nameof(LabelStrokeThickness));
        }

        // Markers in IMAGE pixel coords; the view applies the viewport transform to place them.
        public ObservableCollection<AcceptedMarker> AcceptedMarkers { get; } = new();
        public ObservableCollection<RejectedMarker> RejectedMarkers { get; } = new();
        public ObservableCollection<LabelBoxMarker> MissedMarkers { get; } = new();
        public ObservableCollection<LabelBoxMarker> ShouldRejectMarkers { get; } = new();
        public ObservableCollection<LabelBoxMarker> WronglyRejectedMarkers { get; } = new();

        private LabelPass pass = LabelPass.Missed;
        public LabelPass Pass {
            get => pass;
            set {
                if (pass != value) {
                    pass = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsMissedPass));
                    RaisePropertyChanged(nameof(IsShouldRejectPass));
                    RaisePropertyChanged(nameof(IsWronglyRejectedPass));
                    RaisePropertyChanged(nameof(PassLabel));
                }
            }
        }

        public bool IsMissedPass => Pass == LabelPass.Missed;
        public bool IsShouldRejectPass => Pass == LabelPass.ShouldReject;
        public bool IsWronglyRejectedPass => Pass == LabelPass.WronglyRejected;
        public string PassLabel {
            get {
                switch (Pass) {
                    case LabelPass.Missed: return "Marking: MISSED — drag a box around a star the detector missed (false negatives)";
                    case LabelPass.ShouldReject: return "Marking: SHOULD-REJECT — click an ACCEPTED (green) box to flag it (false positives)";
                    case LabelPass.WronglyRejected: return "Marking: WRONGLY-REJECTED — click a REJECTED box to keep it (folds into recall)";
                    default: return "Marking";
                }
            }
        }

        private double radiusPx = StarReviewLabelStore.DefaultRadiusPx;
        public double RadiusPx {
            get => radiusPx;
            set {
                if (Math.Abs(radiusPx - value) > 1e-9 && value > 0) {
                    radiusPx = value;
                    RaisePropertyChanged();
                    PersistRadiusToCurrentPosition();
                }
            }
        }

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
        public RelayCommand MarkMissedCommand { get; }
        public RelayCommand MarkShouldRejectCommand { get; }
        public RelayCommand MarkWronglyRejectedCommand { get; }
        public RelayCommand FitCommand { get; }

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

        private void Next() {
            if (CurrentIndex < queue.Count - 1) {
                SaveCurrentRun();
                CurrentIndex++;
                LoadCurrent();
            }
        }

        private void Prev() {
            if (CurrentIndex > 0) {
                SaveCurrentRun();
                CurrentIndex--;
                LoadCurrent();
            }
        }

        private void RequestFit() => FitRequested?.Invoke(this, EventArgs.Empty);

        private void LoadCurrent() {
            var f = Current;
            FrameHeader = $"{f.RunId}  @ focuser {f.FocuserPosition}  |  {f.Accepted.Count} accepted";

            // Build the MTF-stretched background image (off-thread, then marshal to the UI).
            FrameImage = null;
            _ = LoadImageAsync(f.FramePath);

            // Overlays in image coords — accepted stars draw the detector's REAL bounding box (top-left + size).
            AcceptedMarkers.Clear();
            foreach (var (_, _, hfr, b) in f.Accepted) {
                AcceptedMarkers.Add(new AcceptedMarker { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, HFR = hfr });
            }
            RejectedMarkers.Clear();
            foreach (var (reason, b) in f.Rejected) {
                var color = ReasonColors.TryGetValue(reason, out var c) ? c : Colors.Gray;
                RejectedMarkers.Add(new RejectedMarker {
                    X = b.X, Y = b.Y, Width = b.Width, Height = b.Height, Reason = reason, Color = color
                });
            }

            // The radius shown defaults to the run default unless this position already overrode it.
            var run = CurrentRunLabels;
            var pos = CurrentPositionLabels;
            radiusPx = pos?.RadiusPx ?? run?.RadiusPx ?? StarReviewLabelStore.DefaultRadiusPx;
            RaisePropertyChanged(nameof(RadiusPx));

            RefreshLabelMarkers();

            NextCommand.NotifyCanExecuteChanged();
            PrevCommand.NotifyCanExecuteChanged();
        }

        private async Task LoadImageAsync(string path) {
            try {
                var bmp = await Task.Run(() => BuildStretchedBitmap(path)).ConfigureAwait(true);
                FrameImage = bmp;
                RequestFit();
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to load/stretch frame {path}");
                Console.Error.WriteLine($"Failed to load frame {path}: {ex.Message}");
            }
        }

        private BitmapSource BuildStretchedBitmap(string path) {
            using var srcFloat = DiagnosticUtil.LoadFloatMat(path, profileService).GetAwaiter().GetResult();
            using var src16 = new Mat();
            srcFloat.ConvertTo(src16, MatType.CV_16U, ushort.MaxValue);
            using var stretched16 = new Mat();
            try {
                var stats = CvImageUtility.CalculateStatistics_Histogram(src16);
                using var lut = CvImageUtility.CreateMTFLookup(stats);
                CvImageUtility.ApplyLUT(src16, lut, stretched16);
            } catch (Exception ex) {
                Logger.Warning($"MTF stretch failed ({ex.Message}); falling back to linear normalization");
                Cv2.Normalize(src16, stretched16, 0, ushort.MaxValue, NormTypes.MinMax);
            }
            var bmp = Program.ToBitmapSource(stretched16, PixelFormats.Gray16);
            return bmp;
        }

        // ---- Labeling (delegates to the pure store) --------------------------------------------------------

        /// <summary>
        /// MISSED pass: adds (or toggles off) a user-drawn box. The view calls this on mouse-up of a rubber-band
        /// drag (or a small click). (<paramref name="imageX"/>,<paramref name="imageY"/>) is the top-left and
        /// (<paramref name="width"/>,<paramref name="height"/>) the size, already normalized to W,H ≥ 0 by the view.
        /// A degenerate drag (below a few px — effectively a click) is widened to a small default box of side
        /// 2·RadiusPx centered on the click, so a plain click still drops a usable region. Toggling near an existing
        /// missed box's center removes it.
        /// </summary>
        public void AddMissedBox(double imageX, double imageY, double width, double height) {
            var run = CurrentRunLabels;
            if (run == null) {
                return;
            }
            const double minDragPx = 3.0;
            if (width < minDragPx || height < minDragPx) {
                // Effectively a click: drop a small default box centered on the click point.
                var side = 2.0 * RadiusPx;
                var cx = imageX + width / 2.0;
                var cy = imageY + height / 2.0;
                imageX = cx - side / 2.0;
                imageY = cy - side / 2.0;
                width = side;
                height = side;
            }
            var pos = StarReviewLabelStore.GetOrAddPosition(run, Current.FocuserPosition);
            if (pos.RadiusPx == null) {
                pos.RadiusPx = RadiusPx;
            }
            StarReviewLabelStore.ToggleBox(pos.Missed, imageX, imageY, width, height);
            RefreshLabelMarkers();
        }

        /// <summary>
        /// SHOULD-REJECT / WRONGLY-REJECTED pass: a click that hit-tests the detector's ACCEPTED (should-reject) or
        /// REJECTED (wrongly-rejected) boxes and toggles THAT star's actual bounding box into the label list. The
        /// view calls this after mapping the click through <see cref="StarReviewViewport.ScreenToImage"/>. A click
        /// outside every candidate box is a no-op (the MISSED pass is handled by <see cref="AddMissedBox"/> instead).
        /// On overlap the smallest-area box wins. Clicking an already-labeled star again removes it.
        /// </summary>
        public void ToggleLabelAt(double imageX, double imageY) {
            var run = CurrentRunLabels;
            if (run == null || Pass == LabelPass.Missed) {
                return;
            }

            // Hit-test the relevant detector boxes (accepted for should-reject, rejected for wrongly-rejected).
            var hit = Pass == LabelPass.ShouldReject
                ? HitTestBoxes(Current.Accepted.Select(a => a.Bounds), imageX, imageY)
                : HitTestBoxes(Current.Rejected.Select(r => r.Bounds), imageX, imageY);
            if (hit == null) {
                return; // click outside every candidate box: no-op
            }

            var pos = StarReviewLabelStore.GetOrAddPosition(run, Current.FocuserPosition);
            if (pos.RadiusPx == null) {
                pos.RadiusPx = RadiusPx;
            }
            var list = Pass == LabelPass.ShouldReject ? pos.ShouldReject : pos.WronglyRejected;

            // Toggle semantics: if a label already covers this star (its center matches the hit box's center within
            // tolerance), remove it; otherwise record the star's ACTUAL bounding box.
            var b = hit.Value;
            var existing = StarReviewLabelStore.NearestBoxIndexWithin(
                list, b.X + b.Width / 2.0, b.Y + b.Height / 2.0, StarReviewLabelStore.SamePointTolerancePx);
            if (existing >= 0) {
                list.RemoveAt(existing);
            } else {
                list.Add(new StarReviewLabelBox(b.X, b.Y, b.Width, b.Height));
            }
            RefreshLabelMarkers();
        }

        /// <summary>
        /// Returns the SMALLEST-area box (in image coords) from <paramref name="boxes"/> that contains the click, or
        /// null if the click is outside every box. AABB containment; on overlap the smallest-area box wins (so a
        /// click in a region of nested boxes selects the tightest candidate). Pure geometry.
        /// </summary>
        private static Rect? HitTestBoxes(IEnumerable<Rect> boxes, double imageX, double imageY) {
            Rect? best = null;
            var bestArea = double.MaxValue;
            foreach (var b in boxes) {
                if (imageX < b.X || imageY < b.Y || imageX > b.X + b.Width || imageY > b.Y + b.Height) {
                    continue;
                }
                var area = (double)b.Width * b.Height;
                if (area < bestArea) {
                    bestArea = area;
                    best = b;
                }
            }
            return best;
        }

        private void RefreshLabelMarkers() {
            MissedMarkers.Clear();
            ShouldRejectMarkers.Clear();
            WronglyRejectedMarkers.Clear();
            var pos = CurrentPositionLabels;
            if (pos != null) {
                foreach (var p in pos.Missed) {
                    MissedMarkers.Add(ToBoxMarker(p));
                }
                foreach (var p in pos.ShouldReject) {
                    ShouldRejectMarkers.Add(ToBoxMarker(p));
                }
                if (pos.WronglyRejected != null) {
                    foreach (var p in pos.WronglyRejected) {
                        WronglyRejectedMarkers.Add(ToBoxMarker(p));
                    }
                }
            }
            RaisePropertyChanged(nameof(CountsLabel));
        }

        /// <summary>Maps a stored label box to a drawable marker. A legacy label that somehow still lacks W/H is drawn
        /// as a small 2·RadiusPx box centered on its (x,y) (Normalize back-fills on load, so this is belt-and-suspenders).</summary>
        private LabelBoxMarker ToBoxMarker(StarReviewLabelBox b) {
            if (b.HasSize) {
                return new LabelBoxMarker { X = b.X, Y = b.Y, Width = b.W.Value, Height = b.H.Value };
            }
            var side = 2.0 * RadiusPx;
            return new LabelBoxMarker { X = b.X - side / 2.0, Y = b.Y - side / 2.0, Width = side, Height = side };
        }

        private void PersistRadiusToCurrentPosition() {
            var run = CurrentRunLabels;
            if (run == null) {
                return;
            }
            var pos = StarReviewLabelStore.GetOrAddPosition(run, Current.FocuserPosition);
            pos.RadiusPx = RadiusPx;
        }

        // ---- Persistence -----------------------------------------------------------------------------------

        private void SaveCurrentRun() {
            var run = CurrentRunLabels;
            if (run == null) {
                return;
            }
            try {
                StarReviewLabelStore.PruneEmptyPositions(run);
                var path = StarReviewLabelStore.Save(labelsDir, run);
                Logger.Info($"Saved labels for run '{run.RunId}' to {path}");
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to save labels for run '{run.RunId}'");
                Console.Error.WriteLine($"Failed to save labels for run '{run.RunId}': {ex.Message}");
            }
        }

        /// <summary>Saves every run's labels (save-on-close / Save button / final flush from the runner).</summary>
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
