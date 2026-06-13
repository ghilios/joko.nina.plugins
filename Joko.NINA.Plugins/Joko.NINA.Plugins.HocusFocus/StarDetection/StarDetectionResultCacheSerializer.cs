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
using Newtonsoft.Json.Serialization;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// Dedicated (de)serialization for the per-region <c>_star_detection_result.json</c> files that an
    /// auto-focus run writes alongside its saved exposures. Unlike the default Json.NET settings used elsewhere,
    /// this serializer round-trips the result <em>faithfully</em>:
    ///
    /// <list type="bullet">
    ///   <item><see cref="StarDetectionResult.StarList"/> is declared <c>List&lt;DetectedStar&gt;</c> but is
    ///   populated at runtime with <see cref="HocusFocusDetectedStar"/> instances (carrying the fitted
    ///   <c>PSF</c>, <c>NormalisedBrightness</c>, etc.). With default settings the polymorphic subtype is lost
    ///   on reload, so consumers such as <c>SensorModel</c> that cast each star to
    ///   <see cref="HocusFocusDetectedStar"/> would throw. <see cref="TypeNameHandling.Auto"/> emits a
    ///   <c>$type</c> discriminator for each star so the concrete subtype is restored.</item>
    ///   <item><see cref="HocusFocusStarDetectionResult.DetectorVersion"/> and
    ///   <see cref="HocusFocusStarDetectionResult.CacheKey"/> survive the round-trip so a later reuse-side task
    ///   can validate a saved result against the current detector logic/params before reusing it.</item>
    /// </list>
    ///
    /// <para><see cref="TypeNameAssemblyFormatHandling.Simple"/> writes the assembly <em>name only</em> (no
    /// version/culture/key), so the embedded <c>$type</c> is stable across plugin releases. A type rename would
    /// be paired with a <see cref="StarDetector.StarDetectorVersion"/> bump, which changes every result's
    /// <see cref="HocusFocusStarDetectionResult.CacheKey"/> and so invalidates the cache — covering the rename
    /// before any stale <c>$type</c> could be read back.</para>
    ///
    /// <para>This class is the single source of truth for the cache-file format so the save site
    /// (<c>AutoFocusEngine.EvaluateExposure</c>) and the future reuse site agree on exactly how the files are
    /// (de)serialized.</para>
    /// </summary>
    public static class StarDetectionResultCacheSerializer {

        /// <summary>
        /// The shared settings. <see cref="TypeNameHandling.Auto"/> writes a <c>$type</c> only where the static
        /// type differs from the runtime type (each <see cref="HocusFocusDetectedStar"/> in the
        /// <c>List&lt;DetectedStar&gt;</c>); the concrete <see cref="PSFModel"/> matches its declared property
        /// type so no discriminator is emitted, and it is reconstructed through its single public constructor by
        /// parameter-name matching (its derived members are recomputed, not read back).
        /// </summary>
        public static JsonSerializerSettings Settings { get; } = new JsonSerializerSettings {
            TypeNameHandling = TypeNameHandling.Auto,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
            Formatting = Formatting.Indented,
            ContractResolver = new CacheContractResolver(),
            Converters = { new PSFModelJsonConverter() }
        };

        /// <summary>
        /// Round-trips <see cref="PSFModel"/> faithfully despite it being immutable (single parameterized
        /// constructor, all <c>private set</c>). The wrinkle the default reconstruction can't handle: the
        /// constructor takes a <c>pixelScale</c> that the type does not expose as a property — it survives only
        /// as the factor in <c>FWHMArcsecs = FWHMPixels * pixelScale</c>. So on write we emit every public
        /// property, and on read we recover <c>pixelScale = FWHMArcsecs / FWHMPixels</c> and feed the stored
        /// inputs back through the constructor, which recomputes the derived members exactly (so
        /// <c>FWHMArcsecs</c> comes back bit-for-bit, not zeroed). <c>null</c> PSFs pass through as JSON null.
        /// </summary>
        private sealed class PSFModelJsonConverter : JsonConverter<PSFModel> {

            public override void WriteJson(JsonWriter writer, PSFModel value, JsonSerializer serializer) {
                if (value == null) {
                    writer.WriteNull();
                    return;
                }

                var o = new JObject {
                    [nameof(PSFModel.PSFType)] = (int)value.PSFType,
                    [nameof(PSFModel.OffsetX)] = value.OffsetX,
                    [nameof(PSFModel.OffsetY)] = value.OffsetY,
                    [nameof(PSFModel.Peak)] = value.Peak,
                    [nameof(PSFModel.Background)] = value.Background,
                    [nameof(PSFModel.SigmaX)] = value.SigmaX,
                    [nameof(PSFModel.SigmaY)] = value.SigmaY,
                    [nameof(PSFModel.Sigma)] = value.Sigma,
                    [nameof(PSFModel.FWHMx)] = value.FWHMx,
                    [nameof(PSFModel.FWHMy)] = value.FWHMy,
                    [nameof(PSFModel.ThetaRadians)] = value.ThetaRadians,
                    [nameof(PSFModel.FWHMPixels)] = value.FWHMPixels,
                    [nameof(PSFModel.FWHMArcsecs)] = value.FWHMArcsecs,
                    [nameof(PSFModel.Eccentricity)] = value.Eccentricity,
                    [nameof(PSFModel.RSquared)] = value.RSquared,
                    [nameof(PSFModel.ReducedChiSquared)] = value.ReducedChiSquared,
                    [nameof(PSFModel.Beta)] = value.Beta
                };
                o.WriteTo(writer);
            }

            public override PSFModel ReadJson(JsonReader reader, Type objectType, PSFModel existingValue, bool hasExistingValue, JsonSerializer serializer) {
                if (reader.TokenType == JsonToken.Null) {
                    return null;
                }

                var o = JObject.Load(reader);
                var fwhmX = Get(o, nameof(PSFModel.FWHMx));
                var fwhmY = Get(o, nameof(PSFModel.FWHMy));
                var fwhmPixels = Get(o, nameof(PSFModel.FWHMPixels));
                var fwhmArcsecs = Get(o, nameof(PSFModel.FWHMArcsecs));

                // FWHMArcsecs = FWHMPixels * pixelScale → recover the scale; fall back to 1.0 only if FWHMPixels
                // is non-positive (degenerate PSF), in which case FWHMArcsecs would be 0 either way.
                double pixelScale = (fwhmPixels > 0.0 && !double.IsNaN(fwhmPixels)) ? fwhmArcsecs / fwhmPixels : 1.0;

                var psfType = (StarDetectorPSFFitType)(int)Get(o, nameof(PSFModel.PSFType));
                return new PSFModel(
                    psfType: psfType,
                    offsetX: Get(o, nameof(PSFModel.OffsetX)),
                    offsetY: Get(o, nameof(PSFModel.OffsetY)),
                    peak: Get(o, nameof(PSFModel.Peak)),
                    background: Get(o, nameof(PSFModel.Background)),
                    sigmaX: Get(o, nameof(PSFModel.SigmaX)),
                    sigmaY: Get(o, nameof(PSFModel.SigmaY)),
                    fwhmX: fwhmX,
                    fwhmY: fwhmY,
                    thetaRadians: Get(o, nameof(PSFModel.ThetaRadians)),
                    rSquared: Get(o, nameof(PSFModel.RSquared)),
                    pixelScale: pixelScale,
                    reducedChiSquared: Get(o, nameof(PSFModel.ReducedChiSquared)),
                    beta: Get(o, nameof(PSFModel.Beta)));
            }

            private static double Get(JObject o, string name) {
                var token = o[name];
                return token == null || token.Type == JTokenType.Null
                    ? double.NaN
                    : token.Value<double>();
            }
        }

        /// <summary>
        /// Contract resolver that makes the result graph safely deserializable. <see cref="StarDetectorMetrics"/>
        /// exposes several read-only <em>computed-count</em> aliases (e.g. <c>TooDistorted</c>) whose getter
        /// derives the count from a corresponding <c>*Bounds</c> list and whose setter throws
        /// <see cref="NotSupportedException"/>. They serialize fine (getter), but Json.NET would invoke the
        /// throwing setter on reload. Since each count is fully reconstructed from its <c>*Bounds</c> list (which
        /// does round-trip), we simply skip these on deserialize. Detection is structural — any property whose
        /// declared setter throws <see cref="NotSupportedException"/> unconditionally — so adding another such
        /// computed count in the future needs no change here.
        /// </summary>
        private sealed class CacheContractResolver : DefaultContractResolver {

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization) {
                var property = base.CreateProperty(member, memberSerialization);
                if (property.Writable && member is PropertyInfo pi && SetterAlwaysThrowsNotSupported(pi)) {
                    // Keep it readable (so the count is still written for human inspection), but never attempt
                    // to set it on reload; the value is recomputed from the round-tripped *Bounds list.
                    property.Writable = false;
                    property.ShouldDeserialize = _ => false;
                }
                return property;
            }

            // Some types in the result graph (e.g. StarDetectionRegion, RatioRect) are immutable: no default
            // constructor and only parameterized constructors. Json.NET auto-selects a parameterized constructor
            // only when there is exactly one; StarDetectionRegion has several overloads, so we pick the greediest
            // one and wire its parameters to the matching JSON properties by name (case-insensitive, the
            // Newtonsoft default). The constructor parameter names line up with the property names
            // (outerBoundary→OuterBoundary, innerCropBoundary→InnerCropBoundary, index→Index;
            // startX→StartX, ...), so the immutable region geometry round-trips faithfully.
            protected override JsonObjectContract CreateObjectContract(Type objectType) {
                var contract = base.CreateObjectContract(objectType);
                if (contract.OverrideCreator == null && contract.DefaultCreator == null) {
                    var ctors = objectType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                    if (ctors.Length > 1) {
                        ConstructorInfo greediest = null;
                        foreach (var ctor in ctors) {
                            if (greediest == null || ctor.GetParameters().Length > greediest.GetParameters().Length) {
                                greediest = ctor;
                            }
                        }
                        if (greediest != null && greediest.GetParameters().Length > 0) {
                            var parameters = CreateConstructorParameters(greediest, contract.Properties);
                            contract.CreatorParameters.Clear();
                            foreach (var p in parameters) {
                                contract.CreatorParameters.Add(p);
                            }
                            contract.OverrideCreator = args => greediest.Invoke(args);
                        }
                    }
                }
                return contract;
            }

            private static readonly Dictionary<MethodInfo, bool> setterThrowsCache = new Dictionary<MethodInfo, bool>();

            // True for the StarDetectorMetrics computed-count pattern: a setter whose body constructs and throws
            // a NotSupportedException. Detected by scanning the setter IL for a newobj (0x73) whose operand
            // resolves (via the setter's own module) to a NotSupportedException constructor.
            private static bool SetterAlwaysThrowsNotSupported(PropertyInfo pi) {
                var setter = pi.GetSetMethod(nonPublic: true);
                if (setter == null) {
                    return false;
                }

                lock (setterThrowsCache) {
                    if (setterThrowsCache.TryGetValue(setter, out var cached)) {
                        return cached;
                    }
                }

                var result = ScanSetterForNotSupportedThrow(setter);
                lock (setterThrowsCache) {
                    setterThrowsCache[setter] = result;
                }
                return result;
            }

            private static bool ScanSetterForNotSupportedThrow(MethodInfo setter) {
                var il = setter.GetMethodBody()?.GetILAsByteArray();
                if (il == null) {
                    return false;
                }

                var module = setter.Module;
                var generic = setter.DeclaringType?.GetGenericArguments();
                for (int i = 0; i + 4 < il.Length; i++) {
                    if (il[i] != 0x73) {
                        continue; // newobj opcode
                    }

                    var token = BitConverter.ToInt32(il, i + 1);
                    try {
                        var ctor = module.ResolveMethod(token, generic, null);
                        if (ctor?.DeclaringType == typeof(NotSupportedException)) {
                            return true;
                        }
                    } catch {
                        // Token at this offset is not a method ref (operand of a different instruction that
                        // happens to share the 0x73 byte); ignore and keep scanning.
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Serializes a star-detection result for the on-disk cache, preserving derived star subtypes and the
        /// detector version / cache key. Output is indented to match the previous save format.
        /// </summary>
        public static string Serialize(StarDetectionResult result) {
            // Serialize with the declared root type so the root concrete type is known on read without a
            // root-level $type, while per-element $type markers preserve the polymorphic StarList entries.
            return JsonConvert.SerializeObject(result, typeof(HocusFocusStarDetectionResult), Settings);
        }

        /// <summary>
        /// Deserializes a star-detection result previously written by <see cref="Serialize"/>. The returned
        /// instance is a <see cref="HocusFocusStarDetectionResult"/> whose <c>StarList</c> entries are
        /// <see cref="HocusFocusDetectedStar"/>.
        /// </summary>
        public static HocusFocusStarDetectionResult Deserialize(string json) {
            return JsonConvert.DeserializeObject<HocusFocusStarDetectionResult>(json, Settings);
        }
    }
}
