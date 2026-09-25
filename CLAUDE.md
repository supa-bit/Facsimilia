# Working on Facsimilia

## Who this is for

The designer is learning to code; this is their first big project. Explain
things in plain language, step by step, without jargon (no "set an env var"
without saying exactly where and how). They use Windows, GitHub Desktop, the
Godot 4.7.2 .NET editor and VS Code.

The game is made for the designer's own taste: long, deep playthroughs
(weeks or months), with real history as the guiding factor throughout.
Design decisions are recorded in MECHANICS.md ("Design decisions").

## The Ledger: keep it current (required)

The designer tracks the project in the **Facsimilia Ledger**, an artifact
page: https://claude.ai/artifact/JV9NfG4Xg8Eo1syEqjY8w4

Update it with the ArtifactData tool (url above):

- **Every commit**: update `meta/info` (`build` = the new short commit hash,
  `updated` = today's date like "25 Sep 2026") and the status/detail of any
  feature the commit changes.
- **Every new or changed idea**: add or edit the matching `features` doc,
  and add a `decisions` doc for any new question only the designer can
  answer. Keep ROADMAP.md and MECHANICS.md in step with the Ledger.
- **At the start of a session**: read the `decisions` collection. Answered
  decisions (`answer` set, plus `note`) are the designer's instructions;
  a note overrides the chosen option when they conflict. Record new answers
  in MECHANICS.md and ROADMAP.md, then act on them.

Collections:

- `features/<fNN>`: `{order, area, status, title, detail}`; status is one of
  `done`, `doing`, `next`, `blocked` (waits on a decision), `later`. New
  features take the next free `fNN` id and order.
- `decisions/<id>`: `{order, title, question, why, blocks, options: [{id,
  label, detail}], suggest, answer, note}`; leave `answer` and `note` empty
  for the designer to fill in on the page.
- `todos/<tN>`: `{order, title, detail}`: things only the designer can do.
- `meta/info`: `{updated, build}`.

Always pass `if_version` (from your last read) when updating an existing
document, and batch several writes into one call.

## Branches (the designer's choice)

Work on one branch, `claude/facsimilia-game-dev-bq0sed`, all the time. The
designer merges into `master` only when they want a milestone snapshot (or
never). After a merge, do not restart or reset the branch and do not ask the
designer to do anything: just keep committing and pushing to the same branch.

## Building and testing

- Build: `dotnet build` (the session hook installs .NET and Godot).
- Tests: `godot --headless --path . --script res://src/Tests/<Name>Test.cs`
  for each file in `src/Tests/`; all must pass before pushing. GitHub runs
  them too (the Tests workflow).
- Check visible changes in the running game with screenshots before
  saying they work.
- After changing MECHANICS.md, rebuild the PDF: `python3 tools/build_docs_pdf.py`.
