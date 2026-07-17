#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Combines several integers into one well-distributed RNG seed.
    ///
    /// <para>Both callers combine <b>adjacent</b> integers and both need avalanche, not merely uniqueness.
    /// <see cref="FrameDeveloper"/> derives a per-stripe seed from (frame seed, stripe index) — stripes 3 and 4
    /// abut in the image, so correlated streams would show as a visible seam. The camera derives a per-exposure
    /// seed from (base seed, focuser position, exposure counter) — consecutive exposures differ by 1 in the
    /// counter and consecutive autofocus points by ~20 in the focuser position. <c>new Random(seed + 1)</c> does
    /// not guarantee a stream decorrelated from <c>new Random(seed)</c>; the MurmurHash3 finalizer spreads a
    /// one-bit input change across the whole output.</para>
    /// </summary>
    public static class SeedMixer {

        /// <summary>Combines values in order (order-sensitive) into a seed. Allocates — call per exposure/stripe, never per pixel.</summary>
        public static int Combine(params int[] values) {
            if (values == null) throw new ArgumentNullException(nameof(values));
            unchecked {
                var hash = 2166136261u; // FNV-1a offset basis
                foreach (var value in values) {
                    hash = Fmix32((hash ^ (uint)value) * 16777619u); // FNV-1a step, then avalanche
                }
                return (int)hash;
            }
        }

        /// <summary>MurmurHash3's 32-bit finalizer: spreads every input bit across every output bit.</summary>
        private static uint Fmix32(uint hash) {
            unchecked {
                hash ^= hash >> 16;
                hash *= 0x85ebca6bu;
                hash ^= hash >> 13;
                hash *= 0xc2b2ae35u;
                hash ^= hash >> 16;
                return hash;
            }
        }
    }
}
