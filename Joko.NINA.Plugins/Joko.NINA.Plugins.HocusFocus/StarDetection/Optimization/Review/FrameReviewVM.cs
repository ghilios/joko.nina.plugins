#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>One drawable accepted-star marker for the Frame Review overlay, in the frame's RAW image coords. The
    /// box (<see cref="BoxX"/>/<see cref="BoxY"/> top-left + size) is drawn in <see cref="BoxBrush"/> (green/red/grey by
    /// registration state). The cyan registration marker sits at the raw centroid (<see cref="CenterX"/>/<see
    /// cref="CenterY"/>); the orange registration line runs to the aligned target (<see cref="TargetX"/>/<see
    /// cref="TargetY"/>). Label/line/offset visibility is gated by the VM toggles at the layer level, and per-marker by
    /// <see cref="HasRegistrationId"/>/<see cref="HasRegistrationLine"/>/<see cref="HasFocusOffset"/>.</summary>
    public sealed class FrameReviewMarker {
        public double BoxX { get; init; }
        public double BoxY { get; init; }
        public double BoxWidth { get; init; }
        public double BoxHeight { get; init; }
        public Brush BoxBrush { get; init; }

        public string HfrText { get; init; }

        public double CenterX { get; init; }
        public double CenterY { get; init; }
        public double TargetX { get; init; }
        public double TargetY { get; init; }

        public bool HasRegistrationId { get; init; }
        public string RegistrationIdText { get; init; }
        public bool HasRegistrationLine { get; init; }

        public string FocusOffsetText { get; init; }
        public bool HasFocusOffset { get; init; }

        /// <summary>Registration id (for the hover focus-graph lookup); null when unmatched.</summary>
        public int? RegistrationId { get; init; }

        /// <summary>True when this star has an accepted focus fit, so hovering it can show a focus graph.</summary>
        public bool CanShowFocusGraph { get; init; }
    }

    /// <summary>A fitted focus graph for a registered star WITH an accepted hyperbolic fit: the cross-frame scatter
    /// plus the fitted curve and its best-focus minimum. Rendered with plain markers (no error bars — each point is a
    /// single detection, so there is no sample spread to draw).</summary>
    public sealed class FrameReviewFocusGraph {
        public string Label { get; init; }
        public IReadOnlyList<ScatterErrorPoint> Points { get; init; }
        public AlglibHyperbolicFitting Fit { get; init; }
        public Func<double, double> Fitting => Fit?.Fitting;
        public DataPoint Minimum => Fit?.Minimum ?? default;
    }

    /// <summary>A points-only focus graph for a registered star that has NO accepted hyperbolic fit: just the
    /// cross-frame (focuser position, HFR) scatter, so the user can see why the fit failed (no curve/minimum).</summary>
    public sealed class FrameReviewScatterGraph {
        public string Label { get; init; }
        public IReadOnlyList<ScatterErrorPoint> Points { get; init; }
    }

    /// <summary>
    /// Read-only viewer VM for the Aberration Inspector "Review Frames" dialog. Steps through the captured frames,
    /// overlaying each accepted star's bounding box (colored by registration/fit state), HFR, optional registration
    /// id + path, and optional best-focus offset; and shows a per-star focus graph on hover. Reuses the unit-tested
    /// <see cref="StarReviewViewport"/> zoom/pan math and the <see cref="StarReviewLegendEntry"/> legend-row type;
    /// the hover graph uses <see cref="FrameReviewFocusGraph"/> / <see cref="FrameReviewScatterGraph"/> OxyPlot charts.
    /// Disposing releases the retained bitmaps.
    /// </summary>
    public sealed class FrameReviewVM : BaseINPC, IDisposable {

        private static SolidColorBrush FrozenBrush(Color c) {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>Accepted star WITH an accepted focus fit (green) — contributed to the model.</summary>
        public Brush AcceptedBrush { get; } = FrozenBrush(Color.FromRgb(0x00, 0xFF, 0x00));

        /// <summary>Registered star with NO successful focus fit (red).</summary>
        public Brush RegisteredNoFitBrush { get; } = FrozenBrush(Color.FromRgb(0xFF, 0x52, 0x52));

        /// <summary>Accepted but unregistered star (grey).</summary>
        public Brush UnregisteredBrush { get; } = FrozenBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

        /// <summary>Registration id label + center marker (cyan).</summary>
        public Brush RegistrationBrush { get; } = FrozenBrush(Color.FromRgb(0x00, 0xE5, 0xFF));

        /// <summary>HFR label (yellow).</summary>
        public Brush HfrBrush { get; } = FrozenBrush(Color.FromRgb(0xFF, 0xD7, 0x00));

        /// <summary>Registration path line + target marker (orange).</summary>
        public Brush RegistrationTargetBrush { get; } = FrozenBrush(Color.FromRgb(0xFF, 0x8C, 0x00));

        /// <summary>Best-focus offset label (violet).</summary>
        public Brush FocusOffsetBrush { get; } = FrozenBrush(Color.FromRgb(0xE0, 0x40, 0xFB));

        private FrameReviewSnapshot snapshot;

        public event EventHandler RequestClose;
        public event EventHandler FitRequested;

        public StarReviewViewport Viewport { get; }
        public ObservableCollection<FrameReviewMarker> Markers { get; } = new();

        public RelayCommand PrevCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand FitCommand { get; }
        public RelayCommand CloseCommand { get; }

        public FrameReviewVM(FrameReviewSnapshot snapshot) {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Viewport = new StarReviewViewport();
            legendEntries = BuildLegend();

            PrevCommand = new RelayCommand(Prev, () => CurrentIndex > 0);
            NextCommand = new RelayCommand(Next, () => CurrentIndex < FrameCount - 1);
            FitCommand = new RelayCommand(() => FitRequested?.Invoke(this, EventArgs.Empty));
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));

            CurrentIndex = 0;
            // No fit here: FitRequested has zero subscribers at construction time (the control subscribes in
            // OnDataContextChanged, after the ctor returns). The initial fit is driven by the control's first
            // layout (Loaded/SizeChanged). Passing fit:false makes that contract explicit.
            LoadCurrent(fit: false);
        }

        private int FrameCount => snapshot?.Frames.Count ?? 0;

        // ---- toggles --------------------------------------------------------------------------------------

        private bool showRegistration;
        /// <summary>Show registration ids + path (cyan center marker + orange line/target). Off by default.</summary>
        public bool ShowRegistration {
            get => showRegistration;
            set {
                if (showRegistration != value) {
                    showRegistration = value;
                    RaisePropertyChanged();
                    RebuildLegend();
                }
            }
        }

        private bool showFocusOffset;
        /// <summary>Show each fitted star's best-focus offset from the field mean. Off by default.</summary>
        public bool ShowFocusOffset {
            get => showFocusOffset;
            set {
                if (showFocusOffset != value) {
                    showFocusOffset = value;
                    RaisePropertyChanged();
                    RebuildLegend();
                }
            }
        }

        // ---- navigation / current frame -------------------------------------------------------------------

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

        public string PositionLabel => FrameCount > 0 ? $"{CurrentIndex + 1} / {FrameCount}" : "0 / 0";

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

        private string frameHeader;
        public string FrameHeader {
            get => frameHeader;
            private set { frameHeader = value; RaisePropertyChanged(); }
        }

        private string detectedCountText;
        public string DetectedCountText {
            get => detectedCountText;
            private set { detectedCountText = value; RaisePropertyChanged(); }
        }

        private string referenceNote;
        public string ReferenceNote {
            get => referenceNote;
            private set { referenceNote = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowReferenceNote)); }
        }

        public bool ShowReferenceNote => !string.IsNullOrEmpty(ReferenceNote);

        private string transformText;
        public string TransformText {
            get => transformText;
            private set { transformText = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowTransform)); }
        }

        public bool ShowTransform => !string.IsNullOrEmpty(TransformText);

        // ---- inverse-zoom overlay bindings ----------------------------------------------------------------

        public double MarkerStrokeThickness => 1.5 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);
        public double MarkerTextScale => 1.0 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);
        public double HfrLabelOffset => -15.0 * MarkerTextScale;
        public double RegistrationLabelOffset => 3.0 * MarkerTextScale;

        public void NotifyViewportChanged() {
            RaisePropertyChanged(nameof(MarkerStrokeThickness));
            RaisePropertyChanged(nameof(MarkerTextScale));
            RaisePropertyChanged(nameof(HfrLabelOffset));
            RaisePropertyChanged(nameof(RegistrationLabelOffset));
        }

        // ---- hover focus graph ----------------------------------------------------------------------------

        // Either a FrameReviewFocusGraph (fitted: points + curve + minimum) or a FrameReviewScatterGraph (no fit:
        // cross-frame points only). The hover overlay's ContentControl resolves the right DataTemplate by type.
        private object hoverContent;
        public object HoverContent {
            get => hoverContent;
            private set { hoverContent = value; RaisePropertyChanged(); }
        }

        private bool showHover;
        public bool ShowHover {
            get => showHover;
            private set { if (showHover != value) { showHover = value; RaisePropertyChanged(); } }
        }

        private string hoverHeaderText;
        public string HoverHeaderText {
            get => hoverHeaderText;
            private set { hoverHeaderText = value; RaisePropertyChanged(); }
        }

        private string hoverRSquaredText;
        public string HoverRSquaredText {
            get => hoverRSquaredText;
            private set { hoverRSquaredText = value; RaisePropertyChanged(); }
        }

        private string hoverOptimalFocusText;
        public string HoverOptimalFocusText {
            get => hoverOptimalFocusText;
            private set { hoverOptimalFocusText = value; RaisePropertyChanged(); }
        }

        private int? hoverRegistrationId;

        /// <summary>Show the focus graph for the registered star with the given id (no-op if already shown / no curve).
        /// Fitted stars show the curve + R² + best focus; registered-but-unfitted stars show their cross-frame
        /// points only (so the user can see why the fit failed).</summary>
        public void SetHover(int registrationId) {
            if (hoverRegistrationId == registrationId && ShowHover) {
                return;
            }
            if (snapshot?.FocusCurvesByRegistrationId == null ||
                !snapshot.FocusCurvesByRegistrationId.TryGetValue(registrationId, out var curve)) {
                ClearHover();
                return;
            }
            hoverRegistrationId = registrationId;
            HoverHeaderText = $"Star {registrationId}";
            if (curve.Fit != null) {
                HoverContent = new FrameReviewFocusGraph {
                    Label = $"Star {registrationId}",
                    Points = curve.Points,
                    Fit = curve.Fit,
                };
                HoverRSquaredText = $"R²: {curve.RSquared:0.###}";
                var offset = curve.OffsetFromMean ?? 0.0;
                HoverOptimalFocusText = $"Best focus: {curve.BestFocus:0}  (Δ field {offset:+0.#;-0.#;0})";
            } else {
                HoverContent = new FrameReviewScatterGraph {
                    Label = $"Star {registrationId}",
                    Points = curve.Points,
                };
                HoverRSquaredText = "No accepted focus fit";
                HoverOptimalFocusText = $"{curve.Points.Count} detection(s) across frames";
            }
            ShowHover = true;
        }

        public void ClearHover() {
            hoverRegistrationId = null;
            ShowHover = false;
            HoverContent = null;
        }

        // ---- legend ---------------------------------------------------------------------------------------

        private IReadOnlyList<StarReviewLegendEntry> legendEntries;
        public IReadOnlyList<StarReviewLegendEntry> LegendEntries {
            get => legendEntries;
            private set { legendEntries = value; RaisePropertyChanged(); }
        }

        private void RebuildLegend() {
            LegendEntries = BuildLegend();
        }

        private IReadOnlyList<StarReviewLegendEntry> BuildLegend() {
            return new List<StarReviewLegendEntry> {
                new() { Brush = AcceptedBrush, Caption = "Accepted (focus fit)", Dashed = false },
                new() { Brush = RegisteredNoFitBrush, Caption = "Registered, no focus fit", Dashed = false },
                new() { Brush = UnregisteredBrush, Caption = "Not registered", Dashed = false },
                new() { Brush = HfrBrush, Caption = "HFR", Dashed = false },
                new() { Brush = RegistrationBrush, Caption = "Registration ID & marker", Dashed = false, Enabled = ShowRegistration },
                new() { Brush = RegistrationTargetBrush, Caption = "Registration target (aligned)", Dashed = false, Enabled = ShowRegistration },
                new() { Brush = FocusOffsetBrush, Caption = "Best-focus offset from field mean", Dashed = false, Enabled = ShowFocusOffset },
            };
        }

        // ---- frame loading --------------------------------------------------------------------------------

        private void Prev() {
            if (CurrentIndex > 0) {
                CurrentIndex--;
                LoadCurrent(fit: false);
            }
        }

        private void Next() {
            if (CurrentIndex < FrameCount - 1) {
                CurrentIndex++;
                LoadCurrent(fit: false);
            }
        }

        private void LoadCurrent(bool fit) {
            ClearHover();
            Markers.Clear();
            var frames = snapshot?.Frames;
            if (frames == null || frames.Count == 0) {
                FrameImage = null;
                FrameHeader = string.Empty;
                DetectedCountText = string.Empty;
                ReferenceNote = string.Empty;
                TransformText = string.Empty;
                PrevCommand.NotifyCanExecuteChanged();
                NextCommand.NotifyCanExecuteChanged();
                return;
            }

            var frame = frames[Math.Min(CurrentIndex, frames.Count - 1)];
            FrameImage = frame.Image;
            FrameHeader = $"Focuser position: {frame.FocuserPosition:0}";
            DetectedCountText = $"Detected stars: {frame.DetectedStarCount}";
            ReferenceNote = frame.IsReference ? "Reference frame" : string.Empty;
            TransformText = frame.TransformText ?? string.Empty;

            foreach (var s in frame.Stars) {
                Markers.Add(BuildMarker(s));
            }

            PrevCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
            if (fit) {
                FitRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private FrameReviewMarker BuildMarker(FrameReviewStar s) {
            var boxBrush = s.RegistrationState switch {
                FrameReviewRegistrationState.MatchedWithFit => AcceptedBrush,
                FrameReviewRegistrationState.MatchedNoFit => RegisteredNoFitBrush,
                _ => UnregisteredBrush,
            };
            return new FrameReviewMarker {
                BoxX = s.BoxX,
                BoxY = s.BoxY,
                BoxWidth = s.BoxWidth,
                BoxHeight = s.BoxHeight,
                BoxBrush = boxBrush,
                HfrText = FormatHfr(s.Hfr),
                CenterX = s.CenterX,
                CenterY = s.CenterY,
                TargetX = s.TargetX,
                TargetY = s.TargetY,
                HasRegistrationId = s.RegistrationId != null,
                RegistrationIdText = s.RegistrationId?.ToString() ?? "—",
                HasRegistrationLine = s.HasRegistrationLine,
                FocusOffsetText = FormatOffset(s.FocusOffsetFromMean),
                HasFocusOffset = s.FocusOffsetFromMean.HasValue,
                RegistrationId = s.RegistrationId,
                // Any matched star with a focus curve can be hovered: fitted stars show the curve, registered-but-
                // unfitted stars show their cross-frame points only.
                CanShowFocusGraph = s.RegistrationId != null
                    && snapshot?.FocusCurvesByRegistrationId != null
                    && snapshot.FocusCurvesByRegistrationId.ContainsKey(s.RegistrationId.Value),
            };
        }

        private static string FormatHfr(double hfr) => double.IsNaN(hfr) || hfr <= 0.0 ? "—" : hfr.ToString("F2");

        private static string FormatOffset(double? offset) => offset.HasValue ? offset.Value.ToString("+0;-0;0") : string.Empty;

        public void Dispose() {
            ClearHover();
            Markers.Clear();
            FrameImage = null;
            snapshot = null;
            FitRequested = null;
            RequestClose = null;
        }
    }
}
