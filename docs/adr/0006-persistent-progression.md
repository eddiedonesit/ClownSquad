# 6. Persistent characters, level-based progression

- **Status:** Accepted (direction); save format not yet designed
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

## Open, needed before the save system

- Slots per player, and whether a co-op guest brings their own character.
- Whether gear rolls happen on the host only (ADR 0003 says yes for fairness).
