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
using System.Linq;
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

        // ── G8c: the full synth-bank card set/order + block padding, pure via BuildHeaderBlock ─────────────

        /// <summary>The exact 7 fixed cards <c>MonoFits16Writer.Write</c>'s private <c>BuildCards</c> prepends to
        /// any extra cards, reproduced here since that helper is private — this is what every caller (the linear
        /// export AND the synth-bank generator) shares before their own extra cards diverge.</summary>
        private static List<FitsCard> FixedCards(int width, int height) => new List<FitsCard> {
            new FitsCard("SIMPLE", "T", "HocusFocus linear export"),
            new FitsCard("BITPIX", "16"),
            new FitsCard("NAXIS", "2"),
            new FitsCard("NAXIS1", width.ToString()),
            new FitsCard("NAXIS2", height.ToString()),
            new FitsCard("BZERO", "32768"),
            new FitsCard("BSCALE", "1"),
        };

        [Test]
        public void SynthBankCardSet_KeywordOrder_MatchesTheDesignExactly() {
            // docs/synthetic-af-bank-design.md "G3. FITS writer": SIMPLE, BITPIX, NAXIS, NAXIS1, NAXIS2, BZERO,
            // BSCALE, XBINNING, YBINNING, XPIXSZ, YPIXSZ, FOCALLEN, EXPTIME, GAIN, FOCUSPOS, INSTRUME, END.
            var cards = FixedCards(3840, 2160);
            cards.AddRange(MonoFits16Writer.StandardCards(
                binning: 2, pixelSizeMicronsTimesBinning: 5.8, focalLengthMm: 2000.0,
                exposureSeconds: 3.0, gain: 100, focuserPosition: 12000, instrument: "HocusFocusSynthBank D11_rc10_585_afbin2"));

            var expectedOrder = new[] {
                "SIMPLE", "BITPIX", "NAXIS", "NAXIS1", "NAXIS2", "BZERO", "BSCALE",
                "XBINNING", "YBINNING", "XPIXSZ", "YPIXSZ", "FOCALLEN", "EXPTIME", "GAIN", "FOCUSPOS", "INSTRUME"
            };
            Assert.That(cards.Select(c => c.Keyword), Is.EqualTo(expectedOrder));
        }

        [Test]
        public void SynthBankCardSet_HeaderBlock_Is17RecordsPaddedToOneBlock() {
            var cards = FixedCards(3840, 2160);
            cards.AddRange(MonoFits16Writer.StandardCards(
                binning: 2, pixelSizeMicronsTimesBinning: 5.8, focalLengthMm: 2000.0,
                exposureSeconds: 3.0, gain: 100, focuserPosition: 12000, instrument: "HocusFocusSynthBank D11_rc10_585_afbin2"));

            var block = MonoFits16Writer.BuildHeaderBlock(cards);

            Assert.Multiple(() => {
                // 7 fixed + 9 StandardCards + 1 END = 17 records * 80 bytes = 1360 bytes -- one 2880-byte block.
                Assert.That(block.Length % 2880, Is.EqualTo(0), "header block must be a 2880-byte multiple");
                Assert.That(block.Length, Is.EqualTo(2880));

                // Every record is intact at its 80-byte offset -- keyword text lands exactly where FITS expects it.
                var text = System.Text.Encoding.ASCII.GetString(block);
                for (int i = 0; i < cards.Count; i++) {
                    var record = text.Substring(i * 80, 80);
                    Assert.That(record, Does.StartWith(cards[i].Keyword.PadRight(8) + "="), $"record[{i}] ({cards[i].Keyword})");
                }
                var endRecord = text.Substring(cards.Count * 80, 80);
                Assert.That(endRecord, Does.StartWith("END"));

                // The remainder of the block (after the 17 records) must be pure space padding.
                var usedBytes = (cards.Count + 1) * 80;
                Assert.That(text.Substring(usedBytes), Is.EqualTo(new string(' ', block.Length - usedBytes)));
            });
        }

        [Test]
        public void SynthBankCardSet_WithLongInstrumentId_TruncatesTheOneRecord_RecordCountAndBlockSizeUnaffected() {
            // A dataset id long enough to push the INSTRUME record's rendered length past 80 chars, exercising
            // FitsCard.ToRecord's truncate-at-80 path together with the block-padding math around it: the record
            // COUNT (17), not any one record's content length, is what drives the block count, so this still
            // fits in a single 2880-byte block just like the short-id case.
            var cards = FixedCards(9576, 6388);
            cards.AddRange(MonoFits16Writer.StandardCards(
                binning: 1, pixelSizeMicronsTimesBinning: 3.76, focalLengthMm: 2563.0,
                exposureSeconds: 4.0, gain: 100, focuserPosition: 12000,
                instrument: "HocusFocusSynthBank D14_cdk14_2563mm_e47_a_much_longer_instrument_tag_than_usual"));
            var instrumeCard = cards.Single(c => c.Keyword == "INSTRUME");

            var block = MonoFits16Writer.BuildHeaderBlock(cards);

            Assert.Multiple(() => {
                Assert.That(instrumeCard.ToRecord(), Has.Length.EqualTo(80), "ToRecord truncates rather than overflowing the record");
                Assert.That(block.Length % 2880, Is.EqualTo(0));
                Assert.That(block.Length, Is.EqualTo(2880), "still 17 records regardless of how long any one comment is");
            });
        }
    }
}
