using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class SuppressiblePluginOptionsAccessorTests {

        private static (SuppressiblePluginOptionsAccessor accessor, InMemoryPluginOptionsAccessor inner) Build(params string[] alwaysWriteKeys) {
            var inner = new InMemoryPluginOptionsAccessor();
            var accessor = new SuppressiblePluginOptionsAccessor(inner, new HashSet<string>(alwaysWriteKeys, StringComparer.Ordinal));
            return (accessor, inner);
        }

        [Test]
        public void Constructor_NullArguments_Throw() {
            Assert.Throws<ArgumentNullException>(() => new SuppressiblePluginOptionsAccessor(null, new HashSet<string>()));
            Assert.Throws<ArgumentNullException>(() => new SuppressiblePluginOptionsAccessor(new InMemoryPluginOptionsAccessor(), null));
        }

        [Test]
        public void Writes_ForwardByDefault() {
            var (accessor, inner) = Build();
            accessor.SetValueBoolean("b", true);
            accessor.SetValueInt32("i", 42);
            accessor.SetValueDouble("d", 1.5);
            accessor.SetValueString("s", "hello");
            accessor.SetValueEnum("e", DayOfWeek.Friday);
            Assert.Multiple(() => {
                Assert.That(accessor.SuppressWrites, Is.False);
                Assert.That(inner.GetValueBoolean("b", false), Is.True);
                Assert.That(inner.GetValueInt32("i", -1), Is.EqualTo(42));
                Assert.That(inner.GetValueDouble("d", -1.0), Is.EqualTo(1.5));
                Assert.That(inner.GetValueString("s", null), Is.EqualTo("hello"));
                Assert.That(inner.GetValueEnum("e", DayOfWeek.Monday), Is.EqualTo(DayOfWeek.Friday));
            });
        }

        [Test]
        public void SuppressWrites_DropsNonAlwaysKeyWrites() {
            var (accessor, inner) = Build();
            accessor.SetValueInt32("i", 42);

            accessor.SuppressWrites = true;
            accessor.SetValueInt32("i", 99);
            accessor.SetValueBoolean("b", true);

            Assert.Multiple(() => {
                Assert.That(inner.GetValueInt32("i", -1), Is.EqualTo(42));
                Assert.That(inner.Snapshot.ContainsKey("b"), Is.False);
            });
        }

        [Test]
        public void SuppressWrites_AlwaysWriteKeysStillForward() {
            var (accessor, inner) = Build("DetectionDebugMode", "PSFParallelPartitionSize");
            accessor.SuppressWrites = true;

            accessor.SetValueBoolean("DetectionDebugMode", true);
            accessor.SetValueInt32("PSFParallelPartitionSize", 250);
            accessor.SetValueInt32("NoiseReductionRadius", 9);

            Assert.Multiple(() => {
                Assert.That(inner.GetValueBoolean("DetectionDebugMode", false), Is.True);
                Assert.That(inner.GetValueInt32("PSFParallelPartitionSize", -1), Is.EqualTo(250));
                Assert.That(inner.Snapshot.ContainsKey("NoiseReductionRadius"), Is.False);
            });
        }

        [Test]
        public void SuppressWrites_ReadsStillForward() {
            var (accessor, inner) = Build();
            inner.SetValueInt32("i", 42);
            inner.SetValueString("s", "x");

            accessor.SuppressWrites = true;

            Assert.Multiple(() => {
                Assert.That(accessor.GetValueInt32("i", -1), Is.EqualTo(42));
                Assert.That(accessor.GetValueString("s", null), Is.EqualTo("x"));
                Assert.That(accessor.GetValueBoolean("missing", true), Is.True);
            });
        }

        [Test]
        public void SuppressWrites_ToggledBackOff_ResumesWrites() {
            var (accessor, inner) = Build();
            accessor.SuppressWrites = true;
            accessor.SetValueInt32("i", 99);
            accessor.SuppressWrites = false;
            accessor.SetValueInt32("i", 7);
            Assert.That(inner.GetValueInt32("i", -1), Is.EqualTo(7));
        }

        // Exercises every typed setter of the IPluginOptionsAccessor surface exactly once (18 total).
        private static void InvokeEveryTypedSetter(SuppressiblePluginOptionsAccessor accessor) {
            accessor.SetValueColor("k", Colors.Red);
            accessor.SetValueEnum("k", DayOfWeek.Monday);
            accessor.SetValueBoolean("k", true);
            accessor.SetValueByte("k", (byte)1);
            accessor.SetValueSByte("k", (sbyte)-1);
            accessor.SetValueChar("k", 'x');
            accessor.SetValueDecimal("k", 1m);
            accessor.SetValueDouble("k", 1.0);
            accessor.SetValueSingle("k", 1f);
            accessor.SetValueInt32("k", 1);
            accessor.SetValueUInt32("k", 1u);
            accessor.SetValueInt64("k", 1L);
            accessor.SetValueUInt64("k", 1ul);
            accessor.SetValueInt16("k", (short)1);
            accessor.SetValueUInt16("k", (ushort)1);
            accessor.SetValueString("k", "v");
            accessor.SetValueDateTime("k", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            accessor.SetValueGuid("k", Guid.NewGuid());
        }

        [Test]
        public void EveryTypedSetter_SuppressedWhenSuppressWritesOn() {
            var (accessor, inner) = Build();
            accessor.SuppressWrites = true;
            InvokeEveryTypedSetter(accessor);
            Assert.That(inner.WriteCount, Is.EqualTo(0));
        }

        [Test]
        public void EveryTypedSetter_ForwardsWhenSuppressWritesOff() {
            var (accessor, inner) = Build();
            InvokeEveryTypedSetter(accessor);
            Assert.That(inner.WriteCount, Is.EqualTo(18));
        }
    }
}
