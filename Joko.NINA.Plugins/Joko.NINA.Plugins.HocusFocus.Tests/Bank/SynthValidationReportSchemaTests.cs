#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TestApp.SynthBank;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Bank {

    /// <summary>
    /// W24 P3 — the observability the register has been owed since 2026-08-03, pinned by the JSON NAMES every
    /// scorer reads by rather than by the C# property names, because a rename of the former silently unevaluates a
    /// rule while a rename of the latter is a compile error somebody notices.
    ///
    /// <para><b>Why these four fields and not a comment.</b> A binning deferral was recoverable only as a substring
    /// of <c>applied.reasons[]</c>, and the factor a round ran at was recoverable only from the NEXT round's
    /// bootstrap — which does not exist for the LAST round, and the last round is exactly where a terminal state is
    /// decided. A prior wave had to infer a starvation from "the bootstrap step never moving" for that reason.
    /// <c>binningDeferralBoundReached</c> is P2's engagement marker: without it a scorer cannot tell "the bound
    /// worked" from "the population never contained the defect", and the honest verdict is then UNEVALUATED.</para>
    ///
    /// <para>The test is written reflectively so it COMPILES against the pre-change schema and FAILS on it, naming
    /// the missing field, instead of breaking the build and reporting nothing.</para>
    /// </summary>
    [TestFixture]
    public class SynthValidationReportSchemaTests {

        [Test]
        public void RoundRecord_CarriesTheBinningAndDeferralFields() {
            var problems = new List<string>();

            // P3 — the two on binningRecommendation.
            CheckJsonField(typeof(BinningRecommendationSnapshot), "currentFactor", typeof(int), problems);
            CheckJsonField(typeof(BinningRecommendationSnapshot), "appliedFactor", typeof(int), problems);

            // P3 — the two on applied. The second is P2's engagement marker.
            CheckJsonField(typeof(AppliedSnapshot), "stepDeferredByBinning", typeof(bool), problems);
            CheckJsonField(typeof(AppliedSnapshot), "binningDeferralBoundReached", typeof(bool), problems);

            // P1's mirror into the same round record. Nothing else in the harness carries it, and the
            // degenerate-fit census reads it as a key, so its absence is a could-not-look of the same kind.
            CheckJsonField(typeof(StepRecommendationSnapshot), "degenerateReason", typeof(string), problems);

            // Every missing field is named at once: a schema check that stops at the first gap makes the reader
            // discover the second one on the next run, which is how a two-wave repair becomes a four-wave one.
            Assert.That(problems, Is.Empty, "synth-validate/1 round record: " + string.Join("; ", problems));

            // And the round record must actually SERIALIZE them: a property that exists but is dropped from the
            // JSON is indistinguishable, to every scorer in the series, from a property that does not exist.
            var round = new RoundValidationReport {
                RoundIndex = 0,
                BinningRecommendation = new BinningRecommendationSnapshot { HasMeasurement = true, RecommendedFactor = 1 },
                Applied = new AppliedSnapshot(),
                StepRecommendation = new StepRecommendationSnapshot()
            };
            Set(round.BinningRecommendation, "currentFactor", 2);
            Set(round.BinningRecommendation, "appliedFactor", 1);
            Set(round.Applied, "stepDeferredByBinning", true);
            Set(round.Applied, "binningDeferralBoundReached", true);
            Set(round.StepRecommendation, "degenerateReason", "half-width-unresolved");

            var json = JObject.Parse(JsonConvert.SerializeObject(round, Formatting.None));

            Assert.Multiple(() => {
                Assert.That((int)json["binningRecommendation"]["currentFactor"], Is.EqualTo(2));
                Assert.That((int)json["binningRecommendation"]["appliedFactor"], Is.EqualTo(1));
                Assert.That((bool)json["applied"]["stepDeferredByBinning"], Is.True);
                Assert.That((bool)json["applied"]["binningDeferralBoundReached"], Is.True);
                Assert.That((string)json["stepRecommendation"]["degenerateReason"], Is.EqualTo("half-width-unresolved"));
            });
        }

        // ---- reflection over the JsonProperty NAME, which is the contract ------------------------------------

        private static void CheckJsonField(Type owner, string jsonName, Type expectedType, List<string> problems) {
            var property = FindByJsonName(owner, jsonName);
            if (property == null) {
                problems.Add($"{owner.Name} carries no property serialized as \"{jsonName}\" (a scorer reading it "
                             + "gets could-not-look, which is not the same answer as false)");
                return;
            }
            if (property.PropertyType != expectedType) {
                problems.Add($"{owner.Name}.\"{jsonName}\" is {property.PropertyType.Name}, expected {expectedType.Name}");
            }
            if (!property.CanWrite) {
                problems.Add($"{owner.Name}.\"{jsonName}\" is not settable, so the runner cannot populate it");
            }
        }

        private static PropertyInfo FindByJsonName(Type owner, string jsonName) {
            return owner.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName == jsonName);
        }

        private static void Set(object target, string jsonName, object value) {
            FindByJsonName(target.GetType(), jsonName)?.SetValue(target, value);
        }
    }
}
