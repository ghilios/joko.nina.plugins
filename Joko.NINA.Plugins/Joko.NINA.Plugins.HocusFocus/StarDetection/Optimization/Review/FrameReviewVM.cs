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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>One drawable accepted-star marker for the Frame Review overlay, in image-pixel coords. Box geometry is
    /// the detector's real bounding box (TOP-LEFT + size, for the Canvas.Left/Top binding); the two labels are the HFR
    /// (above the box) and the registration id (below). The optional arrow runs from the raw detection
    /// (<see cref="ArrowStartX"/>,<see cref="ArrowStartY"/>) to the registered target
    /// (<see cref="ArrowEndX"/>,<see cref="ArrowEndY"/>); <see cref="ArrowHead"/> is a precomputed triangle at the
    /// target so the XAML needs no rotation math.</summary>
    public sealed class FrameReviewMarker {
        public double BoxX { get; init; }
        public double BoxY { get; init; }
        public double BoxWidth { get; init; }
        public double BoxHeight { get; init; }
        public string HfrText { get; init; }
        public string RegistrationIdText { get; init; }
        public bool HasArrow { get; init; }
        public double ArrowStartX { get; init; }
        public double ArrowStartY { get; init; }
        public double ArrowEndX { get; init; }
        public double ArrowEndY { get; init; }
        public PointCollection ArrowHead { get; init; }
    }

    /// <summary>
    /// Read-only viewer VM for the Aberration Inspector "Review Frames" dialog — a stripped fork of
    /// <see cref="StarReviewVM"/> with all labeling/undo removed. Steps through the captured frames one at a time,
    /// overlaying each accepted star's bounding box, its HFR + registration-id labels, and (when RANSAC alignment was
    /// on) a registration arrow to its target. Reuses the unit-tested <see cref="StarReviewViewport"/> zoom/pan math
    /// and the <see cref="StarReviewLegendEntry"/> legend-row type. Disposing releases the retained frame bitmaps.
    /// </summary>
    public sealed class FrameReviewVM : BaseINPC, IDisposable {

        private static SolidColorBrush FrozenBrush(Color c) {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>Accepted-star bounding-box stroke (green) — matches StarReview's accepted color.</summary>
        public Brush AcceptedBrush { get; } = FrozenBrush(Color.FromRgb(0x00, 0xFF, 0x00));

        /// <summary>Registration-id label color (cyan).</summary>
        public Brush RegistrationBrush { get; } = FrozenBrush(Color.FromRgb(0x00, 0xE5, 0xFF));

        /// <summary>HFR label color (yellow).</summary>
        public Brush HfrBrush { get; } = FrozenBrush(Color.FromRgb(0xFF, 0xD7, 0x00));

        /// <summary>Registration-arrow color (orange).</summary>
        public Brush ArrowBrush { get; } = FrozenBrush(Color.FromRgb(0xFF, 0x8C, 0x00));

        private FrameReviewSnapshot snapshot;

        /// <summary>Raised when the VM's Close command asks the host to dismiss the dialog.</summary>
        public event EventHandler RequestClose;

        /// <summary>Raised when the view should re-fit the image to the viewport (Fit button / new frame load).</summary>
        public event EventHandler FitRequested;

        public StarReviewViewport Viewport { get; }
        public IReadOnlyList<StarReviewLegendEntry> LegendEntries { get; }
        public ObservableCollection<FrameReviewMarker> Markers { get; } = new();

        public RelayCommand PrevCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand FitCommand { get; }
        public RelayCommand CloseCommand { get; }

        public FrameReviewVM(FrameReviewSnapshot snapshot) {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Viewport = new StarReviewViewport();
            LegendEntries = BuildLegend();

            PrevCommand = new RelayCommand(Prev, () => CurrentIndex > 0);
            NextCommand = new RelayCommand(Next, () => CurrentIndex < FrameCount - 1);
            FitCommand = new RelayCommand(() => FitRequested?.Invoke(this, EventArgs.Empty));
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));

            CurrentIndex = 0;
            LoadCurrent(fit: true);
        }

        private int FrameCount => snapshot?.Frames.Count ?? 0;

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

        // All markers live inside a canvas whose RenderTransform scales everything (including stroke width and text),
        // so bind these INVERSELY to the current scale to keep stroke/text/labels a roughly constant on-screen size.
        // Clamped to MinScale so they never blow up at extreme zoom-out. (Copied from StarReviewVM.)
        public double MarkerStrokeThickness => 1.5 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);
        public double MarkerTextScale => 1.0 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);

        // HFR label one text-height ABOVE the box top (≈15 px on screen at any zoom); reg-id label a small gap BELOW
        // the box (the per-marker BoxHeight translate puts it at the box bottom; this adds the gap).
        public double HfrLabelOffset => -15.0 * MarkerTextScale;
        public double RegistrationLabelOffset => 3.0 * MarkerTextScale;

        /// <summary>Re-raises the zoom-dependent overlay bindings. The view calls this after any viewport change
        /// (wheel-zoom, fit, pan) so stroke widths + label sizes track the current scale.</summary>
        public void NotifyViewportChanged() {
            RaisePropertyChanged(nameof(MarkerStrokeThickness));
            RaisePropertyChanged(nameof(MarkerTextScale));
            RaisePropertyChanged(nameof(HfrLabelOffset));
            RaisePropertyChanged(nameof(RegistrationLabelOffset));
        }

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
            var frames = snapshot?.Frames;
            Markers.Clear();
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

        private static FrameReviewMarker BuildMarker(FrameReviewStar s) {
            return new FrameReviewMarker {
                BoxX = s.BoxX,
                BoxY = s.BoxY,
                BoxWidth = s.BoxWidth,
                BoxHeight = s.BoxHeight,
                HfrText = FormatHfr(s.Hfr),
                RegistrationIdText = s.RegistrationId?.ToString() ?? "—",
                HasArrow = s.HasArrow,
                ArrowStartX = s.OriginalX,
                ArrowStartY = s.OriginalY,
                ArrowEndX = s.CenterX,
                ArrowEndY = s.CenterY,
                ArrowHead = s.HasArrow ? BuildArrowHead(s.OriginalX, s.OriginalY, s.CenterX, s.CenterY) : null,
            };
        }

        private static string FormatHfr(double hfr) => double.IsNaN(hfr) || hfr <= 0.0 ? "—" : hfr.ToString("F2");

        // A small filled triangle at the arrow's target end, precomputed in image coords so the XAML needs no rotation
        // math. Sized in image pixels (scales with the line under zoom — acceptable for a diagnostic overlay).
        private static PointCollection BuildArrowHead(double sx, double sy, double ex, double ey) {
            const double headLen = 8.0;
            const double headWidth = 5.0;
            var dx = ex - sx;
            var dy = ey - sy;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) {
                return new PointCollection();
            }
            var ux = dx / len;
            var uy = dy / len;        // unit direction (start -> end)
            var px = -uy;
            var py = ux;              // unit perpendicular
            var baseX = ex - ux * headLen;
            var baseY = ey - uy * headLen;
            var pc = new PointCollection {
                new System.Windows.Point(ex, ey),                                         // tip
                new System.Windows.Point(baseX + px * headWidth, baseY + py * headWidth),  // base corner 1
                new System.Windows.Point(baseX - px * headWidth, baseY - py * headWidth),  // base corner 2
            };
            pc.Freeze();
            return pc;
        }

        private IReadOnlyList<StarReviewLegendEntry> BuildLegend() {
            var entries = new List<StarReviewLegendEntry> {
                new() { Brush = AcceptedBrush, Caption = "Accepted star", Dashed = false },
                new() { Brush = RegistrationBrush, Caption = "Registration ID (— = unmatched)", Dashed = false },
                new() { Brush = HfrBrush, Caption = "HFR", Dashed = false },
            };
            // The arrow only appears when RANSAC alignment was on, so only show its legend row then.
            if (snapshot?.RansacEnabled == true) {
                entries.Add(new StarReviewLegendEntry { Brush = ArrowBrush, Caption = "Registration arrow (to target)", Dashed = false });
            }
            return entries;
        }

        public void Dispose() {
            Markers.Clear();
            FrameImage = null;
            snapshot = null;
        }
    }
}
