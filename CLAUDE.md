# HocusFocus Plugin — Claude Code Instructions

## Project Overview

NINA astrophotography plugin (C# WPF) providing advanced auto-focus, star detection, aberration inspection, and related tools. Uses MEF composition for dependency injection, CommunityToolkit.Mvvm for MVVM, and PluginOptionsAccessor for persistent settings.

**Solution**: `Joko.NINA.Plugins/Joko.NINA.Plugins.sln`
**Primary project**: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/`
**Tests**: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/`
**Assembly/RootNamespace**: `NINA.Joko.Plugins.HocusFocus`
**Target**: `.NET 8.0-windows7.0`, x64, Class Library + WPF + Windows Forms

## Reference Docs — read the relevant one before working in that area

Detailed conventions live in `.claude/docs/`. Read the matching file when a task touches that area; don't work from memory.

| Working on... | Read |
|---|---|
| Project layout, namespaces, file locations, dependencies, build output | `.claude/docs/architecture.md` |
| MEF exports (manifest, DockableVM, sequence item, pluggable behavior), `HocusFocusPlugin.cs` bootstrap, dual-constructor | `.claude/docs/mef-and-bootstrap.md` |
| ViewModels, commands, observable properties, mediators, async, error handling, logging | `.claude/docs/mvvm-patterns.md` |
| Adding/changing a persisted option (`*Options` classes) | `.claude/docs/options-system.md` |
| Adding/editing a NINA `SequenceItem` | `.claude/docs/sequence-items.md` |
| XAML/WPF, value converters, validation rules | `.claude/docs/wpf-xaml.md` |
| Star detector internals: contamination test, local background plane, `StarDetectorMetrics` | `.claude/docs/star-detection-internals.md` |
| Running TestApp diagnostics/optimizer (`contamination`/`optimize`/`review`/`diagnose-labels`) | `.claude/docs/testapp-cli.md` |
| Sensor tilt, tilt adapters, aberration inspector, screw orientation | `.claude/docs/tilt-domain.md` |
| Editing the user-facing MkDocs manual (`documentation/docs/`) | `.claude/docs/documentation-style.md` |
| Capturing real NINA/HocusFocus screenshots for the manual via the Windows MCP (capture pipeline, annotate helper, NINA navigation map) | `.claude/docs/nina-mcp-screenshots.md` |

## Project Invariants

These rules must never be skipped. Where a detail doc applies, read it before acting.

- **Run the test suite after every code change** before reporting work complete (see Testing below). Never skip, ignore, or mark tests expected-to-fail to make the suite pass — fix the cause.
- **Adding a persisted option** → it **must** also get a UI control in `Resources/OptionsDataTemplates.xaml`. Detail: `.claude/docs/options-system.md`.
- **Adding a `StarDetectorMetrics` field** → it **must** also appear in the metrics panel in `AutoFocus/DataTemplates.xaml`. Detail: `.claude/docs/star-detection-internals.md`.
- **Git**: never push to `develop` directly; specs/plans go in their folders; commit with the privacy email (all below).

## Specs & Plans Workflow

This project separates **design specs** from **implementation plans**, and they live in different folders:

- **Design specs go in the `docs/` folder.** A spec is the what/why/approach produced by brainstorming or design exploration (and its related analyses/results). Use a meaningful filename with a `-design.md` suffix (e.g., `docs/sigma-consistency-design.md`).
- **Implementation plans go in the `plans/` folder.** A plan is the step-by-step execution of an approved spec. Use a meaningful filename with a `-plan.md` suffix (e.g., `plans/sigma-consistency-plan.md`).
- **Never leave a spec or plan only in chat, in another directory, or in a scratch file** — write it to the correct folder, regardless of size or how it was produced (planning mode, brainstorming, ad-hoc requests, etc.).
- **Clear your Claude Code context (`/clear`) before executing a plan** to avoid stale planning context affecting implementation.
- The user will explicitly say when a plan is ready to execute.

## Git Workflow

- **Never push to `develop` directly.** Create a feature branch (`ghilios/<topic>`), push it, and open a PR — `develop` is only updated via PR merges.
- **Author + committer email** must be `322725+ghilios@users.noreply.github.com`. GitHub email-privacy blocks pushes from `ghilios@gmail.com`. When committing, set both:
  ```
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
  ```
  If a commit slips through with `ghilios@gmail.com`, amend it with `--author=` and the env vars above before pushing.

## Testing

**Run the unit test suite after every code change** before reporting work complete. From the solution root:

```
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```

If any test fails, fix the underlying cause — do not skip, ignore, or mark tests as expected-to-fail to make the suite pass.

**Framework**: NUnit 4.4.0 + NUnit3TestAdapter

```csharp
namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class MathUtilityTests {
    [Test]
    public void MedianMAD_ScalesOddLengthMedianAbsoluteDeviation() {
        var (median, mad) = new[] { 1.0, 2.0, 3.0 }.MedianMAD();
        Assert.Multiple(() => {
            Assert.That(median, Is.EqualTo(2.0));
            Assert.That(mad, Is.EqualTo(1.483).Within(1e-12));
        });
    }
}
```

The test project links shared source files directly (e.g., `MathUtility.cs`) rather than referencing the plugin assembly.
