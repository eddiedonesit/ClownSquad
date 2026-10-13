# 4. Everything that moves uses directional sprites

- **Status:** Accepted, revised same day
- **Date:** 2026-10-13

> **Revision (2026-10-13).** Originally specified three directional sets
> (front, back, profile). Revised to **profile only**: fighters face left or
> right and never toward or away from the camera, and attacks can only resolve
> horizontally. The front and back sets were removed rather than left unused.
> Sections below reflect the revision; the rationale for directional sprites at
> all is unchanged.

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

**Facing is horizontal only.** Each visual state is a single clip, drawn facing
right and mirrored for leftward travel. There are no front or back sets.

Entities may still *move* in depth — walking into and out of the plane is core
to a brawler — but depth movement never turns the sprite. Heading is projected
onto the X axis and nothing else.

A direct consequence, and the reason this was specified: **attacks can only ever
go left or right.** Attack arcs resolve along the facing vector, so constraining
facing to the X axis structurally prevents attacking up or down. It is not a
check that can be forgotten at a call site; there is no way to express a
vertical attack.

Implementation is `scripts/Visuals/DirectionalSprite.cs`. An entity sets a
heading and plays a *state*; the component takes the sign of the X component and
mirrors accordingly. Clip lists come from a JSON frame map emitted beside each
sheet, so adding frames never requires a code change.

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
- One clip per state means an entity cannot be partially authored into a broken
  state: either the clip exists or it does not.

**Negative — accepted**
- **Art cost is one profile view per state**, which is the cheapest directional
  scheme available and a third of what the original three-set version required.
  Any art brief should say profile view, mirrored, no front or back.
- **Depth is read only through position and scale**, never through the sprite.
  A fighter walking away from the camera looks identical to one walking toward
  it. This is normal for the genre but is a real expressive limit.
- Entities whose identity depends on facing the viewer, such as a boss that
  should loom at the camera, cannot do so without breaking the rule. No such
  entity is planned; revisit if one appears.
- Every new entity type needs a sheet plus frame map before it can render.

## Operational note

Godot does **not** reimport changed assets when running the game binary; it
loads the cached `.ctex`. Regenerating a sheet without an import pass leaves the
game rendering stale art while all the code looks correct. This silently hid the
first directional sprite set entirely. Always regenerate through
`tools/refresh_sprites.sh`, which imports as part of the same step.
