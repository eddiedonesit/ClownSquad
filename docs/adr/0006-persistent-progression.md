# 6. Persistent characters, level-based progression

- **Status:** Accepted; save format version 1 implemented 2026-10-14
- **Date:** 2026-10-14

## Context

The GDD left open whether Clown Squad is run-based (die, restart, keep some
meta-progression, like Rampage Knights) or level-based with persistent
characters (like Guardian Heroes). Loot, captured monsters, levelling and skill
trees all depend on the answer, because it decides what survives a session.

## Decision

**Persistent levels.** A character is kept between sessions: level and XP, skill
points and tree choices, equipped and stored gear, and captured monsters all
persist in a save slot. The game is a sequence of discrete stages rather than a
procedurally generated run; the current wave fight is the shape of one stage.

## Consequences

- A save system is now required before loot or levelling is worth building,
  since neither means anything if it vanishes on quit.
- Stages are authored, which suits Guardian Heroes style branching and multiple
  endings: the branch structure becomes a map of stages.
- Enemy scaling to the host level (GDD section 7) now matters for real, because
  co-op partners will arrive with characters of different levels.

## Failing a stage (decided 2026-10-14)

A failed stage keeps the XP earned in it and loses the loot. Levels always
move forward, so a loss is never wasted time; gear only arrives on a clear,
so every stage still has something at stake. Loot picked up mid-stage is
held provisionally and banked to the character on the stage-clear screen.

## Save format (version 1)

One JSON file, `user://clownsquad_save.json`, holding `Version` and a list of
`Characters`, one per player slot by index. A character is `Name`, `Level`,
`Xp` (toward the next level), `TotalXp`, `StatPoints`, `SkillPoints`, and the
four stats `Pow`, `Guts`, `Zip`, `Luck`. Writes go to a temporary file that
then replaces the save, so a crash cannot leave half a file; a save that fails
to parse, or comes from a newer build, is renamed aside and a fresh one started.
Later versions migrate older files on load rather than refusing them.

Gear, skills and captures are not in version 1; they arrive with their systems.

## Open


- Slots per player, and whether a co-op guest brings their own character.
- Whether gear rolls happen on the host only (ADR 0003 says yes for fairness).
