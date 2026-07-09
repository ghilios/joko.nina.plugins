# AutoFocus Report Watcher Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `PathUtility.WriteAllTextAtomic` stage its temp file outside the target directory so that NINA's `FileSystemWatcher` raises `Created` (not `Renamed`) for new AutoFocus reports, restoring live updates of the AF chart dropdown.

**Architecture:** Windows reports an intra-directory rename as `RENAMED_OLD_NAME`/`RENAMED_NEW_NAME`, which .NET surfaces as `FileSystemWatcher.Renamed`. NINA's `AutoFocusToolVM` subscribes only to `Created`/`Deleted`. Moving the staging file to a **sibling** directory of the target (same parent ⇒ same volume ⇒ `File.Move` remains a true atomic rename, verified via preserved NTFS file ID) makes the destination see `FILE_ACTION_ADDED` instead, which raises `Created`. Content is complete and no write handle is open at that instant, so the original sharing-violation `IOException` stays fixed. The change is confined to `PathUtility`; a defaulted `tempDirectory` parameter keeps all three call sites untouched.

**Tech Stack:** C# / .NET 8.0-windows7.0, NUnit 4.4.0, `System.IO.FileSystemWatcher`.

**Design spec:** `docs/autofocus-report-watcher-design.md`

---

## Environment Notes (read first)

There is **no `dotnet` in WSL** and the test project targets `net8.0-windows7.0`. Tests must be run on the
Windows side via the `mcp__windows-mcp__PowerShell` tool, against the repo over the WSL share.

Test command (use `timeout: 600`):

```powershell
Set-Location '\\wsl.localhost\Ubuntu-20.04\home\ghilios\src\hocus-focus'
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~PathUtilityTests"
```

Full-suite command (Project Invariant: run after every code change), also `timeout: 600`:

```powershell
Set-Location '\\wsl.localhost\Ubuntu-20.04\home\ghilios\src\hocus-focus'
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```

Baseline as of writing: `--filter FullyQualifiedName~PathUtilityTests` ⇒ **Passed! Failed: 0, Passed: 8**.

Git: never push to `develop`. Work on branch `ghilios/af-report-watcher-created-event`. Commit with the
privacy email (exact command given in each commit step).

---

## File Structure

- **Modify:** `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/PathUtility.cs`
  Sole responsibility change: `WriteAllTextAtomic` gains an optional `tempDirectory`, defaults it to a
  sibling of the target directory, and its `<remarks>` are corrected (they currently assert a rationale
  that is the actual bug).
- **Modify:** `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/PathUtilityTests.cs`
  Adds the watcher regression test plus temp-location tests; existing atomic-write tests are restructured
  to clean up the new sibling temp directory.

No call sites change: `InspectorVM.cs:1476`, `InspectorVM.cs:1536`, `HocusFocusVM.cs:456` all keep calling
`WriteAllTextAtomic(path, reportText)`.

---

## Task 1: Regression test — the watcher must see `Created`

This is the assertion that was missing and would have caught the bug. It reproduces NINA's exact watcher
configuration (`AutoFocusToolVM.cs:88-96`).

