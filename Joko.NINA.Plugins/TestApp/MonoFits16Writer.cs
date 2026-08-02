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
using System.Globalization;
using System.IO;
using System.Text;

namespace TestApp {

    /// <summary>
    /// A single FITS header record's content (keyword, value, optional comment) — NOT yet padded to a card. FITS
    /// values here are already-formatted text (e.g. "T", "16", "32768", "'ASI183MM'"): this type only knows how
    /// to lay a keyword/value/comment out in the standard 80-byte fixed-field record, not how to format any
    /// particular data type, so numeric formatting and string quoting are the caller's job.
    /// </summary>
    public readonly struct FitsCard {

        public FitsCard(string keyword, string value, string comment = null) {
            if (string.IsNullOrEmpty(keyword)) throw new ArgumentException("Keyword is required.", nameof(keyword));
            if (keyword.Length > 8) throw new ArgumentException($"FITS keyword '{keyword}' exceeds the 8-character limit.", nameof(keyword));
            Keyword = keyword;
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Comment = comment;
        }

        /// <summary>FITS keyword, ≤8 characters (e.g. "NAXIS1").</summary>
        public string Keyword { get; }

        /// <summary>Already-formatted value text, e.g. "3840" or "'ASI183MM'". Right-justified into the value field.</summary>
        public string Value { get; }

        /// <summary>Optional human-readable comment appended after " / ". Omit for cards that don't carry one
        /// (matching the original hand-written header, where only SIMPLE has a comment).</summary>
        public string Comment { get; }

        /// <summary>
        /// Renders this card as a FITS-standard 80-byte fixed record: an 8-character left-justified keyword
        /// field, "=", a right-justified 21-character value field (the leading space plus a 20-character value,
        /// per the FITS standard), then an optional " / comment" — identical in shape to the hand-written cards
        /// in the original <c>ExportLinearRunner.WriteHeader</c> (e.g. <c>$"NAXIS1  = {w,20}"</c>), generalized
        /// to an arbitrary keyword/value/comment. Space-padded to 80 (truncated if a caller-supplied comment runs
        /// the record long — none of the standard cards this writer builds do).
        /// </summary>
        public string ToRecord() {
            var text = Comment == null
                ? $"{Keyword,-8}={Value,21}"
                : $"{Keyword,-8}={Value,21} / {Comment}";
            return text.Length >= 80 ? text.Substring(0, 80) : text.PadRight(80);
        }
    }

    /// <summary>
    /// Pure, dependency-free writer for the minimal mono 16-bit FITS layout HocusFocus's test tooling uses:
    /// BITPIX=16 with a BZERO=32768 unsigned-integer offset (pixel values stored as big-endian signed int16,
    /// <c>value - 32768</c>), single HDU, no extensions. This is the SAME on-disk layout
    /// <see cref="ExportLinearRunner"/>'s linear-export sidecar needs and the synthetic AF bank generator will
    /// need, so the byte layout is implemented exactly once here — both callers become thin adapters over it.
    /// The layout matters beyond internal consistency: <c>tools/golden/snr_ref.py</c> parses it directly
    /// (<c>&gt;i2</c>, <c>BSCALE*value + BZERO</c>) and only tolerates the minimal card set, so any caller that
    /// feeds that tool must pass an empty <c>extraCards</c> list.
    ///
    /// <para>Deliberately has NO NINA types, no profile access, and does no I/O beyond writing the requested
    /// file — every piece of information it needs (pixels, dimensions, header cards) is passed in, so it is
    /// trivially unit-testable without a NINA profile or a running <c>Application</c>.</para>
    /// </summary>
    public static class MonoFits16Writer {

        private const int RecordLength = 80;
        private const int BlockLength = 2880;

        /// <summary>
        /// Writes a complete FITS file: the fixed SIMPLE/BITPIX/NAXIS/NAXIS1/NAXIS2/BZERO/BSCALE cards, then
        /// <paramref name="extraCards"/> in the order given, then END; followed by the pixel data encoded as
        /// BZERO=32768-offset big-endian int16. Header and data are each padded to the next 2880-byte block.
        /// </summary>
        /// <param name="path">Destination file path. Overwritten if it already exists.</param>
        /// <param name="pixels">Row-major pixel data (length must equal <paramref name="width"/> * <paramref name="height"/>).</param>
        /// <param name="width">Image width; becomes NAXIS1.</param>
        /// <param name="height">Image height; becomes NAXIS2.</param>
        /// <param name="extraCards">Additional header cards inserted between BSCALE and END, in order. Pass
        /// <see cref="Array.Empty{T}"/> for the minimal layout — e.g. <see cref="ExportLinearRunner"/>'s adapter
        /// passes none, because <c>tools/golden/snr_ref.py</c> only tolerates the minimal card set.</param>
        public static void Write(string path, ushort[] pixels, int width, int height, IReadOnlyList<FitsCard> extraCards) {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (extraCards == null) throw new ArgumentNullException(nameof(extraCards));
            long expected = (long)width * height;
            if (pixels.LongLength != expected) {
                throw new ArgumentException($"pixels.Length ({pixels.LongLength}) must equal width*height ({expected}).", nameof(pixels));
            }

            var headerBlock = BuildHeaderBlock(BuildCards(width, height, extraCards));
            var dataBlock = BuildDataBlock(pixels);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            fs.Write(headerBlock, 0, headerBlock.Length);
            fs.Write(dataBlock, 0, dataBlock.Length);
        }

