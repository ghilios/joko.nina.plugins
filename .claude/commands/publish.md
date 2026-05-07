---
description: Tag the current HEAD as release/vA.B.C.D using the version already in AssemblyInfo.cs and push the tag to trigger the GitHub Actions release workflow. Does not bump the version or create a commit.
allowed-tools: Bash(git tag *), Bash(git push *), Bash(git describe *), Read
---

Current AssemblyInfo.cs:
```!
cat "Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs"
```

Publish the current version of Hocus Focus without bumping the version number.

**1. Read current version**

From the AssemblyInfo.cs content above, extract the version string `A.B.C.D` from `[assembly: AssemblyVersion("A.B.C.D")]`.

**2. Check for an existing tag**

Run `git describe --tags --exact-match HEAD 2>/dev/null` to see if HEAD is already tagged. If it already has a `release/v` tag, tell the user and stop.

**3. Show the plan and confirm**

Display:
- Version: `A.B.C.D`
- Tag to create: `release/vA.B.C.D`
- This will trigger `.github/workflows/build-and-release.yml`

Ask: **"Proceed with tag and push? (yes/no)"**

**4. After confirmation, execute in order**

```bash
git tag release/vA.B.C.D
git push origin release/vA.B.C.D
```
