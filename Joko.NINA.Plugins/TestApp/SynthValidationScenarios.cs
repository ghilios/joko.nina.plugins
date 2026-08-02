#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;

namespace TestApp.SynthBank {

    /// <summary>What a scenario is measuring — most scenarios probe whether the recommenders CONVERGE toward the
    /// expected-optimal bootstrap; S5 is deliberately different (design: "NOT convergence -- PASS = the
    /// degradation signature appears").</summary>
    public enum SynthValidationExpectation {
        Convergence,
        DegradationSignature
    }

    /// <summary>
    /// One convergence-driver scenario (design §"V1. The convergence driver", the S0-S6 table): how it perturbs
    /// the dataset's expected-optimal bootstrap, which datasets it applies to, and what its terminal assertions
    /// should expect. <see cref="IsApplicable"/> is DATA (a predicate over the dataset spec + its derived
    /// expected-optimal), never a hardcoded dataset-id list, per the design instruction that "scenario -&gt;
    /// dataset applicability is data, not hardcoded ifs".
    /// </summary>
    public sealed class SynthValidationScenario {
        public string Id { get; init; }
        public string Description { get; init; }

        /// <summary>Multiplies <see cref="SynthExpectedOptimal.StepSizeSteps"/> for the scenario's INITIAL bootstrap (round 0).</summary>
        public double StepFactor { get; init; } = 1.0;

        /// <summary>Multiplies <see cref="SynthExpectedOptimal.ExposureSeconds"/> for the scenario's INITIAL bootstrap.</summary>
        public double ExposureFactor { get; init; } = 1.0;

        /// <summary>S4: start detection binning at 1 instead of the dataset's expected (2).</summary>
        public bool ForceDetectionBinningTo1 { get; init; }

        /// <summary>S5: force donut-aware detection OFF even though the dataset's physics call for it ON.</summary>
        public bool ForceDonutOff { get; init; }

        public SynthValidationExpectation Expectation { get; init; } = SynthValidationExpectation.Convergence;

        /// <summary>S2's "converges within &lt;= 2 rounds" — checked as a FLAG (not a FAIL) if exceeded, since the
        /// driver's own convergence-rate expectations are softer than the hard A1-A7 invariants.</summary>
        public int? ExpectedMaxRoundsForConvergence { get; init; }

        /// <summary>Data-driven applicability predicate over the dataset spec and its derived expected-optimal.
        /// Returns (applicable, reasonIfNot).</summary>
        public Func<SynthDatasetSpec, SynthExpectedOptimal, (bool Applicable, string Reason)> IsApplicable { get; init; }
    }

    /// <summary>
    /// The checked-in S0-S6 scenario set (design table, verbatim). <see cref="All"/> is in run order: S0 always
    /// first (design: "the harness self-test", run first on every dataset), then S1-S6.
    /// </summary>
    public static class SynthValidationScenarios {

        /// <summary>
        /// "Meaningfully above the 0.5 s floor" (design, S3 applicability note): most datasets derive exactly the
        /// 0.5 s exposure floor (<c>SynthBankDerivations.MinExposureSeconds</c> is private; this driver only needs
        /// the SAME numeric floor to state "meaningfully above" it, not the constant itself), so ANY threshold
        /// strictly between 0.5 s and the smallest elevated dataset's derived exposure (D15 at 1.0 s) separates the
        /// two groups cleanly. 0.75 s is the midpoint of that gap -- a deliberately arbitrary but safely-centered
        /// choice, documented here since the design leaves the exact cutoff unspecified.
        /// </summary>
        public const double MeaningfulExposureAboveFloorSeconds = 0.75;

        private static (bool, string) AlwaysApplicable(SynthDatasetSpec d, SynthExpectedOptimal e) => (true, null);

        public static readonly SynthValidationScenario S0 = new SynthValidationScenario {
            Id = "S0",
            Description = "no perturbation (bootstrap = expected optimal) -- the harness self-test; runs on every dataset, first",
            StepFactor = 1.0,
            ExposureFactor = 1.0,
            IsApplicable = AlwaysApplicable
        };

        public static readonly SynthValidationScenario S1 = new SynthValidationScenario {
            Id = "S1",
            Description = "step x0.25 -- multi-round convergence; WasCapped true early",
            StepFactor = 0.25,
            IsApplicable = AlwaysApplicable
        };

        public static readonly SynthValidationScenario S2 = new SynthValidationScenario {
            Id = "S2",
            Description = "step x4 -- converges within <= 2 rounds",
            StepFactor = 4.0,
            ExpectedMaxRoundsForConvergence = 2,
            IsApplicable = AlwaysApplicable
        };

        public static readonly SynthValidationScenario S3 = new SynthValidationScenario {
            Id = "S3",
            Description = "exposure x0.25 -- multi-round via the 4x per-round cap",
            ExposureFactor = 0.25,
            IsApplicable = (d, e) => double.IsFinite(e.ExposureSeconds) && e.ExposureSeconds > MeaningfulExposureAboveFloorSeconds
                ? (true, null)
                : (false, $"derived exposure {e.ExposureSeconds:0.###}s is at (or NaN vs) the 0.5s floor -- not meaningfully " +
                    $"above it (design: 'most datasets derive 0.5 s'; only D10/D12/D15/D17-class datasets qualify)")
        };

        public static readonly SynthValidationScenario S4 = new SynthValidationScenario {
            Id = "S4",
            Description = "detection binning 1 where 2 expected -- asserts binning-first ordering",
            ForceDetectionBinningTo1 = true,
            IsApplicable = (d, e) => e.DetectionBinning == 2
                ? (true, null)
                : (false, $"expected detection binning is {e.DetectionBinning}, not 2 -- nothing to force-mismatch")
        };

        public static readonly SynthValidationScenario S5 = new SynthValidationScenario {
            Id = "S5",
            Description = "donut off on obstructed datasets -- NOT convergence; PASS = the degradation signature appears",
            ForceDonutOff = true,
            Expectation = SynthValidationExpectation.DegradationSignature,
            IsApplicable = (d, e) => d.CentralObstructionFraction > 0.0
                ? (true, null)
                : (false, $"unobstructed optic (ε={d.CentralObstructionFraction:0.00}) -- no donut to turn off")
        };

        public static readonly SynthValidationScenario S6 = new SynthValidationScenario {
            Id = "S6",
            Description = "step and exposure both wrong -- combined interaction",
            StepFactor = 0.25,
            ExposureFactor = 0.25,
            // S6's exposure axis is literally S3's perturbation combined with a step error (design table: "step and
            // exposure BOTH wrong"), so it inherits S3's applicability gate for the same reason: perturbing an
            // exposure that is already sitting at the 0.5s floor down to 0.125s renders a pathologically
            // signal-starved sweep that tests floor behavior, not convergence. Not stated explicitly in the design;
            // this is this implementation's chosen resolution of that gap.
            IsApplicable = (d, e) => S3.IsApplicable(d, e)
        };

        /// <summary>Run order: S0 first (design: run first, on every dataset), then S1-S6.</summary>
        public static readonly IReadOnlyList<SynthValidationScenario> All = new[] { S0, S1, S2, S3, S4, S5, S6 };

        public static SynthValidationScenario ById(string id) {
            foreach (var s in All) {
                if (string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) {
                    return s;
                }
            }
            return null;
        }
    }
}
