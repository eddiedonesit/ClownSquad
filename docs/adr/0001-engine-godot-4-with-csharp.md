# 1. Use Godot 4 with C# as the engine

- **Status:** Accepted
- **Date:** 2026-10-13

## Context

Clown Squad is a 2D-sprite game set in a 2.5D (HD-2D) environment, targeting a
Steam release on Windows, with Linux/Steam Deck as a likely secondary target.
The developer's existing codebase is C#/.NET tooling, and .NET 10 SDK is already
installed locally.

## Decision

Build in **Godot 4.7 (.NET/"Mono" build) using C#**.

## Alternatives considered

| Option | Why rejected |
| --- | --- |
| **Unity 6 (C#)** | Seamless 2D/3D mixing and the most mature Steamworks tooling, but scenes and prefabs are machine-generated YAML that code review cannot meaningfully read, and the license has repeatedly changed (Personal free only under $200K revenue/funding; Pro $2,310/seat/yr as of Jan 2026). |
| **MonoGame (C#)** | Pure code and fully reviewable, but provides no 3D pipeline — model loading, lighting and shaders would all be hand-built. Too much infrastructure before any gameplay exists. |
| **TypeScript + Phaser** | 2D only; 3D would require bolting on Three.js, and Steam distribution would need an Electron wrapper with weak Steam integration and worse performance. |

## Consequences

**Positive**
- MIT licensed, free permanently, with no revenue threshold to ever cross.
- `.tscn` / `.tres` scene files are plain text, so the whole project stays diffable and reviewable.
- First-class dedicated 2D renderer alongside a full 3D renderer.
- Proven for this exact aesthetic — Cassette Beasts shipped 2D sprites in 3D environments on Steam in Godot.

**Negative — accepted risks**
- **Steam integration requires manual wiring.** GodotSteam is the mature wrapper but is GDScript-first and its maintainers direct C# users elsewhere. The chosen route is Steamworks.NET used directly as a .NET library, following the Chickensoft GameTemplate. Budget roughly a day.
- **Console ports would need third-party porting houses**, as Godot has no first-party console support.
- Godot's C# tooling has a smaller ecosystem and fewer tutorials than Unity's.

## References

- Chickensoft GameTemplate (working Godot + C# + Steamworks.NET setup): https://github.com/chickensoft-games/GameTemplate
- Steamworks.NET: https://github.com/rlabrecque/Steamworks.NET
- GodotSteam C# guidance: https://godotsteam.com/tutorials/c-sharp
