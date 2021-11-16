#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using System;
using System.Linq;

namespace TenMicronTestApp {

    internal class Program {

        // TODO List:
        //  * Add trigger to push refraction updates. The built in source is at:
        //         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Temp\\10micronRefraction.txt";
        //         Path.GetTempPath() + "\\10micronRefraction.txt";
        private static void Main(string[] args) {
            /*
            string id = ASCOM.DriverAccess.Telescope.Choose("");
            if (string.IsNullOrEmpty(id))
                return;
            */

            // create this device
            ASCOM.DriverAccess.Telescope device = new ASCOM.DriverAccess.Telescope("ASCOM.tenmicron_mount.Telescope");
            device.Connected = true;

            var mountCommander = new AscomMountCommander(device);
            var mount = new Mount(mountCommander);
            var productFirmware = mount.GetProductFirmware();
            var mountId = mount.GetId();
            var alignmentModelInfo = mount.GetAlignmentModelInfo();
            var declination = mount.GetDeclination();
            var rightAscension = mount.GetRightAscension();
            var sideOfPier = mount.GetSideOfPier();
            var lst = mount.GetLocalSiderealTime();
            var modelCount = mount.GetModelCount();
            var modelNames = Enumerable.Range(1, modelCount).Select(i => mount.GetModelName(i)).ToList();
            var alignmentStarCount = mount.GetAlignmentStarCount();
            var alignmentStars = Enumerable.Range(1, alignmentStarCount).Select(i => mount.GetAlignmentStarInfo(i)).ToList();
            Console.WriteLine("Complete");
        }
    }
}