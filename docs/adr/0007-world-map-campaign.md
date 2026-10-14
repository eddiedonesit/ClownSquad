# 7. World map campaign

- **Status:** Accepted; first version built
- **Date:** 2026-10-14
- **Extends:** ADR 0006 (persistent characters, stage-based structure)

## Context

ADR 0006 made the game a sequence of authored stages with persistent
characters, but the only way to play was one fixed fight that replayed. The
direction now is a Super Mario World / Castle Crashers style overworld: ten
worlds, stages named 1-1, 1-2 and so on, unlocking as they are cleared, with
bonus stages and shops along the way and difficulty rising steadily to world
10. A story is planned for later and must have somewhere to attach.

## Decision

**A node-and-path world map, one island per world.** Each world is a road of
five stages and a boss, a shop just off the road after stage 2, a bonus
stage on a side branch after stage 3, and a gate to the next world. A node
unlocks when any node it requires is cleared; the clown walks between open
nodes and enters what it stands on. The map is its own scene
(scenes/WorldMap.tscn); a fight scene runs whichever stage the map hands it
and returns to the map, won or lost.

**The campaign is generated from rules, not typed out** (scripts/Map/Campaign.cs).
Per stage: a level floor of `1 + 5 x (world - 1) + (stage - 1)`, more and bigger
waves later in each world and from worlds 4 and 8, a Rare/Epic/Legendary spawn
boost growing to 2.5x by world 10, and grunt attack cooldowns falling from
1.3 s to 0.65 s. Enemies take the host's level when it is above the floor, so
over-levelling never trivialises a stage. Retuning the whole campaign is a
change to that one file.

**Co-op scaling: enemies scale to the host** (player one), resolving the GDD 7
question. Partners keep their own levels and gear.

**Gold and shops.** Enemies pay gold into the stage bag; like loot it is
banked on a clear and lost on a fail. Each world's shop sells six rolled items
at the world's or the squad's level, restocked whenever a stage in that world
is cleared (the stock is seeded from the clear count, so nothing is stored).
A **secret shop** appears between waves in about 7% of stages, selling items
three levels higher with rarity pushed hard. Purchases go straight to the
stash, paid with banked gold, so they cannot be lost.

**Bonus stages** are a 40-second pinata party: harmless enemies stream in,
pay triple gold and drop loot often. They cannot be lost.

**Story hooks.** Node ids ("1-1", "1-B", "1-*", "1-S", "1-G") are stable and
are the attachment points for story: a scene can play on entering, clearing
or first unlocking a node. Nothing else needs to change to add it.

## Save format (version 3)

`SaveData.Campaign` holds `Cleared` (node ids), `At` (where the squad stands)
and `ShopRestocks` (clears per world). `CharacterRecord.Gold` is banked gold.
Version 1 and 2 files load unchanged, starting at 1-1 with no gold.

## Consequences

- The fight scene no longer replays itself; with no stage chosen and no test
  flag it opens the map. `-- stage 2-3` runs one stage directly; `-- fight`
  keeps the old free fight.
- Every stage currently shares the one circus arena and the one boss.
  Per-world arenas, more bosses and new enemy types are the obvious next art
  and content work, and the stage definition is where they plug in.
- The squad's controllers are chosen on the map (pads join with A) and carried
  into each stage.

## Revisions (2026-10-14)

- **The map is 3D**: every world is an island in one sea, built from the same
  map data (heightmap, ground texture and scenery spots from tools/gen_map.py)
  with a 3D landmark per world. The 2D map script is kept but unused.
- **Fights are walked through**, left to right, instead of fought in one ring.
  A stage's waves become groups waiting along the road: reaching one locks the
  camera until it is beaten; there is no wave count on screen. Bonus stages
  keep the ring. Stage scenery comes from tools/gen_stage.py, one set per world.
