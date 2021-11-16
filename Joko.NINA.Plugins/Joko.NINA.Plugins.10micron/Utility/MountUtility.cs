#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ASCOM.DriverAccess;
using ASCOM.Utilities;
using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Joko.NINA.Plugins.TenMicron.Utility {

    public static class MountUtility {

        private static ISet<string> SupportedProducts = ImmutableHashSet.CreateRange(
            new[] {
                "10micron GM1000HPS",
                "10micron GM2000QCI",
                "10micron GM2000HPS",
                "10micron GM3000HPS",
                "10micron GM4000QCI",
                "10micron GM4000QCI 48V",
                "10micron GM4000HPS",
                "10micron AZ2000",
                "10micron AZ2000HPS",
                "10micron AZ4000HPS"
            });

        public static bool IsSupportedProduct(ProductFirmware productFirmware) {
            return SupportedProducts.Contains(productFirmware.ProductName);
        }

        private static double GetASCOMProfileDouble(Profile profile, string driverId, string name, string subkey, double defaultvalue) {
            if (double.TryParse(profile.GetValue(driverId, name, subkey, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)) {
                return result;
            }
            return defaultvalue;
        }

        private static bool GetASCOMProfileBool(Profile profile, string driverId, string name, string subkey, bool defaultvalue) {
            if (bool.TryParse(profile.GetValue(driverId, name, subkey, ""), out var result)) {
                return result;
            }
            return defaultvalue;
        }

        private static string GetASCOMProfileString(Profile profile, string driverId, string name, string subkey, string defaultvalue) {
            return profile.GetValue(driverId, name, subkey, defaultvalue);
        }

        public static MountAscomConfig GetMountAscomConfig(string driverId) {
            var profile = new Profile();
            profile.DeviceType = nameof(Telescope);
            if (profile.IsRegistered(driverId)) {
                var ascomProfile = profile.GetProfile(driverId);
                var profileJson = JsonConvert.SerializeObject(ascomProfile.ProfileValues);
                Logger.Info($"10u ASCOM driver configuration: {profileJson}");

                if (driverId == "ASCOM.tenmicron_mount.Telescope") {
                    return new MountAscomConfig() {
                        EnableUncheckedRawCommands = GetASCOMProfileBool(profile, driverId, "enable_unchecked_raw_commands", "mount_settings", true),
                        UseJ2000Coordinates = GetASCOMProfileBool(profile, driverId, "use_J2000_coords", "mount_settings", false),
                        EnableSync = GetASCOMProfileBool(profile, driverId, "enable_sync", "mount_settings", false),
                        UseSyncAsAlignment = GetASCOMProfileBool(profile, driverId, "use_sync_as_alignment", "mount_settings", false),
                        RefractionUpdateFile = GetASCOMProfileString(profile, driverId, "refraction_update_file", "mount_settings", "")
                    };
                }
            }
            return null;
        }

        public static bool ValidateMountAscomConfig(MountAscomConfig config) {
            if (config.EnableUncheckedRawCommands) {
                Notification.ShowError("Enable Unchecked Raw Commands cannnot be enabled. Open the ASCOM driver configuration and disable it, then reconnect");
                return false;
            }
            if (config.EnableSync && config.UseSyncAsAlignment) {
                Notification.ShowWarning("Use Sync as Alignment is enabled. It is recommended you disable this setting and build models explicitly");
            }
            if (config.UseJ2000Coordinates) {
                Notification.ShowWarning("ASCOM driver is configured to use J2000 coordinates. It is recommended you use JNow instead and reconnect");
            }
            return true;
        }
    }
}