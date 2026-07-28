#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

/// <summary>
/// The measured in-focus HFR is a star size in CAPTURED PIXELS, so it only means anything at the pixel scale it
/// was measured at. These cover the invalidation rule: when the active profile's pixel scale moves, the
/// measurement is dropped so the detection-binning recommendation asks for a fresh auto-focus instead of
/// answering from a rig that no longer exists.
/// </summary>
[TestFixture]
public class InFocusHfrRecordTests {

    private static readonly DateTime MeasuredAt = new DateTime(2026, 7, 28, 1, 2, 3, DateTimeKind.Utc);

    /// <summary>A profile whose three pixel-scale-bearing settings objects are substitutes that both report
    /// values and raise PropertyChanged, so a test can move one the way the NINA options UI would.</summary>
    private sealed class FakeProfile {
        public IProfileService Service { get; } = Substitute.For<IProfileService>();
        public ITelescopeSettings Telescope { get; } = Substitute.For<ITelescopeSettings>();
        public ICameraSettings Camera { get; } = Substitute.For<ICameraSettings>();
        public IFocuserSettings Focuser { get; } = Substitute.For<IFocuserSettings>();

        public FakeProfile(double focalLength = 2800.0, double pixelSize = 3.76, short afBinning = 1) {
            var profile = Substitute.For<IProfile>();
            Telescope.FocalLength.Returns(focalLength);
            Camera.PixelSize.Returns(pixelSize);
            Focuser.AutoFocusBinning.Returns(afBinning);
            profile.TelescopeSettings.Returns(Telescope);
            profile.CameraSettings.Returns(Camera);
            profile.FocuserSettings.Returns(Focuser);
            Service.ActiveProfile.Returns(profile);
        }

        public void SetFocalLength(double value) {
            Telescope.FocalLength.Returns(value);
            Telescope.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                Telescope, new PropertyChangedEventArgs(nameof(ITelescopeSettings.FocalLength)));
        }

