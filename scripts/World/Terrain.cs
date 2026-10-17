namespace ClownSquad.World;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;

/// <summary>
/// The lie of the land on a scrolling stage, different for every stage:
/// raised decks and ramps (laid out in one of several styles), pits to fall
/// into, solid obstacles to fight round, and the stage's features and traps
/// (trampolines, a cannon, a giant mallet, fire jets, slippery patches).
///
/// The ground is no longer just y = 0. A fighter stands on whatever height is
/// under them; a ramp rises gently enough to walk; a deck's edge is a ledge:
/// walking into it stops you (jump up, or take the ramp), and enemies (and
/// test bots) hop up after you. Anyone found below a deck's top (just spawned
/// there, say) is lifted onto it. Pits are open drops: a fall costs a fifth of
/// your health and puts you back at the edge. Enemies will not walk into one,
/// but they can be knocked in. Obstacles push fighters round them.
///
/// Built the same on every machine from the stage alone, so an online guest
/// sees the same ground (the features' moving parts are the host's, mirrored).
/// </summary>
public sealed class Terrain
{
    public static Terrain? Current { get; set; }

    /// <summary>How high a step can be walked up without a jump.</summary>
    public const float StepUp = 0.35f;
    /// <summary>The "height" of a pit: a long way down.</summary>
    public const float PitDepth = -20f;

    public enum Style { Flat, BackDecks, Islands, FrontRidge, Tiers }

    public enum FeatureKind { Trampoline, Cannon, Mallet, FireJet, Slick, Spill, PoisonBarrel, ExplosiveBarrel }

    public sealed record Block(float X0, float X1, float Z0, float Z1, float Height, int Ramp, float Base = 0f)
    {
        /// <summary>Height at a point inside: flat, or rising along x (+1 rises with x, -1 falls) from Base to Height.</summary>
        public float At(float x)
        {
            if (Ramp == 0) return Height;
            float t = (x - X0) / (X1 - X0);
            return Base + (Height - Base) * Mathf.Clamp(Ramp > 0 ? t : 1f - t, 0f, 1f);
        }

        public bool Contains(float x, float z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
    }

    /// <summary>A solid round obstacle: a barrel, a rock, a cactus. Fighters below its top are pushed round it.</summary>
    public sealed record Obstacle(float X, float Z, float Radius, float Height, string Kind);

    /// <summary>An open drop in the walkway.</summary>
    public sealed record Pit(float X0, float X1, float Z0, float Z1)
    {
        public bool Contains(float x, float z, float margin = 0f) =>
            x >= X0 - margin && x <= X1 + margin && z >= Z0 - margin && z <= Z1 + margin;
    }

    /// <summary>A stage feature or trap, placed on the walkway; Phase staggers the timed ones.</summary>
    public sealed record Feature(FeatureKind Kind, float X, float Z, float Phase);

    public Style Layout { get; private set; }
    public readonly List<Block> Blocks = new();
    public readonly List<Obstacle> Obstacles = new();
    public readonly List<Pit> Pits = new();
    public readonly List<Feature> Features = new();

    public bool InPit(float x, float z, float margin = 0f)
    {
        foreach (Pit p in Pits) if (p.Contains(x, z, margin)) return true;
        return false;
    }

    public float HeightAt(float x, float z)
    {
        if (InPit(x, z)) return PitDepth;
        float h = 0f;
        foreach (Block b in Blocks) if (b.Contains(x, z)) h = Mathf.Max(h, b.At(x));
        return h;
    }

    public float HeightAt(Vector3 p) => HeightAt(p.X, p.Z);

