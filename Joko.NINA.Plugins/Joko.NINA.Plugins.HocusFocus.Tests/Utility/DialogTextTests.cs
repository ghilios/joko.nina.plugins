#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

// NINA's MyMessageBox does not wrap its message, so the window grows to fit the longest line. A device-error
// prompt on one long line therefore stretched the modal past both screen edges and clipped the very question
// the user had to answer. These pin the wrapping that prevents that.
[TestFixture]
public class DialogTextTests {

    private static int LongestLine(string text) =>
        text.Split('\n').Max(l => l.TrimEnd('\r').Length);

    [Test]
    public void Wrap_LongSingleLine_BreaksItBelowTheColumnLimit() {
        var message =
            "Automatic Adjustment failed after 3 move(s) were sent: EAT command 'bf,33' (Backfocus: +33 steps " +
            "(part 2 of 2)) did not acknowledge success within 20s. Device state is ambiguous; positions were " +
            "NOT advanced -- treat as a potential state-dirty condition.";

        var wrapped = DialogText.Wrap(message);

        Assert.Multiple(() => {
            Assert.That(LongestLine(wrapped), Is.LessThanOrEqualTo(DialogText.DefaultWrapColumns));
            Assert.That(wrapped.Replace("\n", " "), Is.EqualTo(message), "wrapping must not lose or alter any text");
        });
    }

    [Test]
    public void Wrap_PreservesAuthoredLineBreaks() {
        // The blank line separating a failure description from its question is deliberate structure.
        var message = "Something failed.\n\nRevert the applied move(s) now?";
        Assert.That(DialogText.Wrap(message), Is.EqualTo(message));
    }

    [Test]
    public void Wrap_ShortText_IsUnchanged() {
        Assert.That(DialogText.Wrap("Disconnect the device?"), Is.EqualTo("Disconnect the device?"));
    }

    [Test]
    public void Wrap_WordLongerThanTheLimit_IsLeftIntactRatherThanSplit() {
        // Breaking a path/URL mid-token would corrupt what it names; a single over-long line is the lesser evil.
        var path = new string('x', DialogText.DefaultWrapColumns + 40);
        var wrapped = DialogText.Wrap("Saved to " + path);
        Assert.That(wrapped.Split('\n'), Does.Contain(path));
    }

    [TestCase(null)]
    [TestCase("")]
    public void Wrap_NullOrEmpty_IsReturnedUnchanged(string text) {
        Assert.That(DialogText.Wrap(text), Is.EqualTo(text));
    }

    [Test]
    public void Wrap_CrLfInput_KeepsItsCarriageReturns() {
        var message = "First line.\r\nSecond line.";
        Assert.That(DialogText.Wrap(message), Is.EqualTo(message));
    }

    [Test]
    public void Wrap_EveryWrappedLineIsNonEmpty_AndNoTextIsDuplicated() {
        var message = string.Join(" ", Enumerable.Repeat("word", 200));
        var wrapped = DialogText.Wrap(message);
        Assert.Multiple(() => {
            Assert.That(wrapped.Split('\n'), Has.All.Not.Empty);
            Assert.That(wrapped.Split('\n').Sum(l => l.Split(' ').Length), Is.EqualTo(200));
            Assert.That(LongestLine(wrapped), Is.LessThanOrEqualTo(DialogText.DefaultWrapColumns));
        });
    }
}
