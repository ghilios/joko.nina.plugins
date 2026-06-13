using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class ParallelExecutionTests {

    // -------------------------------------------------------------------------
    // SharedScheduler: concurrency cap
    // -------------------------------------------------------------------------

    [Test]
    public void SharedScheduler_BoundsActiveConcurrency() {
        // Schedule 64 tasks; each records the running high-water mark, then finishes.
        const int taskCount = 64;
        int activeCount = 0;
        int highWater = 0;
        int completedCount = 0;
        var allDone = new ManualResetEventSlim(false);

        var scheduler = ParallelExecution.SharedScheduler;
        var factory = new TaskFactory(scheduler);

        for (int i = 0; i < taskCount; i++) {
            factory.StartNew(() => {
                // Increment active, capture high-water mark atomically.
                int current = Interlocked.Increment(ref activeCount);
                int prev;
                do {
                    prev = Volatile.Read(ref highWater);
                } while (current > prev && Interlocked.CompareExchange(ref highWater, current, prev) != prev);

                // Brief work so tasks genuinely overlap.
                Thread.Sleep(5);

                Interlocked.Decrement(ref activeCount);
                if (Interlocked.Increment(ref completedCount) == taskCount) {
                    allDone.Set();
                }
            });
        }

        // Give up to 30 seconds — plenty for 64 × 5 ms tasks even on a single core.
        bool finished = allDone.Wait(TimeSpan.FromSeconds(30));

        Assert.That(finished, Is.True, "Not all tasks completed within the time limit");
        Assert.That(completedCount, Is.EqualTo(taskCount), "All tasks must complete");
        Assert.That(highWater, Is.GreaterThanOrEqualTo(1), "At least one task must have run");
        Assert.That(highWater, Is.LessThanOrEqualTo(scheduler.MaximumConcurrencyLevel),
            "Observed concurrency must not exceed the scheduler's MaximumConcurrencyLevel");
    }

    [Test]
    public void SharedScheduler_MaximumConcurrencyLevel_EqualsProcessorCount() {
        Assert.That(
            ParallelExecution.SharedScheduler.MaximumConcurrencyLevel,
            Is.EqualTo(Math.Max(1, Environment.ProcessorCount)));
    }

    // -------------------------------------------------------------------------
    // ResolveDegreeOfParallelism
    // -------------------------------------------------------------------------

    [Test]
    public void ResolveDegreeOfParallelism_Zero_ReturnsProcessorCount() {
        var expected = Math.Min(Environment.ProcessorCount,
            ParallelExecution.SharedScheduler.MaximumConcurrencyLevel);
        Assert.That(ParallelExecution.ResolveDegreeOfParallelism(0), Is.EqualTo(expected));
    }

    [Test]
    public void ResolveDegreeOfParallelism_Negative_ReturnsProcessorCount() {
        var expected = Math.Min(Environment.ProcessorCount,
            ParallelExecution.SharedScheduler.MaximumConcurrencyLevel);
        Assert.That(ParallelExecution.ResolveDegreeOfParallelism(-5), Is.EqualTo(expected));
    }

    [Test]
    public void ResolveDegreeOfParallelism_One_ReturnsOne() {
        Assert.That(ParallelExecution.ResolveDegreeOfParallelism(1), Is.EqualTo(1));
    }

    [Test]
    public void ResolveDegreeOfParallelism_LargeValue_ClampedToMaxConcurrencyLevel() {
        int result = ParallelExecution.ResolveDegreeOfParallelism(int.MaxValue);
        Assert.That(result, Is.LessThanOrEqualTo(ParallelExecution.SharedScheduler.MaximumConcurrencyLevel));
        Assert.That(result, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void ResolveDegreeOfParallelism_WithinRange_ReturnsSameValue() {
        int max = ParallelExecution.SharedScheduler.MaximumConcurrencyLevel;
        // Pick a value strictly within range (only meaningful if max >= 2).
        if (max >= 2) {
            Assert.That(ParallelExecution.ResolveDegreeOfParallelism(1), Is.EqualTo(1));
        }
        Assert.That(ParallelExecution.ResolveDegreeOfParallelism(max), Is.EqualTo(max));
    }

    // -------------------------------------------------------------------------
    // CreateOptions
    // -------------------------------------------------------------------------

    [Test]
    public void CreateOptions_UsesSharedScheduler() {
        var opts = ParallelExecution.CreateOptions(0, CancellationToken.None);
        Assert.That(opts.TaskScheduler, Is.SameAs(ParallelExecution.SharedScheduler));
    }

    [Test]
    public void CreateOptions_EmbedsCancellationToken() {
        using var cts = new CancellationTokenSource();
        var opts = ParallelExecution.CreateOptions(1, cts.Token);
        Assert.That(opts.CancellationToken, Is.EqualTo(cts.Token));
    }

    [Test]
    public void CreateOptions_MaxDegreeOfParallelism_MatchesResolve() {
        var opts = ParallelExecution.CreateOptions(1, CancellationToken.None);
        Assert.That(opts.MaxDegreeOfParallelism,
            Is.EqualTo(ParallelExecution.ResolveDegreeOfParallelism(1)));
    }

    // -------------------------------------------------------------------------
    // StarDetectorParams.ToString contains the new field
    // -------------------------------------------------------------------------

    [Test]
    public void StarDetectorParams_ToString_ContainsMaxStarEvaluationParallelism() {
        var p = new StarDetectorParams();
        Assert.That(p.ToString(), Does.Contain(nameof(StarDetectorParams.MaxStarEvaluationParallelism)));
    }

    [Test]
    public void StarDetectorParams_MaxStarEvaluationParallelism_DefaultsToZero() {
        var p = new StarDetectorParams();
        Assert.That(p.MaxStarEvaluationParallelism, Is.EqualTo(0));
    }
}
