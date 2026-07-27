using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

[TestFixture]
public class AutoFocusBinningConflictTests {

    private static IProfileService BuildProfile(short globalBinning, params (string name, short binning)[] filters) {
        var profileService = Substitute.For<IProfileService>();
        var profile = Substitute.For<IProfile>();
        profileService.ActiveProfile.Returns(profile);

        var focuserSettings = Substitute.For<IFocuserSettings>();
        focuserSettings.AutoFocusBinning.Returns(globalBinning);
        // Substitute properties are settable stubs, so ResetToUnbinned's write is observable via the getter.
        focuserSettings.When(x => x.AutoFocusBinning = Arg.Any<short>())
            .Do(call => focuserSettings.AutoFocusBinning.Returns(call.Arg<short>()));
        profile.FocuserSettings.Returns(focuserSettings);

        var filterSettings = Substitute.For<IFilterWheelSettings>();
        var rows = new ObserveAllCollection<FilterInfo>();
        foreach (var (name, binning) in filters) {
            rows.Add(new FilterInfo(name, 0, 0) { AutoFocusBinning = new BinningMode(binning, binning) });
        }
        filterSettings.FilterWheelFilters.Returns(rows);
        profile.FilterWheelSettings.Returns(filterSettings);

        var cameraSettings = Substitute.For<ICameraSettings>();
        cameraSettings.PixelSize.Returns(3.76);
        profile.CameraSettings.Returns(cameraSettings);

        var telescopeSettings = Substitute.For<ITelescopeSettings>();
        telescopeSettings.FocalLength.Returns(2800.0);
        profile.TelescopeSettings.Returns(telescopeSettings);

        return profileService;
    }

    [Test]
    public void Detect_NothingAboveOne_ReportsNoConflict() {
        var conflict = AutoFocusBinningConflict.Detect(BuildProfile(1, ("L", 1), ("R", 1)));
        Assert.Multiple(() => {
            Assert.That(conflict.HasConflict, Is.False);
            Assert.That(conflict.FilterNames, Is.Empty);
        });
    }

    [Test]
    public void Detect_GlobalOnly_ReportsTheGlobalValue() {
        var conflict = AutoFocusBinningConflict.Detect(BuildProfile(2, ("L", 1)));
        Assert.Multiple(() => {
            Assert.That(conflict.HasConflict, Is.True);
            Assert.That(conflict.GlobalBinning, Is.EqualTo(2));
            Assert.That(conflict.FilterNames, Is.Empty);
        });
    }

    [Test]
    public void Detect_PerFilterOnly_ReportsThoseFilters() {
        // A per-filter override wins over the global value, so a filter above 1x1 is a conflict even when the
        // global setting is clean — otherwise that filter silently keeps capturing binned.
        var conflict = AutoFocusBinningConflict.Detect(BuildProfile(1, ("L", 1), ("Ha", 2), ("OIII", 3)));
        Assert.Multiple(() => {
            Assert.That(conflict.HasConflict, Is.True);
            Assert.That(conflict.GlobalBinning, Is.EqualTo(1));
            Assert.That(conflict.FilterNames, Is.EqualTo(new[] { "Ha", "OIII" }));
        });
    }

