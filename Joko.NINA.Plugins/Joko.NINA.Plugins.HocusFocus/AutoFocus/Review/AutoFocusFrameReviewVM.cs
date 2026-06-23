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
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Review {

    /// <summary>One drawable star marker for the manual-AF frame-review overlay, in the frame's image-pixel coords.
    /// Exactly one of <see cref="ShowBox"/>/<see cref="ShowEllipse"/>/<see cref="ShowPsfEllipse"/> is set (per the
    /// annotator's StarBoundsType, with the PSF ellipse only when a PSF fit exists). The annotation text + star-center
    /// crosshair are independent (gated at the layer level by the annotator's show-flags).</summary>
    public sealed class AutoFocusReviewMarker {
        // Box / Ellipse bounds (anchored at the detector bounding box).
        public bool ShowBox { get; init; }
        public bool ShowEllipse { get; init; }
        public double BoxX { get; init; }
        public double BoxY { get; init; }
        public double BoxWidth { get; init; }
        public double BoxHeight { get; init; }

        // PSF-ellipse bounds (anchored at the star centroid, rotated by the PSF angle).
        public bool ShowPsfEllipse { get; init; }
        public double PsfCenterX { get; init; }
        public double PsfCenterY { get; init; }
        public double PsfEllipseWidth { get; init; }
        public double PsfEllipseHeight { get; init; }
        public double PsfTranslateX { get; init; }
        public double PsfTranslateY { get; init; }
        public double PsfRotationDegrees { get; init; }

        // Per-star annotation text, anchored at the bounding box's top-right (matching the annotator).
        public bool HasAnnotationText { get; init; }
        public string AnnotationText { get; init; }
        public double TextX { get; init; }
        public double TextY { get; init; }

        // Star-center crosshair (image-pixel arm half-lengths, like the annotator).
        public double CrossCenterX { get; init; }
        public double CrossCenterY { get; init; }
        public double CrossHalfX { get; init; }
        public double CrossHalfY { get; init; }
        public double NegCrossHalfX => -CrossHalfX;
        public double NegCrossHalfY => -CrossHalfY;
    }

    /// <summary>A detector rejection-reason / ROI rectangle, pre-colored with its annotator brush and ready to draw
    /// (built per frame from only the enabled reasons).</summary>
    public sealed class AutoFocusReviewRectOverlay {
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public Brush Brush { get; init; }
    }

    /// <summary>
    /// Read-only viewer VM for the manual AutoFocus "Review Frames" dialog. Steps through the captured frames,
    /// reproducing the Hocus Focus Star Annotator's overlays — star bounds (box/ellipse/PSF), a selectable per-star
    /// annotation text, the star-center crosshair, the seven rejection-reason rectangles, and the ROI — honoring the
    /// shared <see cref="IStarAnnotatorOptions"/> colors/show-flags live. Reuses the unit-tested
    /// <see cref="StarReviewViewport"/> zoom/pan math, <see cref="StarReviewHfrStats"/> for the per-frame HFR header,
    /// and <see cref="StarReviewLegendEntry"/> for the legend. The per-star text selection is review-local (initialized
    /// from the annotator setting, not written back). Disposing releases the retained bitmaps.
    /// </summary>
    public sealed class AutoFocusFrameReviewVM : BaseINPC, IReviewDialogViewModel, IViewportHostViewModel {

        private static SolidColorBrush FrozenBrush(Color c) {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private AutoFocusFrameReviewSnapshot snapshot;
        private readonly IStarAnnotatorOptions annotatorOptions;
        private readonly IApplicationDispatcher applicationDispatcher;
        private readonly MeasurementAverageEnum measurementAverage;
        private readonly bool psfAvailable;

        public event EventHandler RequestClose;
        public event EventHandler FitRequested;

        public StarReviewViewport Viewport { get; }
        public ObservableCollection<AutoFocusReviewMarker> Markers { get; } = new();
        public ObservableCollection<AutoFocusReviewRectOverlay> RectOverlays { get; } = new();

        public RelayCommand PrevCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand FitCommand { get; }
        public RelayCommand CloseCommand { get; }

        // The public PrevCommand/NextCommand are RelayCommand; expose them as ICommand for the viewport host (F08).
        System.Windows.Input.ICommand IViewportHostViewModel.PrevCommand => PrevCommand;
        System.Windows.Input.ICommand IViewportHostViewModel.NextCommand => NextCommand;

        public AutoFocusFrameReviewVM(AutoFocusFrameReviewSnapshot snapshot, IStarAnnotatorOptions annotatorOptions, IApplicationDispatcher applicationDispatcher, MeasurementAverageEnum measurementAverage) {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            this.annotatorOptions = annotatorOptions ?? throw new ArgumentNullException(nameof(annotatorOptions));
            this.applicationDispatcher = applicationDispatcher ?? throw new ArgumentNullException(nameof(applicationDispatcher));
            this.measurementAverage = measurementAverage;

            // PSF fitting is intentionally disabled during auto-focus, so the PSF-derived annotation options (FWHM,
            // eccentricity, PSF rotation/background/peak, Moffat beta) have no data and would always render blank. Only
            // offer the options that AF actually produces, and seed the selection from the configured Star Annotator
            // option — falling back to HFR when that configured option is a (now-unavailable) PSF property.
            this.psfAvailable = snapshot.Frames.Any(f => f.Stars.Any(s => s.HasPsf));
            AvailableAnnotationTypes = Enum.GetValues<ShowAnnotationTypeEnum>()
                .Where(t => psfAvailable || !RequiresPsf(t))
                .ToList();
            var configured = annotatorOptions.ShowAnnotationType;
            this.showAnnotationType = AvailableAnnotationTypes.Contains(configured) ? configured : ShowAnnotationTypeEnum.HFR;

            // Star-bounds shapes: PSF bounds need a PSF fit (unavailable during auto-focus), so offer only the shapes
            // that can actually draw. Review-local, seeded from the configured annotator option (PSF -> Box fallback).
            AvailableBoundsTypes = Enum.GetValues<StarBoundsTypeEnum>()
                .Where(t => psfAvailable || t != StarBoundsTypeEnum.PSF)
                .ToList();
            var configuredBounds = annotatorOptions.StarBoundsType;
            this.starBoundsType = AvailableBoundsTypes.Contains(configuredBounds) ? configuredBounds : StarBoundsTypeEnum.Box;

            Viewport = new StarReviewViewport();

            annotatorOptions.PropertyChanged += AnnotatorOptions_PropertyChanged;

            PrevCommand = new RelayCommand(Prev, () => CurrentIndex > 0);
            NextCommand = new RelayCommand(Next, () => CurrentIndex < FrameCount - 1);
            FitCommand = new RelayCommand(() => FitRequested?.Invoke(this, EventArgs.Empty));
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));

            legendEntries = BuildLegend();
            CurrentIndex = 0;
            // No fit here: FitRequested has zero subscribers at construction time (the control subscribes in
            // OnDataContextChanged, after the ctor returns). The initial fit is driven by the control's first
            // layout (Loaded/SizeChanged). Passing fit:false makes that contract explicit.
            LoadCurrent(fit: false);
        }

        private int FrameCount => snapshot?.Frames.Count ?? 0;

        // Overlay-affecting annotator properties: a change to any of these re-renders the overlays + legend live
        // (same UX as the image annotator). Properties the review does not render are ignored to avoid wasted rebuilds.
        private static readonly HashSet<string> OverlayAffectingProperties = new() {
            nameof(IStarAnnotatorOptions.ShowAnnotations),
            nameof(IStarAnnotatorOptions.ShowStarBounds),
            nameof(IStarAnnotatorOptions.StarBoundsType),
            nameof(IStarAnnotatorOptions.StarBoundsColor),
            nameof(IStarAnnotatorOptions.ShowStarCenter),
            nameof(IStarAnnotatorOptions.StarCenterColor),
            nameof(IStarAnnotatorOptions.ShowAnnotationType),
            nameof(IStarAnnotatorOptions.AnnotationColor),
            nameof(IStarAnnotatorOptions.ShowROI),
            nameof(IStarAnnotatorOptions.ROIColor),
            nameof(IStarAnnotatorOptions.ShowTooDistorted), nameof(IStarAnnotatorOptions.TooDistortedColor),
            nameof(IStarAnnotatorOptions.ShowDegenerate), nameof(IStarAnnotatorOptions.DegenerateColor),
            nameof(IStarAnnotatorOptions.ShowSaturated), nameof(IStarAnnotatorOptions.SaturatedColor),
            nameof(IStarAnnotatorOptions.ShowLowSensitivity), nameof(IStarAnnotatorOptions.LowSensitivityColor),
            nameof(IStarAnnotatorOptions.ShowNotCentered), nameof(IStarAnnotatorOptions.NotCenteredColor),
            nameof(IStarAnnotatorOptions.ShowTooFlat), nameof(IStarAnnotatorOptions.TooFlatColor),
            nameof(IStarAnnotatorOptions.ShowContaminated), nameof(IStarAnnotatorOptions.ContaminatedColor),
        };

        // Changing an overlay-affecting annotator property re-renders the overlays + legend live. Treat a null/empty
        // PropertyName as "rebuild all". The body is marshaled to the UI thread because the shared annotator options
        // object could raise PropertyChanged off the UI thread, and the rebuild mutates the bound ObservableCollections.
        private void AnnotatorOptions_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (!string.IsNullOrEmpty(e.PropertyName) && !OverlayAffectingProperties.Contains(e.PropertyName)) {
                return;
            }
            applicationDispatcher.DispatchSynchronizationContext(() => {
                RaiseAnnotatorVisualsChanged();
                RebuildCurrentFrameOverlays();
                RebuildLegend();
            });
        }

        private void RaiseAnnotatorVisualsChanged() {
            RaisePropertyChanged(nameof(StarBoundsBrush));
            RaisePropertyChanged(nameof(AnnotationBrush));
            RaisePropertyChanged(nameof(StarCenterBrush));
            RaisePropertyChanged(nameof(ShowStarBounds));
            RaisePropertyChanged(nameof(ShowStarCenter));
        }

        // ---- annotator-driven visuals ---------------------------------------------------------------------

        public Brush StarBoundsBrush => FrozenBrush(annotatorOptions.StarBoundsColor);
        public Brush AnnotationBrush => FrozenBrush(annotatorOptions.AnnotationColor);
        public Brush StarCenterBrush => FrozenBrush(annotatorOptions.StarCenterColor);
        public bool ShowStarBounds => annotatorOptions.ShowStarBounds;
        public bool ShowStarCenter => annotatorOptions.ShowStarCenter;

        // ---- per-star text selection (review-local) -------------------------------------------------------

        /// <summary>The annotation-text options offered in the review. PSF-derived properties are excluded when the run
        /// has no PSF fits (auto-focus disables PSF fitting), so the combo never offers options that render blank.</summary>
        public IReadOnlyList<ShowAnnotationTypeEnum> AvailableAnnotationTypes { get; }

        /// <summary>Whether an annotation type needs a PSF fit (everything except None/HFR/Background, which read fields
        /// the detector always populates). Mirrors the PSF-null guards in <see cref="AutoFocusAnnotationText"/>.</summary>
        private static bool RequiresPsf(ShowAnnotationTypeEnum t) =>
            t != ShowAnnotationTypeEnum.None && t != ShowAnnotationTypeEnum.HFR && t != ShowAnnotationTypeEnum.Background;

        /// <summary>The star-bounds shapes offered in the review (PSF excluded when there are no PSF fits).</summary>
        public IReadOnlyList<StarBoundsTypeEnum> AvailableBoundsTypes { get; }

        /// <summary>Review-local star-bounds shape (Box/Ellipse/PSF), seeded from the annotator option. Changing it
        /// re-renders the overlay without touching the saved Star Annotator setting.</summary>
        private StarBoundsTypeEnum starBoundsType;
        public StarBoundsTypeEnum StarBoundsType {
            get => starBoundsType;
            set {
                if (starBoundsType != value) {
                    starBoundsType = value;
                    RaisePropertyChanged();
                    RebuildCurrentFrameOverlays();
                    RebuildLegend();
                }
            }
        }

        private ShowAnnotationTypeEnum showAnnotationType;
        public ShowAnnotationTypeEnum ShowAnnotationType {
            get => showAnnotationType;
            set {
                if (showAnnotationType != value) {
                    showAnnotationType = value;
                    RaisePropertyChanged();
                    RebuildCurrentFrameOverlays();
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

        private string statsText;
        public string StatsText {
            get => statsText;
            private set { statsText = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowStats)); }
        }

        public bool ShowStats => !string.IsNullOrEmpty(StatsText);

        // ---- inverse-zoom overlay bindings ----------------------------------------------------------------

        public double MarkerStrokeThickness => 1.5 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);
        public double MarkerTextScale => 1.0 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);
        public double LabelOffset => -15.0 * MarkerTextScale;

        public void NotifyViewportChanged() {
            RaisePropertyChanged(nameof(MarkerStrokeThickness));
            RaisePropertyChanged(nameof(MarkerTextScale));
            RaisePropertyChanged(nameof(LabelOffset));
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
            var entries = new List<StarReviewLegendEntry> {
                new() { Brush = StarBoundsBrush, Caption = $"Star bounds ({StarBoundsType})", Enabled = annotatorOptions.ShowStarBounds },
                new() { Brush = AnnotationBrush, Caption = "Star annotation text", Enabled = ShowAnnotationType != ShowAnnotationTypeEnum.None },
                new() { Brush = StarCenterBrush, Caption = "Star center", Enabled = annotatorOptions.ShowStarCenter },
            };
            foreach (var r in Reasons()) {
                entries.Add(new StarReviewLegendEntry { Brush = FrozenBrush(r.Color), Caption = r.Caption, Enabled = r.Show });
            }
            // ROI is drawn whenever the region is not full (annotator ignores ShowROI), i.e. whenever the
            // current frame actually has ROI rects.
            var roiActive = CurrentFrame?.RoiRects.Count > 0;
            entries.Add(new StarReviewLegendEntry { Brush = FrozenBrush(annotatorOptions.ROIColor), Caption = "Detection ROI", Enabled = roiActive });
            return entries;
        }

        // The seven rejection reasons the annotator draws, with their annotator show-flag + color + a plain-language
        // caption — the single source the overlays AND the legend iterate, so they can't drift.
        private IEnumerable<(bool Show, Color Color, string Caption, Func<AutoFocusReviewFrame, IReadOnlyList<System.Windows.Rect>> Rects)> Reasons() {
            yield return (annotatorOptions.ShowTooDistorted, annotatorOptions.TooDistortedColor, "Too distorted", f => f.TooDistorted);
            yield return (annotatorOptions.ShowDegenerate, annotatorOptions.DegenerateColor, "Degenerate shape", f => f.Degenerate);
            yield return (annotatorOptions.ShowSaturated, annotatorOptions.SaturatedColor, "Saturated", f => f.Saturated);
            yield return (annotatorOptions.ShowLowSensitivity, annotatorOptions.LowSensitivityColor, "Below sensitivity", f => f.LowSensitivity);
            yield return (annotatorOptions.ShowNotCentered, annotatorOptions.NotCenteredColor, "Off-center", f => f.NotCentered);
            yield return (annotatorOptions.ShowTooFlat, annotatorOptions.TooFlatColor, "Too flat", f => f.TooFlat);
            yield return (annotatorOptions.ShowContaminated, annotatorOptions.ContaminatedColor, "Contaminated", f => f.Contaminated);
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

        private AutoFocusReviewFrame CurrentFrame {
            get {
                var frames = snapshot?.Frames;
                if (frames == null || frames.Count == 0) {
                    return null;
                }
                return frames[Math.Min(CurrentIndex, frames.Count - 1)];
            }
        }

        private void LoadCurrent(bool fit) {
            var frame = CurrentFrame;
            if (frame == null) {
                FrameImage = null;
                FrameHeader = string.Empty;
                DetectedCountText = string.Empty;
                StatsText = string.Empty;
                Markers.Clear();
                RectOverlays.Clear();
                PrevCommand.NotifyCanExecuteChanged();
                NextCommand.NotifyCanExecuteChanged();
                return;
            }

            FrameImage = frame.Image;
            FrameHeader = $"Focuser position: {frame.FocuserPosition:0}";
            DetectedCountText = $"Detected stars: {frame.DetectedStarCount}";
            var (center, deviation) = StarReviewHfrStats.Compute(frame.Stars.Select(s => s.Hfr), measurementAverage);
            // Omit the "(n=…)" count — the detected-star count is already shown separately in the header.
            StatsText = StarReviewHfrStats.FormatStats(center, deviation, frame.DetectedStarCount, measurementAverage, includeCount: false);

            RebuildCurrentFrameOverlays();

            PrevCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
            if (fit) {
                FitRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RebuildCurrentFrameOverlays() {
            Markers.Clear();
            RectOverlays.Clear();
            var frame = CurrentFrame;
            if (frame == null) {
                return;
            }
            var boundsType = StarBoundsType;
            foreach (var s in frame.Stars) {
                Markers.Add(BuildMarker(s, boundsType));
            }
            foreach (var r in Reasons()) {
                if (!r.Show) {
                    continue;
                }
                var brush = FrozenBrush(r.Color);
                foreach (var rect in r.Rects(frame)) {
                    RectOverlays.Add(ToOverlay(rect, brush));
                }
            }
            // The annotator draws the ROI whenever the detection region is not full and never reads ShowROI
            // (HocusFocusStarAnnotator.cs). frame.RoiRects is already empty for a full-frame region,
            // so drawing it unconditionally matches the annotated image.
            var roiBrush = FrozenBrush(annotatorOptions.ROIColor);
            foreach (var rect in frame.RoiRects) {
                RectOverlays.Add(ToOverlay(rect, roiBrush));
            }
        }

        private static AutoFocusReviewRectOverlay ToOverlay(System.Windows.Rect rect, Brush brush) {
            return new AutoFocusReviewRectOverlay { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height, Brush = brush };
        }

        private AutoFocusReviewMarker BuildMarker(AutoFocusReviewStar s, StarBoundsTypeEnum boundsType) {
            // Star-center crosshair: center at the centroid (offset by the PSF sub-pixel offset when the bounds type is
            // PSF, exactly as the annotator does), arms sized to half the distance to the nearest box edge.
            var centerX = s.CenterX;
            var centerY = s.CenterY;
            if (boundsType == StarBoundsTypeEnum.PSF && s.HasPsf) {
                centerX += s.PsfOffsetX;
                centerY += s.PsfOffsetY;
            }
            var halfX = Math.Max(1.0, Math.Min(centerX - s.BoxX, s.BoxX + s.BoxWidth - centerX)) / 2.0;
            var halfY = Math.Max(1.0, Math.Min(centerY - s.BoxY, s.BoxY + s.BoxHeight - centerY)) / 2.0;

            var showPsfEllipse = boundsType == StarBoundsTypeEnum.PSF && s.HasPsf;
            var text = AutoFocusAnnotationText.Format(s, ShowAnnotationType);

            return new AutoFocusReviewMarker {
                ShowBox = boundsType == StarBoundsTypeEnum.Box,
                ShowEllipse = boundsType == StarBoundsTypeEnum.Ellipse,
                BoxX = s.BoxX,
                BoxY = s.BoxY,
                BoxWidth = s.BoxWidth,
                BoxHeight = s.BoxHeight,
                ShowPsfEllipse = showPsfEllipse,
                PsfCenterX = s.CenterX,
                PsfCenterY = s.CenterY,
                PsfEllipseWidth = showPsfEllipse ? s.PsfFwhmX * 2.0 : 0.0,
                PsfEllipseHeight = showPsfEllipse ? s.PsfFwhmY * 2.0 : 0.0,
                PsfTranslateX = showPsfEllipse ? -s.PsfFwhmX : 0.0,
                PsfTranslateY = showPsfEllipse ? -s.PsfFwhmY : 0.0,
                // Annotator rotation is clockwise whereas PSF theta is counter-clockwise, so negate (matches annotator).
                PsfRotationDegrees = showPsfEllipse ? -s.PsfThetaDegrees : 0.0,
                HasAnnotationText = !string.IsNullOrEmpty(text),
                AnnotationText = text,
                TextX = s.BoxX + s.BoxWidth,
                TextY = s.BoxY,
                CrossCenterX = centerX,
                CrossCenterY = centerY,
                CrossHalfX = halfX,
                CrossHalfY = halfY,
            };
        }

        public void Dispose() {
            annotatorOptions.PropertyChanged -= AnnotatorOptions_PropertyChanged;
            Markers.Clear();
            RectOverlays.Clear();
            FrameImage = null;
            snapshot = null;
            FitRequested = null;
            RequestClose = null;
        }
    }
}
