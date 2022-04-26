#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Reflection;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin
[assembly: Guid("0f1d10b6-d306-4168-b751-d454cbac9670")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("1.11.0.42")]
[assembly: AssemblyFileVersion("1.11.0.42")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Hocus Focus")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Improved Star Detection, Star Annotation, Auto Focus, and Tilt Correction for NINA")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("George Hilios (jokogeo)")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Hocus Focus")]
[assembly: AssemblyCopyright("Copyright ©  2022")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "2.0.0.2059")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://github.com/ghilios/joko.nina.plugins")]

// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "StarDetection,AutoFocus,Tilt,Aberration,BackFocus,Sensor,Curvature")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/ghilios/joko.nina.plugins/commits/develop")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"This plugin improves Star Detection, Star Annotation, and Auto Focus for NINA. It also includes an aberration inspector that measures backfocus and sensor tilt errors.

*Special thanks to Frank Freestar8n, Ph.D. Optical Sciences, for his guidance and expertise creating the Sensor Model in the Aberration Inspector*

Check out his website at [https://www.smallstarspot.com](https://www.smallstarspot.com)

**Features**

*AutoFocus*

* Can be faster than the built-in NINA Auto Focuser by analyzing exposures while next focus points are being exposed
* Supports saving AF runs (images, and annotated star detection) to replay them later with different settings
* The concurrent Auto Focus is particularly useful when using the Hocus Focus star detector, which can be more resource intensive

*Aberration Inspector*

* Generates a full sensor tilt and curvature model, enabling measurement of backfocus error even in the presence of tilt
* Estimates backfocus and tilt errors by doing an Auto Focus and computing AF curves for the center and corner regions of the sensor split into a 3x3 grid
* Generates FWHM Contour Maps and Eccentricity Vector Fields for single exposures for quick visualizations
* Supports replaying saved AF runs
* Hocus Focus must be selected for both Auto Focus and Star Detection
* FWHM Contour Map
* 3D visualization of sensor tilt

*Improved star detection*

* Fit PSFs to stars using Gaussian and Moffat 4. If enabled, fit failures lead to rejected stars and a better HFR calculation
* Eccentricity and FWHM calculation when PSF modeling is enabled
* Simpler configuration, with an advanced mode for fine tuning
* Higher accuracy (lower HFR Standard Deviation) if parameters are set properly
* Advanced control over star detection parameters
* Can use the new Star Detector or Annotator without requiring both. Keep what you like

*Customizable Star Annotation*
* Customizable colors and fonts for star annotations
* Dynamic reloading of star annotations without needing to re-run star detection

To enable these features, go to Options -> Imaging -> Image Options. After this plugin is installed, there will be Star Annotator and Auto Focus dropdown boxes you can select ""Hocus Focus"" for.

**Features coming soon**

*AutoFocus*

* Fast focus mode when still close to a recent auto focus that can utilize fewer steps, smaller step sizes, and a different fitting as the regular ""blind"" focus
* Incorporation of measurement error into quality measurement of focusing results
* Focusing individual color channels instead of just luminance for OSC, since that is skewed towards green focus

# Getting Help #

If you have questions, come ask in the **#plugin-discussions** channel on the NINA [Discord chat server](https://discord.com/invite/rWRbVbw).
* Hocus Focus is provided 'as is' under the terms of the [Mozilla Public License 2.0](https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/blob/develop/LICENSE.txt)
* Source code for this plugin is available at this plugin's [source code repository](https://github.com/ghilios/joko.nina.plugins)
")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]