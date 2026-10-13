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

### 2.1 Co-op: how many players, and over what? UNRESOLVED — blocks everything

The brief says monsters scale to "the primary player or **host's** level," which
implies networked multiplayer. This must be settled before systems are built.

| Option | Cost | Consequence |
| --- | --- | --- |
| **Single-player only** | Lowest | Fastest to ship. Diverges from both references. Adding co-op later means rewriting every system that rolls a random result. |
| **Local co-op (shared screen)** | Low-medium | No networking. Multiple inputs, shared camera. Rampage Knights supports this. Camera constrains level design. |
| **Online co-op (P2P host)** | High | Matches the brief's "host" language. Requires authority rules for every roll, plus Steamworks P2P. Roughly doubles the cost of each system below. |

**Why this cannot wait:** every system in this document produces a random result —
loot rolls, affix rolls, capture chance, rare spawn chance. In multiplayer each
needs a defined authority and replication rule. Deciding later means revisiting
all of them.

### 2.2 Run structure

Is this run-based like Rampage Knights (die, restart, keep meta-progression), or
level-based with persistent characters like Guardian Heroes? It changes whether
loot and captures persist between sessions. Unresolved.

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
- Players have limited capture slots, forcing choice — otherwise this becomes
  strictly additive power creep

**Open:** are captures permanent or per-run? Depends on 2.2.

**Co-op note:** who gets the capture when four players damaged it? Needs a rule.

## 5. Job classes

Each class needs a stat template, a starting kit, equipment affinities, and a
distinct role in co-op.

| Class | Role | Hook |
| --- | --- | --- |
| Warrior | Melee bruiser | Balanced, high survivability |
| Berserker | Melee burst | Damage scales as health drops; high risk |
| Assassin | Melee burst | Crits, positioning, backstabs |
| Ranger | Ranged sustained | Kiting, mobility |
| Monk | Melee combo | Speed, chains, low weapon dependency |
| Healer | Support | Sustain; needs offence to be fun solo |
| Buffer | Support | Team amplification; hardest to make fun solo |

**Additions worth considering:**

| Class | Hook |
| --- | --- |
| Ringmaster | Commands captured monsters as summons — ties the capture system to a class |
| Juggler | Throws weapons, ricochet and multi-target |
| Illusionist | Clones, confusion, crowd control |
| Beast Tamer | Capture specialist; higher capture rates |
| Acrobat | High-mobility aerial combat |

**Design risk:** pure support classes (Healer, Buffer) are miserable to play solo.
Either guarantee co-op, or give them self-sufficient offensive kits.

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

- Players gain levels through combat
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