**Files:**
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/PathUtilityTests.cs`

- [ ] **Step 1: Create the branch**

```bash
cd /home/ghilios/src/hocus-focus
git checkout -b ghilios/af-report-watcher-created-event
```

- [ ] **Step 2: Add the test helpers and the failing regression test**

In `PathUtilityTests.cs`, replace the `using` block at the top of the file with:

```csharp
using System;
using System.IO;
using System.Threading;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
```

Then add these two private helpers directly beneath the existing `Combine` helper:

```csharp
    private static string NewRoot() {
        var root = Path.Combine(Path.GetTempPath(), "hf-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root) {
        try { Directory.Delete(root, recursive: true); } catch { }
    }
```

And append this test to the class (before the closing brace):

```csharp
    [Test]
    public void WriteAllTextAtomic_RaisesCreatedEvent_OnNinaStyleWatcher() {
        var root = NewRoot();
        var reportDir = Path.Combine(root, "AutoFocus");
        Directory.CreateDirectory(reportDir);
        try {
            using var createdSignal = new ManualResetEventSlim(false);
            string createdName = null;

            // Mirrors NINA core's AutoFocusToolVM.reportFileWatcher exactly.
            using (var watcher = new FileSystemWatcher {
                Path = reportDir,
                NotifyFilter = NotifyFilters.FileName,
                Filter = "*.json",
                IncludeSubdirectories = false
            }) {
                watcher.Created += (_, e) => { createdName = e.Name; createdSignal.Set(); };
                watcher.EnableRaisingEvents = true;

                PathUtility.WriteAllTextAtomic(Path.Combine(reportDir, "report.json"), "{\"ok\":true}");

                Assert.That(
                    createdSignal.Wait(TimeSpan.FromSeconds(10)),
                    Is.True,
                    "NINA's AutoFocusToolVM subscribes only to Created/Deleted. Staging the temp file inside the "
                    + "watched directory turns the publish into an intra-directory rename, which Windows reports as "
                    + "Renamed, so the AF chart dropdown never updates until restart.");
            }

            Assert.That(createdName, Is.EqualTo("report.json"));
        } finally {
            Cleanup(root);
        }
    }
```

- [ ] **Step 3: Run the test to verify it fails (RED)**

```powershell
Set-Location '\\wsl.localhost\Ubuntu-20.04\home\ghilios\src\hocus-focus'
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~WriteAllTextAtomic_RaisesCreatedEvent_OnNinaStyleWatcher"
```

Expected: **FAIL** — `Failed: 1`, with the assertion message above (the wait times out after 10 s because
the current code raises `Renamed`, never `Created`).

Do **not** proceed to Task 2 until you have observed this failure.

---

## Task 2: Stage the temp file outside the target directory

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/PathUtility.cs:56-92`

- [ ] **Step 1: Replace `WriteAllTextAtomic` and its doc comment**

Replace the entire block from the `/// <summary>` above `WriteAllTextAtomic` through the closing brace of
`WriteAllTextAtomic` with:

```csharp
        /// <summary>
        /// The suffix appended to a target directory's name to form the default staging directory.
        /// </summary>
        public const string TempDirectorySuffix = ".hf-tmp";

        /// <summary>
        /// Writes text to <paramref name="path"/> atomically: the content is written to a temp file in a
        /// staging directory outside the target directory, which is then renamed into place, so a concurrent
        /// reader never observes a half-written file or an open write handle on the final path.
        /// </summary>
        /// <param name="path">The destination file.</param>
        /// <param name="contents">The content to write.</param>
        /// <param name="tempDirectory">
        /// Staging directory for the temp file. It MUST be on the same volume as <paramref name="path"/> for the
        /// rename to be atomic, and MUST NOT be the target directory itself (see remarks). Defaults to a sibling
        /// of the target directory named <c>&lt;targetDirName&gt;<see cref="TempDirectorySuffix"/></c>.
        /// </param>
        /// <remarks>
        /// Two separate hazards are in play, and both must be respected.
        /// <para>
        /// First: a plain <see cref="File.WriteAllText(string, string)"/> keeps a <see cref="FileAccess.Write"/>
        /// handle open on the destination. NINA core's <c>AutoFocusToolVM</c> watches the AutoFocus report
        /// directory and immediately <c>File.OpenText</c>s new reports with <see cref="FileShare.Read"/>; that
        /// share mode does not admit the still-open write access, so the read fails with a sharing violation
        /// ("being used by another process"). Staging the content in a temp file and renaming it into place means
        /// the final report never has an open write handle.
        /// </para>
        /// <para>
        /// Second: the staging file must live OUTSIDE the target directory. Windows reports an intra-directory
        /// rename as <c>FILE_ACTION_RENAMED_OLD_NAME</c>/<c>FILE_ACTION_RENAMED_NEW_NAME</c>, which .NET raises as
        /// <see cref="FileSystemWatcher.Renamed"/> — not <see cref="FileSystemWatcher.Created"/>. NINA's watcher
        /// subscribes only to Created and Deleted, so a same-directory rename publishes a report that NINA never
        /// notices until it restarts. Renaming in from another directory makes the destination observe
        /// <c>FILE_ACTION_ADDED</c>, which raises Created. Because the source and destination share a volume, the
        /// move remains a true atomic rename rather than a copy.
        /// </para>
        /// <para>
        /// Keeping the temp file out of the target directory also means a process crash between the write and the
        /// rename cannot strand a <c>.tmp</c> file where NINA's unfiltered
        /// <c>Directory.GetFiles(ReportDirectory)</c> would list it as a bogus chart.
        /// </para>
        /// </remarks>
        public static void WriteAllTextAtomic(string path, string contents, string tempDirectory = null) {
            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentNullException(nameof(path));
            }

            var fullPath = Path.GetFullPath(path);
            var targetDirectory = Path.GetDirectoryName(fullPath);
            var stagingDirectory = string.IsNullOrEmpty(tempDirectory)
                ? GetDefaultTempDirectory(targetDirectory)
                : tempDirectory;
            Directory.CreateDirectory(stagingDirectory);

            var tempPath = Path.Combine(
                stagingDirectory,
                Path.GetFileNameWithoutExtension(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                File.WriteAllText(tempPath, contents);
                // overwrite: a stale report from the same second (filenames are second-resolution) must not block it.
                File.Move(tempPath, fullPath, overwrite: true);
            } catch {
                // Best-effort cleanup so a failed rename doesn't strand a temp file; preserve the original error.
                try {
                    if (File.Exists(tempPath)) {
                        File.Delete(tempPath);
                    }
                } catch { }
                throw;
            }
        }

        /// <summary>
        /// A sibling of <paramref name="targetDirectory"/>, which is guaranteed to be on the same volume (same
        /// parent) and outside the target directory regardless of whether a watcher sets IncludeSubdirectories.
        /// Falls back to the target directory itself when it is a volume root and therefore has no sibling; in
        /// that degenerate case the write is still atomic but no Created event is raised.
        /// </summary>
        private static string GetDefaultTempDirectory(string targetDirectory) {
            var parent = Path.GetDirectoryName(targetDirectory);
            if (string.IsNullOrEmpty(parent)) {
                return targetDirectory;
            }

            return Path.Combine(parent, Path.GetFileName(targetDirectory) + TempDirectorySuffix);
        }
```

- [ ] **Step 2: Run the regression test to verify it passes (GREEN)**

```powershell
Set-Location '\\wsl.localhost\Ubuntu-20.04\home\ghilios\src\hocus-focus'
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~WriteAllTextAtomic_RaisesCreatedEvent_OnNinaStyleWatcher"
```

Expected: **PASS** — `Failed: 0, Passed: 1`.

---

## Task 3: Cover the temp-file location and the explicit `tempDirectory`

The existing atomic-write tests pass `dir` as the target directory, so the new default staging directory
becomes `<dir>.hf-tmp` — a sibling that their `finally` block does not delete. They must be restructured to
create a root, or they leak a directory into `%TEMP%` on every run.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/PathUtilityTests.cs`

- [ ] **Step 1: Restructure the two existing atomic-write tests to use a root directory**

Replace `WriteAllTextAtomic_WritesContent_AndLeavesNoTempFile` and `WriteAllTextAtomic_OverwritesExistingFile`
in their entirety with:

```csharp
    [Test]
    public void WriteAllTextAtomic_WritesContent_AndLeavesNoTempFile() {
        var root = NewRoot();
        var dir = Path.Combine(root, "AutoFocus");
        Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "2026-07-03--22-53-42.json");
            PathUtility.WriteAllTextAtomic(path, "{\"ok\":true}");

            Assert.Multiple(() => {
                Assert.That(File.ReadAllText(path), Is.EqualTo("{\"ok\":true}"));
                // The rename must clean up after itself: no leftover temp file, and the final file keeps its extension.
                Assert.That(Directory.GetFiles(dir, "*.tmp"), Is.Empty, "no temp file should remain after an atomic write");
                Assert.That(Directory.GetFiles(dir), Has.Length.EqualTo(1));
            });
        } finally {
            Cleanup(root);
        }
    }

    [Test]
    public void WriteAllTextAtomic_OverwritesExistingFile() {
        var root = NewRoot();
        var dir = Path.Combine(root, "AutoFocus");
        Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "report.json");
            File.WriteAllText(path, "old");
            PathUtility.WriteAllTextAtomic(path, "new");

            Assert.That(File.ReadAllText(path), Is.EqualTo("new"));
        } finally {
            Cleanup(root);
        }
    }
