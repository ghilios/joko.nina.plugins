using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NINA.Astrometry;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

// End-to-end reader tests that decode REAL bytes: each fixture is a valid ASTAP/HNSKY cell file (110-byte
// header + fixed-width records with FF-FF-FF magnitude-header markers) written to a temp directory, then
// read back through AstapCatalogReader. The encode helper independently implements the documented byte
// format (the inverse of the reader's decode), so agreement proves the decode is faithful.
[TestFixture]
public class AstapCatalogReaderTests {
    private const double Deg = Math.PI / 180.0;
    private const string Prefix = "d50";

    // Documented ASTAP scale factors (design doc §"ASTAP catalog format", reference readdatabase1476).
    private const double RaScale = 2.0 * Math.PI / ((256.0 * 256.0 * 256.0) - 1.0);
    private const double DecScale = (Math.PI * 0.5) / ((128.0 * 256.0 * 256.0) - 1.0);

    private string tempDir;

    [SetUp]
    public void SetUp() {
        tempDir = Path.Combine(Path.GetTempPath(), "hf_astap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    [TearDown]
    public void TearDown() {
        try {
            if (tempDir != null && Directory.Exists(tempDir)) {
                Directory.Delete(tempDir, recursive: true);
            }
        } catch {
            // best-effort cleanup
        }
    }

    private sealed class StarSpec {
        public double RaDeg;
        public double DecDeg;
        public double Mag;
        public double? ColorBV;
    }

    // ---- Happy path: 6-byte records with color -------------------------------------------------------

    [Test]
    public void Query_SixByteRecords_DecodesPositionMagnitudeAndColor() {
        const double raC = 30.0;
        const double decC = 40.0;
        const double fov = 0.5;

        var a = new StarSpec { RaDeg = 30.05, DecDeg = 40.05, Mag = 5.0, ColorBV = 0.5 };
        var outOfBox = new StarSpec { RaDeg = 30.0, DecDeg = 40.4, Mag = 6.0, ColorBV = -0.2 };
        var b = new StarSpec { RaDeg = 29.95, DecDeg = 39.95, Mag = 8.3, ColorBV = -0.6 };
        var faint = new StarSpec { RaDeg = 30.0, DecDeg = 40.0, Mag = 15.0, ColorBV = 0.0 };
        var starsBrightToFaint = new[] { a, outOfBox, b, faint };

        WriteDatabase(raC, decC, fov, AstapPartitioning.Astap1476, recordSize: 6, version: 2, starsBrightToFaint);

        var reader = new AstapCatalogReader(tempDir);
        var result = reader.Query(raC, decC, fov, limitingMagnitude: 10.0).ToList();

        Assert.That(result.Count, Is.EqualTo(2), "outOfBox filtered by FOV; faint dropped by mag early-out");
        AssertMatches(result[0], a, expectColor: true);
        AssertMatches(result[1], b, expectColor: true);
        Assert.That(result[0].Coordinates.Epoch, Is.EqualTo(Epoch.J2000));
    }

    // ---- Happy path: 5-byte records, no color --------------------------------------------------------

    [Test]
    public void Query_FiveByteRecords_DecodePositionMagnitude_ColorNull() {
        const double raC = 30.0;
        const double decC = 40.0;
        const double fov = 0.5;

        var a = new StarSpec { RaDeg = 30.05, DecDeg = 40.05, Mag = 5.0 };
        var b = new StarSpec { RaDeg = 29.95, DecDeg = 39.95, Mag = 8.3 };
        WriteDatabase(raC, decC, fov, AstapPartitioning.Astap1476, recordSize: 5, version: 0, new[] { a, b });

        var reader = new AstapCatalogReader(tempDir);
        var result = reader.Query(raC, decC, fov, limitingMagnitude: 12.0).ToList();

        Assert.That(result.Count, Is.EqualTo(2));
        AssertMatches(result[0], a, expectColor: false);
        AssertMatches(result[1], b, expectColor: false);
        Assert.That(result[0].ColorBV, Is.Null);
        Assert.That(result[1].ColorBV, Is.Null);
    }

    // ---- Bright -> faint early-out at the limiting magnitude -----------------------------------------

    [Test]
    public void Query_StopsAtLimitingMagnitude() {
        const double raC = 30.0;
        const double decC = 40.0;
        const double fov = 0.5;

        var a = new StarSpec { RaDeg = 30.02, DecDeg = 40.02, Mag = 5.0 };
        var b = new StarSpec { RaDeg = 29.98, DecDeg = 39.98, Mag = 8.3 };
        WriteDatabase(raC, decC, fov, AstapPartitioning.Astap1476, recordSize: 5, version: 0, new[] { a, b });

        var reader = new AstapCatalogReader(tempDir);

        // Limit between the two magnitudes: only the bright star is returned; the reader stops before B.
        var bright = reader.Query(raC, decC, fov, limitingMagnitude: 6.0).ToList();
        Assert.That(bright.Select(s => s.Magnitude), Is.EqualTo(new[] { 5.0 }));

        // Limit above both: both returned.
        var both = reader.Query(raC, decC, fov, limitingMagnitude: 9.0).ToList();
        Assert.That(both.Count, Is.EqualTo(2));

        // Limit below the brightest: nothing returned.
        var none = reader.Query(raC, decC, fov, limitingMagnitude: 4.0).ToList();
        Assert.That(none, Is.Empty);
    }

    // ---- FOV box filter ------------------------------------------------------------------------------

    [Test]
    public void Query_FiltersStarsOutsideFov() {
        const double raC = 30.0;
        const double decC = 40.0;
        const double fov = 0.5; // half-FOV 0.25 deg

        var inside = new StarSpec { RaDeg = 30.1, DecDeg = 40.1, Mag = 5.0 };
        var farDec = new StarSpec { RaDeg = 30.0, DecDeg = 41.0, Mag = 6.0 };  // 1 deg north
        var farRa = new StarSpec { RaDeg = 31.5, DecDeg = 40.0, Mag = 7.0 };   // ~1.5 deg east
        WriteDatabase(raC, decC, fov, AstapPartitioning.Astap1476, recordSize: 5, version: 0,
            new[] { inside, farDec, farRa });

        var reader = new AstapCatalogReader(tempDir);
        var result = reader.Query(raC, decC, fov, limitingMagnitude: 12.0).ToList();

        Assert.That(result.Count, Is.EqualTo(1));
        AssertMatches(result[0], inside, expectColor: false);
    }

    // ---- Descriptive errors --------------------------------------------------------------------------

    [Test]
    public void Query_MissingFolder_ThrowsDirectoryNotFound() {
        var reader = new AstapCatalogReader(Path.Combine(tempDir, "does", "not", "exist"));
        var ex = Assert.Throws<DirectoryNotFoundException>(() => reader.Query(30, 40, 0.5, 10));
        Assert.That(ex.Message, Does.Contain("catalog folder"));
    }

    [Test]
    public void Query_NoDatabaseFiles_ThrowsFileNotFound() {
        // Empty (but existing) directory.
        var reader = new AstapCatalogReader(tempDir);
        var ex = Assert.Throws<FileNotFoundException>(() => reader.Query(30, 40, 0.5, 10));
        Assert.That(ex.Message, Does.Contain(".1476").Or.Contain("database"));
    }

    [Test]
    public void Query_TruncatedHeader_ThrowsInvalidData() {
        // Name the file for the cell the query selects, but write fewer than 110 bytes.
        var cell = SingleCell(30, 40, 0.1, AstapPartitioning.Astap1476);
        File.WriteAllBytes(Path.Combine(tempDir, Prefix + "_" + cell), new byte[50]);

        var reader = new AstapCatalogReader(tempDir);
        var ex = Assert.Throws<InvalidDataException>(() => reader.Query(30, 40, 0.1, 10).ToList());
        Assert.That(ex.Message, Does.Contain("truncated").Or.Contain("header"));
    }

    [Test]
    public void Query_UnsupportedRecordSize_ThrowsInvalidData() {
        var cell = SingleCell(30, 40, 0.1, AstapPartitioning.Astap1476);
        var header = new byte[110];
        for (var i = 0; i < header.Length; i++) header[i] = (byte)' ';
        header[109] = 8; // unsupported record size
        File.WriteAllBytes(Path.Combine(tempDir, Prefix + "_" + cell), header);

        var reader = new AstapCatalogReader(tempDir);
        var ex = Assert.Throws<InvalidDataException>(() => reader.Query(30, 40, 0.1, 10).ToList());
        Assert.That(ex.Message, Does.Contain("record size"));
    }

    [Test]
    public void Query_DetectsPrefixAndPartitioning_From290Files() {
        const double raC = 120.0;
        const double decC = -20.0;
        const double fov = 0.5;
        var a = new StarSpec { RaDeg = 120.05, DecDeg = -20.05, Mag = 4.0 };
        WriteDatabase(raC, decC, fov, AstapPartitioning.Astap290, recordSize: 5, version: 0, new[] { a });

        var reader = new AstapCatalogReader(tempDir);
        var result = reader.Query(raC, decC, fov, limitingMagnitude: 10.0).ToList();
        Assert.That(result.Count, Is.EqualTo(1));
        AssertMatches(result[0], a, expectColor: false);
    }

    // ---- One magnitude marker carried across MULTIPLE following star records -------------------------

    [Test]
    public void Query_SingleMarker_CarriesMagnitudeAndDec9AcrossMultipleStars() {
        const double raC = 200.0;
        const double decC = -30.0; // negative dec -> the carried high byte is a two's-complement negative
        const double fov = 0.5;
        const double sharedMag = 7.0;
        const int recordSize = 5;

        // Encode the shared dec (−30°) once; all three star records reuse dec7/dec8 and rely on the SINGLE
        // preceding marker for both the magnitude and the high dec byte (dec9). A reader that reset the
        // carry after the first record, or dropped the carried high byte, would decode records 2–3 wrong.
        var decRaw = (int)Math.Round((decC * Deg) / DecScale);
        var dec9 = decRaw >> 16;
        var dec7 = decRaw & 0xFF;
        var dec8 = (decRaw >> 8) & 0xFF;
        Assert.That(dec9, Is.LessThan(0), "test intends a negative (two's-complement) high byte");

        var raDegs = new[] { 200.05, 200.0, 199.95 };
        var section = new List<byte>();
        section.AddRange(MarkerRecord(recordSize, dec9, sharedMag)); // exactly ONE marker
        foreach (var raDeg in raDegs) {
            section.AddRange(StarRecordRaw(recordSize, RawRa(raDeg), dec7, dec8));
        }

        WriteRawDatabase(raC, decC, fov, AstapPartitioning.Astap1476, recordSize, version: 0, section.ToArray());

        var reader = new AstapCatalogReader(tempDir);
        var result = reader.Query(raC, decC, fov, limitingMagnitude: 10.0).ToList();

        Assert.That(result.Count, Is.EqualTo(3), "all three stars under one marker must decode");
        Assert.Multiple(() => {
            foreach (var star in result) {
                Assert.That(star.Magnitude, Is.EqualTo(sharedMag).Within(1e-9), "carried magnitude");
                Assert.That(star.Coordinates.Dec, Is.EqualTo(decC).Within(1e-3), "carried dec9 high byte");
            }
            Assert.That(result.Select(s => s.Coordinates.RADegrees).OrderBy(x => x).ToArray(),
                Is.EqualTo(raDegs.OrderBy(x => x).ToArray()).Within(1e-3), "distinct RAs preserved");
        });
    }

    // ---- Independent decode oracle: hardcoded bytes -> hand-computed (RA, Dec), absolute scale ------

    [Test]
    public void Query_DecodesHardcodedSiriusVector_LocksAbsoluteScale() {
        // Straight from the reference (unit_star_database.pas) worked example for Sirius, record size 5:
        //   RA  bytes C3 06 48 -> (195 + 6·256 + 72·256²)·24 / (256³−1) = 6.75247662 h  = 101.2871493°
        //   Dec bytes D7 39 with dec9 = 0xE8 (−24) -> (215 + 57·256 − 24·256²)·90 / (128·256²−1) = −16.7161401°
        // The expected values are computed independently of the reader's RaScale/DecScale constants, so a
        // wrong ABSOLUTE scale cannot cancel out and hide.
        const int recordSize = 5;
        const double siriusRaDeg = 101.2871493;
        const double siriusDecDeg = -16.7161401;
        const double siriusMag = -1.0;

        var section = new List<byte>();
        section.AddRange(MarkerRecord(recordSize, dec9: -24, mag: siriusMag)); // dec9 byte 0xE8 = −24 + 128
        section.AddRange(new byte[] { 0xC3, 0x06, 0x48, 0xD7, 0x39 });         // Sirius star record

        WriteRawDatabase(siriusRaDeg, siriusDecDeg, 0.5, AstapPartitioning.Astap1476, recordSize, version: 0, section.ToArray());

        var reader = new AstapCatalogReader(tempDir);
        var star = reader.Query(siriusRaDeg, siriusDecDeg, 0.5, limitingMagnitude: 5.0).Single();

        Assert.Multiple(() => {
            Assert.That(star.Coordinates.RADegrees, Is.EqualTo(siriusRaDeg).Within(2e-4), "absolute RA scale");
            Assert.That(star.Coordinates.Dec, Is.EqualTo(siriusDecDeg).Within(2e-4), "absolute Dec scale");
            Assert.That(star.Magnitude, Is.EqualTo(siriusMag).Within(1e-9));
        });
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static void AssertMatches(CatalogStar actual, StarSpec expected, bool expectColor) {
        Assert.Multiple(() => {
            Assert.That(actual.Coordinates.RADegrees, Is.EqualTo(expected.RaDeg).Within(1e-3), "RA deg");
            Assert.That(actual.Coordinates.Dec, Is.EqualTo(expected.DecDeg).Within(1e-3), "Dec deg");
            Assert.That(actual.Magnitude, Is.EqualTo(expected.Mag).Within(1e-9), "magnitude");
            if (expectColor && expected.ColorBV.HasValue) {
                Assert.That(actual.ColorBV, Is.Not.Null);
                Assert.That(actual.ColorBV.Value, Is.EqualTo(expected.ColorBV.Value).Within(1e-9), "B-V");
            } else {
                Assert.That(actual.ColorBV, Is.Null);
            }
        });
    }

    // Returns the single cell file name FindAreas selects for a tiny FOV (asserts exactly one).
    private static string SingleCell(double raDeg, double decDeg, double fovDeg, AstapPartitioning p) {
        var cells = AstapCellGeometry.FindAreas(raDeg * Deg, decDeg * Deg, fovDeg * Deg, p);
        Assert.That(cells.Count, Is.EqualTo(1), "test point must be interior to a single cell");
        return cells[0].CellFileName;
    }

    // Writes a fixture database: the primary (first) selected cell gets the star records; any other
    // selected cells get valid header-only files so the reader never hits a missing required cell.
    private void WriteDatabase(double raDeg, double decDeg, double fovDeg, AstapPartitioning p,
        int recordSize, int version, IReadOnlyList<StarSpec> starsBrightToFaint) {

        var cells = AstapCellGeometry.FindAreas(raDeg * Deg, decDeg * Deg, fovDeg * Deg, p);
        Assert.That(cells.Count, Is.GreaterThanOrEqualTo(1));

        for (var i = 0; i < cells.Count; i++) {
            var path = Path.Combine(tempDir, Prefix + "_" + cells[i].CellFileName);
            var bytes = i == 0
                ? BuildCellBytes(recordSize, version, starsBrightToFaint)
                : BuildCellBytes(recordSize, version, Array.Empty<StarSpec>());
            File.WriteAllBytes(path, bytes);
        }
    }

    // Independent encoder of the ASTAP byte format: 110-byte header then, per star, a magnitude-header
    // record (FF FF FF, dec9+128, mag*10+16) followed by the star record (RA 3 bytes LE, dec7, dec8,
    // [color]). Stars must be supplied bright -> faint (non-decreasing magnitude), as real files are sorted.
    private static byte[] BuildCellBytes(int recordSize, int version, IReadOnlyList<StarSpec> stars) {
        var buf = new List<byte>();
        buf.AddRange(HeaderBytes110(recordSize, version));

        foreach (var s in stars) {
            var decRaw = (int)Math.Round((s.DecDeg * Deg) / DecScale);
            var dec9 = decRaw >> 16;                 // signed high byte
            var magTimes10 = (int)Math.Round(s.Mag * 10.0);

            // Magnitude-header record.
            var hrec = new byte[recordSize];
            hrec[0] = 0xFF;
            hrec[1] = 0xFF;
            hrec[2] = 0xFF;
            hrec[3] = (byte)((dec9 + 128) & 0xFF);
            hrec[4] = (byte)((magTimes10 + 16) & 0xFF);
            buf.AddRange(hrec);

            // Star record.
            var rawRA = (int)Math.Round((s.RaDeg * Deg) / RaScale);
            if (rawRA >= 0xFFFFFF) rawRA = 0xFFFFFE; // never collide with the marker
            if (rawRA < 0) rawRA = 0;

            var srec = new byte[recordSize];
            srec[0] = (byte)(rawRA & 0xFF);
            srec[1] = (byte)((rawRA >> 8) & 0xFF);
            srec[2] = (byte)((rawRA >> 16) & 0xFF);
            srec[3] = (byte)(decRaw & 0xFF);          // dec7 (low)
            srec[4] = (byte)((decRaw >> 8) & 0xFF);   // dec8 (mid)
            if (recordSize >= 6 && s.ColorBV.HasValue) {
                srec[5] = (byte)(sbyte)Math.Round(s.ColorBV.Value * 50.0);
            }
            buf.AddRange(srec);
        }

        return buf.ToArray();
    }

    private static byte[] HeaderBytes110(int recordSize, int version) {
        var header = new byte[110];
        for (var i = 0; i < header.Length; i++) header[i] = (byte)' ';
        header[108] = (byte)version;   // database_version (byte 108)
        header[109] = (byte)recordSize; // record size (byte 109)
        return header;
    }

    private static byte[] MarkerRecord(int recordSize, int dec9, double mag) {
        var rec = new byte[recordSize];
        rec[0] = 0xFF;
        rec[1] = 0xFF;
        rec[2] = 0xFF;
        rec[3] = (byte)((dec9 + 128) & 0xFF);
        rec[4] = (byte)(((int)Math.Round(mag * 10.0) + 16) & 0xFF);
        return rec;
    }

    private static byte[] StarRecordRaw(int recordSize, int rawRA, int dec7, int dec8) {
        var rec = new byte[recordSize];
        rec[0] = (byte)(rawRA & 0xFF);
        rec[1] = (byte)((rawRA >> 8) & 0xFF);
        rec[2] = (byte)((rawRA >> 16) & 0xFF);
        rec[3] = (byte)(dec7 & 0xFF);
        rec[4] = (byte)(dec8 & 0xFF);
        return rec;
    }

    private static int RawRa(double raDeg) => (int)Math.Round((raDeg * Deg) / RaScale);

    // Writes a fixture whose primary (first) selected cell gets a hand-built record section (header +
    // raw bytes); any other selected cells get valid header-only files.
    private void WriteRawDatabase(double raDeg, double decDeg, double fovDeg, AstapPartitioning p,
        int recordSize, int version, byte[] recordSection) {

        var cells = AstapCellGeometry.FindAreas(raDeg * Deg, decDeg * Deg, fovDeg * Deg, p);
        Assert.That(cells.Count, Is.GreaterThanOrEqualTo(1));
        for (var i = 0; i < cells.Count; i++) {
            var path = Path.Combine(tempDir, Prefix + "_" + cells[i].CellFileName);
            var buf = new List<byte>();
            buf.AddRange(HeaderBytes110(recordSize, version));
            if (i == 0) {
                buf.AddRange(recordSection);
            }
            File.WriteAllBytes(path, buf.ToArray());
        }
    }
}
