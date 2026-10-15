#!/usr/bin/env bash
# Regenerate sprites AND force Godot to reimport them.
#
# Running the Godot binary does NOT reimport changed assets - it loads the
# cached .ctex. Regenerating a sheet without this step leaves the game
# rendering stale art, which silently broke the directional sprites once.
set -e
cd "$(dirname "$0")/.."
GODOT="${GODOT_BIN:-/c/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe}"
python tools/gen_sprite.py
python tools/gen_effects.py
python tools/gen_enemy.py
python tools/gen_sprite_hd.py
python tools/gen_portrait.py
python tools/gen_enemy_hd.py
python tools/gen_cast_hd.py
python tools/gen_arena.py
python tools/gen_map.py
python tools/gen_stage.py
python tools/gen_audio.py >/dev/null
# The soundtrack takes a couple of minutes; skip it with SKIP_MUSIC=1.
[ -n "$SKIP_MUSIC" ] || python tools/gen_music.py
"$GODOT" --headless --path . --import >/dev/null 2>&1
echo "sprites regenerated and reimported"
