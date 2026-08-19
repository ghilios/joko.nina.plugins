#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual {

    /// <summary>Which vocabulary a manual pad target belongs to — mirrors the approval dialog's Corner / Side / Backfocus badge.</summary>
    public enum ManualMoveKind {
        Corner,
        Side,
        Backfocus
    }

    /// <summary>
    /// Static description of one physical corner of a 4-corner coupled adapter, in all three index spaces at
    /// once. The plugin carries three different orderings for the same four motors (see
    /// <c>.claude/docs/tilt-domain.md</c>) and hand-converting between them is the single most repeated bug in
    /// this feature, so every manual-panel readout resolves its labels through this table instead:
    ///
    ///   corner label  TR   TL   BL   BR
    ///   device motor   1    2    4    3
    ///   wizard screw   1    2    3    4
    /// </summary>
    public sealed class TiltAdapterCorner {

        private TiltAdapterCorner(string label, int deviceMotorNumber, int wizardScrewNumber, int padRow, int padColumn) {
            Label = label;
            DeviceMotorNumber = deviceMotorNumber;
            WizardScrewNumber = wizardScrewNumber;
            PadRow = padRow;
            PadColumn = padColumn;
        }

        /// <summary>Corner label as printed everywhere in the tilt UI: TR, TL, BL, BR.</summary>
        public string Label { get; }

        /// <summary>1-based device motor number (the order <c>TiltDeviceConnectionService.CurrentPositions</c> uses).</summary>
        public int DeviceMotorNumber { get; }

        /// <summary>1-based wizard screw number (the order <c>TiltAdapterMove.PerCornerSteps</c> and the planner's <c>sPerScrew</c> use).</summary>
        public int WizardScrewNumber { get; }

        /// <summary>Row of this corner in the 2×2 spatial grid the UI draws (0 = top).</summary>
        public int PadRow { get; }

        /// <summary>Column of this corner in the 2×2 spatial grid the UI draws (0 = left).</summary>
        public int PadColumn { get; }

        /// <summary>
        /// "TL · M2" — the heading the position/target/preview cells share: the corner, which is how the
        /// device's own reports and the vendor app identify this motor, plus whatever the user calls the
        /// screw. With no names entered on a manual adapter it reads "TL · Screw 2".
        /// </summary>
        public string HeadingWith(IScrewLabelProvider labels) =>
            string.Format(CultureInfo.CurrentCulture, "{0} · {1}", Label, TiltScrewLabels.Resolve(labels, WizardScrewNumber));

        /// <summary>
        /// "Motor 2 · wizard screw 2" — the identities behind a heading, kept visible rather than tucked into
        /// a tooltip so a user watching the physical adapter can tell which corner should be moving.
        /// </summary>
        public string CellCaption => string.Format(CultureInfo.CurrentCulture, "Motor {0} · wizard screw {1}", DeviceMotorNumber, WizardScrewNumber);

        private static readonly TiltAdapterCorner TopRight = new TiltAdapterCorner("TR", deviceMotorNumber: 1, wizardScrewNumber: 1, padRow: 0, padColumn: 1);
        private static readonly TiltAdapterCorner TopLeft = new TiltAdapterCorner("TL", deviceMotorNumber: 2, wizardScrewNumber: 2, padRow: 0, padColumn: 0);
        private static readonly TiltAdapterCorner BottomLeft = new TiltAdapterCorner("BL", deviceMotorNumber: 4, wizardScrewNumber: 3, padRow: 1, padColumn: 0);
        private static readonly TiltAdapterCorner BottomRight = new TiltAdapterCorner("BR", deviceMotorNumber: 3, wizardScrewNumber: 4, padRow: 1, padColumn: 1);

        /// <summary>The four corners indexed by wizard screw index [0..3] — the order of <c>PerCornerSteps</c> and <c>sPerScrew</c>.</summary>
        public static readonly IReadOnlyList<TiltAdapterCorner> InWizardScrewOrder =
            Array.AsReadOnly(new[] { TopRight, TopLeft, BottomLeft, BottomRight });

        /// <summary>The four corners indexed by device motor index [0..3] — the order of <c>CurrentPositions</c> and <c>TiltDevicePositions.PerMotorSteps</c>.</summary>
        public static readonly IReadOnlyList<TiltAdapterCorner> InDeviceMotorOrder =
            Array.AsReadOnly(new[] { TopRight, TopLeft, BottomRight, BottomLeft });

        /// <summary>The four corners in the reading order the 2×2 UI grid lays them out: TL, TR, BL, BR.</summary>
        public static readonly IReadOnlyList<TiltAdapterCorner> InDisplayOrder =
            Array.AsReadOnly(new[] { TopLeft, TopRight, BottomLeft, BottomRight });

        public static TiltAdapterCorner ForWizardScrew(int wizardScrewNumber) {
            if (wizardScrewNumber < 1 || wizardScrewNumber > 4) {
                throw new ArgumentOutOfRangeException(nameof(wizardScrewNumber), wizardScrewNumber, "Wizard screw index must be 1-4.");
            }
            return InWizardScrewOrder[wizardScrewNumber - 1];
        }

        public static TiltAdapterCorner ForDeviceMotor(int deviceMotorNumber) {
            if (deviceMotorNumber < 1 || deviceMotorNumber > 4) {
                throw new ArgumentOutOfRangeException(nameof(deviceMotorNumber), deviceMotorNumber, "Device motor number must be 1-4.");
            }
            return InDeviceMotorOrder[deviceMotorNumber - 1];
        }
    }

    /// <summary>
    /// One cell of the manual adjustment pad: the nine things a 4-corner coupled adapter can be told to move
    /// (four corners, four sides, all together). Each cell is exactly one <see cref="TiltMoveAxis"/> plus a
    /// sign, so a pad click is always a single device command — never a decomposition.
    ///
    /// THE TABLE IN <see cref="All"/> IS A CORRECTNESS ANCHOR, like <c>TiltAdapterMove</c>'s unit-effect
    /// vectors. A sign error here is an EEPROM-persisted wrong-way motor move that the user cannot undo. Do
    /// not edit it without re-deriving against <c>TiltAdapterMove.UnitEffect</c> and re-running
    /// <c>ManualAdjustmentTargetTests</c>, which pins every cell in both directions.
    /// </summary>
    public sealed class ManualAdjustmentTarget {

        private ManualAdjustmentTarget(
            string key,
            string label,
            ManualMoveKind kind,
            TiltMoveAxis axis,
            int axisSign,
            int padRow,
            int padColumn,
            string movedLabel,
            string oppositeLabel,
            string tooltip) {
            Key = key;
            Label = label;
            Kind = kind;
            Axis = axis;
            AxisSign = axisSign;
            PadRow = padRow;
            PadColumn = padColumn;
            MovedLabel = movedLabel;
            OppositeLabel = oppositeLabel;
            Tooltip = tooltip;
        }

        /// <summary>Stable identifier used by tests and session state; never shown to the user.</summary>
        public string Key { get; }

        /// <summary>Text on the pad button.</summary>
        public string Label { get; }

        public ManualMoveKind Kind { get; }

        /// <summary>The single generator axis this cell drives.</summary>
        public TiltMoveAxis Axis { get; }

        /// <summary>
        /// Sign applied to <see cref="Axis"/> when the user picks direction "+". Because the axes are signed,
        /// two pad cells share each tilt axis (TR/BL share DiagonalA) and differ only here — the pad exists so
        /// the user never has to work out that "BL +" and "TR −" are the same move.
        /// </summary>
        public int AxisSign { get; }

        /// <summary>Row of this cell in the 3×3 pad (0 = top).</summary>
        public int PadRow { get; }

        /// <summary>Column of this cell in the 3×3 pad (0 = left).</summary>
        public int PadColumn { get; }

        /// <summary>The element that receives the signed amount, as it reads in prose ("TR", "top"). Null for Backfocus.</summary>
        public string MovedLabel { get; }

        /// <summary>The coupled element that receives the negated amount ("BL", "bottom"). Null for Backfocus.</summary>
        public string OppositeLabel { get; }

        public string Tooltip { get; }

        /// <summary>Screen-reader name: the first clause of the tooltip plus the cell's own label.</summary>
        public string AutomationName {
            get {
                int sentenceEnd = Tooltip.IndexOf('.');
                string firstSentence = sentenceEnd < 0 ? Tooltip : Tooltip.Substring(0, sentenceEnd);
                return string.Format(CultureInfo.CurrentCulture, "{0} — {1}", Label, firstSentence);
            }
        }

        /// <summary>
        /// Signed magnitude to command on <see cref="Axis"/> for a pad click. <paramref name="amount"/> is
        /// always positive (the amount box is strictly positive); the sign comes from the cell and the
        /// direction toggle together.
        /// </summary>
        public int SignedAxisSteps(bool positiveDirection, int amount) {
            return AxisSign * (positiveDirection ? 1 : -1) * amount;
        }

        /// <summary>
        /// The per-screw step effect of this pad click, in WIZARD screw indices 1..4 at [0..3] — the same
        /// space as <c>TiltAdapterMove.PerCornerSteps</c>. Derived from <c>TiltAdapterMove.UnitEffect</c>, so
        /// it cannot drift from what the device actually does.
        /// </summary>
        public IReadOnlyList<double> PerScrewEffect(bool positiveDirection, int amount) {
            int steps = SignedAxisSteps(positiveDirection, amount);
            var unit = TiltAdapterMove.UnitEffect(Axis);
            var effect = new double[unit.Count];
            for (int i = 0; i < unit.Count; ++i) {
                effect[i] = unit[i] * steps;
            }
            return Array.AsReadOnly(effect);
        }

        /// <summary>
        /// The device move for a pad click. Throws <see cref="ArgumentOutOfRangeException"/> for a
        /// non-positive amount — the panel gates on that before ever calling here, and a zero-magnitude move
        /// would be rejected by <c>EatCommands.Format</c> anyway.
        /// </summary>
        public TiltAdapterMove BuildMove(bool positiveDirection, int amount) {
            if (amount < 1) {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "A manual move amount must be at least 1 step.");
            }
            int steps = SignedAxisSteps(positiveDirection, amount);
            var group = Axis == TiltMoveAxis.Backfocus ? TiltMoveGroup.Backfocus : TiltMoveGroup.Tilt;
            return new TiltAdapterMove(Axis, steps, group, DescribeMove(steps));
        }

        /// <summary>
        /// "TR (Motor 1) +20 · BL (Motor 4) −20 steps" — the semantic line for a pad click, in the panel's own
        /// corner-and-motor vocabulary rather than the approval dialog's wizard-screw one. This becomes the
        /// move's <c>Description</c>, so it is also what the controller's progress text and any limit
        /// exception quote back at the user.
        /// </summary>
        public string DescribeMove(int signedAxisSteps) {
            var effect = TiltAdapterMove.UnitEffect(Axis);
            if (Axis == TiltMoveAxis.Backfocus) {
                return string.Format(CultureInfo.CurrentCulture, "All four motors {0} steps together", FormatSigned(signedAxisSteps));
            }

            var parts = new List<string>(4);
            // Wizard screw order, but rendered in the 2×2 display order so the sentence reads the way the grid
            // above it looks.
            foreach (var corner in TiltAdapterCorner.InDisplayOrder) {
                double perScrew = effect[corner.WizardScrewNumber - 1] * signedAxisSteps;
                if (Math.Abs(perScrew) < 0.5) {
                    continue;
                }
                parts.Add(string.Format(
                    CultureInfo.CurrentCulture, "{0} (Motor {1}) {2}", corner.Label, corner.DeviceMotorNumber, FormatSigned((int)Math.Round(perScrew))));
            }
            return string.Join(" · ", parts) + " steps";
        }

        private static string FormatSigned(int value) {
            // U+2212 MINUS SIGN, matching the rest of the tilt UI's signed readouts.
            return value < 0
                ? "−" + Math.Abs(value).ToString(CultureInfo.CurrentCulture)
                : "+" + value.ToString(CultureInfo.CurrentCulture);
        }

        // ---------------------------------------------------------------------------------------------------
        // The nine pad cells. Layout (matching the sensor as imaged in NINA):
        //
        //     TL   Top    TR
        //     Left All    Right
        //     BL   Bottom BR
        //
        // Axis + sign per cell, cross-checked against TiltAdapterMove.UnitEffect (wizard screws s1..s4):
        //     DiagonalA      (+1,  0, -1,  0)   s1 = TR, s3 = BL
        //     DiagonalB      ( 0, +1,  0, -1)   s2 = TL, s4 = BR
        //     EdgeVertical   (+1, +1, -1, -1)   top pair s1,s2 = TR,TL
        //     EdgeHorizontal (+1, -1, -1, +1)   right pair s1,s4 = TR,BR
        //     Backfocus      (+1, +1, +1, +1)
        // ---------------------------------------------------------------------------------------------------
        public static readonly IReadOnlyList<ManualAdjustmentTarget> All = new ReadOnlyCollection<ManualAdjustmentTarget>(new[] {
            new ManualAdjustmentTarget(
                key: "TL", label: "TL", kind: ManualMoveKind.Corner, axis: TiltMoveAxis.DiagonalB, axisSign: 1,
                padRow: 0, padColumn: 0, movedLabel: "TL", oppositeLabel: "BR",
                tooltip: "Tilts along the TL–BR diagonal. Motor 2 (TL) gets the signed amount; Motor 3 (BR) gets the opposite. One command."),
            new ManualAdjustmentTarget(
                key: "Top", label: "Top", kind: ManualMoveKind.Side, axis: TiltMoveAxis.EdgeVertical, axisSign: 1,
                padRow: 0, padColumn: 1, movedLabel: "top", oppositeLabel: "bottom",
                tooltip: "Tilts top vs bottom. Motors 2 & 1 (TL, TR) get the signed amount; Motors 4 & 3 (BL, BR) get the opposite. One command."),
            new ManualAdjustmentTarget(
                key: "TR", label: "TR", kind: ManualMoveKind.Corner, axis: TiltMoveAxis.DiagonalA, axisSign: 1,
                padRow: 0, padColumn: 2, movedLabel: "TR", oppositeLabel: "BL",
                tooltip: "Tilts along the TR–BL diagonal. Motor 1 (TR) gets the signed amount; Motor 4 (BL) gets the opposite. One command."),

            new ManualAdjustmentTarget(
                key: "Left", label: "Left", kind: ManualMoveKind.Side, axis: TiltMoveAxis.EdgeHorizontal, axisSign: -1,
                padRow: 1, padColumn: 0, movedLabel: "left", oppositeLabel: "right",
                tooltip: "Tilts right vs left. Motors 2 & 4 (TL, BL) get the signed amount; Motors 1 & 3 (TR, BR) get the opposite. One command."),
            new ManualAdjustmentTarget(
                key: "All", label: "All", kind: ManualMoveKind.Backfocus, axis: TiltMoveAxis.Backfocus, axisSign: 1,
                padRow: 1, padColumn: 1, movedLabel: null, oppositeLabel: null,
                tooltip: "Moves all four motors together by the signed amount — changes sensor spacing (backfocus), not tilt. One command."),
            new ManualAdjustmentTarget(
                key: "Right", label: "Right", kind: ManualMoveKind.Side, axis: TiltMoveAxis.EdgeHorizontal, axisSign: 1,
                padRow: 1, padColumn: 2, movedLabel: "right", oppositeLabel: "left",
                tooltip: "Tilts right vs left. Motors 1 & 3 (TR, BR) get the signed amount; Motors 2 & 4 (TL, BL) get the opposite. One command."),

            new ManualAdjustmentTarget(
                key: "BL", label: "BL", kind: ManualMoveKind.Corner, axis: TiltMoveAxis.DiagonalA, axisSign: -1,
                padRow: 2, padColumn: 0, movedLabel: "BL", oppositeLabel: "TR",
                tooltip: "Tilts along the TR–BL diagonal. Motor 4 (BL) gets the signed amount; Motor 1 (TR) gets the opposite. One command."),
            new ManualAdjustmentTarget(
                key: "Bottom", label: "Bottom", kind: ManualMoveKind.Side, axis: TiltMoveAxis.EdgeVertical, axisSign: -1,
                padRow: 2, padColumn: 1, movedLabel: "bottom", oppositeLabel: "top",
                tooltip: "Tilts top vs bottom. Motors 4 & 3 (BL, BR) get the signed amount; Motors 2 & 1 (TL, TR) get the opposite. One command."),
            new ManualAdjustmentTarget(
                key: "BR", label: "BR", kind: ManualMoveKind.Corner, axis: TiltMoveAxis.DiagonalB, axisSign: -1,
                padRow: 2, padColumn: 2, movedLabel: "BR", oppositeLabel: "TL",
                tooltip: "Tilts along the TL–BR diagonal. Motor 3 (BR) gets the signed amount; Motor 2 (TL) gets the opposite. One command."),
        });

        public static ManualAdjustmentTarget ByKey(string key) {
            var match = All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
            if (match == null) {
                throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown manual adjustment pad target.");
            }
            return match;
        }

        /// <summary>The badge text the preview shows, matching the approval dialog's Corner / Side / Backfocus vocabulary.</summary>
        public string KindLabel {
            get {
                switch (Kind) {
                    case ManualMoveKind.Corner: return "Corner";
                    case ManualMoveKind.Side: return "Side";
                    case ManualMoveKind.Backfocus: return "Backfocus";
                    default: throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown manual move kind.");
                }
            }
        }
    }
}