        public void SetPixelSize(double value) {
            Camera.PixelSize.Returns(value);
            Camera.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                Camera, new PropertyChangedEventArgs(nameof(ICameraSettings.PixelSize)));
        }

        public void SetAutoFocusBinning(short value) {
            Focuser.AutoFocusBinning.Returns(value);
            Focuser.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                Focuser, new PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusBinning)));
        }

        /// <summary>Stages a value without announcing it, so a test can move two inputs and then raise a single
        /// notification — the only way to exercise a change that leaves the computed scale where it was.</summary>
        public void StageFocalLength(double value) => Telescope.FocalLength.Returns(value);

        public void StageAutoFocusBinning(short value) => Focuser.AutoFocusBinning.Returns(value);

        public void RaiseTelescopeChanged() =>
            Telescope.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                Telescope, new PropertyChangedEventArgs(nameof(ITelescopeSettings.FocalLength)));
    }

    private static (InFocusHfrRecord record, FakeProfile profile) NewRecordWithMeasurement(
        double focalLength = 2800.0, double pixelSize = 3.76, short afBinning = 1) {
        var profile = new FakeProfile(focalLength, pixelSize, afBinning);
        var record = new InFocusHfrRecord(new InMemoryPluginOptionsAccessor(), profile.Service);
        record.Record(5.8, MeasuredAt, "test");
        Assume.That(record.HasMeasurement, Is.True, "fixture guard: the measurement must be stored to begin with");
        return (record, profile);
    }

    [Test]
    public void ChangingFocalLength_ClearsTheMeasurement() {
        var (record, profile) = NewRecordWithMeasurement();

        profile.SetFocalLength(1400.0);

        Assert.Multiple(() => {
            Assert.That(record.HasMeasurement, Is.False, "5.8 px at 2800mm is 2.9 px at 1400mm - a different answer");
            Assert.That(record.HfrPixels, Is.NaN);
            Assert.That(record.MeasuredAtUtc, Is.Null, "the date must go with the value it described");
        });
    }

    [Test]
    public void ChangingPixelSize_ClearsTheMeasurement() {
        var (record, profile) = NewRecordWithMeasurement();

        profile.SetPixelSize(9.0);

        Assert.That(record.HasMeasurement, Is.False);
    }

    [Test]
    public void ChangingCaptureBinning_ClearsTheMeasurement() {
        // The stored value is in CAPTURED pixels, so capture binning rescales it just as surely as focal length.
        var (record, profile) = NewRecordWithMeasurement();

        profile.SetAutoFocusBinning(2);

        Assert.That(record.HasMeasurement, Is.False);
    }

    [Test]
    public void ClearingRaisesChanged_SoABoundRecommendationRefreshes() {
        var (record, profile) = NewRecordWithMeasurement();
        var changedCount = 0;
        record.Changed += (s, e) => changedCount++;

        profile.SetFocalLength(1400.0);

        Assert.That(changedCount, Is.EqualTo(1),
            "the options page binds the recommendation to this event; without it the stale line stays on screen");
    }

    [Test]
    public void SettingAValueToWhatItAlreadyWas_KeepsTheMeasurement() {
        // WPF raises PropertyChanged on assignment regardless of whether the value moved. Clearing on the event
        // rather than on the scale would cost the user a good measurement for touching a field and changing nothing.
        var (record, profile) = NewRecordWithMeasurement(focalLength: 2800.0);

        profile.SetFocalLength(2800.0);

        Assert.That(record.HasMeasurement, Is.True);
    }

    [Test]
    public void CompensatingChanges_ThatLeavePixelScaleWhereItWas_KeepTheMeasurement() {
        // 1400mm at 1x1 and 2800mm at 2x2 are the same captured pixel scale: doubling focal length halves the
        // scale, and doubling capture binning doubles it back. The same star still spans the same number of
        // captured pixels, so the measurement is still true. This is why the rule compares the computed SCALE and
        // not its three inputs - comparing inputs would clear here for no reason.
        var (record, profile) = NewRecordWithMeasurement(focalLength: 1400.0, afBinning: 1);

        profile.StageFocalLength(2800.0);
        profile.StageAutoFocusBinning(2);
        profile.RaiseTelescopeChanged();

        Assert.That(record.HasMeasurement, Is.True,
            "the scale did not move, so nothing about the star size changed");
    }

    [Test]
    public void SequentialEdits_ClearAsSoonAsOneMovesTheScale_EvenIfALaterOneWouldUndoIt() {
        // Documents the deliberate limit of the above. Edits arrive one property at a time, and nothing can know a
        // compensating second edit is coming, so the intermediate state (2800mm still at 1x1) is a real change and
        // is treated as one. Clearing is the safe direction: the cost is one auto-focus, and the alternative is
        // recommending from a rig that no longer exists.
        var (record, profile) = NewRecordWithMeasurement(focalLength: 1400.0, afBinning: 1);

        profile.SetFocalLength(2800.0);
        profile.SetAutoFocusBinning(2);

        Assert.That(record.HasMeasurement, Is.False);
    }

    [Test]
    public void SwitchingProfiles_DoesNotClear() {
        // The record is profile-scoped, so a switch already moves to the other profile's own stored measurement.
        // Two profiles having different optics is not a change to either one's optics.
        var (record, profile) = NewRecordWithMeasurement(focalLength: 2800.0);

        var other = Substitute.For<IProfile>();
        var otherTelescope = Substitute.For<ITelescopeSettings>();
        var otherCamera = Substitute.For<ICameraSettings>();
        var otherFocuser = Substitute.For<IFocuserSettings>();
        otherTelescope.FocalLength.Returns(500.0);
        otherCamera.PixelSize.Returns(3.76);
        otherFocuser.AutoFocusBinning.Returns((short)1);
        other.TelescopeSettings.Returns(otherTelescope);
        other.CameraSettings.Returns(otherCamera);
        other.FocuserSettings.Returns(otherFocuser);
        profile.Service.ActiveProfile.Returns(other);
        profile.Service.ProfileChanged += Raise.Event<EventHandler>(profile.Service, EventArgs.Empty);

        Assert.That(record.HasMeasurement, Is.True, "a profile switch is not an optics change");
    }

    [Test]
    public void AfterSwitchingProfiles_TheNewProfilesSettingsAreTheOnesWatched() {
        // The whole point of re-hooking: the old profile's settings objects must stop mattering, and the new
        // profile's must start. A switch that forgot to re-subscribe would leave this measurement immortal.
        var (record, profile) = NewRecordWithMeasurement(focalLength: 2800.0);

        var other = Substitute.For<IProfile>();
        var otherTelescope = Substitute.For<ITelescopeSettings>();
        var otherCamera = Substitute.For<ICameraSettings>();
        var otherFocuser = Substitute.For<IFocuserSettings>();
        otherTelescope.FocalLength.Returns(2800.0);
        otherCamera.PixelSize.Returns(3.76);
        otherFocuser.AutoFocusBinning.Returns((short)1);
        other.TelescopeSettings.Returns(otherTelescope);
        other.CameraSettings.Returns(otherCamera);
        other.FocuserSettings.Returns(otherFocuser);
        profile.Service.ActiveProfile.Returns(other);
        profile.Service.ProfileChanged += Raise.Event<EventHandler>(profile.Service, EventArgs.Empty);
        Assume.That(record.HasMeasurement, Is.True);

        // The OLD settings object now belongs to nobody: moving it must not touch the record.
        profile.SetFocalLength(1.0);
        Assert.That(record.HasMeasurement, Is.True, "the previous profile's settings must no longer be watched");

        // The NEW one must be live.
        otherTelescope.FocalLength.Returns(1400.0);
        otherTelescope.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
            otherTelescope, new PropertyChangedEventArgs(nameof(ITelescopeSettings.FocalLength)));
        Assert.That(record.HasMeasurement, Is.False, "the newly active profile's settings must be watched");
    }

    [Test]
    public void RecordingAfterAPixelScaleChange_Sticks() {
        // The end-to-end promise: cleared until the next auto-focus, and the next auto-focus refills it. If Record
        // did not re-baseline the scale it compares against, the next unrelated edit would wipe the new value.
        var (record, profile) = NewRecordWithMeasurement(focalLength: 2800.0);
        profile.SetFocalLength(1400.0);
        Assume.That(record.HasMeasurement, Is.False);

        record.Record(2.9, MeasuredAt, "the next auto-focus");
        profile.SetPixelSize(3.76);  // unchanged value, raised anyway

        Assert.Multiple(() => {
            Assert.That(record.HasMeasurement, Is.True);
            Assert.That(record.HfrPixels, Is.EqualTo(2.9).Within(1e-9));
        });
    }

    [Test]
    public void ClearIsANoOpWhenNothingIsStored() {
        var profile = new FakeProfile();
        var record = new InFocusHfrRecord(new InMemoryPluginOptionsAccessor(), profile.Service);
        var changedCount = 0;
        record.Changed += (s, e) => changedCount++;

        record.Clear("test");
        profile.SetFocalLength(1400.0);

        Assert.That(changedCount, Is.Zero, "nothing was lost, so nothing needs to refresh");
    }

    [Test]
    public void WithNoProfileService_TheRecordStillWorks() {
        // The internal test constructor is used without a profile in several fixtures; it must not require one.
        var record = new InFocusHfrRecord(new InMemoryPluginOptionsAccessor());
        record.Record(4.2, MeasuredAt, "test");
        Assert.That(record.HfrPixels, Is.EqualTo(4.2).Within(1e-9));
    }
}
