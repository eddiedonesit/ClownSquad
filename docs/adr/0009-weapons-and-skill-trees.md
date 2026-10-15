# 9. Weapons, and a skill tree per weapon

- **Status:** Accepted; built (eight weapons)
- **Date:** 2026-10-15
- **Extends:** ADR 0005 (combat model), ADR 0006 (persistent progression)

## Context

Every clown fought with the same mallet, and skill points banked at each level
had nothing to buy. The brief: "skill trees should be dependent on the weapon
used, and weapons can only be changed after the stage has been completed."

## Decision

**A weapon is data.** `WeaponDef` (scripts/Progression/Weapons.cs) holds:
- a name and a blurb;
- a combo as `AttackStep`s (timings, damage, knockback, range, launch);
- one extra step used by skills that lengthen the combo;
- the charge move (what a full held charge does);
- a hit sound and pitch;
- how hard it strikes the ground;
- stun and finisher-stagger multipliers;
- whether the combo fires corks instead of swinging;
- a three-by-three skill tree.

`PlayerFighter` takes the weapon from the character record once, before its
sprite is built, and reads everything else from the definition.

**Charge moves:**

| Charge | Weapons | What it does |
|---|---|---|
| Throw | mallet, chicken | Thrown, and it comes back (`MalletProjectile`) |
| Quake | hammer | Ground pound all round |
| Clap | slapstick | Stuns everything in front |
| Lob | pie | Lobbed pie that splats and blinds a group |
| Fan | pins | Fan of pins down the lane |
| BigShot | pop gun | Piercing big cork |
| Lunge | balloon sword | Fencer's dash forward |

Pies, pins and corks are `PlayerShot`s: they do not come back, so the
clown stays armed.

**Skill trees:**
- **Shape:** three columns of three, learned top to bottom.
- **Cost:** a node costs its tier (1, 2, 3), so a full tree is 18 points.
- **Points vs. skills:** skill points are shared by the character; learned skills belong to the weapon (`CharacterRecord.Skills`, keyed by weapon). Switching weapon keeps each tree's progress, and points spent in one tree stay there.
- **Effects:** learned skills reduce to one `WeaponPerks` value (multipliers and flags) applied to the combo when the record is applied, and read by the moves and by `Leveling` (health, crit, loot).

**When things can change:**

| Action | In a stage | On the map |
|---|---|---|
| Learn a skill | Yes | Yes |
| Unlearn (refund) | No | Yes |
| Change weapon | No | Yes |

The map's pause menu has SKILLS & WEAPONS; the fight's has SKILLS. That is
the "only after the stage is completed" rule, and it means a fight never
has to swap a sprite sheet or a combo mid-stage.

**Art:**
- `tools/gen_sprite_hd.py` draws each weapon in place of the mallet in every pose, giving one sheet per clown per weapon: `pip_hammer_hd.png`, `poppy_pie_hd.png`...
- The pop gun and the pie have their own attack poses: aimed, and shoved forward.

## Alternatives considered

| Option | Why rejected |
|---|---|
| One skill tree per character, weapon-independent | The brief asked for weapon-dependent trees; and per-weapon trees make each weapon a long-term project |
| Weapons as loot items with rolled stats | Overlaps the gear system (the Mallet slot already rolls affixes); weapons are a playstyle, gear is numbers |
| Changing weapon anywhere | Mid-fight swaps undermine the choice and need sheet and combo swaps on a live fighter |
| A separate point pool per weapon | Punishes trying a new weapon late; shared points with per-weapon spending is the usual compromise |

## Consequences

**Positive:**
- **Cheap to extend:** a new weapon is one definition, one drawing function and an optional new charge kind.
- **Testable:** `weapontest` drives every weapon's combo and charge against dummies, with and without full trees.

**Negative, accepted:**
- **Sprite sheets multiply:** clowns × weapons, 32 sheets plus normal maps. A ninth weapon adds four more.
- **Balance is probe-tuned only so far.** It needs playtesting; numbers live in Weapons.cs.
- **No respec of points across trees:** points spent stay in their tree, though they can be unlearned on the map.