    /// <summary>
    /// Put a fighter right after it moved from <paramref name="before"/>: round
    /// obstacles, stopped by ledges it cannot step up (sliding along them where
    /// it can), lifted onto a top it was found under, and (if it is careful:
    /// an enemy or a bot on its feet) kept from walking into a pit. Returns the
    /// ground height where it ends up, and whether a ledge blocked it.
    /// </summary>
    public (float ground, bool ledge) Settle(Fighter f, Vector3 before, bool careful)
    {
        Vector3 p = f.GlobalPosition;
        foreach (Obstacle o in Obstacles)
        {
            if (p.Y > o.Height - 0.1f) continue;
            Vector2 d = new(p.X - o.X, p.Z - o.Z);
            float r = o.Radius + f.BodyHalfWidth * 0.45f;
            if (d.LengthSquared() >= r * r) continue;
            Vector2 n = d.LengthSquared() < 0.0001f ? new Vector2(-1f, 0f) : d.Normalized();
            p = new Vector3(o.X + n.X * r, p.Y, o.Z + n.Y * r);
        }

        // the careful stop at a pit's edge, the way they would at a ledge
        if (careful && InPit(p.X, p.Z, 0.15f) && !InPit(before.X, before.Z, 0.15f))
        {
            if (!InPit(p.X, before.Z, 0.15f)) p = new Vector3(p.X, p.Y, before.Z);
            else if (!InPit(before.X, p.Z, 0.15f)) p = new Vector3(before.X, p.Y, p.Z);
            else p = new Vector3(before.X, p.Y, before.Z);
        }

        bool ledge = false;
        float here = HeightAt(p);
        if (here > p.Y + StepUp)
        {
            float was = HeightAt(before);
            if (was >= here - 0.05f) p.Y = here;        // already on (or in) it: stand on top
            else
            {
                ledge = true;
                if (HeightAt(p.X, before.Z) <= p.Y + StepUp) p = new Vector3(p.X, p.Y, before.Z);
                else if (HeightAt(before.X, p.Z) <= p.Y + StepUp) p = new Vector3(before.X, p.Y, p.Z);
                else p = new Vector3(before.X, p.Y, before.Z);
                here = HeightAt(p);
            }
        }
        f.GlobalPosition = p;
        return (here, ledge);
    }

    /// <summary>A spot clear of obstacles and pits (for something placed or spawned there).</summary>
    public Vector3 Clear(Vector3 at, float radius)
    {
        foreach (Obstacle o in Obstacles)
        {
            Vector2 d = new(at.X - o.X, at.Z - o.Z);
            float r = o.Radius + radius;
            if (d.LengthSquared() < r * r)
            {
                Vector2 n = d.LengthSquared() < 0.0001f ? new Vector2(1f, 0f) : d.Normalized();
                at = new Vector3(o.X + n.X * r, at.Y, o.Z + n.Y * r);
            }
        }
        foreach (Pit p in Pits)
            if (p.Contains(at.X, at.Z, radius))
            {
                // out the nearer side, along the depth of the walkway
                float toFar = at.Z - (p.Z0 - radius), toNear = (p.Z1 + radius) - at.Z;
                at = new Vector3(at.X, at.Y, toFar < toNear ? p.Z0 - radius - 0.05f : p.Z1 + radius + 0.05f);
            }
        return at;
    }

    // ------------------------------------------------------------ layout

    // Per world: the features and traps it draws on. The pool widens as the
    // worlds go on, and (in Lay) the hard worlds use more kinds at once, more
    // of each, and more pits; the traps themselves hit harder and come round
    // faster (StageFeature.Severity, Pace). World 1 is gentle, world 10 a gauntlet.
    private static readonly FeatureKind[][] FeaturePools =
    {
        new[] { FeatureKind.Trampoline, FeatureKind.ExplosiveBarrel, FeatureKind.Slick },
        new[] { FeatureKind.Slick, FeatureKind.Trampoline, FeatureKind.ExplosiveBarrel },
        new[] { FeatureKind.Trampoline, FeatureKind.Spill, FeatureKind.PoisonBarrel },
        new[] { FeatureKind.Cannon, FeatureKind.ExplosiveBarrel, FeatureKind.Mallet },
        new[] { FeatureKind.Mallet, FeatureKind.Slick, FeatureKind.Spill, FeatureKind.ExplosiveBarrel },
        new[] { FeatureKind.Slick, FeatureKind.Cannon, FeatureKind.ExplosiveBarrel, FeatureKind.PoisonBarrel },
        new[] { FeatureKind.FireJet, FeatureKind.ExplosiveBarrel, FeatureKind.Cannon, FeatureKind.Spill },
        new[] { FeatureKind.FireJet, FeatureKind.PoisonBarrel, FeatureKind.Spill, FeatureKind.Slick, FeatureKind.Mallet },
        new[] { FeatureKind.Cannon, FeatureKind.Mallet, FeatureKind.FireJet, FeatureKind.ExplosiveBarrel, FeatureKind.PoisonBarrel },
        new[] { FeatureKind.Trampoline, FeatureKind.Cannon, FeatureKind.Mallet, FeatureKind.FireJet, FeatureKind.Spill, FeatureKind.PoisonBarrel, FeatureKind.ExplosiveBarrel },
    };

