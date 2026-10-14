# Clown Squad — Design Document

**Status:** Draft, captured 2026-10-13. Numbers are starting proposals, not balance.

## 1. Vision

A co-op beat-em-up brawler with roguelite runs and ARPG loot depth, told through a
branching story with multiple endings.

| Reference | What we take from it |
| --- | --- |
| **Rampage Knights** | Co-op brawler combat, run-based structure, item synergy and build chaos |
| **Guardian Heroes** | Branching narrative, multiple endings, RPG progression, class variety, scale of on-screen combat |

Visual direction is settled: 16-bit sprites in lit 3D environments, no depth of
field. See [ADR 0002](../adr/0002-hd2d-rendering-architecture.md).

Tone: clown-themed, but not exclusively — clown weapons and enemies sit alongside
conventional fantasy ones.

## 2. Open decisions — these block architecture

### 2.1 Co-op: RESOLVED — co-op is in

Co-op is confirmed. Architecture is recorded in
[ADR 0003](../adr/0003-coop-architecture.md): host-authoritative simulation with
an input-source abstraction, so **local co-op works first and online layers on
additively** rather than requiring a rewrite.

The only remaining question is scheduling, not architecture: does online ship at
launch, or after? That affects when Steamworks P2P work lands, nothing else.

### 2.2 Run structure: RESOLVED, persistent levels

Characters persist between sessions (level, gear, skills, captures) and the game
is a sequence of authored stages. See [ADR 0006](../adr/0006-persistent-progression.md).

### 2.3 Deferred by agreement

- Steam Deck as a launch target (ADR 0002)
- Locking sprite resolution and texel-to-world ratio (ADR 0002)
- Story branching and ending structure — acknowledged, parked

## 3. Enemy rarity

Five tiers. Rarity drives spawn weight, stat multipliers, drop quality, capture
difficulty, and visual treatment.

| Tier | Spawn weight | Stat multiplier | Visual treatment | Notes |
| --- | --- | --- | --- | --- |
| Common | 1000 | 1.0x | Base palette | The bulk of encounters |
| Rare | 150 | 1.6x | Palette shift + subtle glow | Small ability additions |
| Epic | 30 | 2.5x | Distinct palette + particle aura | Mechanically distinct, not just bigger |
| Legendary (boss) | — | 6.0x+ | Unique sprite | Scripted encounter, not spawn-rolled |
| Legendary (wild) | 1 | 5.0x | Unique palette + strong aura | A normal mob with a ~0.1% spawn roll. Dangerous, hugely rewarding. |

Wild legendaries are the headline idea here: any common enemy can, very rarely,
spawn as a legendary variant. They should be genuinely threatening and drop
accordingly.

## 4. Monster capture

A small chance, on kill, to capture the monster and absorb its power or abilities.

**Proposed rules (to tune):**

- Base capture chance scales inversely with rarity: Common 5%, Rare 2%, Epic 0.5%,
  Legendary 0.1%
- Capture chance improved by gear affixes and possibly a dedicated class
- A captured monster grants either a **passive stat bonus** or an **active ability**
  lifted from its own kit
- **Capture slots are limited and earned by level.** Level cap is 50. Players
  start with 1 slot at level 1 and gain 1 more every 10 levels:

  | Level | 1 | 10 | 20 | 30 | 40 | 50 |
  | --- | --- | --- | --- | --- | --- | --- |
  | Slots | 1 | 2 | 3 | 4 | 5 | 6 |

  Six slots at cap keeps captures a question of *choice* rather than
  accumulation. Swapping a capture should be free, so experimenting is cheap and
  the decision stays interesting all game.

**Open:** are captures permanent or per-run? Depends on 2.2.

**Co-op note:** who gets the capture when four players damaged it? Needs a rule.

## 5. Job classes

Seven classes. Healer and Buffer are **cut for now** — pure support is miserable
to play solo and doubles the balance surface. Revisit only if online co-op proves
to be how people actually play.

Each class needs a stat template, a starting kit, equipment affinities, and a
skill tree (section 5.2).

| Class | Role | Hook |
| --- | --- | --- |
| Warrior | Melee bruiser | Balanced, high survivability. The baseline everything else is tuned against. |
| Berserker | Melee burst | Damage scales as health drops; high risk, high reward |
| Assassin | Melee burst | Crits, positioning, backstabs |
| Ranger | Ranged sustained | Kiting, mobility, consistent damage |
| Monk | Melee combo | Speed and chains, low weapon dependency |
| Juggler | Ranged burst | See 5.1 |
| Illusionist | Control | Clones, confusion, crowd control |
| Acrobat | Mobile skirmisher | High-mobility aerial combat, repositioning |
| Ringmaster | Summoner | See 5.1 |

### 5.1 The two classes that need care

**Juggler.** Juggles a held set of items or weapons, then releases the whole set
at once for burst damage. The juggle is a resource: items accumulate while
juggling, and the release spends them. This gives the class a natural rhythm —
build, hold, dump — and a real decision about when to cash out. Juggled items
should be drawn from actual weapons and pickups so the fantasy reads visually.

