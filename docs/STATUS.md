# Status — what is actually built

Updated 2026-10-15. The [GDD](design/GDD.md) describes the intended game; this
records what exists in the repository today.

## Running it

```
# play
C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64.exe --path C:/Claude/ClownSquad

# automated checks (headless; prints clip/state/position assertions)
C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path C:/Claude/ClownSquad -- animtest

# the whole fight, unattended and fast: an AI must win it / an idle player must lose it
C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path C:/Claude/ClownSquad -- wavetest
C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path C:/Claude/ClownSquad -- losetest

# the old training room (dummy and respawning boss): add  -- sandbox
# the game opens on the world map; run one stage directly with  -- stage 2-3  (or  -- fight  for the old free fight)
# campaign checks: add  stage 1-1  to wavetest (bonus: stage 1-*; secret shop: stage 1-3 secret)

# the whole regression suite in parallel, one line per run (add --balance for the bot-vs-boss runs)
bash tools/run_probes.sh

# every weapon's combo and charge move against two dummies (add  skills all  for full trees)
C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path C:/Claude/ClownSquad -- weapontest

# moves: screen walls, charge throw, spin, flip, launch and wall bounce (17 checks)
C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path C:/Claude/ClownSquad -- movetest

# AI: boss closes distance, holds range, swings, slams
C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path C:/Claude/ClownSquad -- aitest

# regenerate art - ALWAYS use this, never gen_sprite.py alone (also renders the soundtrack; SKIP_MUSIC=1 skips it)
bash tools/refresh_sprites.sh

# screenshots into debug/ (need a window, so no --headless):
#   -- titleshot              intro panels, intro logo, title, load slots, squad select
#   -- stageshot stage 3-1    a stage's 3D scenery, twice along the road
#   -- mapshot mapall mapat 6-3   the world map on any island, all of them lit
#   -- castshot / castshot bosses the new enemies or bosses lined up, idle and mid-move
#   -- skillshot weapon pie level 10 stage 1-1   the skill menu for a weapon
#   -- demo stage 1-B level 4 [weapon X]   the smart bot plays a stage at normal speed (record with --write-movie)
# probes at a level: add  level 20  to wavetest (e.g. -- wavetest stage 7-B level 21)
# probes with real damage (no 2x bot bonus): add  fair ;  with a weapon: add  weapon popgun  (and  skills all )
# boss rush probe:  -- wavetest bossrush
```

In PowerShell, launch with the call operator: `& "C:/Tools/Godot/4.7.2/Godot_v4.7.2-stable_mono_win64.exe" --path C:/Claude/ClownSquad`.

Controls: **WASD** move, **J** attack, **Space** jump, **K/Shift** dodge, **Tab** Stat Tent. Hold attack to charge.
Gamepad: stick, **X** attack, **A** jump, **B/RB** dodge, **Back/Select** Stat Tent. Keyboard plays
immediately; pads join by pressing a button.

## Built