    /// <summary>
    /// The ground for a scrolling stage, from the stage alone (so every machine
    /// builds the same). The seed picks a layout style; the style places the
    /// decks; then pits, obstacles and features fill in round them.
    /// </summary>
    public static Terrain Lay(string stageId, int world, float[] encounters, float exitX, float bandFar, float bandNear)
    {
        var t = new Terrain();
        var rng = new RandomNumberGenerator { Seed = (ulong)GD.Hash(stageId) ^ 0x7e7a17UL };
        int w = Mathf.Clamp(world, 1, 10) - 1;
        float band = bandNear - bandFar;
        t.Layout = (Style)rng.RandiRange(0, 4);

        // ---- decks, by style
        for (int i = 0; i < encounters.Length; i++)
        {
            float e = encounters[i];
            switch (t.Layout)
            {
                case Style.BackDecks when rng.Randf() < 0.6f:
                {
                    float depth = Mathf.Min(1.9f, band * 0.32f);
                    float h = rng.RandfRange(0.75f, 1.05f);
                    float x0 = e - rng.RandfRange(3f, 6f), x1 = e + rng.RandfRange(2f, 5f);
                    t.Deck(x0, x1, bandFar - 0.6f, bandFar + depth, h, rng.RandiRange(0, 2));
                    break;
                }
                case Style.Tiers when rng.Randf() < 0.7f:
                {
                    // a low deck, and a higher one behind it, reached from the low one
                    float depth = Mathf.Min(2.4f, band * 0.4f);
                    float x0 = e - rng.RandfRange(4f, 6f), x1 = e + rng.RandfRange(3f, 5f);
                    float zMid = bandFar + depth * 0.5f;
                    t.Deck(x0, x1, bandFar - 0.6f, bandFar + depth, 0.7f, rng.RandiRange(0, 2));
                    float u0 = x0 + 1.2f, u1 = x1 - 1.5f;
                    if (u1 - u0 > 2.5f)
                    {
                        t.Blocks.Add(new Block(u0 + 2.2f, u1, bandFar - 0.6f, zMid, 1.45f, 0));
                        t.Blocks.Add(new Block(u0, u0 + 2.2f, bandFar - 0.6f, zMid, 1.45f, +1, 0.7f));
                    }
                    break;
                }
                case Style.Islands when rng.Randf() < 0.75f:
                {
                    // a raised island out in the middle of the walkway, a ramp on one side
                    float cx = e + rng.RandfRange(-3f, 3f), len = rng.RandfRange(3f, 4.5f);
                    float cz = bandFar + band * rng.RandfRange(0.35f, 0.55f), dz = rng.RandfRange(1.3f, 1.8f);
                    t.Deck(cx - len * 0.5f, cx + len * 0.5f, cz - dz * 0.5f, cz + dz * 0.5f, rng.RandfRange(0.55f, 0.8f), rng.RandiRange(0, 2));
                    break;
                }
                case Style.FrontRidge when rng.Randf() < 0.6f:
                {
                    // a low ridge along the front, nearest the camera
                    float x0 = e - rng.RandfRange(3f, 6f), x1 = e + rng.RandfRange(3f, 6f);
                    t.Deck(x0, x1, bandNear - 1.3f, bandNear + 0.6f, 0.5f, rng.RandiRange(0, 2));
                    break;
                }
            }
        }

        bool Free(float x, float z, float r)
        {
            if (t.HeightAt(x, z) != 0f) return false;
            foreach (Block b in t.Blocks) if (x > b.X0 - r && x < b.X1 + r && z > b.Z0 - r && z < b.Z1 + r) return false;
            foreach (Obstacle o in t.Obstacles) if (new Vector2(o.X - x, o.Z - z).Length() < o.Radius + r) return false;
            foreach (Pit p in t.Pits) if (p.Contains(x, z, r)) return false;
            foreach (Feature f in t.Features) if (new Vector2(f.X - x, f.Z - z).Length() < 1.4f + r) return false;
            return true;
        }

        // ---- pits: on some stages, never wider than leaves a way round
        if (rng.Randf() < 0.15f + 0.07f * w)
        {
            int n = rng.RandiRange(1, Mathf.Min(1 + w / 3, encounters.Length + 1));
            for (int k = 0, tries = 0; k < n && tries < 30; tries++)
            {
                float x = rng.RandfRange(6f, exitX - 4f);
                float len = rng.RandfRange(2.0f, 3.2f), dz = Mathf.Min(rng.RandfRange(1.2f, 2.0f), band * 0.38f);
                float z = rng.RandfRange(bandFar + 0.9f, bandNear - dz - 0.9f);
                bool ok = true;
                for (float sx = x - 0.4f; sx <= x + len + 0.4f && ok; sx += 0.5f)
                    for (float sz = z - 0.4f; sz <= z + dz + 0.4f && ok; sz += 0.5f)
                        ok = Free(sx, sz, 0.3f);
                if (!ok) continue;
                t.Pits.Add(new Pit(x, x + len, z, z + dz));
                k++;
            }
        }

        // ---- features and traps: more kinds from the world's pool, and more of them, the later the world
        var pool = new List<FeatureKind>(FeaturePools[w]);
        var kinds = new List<FeatureKind>();
        for (int k = 1 + w / 3; k > 0 && pool.Count > 0; k--)
        {
            int at = rng.RandiRange(0, pool.Count - 1);
            kinds.Add(pool[at]);
            pool.RemoveAt(at);
        }
        int count = Mathf.Max(1, Mathf.RoundToInt(encounters.Length * (0.6f + 0.16f * w) + rng.RandfRange(-0.5f, 0.5f)));
        for (int k = 0, tries = 0; k < count && tries < 40; tries++)
        {
            float e = encounters[rng.RandiRange(0, encounters.Length - 1)];
            float x = e + rng.RandfRange(-5f, 5f);
            float z = rng.RandfRange(bandFar + 1.0f, bandNear - 1.0f);
            if (!Free(x, z, 1.1f)) continue;
            FeatureKind kind = kinds[rng.RandiRange(0, kinds.Count - 1)];
            t.Features.Add(new Feature(kind, x, z, rng.Randf()));
            if (kind == FeatureKind.Cannon) t.Obstacles.Add(new Obstacle(x, z, 0.6f, 1.2f, "cannon"));   // the carriage is solid
            k++;
        }

        // ---- obstacles on the walkway: more on a flat stage, which has nothing else to climb
        int per = t.Layout == Style.Flat ? 3 : 2;
        for (int i = 0; i < encounters.Length; i++)
        {
            float from = i == 0 ? 3f : encounters[i - 1], e = encounters[i];
            int n = rng.RandiRange(per - 1, per + 1);
            for (int k = 0, tries = 0; k < n && tries < 20; tries++)
            {
                float x = rng.RandfRange(from + 2.5f, e + 4f);
                float z = rng.RandfRange(bandFar + 0.8f, bandNear - 1.0f);
                if (!Free(x, z, 1.0f)) continue;
                t.Obstacles.Add(new Obstacle(x, z, 0.5f, 1.4f, rng.Randf() < 0.5f ? "a" : "b"));
                k++;
            }
        }
        return t;
    }

    /// <summary>A flat deck with its ramps: ramps 0 = left end, 1 = right end, 2 = both.</summary>
    private void Deck(float x0, float x1, float z0, float z1, float h, int ramps)
    {
        Blocks.Add(new Block(x0, x1, z0, z1, h, 0));
        if (ramps != 1) Blocks.Add(new Block(x0 - 2.6f, x0, z0, z1, h, +1));
        if (ramps != 0) Blocks.Add(new Block(x1, x1 + 2.6f, z0, z1, h, -1));
    }
}
