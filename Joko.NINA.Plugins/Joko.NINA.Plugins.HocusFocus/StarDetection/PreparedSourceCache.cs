#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// The "legitimate speedup" the headless-parity spec reserves for the optimizer (see
    /// docs/headless-detection-parity-design.md and .claude/docs/testapp-cli.md): a cache of the PREPARED
    /// source Mat per (frame image, hotpixel-param key), so an optimization run stops re-doing the
    /// params-identical CFA hotpixel filter + debayer + float conversion on every early-context rebuild
    /// (~1.2 s per build on a 26 MP bayered frame). Exact reuse of the same computed pixels ⇒ detection is
    /// byte-identical to computing fresh each time — this is NOT the rejected "filter at load time" pattern:
    /// the key includes the searched hotpixel axes, so a candidate that moves them recomputes.
    ///
    /// Carried on <c>StarDetectorParams.SourceCache</c> by the OPTIMIZATION paths only (wizard + TestApp
    /// harness); autofocus, sensor modeling, and single-frame detection never set it. Candidate params
    /// inherit the reference via <c>Clone()</c>.
    ///
    /// One entry per frame image (single slot: a key change evicts and replaces, mirroring the early-context
    /// cache's bound). Thread-safety: the dictionary is lock-guarded; per-image entries are never accessed
    /// concurrently because <c>RunEvaluationData</c>'s claim/publish lock serializes builds per frame.
    /// The cache owns its Mats; callers always receive clones. Dispose releases everything.
    /// </summary>
    public sealed class PreparedSourceCache : IDisposable {

        private sealed class Entry {
            public string Key;
            public Mat Prepared;
            public long? HotpixelCount;
        }

        private readonly object sync = new object();
        private readonly Dictionary<object, Entry> entries = new Dictionary<object, Entry>();
        private bool disposed;

        /// <summary>Cache hits/misses, for the optimizer's health line.</summary>
        public long Hits;

        public long Misses;

        /// <summary>Returns a CLONE of the cached prepared Mat for (image, key), or null.</summary>
        public Mat TryGet(object image, string key, out long? hotpixelCount) {
            lock (sync) {
                if (!disposed && entries.TryGetValue(image, out var entry) && entry.Key == key) {
                    Hits++;
                    hotpixelCount = entry.HotpixelCount;
                    return entry.Prepared.Clone();
                }
                Misses++;
                hotpixelCount = null;
                return null;
            }
        }

        /// <summary>Stores a CLONE of <paramref name="prepared"/> for (image, key), evicting any prior slot.</summary>
        public void Store(object image, string key, Mat prepared, long? hotpixelCount) {
            // Check disposed BEFORE cloning: the wizard's Review step detects with params that still carry the
            // torn-down run's cache, so every such Store would otherwise pay a full-frame clone just to throw it
            // away. The clone itself stays outside the lock (it can be ~100 MB); the second check below covers a
            // Dispose racing in between.
            lock (sync) {
                if (disposed) {
                    return;
                }
            }
            var clone = prepared.Clone();
            lock (sync) {
                if (disposed) {
                    clone.Dispose();
                    return;
                }
                if (entries.TryGetValue(image, out var existing)) {
                    existing.Prepared.Dispose();
                    existing.Key = key;
                    existing.Prepared = clone;
                    existing.HotpixelCount = hotpixelCount;
                } else {
                    entries[image] = new Entry { Key = key, Prepared = clone, HotpixelCount = hotpixelCount };
                }
            }
        }

        /// <summary>Releases every entry (the harness calls this between --per-run iterations so a long bank
        /// pass does not accumulate all runs' prepared Mats). The cache stays usable afterwards.</summary>
        public void Clear() {
            lock (sync) {
                foreach (var entry in entries.Values) {
                    entry.Prepared?.Dispose();
                }
                entries.Clear();
            }
        }

        public void Dispose() {
            lock (sync) {
                if (disposed) {
                    return;
                }
                disposed = true;
                foreach (var entry in entries.Values) {
                    entry.Prepared?.Dispose();
                }
                entries.Clear();
            }
        }
    }
}
