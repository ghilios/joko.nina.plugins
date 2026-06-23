#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class HocusFocusVMInteractiveTests {

        // The interactive/retain decision must be an explicit flag with no public setter, toggled only through the named
        // MarkInteractive()/MarkNonInteractive() opt-in methods (called by InteractiveHostBehavior for the pane instance).
        // This guards the F07/F20 contract that intent is declared explicitly, never via an arbitrary settable property.
        [Test]
        public void IsInteractive_HasNoPublicSetter_AndMarkMethodsExist() {
            Assert.Multiple(() => {
                // nonPublic:false returns only a PUBLIC set accessor (null when the setter is private/absent). The spec's
                // contract is "no PUBLIC setter" while still allowing the private set used by MarkInteractive/MarkNonInteractive;
                // GetSetMethod(nonPublic:true) would return the private accessor and is therefore the wrong probe here.
                Assert.That(typeof(HocusFocusVM).GetProperty(nameof(HocusFocusVM.IsInteractive)).GetSetMethod(nonPublic: false),
                    Is.Null, "IsInteractive must not expose a public setter; it is toggled via MarkInteractive()/MarkNonInteractive().");
                Assert.That(typeof(HocusFocusVM).GetMethod(nameof(HocusFocusVM.MarkInteractive)), Is.Not.Null,
                    "MarkInteractive() must exist as the explicit opt-in.");
                Assert.That(typeof(HocusFocusVM).GetMethod(nameof(HocusFocusVM.MarkNonInteractive)), Is.Not.Null,
                    "MarkNonInteractive() must exist as the explicit opt-out.");
            });
        }

        // Behavioral check that the Mark methods actually toggle the flag and that a freshly-built (sequencer-style) VM
        // defaults to non-interactive — the core F07/F20 contract. Uses the shared MediatorBundle helper the other
        // behavioral tests use, so the DI cost is one line.
        [Test]
        public void MarkMethods_ToggleIsInteractive() {
            var vm = new MediatorBundle().BuildHocusFocusVM();
            Assert.Multiple(() => {
                Assert.That(vm.IsInteractive, Is.False, "A freshly-built VM (as the sequencer creates) must default non-interactive.");
            });
            vm.MarkInteractive();
            Assert.That(vm.IsInteractive, Is.True);
            vm.MarkNonInteractive();
            Assert.That(vm.IsInteractive, Is.False);
        }
    }
}
