#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// Unit tests for <see cref="LabelConverter.ToFrameLabels"/> — the pure mapping from in-memory review labels to the
/// optimizer's objective-side <see cref="FrameLabels"/>/<see cref="LabelBox"/> form. Mirrors the box rule the offline
/// <c>optimize --labels</c> path uses: explicit-size boxes pass through as-is; legacy point-only boxes widen to a
/// 2·radius box centered on the point.
/// </summary>
[TestFixture]
public class LabelConverterTests {

    [Test]
    public void ToFrameLabels_Null_ReturnsNull() {
        Assert.That(LabelConverter.ToFrameLabels(null), Is.Null);
    }

    [Test]
    public void ToFrameLabels_NoPositions_ReturnsNull() {
        var run = new StarReviewRunLabels { RunId = "r", Positions = new List<StarReviewPositionLabels>() };
        Assert.That(LabelConverter.ToFrameLabels(run), Is.Null);
    }

    [Test]
    public void ToFrameLabels_ExplicitSizeBox_TakenAsIs() {
        var run = new StarReviewRunLabels {
            RunId = "r",
            Positions = new List<StarReviewPositionLabels> {
                new StarReviewPositionLabels {
                    FocuserPosition = 5000,
                    Missed = new List<StarReviewLabelBox> { new StarReviewLabelBox(100, 120, 20, 16) }
                }
            }
        };

        var result = LabelConverter.ToFrameLabels(run);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        var box = result[0].Missed.Single();
        Assert.Multiple(() => {
            Assert.That(box.X, Is.EqualTo(100).Within(1e-9));
            Assert.That(box.Y, Is.EqualTo(120).Within(1e-9));
            Assert.That(box.W, Is.EqualTo(20).Within(1e-9));
            Assert.That(box.H, Is.EqualTo(16).Within(1e-9));
        });
    }

    [Test]
    public void ToFrameLabels_LegacyPointBox_WidenedToTwiceRadiusCenteredOnPoint() {
        // A point-only box (no W/H) carrying x=200,y=300, with a per-position radius of 5 => a 10x10 box centered on
        // (200,300), i.e. top-left (195,295).
        var run = new StarReviewRunLabels {
            RunId = "r",
            RadiusPx = 99, // run-level default; the per-position override below must win
            Positions = new List<StarReviewPositionLabels> {
                new StarReviewPositionLabels {
                    FocuserPosition = 5000,
                    RadiusPx = 5.0,
                    Missed = new List<StarReviewLabelBox> { new StarReviewLabelBox { X = 200, Y = 300 } }
                }
            }
        };

        var box = LabelConverter.ToFrameLabels(run)[0].Missed.Single();

        Assert.Multiple(() => {
            Assert.That(box.W, Is.EqualTo(10).Within(1e-9), "2·radius width");
            Assert.That(box.H, Is.EqualTo(10).Within(1e-9), "2·radius height");
            Assert.That(box.X, Is.EqualTo(195).Within(1e-9), "centered: top-left = x - radius");
            Assert.That(box.Y, Is.EqualTo(295).Within(1e-9), "centered: top-left = y - radius");
            // Containment sanity: the original point lies inside the widened box.
            Assert.That(box.Contains(200, 300), Is.True);
        });
    }

    [Test]
    public void ToFrameLabels_LegacyPoint_FallsBackToRunRadius_ThenDefault() {
        // No per-position radius => run radius (8) drives the widening.
        var runWithRadius = new StarReviewRunLabels {
            RunId = "r",
            RadiusPx = 8.0,
            Positions = new List<StarReviewPositionLabels> {
                new StarReviewPositionLabels {
                    FocuserPosition = 1,
                    Missed = new List<StarReviewLabelBox> { new StarReviewLabelBox { X = 0, Y = 0 } }
                }
            }
        };
        var boxRunRadius = LabelConverter.ToFrameLabels(runWithRadius)[0].Missed.Single();
        Assert.That(boxRunRadius.W, Is.EqualTo(16).Within(1e-9), "2·runRadius");

        // No per-position AND no run radius => the default radius.
        var runNoRadius = new StarReviewRunLabels {
            RunId = "r",
            Positions = new List<StarReviewPositionLabels> {
                new StarReviewPositionLabels {
                    FocuserPosition = 1,
                    Missed = new List<StarReviewLabelBox> { new StarReviewLabelBox { X = 0, Y = 0 } }
                }
            }
        };
        var boxDefault = LabelConverter.ToFrameLabels(runNoRadius)[0].Missed.Single();
        Assert.That(boxDefault.W, Is.EqualTo(2.0 * LabelConverter.DefaultRadiusPx).Within(1e-9), "2·defaultRadius");
    }

    [Test]
    public void ToFrameLabels_FocuserPositionPreserved() {
        var run = new StarReviewRunLabels {
            RunId = "r",
            Positions = new List<StarReviewPositionLabels> {
                new StarReviewPositionLabels { FocuserPosition = 4321, Missed = new List<StarReviewLabelBox> { new StarReviewLabelBox(1, 2, 3, 4) } },
                new StarReviewPositionLabels { FocuserPosition = 8765, Missed = new List<StarReviewLabelBox> { new StarReviewLabelBox(5, 6, 7, 8) } }
            }
        };

        var result = LabelConverter.ToFrameLabels(run);

        Assert.Multiple(() => {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].FocuserPosition, Is.EqualTo(4321));
            Assert.That(result[1].FocuserPosition, Is.EqualTo(8765));
        });
    }

    [Test]
    public void ToFrameLabels_MapsEachCategoryToTheRightList() {
        var run = new StarReviewRunLabels {
            RunId = "r",
            Positions = new List<StarReviewPositionLabels> {
                new StarReviewPositionLabels {
                    FocuserPosition = 5000,
                    Missed = new List<StarReviewLabelBox> { new StarReviewLabelBox(10, 10, 4, 4) },
                    ShouldReject = new List<StarReviewLabelBox> { new StarReviewLabelBox(20, 20, 4, 4), new StarReviewLabelBox(30, 30, 4, 4) },
                    WronglyRejected = new List<StarReviewLabelBox> { new StarReviewLabelBox(40, 40, 4, 4) }
                }
            }
        };

        var fl = LabelConverter.ToFrameLabels(run).Single();

        Assert.Multiple(() => {
            Assert.That(fl.Missed.Count, Is.EqualTo(1));
            Assert.That(fl.Missed[0].X, Is.EqualTo(10).Within(1e-9));
            Assert.That(fl.ShouldReject.Count, Is.EqualTo(2));
            Assert.That(fl.ShouldReject[0].X, Is.EqualTo(20).Within(1e-9));
            Assert.That(fl.ShouldReject[1].X, Is.EqualTo(30).Within(1e-9));
            Assert.That(fl.WronglyRejected.Count, Is.EqualTo(1));
            Assert.That(fl.WronglyRejected[0].X, Is.EqualTo(40).Within(1e-9));
        });
    }

    [Test]
    public void ToFrameLabels_EmptyCategoryLists_AreEmptyNotNull() {
        var run = new StarReviewRunLabels {
            RunId = "r",
            Positions = new List<StarReviewPositionLabels> {
                new StarReviewPositionLabels { FocuserPosition = 5000 } // all lists default-empty
            }
        };

        var fl = LabelConverter.ToFrameLabels(run).Single();

        Assert.Multiple(() => {
            Assert.That(fl.Missed, Is.Not.Null.And.Empty);
            Assert.That(fl.ShouldReject, Is.Not.Null.And.Empty);
            Assert.That(fl.WronglyRejected, Is.Not.Null.And.Empty);
        });
    }
}
