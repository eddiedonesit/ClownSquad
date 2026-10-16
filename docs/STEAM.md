# Steam: checklist and integration plan

What is ready in the game, what you need to do on Steamworks, and the one
remaining code step. The game talks to a store only through `IPlatform`
(scripts/Platform/Platform.cs). Today it runs `OfflinePlatform`; the Steam build
adds a `SteamPlatform` class and nothing else changes.

## 1. Ready in the game now

| Feature | State |
|---|---|
| Achievements | 34, all triggers wired; in-game list (Title > ACHIEVEMENTS); earned offline into `user://platform.cfg` and unlocked on Steam the first time the Steam build runs |
| Stats | `kos`, `stages`, `bosses` (integers, counted up) |
| Leaderboard | `boss_rush`: best boss-rush time, lower is better |
| Rich presence | Set on the title, map, every stage, boss, boss rush, practice and online lobby |
| Overlay | `PlatformPump` pauses the game while the overlay is open |
| Controllers | Full gamepad support and rebinding (Steam Deck friendly) |
| Online | Host/join over ENet today; the Steam transport replaces it (§4) |
| Cloud saves | Needs no code: Steam Auto-Cloud (§3) |
| Practice and tests | Never earn achievements or stats |

## 2. What you do on Steamworks

1. **Join Steamworks** at partner.steamgames.com: identity and tax forms, then pay the Steam Direct fee ($100 per game, recouped after $1,000 in sales). You get an **App ID**.
2. **Achievements** (App Admin > Stats & Achievements > Achievements). Add one per row below; the **API name must match exactly**. Each needs two 256×256 icons (earned and greyed). I can generate these in the game's art style once you're ready.
3. **Stats:** `kos`, `stages`, `bosses`, type INT, "increment only".
4. **Leaderboard:** `boss_rush`, sort ascending, display type "time (milliseconds)". The game will send milliseconds.
5. **Rich presence:** upload a localisation file with one token, `#Status = %status%`. The game fills `status`.
6. **Steam Cloud:** Auto-Cloud, root `WinAppDataRoaming`, subdirectory `Godot/app_userdata/Clown Squad`, patterns:
   - `clownsquad_slot*.json` (save slots)
   - `settings.cfg`
   - `controls.cfg`
   - `platform.cfg`
   - Quota: 10 files, 5 MB.
7. **Store page:** capsules (header 920×430, small 462×174, main 1232×706, vertical 748×896, library 600×900 and hero 3840×1240), 5+ screenshots, a trailer, description and tags (Beat 'em up, Co-op, Action RPG, Pixel Graphics, Local Co-op, Online Co-op). The `demo` and `--write-movie` tools make trailer footage.
8. **Builds:** SteamPipe depot for Windows; upload with SteamCMD or the SteamPipe GUI.

### Achievement API names

| API name | Name | How |
|---|---|---|
| first_clear | OPENING NIGHT | Clear your first stage |
| bonus_clear | PINATA PARTY | Clear a bonus stage |
| flawless | NOT A SCRATCH | Clear a stage without anyone taking a hit |
| secret_shop | BEHIND THE CURTAIN | Find the secret shop |
| legendary | ONCE IN A BLUE MOON | Pick up a Legendary item |
| combo_25 | CROWD PLEASER | Land a 25-hit combo |
| combo_50 | STANDING OVATION | Land a 50-hit combo |
| level_10 | UP AND COMING | Reach level 10 |
| level_25 | HEADLINER | Reach level 25 |
| level_50 | LEGEND OF THE RING | Reach level 50 |
| full_tree | MASTER OF ONE | Learn every skill in a weapon's tree |
| all_acts | SEEN IT ALL | Meet every act in the circus |
| hard_boss | NO SAFETY NET | Beat a boss on Hard |
| rush_clear | THE FULL SHOW | Beat the boss rush |
| rush_fast | QUICK CHANGE | Beat the boss rush in under 20 minutes |
| together | SEND IN THE CLOWNS | Play an online game with a friend |
| boss_1 ... boss_9 | WORLD n TAMED | Beat each world's boss |
| boss_10 | THE GREATEST SHOW (hidden) | Beat the Ringmaster |
| win_mallet, win_hammer, win_seltzer, win_pie, win_pins, win_slapstick, win_popgun, win_balloonsword | (weapon) CHAMP | Beat a boss holding that weapon |

## 3. Cloud saves need no code

Steam Auto-Cloud syncs the files in §2.6 itself. Saves are already written
atomically (temp file, then replace), which is what Auto-Cloud expects.

## 4. The code: `SteamPlatform` (built) and the Steam transport (next)

**Built (2026-10-16):** Facepunch.Steamworks 2.3.3 (NuGet) and `steam_api64.dll`
beside the project; `scripts/Platform/SteamPlatform.cs`.
- At start-up `Platform.Start()` connects to Steam if it is running; otherwise the game plays offline as before.
  Tests, headless runs and `-- nosteam` always stay offline.
- Achievements, stats (sent in batches), the `boss_rush` leaderboard (milliseconds), rich presence (`#Status`) and the overlay pause all go to Steam.
- Everything is also kept in the offline record, which the in-game ACHIEVEMENTS list reads; anything earned offline unlocks on Steam at the next start.
- **App ID:** `SteamPlatform.AppId`, set to 480 (Valve's "Spacewar" test app) until Clown Squad has its own. Change that one number and `steam_appid.txt`.
- **To try it:** open Steam, then start the game. The log says `Steam: signed in as ...`, Shift+Tab opens the overlay and pauses the game, and friends see what you are doing (as Spacewar until the real App ID).

- **Online over Steam:** a `SteamPeer : MultiplayerPeerExtension` (~300 lines)
  carrying Godot's RPCs over Steam Networking Sockets, plus a Steam lobby.
  - Host: create the lobby.
  - Friends: join from the Steam friends list ("Join game") or by invite.
  - No IP addresses and no port forwarding.
  - `NetSession` gains `HostSteam()` and `JoinSteam(lobby)`; everything above it (lobby screen, roster, character transfer, phase 2 and later) is unchanged.
- **Export:**
  - Godot 4.7.2 .NET export templates (another download to approve).
  - `steam_appid.txt` beside the exe for local testing (git-ignored already).
  - A `-- classic`-free release build. Test flags are already ignored in release builds.

## 5. Before release (not code)

- Steam Deck: check text size at 1280×800 and that every menu works on the pad.
- Age rating questionnaire (cartoon violence).
- Playtest and balance (weapons, late bosses).
- The soundtrack is synthesised; decide whether to keep it or commission music.
