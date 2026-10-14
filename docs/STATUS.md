# Status — what is actually built

Updated 2026-10-13. The [GDD](design/GDD.md) describes the intended game; this
records what exists in the repository today.

## Running it

```
# play
C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64.exe --path C:/Claude/ClownSquad

# automated checks (headless; prints clip/state/position assertions)
C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path C:/Claude/ClownSquad -- animtest

# regenerate art - ALWAYS use this, never gen_sprite.py alone
bash tools/refresh_sprites.sh
```

Controls: **WASD** move, **J** attack, **Space** jump, **K/Shift** dodge.
Gamepad: stick, **X** attack, **A** jump, **B/RB** dodge. Keyboard plays
immediately; pads join by pressing a button.

## Built

| Area | State |
| --- | --- |
| Combat | 3-hit combo, hitstop, input buffering, knockback, hit flash and squash |
| Dodge | Ground slide with i-frames 0.05-0.26s of a 0.35s roll |
| Jump | Gravity 26, jump speed 8.5, apex ~1.4 units, airtime ~0.65s |
| Input | Per-device polling, press-to-join local co-op, keyboard + N pads |
| Sprites | Profile only, 16 frames: idle 2, walk 4, attack 4, hit 1, dodge 3, jump 2 |
| Weapon | Cylinder mallet, carried in every pose, swing arc 165/120/45/0 degrees |
| Effects | Ground impact burst and camera shake, scaled by combo step |
| Enemy AI | `BrawlerAi` as an IInputSource: closes distance, holds swing range, attacks on cooldown |
| Boss clown | Big fat grinning enemy, 320 hp, slow, own sprite sheet and larger scale |
| Specials | `HasSpecial` / `OnSpecialStart` / `TickSpecialMove` hooks; boss belly slam with a radial shockwave |
| Factions | `Fighter.Hostile` gates attacks, so there is no friendly fire |
| Tooling | Procedural sprite/effect/enemy generators, animation probe, AI probe, variant renderer |

## Not built

Everything else in the GDD. Nothing exists yet for: rarity tiers, monster
capture, classes, skill trees, item generation or affixes, levelling, shops,
story, or online co-op. One enemy type exists; there is no spawning system,
no death handling beyond health reaching zero, and no win or lose state.

## Known simplifications

- **No floor collider.** The arena is a flat plane and ground contact is a clamp
  at y=0 in `Fighter`. Platforms or uneven terrain need real collision shapes.
- **No colliders on props either**, so fighters walk through the boxes.
- **Hits resolve by arc query**, not physics hitboxes. Fine for cones, wrong for
  any attack needing another shape.
- **Dodge i-frames and the slide pose are not aligned.** Invulnerability runs
  0.05-0.26s; the extended slide frame holds 0.08-0.25s. Close, not matched.

## Traps worth remembering

- **Godot does not reimport changed art when running the game binary.** It loads
  the cached `.ctex`. Regenerating a sheet without an import pass leaves the game
  rendering stale art while the code looks correct. This hid an entire sprite set
  once. Use `tools/refresh_sprites.sh`.
- **Verify pixels, not intent.** The probe passed for hours while the directional
  sprites were invisible, because it checked which clip was selected rather than
  what was drawn. It now saves screenshots.
- **Kill running instances before relaunching.** Two Godot windows were live
  simultaneously at one point, so testing hit a stale build.

## Open decisions

See [README](README.md) for the full table. The ones that will bite soonest:
run-based or level-based structure, and max player count.

## Not done, still outstanding

`ECC_GATEGUARD` is still enabled, so ECC demands a written justification before
each new file and each first edit of a file. Disabling it needs
`.claude/settings.json` in the project with
`{ "env": { "ECC_GATEGUARD": "off" } }`, which must be created by hand: the
agent is blocked from writing Claude Code configuration.
