# AutoFocus report atomic write vs. NINA's report-directory watcher

## Status

Root cause **confirmed** (code inspection + empirical test on Windows). Fix **proposed, not yet implemented**.

## Symptom

After an AutoFocus run completes, the new report does not appear in NINA's AutoFocus chart
dropdown. It only shows up after NINA is restarted. The report file itself is written correctly
and the calibration data is unaffected.

## Root cause

Commit `8beeed5` ("fix(af): write AutoFocus reports atomically to avoid chart-load IOException")
introduced `PathUtility.WriteAllTextAtomic`, which writes the report to a temp file **in the same
directory** as the target and then renames it into place:

```csharp
// PathUtility.cs:75-82
var directory = Path.GetDirectoryName(path);
var tempPath = Path.Combine(directory ?? string.Empty, ... + ".tmp");
File.WriteAllText(tempPath, contents);
File.Move(tempPath, path, overwrite: true);
```

Windows' `ReadDirectoryChangesW` reports an **intra-directory** rename as the pair
`FILE_ACTION_RENAMED_OLD_NAME` / `FILE_ACTION_RENAMED_NEW_NAME`. .NET surfaces that as
`FileSystemWatcher.Renamed` — **not** `Created`.

NINA's `AutoFocusToolVM` (`NINA/ViewModel/Imaging/AutoFocusToolVM.cs:88-96`) builds its watcher with
`NotifyFilter = NotifyFilters.FileName`, `Filter = "*.json"`, and subscribes to **only** `Created`
and `Deleted`. There is no `Renamed` handler, so the report never enters `ChartList`. It appears
only on the next startup, when `InitializeChartList()` re-enumerates the directory.

That is exactly the reported behaviour.

### Empirical confirmation

Run on Windows (PowerShell 7.5.8) against a `FileSystemWatcher` configured identically to NINA's
(`NotifyFilter = FileName`, `Filter = *.json`, `IncludeSubdirectories = false`):

| Write strategy | Event(s) raised on the `.json` target |
|---|---|
| `File.WriteAllText` directly (what NINA core does) | `Created` |
| **same-dir `.tmp` + `File.Move`** — *today's plugin code* | **`Renamed`** ← no `Created`, dropdown never updates |
| same-dir `.tmp` + `File.Move(overwrite)` onto an **existing** target | **`Deleted`** + `Renamed` ← actively *removes* the entry |
| temp in a **subdirectory** of the watched dir + `File.Move` | `Created` |
| temp in a **sibling directory** + `File.Move` | `Created` |
| temp in a sibling dir + `File.Move(overwrite)` onto existing target | `Deleted` + `Created` |
| `CreateHardLink(temp → final)` then delete temp | `Created` |
| `File.Copy(temp → final)` | `Created` |

A cross-directory move on the same volume was verified to be a **true atomic rename, not a
copy+delete** — the NTFS file ID is preserved across the move:

```
File ID before move (sibling dir): 0x00000000000000000028000000135128
File ID after  move (watched dir): 0x00000000000000000028000000135128
```

The rename pair is only emitted when *both* endpoints are inside the watch scope. Move the temp
file out of the watched directory and the destination sees `FILE_ACTION_ADDED` instead.

## Two further defects found in the same code

1. **The overwrite path deletes the dropdown entry.** With the temp file in the watched directory,
   `File.Move(..., overwrite: true)` onto an existing report raises `Deleted` + `Renamed`. NINA's
   `ReportFileWatcher_Deleted` removes the chart from `ChartList` and nothing re-adds it. Report
   filenames are second-resolution, and the two `InspectorVM` write sites
   (`InspectorVM.cs:1473`, `InspectorVM.cs:1532`) don't include the profile GUID at all, so a
   same-second collision is plausible.

2. **A stranded `.tmp` becomes a bogus dropdown entry.** The temp file lives in `ReportDirectory`,
   and `AutoFocusToolVM.InitializeChartList()` calls `Directory.GetFiles(ReportDirectory)` with **no
   filter** — every file, not just `*.json`. If the process dies between `WriteAllText` and
   `File.Move`, the leftover `.tmp` is listed as a chart at the next startup. (`CoreUtil.DirectoryCleanup`
   only prunes at 180 days.)

Both disappear once the temp file lives outside `ReportDirectory`.

## The constraint that rules out the obvious fixes

