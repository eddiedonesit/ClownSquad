# 5. Combat model: borrowed from the brawlers that worked

- **Status:** Accepted
- **Date:** 2026-10-14

## Context

The combat prototype had swings, hitstop and a dodge, but hits did not
interrupt anything, nothing could be launched, enemies could all attack at
once, and fighters could walk off screen. The brief was to adopt what made
successful beat-em-ups work rather than invent a model.

## Decision

Adopt the genre's proven mechanisms, each for a stated reason:

| Mechanism | Seen in | Why it is here |
| --- | --- | --- |
| **Hitstun** on every hit | all of them | A hit must take control away, or a combo is just two attacks near each other |
| **Launch, juggle, decaying lift** | Streets of Rage 4, Guardian Heroes | Turns enemy stun into something the player steers. Each juggle hit lifts less, so juggles end naturally rather than by a hit cap |
| **Wall bounce off the screen edge** | Streets of Rage 4 | The edge extends a combo instead of ending it. Once per launch |
| **Knockdown with get-up invulnerability** | Final Fight onward | Without it a floored fighter can be kept floored forever |
| **Poise (super armour) on boss specials** | Streets of Rage, Dragon's Crown | Makes a special something to dodge, not something to interrupt |
| **Attack tokens** | Arkham, God Hand, modern brawlers | Caps simultaneous attackers so a crowd leaves readable openings. The cap is a difficulty lever needing no stat changes |
| **Depth lanes with lane assist** | Streets of Rage 4, Shredder's Revenge | Lining up on a tilted camera is the least fun part of the genre; a swing slides the attacker a short way onto the target's lane |
| **Dodge-cancel out of recovery** | Streets of Rage 4, River City Girls | An attack with no way out stops being used once enemies crowd in |
| **Screen edges are walls** | the whole genre | Nothing that fights may leave the camera view |

The arena is derived from the camera frustum, not typed in: it stays correct if
the camera moves, zooms, or the window changes shape, and it keeps a fighter's
whole body on screen, so taller fighters get a shallower far limit.

Fighters do not body-block each other. Blocking lets a crowd pin the player and
let launched bodies come to rest on top of others; spacing belongs to the AI.

## Consequences

**Positive**
- Combos, juggles and wall bounces fall out of four small rules in `Fighter`
  rather than per-move scripting.
- Every enemy and every player class gets the model for free.

**Negative, accepted**
- More state on every fighter: stun, launched, down, invulnerable, juggle count.
- Juggle balance now exists as a problem. The decay constant (17% per hit, floor
  40%) and the single wall bounce are first guesses.
- A floored body has no art of its own yet; it is the hit pose pressed flat.
- Charge time for the mallet throw was specified as three seconds and cut to
  one after play-testing; it is defined in real seconds and converted to game time.

## Sources

- Streets of Rage 4 reviews on juggles and wall bounces: https://mp1st.com/news/streets-of-rage-4-review-beat-em-up-evolution
- Developer interview, "we start on the punch": https://blog.playstation.com/2020/04/30/streets-of-rage-4-how-three-studios-revived-a-legendary-series/
- Attack-token devlogs: https://serfofcinder.itch.io/archetype-portal-to-hell/devlog/738495/enemy-ai-tech-talk and https://lghts.itch.io/iron-reclamation/devlog/1092177/making-combat-feel-good-fair-readable-and-responsive-ai
- Final Fight analysis on crowd pressure: https://eriktwice.com/en/2019/01/25/final-fight-%E2%98%85%E2%98%85%E2%98%85/
