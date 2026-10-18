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
- `decisions/<id>`: `{order, area, title, question, why, blocks, options:
  [{id, label, detail}], suggest, answer, note}`; leave `answer` and `note`
  empty for the designer to fill in on the page. `area` is the topic the
  page groups it under (the same topics as the features). A follow-up to an
  earlier answer says so at the start of its question ("Follows your answer
  ..."). The designer wants depth: every answered topic gets its follow-up
  choices before it is built.
- `todos/<tN>`: `{order, title, detail}`: things only the designer can do.
- `meta/info`: `{updated, build}`.

One feature per item, never several ideas bundled into one entry (the
designer asked for this): each system is its own `area` (for example
"War: the battle map", "The court: characters and families"), and every
part of it is its own feature line with its own status. A feature that
waits on a decision is `blocked` and names the decision in its detail.

Always pass `if_version` (from your last read) when updating an existing
document, and batch several writes into one call.

How to word Ledger questions (the designer's rule, 15 Oct 2026; they take a
while to answer when oddly worded):

- `question` is one plain, complete question ending in a question mark,
  after at most one or two sentences of history for context. Never just a
  list of examples, never internal codes ("Next 26", "Playable 14").
- A follow-up quotes the earlier answer in words: "You answered 'Yes,
  loyalty grows with service' to 'Armies loyal to their general'."
- Every option has a `detail` that says what would happen in the game if
  chosen, in a sentence a player would understand.
- `why` says in one sentence what the choice changes for the player.
- Plain words; explain any term a non-historian might not know.

Keep a good stock of open questions on the Ledger (the designer asked for a
few hundred, 15 Oct 2026) so work never waits: follow-ups that every
answered topic needs before it is built, written in the style above.

## Pages and artifacts: sans serif only (the designer's rule, 17 Oct 2026)

The designer has chronic eye pain. Every artifact page (the Ledger and any
other) uses a sans serif typeface, never a serif one: Lexend (Google
Fonts) with system-ui, "Segoe UI" and Verdana behind it. Strokes slim, not
heavy (the designer found Atkinson Hyperlegible's too thick): weight 300
for text, 400 for headings, buttons and labels, never bold. Layout matters
more than the typeface (the designer's note, 17 Oct 2026): line spacing
about 2.1, wide letter spacing (0.04em) and word spacing (0.22em), and a
narrow column of about 58 characters so the eyes needn't swing across the
screen. Set sizes in rem, and give each page sliders for text size, line
spacing and column width (as the Ledger has) that the page remembers.
Colours (checked 17 Oct 2026): no pure black or white; main text at about
11:1 contrast with its background (enough to read, without glare), every
other text at least 5:1; a dark ground lifted off black (like #1e1914) and
a light ground dimmed off white (like #e4dccb); a switch for "as my
system", dark and light. Reuse the Ledger's colour values.

## Naming work (the designer asked for this)

When talking about the work, use the Ledger's titles (its areas, features
and decisions, e.g. "War: mercenaries"), never "section 11", "part 4" or
"Next 8". Numbers may be added alongside a title, but never replace it.

## Branches (the designer's choice)

Work on one branch, `claude/facsimilia-game-dev-bq0sed`, all the time. The
designer merges into `master` only when they want a milestone snapshot (or
never). After a merge, do not restart or reset the branch and do not ask the
designer to do anything: just keep committing and pushing to the same branch.

## Building and testing

- Build: `dotnet build -warnaserror` (the session hook installs .NET and Godot).
  GitHub counts warnings as errors, so a warning breaks its Tests run;
  `tools/run_tests.sh` builds that strict way first and stops if it fails.
  Never pipe the test script into `tail` before `&&`: check its exit code.
- Tests (the designer's rule, 13 Oct 2026: this is not a live game, so the
  long tests need not run every time): `tools/run_tests.sh` runs the quick
  ones (all but the slow ones listed in it), `tools/run_tests.sh full`
  runs every test, `tools/run_tests.sh VoiceTest OpeningTest` runs just
  those. Every commit: the quick tests plus any test for what changed must
  pass before pushing. Every third commit (and before a milestone merge
  into `master`): the full run. End each commit message with a line
  "Tests: quick" or "Tests: full", so the last full run is easy to find.
  GitHub runs every test on each push anyway (the Tests workflow).
- SpeedTest (part of the quick run) plays ten years and fails if a year
  averages over 3 seconds (the designer's limit), any year passes 6, or a
  save passes 1.5. It prints the times: mention them when they grow.
- Check visible changes in the running game with screenshots before
  saying they work.
- After changing MECHANICS.md, rebuild the PDF: `python3 tools/build_docs_pdf.py`.

## Generating sound, music and pictures (the designer's rule)

With ElevenLabs or any other paid generator, make TWO takes of each
thing (generations_count 2) unless the designer asks otherwise.
Say what it will cost before anything over a few cents.
ElevenLabs' voice design always makes three previews and can't be set to
two, so ask before designing a voice (or pick from the existing voice
library, which costs nothing).
