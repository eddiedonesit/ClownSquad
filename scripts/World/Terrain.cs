namespace ClownSquad.World;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;

/// <summary>
/// The lie of the land on a scrolling stage: raised terraces along the back
/// of the walkway, ramps up to them, and solid obstacles to fight round.
///
/// The ground is no longer just y = 0. A fighter stands on whatever height is
/// under them; a ramp rises gently enough to walk; a terrace's edge is a
/// ledge: walking into it stops you (jump up, or take the ramp), and enemies
/// (and test bots) hop up after you. Anyone found below a terrace's top (just
/// spawned there, say) is lifted onto it. Obstacles push fighters round them.
/// Built the same on every machine from the stage alone, so an online guest
/// sees the same ground.
/// </summary>
public sealed class Terrain
{
    public static Terrain? Current { get; set; }

    /// <summary>How high a step can be walked up without a jump.</summary>
    public const float StepUp = 0.35f;

    public sealed record Block(float X0, float X1, float Z0, float Z1, float Height, int Ramp)
    {
        /// <summary>Height at a point inside: flat, or rising along x toward the end the ramp climbs to (+1 rises with x, -1 falls).</summary>
        public float At(float x)
        {
            if (Ramp == 0) return Height;
            float t = (x - X0) / (X1 - X0);
            return Height * Mathf.Clamp(Ramp > 0 ? t : 1f - t, 0f, 1f);
        }

        public bool Contains(float x, float z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
    }

    /// <summary>A solid round obstacle: a barrel, a rock, a cactus. Fighters below its top are pushed round it.</summary>
    public sealed record Obstacle(float X, float Z, float Radius, float Height, string Kind);

    public readonly List<Block> Blocks = new();
    public readonly List<Obstacle> Obstacles = new();

    public float HeightAt(float x, float z)
    {
        float h = 0f;
        foreach (Block b in Blocks) if (b.Contains(x, z)) h = Mathf.Max(h, b.At(x));
        return h;
    }

    public float HeightAt(Vector3 p) => HeightAt(p.X, p.Z);

    /// <summary>
    /// Put a fighter right after it moved from <paramref name="before"/>: round
    /// obstacles, stopped by ledges it cannot step up (sliding along them where
    /// it can), or lifted onto a top it was found under. Returns the ground
    /// height where it ends up, and whether a ledge blocked it.
    /// </summary>
    public (float ground, bool ledge) Settle(Fighter f, Vector3 before)
    {
        Vector3 p = f.GlobalPosition;
        // obstacles: pushed out to their rim
        foreach (Obstacle o in Obstacles)
        {
            if (p.Y > o.Height - 0.1f) continue;
            Vector2 d = new(p.X - o.X, p.Z - o.Z);
            float r = o.Radius + f.BodyHalfWidth * 0.45f;
            if (d.LengthSquared() >= r * r) continue;
            Vector2 n = d.LengthSquared() < 0.0001f ? new Vector2(-1f, 0f) : d.Normalized();
            p = new Vector3(o.X + n.X * r, p.Y, o.Z + n.Y * r);
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
                // slide along the ledge where one direction is clear
                if (HeightAt(p.X, before.Z) <= p.Y + StepUp) p = new Vector3(p.X, p.Y, before.Z);
                else if (HeightAt(before.X, p.Z) <= p.Y + StepUp) p = new Vector3(before.X, p.Y, p.Z);
                else p = new Vector3(before.X, p.Y, before.Z);
                here = HeightAt(p);
            }
        }
        f.GlobalPosition = p;
        return (here, ledge);
    }

    /// <summary>A spot on the walkway clear of obstacles (for something placed there).</summary>
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
        return at;
    }

    // ------------------------------------------------------------ layout

    /// <summary>
    /// The ground for a scrolling stage, from the stage alone (so every machine
    /// builds the same): at most encounters, a terrace along the back with a
    /// ramp up at one end; between them, a couple of the world's obstacles.
    /// </summary>
    public static Terrain Lay(string stageId, float[] encounters, float exitX, float bandFar, float bandNear)
    {
        var t = new Terrain();
        var rng = new RandomNumberGenerator { Seed = (ulong)GD.Hash(stageId) ^ 0x7e7a17UL };
        float depth = Mathf.Min(1.9f, (bandNear - bandFar) * 0.32f);
        float z0 = bandFar - 0.6f, z1 = bandFar + depth;
        for (int i = 0; i < encounters.Length; i++)
        {
            float e = encounters[i];
            if (i == 0 || rng.Randf() < 0.65f)
            {
                // a terrace across the fight, its ramp at one end
                float h = rng.RandfRange(0.75f, 1.05f);
                float x0 = e - rng.RandfRange(4f, 6f), x1 = e + rng.RandfRange(3f, 5f);
                bool rampLeft = rng.Randf() < 0.5f;
                t.Blocks.Add(new Block(x0, x1, z0, z1, h, 0));
                if (rampLeft) t.Blocks.Add(new Block(x0 - 2.6f, x0, z0, z1, h, +1));
                else t.Blocks.Add(new Block(x1, x1 + 2.6f, z0, z1, h, -1));
            }
            // obstacles out on the walkway, on this stretch of road
            float from = i == 0 ? 3f : encounters[i - 1];
            int n = rng.RandiRange(1, 3);
            for (int k = 0; k < n; k++)
            {
                float x = rng.RandfRange(from + 2.5f, e + 4f);
                float z = rng.RandfRange(z1 + 0.9f, bandNear - 1.0f);
                if (z <= z1 + 0.6f) continue;
                bool crowded = false;
                foreach (Obstacle o in t.Obstacles) if (Mathf.Abs(o.X - x) < 2.2f && Mathf.Abs(o.Z - z) < 1.6f) crowded = true;
                if (crowded) continue;
                t.Obstacles.Add(new Obstacle(x, z, 0.5f, 1.4f, rng.Randf() < 0.5f ? "a" : "b"));
            }
        }
        return t;
    }
}
