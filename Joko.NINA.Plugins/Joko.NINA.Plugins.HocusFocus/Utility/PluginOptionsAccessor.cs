using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Joko.NINA.Plugins.HocusFocus.Utility {
    public class PluginOptionsAccessor {
        private readonly IProfileService profileService;
        private readonly Guid pluginGuid;

        public PluginOptionsAccessor(IProfileService profileService, Guid pluginGuid) {
            this.profileService = profileService;
            this.pluginGuid = pluginGuid;
        }

        public static Guid? GetAssemblyGuid(Type type) {
            var guidAttributes = type.Assembly.GetCustomAttributes(typeof(GuidAttribute), false);
            if (guidAttributes == null || guidAttributes.Length != 1) {
                return null;
            }
            return Guid.Parse(((GuidAttribute)guidAttributes[0]).Value);
        }

        public bool GetValueBool(string name, bool defaultValue) {
            if (profileService.ActiveProfile.PluginSettings.TryGetValue(pluginGuid, name, out bool result)) {
                return result;
            }
            return defaultValue;
        }

        public int GetValueInt(string name, int defaultValue) {
            if (profileService.ActiveProfile.PluginSettings.TryGetValue(pluginGuid, name, out int result)) {
                return result;
            }
            return defaultValue;
        }

        public float GetValueFloat(string name, float defaultValue) {
            if (profileService.ActiveProfile.PluginSettings.TryGetValue(pluginGuid, name, out float result)) {
                return result;
            }
            return defaultValue;
        }

        public double GetValueDouble(string name, double defaultValue) {
            if (profileService.ActiveProfile.PluginSettings.TryGetValue(pluginGuid, name, out double result)) {
                return result;
            }
            return defaultValue;
        }

        public string GetValueString(string name, string defaultValue) {
            if (profileService.ActiveProfile.PluginSettings.TryGetValue(pluginGuid, name, out string result)) {
                return result;
            }
            return defaultValue;
        }

        public Color GetValueColor(string name, Color defaultValue) {
            if (profileService.ActiveProfile.PluginSettings.TryGetValue(pluginGuid, name, out int result)) {
                return IntToColor(result);
            }
            return defaultValue;
        }

        public T GetValueEnum<T>(string name, T defaultValue) where T : struct, Enum {
            if (profileService.ActiveProfile.PluginSettings.TryGetValue(pluginGuid, name, out string resultString)) {
                if (Enum.TryParse<T>(resultString, out var result)) {
                    return result;
                }
            }
            return defaultValue;            
        }

        private static int ColorToInt(Color color) {
            return color.A << 24 | color.R << 16 | color.G << 8 | color.B;
        }

        private static Color IntToColor(int colorInt) {
            byte a = (byte)(colorInt >> 24);
            byte r = (byte)(colorInt >> 16);
            byte g = (byte)(colorInt >> 8);
            byte b = (byte)(colorInt);
            return Color.FromArgb(a, r, g, b);
        }

        public void SetValueBool(string name, bool value) {
            profileService.ActiveProfile.PluginSettings.SetValue(pluginGuid, name, value);
        }

        public void SetValueInt(string name, int value) {
            profileService.ActiveProfile.PluginSettings.SetValue(pluginGuid, name, value);
        }

        public void SetValueDouble(string name, double value) {
            profileService.ActiveProfile.PluginSettings.SetValue(pluginGuid, name, value);
        }

        public void SetValueString(string name, string value) {
            profileService.ActiveProfile.PluginSettings.SetValue(pluginGuid, name, value);
        }

        public void SetValueFloat(string name, float value) {
            profileService.ActiveProfile.PluginSettings.SetValue(pluginGuid, name, value);
        }

        public void SetValueColor(string name, Color value) {
            profileService.ActiveProfile.PluginSettings.SetValue(pluginGuid, name, ColorToInt(value));
        }

        public void SetValueEnum<T>(string name, T value) where T : struct, Enum {
            profileService.ActiveProfile.PluginSettings.SetValue(pluginGuid, name, Enum.GetName(typeof(T), value));
        }
    }
}
