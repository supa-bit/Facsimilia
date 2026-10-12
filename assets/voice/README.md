# Spoken lines

Every line in `data/voice_lines.json`, made with ElevenLabs' newest voice
model (eleven_v4), two takes each:

- `assets/voice/<line>.mp3` is take A, the one the game plays.
- `assets/voice/takes/<line>_b.mp3` is take B. Godot ignores this folder
  (the `.gdignore` file); to use take B instead, copy it over take A's
  file with the same name (without `_b`).

Voices (saved in the ElevenLabs voice library): Bard (opening and before
600), Chronicler (600-1800), Facsimilia Newsreader (from 1800),
Facsimilia Adviser (tutorial), Facsimilia Warrior (war cries).
