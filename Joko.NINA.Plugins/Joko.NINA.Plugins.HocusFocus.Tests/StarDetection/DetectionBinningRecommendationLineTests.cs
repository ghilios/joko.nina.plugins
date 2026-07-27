using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

/// <summary>
/// The options page's detection-binning line, end to end over the real options object and the real measurement
/// record — the three states the UI can be in, and the fact that a new measurement refreshes the line.
/// </summary>
[TestFixture]
public class DetectionBinningRecommendationLineTests {

    private static (StarDetectionOptions options, InFocusHfrRecord record) Build() {
        var profile = Substitute.For<IProfileService>();
        var record = new InFocusHfrRecord(new InMemoryPluginOptionsAccessor());
        var options = new StarDetectionOptions(profile, new InMemoryPluginOptionsAccessor(), record);
        return (options, record);
    }

    [Test]
    public void NoMeasurement_AsksForAnAutoFocus() {
        var (options, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.DetectionBinningRecommendationVisible, Is.True);
            Assert.That(options.DetectionBinningHint, Is.EqualTo("Run an auto-focus to get a recommendation"));
        });
    }

    [Test]
    public void MeasurementAgrees_HidesTheLineEntirely() {
        var (options, record) = Build();
        record.Record(3.6, DateTime.UtcNow, "test");
        Assert.Multiple(() => {
            Assert.That(options.DetectionBinningRecommendationVisible, Is.False, "nothing to act on at 1x1 with 3.6 px stars");
            Assert.That(options.DetectionBinningHint, Is.EqualTo("Measured in-focus HFR 3.6 px - 1x1 is right"));
        });
    }

    [Test]
    public void MeasurementDisagrees_AsksForTheFactorChange() {
        var (options, record) = Build();
        record.Record(6.1, DateTime.UtcNow, "test");
        Assert.Multiple(() => {
            Assert.That(options.DetectionBinningRecommendationVisible, Is.True);
            Assert.That(options.DetectionBinningHint, Is.EqualTo("Measured in-focus HFR 6.1 px - 2x2 recommended"));
        });
    }

    [Test]
    public void ANewMeasurement_RefreshesTheLine() {
        var (options, record) = Build();
        var raised = 0;
        options.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(StarDetectionOptions.DetectionBinningHint)) { raised++; } };

        record.Record(6.1, DateTime.UtcNow, "test");

        Assert.That(raised, Is.GreaterThan(0), "an auto-focus finishing must refresh the recommendation");
        Assert.That(options.DetectionBinningHint, Does.Contain("6.1 px"));
    }

    [Test]
    public void ChangingTheFactorToTheRecommendation_HidesTheLine() {
        var (options, record) = Build();
        record.Record(6.1, DateTime.UtcNow, "test");
        Assert.That(options.DetectionBinningRecommendationVisible, Is.True);

        options.DetectionBinning = DetectionBinningEnum.Bin2;

        Assert.Multiple(() => {
            Assert.That(options.DetectionBinningRecommendationVisible, Is.False, "acting on it makes it go away");
            Assert.That(options.DetectionBinningHint, Is.EqualTo("Measured in-focus HFR 6.1 px - 2x2 is right"));
        });
    }
}