**Ringmaster.** Commands captured monsters as summons. This is the class most at
risk of overshadowing the others, because it stacks its own power on top of
captured power while everyone else gets only one of those.

Proposed constraints, to be validated in play:

- **Summons replace personal damage, they do not add to it.** Ringmaster base
  weapon damage is substantially below other classes — roughly 50-60%.
- **Summons draw from the same capture slots everyone has.** No private extra
  slots. The Ringmaster converts captures into summons; other classes convert
  them into abilities or passives.
- **Only one summon active at a time** at base; additional concurrent summons are
  a deep skill-tree investment paid for with other power.
- **Summons are time-limited or health-limited**, so the class plays actively
  rather than deploying and idling.
- Summon strength scales off the captured monster, not off the Ringmaster, so the
  ceiling is bounded by what has been captured rather than by levels.

The guiding rule: a Ringmaster should be *different*, not *more*. If a Ringmaster
with a good capture beats a Warrior with a good weapon, the constraints are wrong.

### 5.2 Skill trees

Every class has its own tree of active abilities and passive traits.

**Proposed structure:**

- Points earned per level — 1 per level, 50 total at cap
- Three branches per class, each expressing a different read on the class (for
  example Berserker: sustained rage / burst detonation / survivability-through-aggression)
- **Passive traits** are the majority of nodes: stat shifts, conditional bonuses,
  behaviour modifiers
- **Active abilities** are sparse and expensive — a handful per tree, each a real
  change in how the class plays
- **Capstone nodes** deep in each branch, costly enough that a full build reaches
  at most one or two

**Design constraints:**

- A full 50 points should *not* fill a tree. Builds must exclude things.
- Respec should be cheap or free. Build experimentation is the point; punishing it
  makes people pick safe nodes and play less.
- Trees must interact with loot affixes and captures without multiplying them.
  Three multiplicative power sources is how balance dies.

## 6. Equipment and affixes

Standard ARPG item generation. Every item is rolled, never fixed.

### 6.1 Generation pipeline

```
base item (type, base stats, level requirement)
  -> quality roll   : multiplies base stats by a random factor
  -> rarity roll    : determines affix count
  -> affix rolls    : each affix drawn from a weighted pool, each with its own roll
  = final item
```

**Proposed quality roll:** uniform in [0.85, 1.15], so the same base item varies
±15%. Stats can roll *below* base, as specified.

**Proposed rarity bands:**

| Item rarity | Affix count | Drop weight |
| --- | --- | --- |
| Common | 0 | 1000 |
| Magic | 1-2 | 300 |
| Rare | 3-4 | 60 |
| Epic | 4-5, one may be an ability affix | 12 |
| Legendary | 5-6, guaranteed ability affix | 1 |

### 6.2 Affix types

- **Stat affixes** — flat or percentage: damage, health, speed, crit, resistances
- **Ability affixes** — grant or modify an ability. The interesting ones; these
  create builds
- **Conditional affixes** — "on kill", "below 30% health", "while airborne"

Affixes should be tagged so they can be restricted by item type, class, or level.

### 6.3 Sources

- Monster drops, with drop quality weighted by enemy rarity
- Shop purchase — stock rerolls periodically, giving players a gold sink and a way
  to target a slot that refuses to drop

## 7. Progression and scaling

- **Level cap is 50.** Players gain levels through combat, earning 1 skill point
  per level and a capture slot every 10 levels
- **Enemies scale to the host/primary player's level**, so co-op partners of
  different levels can play together
- **Open:** does a level-5 player joining a level-40 host get flattened? Options:
  scale to the *average*, scale to the *highest*, or scale enemies per-player.
  Each has a different feel and a different implementation cost.

## 8. Weapons

Variety across both registers:

- **Clown-themed:** mallet, pie launcher, balloon-animal blades, juggling knives,
  unicycle (mobility), seltzer bottle, circus cannon, whip
- **Conventional:** swords, axes, bows, staves, daggers, gauntlets, spears

Weapon type should drive the moveset, not just the damage number — a mallet and a
dagger must feel different to swing. This is where brawler combat lives or dies.

## 9. Story and endings — parked

Guardian Heroes-style branching with multiple endings. Acknowledged as a goal,
deliberately deferred. Worth noting early that branching narrative constrains level
and encounter structure, so the branch *shape* should be sketched before content
production begins, even if the writing comes later.

## 10. Suggested build order

This design is large — comparable in scope to a funded studio project. Vertical
slice first, in roughly this order:

1. **Settle the co-op question** (2.1). Everything downstream depends on it.
2. **One character, one weapon, one enemy, movement and hit detection.** Brawler
   combat has to feel good before anything else matters.
3. **Enemy rarity tiers + scaling.** Cheap once combat exists; validates the
   wild-legendary idea early.
4. **Item generation and affixes.** Self-contained and highly testable — a good
   candidate for the TDD workflow.
5. **One class beyond the default**, to prove the class abstraction holds.
6. **Capture system.** Depends on enemy abilities existing.
7. **Shop, then remaining classes, then story structure.**

Each stage should be playable. The biggest risk to a project this size is building
systems that never meet each other.
