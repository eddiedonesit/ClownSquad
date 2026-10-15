# 8. The enemy cast and a boss for every world

- **Status:** Accepted; first version built
- **Date:** 2026-10-14
- **Extends:** ADR 0007 (campaign), ADR 0003 (input sources), ADR 0005 (hit reactions)

## Context

Every stage in all ten worlds used one enemy, the grunt, and every world ended
with the same boss, Bellyflop Bertie. A brawler lives on enemy variety: each
new enemy is a new problem, each boss a set piece.

## Decision

**One shared base, moves as data.** Every new character is a `CastFighter`
(scripts/Actors/Cast/CastFighter.cs), which is the existing `PlayerFighter`
with a sheet from assets/sprites/cast and a list of moves. A move is a name, a
range band, a weight, a cooldown, a start action and a tick that says when it
is done. The AI (the same `BrawlerAi`, which gained a keep-away distance for
throwers) asks for a special on a cooldown; the fighter picks a move that fits
the distance and is off cooldown. Bosses (`BossFighter`) add a HUD title,
poise during moves, no launching, phases at two thirds and one third health,
and a comic title card on arrival. A boss can `Order` a partner's move (the
trainer's tiger) through the `CastInput` wrapper.

**Shared combat pieces**, used by every new move:
`Telegraph` (a circle, lane or cone on the floor, the exact size of the hit,
pulsing faster as it lands), `EnemyShot` (anything lobbed or flung, with a
landing marker, bursts and leftovers like fire), `Hazard` (burning floor,
rings of fire, growing rings), `Fighter.Hold` (being grabbed or reeled in),
`Fighter.Stagger` and `StunClip` (topples and dizzy spells).

**The cast.** Six enemies, one introduced per world, each with one idea:
juggler (keeps away, lobs pins), unicyclist (lane charge, then dizzy),
balloon artist (twists balloon dogs), mime (invisible rope pull, invisible
wall), stilt-walker (hard to hurt until four quick hits topple him),
weightlifter (blocks from the front, grabs and slams). Nine bosses join
Bertie, one per world: Bobo the Balancer, Madame Stripes and Rajah, the
Jalopy, Stretch, the Flying Fratelli, Pyro Pete, the Totem Tots, Iron Ivan
and the Ringmaster. Who appears where is one table, `EnemyCatalog`.

**Art.** tools/gen_cast_hd.py draws them all in the HD pipeline at the squad's
pixel density: one parametric humanoid rig (build, outfit pattern, face, hair,
hat, legs or stilts or unicycle, a held prop, poses by joint angle), a
quadruped rig (tiger, pony), and hand-drawn balloon dog, clown car and
carriage. Props that move in the world are 3D (the beach ball, trapezes, the
elastic arm, thrown pins and torches), in keeping with the HD-2D scenery.

## Consequences

- A new enemy is a sheet entry in gen_cast_hd.py and a class of a few dozen
  lines listing its moves.
- The automated win probe uses a bot that never dodges. It beats worlds 1 to 5
  and their bosses but not most late stages, which are built around
  telegraphs a player reads and dodges. Balance for those worlds needs play
  testing, or a bot that reads telegraphs (`Telegraph` instances are in the
  scene tree, so one could).
- "Totem Tots" is the name used for the stacking act of small clowns.
