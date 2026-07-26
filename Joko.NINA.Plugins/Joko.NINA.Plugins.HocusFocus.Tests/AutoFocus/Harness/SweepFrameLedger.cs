#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.Interfaces;
using System;
using System.Runtime.CompilerServices;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus.Harness {

    /// <summary>
    /// Maps each rendered/prepared frame to the focuser position it was CAPTURED at. This is what kills the
    /// capture/detect concurrency race in the deterministic harness: capture happens on the walk thread and stamps the
    /// current position, but detection runs later on a pooled task by which time the focuser may already have moved on
    /// to the next point. Scripted detection therefore reads the FRAME's stamped position from this ledger, never the
    /// focuser's live "current" position. Keyed by reference identity (a <see cref="ConditionalWeakTable{TKey, TValue}"/>)
    /// so entries are collected with their frames and no test needs to clear it.
    /// </summary>
    internal sealed class SweepFrameLedger {

        // ConditionalWeakTable requires a reference-type value; box the int so it can be stored (and updated on the rare
        // re-association of the same frame instance).
        private sealed class PositionBox {
            public int Position;
        }

        private readonly ConditionalWeakTable<IRenderedImage, PositionBox> positionsByFrame = new ConditionalWeakTable<IRenderedImage, PositionBox>();

        /// <summary>Binds <paramref name="image"/> to the focuser <paramref name="position"/> in effect at capture time.</summary>
        public void Associate(IRenderedImage image, int position) {
            if (image == null) {
                throw new ArgumentNullException(nameof(image));
            }
            positionsByFrame.AddOrUpdate(image, new PositionBox { Position = position });
        }

        /// <summary>
        /// Returns the focuser position <paramref name="image"/> was captured at. Throws if the frame was never
        /// associated — an unassociated frame reaching detection means the harness wiring is broken, and silently
        /// falling back to a "current" position would reintroduce the very race this ledger exists to remove.
        /// </summary>
        public int PositionFor(IRenderedImage image) {
            if (image != null && positionsByFrame.TryGetValue(image, out var box)) {
                return box.Position;
            }
            throw new InvalidOperationException("No focuser position is associated with this frame. The StampedImagingMediator must Associate every prepared frame before detection reads it.");
        }
    }
}
