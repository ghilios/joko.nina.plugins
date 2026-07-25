#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus.Harness {

    /// <summary>
    /// A deterministic <see cref="IImagingMediator"/> for the AutoFocus sweep harness. It never renders pixels: on
    /// <see cref="CaptureImage"/> it snapshots the focuser's position NOW and threads that position through the
    /// exposure → image-data → rendered-frame chain, so <see cref="SweepFrameLedger"/> can bind the finished frame to
    /// the position it was captured at (see the ledger for why "current" position is wrong). The image objects are
    /// NSubstitute stand-ins carrying only what the engine's <c>region == null</c> path reads: small
    /// <see cref="ImageProperties"/> (64×64, 16-bit, non-bayered) and a null <see cref="IRenderedImage.OriginalImage"/>
    /// so the live-annotation path early-returns.
    /// </summary>
    internal sealed class StampedImagingMediator : IImagingMediator {
        private readonly MovingFocuserMediator focuser;
        private readonly SweepFrameLedger ledger;
        private readonly int width;
        private readonly int height;
        private readonly int bitDepth;
        private readonly bool isBayered;

        // Carries the capture-time position from CaptureImage forward to PrepareImage: the position is stamped onto the
        // IImageData the exposure yields, then read back when that image data is prepared. Reference-keyed so it is
        // collected with the frames.
        private sealed class PositionBox {
            public int Position;
        }

        private readonly ConditionalWeakTable<IImageData, PositionBox> positionsByImageData = new ConditionalWeakTable<IImageData, PositionBox>();

        public StampedImagingMediator(MovingFocuserMediator focuser, SweepFrameLedger ledger, int width = 64, int height = 64, int bitDepth = 16, bool isBayered = false) {
            this.focuser = focuser ?? throw new ArgumentNullException(nameof(focuser));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.width = width;
            this.height = height;
            this.bitDepth = bitDepth;
            this.isBayered = isBayered;
        }

        public Task<IExposureData> CaptureImage(CaptureSequence sequence, CancellationToken token, IProgress<ApplicationStatus> progress, string targetName = "") {
            token.ThrowIfCancellationRequested();

            // Snapshot the focuser position at CAPTURE time. Everything downstream reads this stamped value, never the
            // live position, so a since-moved focuser cannot mis-attribute this frame's HFR.
            var capturedPosition = focuser.Position;

            var imageData = MakeImageData();
            positionsByImageData.AddOrUpdate(imageData, new PositionBox { Position = capturedPosition });

            var exposureData = Substitute.For<IExposureData>();
            exposureData.ToImageData(Arg.Any<IProgress<ApplicationStatus>>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(imageData));
            return Task.FromResult(exposureData);
        }

        public Task<IRenderedImage> PrepareImage(IImageData imageData, PrepareImageParameters parameters, CancellationToken token) {
            token.ThrowIfCancellationRequested();

            var capturedPosition = positionsByImageData.TryGetValue(imageData, out var box)
                ? box.Position
                : focuser.Position; // Only reached if an unstamped IImageData is prepared; keeps behavior defined.

            var rendered = Substitute.For<IRenderedImage>();
            rendered.RawImageData.Returns(imageData);
            rendered.OriginalImage.Returns((BitmapSource)null); // annotation path early-returns on a null OriginalImage
            rendered.Image.Returns((BitmapSource)null);

            ledger.Associate(rendered, capturedPosition);
            return Task.FromResult(rendered);
        }

        private IImageData MakeImageData() {
            var imageData = Substitute.For<IImageData>();
            imageData.Properties.Returns(new ImageProperties(width, height, bitDepth, isBayered, gain: 0, offset: 0));
            return imageData;
        }

        // ---- Unreached on the region==null Run() path. -----------------------------------------------------------

        public Task<IRenderedImage> CaptureAndPrepareImage(CaptureSequence sequence, PrepareImageParameters parameters, CancellationToken token, IProgress<ApplicationStatus> progress)
            => throw new NotSupportedException();

        public Task<IRenderedImage> PrepareImage(IExposureData imageData, PrepareImageParameters parameters, CancellationToken token)
            => throw new NotSupportedException();

        public Task<bool> StartLiveView(CaptureSequence sequence, CancellationToken ct) => throw new NotSupportedException();

        public void DestroyImage() { }

        public void SetImage(BitmapSource img) { }

        public int GetImageRotation() => 0;

        public void SetImageRotation(int rotation) { }

        public void SetSubSambleRectangle(ObservableRectangle observableRectangle) { }

        public void RegisterHandler(IImagingVM handler) { }

        public event EventHandler<ImagePreparedEventArgs> ImagePrepared { add { } remove { } }
    }
}
