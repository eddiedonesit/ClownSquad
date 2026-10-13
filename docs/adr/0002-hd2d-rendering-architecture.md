# 2. HD-2D rendering: 3D scene root with billboarded sprites

- **Status:** Accepted
- **Date:** 2026-10-13

## Context

The target look is Octopath Traveler's "HD-2D": pixel-art character sprites
standing inside lit 3D dioramas, under heavy tilt-shift depth of field and bloom.

Godot offers two incompatible ways to combine 2D and 3D, and the choice
determines the whole scene tree. Reversing it later means rebuilding every scene:

1. **2D scene root**, compositing a 3D `SubViewport` behind 2D sprites.
2. **3D scene root**, with sprites as camera-facing billboards inside the 3D world.

## Decision

Use a **3D scene root** (option 2). Characters are `AnimatedSprite3D` billboards
positioned in the 3D world.

Option 1 is rejected because it flattens the 3D render into a backdrop layer.
Sprites could then never be occluded by world geometry, receive world lighting,
or be correctly depth-sorted between foreground and background props — all of
which the HD-2D look depends on.

## Implementation constraints this imposes

- **Renderer must be Forward+.** The Compatibility renderer lacks depth of field
  and volumetric fog, which are the signature of the look.
- **Camera3D uses a narrow FOV (~20-30°) at a fixed downward angle**, not a true
  orthographic projection. Narrow-FOV perspective approximates orthographic while
  retaining the slight depth cue Octopath has; full orthographic reads flat.
- **`AnimatedSprite3D` settings are load-bearing:**
  - `billboard = BILLBOARD_FIXED_Y` — faces the camera but stays upright.
  - `texture_filter = NEAREST` — keeps pixel art crisp.
  - `alpha_cut = ALPHA_CUT_DISCARD` — **critical.** Without it sprites do not
    write depth and will render through walls and z-fight against geometry.
- **Hold a fixed texel-to-world-unit ratio** via `pixel_size`. Drifting on this
  makes character scale visibly inconsistent between scenes.
- **WorldEnvironment carries the look:** far+near DOF blur (tilt-shift), glow for
  lanterns and highlights, volumetric fog for depth, and filmic tonemapping.
- **Lighting sells the diorama:** a shadow-casting `DirectionalLight3D` plus
  `OmniLight3D` accents. Shadows on the 3D geometry are what makes it read as a
  physical model rather than a painting.

## Consequences

**Negative — accepted risks**
- `ALPHA_CUT_DISCARD` forbids partial transparency, so sprites get hard edges and
  no soft anti-aliased blending. Acceptable for pixel art; blocks soft-edged VFX
  on sprites, which must use separate unshaded quads instead.
- Billboards cast misleading shadows as they rotate. Standard fix: disable shadow
  casting on character sprites and use a blob-shadow decal underneath.
- DOF and volumetric fog are expensive. If Steam Deck is a target, this needs a
  performance budget early and a quality-settings path.
- Pixel-art sprites clash with detailed 3D. Environment art must stay low-detail
  and stylized to sit alongside them.

## Open questions — DEFERRED

Both were deliberately deferred on 2026-10-13. Revisit as release approaches;
neither blocks current work, but both get more expensive the longer they wait.

| Question | Why it matters | Revisit when |
| --- | --- | --- |
| Is Steam Deck a launch target? | DOF + volumetric fog are cheap on desktop GPUs and expensive on a Deck. Decides whether a quality-settings path is needed. | Before performance work, and before any Steam store page commits to Deck Verified |
| Lock sprite resolution and texel-to-world ratio | Every piece of commissioned art is authored against it; changing it later rescales every character. | Before commissioning art beyond placeholders |

**Current working values (provisional, not locked):** character sprites are
48x64 px with `pixel_size = 0.0225`, giving a character 1.44 world units tall.
