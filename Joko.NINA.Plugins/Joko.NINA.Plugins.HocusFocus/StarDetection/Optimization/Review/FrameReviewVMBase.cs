#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// Shared navigation/viewport/marker/legend scaffolding for the read-only "Review Frames" VMs (F23): clamped frame
    /// stepping, the current-frame image + size, the inverse-zoom marker bindings, and the legend rebuild pair.
    /// Subclasses supply the per-frame load (markers, headers) and the legend contents. Implements
    /// <see cref="IViewportHostViewModel"/> so it drives <see cref="ReviewViewportHostBase"/> directly.
    /// </summary>
    public abstract class FrameReviewVMBase<TMarker> : BaseINPC, IViewportHostViewModel {

        protected FrameReviewVMBase(int frameCount) {
            FrameCount = frameCount;
            Viewport = new StarReviewViewport();
            PrevCommand = new RelayCommand(Prev, () => CurrentIndex > 0);
            NextCommand = new RelayCommand(Next, () => CurrentIndex < FrameCount - 1);
            FitCommand = new RelayCommand(() => FitRequested?.Invoke(this, EventArgs.Empty));
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
            FramePickerItems = Array.Empty<FramePickerItem>();
        }

        public event EventHandler RequestClose;
        public event EventHandler FitRequested;

        public StarReviewViewport Viewport { get; }
        public ObservableCollection<TMarker> Markers { get; } = new();

        public RelayCommand PrevCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand FitCommand { get; }
        public RelayCommand CloseCommand { get; }

        System.Windows.Input.ICommand IViewportHostViewModel.PrevCommand => PrevCommand;
        System.Windows.Input.ICommand IViewportHostViewModel.NextCommand => NextCommand;

        protected int FrameCount { get; }

        private int currentIndex;
        public int CurrentIndex {
            get => currentIndex;
            protected set {
                if (currentIndex != value) {
                    currentIndex = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(PositionLabel));
                    RaisePropertyChanged(nameof(SelectedFrameNumber));
                }
            }
        }

        public string PositionLabel => FrameCount > 0 ? $"{CurrentIndex + 1} / {FrameCount}" : "0 / 0";

        /// <summary>Toolbar frame-picker entries (1-based number + focuser-position label). Populated by the subclass,
        /// the only place with the per-frame focuser positions.</summary>
        public IReadOnlyList<FramePickerItem> FramePickerItems { get; protected set; }

        /// <summary>Two-way bound by the toolbar frame picker (1-based, matching <see cref="PositionLabel"/>). Selecting
        /// a frame jumps to it exactly as Prev/Next do — set the index, then LoadCurrent(fit:false) so zoom/pan is
        /// preserved. Out-of-range and no-op selections are ignored.</summary>
        public int SelectedFrameNumber {
            get => CurrentIndex + 1;
            set {
                var index = value - 1;
                if (index >= 0 && index < FrameCount && index != CurrentIndex) {
                    CurrentIndex = index;
                    LoadCurrent(fit: false);
                }
            }
        }

        private BitmapSource frameImage;
        public BitmapSource FrameImage {
            get => frameImage;
            protected set {
                frameImage = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ImageWidth));
                RaisePropertyChanged(nameof(ImageHeight));
            }
        }

        public double ImageWidth => frameImage?.PixelWidth ?? 0;
        public double ImageHeight => frameImage?.PixelHeight ?? 0;

        // ---- inverse-zoom overlay bindings (subclasses add their own extra offsets and override NotifyViewportChanged) ----
        public double MarkerStrokeThickness => 1.5 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);
        public double MarkerTextScale => 1.0 / Math.Max(StarReviewViewport.MinScale, Viewport.Scale);

        public virtual void NotifyViewportChanged() {
            RaisePropertyChanged(nameof(MarkerStrokeThickness));
            RaisePropertyChanged(nameof(MarkerTextScale));
        }

        // ---- legend ----
        private IReadOnlyList<StarReviewLegendEntry> legendEntries;
        public IReadOnlyList<StarReviewLegendEntry> LegendEntries {
            get => legendEntries;
            private set { legendEntries = value; RaisePropertyChanged(); }
        }

        protected void RebuildLegend() => LegendEntries = BuildLegend();

        protected abstract IReadOnlyList<StarReviewLegendEntry> BuildLegend();

        /// <summary>Load the frame at the given index (set FrameImage, headers, repopulate Markers/overlays).</summary>
        protected abstract void LoadFrame(int index);

        protected void Prev() {
            if (CurrentIndex > 0) {
                CurrentIndex--;
                LoadCurrent(fit: false);
            }
        }

        protected void Next() {
            if (CurrentIndex < FrameCount - 1) {
                CurrentIndex++;
                LoadCurrent(fit: false);
            }
        }

        protected void LoadCurrent(bool fit) {
            Markers.Clear();
            LoadFrame(CurrentIndex);
            PrevCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
            if (fit) {
                FitRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        // F34: subclasses call this from Dispose to break the VM->control event edges (a subclass cannot null an
        // event declared on the base).
        protected void DetachReviewEvents() {
            FitRequested = null;
            RequestClose = null;
        }
    }
}