| Area | State |
| --- | --- |
| Combat | 3-hit combo, hitstop, input buffering, knockback, hit flash and squash |
| Hit reactions | Hitstun, launch and juggle with decaying lift, ground bounce, knockdown, get-up invulnerability, wall bounce (ADR 0005) |
| Mallet moves | Hold attack 1s standing: boomerang throw. Hold attack + left/right: travelling spin. Attack in the air: front flip |
| Arena | Screen edges are walls for players and enemies, derived from the camera frustum |
| Sound | 32 procedurally synthesised effects (tools/gen_audio.py); per-sound mix table in Sfx.cs |
| Soundtrack | 14 original songs, 1.5-3 minutes each, composed and performed by tools/gen_music.py: title, world map, one per world (circus march, calypso, swung forest, ragtime, waltz, heroic brass, desert, eerie caverns, dark rock, grand finale), boss and bonus. Each is intro, A, B, A, bridge, B, A, built from motifs in question/answer phrases, and loops seamlessly from the end of its intro (music_loops.json). Synthesised wavetable, struck and plucked instruments and drums in stereo with reverb; no vocals beyond a wordless choir. The WAVs (~240 MB) are generated and gitignored; Godot imports them QOA-compressed |
| HUD | Circus HUD: portraits, candy-stripe bars with damage trail, named enemies, wave ticket, balloon lives, boss bar, combo ranks; F1 controls, F3 debug |
| Pace | Whole game runs at 0.9 speed via one global time scale (`GameClock.Speed`) |
| The fight | Three waves (3 grunts, 5 grunts, boss + 2), staggered in from the screen edges; 3 shared lives; win and game-over states; press attack to replay |
| Grunt | Weak, bare-handed, launchable enemy with its own recoloured sheet |
| Death | Killing blow lifts the body; it lies in a floored pose, blinks and is removed |
| Enemy AI | Turn-taking by attack token, waiting ring, crowd spacing, depth line-up; timed on game time |
| Saving | One JSON save in the Godot user folder (`user://clownsquad_save.json`), written atomically; an unreadable save is set aside, never overwritten. Saved on every kill, wave clear, stage end, Stat Tent close and quit. Tests use a separate throwaway save |
| Levelling | XP from every enemy to every player (grunt 15, boss 150); curve 120 x level^1.35 to a cap of 50 (tripled from 40 on 2026-10-14); each level gives 3 stat points, 1 skill point, and a full heal. Failing a stage keeps XP |
| Stats | POW +4% damage, GUTS +8 max health, ZIP +2% speed (cap +40%), LUCK +1.5% crit chance (base 3%, cap 50%, crits x1.75). Spent in the Stat Tent: one card per player on their own controller, game paused, undo until closed; opens on Tab / Back and after a stage |
| Art | HD sprites by default: squad (44 frames, every clip), grunts, thrown mallet, Bertie and his fists at double resolution (player 96x128), with 5-tone hue-shifted ramps, form shading, cast shadows between parts and coloured outlines (tools/pixelart_hd.py, gen_sprite_hd.py, gen_enemy_hd.py). The original sheets and generators are kept; `-- classic` plays with them |
| Lighting | The Children of Morta / Sea of Stars recipe: crisp pixel sprites, smooth real lights on top. Every HD sheet has a generated normal map (from the same surface the baked shading used, with outward rim normals on the outline); a sprite shader lights them. Key light from the upper left, warm and cool circus lamps for coloured rims, a light flash on every hit, soft contact shadows under every fighter, a colour grade for saturation |
| Animation | HD idle 4 frames, walk 8 (in-betweens from a continuous cycle), rates set per sheet |
| Arena | The circus ring (tools/gen_arena.py, scripts/World/CircusArena.cs): pixel-art sawdust ring with a red-and-white curb and a worn gold star on a board floor; big-top wall with canvas stripes, the performers' curtain, poles, bunting and glowing bulbs; lit props (cannon, elephant tubs, crates) along the back; flickering bulb light, two sweeping follow-spots with beams in a light haze, drifting confetti. `-- classic` keeps the grey test room |
| Depth layers | Back to front: dark tent interior; two tiers of crowd that bob, jump up cheering at every 5-hit combo, kills and the win, with camera flashes and a cheer sound; the ring barrier with poles, bunting, bulbs and curtain; props; the fight; rim-lit rope posts at the bottom corners |
| Enemy scaling | Enemies take the host's level (health +12%, damage +8%, XP +15% per level). Grunts roll a rank: Common / Rare (x1.6) / Epic (x2.5) / wild Legendary (x5) at 1000/150/30/1, with a tint, size and glow; ranked enemies give 2.5x / 5x / 15x XP |
| Loot | Mallet, Hat, Ruff, Shoes. Base item, quality roll 85-115%, rarity Common to Legendary (1000/300/60/12/1, pushed by enemy rank, bosses, LUCK and FORTUNE), 0-6 affixes; ability affixes on Epic and Legendary (KABOOM finisher, BOING launches, FORTUNE drops). Drops glow in their rarity colour with a beam for Rare+, and drift to a nearby player; carried in a bag for the stage, banked on a clear (best item per slot is put on), lost on a fail. GEAR page in the Stat Tent (Q/E or bumpers) to swap what each slot wears |
| Polish | Camera punch-in on big hits and knockouts; the thrown mallet carries its own light |
| Title intro | Comic-book opening (scripts/UI/ComicIntro.cs): four slanted panels, a quarter of the screen each, slam in one after another between zigzag ink dividers, each a close-up portrait (tools/gen_portrait.py: PIP grins, POPPY winks, BINGO hollers, DOT beams) on halftone and speed rays; a flash, and the logo bounces onto a starburst as the title song starts, then glides to its place. Any button skips |
| Inventory | Pause > INVENTORY, in a stage or on the map: what each player wears, their bag sorted by slot and strength, full stats compared with what is worn, ATTACK to wear, DODGE twice to sell for a third of the shop price |
| Title and squad | The game opens on a title screen (scenes/Title.tscn) over the live circus ring: CONTINUE / NEW GAME / LOAD GAME / QUIT. Three save slots (the pre-slot save moved into slot 1), each summarised; starting over on a used slot asks twice. Character select: four clowns (PIP, POPPY, BINGO, DOT; own colour sheets), one each, P1 on keyboard, pads join with A; HUD portraits and the map clown follow the pick |
| Map pause | Esc / Start on the map: resume, Stat Tent, save & quit to title, save & quit game |
| World map | 3D (scripts/Map/WorldMap3D.cs): all ten islands in one animated sea (water shader with shore foam), height-mapped terrain wearing the pixel-art ground, each island dressed with its stages' 3D props and one or two of its landmarks (a turning Ferris wheel, lighthouse, mountains, towers...), instanced grass, flowers, stones or snow kept off the roads, unreached islands in silhouette, sailboats bobbing round the islands and clouds drifting with their shadows, a 3D landmark at each boss (big top, candy tower, mushroom, turning Ferris wheel, crystal spires, snow peak, popcorn machine, organ pipes, castle, grand finale with sweeping spots), pedestal/booth/star/gate markers, stepping-stone paths, per-world sky and light, tilt-shift depth of field; the gate sails the camera to the next island. Was: Super Mario World style overworld (scenes/WorldMap.tscn): ten themed islands (tools/gen_map.py), a road of stages 1-1 to 1-5 and a boss per world, a shop off the road, a bonus branch, a gate to the next world. Stages unlock as they are cleared; Q/E views reached worlds; pads join on the map. ADR 0007 |
| Difficulty | Generated per stage (scripts/Map/Campaign.cs): level floor rising 5 a world, more and bigger waves, rank spawns boosted up to 2.5x, grunt cooldowns 1.3s to 0.65s by world 10. Enemies scale to the host when above the floor |
| Gold and shops | Enemies pay gold into the stage bag (banked on a clear, lost on a fail). A shop per world (6 items, restocks on every clear there); a secret shop appears between waves in ~7% of stages with much better items. Bonus stages: 40s pinata parties, triple gold, can't be lost |
| Scrolling stages | Campaign fights are walked left to right (scripts/World/StageScenery.cs, tools/gen_stage.py): ten themed sets (midway, candy coast, forest, junction, mirrors, peaks, desert, caverns, fortress, finale), fully 3D: a procedural prop kit (scripts/World/Kit3D.cs, ~40 models from primitive meshes) builds landmarks in the distance under a per-world sky and depth fog, two rows of props with real lamp and torch light, a themed 3D fence, instanced grass and stones on the verges and blurred shapes at the lens; near and far depth of field; the pixel-art walkway and weather. `-- classic` keeps the painted version. Groups wait unseen along the road; reaching one locks the camera until it is beaten, then GO >> points on; the exit ends the stage. Bonus stages stay in the ring |
| QoL | Pause menu (Esc / Start: resume, Stat Tent, retry, map); results screen (time, KOs, best combo, gold, loot) with retry; breakable crates with healing snacks (left for whoever needs them) |
| Font | Luckiest Guy (Apache 2.0, assets/fonts) for all HUD, menus and pop text via UiFont |
| Bertie's fists | No arms: two gloves float beside him and fly at the target on each punch (jab, cross, both for the finisher); hands up and waving through the belly-flop hang |
| Feel helpers | Lane assist, dodge-cancel from recovery, hit sparks, combo counter, attack tokens |
| Dodge | Ground slide with i-frames 0.05-0.26s of a 0.35s roll |
| Jump | Gravity 26, jump speed 8.5, apex ~1.4 units, airtime ~0.65s |
| Input | Per-device polling, press-to-join local co-op, keyboard + N pads |
| Sprites | Profile only, 42 frames incl. charge, spin, throw, 8-frame flip and an empty-handed set; plus the spinning mallet |
| Weapon | Cylinder mallet, carried in every pose, swing arc 165/120/45/0 degrees |
| Effects | Ground impact burst and camera shake, scaled by combo step |
| Enemy AI | `BrawlerAi` as an IInputSource: closes distance, holds swing range, attacks on cooldown |
| Boss clown | Big fat grinning enemy, 320 hp, slow, own sprite sheet and larger scale |
| Enemy cast | Six enemies beyond the grunt, one new per world (EnemyCatalog): juggler (keeps away, lobs pins), unicyclist (lane charge, dizzy after), balloon artist (twists balloon dogs), mime (invisible rope pull and wall), stilt-walker (hard to hurt until 4 quick hits topple him), weightlifter (blocks from the front, grab and slam). Art by tools/gen_cast_hd.py; ADR 0008 |
| Bosses | One per world: 1 Bellyflop Bertie, 2 Bobo the Balancer (on a 3D beach ball: pin volleys, ball roll, bounces), 3 Madame Stripes and Rajah the tiger (whip, commanded pounce and roar, flaming hoop), 4 the Jalopy (clown car: rams, unloads passengers, honk, backfire), 5 Stretch (stretching lane punch, grab-and-reel, spring leaps), 6 the Flying Fratelli (two acrobats on 3D trapezes taking turns to dive), 7 Pyro Pete (fire breath cone, torches, ring of fire), 8 the Totem Tots (a tower of five that topples, throws a tot, and congas; one quits every fifth of its health), 9 Iron Ivan (barbell slam with a lane shockwave, grab-and-throw, dumbbells, flex), 10 the Ringmaster (carriage charges and whip, then on foot: hat bombs, doves, spotlights, calling in acts). Phases at 2/3 and 1/3 health, comic title card on arrival, telegraphs for every big move |
| Specials | `HasSpecial` / `OnSpecialStart` / `TickSpecialMove` hooks; boss belly slam with a radial shockwave |
| Factions | `Fighter.Hostile` gates attacks, so there is no friendly fire |
| Weapons | Eight, each with its own art on all four clowns, combo, sounds and charge move (scripts/Progression/Weapons.cs, ADR 0009): Squeaky Mallet (boomerang throw), Big Bertha hammer (ground-pound quake), Seltzer Bottle (squirts that shove; SOAK blasts the lane and leaves slippery puddles; replaced the rubber chicken 2026-10-16), Cream Pie (lobbed splat that blinds), Juggling Pins (fan of pins), Slapstick (stunning slaps, CLAP), Pop Gun (fires corks; piercing big cork), Balloon Sword (longest reach; fencer's lunge). Changed only on the map, between stages |
| Skill trees | One per weapon: three columns of three, learned top to bottom, a node costs its tier in skill points; points are shared, learned skills belong to the weapon. Pause > SKILLS anywhere; unlearning and weapon swaps only from the map's SKILLS & WEAPONS. Save version 5 |
| Settings | Title, fight pause and map pause: music, sound, screen shake, fullscreen, difficulty (Easy 0.8x enemy health / 0.6x damage / +2 lives; Hard 1.25x / 1.35x / -1 life). user://settings.cfg |
| Boss intros | Each boss arrives with a drumroll, a swinging spotlight, a crowd "ooh", its own signature sound and the comic title card; it cannot be hit for the 2.6 s intro. Crowd gasps at each phase change and laughs when a player goes down |
| Act cards | The first time each enemy kind appears, a NEW ACT! card shows its portrait, name and how to beat it |
| Boss rush | Title > BOSS RUSH: all ten bosses back to back as level-30 clowns on a save of its own, half a heal between, against the clock; best time kept |
| Bestiary | Title > BESTIARY: every act met in any save slot, with portrait and tip; bosses show whether they are beaten; unmet ones are silhouettes |
| Practice ring | Title > PRACTICE RING: your clowns (a copy of the last save; nothing kept) with three dummies; change weapon on the spot, switch the whole tree on and off, call in grunts, damage meter |
| Controls | Settings > CONTROLS: rebind every action on keyboard and gamepad (user://controls.cfg); taken keys swap; d-pad moves |
| Online | Title > ONLINE: host (UDP 7777) or join by IP, lobby with everyone's clown, level and weapon; guests bring their own character. The host plays the campaign from their map and guests play every stage with them, mirrored live, and keep the XP, gold and loot they earn in their own save (ADR 0010 phases 2-3) |
| Steam-ready | Platform layer (`IPlatform`, offline today), 34 achievements wired with an in-game list, stats, boss-rush leaderboard, rich presence, overlay pause; Steamworks checklist and the remaining SDK step in [STEAM.md](STEAM.md) |
| Session | One place for cross-scene mode and save path (Campaign, BossRush, Practice, Test); command-line test flags only in debug builds |
| Tooling | Procedural sprite/effect/enemy generators, animation probe, AI probe, variant renderer, the smart test bot (reads telegraphs, hazards and shots; dodges and jumps rings), weapon probe, `tools/run_probes.sh` |

## Not built

From the GDD: online prediction and Steam networking (ADR 0010 phase 4, STEAM.md),
monster capture, classes, story. Also not yet: Steam integration (achievements,
cloud saves), controller rebinding, localisation.

## Known simplifications

- **No floor collider.** The arena is a flat plane and ground contact is a clamp
  at y=0 in `Fighter`. Platforms or uneven terrain need real collision shapes.
- **No colliders on props either**, so fighters walk through the boxes.
- **Hits resolve by box and radius queries**, not physics hitboxes.
- **A floored fighter has no art**; it is the hit pose squashed flat.
- **Shell commands over about 8 KB fail** in this environment with a misleading
  quote error. Write large files with the file tool, not a heredoc.
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

- The ECC plugin (and its GateGuard edit gate) was disabled on 2026-10-14 in the
  user's Claude Code settings.
- Balance needs human playtesting: the smart bot wins 7-B about half the time and
  reaches boss 7 of 10 in the boss rush; the eight weapons are tuned by probe only.
- Daily Circus (built 2026-10-16): Title > DAILY CIRCUS.
  - One stage a day, picked from the UTC date: a normal fight of up to four waves from anywhere in the campaign, the same waves for everyone.
  - One of ten acts: ALL-STAR NIGHT, PIE NIGHT, MOON BOUNCE, GLASS CANNONS, THE BIG TOP, STAMPEDE, DOUBLE TIME, ONE-HIT WONDERS, BOUNCY CASTLE, LIGHTS OUT.
  - Fresh copies of your clowns at level 20 against level-15 acts, on a save of its own.
  - Score: knockouts, best combo, time against par, and a bonus if nobody falls.
  - The day's best is kept locally and on a per-day Steam leaderboard.
  - Achievement: SAME TIME TOMORROW.
- The code review of 2026-10-15 ([REVIEW-2026-10-15](REVIEW-2026-10-15.md)) lists
  the open risks; R2 (GameRoot split), R3 (Session), R4 (Fx), R6 (rebinding)
  and R7 (debug flags) are done. Still open: R1, push to GitHub and stop
  Google Drive syncing the repo.
- The soundtrack is synthesised. A composer or licensed recordings would be
  the step up for release; the song structure and loop points carry over.

## Story (built 2026-10-16)

- The Ringmaster has stolen the circus's laughter into his top hat and hypnotised every act to perform forever.
- A comic-style story card the first time the squad reaches each world names what's wrong there and who is headlining.
- A last card, THE LAST LAUGH, plays after the Ringmaster falls.
- Each card shows once per save (`CampaignState.StorySeen`); `-- storyshot N` takes a picture of card N without touching the save.
