# tools/

`DebugCapture.gd` — attach to a 3D scene root to dump runtime camera/node
transforms, screen-projected positions and distances, then save a framebuffer
capture to `debug/capture.png` and quit.

Use it when a scene renders wrongly and you need ground truth rather than
guesswork. It caught a transposed camera basis that aimed the camera at the sky.

To use: set `script = ExtResource(...)` pointing here on the scene root, run the
project, then detach it again. `debug/` is gitignored.

`gen_sprite.py` — generates the 16-bit-style placeholder character to
`assets/sprites/clown_16bit.png` (48x64). Draws flat material regions, then
applies pixel-art passes: dark silhouette outline, rim light on the upper-left
edge, shadow on the lower-right. Edit the drawing block to change the character.

`preview_sprite.py <in.png> <out.png> [scale]` — nearest-neighbour upscale onto
a checkerboard so a 48x64 sprite can actually be inspected. Defaults write to
`debug/sprite_preview.png` at 8x.

`refresh_sprites.sh` — **use this instead of running gen_sprite.py directly.**
Running the Godot binary does not reimport changed assets; it loads the cached
`.ctex`. Regenerating a sheet without a reimport pass leaves the game rendering
stale art while all the code looks correct. That is exactly how the directional
sprites appeared broken: clip selection was right, but the texture the atlas
regions sampled predated the frames.
