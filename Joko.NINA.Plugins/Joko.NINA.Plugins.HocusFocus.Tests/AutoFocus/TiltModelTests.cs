using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

[TestFixture]
public class SensorTiltModelTests {

    [Test]
    public void Constructor_AssignsSensorSide() {
        var m = new SensorTiltModel(SensorSide.TopLeft);
        Assert.That(m.SensorSide, Is.EqualTo(SensorSide.TopLeft));
    }

    [TestCase(nameof(SensorTiltModel.FocuserPosition))]
    [TestCase(nameof(SensorTiltModel.AdjustmentRequiredSteps))]
    [TestCase(nameof(SensorTiltModel.AdjustmentRequiredMicrons))]
    [TestCase(nameof(SensorTiltModel.RSquared))]
    public void Setter_RaisesPropertyChanged(string propertyName) {
        var m = new SensorTiltModel(SensorSide.Center);
        var raised = new List<string>();
        m.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var info = typeof(SensorTiltModel).GetProperty(propertyName);
        info.SetValue(m, 42.0);

        Assert.That(raised, Does.Contain(propertyName));
    }

    [Test]
    public void ToString_IncludesAllFields() {
        var m = new SensorTiltModel(SensorSide.BottomRight) {
            FocuserPosition = 10.0,
            AdjustmentRequiredSteps = 2.5,
            AdjustmentRequiredMicrons = 12.5,
            RSquared = 0.99,
        };

        var s = m.ToString();
        Assert.Multiple(() => {
            Assert.That(s, Does.Contain("BottomRight"));
            Assert.That(s, Does.Contain("FocuserPosition="));
            Assert.That(s, Does.Contain("AdjustmentRequiredSteps="));
            Assert.That(s, Does.Contain("RSquared="));
        });
    }
}

[TestFixture]
public class SensorTiltHistoryModelTests {

    [Test]
    public void Constructor_StoresAllValues() {
        var plane = TiltPlaneModel.Create(
            imageSize: new System.Drawing.Size(2000, 1000),
            fRatio: 5.0, focuserStepSizeMicrons: 5.0,
            centerFocuser: 1000,
            topLeftFocuser: 1000, topRightFocuser: 1000,
            bottomLeftFocuser: 1000, bottomRightFocuser: 1000);

        var h = new SensorTiltHistoryModel(historyId: 7, tiltPlaneModel: plane, backfocusFocuserPositionDelta: 1.5);

        Assert.Multiple(() => {
            Assert.That(h.HistoryId, Is.EqualTo(7));
            Assert.That(h.TiltPlaneModel, Is.SameAs(plane));
            Assert.That(h.BackfocusFocuserPositionDelta, Is.EqualTo(1.5));
        });
    }
}

[TestFixture]
public class SensorSideTests {

    [TestCase(SensorSide.Center, "Center")]
    [TestCase(SensorSide.TopLeft, "Top Left")]
    [TestCase(SensorSide.TopRight, "Top Right")]
    [TestCase(SensorSide.BottomLeft, "Bottom Left")]
    [TestCase(SensorSide.BottomRight, "Bottom Right")]
    public void EnumValues_HaveExpectedDescriptionAttribute(SensorSide value, string expectedDescription) {
        var fi = typeof(SensorSide).GetField(value.ToString());
        var attr = fi.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Description, Is.EqualTo(expectedDescription));
    }
}

[TestFixture]
public class TiltModelTests {

    [Test]
    public void Constructor_InitializesEmptyCollections() {
        var model = new TiltModel(Substitute.For<IInspectorOptions>());

        Assert.Multiple(() => {
            Assert.That(model.SensorTiltModels, Is.Empty);
            Assert.That(model.SensorTiltHistoryModels, Is.Empty);
            Assert.That(model.TiltPlaneModel, Is.Null);
        });
    }

    [Test]
    public void Reset_ClearsModelsAndPlane() {
        var options = Substitute.For<IInspectorOptions>();
        var model = new TiltModel(options);

        // Seed SensorTiltModels via the public collection so Reset has something to clear.
        model.SensorTiltModels.Add(new SensorTiltModel(SensorSide.Center));
        model.SensorTiltModels.Add(new SensorTiltModel(SensorSide.TopLeft));
        Assert.That(model.SensorTiltModels.Count, Is.EqualTo(2));

        model.Reset();

        Assert.Multiple(() => {
            Assert.That(model.SensorTiltModels, Is.Empty);
            Assert.That(model.TiltPlaneModel, Is.Null);
        });
    }
}
