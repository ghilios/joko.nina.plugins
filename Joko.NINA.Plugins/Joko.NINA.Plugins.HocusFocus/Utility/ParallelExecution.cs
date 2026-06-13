#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// Process-wide CPU-concurrency governor for inner parallel loops.
    /// <para>
    /// When many CPU-bound star-detection runs execute concurrently (saved-run replay, aberration
    /// inspector multi-region build) each spawning its own <c>Parallel.For</c>, the naive approach
    /// gives <c>ProcessorCount²</c> active threads. This class provides a single shared
    /// <see cref="LimitedConcurrencyLevelTaskScheduler"/> whose cap equals
    /// <see cref="Environment.ProcessorCount"/>, so all inner loops collectively stay within one
    /// logical CPU worth of threads regardless of how many outer detections run concurrently.
    /// </para>
    /// <para>
    /// Callers obtain pre-built <see cref="ParallelOptions"/> via <see cref="CreateOptions"/> or
    /// resolve a raw degree with <see cref="ResolveDegreeOfParallelism"/>.
    /// </para>
    /// </summary>
    public static class ParallelExecution {

        /// <summary>
        /// Shared task scheduler whose maximum concurrency level equals
        /// <see cref="Environment.ProcessorCount"/>. All inner parallel loops should be routed
        /// through this scheduler so that the total number of active threads is bounded.
        /// </summary>
        public static readonly TaskScheduler SharedScheduler =
            new LimitedConcurrencyLevelTaskScheduler(Math.Max(1, Environment.ProcessorCount));

        /// <summary>
        /// Resolves a caller-supplied parallelism knob to a concrete degree of parallelism, then
        /// clamps it to the shared scheduler's <see cref="TaskScheduler.MaximumConcurrencyLevel"/>
        /// so that passing the result to <see cref="ParallelOptions.MaxDegreeOfParallelism"/> never
        /// exceeds what <see cref="SharedScheduler"/> can honour.
        /// </summary>
        /// <param name="knob">
        /// <c>0</c> or negative ⇒ auto (<see cref="Environment.ProcessorCount"/>);
        /// <c>1</c> ⇒ sequential (kill-switch);
        /// greater than <c>1</c> ⇒ that many, clamped to <see cref="TaskScheduler.MaximumConcurrencyLevel"/>.
        /// </param>
        /// <returns>A degree of parallelism in [1, <see cref="TaskScheduler.MaximumConcurrencyLevel"/>].</returns>
        public static int ResolveDegreeOfParallelism(int knob) {
            var raw = knob <= 0 ? Environment.ProcessorCount : knob;
            return Math.Min(raw, SharedScheduler.MaximumConcurrencyLevel);
        }

        /// <summary>
        /// Creates a <see cref="ParallelOptions"/> bound to <see cref="SharedScheduler"/> with
        /// <see cref="ParallelOptions.MaxDegreeOfParallelism"/> resolved from <paramref name="knob"/>
        /// via <see cref="ResolveDegreeOfParallelism"/>.
        /// </summary>
        /// <param name="knob">Parallelism knob — see <see cref="ResolveDegreeOfParallelism"/>.</param>
        /// <param name="ct">Cancellation token to embed in the options.</param>
        /// <returns>A fully configured <see cref="ParallelOptions"/> ready for <c>Parallel.For/ForEach</c>.</returns>
        public static ParallelOptions CreateOptions(int knob, CancellationToken ct) {
            return new ParallelOptions {
                TaskScheduler = SharedScheduler,
                MaxDegreeOfParallelism = ResolveDegreeOfParallelism(knob),
                CancellationToken = ct
            };
        }
    }
}