The original `IOException` that `8beeed5` fixed is real and cannot be worked around from the writer
side. NINA reads the report with `File.OpenText`, i.e. `FileAccess.Read` + **`FileShare.Read`**.
Windows' share check requires the *reader's* share mode to admit the *writer's* existing access:
`FileShare.Read` does not admit `FileAccess.Write`. So **any** open write handle on the final path
at the moment NINA reacts to `Created` is a sharing violation — and `Created` fires the instant the
file is created, before content is written. Widening the writer's `FileShare` cannot help.

So the fix must satisfy both:

- **(a)** the final path must be *created* (`FILE_ACTION_ADDED`), not renamed into existence; and
- **(b)** it must already hold complete content, with no write handle, at the moment it is created.

Only a rename/link of an already-complete file from **outside the watch scope** does both.

## Options

| # | Approach | `Created` fires | No write-handle race | Verdict |
|---|---|---|---|---|
| A | Revert to `File.WriteAllText` (match NINA core, `AutoFocusVM.cs:491`) | yes | **no** | Reject — reintroduces the observed `IOException` |
| B | **Keep `File.Move`, put the temp file outside the watched directory** | yes | yes | **Recommended** |
| C | `CreateHardLink(temp → final)`, then delete temp | yes | yes | Works, but needs P/Invoke and is NTFS-only. Unnecessary given B |
| D | `File.Copy(temp → final)` | yes | **no** | Reject — destination is created *then* written; same race, worse |
| E | Upstream: add a `Renamed` handler to `AutoFocusToolVM` | n/a | n/a | Correct long-term; do **in addition**, doesn't help until NINA ships it |

### Recommended: option B

Change `WriteAllTextAtomic` so the temp file is created in a directory that is on the same volume as
the target (so `File.Move` stays a true atomic rename) but outside the watched directory.

Candidate temp locations:

- **Sibling of the target directory** (e.g. `%LOCALAPPDATA%\NINA\AutoFocus.hf-tmp\`) — same parent,
  therefore same volume; out of the watch scope regardless of `IncludeSubdirectories`. **Preferred.**
- Subdirectory of the target directory (e.g. `ReportDirectory\.hf-tmp\`) — same volume trivially, and
  verified to raise `Created` today. But it is out of scope *only because* NINA sets
  `IncludeSubdirectories = false`; if core ever flips that flag we silently regress to `Renamed`.
- `Path.GetTempPath()` — **reject**. It may sit on a different volume, in which case `File.Move`
  degrades to copy+delete and loses atomicity (or throws).

Proposed signature, keeping the utility general:

```csharp
public static void WriteAllTextAtomic(string path, string contents, string tempDirectory = null)
```

Default `tempDirectory` to the sibling `<targetDirName>.hf-tmp`, creating it on demand. If the target
directory has no parent (a drive root), fall back to the target directory itself and document that the
fallback does not produce a `Created` event.

Note the residual behaviour under B: overwriting an existing report raises `Deleted` + `Created`, so
NINA removes and re-adds the entry. That is correct, and strictly better than today's `Deleted` +
`Renamed` (which removes it permanently).

## Verification plan

- **Regression test (the important one):** a unit test that runs a real `FileSystemWatcher` configured
  exactly like NINA's (`NotifyFilter = FileName`, `Filter = "*.json"`, `IncludeSubdirectories = false`)
  over the target directory, calls `WriteAllTextAtomic`, and asserts a **`Created`** event is raised for
  the target. This is the assertion that was missing and would have caught the bug.
  Caveat: `FileSystemWatcher` semantics differ on Linux (inotify) — gate or run this on Windows, which is
  the only supported target (`net8.0-windows7.0`).
- Assert no temp file is ever created inside the target directory (guards defect 2 above).
- Keep the existing `PathUtilityTests` coverage: content correctness, no stranded temp, overwrite.
- Manual: run an AutoFocus in NINA and confirm the new chart appears in the dropdown without a restart.

## Follow-ups (do not bundle)

- Upstream PR to NINA: subscribe `AutoFocusToolVM.reportFileWatcher.Renamed` (and treat it as a create),
  so core is robust against any writer that publishes reports by atomic rename. Also consider filtering
  `InitializeChartList`'s `Directory.GetFiles` to `*.json`.
- `InspectorVM`'s two report paths omit the profile GUID, so `IsAutofocusForCurrentProfile` falls through
  to `true` and those reports are listed under *every* profile. Separate bug.
