using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

[TestFixture]
public class StarAnnotatorOptionsTests {

    private static (StarAnnotatorOptions options, InMemoryPluginOptionsAccessor store, IProfileService profile) Build() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new StarAnnotatorOptions(profile, store);
        return (options, store, profile);
    }

    [Test]
    public void Defaults_AreLoadedFromAccessor() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.ShowAnnotations, Is.True);
            Assert.That(options.ShowAnnotationsDuringAutoFocus, Is.True);
            Assert.That(options.ShowAllStars, Is.True);
            Assert.That(options.MaxStars, Is.EqualTo(200));
            Assert.That(options.ShowStarBounds, Is.True);
            Assert.That(options.StarBoundsColor, Is.EqualTo(Color.FromArgb(128, 255, 0, 0)));
            Assert.That(options.ShowAnnotationType, Is.EqualTo(ShowAnnotationTypeEnum.HFR));
            Assert.That(options.AnnotationFontFamily.FamilyNames.Values, Does.Contain("Arial"));
            Assert.That(options.AnnotationFontSizePoints, Is.EqualTo(18f));
            Assert.That(options.AnnotationColor, Is.EqualTo(Color.FromArgb(255, 255, 255, 0)));
            Assert.That(options.StarBoundsType, Is.EqualTo(StarBoundsTypeEnum.Box));
            Assert.That(options.ShowROI, Is.True);
            Assert.That(options.ROIColor, Is.EqualTo(Color.FromArgb(255, 255, 255, 0)));
            Assert.That(options.ShowStarCenter, Is.True);
            Assert.That(options.StarCenterColor, Is.EqualTo(Color.FromArgb(128, 0, 0, 255)));
            Assert.That(options.ShowTooDistorted, Is.False);
            Assert.That(options.ShowDegenerate, Is.False);
            Assert.That(options.ShowSaturated, Is.False);
            Assert.That(options.ShowLowSensitivity, Is.False);
            Assert.That(options.ShowNotCentered, Is.False);
            Assert.That(options.ShowTooFlat, Is.False);
            Assert.That(options.ShowStructureMap, Is.EqualTo(ShowStructureMapEnum.None));
        });
    }

    [Test]
    public void Setters_PersistToAccessor() {
        var (options, store, _) = Build();
        options.ShowAnnotations = false;
        options.ShowAnnotationsDuringAutoFocus = false;
        options.ShowAllStars = false;
        options.MaxStars = 50;
        options.ShowStarBounds = false;
        options.StarBoundsColor = Color.FromRgb(10, 20, 30);
        options.ShowAnnotationType = ShowAnnotationTypeEnum.Eccentricity;
        options.AnnotationFontFamily = new FontFamily("Tahoma");
        options.AnnotationFontSizePoints = 24f;
        options.AnnotationColor = Color.FromRgb(40, 50, 60);
        options.StarBoundsType = StarBoundsTypeEnum.Ellipse;
        options.ShowROI = false;
        options.ROIColor = Color.FromRgb(70, 80, 90);
        options.ShowStarCenter = false;
        options.StarCenterColor = Color.FromRgb(100, 110, 120);
        options.ShowTooDistorted = true;
        options.TooDistortedColor = Color.FromRgb(130, 140, 150);
        options.ShowDegenerate = true;
        options.DegenerateColor = Color.FromRgb(160, 170, 180);
        options.ShowSaturated = true;
        options.SaturatedColor = Color.FromRgb(190, 200, 210);
        options.ShowLowSensitivity = true;
        options.LowSensitivityColor = Color.FromRgb(220, 230, 240);
        options.ShowNotCentered = true;
        options.NotCenteredColor = Color.FromRgb(250, 5, 15);
        options.ShowTooFlat = true;
        options.TooFlatColor = Color.FromRgb(25, 35, 45);
        options.ShowStructureMap = ShowStructureMapEnum.Original;
        options.StructureMapColor = Color.FromRgb(55, 65, 75);

        Assert.Multiple(() => {
            Assert.That(store.Snapshot["ShowAnnotations"], Is.False);
            Assert.That(store.Snapshot["ShowAnnotationsDuringAutoFocus"], Is.False);
            Assert.That(store.Snapshot["ShowAllStars"], Is.False);
            Assert.That(store.Snapshot["MaxStars"], Is.EqualTo(50));
            Assert.That(store.Snapshot["ShowStarBounds"], Is.False);
            Assert.That(store.Snapshot["StarBoundsColor"], Is.EqualTo(Color.FromRgb(10, 20, 30)));
            Assert.That(store.Snapshot["ShowAnnotationType"], Is.EqualTo(ShowAnnotationTypeEnum.Eccentricity));
            Assert.That(store.Snapshot["AnnotationFontFamily"], Is.EqualTo("Tahoma"));
            Assert.That(store.Snapshot["AnnotationFontSizePoints"], Is.EqualTo(24f));
            Assert.That(store.Snapshot["AnnotationColor"], Is.EqualTo(Color.FromRgb(40, 50, 60)));
            Assert.That(store.Snapshot["StarBoundsType"], Is.EqualTo(StarBoundsTypeEnum.Ellipse));
            Assert.That(store.Snapshot["ShowROI"], Is.False);
            Assert.That(store.Snapshot["ROIColor"], Is.EqualTo(Color.FromRgb(70, 80, 90)));
            Assert.That(store.Snapshot["ShowStarCenter"], Is.False);
            Assert.That(store.Snapshot["StarCenterColor"], Is.EqualTo(Color.FromRgb(100, 110, 120)));
            Assert.That(store.Snapshot["ShowTooDistorted"], Is.True);
            Assert.That(store.Snapshot["TooDistortedColor"], Is.EqualTo(Color.FromRgb(130, 140, 150)));
            Assert.That(store.Snapshot["ShowDegenerate"], Is.True);
            Assert.That(store.Snapshot["DegenerateColor"], Is.EqualTo(Color.FromRgb(160, 170, 180)));
            Assert.That(store.Snapshot["ShowSaturated"], Is.True);
            Assert.That(store.Snapshot["SaturatedColor"], Is.EqualTo(Color.FromRgb(190, 200, 210)));
            Assert.That(store.Snapshot["ShowLowSensitivity"], Is.True);
            Assert.That(store.Snapshot["LowSensitivityColor"], Is.EqualTo(Color.FromRgb(220, 230, 240)));
            Assert.That(store.Snapshot["ShowNotCentered"], Is.True);
            Assert.That(store.Snapshot["NotCenteredColor"], Is.EqualTo(Color.FromRgb(250, 5, 15)));
            Assert.That(store.Snapshot["ShowTooFlat"], Is.True);
            Assert.That(store.Snapshot["TooFlatColor"], Is.EqualTo(Color.FromRgb(25, 35, 45)));
            Assert.That(store.Snapshot["ShowStructureMap"], Is.EqualTo(ShowStructureMapEnum.Original));
            Assert.That(store.Snapshot["StructureMapColor"], Is.EqualTo(Color.FromRgb(55, 65, 75)));
        });
    }

    [TestCase(nameof(StarAnnotatorOptions.ShowAnnotations), false)]
    [TestCase(nameof(StarAnnotatorOptions.ShowAnnotationsDuringAutoFocus), false)]
    [TestCase(nameof(StarAnnotatorOptions.ShowAllStars), false)]
    [TestCase(nameof(StarAnnotatorOptions.MaxStars), 25)]
    [TestCase(nameof(StarAnnotatorOptions.ShowStarBounds), false)]
    [TestCase(nameof(StarAnnotatorOptions.ShowROI), false)]
    [TestCase(nameof(StarAnnotatorOptions.ShowTooDistorted), true)]
    [TestCase(nameof(StarAnnotatorOptions.ShowDegenerate), true)]
    [TestCase(nameof(StarAnnotatorOptions.AnnotationFontSizePoints), 22f)]
    public void Setter_RaisesPropertyChanged(string propertyName, object newValue) {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var prop = typeof(StarAnnotatorOptions).GetProperty(propertyName);
        prop.SetValue(options, Convert.ChangeType(newValue, prop.PropertyType));
        Assert.That(raised, Does.Contain(propertyName));
    }

    [Test]
    public void ColorSetter_RaisesPropertyChanged() {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        options.StarBoundsColor = Color.FromRgb(1, 2, 3);
        Assert.That(raised, Does.Contain(nameof(StarAnnotatorOptions.StarBoundsColor)));
    }

    [Test]
    public void ResetDefaults_RestoresDocumentedDefaults() {
        var (options, _, _) = Build();
        options.MaxStars = 5;
        options.ShowAnnotations = false;
        options.ShowAnnotationsDuringAutoFocus = false;
        options.ShowStructureMap = ShowStructureMapEnum.Original;
        options.AnnotationFontFamily = new FontFamily("Tahoma");

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.MaxStars, Is.EqualTo(200));
            Assert.That(options.ShowAnnotations, Is.True);
            Assert.That(options.ShowAnnotationsDuringAutoFocus, Is.True);
            Assert.That(options.ShowStructureMap, Is.EqualTo(ShowStructureMapEnum.None));
            Assert.That(options.AnnotationFontFamily.FamilyNames.Values, Does.Contain("Arial"));
        });
    }

    [Test]
    public void ProfileChanged_ReinitializesOptions() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new StarAnnotatorOptions(profile, store);
        options.MaxStars = 5;
        Assert.That(options.MaxStars, Is.EqualTo(5));

        store.Clear();
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.That(options.MaxStars, Is.EqualTo(200));
    }

    [Test]
    public void Constructor_ThrowsOnNullAccessor() {
        var profile = Substitute.For<IProfileService>();
        Assert.Throws<ArgumentNullException>(() => new StarAnnotatorOptions(profile, null));
    }
}
