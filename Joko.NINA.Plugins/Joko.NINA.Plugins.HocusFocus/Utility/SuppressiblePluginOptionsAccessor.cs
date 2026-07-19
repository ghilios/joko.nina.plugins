#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// Wraps an <see cref="IPluginOptionsAccessor"/> so profile writes can be suspended (per-filter buffered
    /// edit mode) while designated machine-local keys keep writing through. Reads always forward to the inner
    /// accessor.
    /// </summary>
    internal sealed class SuppressiblePluginOptionsAccessor : IPluginOptionsAccessor {
        private readonly IPluginOptionsAccessor inner;
        private readonly ISet<string> alwaysWriteKeys;

        public SuppressiblePluginOptionsAccessor(IPluginOptionsAccessor inner, ISet<string> alwaysWriteKeys) {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.alwaysWriteKeys = alwaysWriteKeys ?? throw new ArgumentNullException(nameof(alwaysWriteKeys));
        }

        public bool SuppressWrites { get; set; }

        private bool ShouldWrite(string name) => !SuppressWrites || alwaysWriteKeys.Contains(name);

        public Color GetValueColor(string name, Color defaultValue) => inner.GetValueColor(name, defaultValue);

        public void SetValueColor(string name, Color value) {
            if (ShouldWrite(name)) {
                inner.SetValueColor(name, value);
            }
        }

        public T GetValueEnum<T>(string name, T defaultValue) where T : struct, Enum => inner.GetValueEnum(name, defaultValue);

        public void SetValueEnum<T>(string name, T value) where T : struct, Enum {
            if (ShouldWrite(name)) {
                inner.SetValueEnum(name, value);
            }
        }

        public bool GetValueBoolean(string name, bool defaultValue) => inner.GetValueBoolean(name, defaultValue);

        public void SetValueBoolean(string name, bool value) {
            if (ShouldWrite(name)) {
                inner.SetValueBoolean(name, value);
            }
        }

        public byte GetValueByte(string name, byte defaultValue) => inner.GetValueByte(name, defaultValue);

        public void SetValueByte(string name, byte value) {
            if (ShouldWrite(name)) {
                inner.SetValueByte(name, value);
            }
        }

        public sbyte GetValueSByte(string name, sbyte defaultValue) => inner.GetValueSByte(name, defaultValue);

        public void SetValueSByte(string name, sbyte value) {
            if (ShouldWrite(name)) {
                inner.SetValueSByte(name, value);
            }
        }

        public char GetValueChar(string name, char defaultValue) => inner.GetValueChar(name, defaultValue);

        public void SetValueChar(string name, char value) {
            if (ShouldWrite(name)) {
                inner.SetValueChar(name, value);
            }
        }

        public decimal GetValueDecimal(string name, decimal defaultValue) => inner.GetValueDecimal(name, defaultValue);

        public void SetValueDecimal(string name, decimal value) {
            if (ShouldWrite(name)) {
                inner.SetValueDecimal(name, value);
            }
        }

        public double GetValueDouble(string name, double defaultValue) => inner.GetValueDouble(name, defaultValue);

        public void SetValueDouble(string name, double value) {
            if (ShouldWrite(name)) {
                inner.SetValueDouble(name, value);
            }
        }

        public float GetValueSingle(string name, float defaultValue) => inner.GetValueSingle(name, defaultValue);

        public void SetValueSingle(string name, float value) {
            if (ShouldWrite(name)) {
                inner.SetValueSingle(name, value);
            }
        }

        public int GetValueInt32(string name, int defaultValue) => inner.GetValueInt32(name, defaultValue);

        public void SetValueInt32(string name, int value) {
            if (ShouldWrite(name)) {
                inner.SetValueInt32(name, value);
            }
        }

        public uint GetValueUInt32(string name, uint defaultValue) => inner.GetValueUInt32(name, defaultValue);

        public void SetValueUInt32(string name, uint value) {
            if (ShouldWrite(name)) {
                inner.SetValueUInt32(name, value);
            }
        }

        public long GetValueInt64(string name, long defaultValue) => inner.GetValueInt64(name, defaultValue);

        public void SetValueInt64(string name, long value) {
            if (ShouldWrite(name)) {
                inner.SetValueInt64(name, value);
            }
        }

        public ulong GetValueUInt64(string name, ulong defaultValue) => inner.GetValueUInt64(name, defaultValue);

        public void SetValueUInt64(string name, ulong value) {
            if (ShouldWrite(name)) {
                inner.SetValueUInt64(name, value);
            }
        }

        public short GetValueInt16(string name, short defaultValue) => inner.GetValueInt16(name, defaultValue);

        public void SetValueInt16(string name, short value) {
            if (ShouldWrite(name)) {
                inner.SetValueInt16(name, value);
            }
        }

        public ushort GetValueUInt16(string name, ushort defaultValue) => inner.GetValueUInt16(name, defaultValue);

        public void SetValueUInt16(string name, ushort value) {
            if (ShouldWrite(name)) {
                inner.SetValueUInt16(name, value);
            }
        }

        public string GetValueString(string name, string defaultValue) => inner.GetValueString(name, defaultValue);

        public void SetValueString(string name, string value) {
            if (ShouldWrite(name)) {
                inner.SetValueString(name, value);
            }
        }

        public DateTime GetValueDateTime(string name, DateTime defaultValue) => inner.GetValueDateTime(name, defaultValue);

        public void SetValueDateTime(string name, DateTime value) {
            if (ShouldWrite(name)) {
                inner.SetValueDateTime(name, value);
            }
        }

        public Guid GetValueGuid(string name, Guid defaultValue) => inner.GetValueGuid(name, defaultValue);

        public void SetValueGuid(string name, Guid value) {
            if (ShouldWrite(name)) {
                inner.SetValueGuid(name, value);
            }
        }
    }
}
