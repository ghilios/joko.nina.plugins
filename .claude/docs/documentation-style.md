# User-Facing Documentation Style

Read this when editing the user-facing manual (the MkDocs Material site under `documentation/docs/`, config `mkdocs.yml`, published to GitHub Pages). Write it the way a professional technical writer would, not the way an AI writes by default.

- **Verify every claim against the code.** Setting names, defaults, ranges, enum values, and described behavior must match the source. When unsure, read `BuildStarDetectorParams`, the relevant `*Options.cs`, and the XAML tooltips. Never document reverted or unshipped features.
- **Avoid AI-writer tells.** The biggest one: do not lean on em-dashes. Use commas, parentheses, colons, and periods, and split long sentences, instead. An occasional em-dash for genuine emphasis is fine; several per paragraph is not. Also avoid filler ("it's worth noting", "in essence", "simply", "seamlessly"), buzzwords ("robust", "leverage", "delve", "powerful"), reflexive bolding of every other phrase, and the repeated "not just X, but Y" or rule-of-three cadence.
- **Plain, direct prose.** Lead with what the reader does or what a setting controls. Prefer short declarative sentences and the active voice. Cut hedging and throat-clearing.
- **Use admonitions sparingly.** A `!!! tip` or `!!! note` should add information that genuinely breaks the main flow, not restate it. Pages that are wall-to-wall callouts read as generated.
- **Match the house voice.** Read neighboring pages first and follow their structure, heading style, and terminology. Use the exact in-app UI labels.
- **Verify before claiming done.** Build the site with `mkdocs build --strict` (deps in `requirements-docs.txt`) and confirm internal links resolve. For substantive edits, have a reviewer (or a subagent acting as a professional technical writer) check both factual accuracy and that the prose does not read as AI-generated.
