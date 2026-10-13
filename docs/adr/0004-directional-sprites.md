# 4. Everything that moves uses directional sprites

- **Status:** Accepted
- **Date:** 2026-10-13

## Context

The game renders 16-bit sprites as camera-facing billboards in a 3D world
(ADR 0002). A billboard that never changes with heading reads as a cardboard
cutout sliding across the floor: the silhouette stays identical while the
position changes, and the eye reports gliding rather than movement.

This was observed directly. A front-facing walk cycle, however exaggerated and
however many frames, could not read as walking left or right. Only adding a
profile view fixed it.

## Decision

**Any entity that moves under its own power has a directional sprite set.**

This covers player characters, monsters, summons, projectiles, thrown weapons,
moving pickups, and anything added later that travels. It is a default, not a
case-by-case judgement.

### The contract

Each visual state exists in three sets, selected by heading:

| Heading | Prefix | Example |
| --- | --- | --- |
| Toward the camera (+Z) | none | `walk` |
| Away from the camera (-Z) | `back_` | `back_walk` |
| Horizontal | `side_` | `side_walk`, mirrored for leftward travel |

Horizontal travel wins over depth when both apply, because profile frames read
far better than a front view sliding sideways.

Implementation is `scripts/Visuals/DirectionalSprite.cs`. An entity sets a
facing vector and plays a *state*; the component resolves the prefix, handles
mirroring, and falls back to the unprefixed clip when a direction is missing.
Clip lists come from a JSON frame map emitted beside each sheet, so adding
frames never requires a code change.

### Exemptions

- Static scenery and props
- UI
- View-agnostic effects: sparks, dust, impact flashes
- Entities that move but have no meaningful heading, such as a spinning pickup

An exemption is a deliberate choice, recorded where the entity is defined.

## Consequences

**Positive**
- Movement reads correctly in every direction, which is the whole point of
  choosing billboards over flat 2D.
- One component means one place to fix mirroring, prefix selection and impact
  effects for every entity in the game.
- Falling back to the unprefixed clip means a partially authored entity still
  renders rather than vanishing.

**Negative — accepted**
- **Art cost rises roughly threefold per entity.** Procedurally generated art
  absorbs this cheaply, since poses are parameterised and a direction is a
  drawing-mode flag. Commissioned art must budget three views per state, and
  this needs stating in any art brief.
- Diagonal movement snaps between the side and back sets at 45 degrees. Eight
  directions would smooth it at double the frame count; deferred until it
  proves to matter.
- Every new entity type needs a sheet plus frame map before it can render.

## Operational note

Godot does **not** reimport changed assets when running the game binary; it
loads the cached `.ctex`. Regenerating a sheet without an import pass leaves the
game rendering stale art while all the code looks correct. This silently hid the
first directional sprite set entirely. Always regenerate through
`tools/refresh_sprites.sh`, which imports as part of the same step.
