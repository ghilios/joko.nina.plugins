﻿using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin
[assembly: Guid("0f1d10b6-d306-4168-b751-d454cbac9670")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Hocus Focus")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Improved Star Detection and Star Annotation for NINA")]


// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("George Hilios (jokogeo)")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Hocus Focus")]
[assembly: AssemblyCopyright("Copyright ©  2021")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "1.11.0.1167")]

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
[assembly: AssemblyMetadata("Tags", "StarDetection,AutoFocus")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/ghilios/joko.nina.plugins/commits/develop")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"This plugin improved Star Detection and Star Annotation for NINA. Faster Auto Focus is on the way as well 

*Features*:
* Faster for large sensors, particularly APS-C and Full Frame
* Higher accuracy (lower HFR Standard Deviation) if parameters are set properly
* Advanced control over star detection parameters
* Sophisticated star annotation to simplify parameter tuning
* Can use the new Star Detector or Annotator without requiring both. Keep what you like
* Faster AutoFocus on its way soon
* Eccentricity on its way soon

To enable these features, go to Options -> Imaging -> Image Options. After this plugin is installed, there will be Star Detector and Star Annotator dropdown boxes you can select ""Hocus Focus"" for.
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