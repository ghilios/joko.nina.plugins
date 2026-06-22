using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

[TestFixture]
public class AutoFocusOptionsTests {

    private static (AutoFocusOptions options, InMemoryPluginOptionsAccessor store, IProfileService profile) Build() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new AutoFocusOptions(profile, store);
        return (options, store, profile);
    }

    [Test]
    public void Defaults_AreLoadedFromAccessor() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.MaxConcurrent, Is.EqualTo(0));
            Assert.That(options.FastFocusModeEnabled, Is.False);
            Assert.That(options.FastStepSize, Is.EqualTo(1));
            Assert.That(options.FastOffsetSteps, Is.EqualTo(4));
            Assert.That(options.FastThreshold_Celcius, Is.EqualTo(5));
            Assert.That(options.FastThreshold_FocuserPosition, Is.EqualTo(100));
            Assert.That(options.FastThreshold_Seconds, Is.EqualTo((int)TimeSpan.FromMinutes(60).TotalSeconds));
            Assert.That(options.AutoFocusTimeoutSeconds, Is.EqualTo((int)TimeSpan.FromMinutes(10).TotalSeconds));
            Assert.That(options.ValidateHfrImprovement, Is.True);
            Assert.That(options.HFRImprovementThreshold, Is.EqualTo(0.15));
            Assert.That(options.SavePath, Is.EqualTo(""));
            Assert.That(options.Save, Is.False);
            Assert.That(options.KeepFramesForReview, Is.False);
            Assert.That(options.LastSelectedLoadPath, Is.EqualTo(""));
            Assert.That(options.FocuserOffset, Is.EqualTo(0));
            Assert.That(options.MaxOutlierRejections, Is.EqualTo(1));
            Assert.That(options.OutlierRejectionConfidence, Is.EqualTo(0.90));
            Assert.That(options.WeightedHyperbolicFitEnabled, Is.True);
        });
    }

    [Test]
    public void Setters_PersistToAccessor() {
        var (options, store, _) = Build();
        options.MaxConcurrent = 4;
        options.FastFocusModeEnabled = true;
        options.FastStepSize = 3;
        options.FastOffsetSteps = 6;
        options.FastThreshold_Celcius = 7;
        options.FastThreshold_FocuserPosition = 250;
        options.FastThreshold_Seconds = 1800;
        options.AutoFocusTimeoutSeconds = 900;
        options.ValidateHfrImprovement = false;
        options.HFRImprovementThreshold = 0.25;
        options.SavePath = @"C:\AF";
        options.Save = true;
        options.KeepFramesForReview = true;
        options.LastSelectedLoadPath = @"C:\AF\last";
        options.FocuserOffset = -10;
        options.MaxOutlierRejections = 3;
        options.OutlierRejectionConfidence = 0.95;
        options.WeightedHyperbolicFitEnabled = false;

        Assert.Multiple(() => {
            Assert.That(store.Snapshot["MaxConcurrent"], Is.EqualTo(4));
            Assert.That(store.Snapshot["FastFocusModeEnabled"], Is.True);
            Assert.That(store.Snapshot["FastStepSize"], Is.EqualTo(3));
            Assert.That(store.Snapshot["FastOffsetSteps"], Is.EqualTo(6));
            Assert.That(store.Snapshot["FastThreshold_Celcius"], Is.EqualTo(7));
            Assert.That(store.Snapshot["FastThreshold_FocuserPosition"], Is.EqualTo(250));
            Assert.That(store.Snapshot["FastThreshold_Seconds"], Is.EqualTo(1800));
            Assert.That(store.Snapshot["AutoFocusTimeoutSeconds"], Is.EqualTo(900));
            Assert.That(store.Snapshot["ValidateHfrImprovement"], Is.False);
            Assert.That(store.Snapshot["HFRImprovementThreshold"], Is.EqualTo(0.25));
            Assert.That(store.Snapshot["SavePath"], Is.EqualTo(@"C:\AF"));
            Assert.That(store.Snapshot["Save"], Is.True);
            Assert.That(store.Snapshot[nameof(AutoFocusOptions.KeepFramesForReview)], Is.True);
            Assert.That(store.Snapshot["LastSelectedLoadPath"], Is.EqualTo(@"C:\AF\last"));
            Assert.That(store.Snapshot["FocuserOffset"], Is.EqualTo(-10));
            Assert.That(store.Snapshot[nameof(AutoFocusOptions.MaxOutlierRejections)], Is.EqualTo(3));
            Assert.That(store.Snapshot[nameof(AutoFocusOptions.OutlierRejectionConfidence)], Is.EqualTo(0.95));
            Assert.That(store.Snapshot[nameof(AutoFocusOptions.WeightedHyperbolicFitEnabled)], Is.False);
        });
    }

    [Test]
    public void ResetDefaults_RestoresDocumentedDefaults() {
        var (options, _, _) = Build();
        options.MaxConcurrent = 8;
        options.FastFocusModeEnabled = true;
        options.HFRImprovementThreshold = 0.99;
        options.Save = true;
        options.KeepFramesForReview = true;
        options.MaxOutlierRejections = 4;

        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.MaxConcurrent, Is.EqualTo(0));
            Assert.That(options.FastFocusModeEnabled, Is.False);
            Assert.That(options.HFRImprovementThreshold, Is.EqualTo(0.15));
            Assert.That(options.Save, Is.False);
            Assert.That(options.KeepFramesForReview, Is.False);
            Assert.That(options.MaxOutlierRejections, Is.EqualTo(1));
            Assert.That(options.OutlierRejectionConfidence, Is.EqualTo(0.90));
            Assert.That(options.WeightedHyperbolicFitEnabled, Is.True);
        });
    }

    [TestCase(nameof(AutoFocusOptions.MaxConcurrent), 4)]
    [TestCase(nameof(AutoFocusOptions.FastFocusModeEnabled), true)]
    [TestCase(nameof(AutoFocusOptions.FastStepSize), 5)]
    [TestCase(nameof(AutoFocusOptions.FastThreshold_Celcius), 7)]
    [TestCase(nameof(AutoFocusOptions.AutoFocusTimeoutSeconds), 1200)]
    [TestCase(nameof(AutoFocusOptions.ValidateHfrImprovement), false)]
    [TestCase(nameof(AutoFocusOptions.HFRImprovementThreshold), 0.5)]
    [TestCase(nameof(AutoFocusOptions.Save), true)]
    [TestCase(nameof(AutoFocusOptions.KeepFramesForReview), true)]
    [TestCase(nameof(AutoFocusOptions.SavePath), "x")]
    [TestCase(nameof(AutoFocusOptions.LastSelectedLoadPath), "y")]
    [TestCase(nameof(AutoFocusOptions.FocuserOffset), 5)]
    [TestCase(nameof(AutoFocusOptions.MaxOutlierRejections), 2)]
    [TestCase(nameof(AutoFocusOptions.OutlierRejectionConfidence), 0.95)]
    [TestCase(nameof(AutoFocusOptions.WeightedHyperbolicFitEnabled), false)]
    public void Setter_RaisesPropertyChanged(string propertyName, object newValue) {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var prop = typeof(AutoFocusOptions).GetProperty(propertyName);
        prop.SetValue(options, Convert.ChangeType(newValue, prop.PropertyType));
        Assert.That(raised, Does.Contain(propertyName));
    }

    [Test]
    public void FastOffsetSteps_RejectsValuesLessThanTwo() {
        var (options, _, _) = Build();
        Assert.Throws<ArgumentException>(() => options.FastOffsetSteps = 1);
    }

    [TestCase(-1)]
    public void FastThreshold_SecondsRejectsNegative(int v) {
        var (options, _, _) = Build();
        Assert.Throws<ArgumentException>(() => options.FastThreshold_Seconds = v);
    }

    [Test]
    public void HFRImprovementThreshold_RejectsNonFinite() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.Throws<ArgumentException>(() => options.HFRImprovementThreshold = double.NaN);
            Assert.Throws<ArgumentException>(() => options.HFRImprovementThreshold = double.PositiveInfinity);
        });
    }

    [Test]
    public void AutoFocusTimeoutSeconds_RejectsZeroOrNegative() {
        var (options, _, _) = Build();
        Assert.Throws<ArgumentException>(() => options.AutoFocusTimeoutSeconds = 0);
        Assert.Throws<ArgumentException>(() => options.AutoFocusTimeoutSeconds = -5);
    }

    [Test]
    public void OutlierRejectionConfidence_RejectsOutOfRange() {
        var (options, _, _) = Build();
        Assert.Throws<ArgumentException>(() => options.OutlierRejectionConfidence = 0.5);
        Assert.Throws<ArgumentException>(() => options.OutlierRejectionConfidence = 1.0);
    }

    [Test]
    public void Setter_DoesNotRaiseWhenUnchanged() {
        var (options, _, _) = Build();
        options.MaxConcurrent = 5;
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        options.MaxConcurrent = 5;
        Assert.That(raised, Is.Empty);
    }

    [Test]
    public void ProfileChanged_ReinitializesValues() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new AutoFocusOptions(profile, store);
        options.MaxConcurrent = 7;
        Assert.That(options.MaxConcurrent, Is.EqualTo(7));

        store.Clear();
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.That(options.MaxConcurrent, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_ThrowsOnNullAccessor() {
        var profile = Substitute.For<IProfileService>();
        Assert.Throws<ArgumentNullException>(() => new AutoFocusOptions(profile, null));
    }

    [Test]
    public void HyperbolicFitModel_DefaultsToHybrid() {
        // A profile that never chose a fit model => the Hybrid best-fit model is the default.
        var (options, _, _) = Build();
        Assert.That(options.HyperbolicFitModel, Is.EqualTo(HyperbolicFitModel.Hybrid));
    }

    [Test]
    public void HyperbolicFitModel_IgnoresLegacyUnevenBoolean() {
        // The legacy UnevenHyperbolicFitEnabled boolean has been removed. A value left in the store from an
        // older version must no longer influence the fit model: a profile that never chose a HyperbolicFitModel
        // still defaults to Hybrid.
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        store.SetValueBoolean("UnevenHyperbolicFitEnabled", true);

        var options = new AutoFocusOptions(profile, store);

        Assert.That(options.HyperbolicFitModel, Is.EqualTo(HyperbolicFitModel.Hybrid));
    }

    [Test]
    public void HyperbolicFitModel_SetterPersistsEnum() {
        var (options, store, _) = Build();
        options.HyperbolicFitModel = HyperbolicFitModel.TiltedHyperbola;
        Assert.That(store.Snapshot[nameof(AutoFocusOptions.HyperbolicFitModel)], Is.EqualTo(HyperbolicFitModel.TiltedHyperbola));
    }

    [Test]
    public void HyperbolicFitModel_ResetRestoresHybrid() {
        var (options, _, _) = Build();
        options.HyperbolicFitModel = HyperbolicFitModel.SmoothBlend;
        options.ResetDefaults();
        Assert.That(options.HyperbolicFitModel, Is.EqualTo(HyperbolicFitModel.Hybrid));
    }

    [Test]
    public void FitRejectionCriterion_Defaults() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.That(options.FitRejectionCriterion, Is.EqualTo(FitRejectionCriterion.RSquared));
            Assert.That(options.ReducedChiSquaredRejectionThreshold, Is.EqualTo(5.0));
        });
    }

    [Test]
    public void FitRejectionCriterion_SettersPersist() {
        var (options, store, _) = Build();
        options.FitRejectionCriterion = FitRejectionCriterion.ReducedChiSquared;
        options.ReducedChiSquaredRejectionThreshold = 8.0;
        Assert.Multiple(() => {
            Assert.That(store.Snapshot[nameof(AutoFocusOptions.FitRejectionCriterion)], Is.EqualTo(FitRejectionCriterion.ReducedChiSquared));
            Assert.That(store.Snapshot[nameof(AutoFocusOptions.ReducedChiSquaredRejectionThreshold)], Is.EqualTo(8.0));
        });
    }

    [Test]
    public void FitRejectionCriterion_ResetRestoresDefaults() {
        var (options, _, _) = Build();
        options.FitRejectionCriterion = FitRejectionCriterion.ReducedChiSquared;
        options.ReducedChiSquaredRejectionThreshold = 12.0;
        options.ResetDefaults();
        Assert.Multiple(() => {
            Assert.That(options.FitRejectionCriterion, Is.EqualTo(FitRejectionCriterion.RSquared));
            Assert.That(options.ReducedChiSquaredRejectionThreshold, Is.EqualTo(5.0));
        });
    }

    [Test]
    public void FitRejectionCriterion_SetterRaisesPropertyChanged() {
        var (options, _, _) = Build();
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        options.FitRejectionCriterion = FitRejectionCriterion.ReducedChiSquared;
        options.ReducedChiSquaredRejectionThreshold = 7.5;
        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(AutoFocusOptions.FitRejectionCriterion)));
            Assert.That(raised, Does.Contain(nameof(AutoFocusOptions.ReducedChiSquaredRejectionThreshold)));
        });
    }

    [Test]
    public void ReducedChiSquaredRejectionThreshold_RejectsNonFinite() {
        var (options, _, _) = Build();
        Assert.Multiple(() => {
            Assert.Throws<ArgumentException>(() => options.ReducedChiSquaredRejectionThreshold = double.NaN);
            Assert.Throws<ArgumentException>(() => options.ReducedChiSquaredRejectionThreshold = double.PositiveInfinity);
        });
    }
}
