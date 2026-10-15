namespace ClownSquad.World;

using System.Collections.Generic;
using Godot;

/// <summary>
/// A kit of procedural 3D scenery, built from primitive meshes in a chunky,
/// toy-like style that sits well behind the pixel-art characters: striped
/// tents and big tops, Ferris wheels, lamps that really light, trees, carts,
/// booths, crystals, cacti, castle towers, mountains, dunes and more. Used by
/// the scrolling stages (StageScenery) and the world map.
///
/// Every model stands on its origin, faces +Z (the camera), and is roughly
/// life-size against a clown about 1.6 tall. Meshes and paints are shared.
/// </summary>
public static class Kit3D
{
    /// <summary>What a model needs from its builder: randomness, and lists to report lights and moving parts into.</summary>
    public sealed class Ctx
    {
        public RandomNumberGenerator Rng = new();
        public readonly List<(OmniLight3D light, float energy, float seed)> Lights = new();
        public readonly List<(Node3D node, Vector3 axis, float speed)> Spinners = new();
        public Color Lamp = new(1f, 0.82f, 0.5f);
        public bool LightsOn = true;
    }

    // ------------------------------------------------------------ palette

    public static readonly Color Red = new(0.86f, 0.18f, 0.24f);
    public static readonly Color Cream = new(0.98f, 0.93f, 0.84f);
    public static readonly Color Gold = new(1f, 0.78f, 0.28f);
    public static readonly Color Wood = new(0.55f, 0.35f, 0.21f);
    public static readonly Color DarkWood = new(0.32f, 0.2f, 0.14f);
    public static readonly Color Iron = new(0.22f, 0.2f, 0.27f);
    public static readonly Color Stone = new(0.55f, 0.52f, 0.58f);
    public static readonly Color Leaf = new(0.3f, 0.62f, 0.3f);
    public static readonly Color Blue = new(0.25f, 0.45f, 0.9f);
    public static readonly Color Purple = new(0.55f, 0.3f, 0.75f);

    private static readonly Dictionary<string, Material> Paints = new();
    private static readonly Dictionary<string, Mesh> Meshes = new();

    public static StandardMaterial3D Paint(Color c, float rough = 0.8f, float metal = 0f, float glow = 0f)
    {
        string key = $"p{c.ToHtml()}{rough}{metal}{glow}";
        if (Paints.TryGetValue(key, out Material? m)) return (StandardMaterial3D)m;
        var p = new StandardMaterial3D { AlbedoColor = c, Roughness = rough, Metallic = metal };
        if (glow > 0f) { p.EmissionEnabled = true; p.Emission = c; p.EmissionEnergyMultiplier = glow; }
        Paints[key] = p;
        return p;
    }

