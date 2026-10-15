# 10. Online co-op: host-authoritative puppets over ENet, direct IP

- **Status:** Accepted; phases 0 and 1 built
- **Date:** 2026-10-15
- **Extends:** ADR 0003 (co-op architecture)

## Context

ADR 0003 settled host authority and the input-source abstraction, and named
Steam P2P as the eventual transport. The request now: basic host/join, no
matchmaking. The 2026-10-15 review (§4) found the game was ready in its input
layer but had no way to send effects or refer to spawned things across
machines.

## Decision

**Transport:** Godot's high-level multiplayer over `ENetMultiplayerPeer`.
- The host listens on UDP port 7777 for up to 3 guests; a guest types the host's address.
- Steam networking can replace ENet later under the same RPCs.

**Model: the host simulates and guests are puppets.**
- The host runs the game exactly as offline.
- Guests send their `InputCommand` each frame. The host feeds it to a `RemoteInput : IInputSource`, so gameplay cannot tell a remote player from a local one.
- The host sends guests snapshots (by `NetRegistry` number) and effects (by `Fx` event).
- Guests run no simulation.
- No lockstep, for the reasons in ADR 0003.

**Characters:**
- Each player brings their own character from their own save (P1 of their most recent slot), sent as save-file JSON in the hello message.
- The host plays them as that character.
- Rewards go back to each guest to save locally (phase 3).

**Phases:**

| Phase | Contents | State |
|---|---|---|
| 0. Prep | `Fx` (every gameplay effect through one class, raising `Fx.Emitted`; `Fx.Replay` on guests); `NetRegistry` (every fighter, shot, telegraph, hazard and drop numbered on entering the scene) | Built |
| 1. Connect | `NetSession` (host, join, hello with protocol version and character, roster, kick, leave, disconnect handling); Title > ONLINE lobby showing everyone's clown, level and weapon, and the host's addresses | Built; `nettest host` + `nettest join` |
| 2. Play a stage | `RemoteInput`; input RPC (unreliable ordered); host snapshots at 20–30 Hz and `Fx` event forwarding; guest puppet scene (fighters, shots, telegraphs, hazards, HUD, camera) | Next |
| 3. Campaign | Host drives the map; results, XP, gold and loot sent to each guest to save; shops, inventory and skills local, changed record sent back | Later |
| 4. Polish | Prediction for the guest's own movement, ping display, latency simulation for testing, host-leaves handling mid-stage | Later |

## Consequences

**Positive:**
- **Small change to gameplay code:** phase 2 adds a transport and a puppet view; it does not touch fighters or moves.
- **Testable on one machine:** two copies of the game over localhost, automated.

**Negative, accepted:**
- **Port forwarding:** internet play by IP needs the host to open UDP 7777. LAN and VPN apps (Tailscale, ZeroTier) work without it; Steam networking removes it at release.
- **Guest latency:** guests feel one round trip of input latency until phase 4's prediction.
- **Trust:** a guest's character comes from their own save, so a guest could edit their save. Acceptable for friends-only co-op without matchmaking.