```

- [ ] **Step 2: Add the temp-location tests**

Append to the class:

```csharp
    [Test]
    public void WriteAllTextAtomic_StagesTempFileOutsideTargetDirectory() {
        var root = NewRoot();
        var reportDir = Path.Combine(root, "AutoFocus");
        Directory.CreateDirectory(reportDir);
        try {
            PathUtility.WriteAllTextAtomic(Path.Combine(reportDir, "report.json"), "{\"ok\":true}");

            var expectedStaging = Path.Combine(root, "AutoFocus" + PathUtility.TempDirectorySuffix);
            Assert.Multiple(() => {
                Assert.That(
                    Directory.GetFiles(reportDir),
                    Has.Length.EqualTo(1),
                    "the watched directory must only ever contain the final report");
                Assert.That(
                    Directory.Exists(expectedStaging),
                    Is.True,
                    "the temp file must be staged in a sibling directory so the publish is a cross-directory rename");
                Assert.That(Directory.GetFiles(expectedStaging), Is.Empty, "staging directory should be left empty");
            });
        } finally {
            Cleanup(root);
        }
    }

    [Test]
    public void WriteAllTextAtomic_ExplicitTempDirectory_IsUsedAndLeftEmpty() {
        var root = NewRoot();
        var reportDir = Path.Combine(root, "AutoFocus");
        var stagingDir = Path.Combine(root, "staging");
        Directory.CreateDirectory(reportDir);
        try {
            var path = Path.Combine(reportDir, "report.json");
            PathUtility.WriteAllTextAtomic(path, "hello", stagingDir);

            Assert.Multiple(() => {
                Assert.That(File.ReadAllText(path), Is.EqualTo("hello"));
                Assert.That(Directory.Exists(stagingDir), Is.True, "explicit staging directory should be created on demand");
                Assert.That(Directory.GetFiles(stagingDir), Is.Empty, "staging directory should be left empty");
            });
        } finally {
            Cleanup(root);
        }
    }
