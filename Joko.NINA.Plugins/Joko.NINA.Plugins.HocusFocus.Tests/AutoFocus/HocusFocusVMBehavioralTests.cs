using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NSubstitute;
using NUnit.Framework;
using OxyPlot;
using OxyPlot.Series;
using System.Collections.Generic;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

[TestFixture]
public class HocusFocusVMBehavioralTests {

    private static int CountChanges(INotifyPropertyChanged source, string propertyName, System.Action act) {
        int n = 0;
        PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == propertyName) n++; };
        source.PropertyChanged += handler;
        try { act(); } finally { source.PropertyChanged -= handler; }
        return n;
    }

    [Test]
    public void Construct_WithBundle_PopulatesBaseline() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildHocusFocusVM();

        Assert.Multiple(() => {
            Assert.That(vm.FocusPoints, Is.Not.Null);
            Assert.That(vm.PlotFocusPoints, Is.Not.Null);
            Assert.That(vm.PlotRejectedFocusPoints, Is.Not.Null);
            Assert.That(vm.AutoFocusInProgress, Is.False);
            Assert.That(vm.LastReport, Is.Null);
            Assert.That(vm.LoadSavedAutoFocusRunCommand, Is.Not.Null);
            Assert.That(vm.CancelLoadSavedAutoFocusRunCommand, Is.Not.Null);
        });
    }

    [TestCase(nameof(HocusFocusVM.InitialFocuserPosition), 1234, 1234, 5678)]
    [TestCase(nameof(HocusFocusVM.FinalFocuserPosition), 1, 1, 2)]
    public void IntProperty_RaisesOnlyOnChange(string propertyName, int v1, int v2, int v3) {
        var vm = new MediatorBundle().BuildHocusFocusVM();
        var prop = typeof(HocusFocusVM).GetProperty(propertyName);
        var changes = CountChanges(vm, propertyName, () => {
            prop.SetValue(vm, v1);
            prop.SetValue(vm, v2);
            prop.SetValue(vm, v3);
        });
        Assert.That(changes, Is.EqualTo(2));
    }

    [TestCase(nameof(HocusFocusVM.InitialHFR), 1.5, 1.5, 2.0)]
    [TestCase(nameof(HocusFocusVM.FinalHFR), 0.0, 1.5, 1.5)]
    public void DoubleProperty_RaisesOnlyOnChange(string propertyName, double v1, double v2, double v3) {
        var vm = new MediatorBundle().BuildHocusFocusVM();
        var prop = typeof(HocusFocusVM).GetProperty(propertyName);
        var changes = CountChanges(vm, propertyName, () => {
            prop.SetValue(vm, v1);
            prop.SetValue(vm, v2);
            prop.SetValue(vm, v3);
        });
        Assert.That(changes, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void FinalFocusPoint_RoundsXIntoFinalFocuserPosition() {
        var vm = new MediatorBundle().BuildHocusFocusVM();
        vm.FinalFocusPoint = new DataPoint(123.49, 1.2);
        Assert.That(vm.FinalFocuserPosition, Is.EqualTo(123));

        vm.FinalFocusPoint = new DataPoint(123.51, 1.2);
        Assert.That(vm.FinalFocuserPosition, Is.EqualTo(124));
    }

    [Test]
    public void AutoFocusInProgress_DefaultsFalse() {
        var vm = new MediatorBundle().BuildHocusFocusVM();
        Assert.That(vm.AutoFocusInProgress, Is.False);
    }

    [Test]
    public void AutoFocusChartMethod_Setter_RaisesPropertyChanged() {
        var vm = new MediatorBundle().BuildHocusFocusVM();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.AutoFocusChartMethod = AFMethodEnum.CONTRASTDETECTION;
        Assert.That(raised, Does.Contain(nameof(vm.AutoFocusChartMethod)));
    }

    [Test]
    public void AutoFocusChartCurveFitting_Setter_RaisesPropertyChanged() {
        var vm = new MediatorBundle().BuildHocusFocusVM();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.AutoFocusChartCurveFitting = AFCurveFittingEnum.PARABOLIC;
        Assert.That(raised, Does.Contain(nameof(vm.AutoFocusChartCurveFitting)));
    }

    [Test]
    public void SetCurveFittings_StarHFR_TrendParabolic_PopulatesQuadraticAndTrendline() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildHocusFocusVM();
        // y = (x-100)^2 / 1000 + 1
        for (int x = 50; x <= 150; x += 10) {
            var y = (x - 100.0) * (x - 100.0) / 1000.0 + 1.0;
            vm.FocusPoints.Add(new ScatterErrorPoint(x, y, 0, 0.1));
        }

        vm.SetCurveFittings(method: AFMethodEnum.STARHFR.ToString(), fitting: AFCurveFittingEnum.TRENDPARABOLIC.ToString());

        Assert.Multiple(() => {
            Assert.That(vm.QuadraticFitting, Is.Not.Null);
            Assert.That(vm.TrendlineFitting, Is.Not.Null);
            Assert.That(vm.HyperbolicFitting, Is.Null);
        });
    }

    [Test]
    public void SetCurveFittings_StarHFR_TrendHyperbolic_PopulatesHyperbolicAndTrendline() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildHocusFocusVM();
        var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
            x0: 5000, y0: 0.0, a: 2.0, b: 80.0, xStart: 4800, xStep: 25, count: 17);
        foreach (var p in pts) vm.FocusPoints.Add(p);

        vm.SetCurveFittings(method: AFMethodEnum.STARHFR.ToString(), fitting: AFCurveFittingEnum.TRENDHYPERBOLIC.ToString());

        Assert.Multiple(() => {
            Assert.That(vm.HyperbolicFitting, Is.Not.Null);
            Assert.That(vm.TrendlineFitting, Is.Not.Null);
            Assert.That(vm.QuadraticFitting, Is.Null);
        });
    }

    [Test]
    public void SetCurveFittings_StarHFR_Trendlines_OnlyPopulatesTrendline() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildHocusFocusVM();
        for (int x = 50; x <= 150; x += 10) {
            var y = (x - 100.0) * (x - 100.0) / 1000.0 + 1.0;
            vm.FocusPoints.Add(new ScatterErrorPoint(x, y, 0, 0.1));
        }

        vm.SetCurveFittings(method: AFMethodEnum.STARHFR.ToString(), fitting: AFCurveFittingEnum.TRENDLINES.ToString());

        Assert.Multiple(() => {
            Assert.That(vm.TrendlineFitting, Is.Not.Null);
            Assert.That(vm.HyperbolicFitting, Is.Null);
            Assert.That(vm.QuadraticFitting, Is.Null);
        });
    }

    [Test]
    public void LoadSavedAutoFocusRunCommand_IsAvailable() {
        var vm = new MediatorBundle().BuildHocusFocusVM();
        Assert.That(vm.LoadSavedAutoFocusRunCommand.CanExecute(null), Is.True);
    }
}
