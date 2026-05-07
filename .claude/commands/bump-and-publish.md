---
description: Bump the revision (4th) version component in AssemblyInfo.cs, create a descriptive release commit, tag as release/vA.B.C.D, and push to trigger the GitHub Actions release workflow.
allowed-tools: Bash(git add *), Bash(git commit *), Bash(git tag *), Bash(git push *), Bash(git log *), Bash(git describe *), Bash(git rev-list *), Edit, Read
---

Current AssemblyInfo.cs:
```!
cat "Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs"
```

Commits since last release tag (or last 20 if no tag exists):
```!
git log --oneline "$(git describe --tags --abbrev=0 --match 'release/v*' 2>/dev/null || git rev-list --max-parents=0 HEAD)..HEAD" 2>/dev/null || git log --oneline -20
```

Publish a new Hocus Focus release. Work through these steps without pausing except at the confirmation in step 4.

**1. Extract current version**

From the AssemblyInfo.cs content above, read the version string from `[assembly: AssemblyVersion("A.B.C.D")]`.

**2. Compute new version**

Increment the 4th component by 1. New version = `A.B.C.(D+1)`.

**3. Update AssemblyInfo.cs**

Edit `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs` — update **both** `AssemblyVersion` and `AssemblyFileVersion` to the new version string.

**4. Compose commit message**

From the git log above, summarize the notable changes as a short bullet list (omit merge commits and anything obviously trivial). Format:

```
Release vA.B.C.(D+1)

- <summary of notable changes>
```

**5. Show the plan and ask the user to confirm**

Display:
- Version bump: `A.B.C.D` → `A.B.C.(D+1)`
- Full commit message (exactly as it will be passed to git)
- Tag name: `release/vA.B.C.(D+1)`

Then ask: **"Proceed with commit and push? (yes/no)"**

**6. After the user confirms, execute in order**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs
git commit -m "Release vA.B.C.(D+1)" -m "- <bullet 1>" -m "- <bullet 2>" ...
git tag release/vA.B.C.(D+1)
git push origin HEAD
git push origin release/vA.B.C.(D+1)
```

Use separate `-m` flags for each paragraph of the commit body so git formats it correctly. The tag push triggers `.github/workflows/build-and-release.yml`, which builds, packages, and publishes the release to GitHub.
