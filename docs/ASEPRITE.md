# Aseprite workflow

How hand-drawn sprites get into the game, alongside the generated ones.

## Setup (once)

1. **Install Aseprite** (aseprite.org or Steam, about $20). The default path is easiest: `C:\Program Files\Aseprite\Aseprite.exe` (Steam: `C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe`).
2. **Configure the plugin.** In a Claude session, run the plugin's setup (`/pixel-setup`, with the path if it is not auto-detected). It writes `C:\Users\<you>\.config\pixel-mcp\config.json` containing `aseprite_path`.
3. **Start a new session.** The plugin's server connects when a session starts. Before setup it failed with "connection closed", because there was no Aseprite and no config.

## What the plugin can do

pixel-mcp drives Aseprite from commands. It has more than 40 tools:

| Area | Tools |
|---|---|
| Canvas and layers | create, add and delete layers, flatten |
| Drawing | individual pixels in batches, lines, polygons, rectangles, ellipses, flood fill, all optionally snapped to the palette |
| Selection | rectangle and ellipse selections, copy, cut, paste |
| Palettes | read, set, sort and check harmonies; quantize; dithered fills |
| Shading | palette-constrained shading by light direction, anti-aliasing suggestions |
| Transforms | flip, rotate (including RotSprite), scale, crop, resize, outline |
| Animation | frames, durations, tags, linked cels |
| Reading and importing | read pixels back; import an existing PNG as a layer (so generated sprites can be hand-finished); analyse a reference image; downsample a large image to pixel-art size |
| Export | PNG, GIF, sprite sheets with JSON; save `.aseprite` sources |

It cannot open an existing `.aseprite` file as a sprite: it starts new sprites,
and brings existing art in with `import_image`.

## Into the game: `tools/aseprite_import.py`

Export from Aseprite (or the plugin's `export_spritesheet`) as a sheet PNG plus
JSON, with tags named after the game's clips (`idle`, `walk`, `attack`, `hit`,
`air`, `special`, `down`...). Then:

```
python tools/aseprite_import.py juggler.png juggler.json cast/juggler --canvas 220x300 --baseline 260
```

The tool writes the game's format:
- a horizontal strip;
- `.frames.json` with clips from the tags and frame rates from the durations;
- a generated normal map (`_n.png`) for the lighting.

Trimmed sheets are restored to full frames. `--scale 2` draws at half size and
plays at double size.

**Normal maps for hand-drawn art:** each region enclosed by dark outline, or by
a change of hue (skin to hair, suit to glove), is domed on its own, and the
outline faces outward for the rim light. This is close to the generated sprites'
maps; painted stripes show as faint creases.

**Checked:** an existing sheet passed through as a fake Aseprite export comes
out pixel-identical, with the same clips and baseline.

## Choosing a pixel density

The game's HD sprites are drawn at full resolution: a player frame is 208×288,
with a body about 96×128. Mixing pixel sizes on screen ("mixels") looks wrong,
so a hand-drawn sprite must either:

- **match HD density.** It sits with everything else, but it means a lot of pixels per frame. Best for hand-finishing generated sprites (import, then a detail pass) and for small things: icons, effects, items.
- **draw at half density (`--scale 2`).** Chunkier and more classic, four times less to draw. Only right if the whole game moves that way over time.

The test sprite will show both, so the choice can be made by eye.

## Test sprite: the Juggler, both ways (2026-10-15)

Aseprite 1.3.18 at `C:\Program Files (x86)\Steam\steamapps\common\Aseprite`;
the plugin's config is written (`~/.config/pixel-mcp/config.json`) and connects
from the next session. The test drives Aseprite directly with Lua in batch mode,
which is what the plugin does underneath:

| Step | Tool |
|---|---|
| Game sheet to `.aseprite` (frames + tags) | `tools/aseprite/sheet_to_aseprite.lua` |
| **A**: HD finishing pass (curated hue-shifted ramps, selective outline, rim light, lone-pixel cleanup); 68 colours down to 34 | `tools/aseprite/finish_hd.lua` |
| **B**: half density: majority-colour halving, small palette, fresh silhouette outline, inner lines kept, then the hand-drawn head (`art/aseprite/juggler_head.txt`, a "hit" face variant, arm-in-front frames) | `tools/aseprite/classic_b.lua` |
| Back into the game | `tools/aseprite_import.py` (B with `--scale 2`) |

Sources: `art/aseprite/juggler_original.aseprite`, `juggler_A_finished.aseprite`
(layers: original, finished), `juggler_B_classic.aseprite` (layers: base, hand).
In game: `-- castshot castart juggler=juggler_a` (or `juggler_b`); comparison in
`debug/juggler_test_ingame.png`.

**Findings:**
- **A** is a safe polish: cleaner colours, coloured outlines and a warmer face; it sits with everything else.
- **B** has more character (a readable eye and expression, a cartoon face), but its chunkier pixels stand out among HD actors. It is only right if the whole cast moves to it.