    /// <summary>Stripes of two colours: round a cylinder (u) by default, or along it.</summary>
    public static StandardMaterial3D Stripes(Color a, Color b, int count, bool along = false, float rough = 0.75f)
    {
        string key = $"s{a.ToHtml()}{b.ToHtml()}{count}{along}{rough}";
        if (Paints.TryGetValue(key, out Material? m)) return (StandardMaterial3D)m;
        int n = count * 2;
        Image img = along ? Image.CreateEmpty(1, n, false, Image.Format.Rgba8) : Image.CreateEmpty(n, 1, false, Image.Format.Rgba8);
        for (int i = 0; i < n; i++)
        {
            if (along) img.SetPixel(0, i, i % 2 == 0 ? a : b);
            else img.SetPixel(i, 0, i % 2 == 0 ? a : b);
        }
        var p = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Roughness = rough,
        };
        Paints[key] = p;
        return p;
    }

    /// <summary>Glass-like see-through paint, for beams and windows.</summary>
    public static StandardMaterial3D Glow(Color c, float alpha, float energy = 1.5f)
    {
        string key = $"g{c.ToHtml()}{alpha}{energy}";
        if (Paints.TryGetValue(key, out Material? m)) return (StandardMaterial3D)m;
        var p = new StandardMaterial3D
        {
            AlbedoColor = new Color(c, alpha), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
        };
        Paints[key] = p;
        return p;
    }

    // ------------------------------------------------------------- meshes

    private static T Shared<T>(string key, System.Func<T> make) where T : Mesh
    {
        if (!Meshes.TryGetValue(key, out Mesh? m)) Meshes[key] = m = make();
        return (T)m;
    }

    public static CylinderMesh Cyl(float top, float bottom, float h, int seg = 14) =>
        Shared($"c{top}/{bottom}/{h}/{seg}", () => new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = h, RadialSegments = seg, Rings = 1 });

    public static SphereMesh Ball(float r, float h = -1f, int seg = 14, bool half = false) =>
        Shared($"b{r}/{h}/{seg}/{half}", () => new SphereMesh { Radius = r, Height = h < 0 ? (half ? r : 2f * r) : h, RadialSegments = seg, Rings = Mathf.Max(3, seg / 2), IsHemisphere = half });

    public static BoxMesh Box(float x, float y, float z) => Shared($"x{x}/{y}/{z}", () => new BoxMesh { Size = new Vector3(x, y, z) });

    public static PrismMesh Prism(float x, float y, float z) => Shared($"r{x}/{y}/{z}", () => new PrismMesh { Size = new Vector3(x, y, z) });

    public static TorusMesh Ring(float inner, float outer, int seg = 24) =>
        Shared($"t{inner}/{outer}/{seg}", () => new TorusMesh { InnerRadius = inner, OuterRadius = outer, Rings = seg, RingSegments = 8 });

    public static MeshInstance3D Put(Node3D parent, Mesh mesh, Material mat, Vector3 at, Vector3 rotDeg = default, Vector3? scale = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = at, RotationDegrees = rotDeg };
        if (scale is Vector3 s) mi.Scale = s;
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>A rod from a to b (a post, a rope, a spoke).</summary>
    public static MeshInstance3D Rod(Node3D parent, Vector3 a, Vector3 b, float r, Material mat, int seg = 8)
    {
        Vector3 d = b - a;
        var mi = new MeshInstance3D { Mesh = Cyl(r, r, 1f, seg), MaterialOverride = mat };
        parent.AddChild(mi);
        mi.Position = (a + b) * 0.5f;
        Vector3 up = d.Normalized();
        Vector3 side = Mathf.Abs(up.Dot(Vector3.Forward)) > 0.95f ? Vector3.Right : Vector3.Forward;
        Vector3 x = up.Cross(side).Normalized();
        Vector3 z = x.Cross(up).Normalized();
        mi.Basis = new Basis(x, up * d.Length(), z);
        return mi;
    }

    /// <summary>Many copies of one mesh in a single draw: grass, pickets, bulbs.</summary>
    public static MultiMeshInstance3D Many(Node3D parent, Mesh mesh, Material mat, IList<Transform3D> at, IList<Color>? colors = null)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = colors is not null,
            Mesh = mesh,
            InstanceCount = at.Count,
        };
        for (int i = 0; i < at.Count; i++)
        {
            mm.SetInstanceTransform(i, at[i]);
            if (colors is not null) mm.SetInstanceColor(i, colors[i]);
        }
        Material m = mat;
        if (colors is not null && mat is StandardMaterial3D sm)
        {
            var tinted = (StandardMaterial3D)sm.Duplicate();
            tinted.VertexColorUseAsAlbedo = true;
            tinted.VertexColorIsSrgb = true;              // the colours are authored in sRGB, like every other paint
            m = tinted;
        }
        var inst = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = m };
        parent.AddChild(inst);
        return inst;
    }

    private static void Light(Node3D parent, Ctx ctx, Vector3 at, float energy = 2.2f, float range = 6f, Color? color = null)
    {
        if (!ctx.LightsOn) return;
        var l = new OmniLight3D
        {
            LightColor = color ?? ctx.Lamp, LightEnergy = energy, OmniRange = range, OmniAttenuation = 1.1f,
            Position = at, LightVolumetricFogEnergy = 0f, ShadowEnabled = false,
        };
        parent.AddChild(l);
        ctx.Lights.Add((l, energy, ctx.Rng.Randf() * 10f));
    }

    /// <summary>The height of a built model: the top of its tallest mesh, in its own space.</summary>
    public static float Height(Node3D model)
    {
        float top = 0f;
        void Walk(Node node, Transform3D xf)
        {
            foreach (Node c in node.GetChildren())
            {
                if (c is not Node3D n3) continue;
                Transform3D t = xf * n3.Transform;
                if (n3 is MeshInstance3D mi && mi.Mesh is not null)
                {
                    Aabb box = t * mi.Mesh.GetAabb();
                    top = Mathf.Max(top, box.End.Y);
                }
                Walk(n3, t);
            }
        }
        Walk(model, Transform3D.Identity);
        return top;
    }

    // -------------------------------------------------------------- models

    /// <summary>Build a model by name. Unknown names give a crate.</summary>
    public static Node3D Make(string kind, Ctx ctx, Color a, Color b)
    {
        var n = new Node3D { Name = kind };
        RandomNumberGenerator rng = ctx.Rng;
        switch (kind)
        {
            case "tent": Tent(n, a, b, 1f); break;
            case "bigtop": BigTop(n, a, b, ctx); break;
            case "ferris": Ferris(n, ctx, a, b); break;
            case "lamp": Lamp(n, ctx); break;
            case "popcorn": Cart(n, Red, Cream, Cream, ctx); break;
            case "candycart": Cart(n, new Color(0.95f, 0.55f, 0.75f), Cream, new Color(1f, 0.7f, 0.85f), ctx); break;
            case "balloons": Balloons(n, rng); break;
            case "umbrella": Umbrella(n, a, b); break;
            case "candytree": CandyTree(n, a, b); break;
            case "mushroom": Mushroom(n, a, rng); break;
            case "tree": Tree(n, b.G > b.R ? b : Leaf, rng); break;
            case "lantern": Lantern(n, ctx); break;
            case "pins": Pins(n); break;
            case "booth": Booth(n, a, b, ctx); break;
            case "barrel": Barrel(n, rng); break;
            case "mirror": Mirror(n, a); break;
            case "crystal": Crystals(n, a, rng); break;
            case "pine": Pine(n, rng, snow: b.V > 0.9f); break;
            case "rock": Rocks(n, Stone.Lerp(a, 0.12f), rng); break;
            case "flagpole": Flagpole(n, a, b); break;
            case "cactus": Cactus(n, rng); break;
            case "popmachine": PopMachine(n, ctx); break;
            case "pipes": Pipes(n, ctx); break;
            case "banner": Banner(n, a); break;
            case "torch": Torch(n, ctx); break;
            case "statue": Statue(n); break;
            case "spotlight": Spotlight(n, ctx, rng); break;
            case "trophy": Trophy(n); break;
            case "starprop": StarProp(n, ctx); break;
            case "bunting": Bunting(n, rng); break;
            case "gate": Gate(n, a, b, ctx); break;
            // distance pieces
            case "mountain": Mountain(n, a, rng); break;
            case "hill": Hill(n, a, rng); break;
            case "dune": Dune(n, a, rng); break;
            case "mesa": Mesa(n, a, rng); break;
            case "tower": Tower(n, a, b, ctx); break;
            case "spire": Spire(n, a, rng); break;
            case "stalagmite": Stalagmites(n, a, rng); break;
            case "bigtree": BigTree(n, a, rng); break;
            case "lighthouse": Lighthouse(n, ctx); break;
            case "cloud": Cloud(n, rng); break;
            case "boat": Boat(n, a, b); break;
            default: Put(n, Box(1f, 1f, 1f), Paint(Wood), new Vector3(0f, 0.5f, 0f)); break;
        }
        return n;
    }

    private static void Tent(Node3D n, Color a, Color b, float s)
    {
        Put(n, Cyl(1.15f * s, 1.2f * s, 1.3f * s, 16), Stripes(a, b, 8), new Vector3(0f, 0.65f * s, 0f));
        Put(n, Cyl(0f, 1.45f * s, 1.3f * s, 16), Stripes(a, b, 8), new Vector3(0f, 1.95f * s, 0f));
        Put(n, Ring(1.15f * s, 1.45f * s, 16), Paint(Gold, 0.5f), new Vector3(0f, 1.3f * s, 0f), scale: new Vector3(1f, 0.25f, 1f));
        Put(n, Box(0.7f * s, 1.05f * s, 0.1f), Paint(new Color(0.15f, 0.08f, 0.14f)), new Vector3(0f, 0.53f * s, 1.12f * s));
        Put(n, Cyl(0.03f, 0.03f, 0.7f * s, 6), Paint(Iron), new Vector3(0f, 2.9f * s, 0f));
        Put(n, Prism(0.5f * s, 0.32f * s, 0.03f), Paint(Gold), new Vector3(0.22f * s, 3.1f * s, 0f), new Vector3(0f, 0f, -90f));
    }

    private static void BigTop(Node3D n, Color a, Color b, Ctx ctx)
    {
        Put(n, Cyl(6f, 6.2f, 4.5f, 24), Stripes(a, b, 16), new Vector3(0f, 2.25f, 0f));
        Put(n, Cyl(2.2f, 6.9f, 3.2f, 24), Stripes(a, b, 16), new Vector3(0f, 6.1f, 0f));
        Put(n, Cyl(0f, 2.4f, 2.6f, 24), Stripes(a, b, 16), new Vector3(0f, 9f, 0f));
        Put(n, Ring(6.2f, 6.9f, 24), Paint(Gold, 0.5f), new Vector3(0f, 4.5f, 0f), scale: new Vector3(1f, 0.3f, 1f));
        foreach (float x in new[] { -3.5f, 0f, 3.5f })
        {
            Put(n, Cyl(0.08f, 0.08f, 2f, 6), Paint(Iron), new Vector3(x, x == 0f ? 11f : 8.1f, x == 0f ? 0f : 1.5f));
            Put(n, Prism(1.1f, 0.6f, 0.05f), Paint(Gold), new Vector3(x + 0.5f, x == 0f ? 11.7f : 8.8f, x == 0f ? 0f : 1.5f), new Vector3(0f, 0f, -90f));
        }
        Put(n, Box(2.2f, 3f, 0.2f), Paint(new Color(0.18f, 0.06f, 0.12f)), new Vector3(0f, 1.5f, 6.05f));
        var bulbs = new List<Transform3D>();
        for (int i = 0; i < 40; i++)
        {
            float ang = Mathf.Tau * i / 40f;
            bulbs.Add(new Transform3D(Basis.Identity, new Vector3(Mathf.Sin(ang) * 6.95f, 4.5f, Mathf.Cos(ang) * 6.95f)));
        }
        Many(n, Ball(0.13f, -1f, 6), Paint(new Color(1f, 0.9f, 0.6f), 0.4f, 0f, 2.5f), bulbs);
    }

    private static void Ferris(Node3D n, Ctx ctx, Color a, Color b)
    {
        const float R = 5f, Hub = 6f;
        Material frame = Paint(new Color(0.95f, 0.95f, 1f), 0.5f, 0.3f);
        Rod(n, new Vector3(-3.2f, 0f, 0.8f), new Vector3(0f, Hub, 0.8f), 0.14f, frame);
        Rod(n, new Vector3(3.2f, 0f, 0.8f), new Vector3(0f, Hub, 0.8f), 0.14f, frame);
        Rod(n, new Vector3(-3.2f, 0f, -0.8f), new Vector3(0f, Hub, -0.8f), 0.14f, frame);
        Rod(n, new Vector3(3.2f, 0f, -0.8f), new Vector3(0f, Hub, -0.8f), 0.14f, frame);
        var wheel = new Node3D { Position = new Vector3(0f, Hub, 0f) };
        n.AddChild(wheel);
        Put(wheel, Ring(R - 0.12f, R + 0.12f, 40), frame, Vector3.Zero, new Vector3(90f, 0f, 0f));
        Put(wheel, Ring(R * 0.55f - 0.08f, R * 0.55f + 0.08f, 32), frame, Vector3.Zero, new Vector3(90f, 0f, 0f));
        var bulbs = new List<Transform3D>();
        Color[] cabin = { a, b, Gold, Blue, new Color(0.4f, 0.8f, 0.5f), Purple };
        for (int i = 0; i < 12; i++)
        {
            float ang = Mathf.Tau * i / 12f;
            var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
            Rod(wheel, Vector3.Zero, dir * R, 0.05f, frame, 6);
            Put(wheel, Box(0.7f, 0.6f, 0.7f), Paint(cabin[i % cabin.Length], 0.6f), dir * R + new Vector3(0f, -0.45f, 0f));
            for (int k = 1; k <= 3; k++)
                bulbs.Add(new Transform3D(Basis.Identity, dir * (R * k / 3.4f) + new Vector3(0f, 0f, 0.1f)));
        }
        Many(wheel, Ball(0.1f, -1f, 6), Paint(new Color(1f, 0.85f, 0.5f), 0.4f, 0f, 3f), bulbs);
        Put(wheel, Cyl(0.5f, 0.5f, 0.6f, 12), Paint(Gold, 0.4f, 0.6f), Vector3.Zero, new Vector3(90f, 0f, 0f));
        ctx.Spinners.Add((wheel, Vector3.Back, 0.12f));
    }

    private static void Lamp(Node3D n, Ctx ctx)
    {
        Material iron = Paint(Iron, 0.5f, 0.4f);
        Put(n, Cyl(0.18f, 0.24f, 0.25f, 10), iron, new Vector3(0f, 0.12f, 0f));
        Put(n, Cyl(0.05f, 0.07f, 2.6f, 8), iron, new Vector3(0f, 1.4f, 0f));
        Put(n, Ring(0.18f, 0.24f, 12), iron, new Vector3(0.22f, 2.6f, 0f), new Vector3(90f, 0f, 0f));
        Put(n, Ball(0.2f, -1f, 10), Paint(ctx.Lamp, 0.3f, 0f, 3.5f), new Vector3(0.38f, 2.48f, 0f));
        Put(n, Cyl(0.06f, 0.24f, 0.18f, 10), iron, new Vector3(0.38f, 2.72f, 0f));
        Light(n, ctx, new Vector3(0.4f, 2.3f, 0.5f), 2.4f, 6.5f);
    }

    private static void Cart(Node3D n, Color body, Color trim, Color treat, Ctx ctx)
    {
        Put(n, Box(1.4f, 0.8f, 0.9f), Stripes(body, trim, 4, rough: 0.6f), new Vector3(0f, 0.75f, 0f));
        foreach (float x in new[] { -0.5f, 0.5f })
            Put(n, Cyl(0.28f, 0.28f, 0.1f, 12), Paint(Iron, 0.5f), new Vector3(x, 0.3f, 0.48f), new Vector3(90f, 0f, 0f));
        Put(n, Box(1.2f, 0.7f, 0.75f), Glow(new Color(0.8f, 0.95f, 1f), 0.12f, 0.3f), new Vector3(0f, 1.5f, 0f));
        var pops = new List<Transform3D>();
        var rng = ctx.Rng;
        for (int i = 0; i < 26; i++)
            pops.Add(new Transform3D(Basis.Identity.Scaled(Vector3.One * rng.RandfRange(0.7f, 1.2f)),
                new Vector3(rng.RandfRange(-0.5f, 0.5f), 1.25f + rng.RandfRange(0f, 0.35f), rng.RandfRange(-0.3f, 0.3f))));
        Many(n, Ball(0.1f, -1f, 6), Paint(treat, 0.9f), pops);
        Put(n, Prism(1.6f, 0.4f, 1.05f), Stripes(body, trim, 5), new Vector3(0f, 2.05f, 0f));
        Put(n, Cyl(0.03f, 0.03f, 0.9f, 6), Paint(Gold, 0.4f, 0.6f), new Vector3(-0.85f, 1.1f, 0f), new Vector3(0f, 0f, 70f));
    }

    private static void Balloons(Node3D n, RandomNumberGenerator rng)
    {
        Color[] cols = { Red, Blue, Gold, new(0.45f, 0.8f, 0.4f), Purple, new(1f, 0.55f, 0.75f) };
        Put(n, Cyl(0.12f, 0.15f, 0.25f, 8), Paint(Wood), new Vector3(0f, 0.12f, 0f));
        for (int i = 0; i < 7; i++)
        {
            var top = new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(2.1f, 2.9f), rng.RandfRange(-0.3f, 0.3f));
            Rod(n, new Vector3(0f, 0.2f, 0f), top, 0.008f, Paint(Cream), 4);
            Put(n, Ball(0.3f, 0.75f, 12), Paint(cols[i % cols.Length], 0.15f), top + new Vector3(0f, 0.35f, 0f));
        }
    }

    private static void Umbrella(Node3D n, Color a, Color b)
    {
        Put(n, Cyl(0.03f, 0.03f, 2.2f, 6), Paint(Cream), new Vector3(0f, 1.1f, 0f), new Vector3(0f, 0f, 6f));
        Put(n, Cyl(0.02f, 1.3f, 0.45f, 12), Stripes(a, b, 6), new Vector3(-0.12f, 2.25f, 0f), new Vector3(0f, 0f, 6f));
        Put(n, Box(0.6f, 0.06f, 1.4f), Stripes(b, a, 4), new Vector3(0.7f, 0.35f, 0.3f), new Vector3(-25f, 20f, 0f));
    }

    private static void CandyTree(Node3D n, Color a, Color b)
    {
        Put(n, Cyl(0.08f, 0.1f, 2f, 10), Stripes(Cream, Red, 6, along: true), new Vector3(0f, 1f, 0f));
        Color[] rings = { a, Cream, b, Cream, a };
        for (int i = 0; i < rings.Length; i++)
        {
            float r = 0.85f - i * 0.17f;
            Put(n, Ring(r - 0.1f, r + 0.02f, 24), Paint(rings[i], 0.25f), new Vector3(0f, 2.6f, 0.02f * i), new Vector3(90f, 0f, 0f));
        }
        Put(n, Cyl(0.85f, 0.85f, 0.08f, 24), Paint(Cream, 0.3f), new Vector3(0f, 2.6f, -0.05f), new Vector3(90f, 0f, 0f));
    }

    private static void Mushroom(Node3D n, Color cap, RandomNumberGenerator rng)
    {
        float s = rng.RandfRange(0.8f, 1.4f);
        Put(n, Cyl(0.22f * s, 0.3f * s, 1f * s, 10), Paint(Cream), new Vector3(0f, 0.5f * s, 0f));
        Put(n, Ball(0.8f * s, -1f, 16, half: true), Paint(cap, 0.45f), new Vector3(0f, 0.95f * s, 0f), scale: new Vector3(1f, 0.85f, 1f));
        var dots = new List<Transform3D>();
        for (int i = 0; i < 9; i++)
        {
            float ang = rng.Randf() * Mathf.Tau, el = rng.RandfRange(0.2f, 1.2f);
            var p = new Vector3(Mathf.Cos(ang) * Mathf.Cos(el), Mathf.Sin(el) * 0.85f, Mathf.Sin(ang) * Mathf.Cos(el)) * 0.8f * s;
            dots.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1f, 0.4f, 1f)), p + new Vector3(0f, 0.95f * s, 0f)));
        }
        Many(n, Ball(0.13f * s, -1f, 8), Paint(Cream), dots);
        if (rng.Randf() < 0.5f)
        {
            Put(n, Cyl(0.12f, 0.16f, 0.4f, 8), Paint(Cream), new Vector3(0.7f * s, 0.2f, 0.3f));
            Put(n, Ball(0.35f, -1f, 12, half: true), Paint(cap, 0.45f), new Vector3(0.7f * s, 0.38f, 0.3f));
        }
    }

    private static void Tree(Node3D n, Color leaf, RandomNumberGenerator rng)
    {
        float s = rng.RandfRange(0.9f, 1.3f);
        Put(n, Cyl(0.16f * s, 0.26f * s, 1.8f * s, 8), Paint(Wood), new Vector3(0f, 0.9f * s, 0f));
        for (int i = 0; i < 5; i++)
        {
            var at = new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(2.0f, 2.9f), rng.RandfRange(-0.4f, 0.4f)) * s;
            Color c = leaf.Lightened(rng.RandfRange(-0.1f, 0.15f));
            Put(n, Ball(rng.RandfRange(0.6f, 0.9f) * s, -1f, 10), Paint(c, 0.9f), at);
        }
    }

    private static void Lantern(Node3D n, Ctx ctx)
    {
        Material wood = Paint(DarkWood);
        Put(n, Cyl(0.07f, 0.09f, 2.2f, 8), wood, new Vector3(0f, 1.1f, 0f));
        Put(n, Box(0.7f, 0.08f, 0.08f), wood, new Vector3(0.3f, 2.15f, 0f));
        Put(n, Cyl(0.01f, 0.01f, 0.25f, 4), Paint(Iron), new Vector3(0.55f, 2f, 0f));
        Put(n, Cyl(0.16f, 0.2f, 0.35f, 8), Paint(ctx.Lamp, 0.3f, 0f, 3f), new Vector3(0.55f, 1.72f, 0f));
        Put(n, Cyl(0.04f, 0.22f, 0.12f, 8), Paint(Iron), new Vector3(0.55f, 1.95f, 0f));
        Light(n, ctx, new Vector3(0.55f, 1.6f, 0.5f), 2.2f, 5.5f);
    }

    private static void Pins(Node3D n)
    {
        for (int i = 0; i < 3; i++)
        {
            var at = new Vector3(-0.5f + i * 0.5f, 0f, i == 1 ? -0.3f : 0f);
            Put(n, Cyl(0.12f, 0.2f, 0.9f, 12), Paint(Cream, 0.25f), at + new Vector3(0f, 0.45f, 0f));
            Put(n, Ball(0.25f, 0.55f, 12), Paint(Cream, 0.25f), at + new Vector3(0f, 0.35f, 0f));
            Put(n, Cyl(0.13f, 0.13f, 0.08f, 12), Paint(Red, 0.3f), at + new Vector3(0f, 0.95f, 0f));
            Put(n, Ball(0.15f, -1f, 10), Paint(Cream, 0.25f), at + new Vector3(0f, 1.1f, 0f));
        }
        Put(n, Ball(0.38f, -1f, 16), Paint(Purple, 0.1f, 0.2f), new Vector3(1.1f, 0.38f, 0.3f));
    }

    private static void Booth(Node3D n, Color a, Color b, Ctx ctx)
    {
        Put(n, Box(2.4f, 1f, 1f), Stripes(a, b, 6), new Vector3(0f, 0.5f, 0f));
        Put(n, Box(2.4f, 2.4f, 0.1f), Paint(DarkWood), new Vector3(0f, 1.2f, -0.5f));
        Put(n, Box(2.6f, 0.08f, 1.2f), Paint(Wood), new Vector3(0f, 1.02f, 0.05f));
        foreach (float x in new[] { -1.15f, 1.15f })
            Put(n, Cyl(0.06f, 0.06f, 1.5f, 8), Paint(Gold, 0.4f, 0.5f), new Vector3(x, 1.75f, 0.45f));
        Put(n, Prism(2.8f, 0.6f, 1.3f), Stripes(a, Cream, 7), new Vector3(0f, 2.75f, 0.05f));
        Color[] prize = { Gold, Blue, Red, Purple, new(0.4f, 0.8f, 0.5f) };
        for (int i = 0; i < 6; i++)
            Put(n, Ball(0.15f, -1f, 8), Paint(prize[i % prize.Length], 0.3f), new Vector3(-0.9f + i * 0.36f, 1.65f, -0.35f));
        var bulbs = new List<Transform3D>();
        for (int i = 0; i < 9; i++) bulbs.Add(new Transform3D(Basis.Identity, new Vector3(-1.2f + i * 0.3f, 2.47f, 0.72f)));
        Many(n, Ball(0.07f, -1f, 6), Paint(new Color(1f, 0.9f, 0.6f), 0.4f, 0f, 3f), bulbs);
        Light(n, ctx, new Vector3(0f, 2.2f, 0.8f), 1.6f, 4f);
    }

    private static void Barrel(Node3D n, RandomNumberGenerator rng)
    {
        int count = rng.RandiRange(1, 3);
        for (int i = 0; i < count; i++)
        {
            var at = i == 2 ? new Vector3(0.3f, 1.05f, 0f) : new Vector3(i * 0.65f, 0f, 0f);
            Put(n, Cyl(0.3f, 0.3f, 1f, 12), Paint(Wood), at + new Vector3(0f, 0.5f, 0f), scale: new Vector3(1f, 1f, 1f));
            Put(n, Ball(0.34f, 0.8f, 12), Paint(Wood), at + new Vector3(0f, 0.5f, 0f));
            foreach (float y in new[] { 0.18f, 0.82f })
                Put(n, Ring(0.3f, 0.34f, 14), Paint(Iron, 0.5f, 0.5f), at + new Vector3(0f, y, 0f), scale: new Vector3(1f, 0.6f, 1f));
        }
    }

    private static void Mirror(Node3D n, Color frame)
    {
        Put(n, Box(1.3f, 2.4f, 0.12f), Paint(frame, 0.35f, 0.7f), new Vector3(0f, 1.35f, 0f));
        Put(n, Box(1.05f, 2.1f, 0.05f), Paint(new Color(0.85f, 0.9f, 1f), 0.04f, 1f), new Vector3(0f, 1.35f, 0.05f));
        Put(n, Ball(0.2f, -1f, 10), Paint(frame, 0.3f, 0.7f), new Vector3(0f, 2.7f, 0f));
        Put(n, Box(1.6f, 0.15f, 0.4f), Paint(frame, 0.35f, 0.7f), new Vector3(0f, 0.08f, 0f));
    }

    private static void Crystals(Node3D n, Color c, RandomNumberGenerator rng)
    {
        int count = rng.RandiRange(3, 6);
        for (int i = 0; i < count; i++)
        {
            float h = rng.RandfRange(0.6f, 2.2f);
            var at = new Vector3(rng.RandfRange(-0.6f, 0.6f), h * 0.4f, rng.RandfRange(-0.4f, 0.4f));
            var rot = new Vector3(rng.RandfRange(-20f, 20f), rng.RandfRange(0f, 60f), rng.RandfRange(-25f, 25f));
            Put(n, Cyl(0f, 0.22f * h * 0.6f, h, 6), Paint(c.Lightened(rng.RandfRange(0f, 0.2f)), 0.15f, 0.2f, 0.8f), at, rot);
        }
    }

    private static void Pine(Node3D n, RandomNumberGenerator rng, bool snow)
    {
        float s = rng.RandfRange(0.9f, 1.4f);
        Put(n, Cyl(0.12f * s, 0.18f * s, 1f * s, 8), Paint(Wood), new Vector3(0f, 0.5f * s, 0f));
        Color green = new(0.18f, 0.45f, 0.3f);
        for (int i = 0; i < 3; i++)
        {
            float r = (1.1f - i * 0.28f) * s, y = (1.2f + i * 0.75f) * s;
            Put(n, Cyl(0f, r, 1.3f * s, 10), Paint(green.Lightened(i * 0.05f), 0.9f), new Vector3(0f, y, 0f));
            if (snow) Put(n, Cyl(0f, r * 0.6f, 0.6f * s, 10), Paint(new Color(0.95f, 0.97f, 1f), 0.6f), new Vector3(0f, y + 0.36f * s, 0f));
        }
    }

    private static void Rocks(Node3D n, Color c, RandomNumberGenerator rng)
    {
        int count = rng.RandiRange(1, 3);
        for (int i = 0; i < count; i++)
        {
            float r = rng.RandfRange(0.35f, 0.8f);
            Put(n, Ball(r, -1f, 7), Paint(c.Lightened(rng.RandfRange(-0.1f, 0.1f)), 0.95f),
                new Vector3(i * 0.7f - 0.3f, r * 0.45f, rng.RandfRange(-0.3f, 0.3f)),
                new Vector3(rng.RandfRange(0f, 40f), rng.RandfRange(0f, 180f), 0f), new Vector3(1.2f, 0.75f, 1f));
        }
    }

    private static void Flagpole(Node3D n, Color a, Color b)
    {
        Put(n, Cyl(0.04f, 0.06f, 3.4f, 8), Paint(Cream, 0.5f), new Vector3(0f, 1.7f, 0f));
        Put(n, Ball(0.08f, -1f, 8), Paint(Gold, 0.3f, 0.6f), new Vector3(0f, 3.45f, 0f));
        Put(n, Prism(1.1f, 0.7f, 0.03f), Stripes(a, b, 3, along: true), new Vector3(0.55f, 3.05f, 0f), new Vector3(0f, 0f, -90f));
    }

    private static void Cactus(Node3D n, RandomNumberGenerator rng)
    {
        float s = rng.RandfRange(0.8f, 1.3f);
        Material green = Paint(new Color(0.3f, 0.6f, 0.35f), 0.85f);
        Put(n, new CapsuleMesh { Radius = 0.3f * s, Height = 2.4f * s, RadialSegments = 10 }, green, new Vector3(0f, 1.2f * s, 0f));
        foreach (int side in new[] { -1, 1 })
        {
            if (rng.Randf() < 0.25f) continue;
            float y = rng.RandfRange(0.8f, 1.4f) * s;
            Put(n, new CapsuleMesh { Radius = 0.18f * s, Height = 0.8f * s }, green, new Vector3(side * 0.45f * s, y, 0f), new Vector3(0f, 0f, 90f));
            Put(n, new CapsuleMesh { Radius = 0.18f * s, Height = 0.9f * s }, green, new Vector3(side * 0.75f * s, y + 0.35f * s, 0f));
        }
        Put(n, Ball(0.12f * s, -1f, 8), Paint(new Color(1f, 0.5f, 0.7f), 0.5f), new Vector3(0f, 2.45f * s, 0f));
    }

    private static void PopMachine(Node3D n, Ctx ctx)
    {
        Put(n, Box(1f, 1f, 0.8f), Paint(Red, 0.5f), new Vector3(0f, 0.5f, 0f));
        Put(n, Box(0.95f, 0.9f, 0.75f), Glow(new Color(1f, 0.95f, 0.7f), 0.15f, 0.4f), new Vector3(0f, 1.45f, 0f));
        var pops = new List<Transform3D>();
        for (int i = 0; i < 30; i++)
            pops.Add(new Transform3D(Basis.Identity, new Vector3(ctx.Rng.RandfRange(-0.4f, 0.4f), 1.05f + ctx.Rng.RandfRange(0f, 0.4f), ctx.Rng.RandfRange(-0.3f, 0.3f))));
        Many(n, Ball(0.08f, -1f, 6), Paint(new Color(1f, 0.95f, 0.7f)), pops);
        Put(n, Cyl(0.1f, 0.6f, 0.35f, 10), Paint(Red, 0.5f), new Vector3(0f, 2.07f, 0f));
        Put(n, Ball(0.1f, -1f, 8), Paint(Gold, 0.3f, 0.6f), new Vector3(0f, 2.3f, 0f));
        Light(n, ctx, new Vector3(0f, 1.5f, 0.6f), 1.2f, 3.5f, new Color(1f, 0.85f, 0.5f));
    }

    private static void Pipes(Node3D n, Ctx ctx)
    {
        Material brass = Paint(new Color(0.85f, 0.62f, 0.3f), 0.35f, 0.8f);
        Put(n, Cyl(0.18f, 0.18f, 2.6f, 10), brass, new Vector3(-0.6f, 1.3f, 0f));
        Put(n, Cyl(0.13f, 0.13f, 1.8f, 10), brass, new Vector3(0.6f, 0.9f, 0.1f));
        Put(n, Cyl(0.13f, 0.13f, 1.3f, 10), brass, new Vector3(0f, 1.8f, 0.05f), new Vector3(0f, 0f, 90f));
        Put(n, Ball(0.22f, -1f, 10), brass, new Vector3(-0.6f, 1.8f, 0f));
        Put(n, Ball(0.17f, -1f, 10), brass, new Vector3(0.6f, 1.8f, 0.1f));
        Put(n, Ring(0.2f, 0.26f, 12), Paint(Red, 0.4f), new Vector3(0.6f, 1.2f, 0.25f), new Vector3(90f, 0f, 0f));
        Put(n, Cyl(0.24f, 0.3f, 0.3f, 10), brass, new Vector3(-0.6f, 2.7f, 0f));
        Put(n, Ball(0.12f, -1f, 8), Paint(new Color(1f, 0.6f, 0.3f), 0.3f, 0f, 2.5f), new Vector3(-0.6f, 2.9f, 0f));
    }

    private static void Banner(Node3D n, Color c)
    {
        Put(n, Cyl(0.06f, 0.08f, 3.6f, 8), Paint(DarkWood), new Vector3(0f, 1.8f, 0f));
        Put(n, Box(1.2f, 0.07f, 0.07f), Paint(Gold, 0.4f, 0.6f), new Vector3(0f, 3.3f, 0.06f));
        Put(n, Box(1f, 2f, 0.04f), Paint(c, 0.8f), new Vector3(0f, 2.25f, 0.1f));
        Put(n, Prism(1f, 0.35f, 0.04f), Paint(c, 0.8f), new Vector3(0f, 1.1f, 0.1f), new Vector3(0f, 0f, 180f));
        Put(n, Ball(0.18f, -1f, 10), Paint(Gold, 0.4f, 0.6f), new Vector3(0f, 2.4f, 0.14f), scale: new Vector3(1f, 1f, 0.3f));
        Put(n, Ball(0.1f, -1f, 8), Paint(Gold, 0.3f, 0.6f), new Vector3(0f, 3.65f, 0f));
    }

    private static void Torch(Node3D n, Ctx ctx)
    {
        Material iron = Paint(Iron, 0.5f, 0.4f);
        Put(n, Cyl(0.06f, 0.1f, 1.8f, 8), iron, new Vector3(0f, 0.9f, 0f));
        Put(n, Cyl(0.35f, 0.15f, 0.3f, 10), iron, new Vector3(0f, 1.9f, 0f));
        Put(n, Cyl(0f, 0.25f, 0.6f, 8), Paint(new Color(1f, 0.55f, 0.15f), 0.3f, 0f, 4f), new Vector3(0f, 2.3f, 0f));
        Put(n, Cyl(0f, 0.14f, 0.45f, 8), Paint(new Color(1f, 0.9f, 0.4f), 0.3f, 0f, 5f), new Vector3(0f, 2.25f, 0.05f));
        Light(n, ctx, new Vector3(0f, 2.4f, 0.5f), 2.6f, 6.5f, new Color(1f, 0.55f, 0.3f));
    }

    private static void Statue(Node3D n)
    {
        Material stone = Paint(Stone, 0.95f);
        Put(n, Box(1.1f, 0.9f, 1.1f), Paint(Stone.Darkened(0.2f), 0.95f), new Vector3(0f, 0.45f, 0f));
        Put(n, Cyl(0.32f, 0.45f, 1.1f, 10), stone, new Vector3(0f, 1.45f, 0f));
        Put(n, Ball(0.32f, -1f, 12), stone, new Vector3(0f, 2.25f, 0f));
        Put(n, Ball(0.1f, -1f, 8), stone, new Vector3(0f, 2.22f, 0.32f));
        Put(n, Cyl(0f, 0.28f, 0.6f, 10), stone, new Vector3(0f, 2.75f, 0f), new Vector3(0f, 0f, 12f));
        Rod(n, new Vector3(0.35f, 1.6f, 0.1f), new Vector3(0.75f, 2.5f, 0.1f), 0.08f, stone);
        Put(n, Box(0.5f, 0.32f, 0.32f), stone, new Vector3(0.8f, 2.65f, 0.1f), new Vector3(0f, 0f, 20f));
    }

    private static void Spotlight(Node3D n, Ctx ctx, RandomNumberGenerator rng)
    {
        Material iron = Paint(Iron, 0.4f, 0.5f);
        foreach (float a in new[] { 0f, 120f, 240f })
        {
            float r = Mathf.DegToRad(a);
            Rod(n, new Vector3(Mathf.Sin(r) * 0.5f, 0f, Mathf.Cos(r) * 0.5f), new Vector3(0f, 1.2f, 0f), 0.04f, iron);
        }
        var head = new Node3D { Position = new Vector3(0f, 1.3f, 0f), RotationDegrees = new Vector3(rng.RandfRange(-25f, -10f), 0f, rng.RandfRange(-25f, 25f)) };
        n.AddChild(head);
        Put(head, Cyl(0.28f, 0.2f, 0.7f, 12), iron, Vector3.Zero, new Vector3(90f, 0f, 0f));
        Put(head, Cyl(0.27f, 0.27f, 0.03f, 12), Paint(new Color(1f, 0.95f, 0.8f), 0.2f, 0f, 4f), new Vector3(0f, 0f, 0.36f), new Vector3(90f, 0f, 0f));
        // The beam, swept slowly across the sky.
        var beam = new Node3D();
        head.AddChild(beam);
        Put(beam, Cyl(1.6f, 0.26f, 14f, 16), Glow(new Color(1f, 0.92f, 0.7f), 0.05f, 1f), new Vector3(0f, 0f, 7.2f), new Vector3(90f, 0f, 0f));
        head.RotationDegrees = new Vector3(-55f, 0f, rng.RandfRange(-20f, 20f));
        ctx.Spinners.Add((head, Vector3.Up, rng.RandfRange(-0.25f, 0.25f)));
    }

    private static void Trophy(Node3D n)
    {
        Material gold = Paint(Gold, 0.25f, 0.9f);
        Put(n, Box(0.9f, 0.6f, 0.9f), Paint(DarkWood), new Vector3(0f, 0.3f, 0f));
        Put(n, Cyl(0.1f, 0.3f, 0.3f, 12), gold, new Vector3(0f, 0.75f, 0f));
        Put(n, Cyl(0.08f, 0.08f, 0.5f, 8), gold, new Vector3(0f, 1.15f, 0f));
        Put(n, Cyl(0.55f, 0.18f, 0.8f, 16), gold, new Vector3(0f, 1.8f, 0f));
        foreach (int s in new[] { -1, 1 })
            Put(n, Ring(0.18f, 0.26f, 12), gold, new Vector3(s * 0.55f, 1.85f, 0f), new Vector3(90f, 0f, 0f));
        Put(n, Ball(0.12f, -1f, 8), Paint(Red, 0.2f, 0.3f), new Vector3(0f, 1.75f, 0.42f));
    }

    private static void StarProp(Node3D n, Ctx ctx)
    {
        Put(n, Cyl(0.06f, 0.08f, 2.4f, 8), Paint(Iron, 0.5f, 0.4f), new Vector3(0f, 1.2f, 0f));
        var star = new CsgPolygon3D { Depth = 0.25f, Material = Paint(Gold, 0.3f, 0.6f, 0.6f), Position = new Vector3(0f, 2.9f, 0.12f) };
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.Pi / 2f + Mathf.Tau * i / 10f;
            float r = i % 2 == 0 ? 0.75f : 0.32f;
            pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        star.Polygon = pts;
        n.AddChild(star);
        ctx.Spinners.Add((star, Vector3.Up, 0.6f));
        Light(n, ctx, new Vector3(0f, 2.9f, 0.6f), 1.4f, 4f, Gold);
    }

    private static void Bunting(Node3D n, RandomNumberGenerator rng)
    {
        Material pole = Paint(Cream, 0.5f);
        Put(n, Cyl(0.05f, 0.06f, 3f, 8), pole, new Vector3(-1.6f, 1.5f, 0f));
        Put(n, Cyl(0.05f, 0.06f, 3f, 8), pole, new Vector3(1.6f, 1.5f, 0f));
        Color[] cols = { Red, Gold, Blue, new(0.45f, 0.8f, 0.4f), Purple };
        for (int i = 0; i < 9; i++)
        {
            float t = (i + 0.5f) / 9f;
            float x = -1.6f + 3.2f * t;
            float y = 2.9f - Mathf.Sin(t * Mathf.Pi) * 0.45f;
            Put(n, Prism(0.3f, 0.4f, 0.02f), Paint(cols[i % cols.Length], 0.7f), new Vector3(x, y - 0.2f, 0f), new Vector3(0f, 0f, 180f));
        }
        Rod(n, new Vector3(-1.6f, 2.95f, 0f), new Vector3(0f, 2.5f, 0f), 0.01f, Paint(Iron), 4);
        Rod(n, new Vector3(0f, 2.5f, 0f), new Vector3(1.6f, 2.95f, 0f), 0.01f, Paint(Iron), 4);
    }

    private static void Gate(Node3D n, Color a, Color b, Ctx ctx)
    {
        foreach (float x in new[] { -2.4f, 2.4f })
        {
            Put(n, Cyl(0.35f, 0.4f, 3.6f, 14), Stripes(a, b, 6), new Vector3(x, 1.8f, 0f));
            Put(n, Ball(0.45f, -1f, 12), Paint(Gold, 0.3f, 0.6f), new Vector3(x, 3.8f, 0f));
        }
        Put(n, Ring(2.15f, 2.65f, 32), Stripes(a, b, 12), new Vector3(0f, 3.4f, 0f), new Vector3(90f, 0f, 0f), new Vector3(1f, 1f, 0.6f));
        Put(n, Box(3.4f, 0.8f, 0.2f), Paint(Red, 0.5f), new Vector3(0f, 4.6f, 0.3f));
        var bulbs = new List<Transform3D>();
        for (int i = 0; i <= 16; i++)
        {
            float ang = Mathf.Pi * i / 16f;
            bulbs.Add(new Transform3D(Basis.Identity, new Vector3(Mathf.Cos(ang) * 2.4f, 3.4f + Mathf.Sin(ang) * 2.4f, 0.25f)));
        }
        Many(n, Ball(0.1f, -1f, 6), Paint(new Color(1f, 0.9f, 0.55f), 0.4f, 0f, 3.5f), bulbs);
        Light(n, ctx, new Vector3(0f, 3.5f, 1.2f), 3.5f, 8f, new Color(1f, 0.85f, 0.45f));
    }

    // ---------------------------------------------------- distance pieces

    private static void Mountain(Node3D n, Color c, RandomNumberGenerator rng)
    {
        float h = rng.RandfRange(9f, 16f), r = h * rng.RandfRange(0.7f, 1f);
        Put(n, Cyl(0f, r, h, 9), Paint(c, 0.95f), new Vector3(0f, h * 0.5f, 0f), new Vector3(0f, rng.RandfRange(0f, 40f), 0f));
        Put(n, Cyl(0f, r * 0.36f, h * 0.36f, 9), Paint(new Color(0.96f, 0.97f, 1f), 0.7f), new Vector3(0f, h * 0.83f, 0f), new Vector3(0f, rng.RandfRange(0f, 40f), 0f));
    }

    private static void Hill(Node3D n, Color c, RandomNumberGenerator rng)
    {
        float r = rng.RandfRange(5f, 10f);
        Put(n, Ball(r, -1f, 16), Paint(c, 0.95f), new Vector3(0f, -r * 0.35f, 0f), scale: new Vector3(1.4f, 0.7f, 1f));
    }

    private static void Dune(Node3D n, Color c, RandomNumberGenerator rng)
    {
        float r = rng.RandfRange(6f, 11f);
        Put(n, Ball(r, -1f, 14), Paint(c, 1f), new Vector3(0f, -r * 0.55f, 0f), new Vector3(0f, 0f, rng.RandfRange(-6f, 6f)), new Vector3(1.8f, 0.6f, 1f));
    }

    private static void Mesa(Node3D n, Color c, RandomNumberGenerator rng)
    {
        float h = rng.RandfRange(3f, 6f), r = rng.RandfRange(3f, 6f);
        Put(n, Cyl(r * 0.85f, r, h, 10), Stripes(c, c.Darkened(0.15f), 3, along: true, rough: 1f), new Vector3(0f, h * 0.5f, 0f));
    }

    private static void Tower(Node3D n, Color roof, Color stone, Ctx ctx)
    {
        float h = ctx.Rng.RandfRange(6f, 10f);
        Put(n, Cyl(1.3f, 1.5f, h, 14), Paint(stone, 0.95f), new Vector3(0f, h * 0.5f, 0f));
        var merlons = new List<Transform3D>();
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.Tau * i / 10f;
            merlons.Add(new Transform3D(new Basis(Vector3.Up, a), new Vector3(Mathf.Sin(a) * 1.45f, h + 0.25f, Mathf.Cos(a) * 1.45f)));
        }
        Many(n, Box(0.45f, 0.5f, 0.35f), Paint(stone, 0.95f), merlons);
        Put(n, Cyl(0f, 1.6f, 2.6f, 14), Paint(roof, 0.7f), new Vector3(0f, h + 1.5f, 0f));
        Put(n, Prism(0.8f, 0.5f, 0.04f), Paint(roof.Lightened(0.2f)), new Vector3(0.4f, h + 3.2f, 0f), new Vector3(0f, 0f, -90f));
        for (int i = 0; i < 3; i++)
            Put(n, Box(0.35f, 0.6f, 0.05f), Paint(new Color(1f, 0.75f, 0.35f), 0.4f, 0f, 2.5f), new Vector3(0f, h * (0.35f + i * 0.2f), 1.47f));
    }

    private static void Spire(Node3D n, Color c, RandomNumberGenerator rng)
    {
        float h = rng.RandfRange(7f, 14f);
        Put(n, Cyl(0f, h * 0.12f, h, 6), Paint(c, 0.15f, 0.6f, 0.25f), new Vector3(0f, h * 0.5f, 0f));
        Put(n, Ball(0.4f, -1f, 10), Paint(new Color(0.85f, 0.9f, 1f), 0.1f, 0f, 3f), new Vector3(0f, h + 0.2f, 0f));
    }

    private static void Stalagmites(Node3D n, Color c, RandomNumberGenerator rng)
    {
        int count = rng.RandiRange(2, 4);
        for (int i = 0; i < count; i++)
        {
            float h = rng.RandfRange(3f, 9f);
            Put(n, Cyl(0f, h * 0.22f, h, 7), Paint(c.Lightened(rng.RandfRange(-0.05f, 0.1f)), 0.95f),
                new Vector3(i * 1.6f - 1.5f + rng.RandfRange(-0.5f, 0.5f), h * 0.5f, rng.RandfRange(-1f, 1f)));
        }
    }

    private static void BigTree(Node3D n, Color leaf, RandomNumberGenerator rng)
    {
        float s = rng.RandfRange(2.5f, 4f);
        Put(n, Cyl(0.2f * s, 0.35f * s, 2.2f * s, 8), Paint(DarkWood), new Vector3(0f, 1.1f * s, 0f));
        for (int i = 0; i < 6; i++)
        {
            var at = new Vector3(rng.RandfRange(-0.8f, 0.8f), rng.RandfRange(2.2f, 3.4f), rng.RandfRange(-0.5f, 0.5f)) * s;
            Put(n, Ball(rng.RandfRange(0.7f, 1.1f) * s, -1f, 10), Paint(leaf.Lightened(rng.RandfRange(-0.12f, 0.08f)), 0.95f), at);
        }
    }

    private static void Lighthouse(Node3D n, Ctx ctx)
    {
        Put(n, Cyl(1f, 1.5f, 9f, 16), Stripes(Red, Cream, 4, along: true), new Vector3(0f, 4.5f, 0f));
        Put(n, Cyl(0.9f, 0.9f, 1.2f, 16), Glow(new Color(1f, 0.95f, 0.7f), 0.35f, 2f), new Vector3(0f, 9.6f, 0f));
        Put(n, Cyl(0f, 1.2f, 1.2f, 16), Paint(Red, 0.6f), new Vector3(0f, 10.8f, 0f));
        var beam = new Node3D { Position = new Vector3(0f, 9.6f, 0f) };
        n.AddChild(beam);
        Put(beam, Cyl(2.5f, 0.5f, 22f, 12), Glow(new Color(1f, 0.95f, 0.75f), 0.04f, 1f), new Vector3(11f, 0f, 0f), new Vector3(0f, 0f, 90f));
        ctx.Spinners.Add((beam, Vector3.Up, 0.5f));
    }

    private static void Boat(Node3D n, Color sail, Color trim)
    {
        Put(n, Ball(0.9f, -1f, 12, half: true), Paint(Wood, 0.7f), new Vector3(0f, 0.05f, 0f), new Vector3(180f, 0f, 0f), new Vector3(1.6f, 0.55f, 0.6f));
        Put(n, Box(2.6f, 0.08f, 0.9f), Paint(Cream, 0.6f), new Vector3(0f, 0.06f, 0f));
        Put(n, Cyl(0.04f, 0.05f, 2.4f, 6), Paint(DarkWood), new Vector3(0f, 1.25f, 0f));
        Put(n, Prism(1.4f, 1.9f, 0.04f), Stripes(sail, trim, 3), new Vector3(0.62f, 1.35f, 0f));
        Put(n, Prism(0.4f, 0.25f, 0.03f), Paint(Red), new Vector3(0.2f, 2.55f, 0f), new Vector3(0f, 0f, -90f));
    }

    private static void Cloud(Node3D n, RandomNumberGenerator rng)
    {
        Material puff = Paint(new Color(1f, 1f, 1f), 1f, 0f, 0.25f);
        int count = rng.RandiRange(4, 7);
        for (int i = 0; i < count; i++)
        {
            float r = rng.RandfRange(1.2f, 2.4f);
            Put(n, Ball(r, -1f, 10), puff, new Vector3(i * 1.6f - count * 0.8f, rng.RandfRange(-0.3f, 0.8f) + (i % 2) * 0.4f, rng.RandfRange(-0.6f, 0.6f)),
                scale: new Vector3(1.2f, 0.8f, 1f));
        }
    }
}
