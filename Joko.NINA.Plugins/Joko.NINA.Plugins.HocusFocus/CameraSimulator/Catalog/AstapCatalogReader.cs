#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog {

    /// <summary>
    /// Reads ASTAP/HNSKY star-database cell files. Ports the record decode of <c>open_database</c> +
    /// <c>readdatabase1476</c> from the MPL-2.0 reference reader <c>unit_star_database.pas</c>
    /// (han-k59/astap): a 110-byte text header whose last byte is the record size, followed by fixed-width
    /// records sorted bright → faint with periodic "magnitude header" marker records (RA bytes = FF FF FF).
    ///
    /// <para>Record layout (little-endian):</para>
    /// <list type="bullet">
    /// <item>RA: 3-byte unsigned, ra = raw · 2π / (256³−1).</item>
    /// <item>Dec: two's-complement 3-byte, dec = ((dec9 &lt;&lt; 16) | (dec8 &lt;&lt; 8) | dec7) · (π/2) / (128·256²−1),
    /// where the low two bytes (dec7, dec8) live in the star record and the high byte (dec9) comes from the
    /// preceding magnitude-header record.</item>
    /// <item>Magnitude: carried by the header records — <c>mag = (byte − 16) / 10</c>.</item>
    /// <item>6-byte records add a signed color byte, (B−V)·50 (database version 2 only).</item>
    /// </list>
    /// Record sizes 5 and 6 are decoded per the reference; 7/9/10/11 (which carry extra designation bytes)
    /// decode their RA/Dec from the same leading 5 bytes and carry no color. Other sizes are rejected.
    /// </summary>
    public sealed class AstapCatalogReader : IAstapCatalogReader {
        private const int HeaderBytes = 110;
        private const int RecordSizeByteIndex = 109; // database2[109] in the reference (0-based within the header)
        private const int VersionByteIndex = 108;    // database2[108]
        private const int RaMarker = 0xFFFFFF;       // magnitude-header record marker (RA bytes all 0xFF)
        private const int Dec9Bias = 128;            // magnitude-header dec9 byte is stored offset by +128
        private const int MagOffset = 16;            // magnitude byte offset (keeps Sirius etc. non-negative)
        private const double MagScale = 10.0;        // magnitude byte holds magnitude × 10
        private const double ColorScale = 50.0;      // (B−V) stored as a signed byte × 50

        private const double RaScale = 2.0 * Math.PI / ((256.0 * 256.0 * 256.0) - 1.0);         // 2π / (256³−1)
        private const double DecScale = (Math.PI * 0.5) / ((128.0 * 256.0 * 256.0) - 1.0);      // (π/2) / (128·256²−1)
        private const double DegPerRad = 180.0 / Math.PI;
        private const double RadPerDeg = Math.PI / 180.0;

        private static readonly Regex CellSuffix = new Regex(@"^(?<prefix>.+)_(?<cell>\d{4})\.(?<ext>1476|290)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly string catalogDirectory;

        /// <param name="catalogDirectory">Folder containing the ASTAP database cell files (e.g. the ASTAP
        /// install directory). The database prefix and partitioning are auto-detected from its contents.</param>
        public AstapCatalogReader(string catalogDirectory) {
            catalogDirectory = catalogDirectory ?? throw new ArgumentNullException(nameof(catalogDirectory));
            this.catalogDirectory = catalogDirectory;
        }

        /// <inheritdoc/>
        public IEnumerable<CatalogStar> Query(double centerRaDeg, double centerDecDeg, double fovDeg, double limitingMagnitude) {
            // Eager validation so folder/database problems surface at the call site, before enumeration.
            var db = DetectDatabase();

            var raRad = NormalizeRaRadians(centerRaDeg * RadPerDeg);
            var decRad = centerDecDeg * RadPerDeg;
            var fovRad = fovDeg * RadPerDeg;
            if (fovRad <= 0.0) {
                throw new ArgumentOutOfRangeException(nameof(fovDeg), fovDeg, "FOV must be positive.");
            }

            // F44 — a field WIDER THAN ONE CELL cannot be covered by find_areas: that routine clamps the field to
            // one cell and samples only its four corners, so a 57° render field came back as a ~10° patch and the
            // rest of the sensor rendered starless. Below the cap the two agree by construction (a box at most one
            // cell across touches at most four cells, and its corners hit all of them), so small fields keep the
            // byte-accurate reference path and every existing result is unchanged.
            var maxFov = AstapCellGeometry.MaxFovRadians(db.Partitioning);
            var cells = fovRad > maxFov
                ? AstapCellGeometry.FindAreasCovering(raRad, decRad, fovRad, db.Partitioning)
                : AstapCellGeometry.FindAreas(raRad, decRad, fovRad, db.Partitioning);
            return QueryCells(cells, db, raRad, decRad, fovRad, limitingMagnitude);
        }

        private IEnumerable<CatalogStar> QueryCells(
            IReadOnlyList<AstapAreaSelection> cells, DetectedDatabase db,
            double raRad, double decRad, double fovRad, double limitingMagnitude) {

            var cosCenterDec = Math.Cos(decRad);
            var halfFov = fovRad / 2.0;

            foreach (var cell in cells) {
                var path = Path.Combine(catalogDirectory, db.Prefix + "_" + cell.CellFileName);
                if (!File.Exists(path)) {
                    throw new FileNotFoundException(
                        $"Required ASTAP cell file is missing: '{path}'. The '{db.Prefix}' {AstapCellGeometry.Extension(db.Partitioning)} database appears incomplete.",
                        path);
                }

                foreach (var star in ReadCell(path, raRad, decRad, cosCenterDec, halfFov, limitingMagnitude)) {
                    yield return star;
                }
            }
        }

        private static IEnumerable<CatalogStar> ReadCell(
            string path, double centerRaRad, double centerDecRad, double cosCenterDec, double halfFov, double limitingMagnitude) {

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[HeaderBytes];
            try {
                stream.ReadExactly(header, 0, HeaderBytes);
            } catch (EndOfStreamException ex) {
                throw new InvalidDataException(
                    $"ASTAP cell file '{path}' is truncated: header is shorter than {HeaderBytes} bytes.", ex);
            }

            var recordSize = ParseRecordSize(header, path);
            var version = header[VersionByteIndex];

            var record = new byte[recordSize];
            var currentMag = double.NegativeInfinity; // set by the first magnitude-header record
            var currentDec9 = 0;                       // high Dec byte, signed, from the last header record

            using var buffered = new BufferedStream(stream, 1 << 16);
            while (true) {
                var read = ReadFull(buffered, record, recordSize);
                if (read == 0) {
                    yield break; // clean end of file
                }
                if (read < recordSize) {
                    throw new InvalidDataException(
                        $"ASTAP cell file '{path}' ends with a partial {recordSize}-byte record ({read} bytes).");
                }

                var raRaw = record[0] | (record[1] << 8) | (record[2] << 16);
                if (raRaw == RaMarker) {
                    // Magnitude-header record: FF FF FF <dec9+128> <mag*10+16> [...]. currentMag/currentDec9
                    // carry to every star record that follows, until the next header record.
                    currentDec9 = record[3] - Dec9Bias;
                    currentMag = (record[4] - MagOffset) / MagScale;
                    // Records are sorted bright → faint, so once a header passes the limit we are done.
                    if (currentMag > limitingMagnitude) {
                        yield break;
                    }
                    continue;
                }

                var raRad = raRaw * RaScale;
                var decRaw = (currentDec9 << 16) | (record[4] << 8) | record[3];
                var decRad = decRaw * DecScale;

                // Box filter around the pointing (reference readdatabase1476 FOV test).
                var deltaRa = Math.Abs(raRad - centerRaRad);
                if (deltaRa > Math.PI) {
                    deltaRa = (2.0 * Math.PI) - deltaRa;
                }
                if (deltaRa * cosCenterDec >= halfFov || Math.Abs(decRad - centerDecRad) >= halfFov) {
                    continue;
                }

                double? colorBV = null;
                if (recordSize == 6 && version == 2) {
                    colorBV = (sbyte)record[5] / ColorScale;
                }

                var coordinates = new Coordinates(raRad * DegPerRad, decRad * DegPerRad, Epoch.J2000, Coordinates.RAType.Degrees);
                yield return new CatalogStar(coordinates, currentMag, colorBV);
            }
        }

        private static int ParseRecordSize(byte[] header, string path) {
            var raw = header[RecordSizeByteIndex];
            var recordSize = raw == (byte)' ' ? 11 : raw; // ' ' ⇒ default 11 (reference)
            switch (recordSize) {
                case 5:
                case 6:
                case 7:
                case 9:
                case 10:
                case 11:
                    return recordSize;
                default:
                    throw new InvalidDataException(
                        $"ASTAP cell file '{path}' declares an unsupported record size {recordSize} (header byte {RecordSizeByteIndex}).");
            }
        }

        private DetectedDatabase DetectDatabase() {
            if (!Directory.Exists(catalogDirectory)) {
                throw new DirectoryNotFoundException($"ASTAP catalog folder not found: '{catalogDirectory}'.");
            }

            // Prefer the finer .1476 partitioning when both are present.
            if (TryDetect(AstapPartitioning.Astap1476, out var db) || TryDetect(AstapPartitioning.Astap290, out db)) {
                return db;
            }

            throw new FileNotFoundException(
                $"No ASTAP star database (.1476 or .290 cell files) found in '{catalogDirectory}'. Install an ASTAP star database (e.g. G05, D50, D80).");
        }

        private bool TryDetect(AstapPartitioning partitioning, out DetectedDatabase db) {
            var ext = AstapCellGeometry.Extension(partitioning);

            // Prefer the canonical first cell '<prefix>_0101.<ext>'; fall back to any '<prefix>_NNNN.<ext>'
            // so a partly-present database (missing 0101) is still discoverable.
            string prefix = null;
            foreach (var file in Directory.EnumerateFiles(catalogDirectory, "*." + ext)) {
                var name = Path.GetFileName(file);
                var m = CellSuffix.Match(name);
                if (!m.Success) {
                    continue;
                }
                var thisPrefix = m.Groups["prefix"].Value;
                if (string.Equals(m.Groups["cell"].Value, "0101", StringComparison.Ordinal)) {
                    prefix = thisPrefix; // canonical anchor — prefer it and stop looking
                    break;
                }
                prefix ??= thisPrefix; // remember the first candidate as a fallback
            }

            if (prefix != null) {
                db = new DetectedDatabase(prefix, partitioning);
                return true;
            }

            db = default;
            return false;
        }

        private static double NormalizeRaRadians(double raRad) {
            var twoPi = 2.0 * Math.PI;
            var r = raRad % twoPi;
            if (r < 0.0) {
                r += twoPi;
            }
            return r;
        }

        // Reads up to count bytes, looping until filled or EOF. Returns the number of bytes actually read.
        private static int ReadFull(Stream stream, byte[] buffer, int count) {
            var total = 0;
            while (total < count) {
                var n = stream.Read(buffer, total, count - total);
                if (n == 0) {
                    break;
                }
                total += n;
            }
            return total;
        }

        private readonly struct DetectedDatabase {

            public DetectedDatabase(string prefix, AstapPartitioning partitioning) {
                Prefix = prefix;
                Partitioning = partitioning;
            }

            public string Prefix { get; }
            public AstapPartitioning Partitioning { get; }
        }
    }
}
