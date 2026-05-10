using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using Color = System.Windows.Media.Color;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles {

    public sealed class InMemoryPluginOptionsAccessor : IPluginOptionsAccessor {
        private readonly Dictionary<string, object> store = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, object> Snapshot => store;

        public int WriteCount { get; private set; }

        public int ReadCount { get; private set; }

        public void Clear() => store.Clear();

        private T Get<T>(string key, T defaultValue) {
            ReadCount++;
            if (store.TryGetValue(key, out var value) && value is T typed) {
                return typed;
            }
            return defaultValue;
        }

        private void Set<T>(string key, T value) {
            WriteCount++;
            store[key] = value;
        }

        public bool GetValueBoolean(string key, bool defaultValue) => Get(key, defaultValue);

        public void SetValueBoolean(string key, bool value) => Set(key, value);

        public byte GetValueByte(string key, byte defaultValue) => Get(key, defaultValue);

        public void SetValueByte(string key, byte value) => Set(key, value);

        public sbyte GetValueSByte(string key, sbyte defaultValue) => Get(key, defaultValue);

        public void SetValueSByte(string key, sbyte value) => Set(key, value);

        public char GetValueChar(string key, char defaultValue) => Get(key, defaultValue);

        public void SetValueChar(string key, char value) => Set(key, value);

        public decimal GetValueDecimal(string key, decimal defaultValue) => Get(key, defaultValue);

        public void SetValueDecimal(string key, decimal value) => Set(key, value);

        public double GetValueDouble(string key, double defaultValue) => Get(key, defaultValue);

        public void SetValueDouble(string key, double value) => Set(key, value);

        public float GetValueSingle(string key, float defaultValue) => Get(key, defaultValue);

        public void SetValueSingle(string key, float value) => Set(key, value);

        public int GetValueInt32(string key, int defaultValue) => Get(key, defaultValue);

        public void SetValueInt32(string key, int value) => Set(key, value);

        public uint GetValueUInt32(string key, uint defaultValue) => Get(key, defaultValue);

        public void SetValueUInt32(string key, uint value) => Set(key, value);

        public long GetValueInt64(string key, long defaultValue) => Get(key, defaultValue);

        public void SetValueInt64(string key, long value) => Set(key, value);

        public ulong GetValueUInt64(string key, ulong defaultValue) => Get(key, defaultValue);

        public void SetValueUInt64(string key, ulong value) => Set(key, value);

        public short GetValueInt16(string key, short defaultValue) => Get(key, defaultValue);

        public void SetValueInt16(string key, short value) => Set(key, value);

        public ushort GetValueUInt16(string key, ushort defaultValue) => Get(key, defaultValue);

        public void SetValueUInt16(string key, ushort value) => Set(key, value);

        public string GetValueString(string key, string defaultValue) => Get(key, defaultValue);

        public void SetValueString(string key, string value) => Set(key, value);

        public DateTime GetValueDateTime(string key, DateTime defaultValue) => Get(key, defaultValue);

        public void SetValueDateTime(string key, DateTime value) => Set(key, value);

        public Guid GetValueGuid(string key, Guid defaultValue) => Get(key, defaultValue);

        public void SetValueGuid(string key, Guid value) => Set(key, value);

        public Color GetValueColor(string key, Color defaultValue) => Get(key, defaultValue);

        public void SetValueColor(string key, Color value) => Set(key, value);

        public T GetValueEnum<T>(string key, T defaultValue) where T : struct, Enum => Get(key, defaultValue);

        public void SetValueEnum<T>(string key, T value) where T : struct, Enum => Set(key, value);
    }
}