    [Test]
    public void Describe_ListsExactlyWhatWouldBeReset() {
        var conflict = AutoFocusBinningConflict.Detect(BuildProfile(2, ("L", 1), ("Ha", 2)));
        var text = conflict.Describe(detectionBinning: 2);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("2x2"));
            Assert.That(text, Does.Contain("Ha"));
            Assert.That(text, Does.Not.Contain("\"L\""), "clean filters must not be listed");
            Assert.That(text, Does.Contain("4x the native pixel size"), "the combined factor is the point of the warning");
            Assert.That(text, Does.EndWith("Set these back to 1x1?"));
        });
    }

    [Test]
    public void ResetToUnbinned_ClearsTheGlobalAndTheNamedFiltersOnly() {
        var profileService = BuildProfile(2, ("L", 1), ("Ha", 2), ("OIII", 4));
        var conflict = AutoFocusBinningConflict.Detect(profileService);

        conflict.ResetToUnbinned(profileService);

        var profile = profileService.ActiveProfile;
        Assert.Multiple(() => {
            Assert.That(profile.FocuserSettings.AutoFocusBinning, Is.EqualTo((short)1));
            foreach (var filter in profile.FilterWheelSettings.FilterWheelFilters) {
                Assert.That(filter.AutoFocusBinning.X, Is.EqualTo((short)1), $"filter {filter.Name}");
                Assert.That(filter.AutoFocusBinning.Y, Is.EqualTo((short)1), $"filter {filter.Name}");
            }
        });
    }

    // ---- The options-side trigger ------------------------------------------------------------------------

    private static (StarDetectionOptions options, List<(AutoFocusBinningConflict conflict, int binning)> prompts)
        BuildOptions(IProfileService profileService) {
        var options = new StarDetectionOptions(profileService, new InMemoryPluginOptionsAccessor());
        var prompts = new List<(AutoFocusBinningConflict, int)>();
        options.AutoFocusBinningConflictHandler = (conflict, binning) => prompts.Add((conflict, binning));
        return (options, prompts);
    }

    [Test]
    public void SettingDetectionBinning_WithAConflict_RaisesThePrompt() {
        var (options, prompts) = BuildOptions(BuildProfile(2, ("L", 1)));

        options.DetectionBinning = DetectionBinningEnum.Bin2;

        Assert.That(prompts, Has.Count.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(prompts[0].binning, Is.EqualTo(2));
            Assert.That(prompts[0].conflict.GlobalBinning, Is.EqualTo((short)2));
        });
    }

    [Test]
    public void SettingDetectionBinning_WithoutAConflict_StaysSilent() {
        var (options, prompts) = BuildOptions(BuildProfile(1, ("L", 1)));
        options.DetectionBinning = DetectionBinningEnum.Bin2;
        Assert.That(prompts, Is.Empty);
    }

    [Test]
    public void SettingDetectionBinningToOne_StaysSilentEvenWithAConflict() {
        // Only STACKING is worth warning about; turning detection binning off is never a conflict.
        var (options, prompts) = BuildOptions(BuildProfile(2, ("L", 1)));
        options.DetectionBinning = DetectionBinningEnum.Bin1;
        Assert.That(prompts, Is.Empty);
    }

    [Test]
    public void ThePromptCarriesTheFactorTheUserActuallyChose() {
        // The warning is about STACKING, so it must quote the factor that will stack, not a derived one.
        var (options, prompts) = BuildOptions(BuildProfile(2, ("L", 1)));
        options.DetectionBinning = DetectionBinningEnum.Bin3;
        Assert.That(prompts, Has.Count.EqualTo(1));
        Assert.That(prompts[0].binning, Is.EqualTo(3));
        Assert.That(prompts[0].conflict.Describe(prompts[0].binning), Does.Contain("6x the native pixel size"));
    }

    [Test]
    public void BulkApplies_NeverPrompt() {
        var (options, prompts) = BuildOptions(BuildProfile(2, ("L", 1)));

        // Import / replay / copy-from-filter go through ApplyFullSnapshot, and ResetDefaults restores every knob.
        // Neither is a user deciding to bin, so neither may pop a dialog.
        var source = NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay.StarDetectionSettingsSnapshot.FromOptions(options);
        source.DetectionBinning = DetectionBinningEnum.Bin4;
        options.ApplyFullSnapshot(source);
        Assert.That(options.DetectionBinning, Is.EqualTo(DetectionBinningEnum.Bin4), "the value is still applied");

        options.DetectionBinning = DetectionBinningEnum.Bin1;
        options.ResetDefaults();

        Assert.That(prompts, Is.Empty);
    }

    [Test]
    public void BufferedPerFilterEdits_DoNotPrompt() {
        var (options, prompts) = BuildOptions(BuildProfile(2, ("L", 1)));
        options.PersistToProfile = false;
        options.DetectionBinning = DetectionBinningEnum.Bin3;
        Assert.That(prompts, Is.Empty, "a per-filter edit buffer is not the user changing their live settings");
    }
}
