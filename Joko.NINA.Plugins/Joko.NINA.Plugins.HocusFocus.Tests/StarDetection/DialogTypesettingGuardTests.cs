#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// NINA's <c>MyMessageBox</c> body is a bare TextBlock with NO TextWrapping and NO MaxWidth, inside a Window
    /// with <c>SizeToContent="WidthAndHeight"</c> and <c>ResizeMode="NoResize"</c>. So the dialog is exactly as
    /// wide as the longest line of the string it is handed, and the Yes/No buttons — a two-column Grid spanning
    /// the window — split that width between them.
    ///
    /// <para>A one-paragraph message therefore renders as a single enormous line: the detection-binning prompt
    /// shipped ~1280px wide with 610px buttons, and the AF-binning conflict prompt ~1800px. The fix is hard line
    /// breaks in the caller, which means the breaks are load-bearing layout that nothing else would catch —
    /// reflowing the copy, or adding a sentence, silently widens the dialog again. Hence this guard.</para>
    ///
    /// <para>MyMessageBox is NINA core, shared by every plugin; it cannot be fixed from here.</para>
    /// </summary>
    [TestFixture]
    public class DialogTypesettingGuardTests {

        private const int Budget = StarDetectionOptimizerWizardVM.MaxDialogLineLength;

        /// <summary>Shared by the AF-binning conflict tests, which own the profile scaffolding needed to build a
        /// conflict and so assert the same rule from their own fixture.</summary>
        internal static void AssertEveryLineFits(string text, string what, Func<string, bool> exempt = null) {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            Assert.That(lines.Length, Is.GreaterThan(1), $"{what}: a single-line body is the defect this guards against");

            // Echo the rendered block with a ruler at the budget. These dialogs cannot be inspected from a test
            // run any other way, and the whole point of the fix is how the text SITS, so print the shape.
            TestContext.WriteLine($"--- {what} ---");
            TestContext.WriteLine(new string('-', Budget) + "| " + Budget);
            foreach (var line in lines) {
                TestContext.WriteLine(line);
            }

            var tooLong = lines
                .Select((line, i) => (line, number: i + 1))
                .Where(l => l.line.Length > Budget && !(exempt?.Invoke(l.line) ?? false))
                .Select(l => $"  line {l.number} ({l.line.Length} chars): {l.line}")
                .ToList();

            Assert.That(tooLong, Is.Empty,
                $"{what} has lines over the {Budget}-character budget. MyMessageBox does not wrap, so each of\n" +
                "these sets the dialog's width and stretches its buttons to match:\n" +
                string.Join("\n", tooLong) + "\n\n" +
                "Fix: re-break the string, do not widen the budget.");
        }

        [TestCase(1, 2)]
        [TestCase(1, 3)]
        [TestCase(2, 4)]
        [TestCase(4, 1)]
        public void ReoptimizeAtBinningPrompt_IsTypesetWithinTheBudget(int from, int to) {
            // Swept across factors because the body interpolates them: a wider value must not push a line over.
            AssertEveryLineFits(
                StarDetectionOptimizerWizardVM.DescribeReoptimizeAtBinning(from, to),
                $"The re-optimize prompt ({from}x -> {to}x)");
        }

        /// <summary>The per-filter bullet carries a user-chosen filter name, so its width cannot be bounded.
        /// Every other line of that prompt can be, and is.</summary>
        internal static bool IsFilterBullet(string line) =>
            line.TrimStart().StartsWith("- Filter \"", StringComparison.Ordinal);
    }
}
