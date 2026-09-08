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
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// Per-row run-length index of the "lit" pixels (>= threshold) of a binarized structure map, built in
    /// one parallel pass so candidate collection never has to raster-scan the full frame again. The index
    /// classifies pixels exactly like the sequential walker's <c>&lt; ZERO_THRESHOLD</c> background test
    /// (lit = NOT background), and <see cref="ConsumeRect"/> reproduces the walker's
    /// zero-the-bounding-box mutation as interval subtraction — so a walker driven by this index visits the
    /// same pixels in the same order as one reading (and zeroing) the Mat directly.
    ///
    /// The parallel build is deterministic: each row's runs depend only on that row's pixels.
    /// </summary>
    internal sealed class StructureRunIndex {

        /// <summary>Half-open pixel run [Start, End) on one row.</summary>
        internal readonly struct Run {
            public readonly int Start;
            public readonly int End;

            public Run(int start, int end) {
                Start = start;
                End = end;
            }
        }

        private readonly List<Run>[] rows;

        public int Width { get; }
        public int Height { get; }

        private StructureRunIndex(List<Run>[] rows, int width, int height) {
            this.rows = rows;
            Width = width;
            Height = height;
        }

        public IReadOnlyList<Run> RowRuns(int y) => rows[y];

        public static unsafe StructureRunIndex Build(Mat structureMap, float threshold) {
            if (structureMap.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }
            int width = structureMap.Width;
            int height = structureMap.Height;
            var rows = new List<Run>[height];
            var basePtr = (byte*)structureMap.DataPointer;
            long step = structureMap.Step();

            Parallel.For(0, height, y => {
                var runs = new List<Run>();
                var row = (float*)(basePtr + y * step);
                int x = 0;
                while (x < width) {
                    // Background is "< threshold" exactly like the walker; everything else (including a
                    // hypothetical NaN, which fails <) is lit.
                    if (row[x] < threshold) {
                        ++x;
                        continue;
                    }
                    int start = x;
                    while (x < width && !(row[x] < threshold)) {
                        ++x;
                    }
                    runs.Add(new Run(start, x));
                }
                rows[y] = runs;
            });
            return new StructureRunIndex(rows, width, height);
        }

        /// <summary>True when pixel (x, y) is inside a live (unconsumed) run.</summary>
        public bool IsLit(int y, int x) {
            var runs = rows[y];
            int lo = 0, hi = runs.Count - 1;
            while (lo <= hi) {
                int mid = (lo + hi) >> 1;
                var run = runs[mid];
                if (x < run.Start) {
                    hi = mid - 1;
                } else if (x >= run.End) {
                    lo = mid + 1;
                } else {
                    return true;
                }
            }
            return false;
        }

        /// <summary>First lit x &gt;= fromX on row y, or -1 when the rest of the row is background.</summary>
        public int NextLitInRow(int y, int fromX) {
            var runs = rows[y];
            int lo = 0, hi = runs.Count - 1, result = -1;
            while (lo <= hi) {
                int mid = (lo + hi) >> 1;
                var run = runs[mid];
                if (run.End <= fromX) {
                    lo = mid + 1;
                } else {
                    result = mid;
                    hi = mid - 1;
                }
            }
            if (result < 0) {
                return -1;
            }
            var r = runs[result];
            return fromX > r.Start ? fromX : r.Start;
        }

        /// <summary>
        /// Removes the rectangle's pixels from the live runs — the interval-subtraction equivalent of the
        /// walker zeroing every pixel of a collected candidate's bounding box.
        /// </summary>
        public void ConsumeRect(Rect bounds) {
            int left = bounds.Left;
            int right = bounds.Right;
            int bottom = Math.Min(bounds.Bottom, Height);
            for (int y = Math.Max(0, bounds.Top); y < bottom; ++y) {
                var runs = rows[y];
                for (int i = 0; i < runs.Count; ++i) {
                    var run = runs[i];
                    if (run.End <= left) {
                        continue;
                    }
                    if (run.Start >= right) {
                        break;
                    }
                    bool keepLeft = run.Start < left;
                    bool keepRight = run.End > right;
                    if (keepLeft && keepRight) {
                        runs[i] = new Run(run.Start, left);
                        runs.Insert(i + 1, new Run(right, run.End));
                        break;
                    }
                    if (keepLeft) {
                        runs[i] = new Run(run.Start, left);
                    } else if (keepRight) {
                        runs[i] = new Run(right, run.End);
                        break;
                    } else {
                        runs.RemoveAt(i);
                        --i;
                    }
                }
            }
        }
    }
}