        /// <summary>The fixed SIMPLE..BSCALE cards followed by the caller's extra cards, in the exact order the
        /// FITS layout requires. END is NOT included here — <see cref="BuildHeaderBlock"/> appends it, since it
        /// is a structural terminator rather than a value-bearing card.</summary>
        private static List<FitsCard> BuildCards(int width, int height, IReadOnlyList<FitsCard> extraCards) {
            var cards = new List<FitsCard>(7 + extraCards.Count) {
                new FitsCard("SIMPLE", "T", "HocusFocus linear export"),
                new FitsCard("BITPIX", "16"),
                new FitsCard("NAXIS", "2"),
                new FitsCard("NAXIS1", width.ToString(CultureInfo.InvariantCulture)),
                new FitsCard("NAXIS2", height.ToString(CultureInfo.InvariantCulture)),
                new FitsCard("BZERO", "32768"),
                new FitsCard("BSCALE", "1"),
            };
            cards.AddRange(extraCards);
            return cards;
        }

        /// <summary>
        /// Renders an ordered list of content cards into a full header block: each card as an 80-byte fixed
        /// record, followed by a terminating END record, then space-padded out to the next 2880-byte block
        /// boundary. Pure — no file access — so a unit test can assert card text and block length directly
        /// without touching the filesystem.
        /// </summary>
        public static byte[] BuildHeaderBlock(IReadOnlyList<FitsCard> cards) {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            var sb = new StringBuilder(cards.Count * RecordLength + BlockLength);
            foreach (var card in cards) {
                sb.Append(card.ToRecord());
            }
            sb.Append("END".PadRight(RecordLength));
            var header = sb.ToString();
            int pad = (BlockLength - (header.Length % BlockLength)) % BlockLength;
            if (pad > 0) {
                header += new string(' ', pad);
            }
            return Encoding.ASCII.GetBytes(header);
        }

        /// <summary>
        /// Encodes pixel data as BZERO=32768-offset signed int16, big-endian, then zero-pads out to the next
        /// 2880-byte block boundary. Pure — no file access — so a unit test can assert the BZERO round-trip
        /// directly on the returned bytes.
        ///
        /// <para>The padded buffer is sized and allocated once, then filled in place, rather than encoding into a
        /// raw array and block-copying into a larger one. A 61 MP frame (IMX455) is ~122 MB of pixel bytes, so the
        /// encode-then-copy shape would put ~244 MB on the large object heap per frame — and the synthetic bank
        /// writes thousands of frames.</para>
        /// </summary>
        public static byte[] BuildDataBlock(ushort[] pixels) {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            long rawLength = (long)pixels.Length * 2;
            int remainder = (int)(rawLength % BlockLength);
            long paddedLength = remainder == 0 ? rawLength : rawLength + (BlockLength - remainder);

            var buffer = new byte[paddedLength]; // tail beyond rawLength is already zero — that IS the padding
            for (int i = 0; i < pixels.Length; i++) {
                short s = (short)(pixels[i] - 32768); // unsigned -> signed via the BZERO offset
                buffer[i * 2] = (byte)((s >> 8) & 0xFF);  // big-endian (FITS)
                buffer[i * 2 + 1] = (byte)(s & 0xFF);
            }
            return buffer;
        }

        /// <summary>
        /// Builds the standard extra-card set the synthetic AF bank generator attaches to every frame: capture
        /// binning, pixel geometry, optics, exposure, gain, and focuser position — using the same keyword and
        /// comment conventions as NINA's own <c>FITSHeader.PopulateFromMetaData</c>, so files this writer
        /// produces read like NINA-native FITS to anything downstream that already knows those keywords.
        ///
        /// <para><b>XPIXSZ/YPIXSZ convention:</b> <paramref name="pixelSizeMicronsTimesBinning"/> must already be
        /// the PHYSICAL pixel size multiplied by the CAPTURE binning — NINA's own convention
        /// (<c>FITSHeader.PopulateFromMetaData</c> computes <c>PixelSize * BinX</c> before writing XPIXSZ).
        /// Passing the unmultiplied physical pixel size here double-counts binning, because
        /// <c>HarnessSettingsStore.PixelScaleForFrame</c> multiplies the header value by <c>Camera.BinX</c>
        /// again downstream.</para>
        /// </summary>
        public static IReadOnlyList<FitsCard> StandardCards(
                int binning,
                double pixelSizeMicronsTimesBinning,
                double focalLengthMm,
                double exposureSeconds,
                int gain,
                int focuserPosition,
                string instrument) {
            var binningText = binning.ToString(CultureInfo.InvariantCulture);
            return new[] {
                new FitsCard("XBINNING", binningText, "X axis binning factor"),
                new FitsCard("YBINNING", binningText, "Y axis binning factor"),
                new FitsCard("XPIXSZ", FormatDouble(pixelSizeMicronsTimesBinning), "[um] Pixel X axis size"),
                new FitsCard("YPIXSZ", FormatDouble(pixelSizeMicronsTimesBinning), "[um] Pixel Y axis size"),
                new FitsCard("FOCALLEN", FormatDouble(focalLengthMm), "[mm] Focal length"),
                new FitsCard("EXPTIME", FormatDouble(exposureSeconds), "[s] Exposure duration"),
                new FitsCard("GAIN", gain.ToString(CultureInfo.InvariantCulture), "Sensor gain"),
                new FitsCard("FOCUSPOS", focuserPosition.ToString(CultureInfo.InvariantCulture), "[step] Focuser position"),
                new FitsCard("INSTRUME", $"'{instrument}'", "Imaging instrument name"),
            };
        }

        /// <summary>Matches NINA's own FITSHeaderCard double formatting: at least one decimal, up to 14 more
        /// without trailing zeros, invariant culture (no thousands separators, "." as the decimal point).</summary>
        private static string FormatDouble(double value) => value.ToString("0.0##############", CultureInfo.InvariantCulture);
    }
}
