using System;
using System.IO;
using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class StarDetectionSettingsExportTests {

        private static StarDetectionSettingsExport BuildPopulated() {
            return new StarDetectionSettingsExport() {
                FileType = StarDetectionSettingsExport.ExpectedFileType,
                SchemaVersion = StarDetectionSettingsExport.CurrentSchemaVersion,
                CreatedAtUtc = new DateTime(2026, 6, 28, 1, 2, 3, DateTimeKind.Utc),
                PluginVersion = "3.0.0.99",
                StarDetection = new StarDetectionSettingsSnapshot() {
                    UseAdvanced = true,
                    UseOptimizedSettings = false,
                    BrightnessSensitivity = 7.25,
                    StructureLayers = 5,
                    MaxDistortion = 0.42,
                    MinHFR = 1.1,
                    StarPeakResponse = 0.8,
                    NoiseReductionRadius = 4,
                    PSFFitType = StarDetectorPSFFitType.Gaussian,
                    MeasurementAverage = MeasurementAverageEnum.MeanOutliers,
                    DefocusAwareDonutDetection = true,
                    DonutMorphCloseSize = 7,
                    SaturationThreshold = 0.95,
                    OptimizedSettings = new OptimizedStarDetectionSettings() {
                        BrightnessSensitivity = 7.25,
                        MaxDistortion = 0.42,
                        RunCount = 3,
                        FinalJ = 0.87,
                        CreatedAtUtc = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc)
                    }
                }
            };
        }

        [Test]
        public void RoundTrips_WithoutTypeNameHandling() {
            var original = BuildPopulated();
            var json = original.Serialize();

            Assert.That(json, Does.Not.Contain("$type"));
            Assert.That(json, Does.Contain("fileType"));

            var restored = StarDetectionSettingsExport.Deserialize(json);

            Assert.Multiple(() => {
                Assert.That(restored.FileType, Is.EqualTo(StarDetectionSettingsExport.ExpectedFileType));
                Assert.That(restored.SchemaVersion, Is.EqualTo(original.SchemaVersion));
                Assert.That(restored.CreatedAtUtc, Is.EqualTo(original.CreatedAtUtc));
                Assert.That(restored.PluginVersion, Is.EqualTo("3.0.0.99"));

                Assert.That(restored.StarDetection.BrightnessSensitivity, Is.EqualTo(7.25));
                Assert.That(restored.StarDetection.StructureLayers, Is.EqualTo(5));
                Assert.That(restored.StarDetection.PSFFitType, Is.EqualTo(StarDetectorPSFFitType.Gaussian));
                Assert.That(restored.StarDetection.MeasurementAverage, Is.EqualTo(MeasurementAverageEnum.MeanOutliers));

                // Nested optimized layer round-trips field-for-field.
                Assert.That(restored.StarDetection.OptimizedSettings, Is.Not.Null);
                Assert.That(restored.StarDetection.OptimizedSettings.FinalJ, Is.EqualTo(0.87));
                Assert.That(restored.StarDetection.OptimizedSettings.RunCount, Is.EqualTo(3));
            });
        }

        [Test]
        public void FromOptions_BlanksMachineLocalIntermediateSettings() {
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            options.SaveIntermediateImages = true;
            options.BrightnessSensitivity = 4.2;

            // The live options have a non-empty default intermediate path.
            Assume.That(options.IntermediateSavePath, Is.Not.Empty);

            var export = StarDetectionSettingsExport.FromOptions(options);

            Assert.Multiple(() => {
                Assert.That(export.FileType, Is.EqualTo(StarDetectionSettingsExport.ExpectedFileType));
                Assert.That(export.SchemaVersion, Is.EqualTo(StarDetectionSettingsExport.CurrentSchemaVersion));
                // Machine-local: blanked so the exported file carries neither a local path nor the debug flag.
                Assert.That(export.StarDetection.IntermediateSavePath, Is.Empty);
                Assert.That(export.StarDetection.SaveIntermediateImages, Is.False);
                // Algorithm knob: captured.
                Assert.That(export.StarDetection.BrightnessSensitivity, Is.EqualTo(4.2));
            });
        }

        [Test]
        public void Validate_Throws_WhenFileTypeWrong() {
            var export = BuildPopulated();
            export.FileType = "SomethingElse";
            Assert.Throws<InvalidOperationException>(() => export.Validate());
        }

        [Test]
        public void Validate_Throws_WhenFileTypeNull() {
            var export = BuildPopulated();
            export.FileType = null;
            Assert.Throws<InvalidOperationException>(() => export.Validate());
        }

        [Test]
        public void Validate_Throws_WhenStarDetectionMissing() {
            var export = BuildPopulated();
            export.StarDetection = null;
            Assert.Throws<InvalidOperationException>(() => export.Validate());
        }

        [Test]
        public void Validate_Throws_WhenSchemaVersionNonPositive() {
            var export = BuildPopulated();
            export.SchemaVersion = 0;
            Assert.Throws<InvalidOperationException>(() => export.Validate());
        }

        [Test]
        public void TryLoad_ReturnsTrue_ForValidFile() {
            var path = Path.Combine(Path.GetTempPath(), "HFExport_" + Guid.NewGuid().ToString("N") + ".json");
            try {
                File.WriteAllText(path, BuildPopulated().Serialize());

                var ok = StarDetectionSettingsExport.TryLoad(path, out var export, out var error);

                Assert.Multiple(() => {
                    Assert.That(ok, Is.True);
                    Assert.That(error, Is.Null);
                    Assert.That(export, Is.Not.Null);
                    Assert.That(export.StarDetection.BrightnessSensitivity, Is.EqualTo(7.25));
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public void TryLoad_ReturnsFalseWithError_WhenCorrupt() {
            var path = Path.Combine(Path.GetTempPath(), "HFExport_" + Guid.NewGuid().ToString("N") + ".json");
            try {
                File.WriteAllText(path, "{ not valid json ");
                var ok = StarDetectionSettingsExport.TryLoad(path, out var export, out var error);
                Assert.Multiple(() => {
                    Assert.That(ok, Is.False);
                    Assert.That(error, Is.Not.Null);
                    Assert.That(export, Is.Null);
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public void TryLoad_ReturnsFalseWithError_WhenSchemaNewerThanSupported() {
            var path = Path.Combine(Path.GetTempPath(), "HFExport_" + Guid.NewGuid().ToString("N") + ".json");
            try {
                var future = BuildPopulated();
                future.SchemaVersion = StarDetectionSettingsExport.CurrentSchemaVersion + 1;
                File.WriteAllText(path, future.Serialize());

                var ok = StarDetectionSettingsExport.TryLoad(path, out var export, out var error);

                Assert.Multiple(() => {
                    Assert.That(ok, Is.False);
                    Assert.That(error, Is.Not.Null);
                    Assert.That(export, Is.Null);
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public void TryLoad_ReturnsFalseWithError_WhenFileMissing() {
            var path = Path.Combine(Path.GetTempPath(), "HFExport_" + Guid.NewGuid().ToString("N") + ".json");
            var ok = StarDetectionSettingsExport.TryLoad(path, out var export, out var error);
            Assert.Multiple(() => {
                Assert.That(ok, Is.False);
                Assert.That(error, Is.Not.Null);
                Assert.That(export, Is.Null);
            });
        }

        [Test]
        public void TryLoad_RejectsAutoFocusMetadataFile() {
            // An AutoFocus metadata.json has the same schemaVersion + starDetection shape but no "fileType"
            // discriminator, so it must be rejected rather than silently imported as star-detection settings.
            var path = Path.Combine(Path.GetTempPath(), "HFExport_" + Guid.NewGuid().ToString("N") + ".json");
            try {
                var afMetadata = new AutoFocusReplayMetadata() {
                    SchemaVersion = AutoFocusReplayMetadata.CurrentSchemaVersion,
                    CreatedAtUtc = new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
                    StarDetection = new StarDetectionSettingsSnapshot() { BrightnessSensitivity = 3.0 },
                    AutoFocus = new AutoFocusOptionsSnapshot()
                };
                File.WriteAllText(path, afMetadata.Serialize());

                var ok = StarDetectionSettingsExport.TryLoad(path, out var export, out var error);

                Assert.Multiple(() => {
                    Assert.That(ok, Is.False);
                    Assert.That(error, Is.Not.Null);
                    Assert.That(export, Is.Null);
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public void RoundTrips_FilterNameProvenance() {
            var original = BuildPopulated();
            original.FilterName = "Ha";

            var json = original.Serialize();
            Assert.That(json, Does.Contain("\"filterName\": \"Ha\""));

            var restored = StarDetectionSettingsExport.Deserialize(json);
            restored.Validate();
            Assert.That(restored.FilterName, Is.EqualTo("Ha"));
        }

        [Test]
        public void Serialize_OmitsFilterName_WhenNotSet() {
            // Exports written with per-filter mode off must stay byte-compatible with the pre-FilterName schema.
            var json = BuildPopulated().Serialize();

            Assert.That(json, Does.Not.Contain("\"filterName\""));

            var restored = StarDetectionSettingsExport.Deserialize(json);
            restored.Validate();
            Assert.That(restored.FilterName, Is.Null);
        }

        [Test]
        public void TryLoad_LoadsLegacyFileWithoutFilterName() {
            var path = Path.Combine(Path.GetTempPath(), "HFExport_" + Guid.NewGuid().ToString("N") + ".json");
            try {
                // Written by a plugin version that predates the FilterName provenance field.
                File.WriteAllText(path, @"{
  ""fileType"": ""HocusFocusStarDetectionSettings"",
  ""schemaVersion"": 1,
  ""createdAtUtc"": ""2026-06-28T01:02:03Z"",
  ""pluginVersion"": ""3.0.0.99"",
  ""starDetection"": { ""brightnessSensitivity"": 7.25 }
}");

                var ok = StarDetectionSettingsExport.TryLoad(path, out var export, out var error);

                Assert.Multiple(() => {
                    Assert.That(ok, Is.True);
                    Assert.That(error, Is.Null);
                    Assert.That(export.FilterName, Is.Null);
                    Assert.That(export.StarDetection.BrightnessSensitivity, Is.EqualTo(7.25));
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public void FromOptions_StampsFilterName_WhenProvided() {
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            var export = StarDetectionSettingsExport.FromOptions(options, "SII");
            Assert.That(export.FilterName, Is.EqualTo("SII"));
        }

        [Test]
        public void FromOptions_LeavesFilterNameNull_ByDefault() {
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            var export = StarDetectionSettingsExport.FromOptions(options);
            Assert.That(export.FilterName, Is.Null);
        }

        // --- Sweep geometry travels in the file --------------------------------------------------------------
        //
        // A sibling node, not part of the starDetection snapshot: that type is also the AF replay payload and the
        // engine's in-memory detector override, where a focuser sweep would be meaningless.

        [Test]
        public void FromOptions_StampsSweepGeometry_WhenProvided() {
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

            var export = StarDetectionSettingsExport.FromOptions(
                options, "Ha", new PerFilterSweepGeometry() { StepSize = 25, InitialOffsetSteps = 8 });

            Assert.Multiple(() => {
                Assert.That(export.SweepGeometry, Is.Not.Null);
                Assert.That(export.SweepGeometry.StepSize, Is.EqualTo(25));
                Assert.That(export.SweepGeometry.InitialOffsetSteps, Is.EqualTo(8));
            });
        }

        [Test]
        public void FromOptions_LeavesSweepGeometryNull_ByDefault() {
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            var export = StarDetectionSettingsExport.FromOptions(options);
            Assert.That(export.SweepGeometry, Is.Null);
        }

        [Test]
        public void FromOptions_NormalizesUnusableSweepGeometry() {
            // A 0 step size must never reach the file (nor, from there, the engine): it is written as "inherit".
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

            var export = StarDetectionSettingsExport.FromOptions(
                options, "Ha", new PerFilterSweepGeometry() { StepSize = 0, InitialOffsetSteps = 0 });

            Assert.Multiple(() => {
                Assert.That(export.SweepGeometry.StepSize, Is.EqualTo(PerFilterSweepGeometry.Inherit));
                Assert.That(export.SweepGeometry.InitialOffsetSteps, Is.EqualTo(PerFilterSweepGeometry.Inherit));
            });
        }

        [Test]
        public void FromOptions_DoesNotAliasTheCallersSweepGeometry() {
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            var geometry = new PerFilterSweepGeometry() { StepSize = 25, InitialOffsetSteps = 8 };

            var export = StarDetectionSettingsExport.FromOptions(options, "Ha", geometry);
            geometry.StepSize = 99;

            Assert.That(export.SweepGeometry.StepSize, Is.EqualTo(25));
        }

        [Test]
        public void RoundTrips_SweepGeometry() {
            var original = BuildPopulated();
            original.SweepGeometry = new PerFilterSweepGeometry() { StepSize = 25, InitialOffsetSteps = 8 };

            var json = original.Serialize();
            Assert.Multiple(() => {
                Assert.That(json, Does.Contain("\"sweepGeometry\""));
                // The resolution predicates are computed; only the two stored fields belong in the file.
                Assert.That(json, Does.Not.Contain("hasStepSize"));
                Assert.That(json, Does.Not.Contain("isUnset"));
            });

            var restored = StarDetectionSettingsExport.Deserialize(json);
            restored.Validate();

            Assert.Multiple(() => {
                Assert.That(restored.SweepGeometry.StepSize, Is.EqualTo(25));
                Assert.That(restored.SweepGeometry.InitialOffsetSteps, Is.EqualTo(8));
            });
        }

        [Test]
        public void RoundTrips_InheritSweepGeometry() {
            // "This filter inherits the profile" is a real state that has to survive the file, so that importing it
            // can CLEAR a stale override on the receiving machine rather than silently leaving it in place.
            var original = BuildPopulated();
            original.SweepGeometry = PerFilterSweepGeometry.Unset();

            var restored = StarDetectionSettingsExport.Deserialize(original.Serialize());

            Assert.Multiple(() => {
                Assert.That(restored.SweepGeometry, Is.Not.Null);
                Assert.That(restored.SweepGeometry.IsUnset, Is.True);
            });
        }

        [Test]
        public void Serialize_OmitsSweepGeometry_WhenNotSet() {
            // Exports written with per-filter mode off must stay byte-compatible with the pre-geometry schema.
            var json = BuildPopulated().Serialize();

            Assert.That(json, Does.Not.Contain("sweepGeometry"));

            var restored = StarDetectionSettingsExport.Deserialize(json);
            restored.Validate();
            Assert.That(restored.SweepGeometry, Is.Null);
        }

        [Test]
        public void TryLoad_LoadsLegacyFileWithoutSweepGeometry() {
            var path = Path.Combine(Path.GetTempPath(), "HFExport_" + Guid.NewGuid().ToString("N") + ".json");
            try {
                // Written by a plugin version that predates the sweep-geometry node. Additive and optional, so the
                // schema version is deliberately unchanged — an older plugin can still read a newer file.
                File.WriteAllText(path, @"{
  ""fileType"": ""HocusFocusStarDetectionSettings"",
  ""schemaVersion"": 1,
  ""createdAtUtc"": ""2026-06-28T01:02:03Z"",
  ""pluginVersion"": ""3.0.0.99"",
  ""filterName"": ""Ha"",
  ""starDetection"": { ""brightnessSensitivity"": 7.25 }
}");

                var ok = StarDetectionSettingsExport.TryLoad(path, out var export, out var error);

                Assert.Multiple(() => {
                    Assert.That(ok, Is.True);
                    Assert.That(error, Is.Null);
                    Assert.That(export.SweepGeometry, Is.Null, "a file with no geometry node must not fabricate one");
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public void FromOptimizedLanding_CarriesNoSweepGeometry() {
            // The optimizer's run-folder handoff is written from a headless pass with no filter context, and its
            // consumer (ImportFromRunFolderAsync) has no per-filter target, so it must not claim one.
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            var export = StarDetectionSettingsExport.FromOptimizedLanding(options, new OptimizedStarDetectionSettings());
            Assert.That(export.SweepGeometry, Is.Null);
        }
    }
}
