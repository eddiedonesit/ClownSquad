# 3. Co-op architecture: host-authoritative with input-source abstraction

- **Status:** Accepted
- **Date:** 2026-10-13

## Context

Co-op is confirmed. Both references are multiplayer (Rampage Knights is 2-player,
Guardian Heroes up to 6), and the original brief specified that enemies scale to
"the primary player or host's level".

The complication is that nearly every system in the design produces a random
result: item quality rolls, affix rolls, capture chance, and the ~0.1% wild
legendary spawn. In a multiplayer game each needs a defined answer to "who
decided this, and what do the other players see?"

Deciding this after the systems exist means revisiting all of them, which is why
it is settled before any of them are built.

## Decision

**Host-authoritative simulation, with players driven by an input-source
abstraction.**

1. **Players are entities driven by an input source, never by a device directly.**
   An input source is one of: a local device (keyboard, gamepad), a remote peer,
   or AI. Gameplay code cannot tell which it is.
2. **All randomness is resolved on the host.** Loot, affix, capture and spawn
   rolls happen in one place. Clients receive *results*, never perform rolls.
3. **Clients are authoritative over their own input only**, never over outcomes.
4. **Encounters use explicit seeds** owned by the host, so an encounter is
   reproducible for debugging and replay.

Each stage is additive rather than a rewrite:

| Stage | What it is | Networking |
| --- | --- | --- |
| Single-player | One local input source | None |
| Local co-op | Several local input sources, shared screen | None |
| Online co-op | Some input sources are remote peers | Godot MultiplayerAPI over Steam P2P |

**Local co-op is built first.** It exercises the entire multi-player code path --
multiple entities, shared camera, host authority -- with no networking at all.
Online then replaces the transport beneath an abstraction that is already
load-bearing, rather than introducing a new concept late.

## Alternatives considered

| Option | Why rejected |
| --- | --- |
| **Deterministic lockstep** | Minimal bandwidth and classic for brawlers, but needs bit-identical simulation across machines. Godot physics plus floating-point drift makes that fragile, and one desync corrupts the run. |
| **Client-authoritative** | Simplest to write, but a client deciding its own loot rolls makes rare drops meaningless in an ARPG economy. |
| **Dedicated server** | Unnecessary for small-group co-op; adds hosting cost for no player-facing benefit. |
| **Single-player first, co-op later** | The explicitly rejected path. Every random-result system would need rewriting, and input and camera assumptions would bake in wrongly. |

## Consequences

**Positive**
- Local co-op is playable early with no networking risk, while still exercising
  the real architecture.
- One place to reason about fairness, because one machine rolls.
- Single-player is a degenerate case of co-op, not a separate mode, so there is
  one code path to maintain and test.
- Encounter seeds make rare events reproducible, which matters enormously for
  testing a 0.1% spawn.

**Negative -- accepted**
- **Host latency advantage.** The host sees zero-latency input; remote players
  will need client-side prediction once online lands.
- **No host migration initially.** If the host leaves, the session ends.
- **Shared-screen local co-op constrains level design**, since players cannot
  separate further than the camera allows.
- Replicating results rather than seeds costs slightly more bandwidth.
  Irrelevant at this scale.

## Open

- Does online ship at launch or post-launch? Affects scheduling of Steamworks
  P2P work, not the architecture.
- Maximum player count. Affects encounter density and scaling, not structure.
- How enemy scaling resolves across mixed-level parties. See GDD section 7.
