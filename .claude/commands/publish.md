---
description: Tag the current HEAD as release/vA.B.C.D using the version already in AssemblyInfo.cs and push the tag to trigger the GitHub Actions release workflow. Does not bump the version or create a commit.
allowed-tools: Bash(git tag *), Bash(git push *), Bash(git describe *), Bash(git log *), Bash(git rev-list *), Read
---

Current AssemblyInfo.cs:
```!
cat "Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs"
```

Commits since last release tag (or last 20 if no tag exists):
```!
git log --oneline "$(git describe --tags --abbrev=0 --match 'release/v*' 2>/dev/null || git rev-list --max-parents=0 HEAD)..HEAD" 2>/dev/null || git log --oneline -20
```

Publish the current version of Hocus Focus without bumping the version number.

**1. Read current version**

From the AssemblyInfo.cs content above, extract the version string `A.B.C.D` from `[assembly: AssemblyVersion("A.B.C.D")]`.

**2. Check for an existing tag**

Run `git describe --tags --exact-match HEAD 2>/dev/null` to see if HEAD is already tagged. If it already has a `release/v` tag, tell the user and stop.

**3. Compose release description**

From the git log above, summarize the notable changes as a short bullet list (omit merge commits and anything obviously trivial). Format:

```
Release vA.B.C.D

- <summary of notable changes>
```

**4. Show the plan and confirm**

Display:
- Version: `A.B.C.D`
- Tag to create: `release/vA.B.C.D`
- Release description (exactly as it will appear in the tag message)
- This will trigger `.github/workflows/build-and-release.yml`

Ask: **"Proceed with tag and push? (yes/no)"**

**5. After confirmation, execute in order**

```bash
git tag -a release/vA.B.C.D -m "Release vA.B.C.D" -m "- <bullet 1>" -m "- <bullet 2>" ...
git push origin release/vA.B.C.D
```

Use separate `-m` flags for the title and each bullet so git formats it correctly.
