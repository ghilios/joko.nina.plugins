#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NUnit.Framework;
using System;
using System.Collections.Generic;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Golden {

    [TestFixture]
    public class MonoFits16WriterTests {

        [Test]
        public void StandardCards_ProducesExactTextAndOrder() {
            var cards = MonoFits16Writer.StandardCards(
                binning: 2,
                pixelSizeMicronsTimesBinning: 5.8,
                focalLengthMm: 2000.0,
                exposureSeconds: 1.5,
                gain: 100,
                focuserPosition: 5000,
                instrument: "Synthetic AF Bank");

            var expected = new[] {
                "XBINNING=                    2 / X axis binning factor                          ",
                "YBINNING=                    2 / Y axis binning factor                          ",
                "XPIXSZ  =                  5.8 / [um] Pixel X axis size                         ",
                "YPIXSZ  =                  5.8 / [um] Pixel Y axis size                         ",
                "FOCALLEN=               2000.0 / [mm] Focal length                              ",
                "EXPTIME =                  1.5 / [s] Exposure duration                          ",
                "GAIN    =                  100 / Sensor gain                                    ",
                "FOCUSPOS=                 5000 / [step] Focuser position                        ",
                "INSTRUME=  'Synthetic AF Bank' / Imaging instrument name                        ",
            };

            Assert.That(cards, Has.Count.EqualTo(expected.Length));
            Assert.Multiple(() => {
                for (int i = 0; i < expected.Length; i++) {
                    Assert.That(cards[i].ToRecord(), Is.EqualTo(expected[i]), $"card[{i}] ({cards[i].Keyword})");
                }
            });
        }

        [TestCase("SIMPLE", "T", "HocusFocus linear export")]
        [TestCase("BITPIX", "16", null)]
        [TestCase("NAXIS1", "3840", null)]
        [TestCase("INSTRUME", "'Synthetic AF Bank'", "Imaging instrument name")]
        public void ToRecord_IsExactly80Characters(string keyword, string value, string comment) {
            var card = new FitsCard(keyword, value, comment);
            Assert.That(card.ToRecord(), Has.Length.EqualTo(80));
        }

        [Test]
        public void BuildHeaderBlock_MinimalCardSet_PadsToOneBlock() {
            var cards = new List<FitsCard> {
                new FitsCard("SIMPLE", "T", "HocusFocus linear export"),
                new FitsCard("BITPIX", "16"),
                new FitsCard("NAXIS", "2"),
                new FitsCard("NAXIS1", "3840"),
                new FitsCard("NAXIS2", "2160"),
                new FitsCard("BZERO", "32768"),
                new FitsCard("BSCALE", "1"),
            };

            var block = MonoFits16Writer.BuildHeaderBlock(cards);

            Assert.Multiple(() => {
                Assert.That(block.Length % 2880, Is.EqualTo(0), "header block must be a 2880-byte multiple");
                Assert.That(block.Length, Is.EqualTo(2880), "8 records (7 cards + END) fit in a single block");
            });
        }

        [Test]
        public void BuildHeaderBlock_ManyCards_StillPadsToA2880Multiple() {
            var cards = new List<FitsCard>();
            for (int i = 0; i < 40; i++) {
                cards.Add(new FitsCard($"CARD{i:D3}", i.ToString()));
            }
            // 40 content cards + 1 END = 41 records * 80 = 3280 bytes, spanning two 2880-byte blocks.

            var block = MonoFits16Writer.BuildHeaderBlock(cards);

            Assert.Multiple(() => {
                Assert.That(block.Length % 2880, Is.EqualTo(0));
                Assert.That(block.Length, Is.EqualTo(5760));
            });
        }

        [TestCase((ushort)0)]
        [TestCase((ushort)65535)]
        [TestCase((ushort)32768)]
        [TestCase((ushort)1)]
        [TestCase((ushort)40000)]
        public void BuildDataBlock_BzeroRoundTrips(ushort value) {
            var block = MonoFits16Writer.BuildDataBlock(new[] { value });

            // Decode exactly as tools/golden/snr_ref.py does: big-endian signed int16, then BSCALE*v + BZERO.
            short stored = (short)((block[0] << 8) | block[1]);
            int decoded = stored + 32768;

            Assert.That(decoded, Is.EqualTo(value));
        }

        [Test]
        public void BuildDataBlock_PadsToNext2880Boundary() {
            var pixels = new ushort[100]; // 200 raw bytes, well under one block.
            var block = MonoFits16Writer.BuildDataBlock(pixels);
            Assert.That(block.Length, Is.EqualTo(2880));
        }

        [Test]
        public void BuildDataBlock_ExactBlockMultiple_AddsNoExtraPadding() {
            var pixels = new ushort[1440]; // 1440*2 = 2880 bytes exactly.
            var block = MonoFits16Writer.BuildDataBlock(pixels);
            Assert.That(block.Length, Is.EqualTo(2880));
        }

        [Test]
        public void Write_MismatchedPixelCount_Throws() {
            var pixels = new ushort[4];
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".fits");
            Assert.Throws<ArgumentException>(() => MonoFits16Writer.Write(path, pixels, 3, 3, Array.Empty<FitsCard>()));
        }
    }
}
