# Clown Squad — Documentation

| Document | What it covers |
| --- | --- |
| [Status](STATUS.md) | What is actually built, how to run it, and the traps |
| [Design Document](design/GDD.md) | The game: references, enemies, capture, classes, loot, progression, weapons |
| [Architecture Decision Records](adr/README.md) | Why the project is built the way it is |

## Decisions currently open

Tracked so they do not quietly disappear.

| Decision | Where | Blocking? |
| --- | --- | --- |
| Co-op scope — **resolved**, see [ADR 0003](adr/0003-coop-architecture.md) | [GDD 2.1](design/GDD.md) | Closed |
| Does online co-op ship at launch or after? | [ADR 0003](adr/0003-coop-architecture.md) | No — scheduling only |
| Maximum player count | [ADR 0003](adr/0003-coop-architecture.md) | No — affects encounter tuning |
| Run structure — **resolved: persistent levels**, see [ADR 0006](adr/0006-persistent-progression.md) | [GDD 2.2](design/GDD.md) | Closed |
| Run structure detail — **resolved: world map of 10 worlds**, see [ADR 0007](adr/0007-world-map-campaign.md) | [ADR 0007](adr/0007-world-map-campaign.md) | Closed |
| Co-op enemy scaling — **resolved: scale to the host**, see [ADR 0007](adr/0007-world-map-campaign.md) | [GDD 7](design/GDD.md) | Closed |
| HUD font — candidates shown, awaiting a pick | STATUS | No |
| Failing a stage — **resolved: keep XP, lose loot**, see [ADR 0006](adr/0006-persistent-progression.md) | [ADR 0006](adr/0006-persistent-progression.md) | Closed |
| Steam Deck as a launch target | [ADR 0002](adr/0002-hd2d-rendering-architecture.md) | Deferred to near release |
| Lock sprite resolution / texel-to-world ratio | [ADR 0002](adr/0002-hd2d-rendering-architecture.md) | Deferred, before commissioning art |
| Story branch shape and endings | [GDD 9](design/GDD.md) | Deferred, before content production |
