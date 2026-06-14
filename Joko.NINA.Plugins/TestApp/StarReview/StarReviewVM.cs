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
    /// The pre-computed detection result + paths for one reviewable frame. AcceptedCenters carry HFR for the
    /// overlay label; Rejected carries the per-reason bounding boxes so the reviewer sees what the detector did.
    /// </summary>
    internal sealed class FrameReview {
        public string RunId { get; set; }
        public int FocuserPosition { get; set; }
        public string FramePath { get; set; }
        public List<(double X, double Y, double HFR)> AcceptedCenters { get; set; } = new();
        public List<(string Reason, Rect Bounds)> Rejected { get; set; } = new();
    }

    /// <summary>A drawable accepted-star marker in image-pixel coords.</summary>
    public sealed class AcceptedMarker {
        public double X { get; set; }
        public double Y { get; set; }
        public double HFR { get; set; }
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
        ShouldReject
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

        // Markers in IMAGE pixel coords; the view applies the viewport transform to place them.
        public ObservableCollection<AcceptedMarker> AcceptedMarkers { get; } = new();
        public ObservableCollection<RejectedMarker> RejectedMarkers { get; } = new();
        public ObservableCollection<StarReviewLabelPoint> MissedMarkers { get; } = new();
        public ObservableCollection<StarReviewLabelPoint> ShouldRejectMarkers { get; } = new();

        private LabelPass pass = LabelPass.Missed;
        public LabelPass Pass {
            get => pass;
            set {
                if (pass != value) {
                    pass = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsMissedPass));
                    RaisePropertyChanged(nameof(IsShouldRejectPass));
                    RaisePropertyChanged(nameof(PassLabel));
                }
            }
        }

        public bool IsMissedPass => Pass == LabelPass.Missed;
        public bool IsShouldRejectPass => Pass == LabelPass.ShouldReject;
        public string PassLabel => Pass == LabelPass.Missed ? "Marking: MISSED (false negatives)" : "Marking: SHOULD-REJECT (false positives)";

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
                var (missed, reject) = StarReviewLabelStore.Counts(labels);
                var pos = CurrentPositionLabels;
                var posMissed = pos?.Missed?.Count ?? 0;
                var posReject = pos?.ShouldReject?.Count ?? 0;
                return $"this frame: {posMissed} missed, {posReject} should-reject | run total: {missed} missed, {reject} should-reject";
            }
        }

        public RelayCommand NextCommand { get; }
        public RelayCommand PrevCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand MarkMissedCommand { get; }
        public RelayCommand MarkShouldRejectCommand { get; }
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
            FrameHeader = $"{f.RunId}  @ focuser {f.FocuserPosition}  |  {f.AcceptedCenters.Count} accepted";

            // Build the MTF-stretched background image (off-thread, then marshal to the UI).
            FrameImage = null;
            _ = LoadImageAsync(f.FramePath);

            // Overlays in image coords.
            AcceptedMarkers.Clear();
            foreach (var (x, y, hfr) in f.AcceptedCenters) {
                AcceptedMarkers.Add(new AcceptedMarker { X = x, Y = y, HFR = hfr });
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
        /// Adds-or-removes a label of the current pass at image pixel (imageX, imageY). Called by the view after
        /// it maps the click through <see cref="StarReviewViewport.ScreenToImage"/>. Toggling near an existing
        /// point of the same pass removes it.
        /// </summary>
        public void ToggleLabelAt(double imageX, double imageY) {
            var run = CurrentRunLabels;
            if (run == null) {
                return;
            }
            var pos = StarReviewLabelStore.GetOrAddPosition(run, Current.FocuserPosition);
            if (pos.RadiusPx == null) {
                pos.RadiusPx = RadiusPx;
            }
            var list = Pass == LabelPass.Missed ? pos.Missed : pos.ShouldReject;
            StarReviewLabelStore.TogglePoint(list, imageX, imageY);
            RefreshLabelMarkers();
        }

        private void RefreshLabelMarkers() {
            MissedMarkers.Clear();
            ShouldRejectMarkers.Clear();
            var pos = CurrentPositionLabels;
            if (pos != null) {
                foreach (var p in pos.Missed) {
                    MissedMarkers.Add(p);
                }
                foreach (var p in pos.ShouldReject) {
                    ShouldRejectMarkers.Add(p);
                }
            }
            RaisePropertyChanged(nameof(CountsLabel));
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