```

- [ ] **Step 3: Run the whole `PathUtilityTests` fixture**

```powershell
Set-Location '\\wsl.localhost\Ubuntu-20.04\home\ghilios\src\hocus-focus'
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~PathUtilityTests"
```

Expected: **PASS** — `Failed: 0, Passed: 12` (8 pre-existing + 3 new + 1 regression test from Task 1).

---

## Task 4: Full suite, then commit

**Files:** none (verification + commit)

- [ ] **Step 1: Run the full test suite (Project Invariant)**

```powershell
Set-Location '\\wsl.localhost\Ubuntu-20.04\home\ghilios\src\hocus-focus'
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```

Expected: `Failed: 0`. If anything fails, fix the cause — do not skip or mark tests expected-to-fail.

- [ ] **Step 2: Commit**

```bash
cd /home/ghilios/src/hocus-focus
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/PathUtility.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/PathUtilityTests.cs \
        docs/autofocus-report-watcher-design.md \
        plans/autofocus-report-watcher-plan.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -F- <<'EOF'
fix(af): stage report temp file outside the watched directory

WriteAllTextAtomic staged its temp file as a sibling in the target directory and
renamed it into place. Windows reports an intra-directory rename as
FILE_ACTION_RENAMED_OLD_NAME/FILE_ACTION_RENAMED_NEW_NAME, which .NET raises as
FileSystemWatcher.Renamed. NINA core's AutoFocusToolVM subscribes only to Created
and Deleted, so every AutoFocus report published since the atomic-write change was
invisible in the AF chart dropdown until NINA restarted and re-enumerated the
directory.

Stage the temp file in a sibling directory of the target instead. The destination
then observes FILE_ACTION_ADDED and raises Created. Sharing the parent directory
guarantees the same volume, so File.Move remains a true atomic rename (verified:
the NTFS file id survives the move), and the final report still never carries an
open write handle -- the sharing-violation IOException that motivated the atomic
write stays fixed.

Two latent defects go away with it: overwriting a same-second report used to raise
Deleted + Renamed, which removed the chart entry and never re-added it; and a temp
file stranded by a crash could no longer be listed as a bogus chart by NINA's
unfiltered Directory.GetFiles(ReportDirectory).

Add a regression test that drives NINA's exact watcher configuration and asserts a
Created event is raised.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
```

- [ ] **Step 3: Push and open a PR**

```bash
cd /home/ghilios/src/hocus-focus
git push -u origin ghilios/af-report-watcher-created-event
gh pr create --base develop --title "fix(af): stage report temp file outside the watched directory" --body "$(cat <<'EOF'
## Summary

New AutoFocus reports did not appear in NINA's AF chart dropdown until NINA was restarted.

`PathUtility.WriteAllTextAtomic` staged its temp file inside the target directory and renamed it into place. Windows reports an intra-directory rename as a `RENAMED_OLD_NAME`/`RENAMED_NEW_NAME` pair, which .NET surfaces as `FileSystemWatcher.Renamed`. NINA core's `AutoFocusToolVM` subscribes only to `Created` and `Deleted`, so the report was written correctly and then ignored.

The fix stages the temp file in a **sibling** directory of the target. The destination then sees `FILE_ACTION_ADDED` → `Created`. Same parent means same volume, so `File.Move` is still a true atomic rename (verified: the NTFS file id is preserved across the move), and the final report still never has an open write handle — so the sharing-violation `IOException` that motivated the atomic write remains fixed.

Two latent defects are fixed along the way:
- Overwriting a same-second report raised `Deleted` + `Renamed`, which *removed* the chart entry and never re-added it.
- A `.tmp` stranded by a crash sat in `ReportDirectory`, which NINA lists via an unfiltered `Directory.GetFiles`, producing a bogus chart entry.

Design spec: `docs/autofocus-report-watcher-design.md`

## Test plan

- New regression test drives NINA's exact watcher config (`NotifyFilter = FileName`, `Filter = "*.json"`, `IncludeSubdirectories = false`) and asserts `Created` fires. It fails on the old code and passes on the new.
- New tests cover the sibling staging directory and the explicit `tempDirectory` override.
- Full suite green.

## Follow-ups (not in this PR)

- Upstream NINA: `AutoFocusToolVM` should also handle `Renamed`, so core is robust against any writer that publishes by atomic rename.
- `InspectorVM`'s two report paths omit the profile GUID, so those reports list under every profile.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Out of Scope (do not bundle)

- Upstream PR to NINA adding a `Renamed` handler to `AutoFocusToolVM`.
- Adding the profile GUID to `InspectorVM`'s two report filenames.
