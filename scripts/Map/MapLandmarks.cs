namespace ClownSquad.Map;

using Godot;

/// <summary>
/// The 3D centrepiece standing at each world's boss on the world map: a
/// signature building you can spot from across the sea. Built from primitive
/// meshes with hand-picked materials and generated stripe textures, sized to
/// tower over the pixel-art props around it, and lit by its own lamps.
/// </summary>
public static class MapLandmarks
{
    /// <summary>A landmark for a world, its base at the node's origin. Some animate (see <see cref="Animate"/>).</summary>
    public static Node3D Build(int world, bool reached)
    {
        var root = new Node3D { Name = $"Landmark{world}" };
        switch (world)
        {
            case 1: BigTop(root, new Color(0.86f, 0.2f, 0.25f), new Color(0.97f, 0.94f, 0.88f), 1f); break;
            case 2: CandyTower(root); break;
            case 3: Mushroom(root); break;
            case 4: FerrisWheel(root); break;
            case 5: Crystals(root); break;
            case 6: Peak(root); break;
            case 7: PopcornMachine(root); break;
            case 8: OrganPipes(root); break;
            case 9: Castle(root); break;
            default: Finale(root); break;
        }
        if (!reached) Dim(root);
        return root;
    }

    /// <summary>Per-frame motion for landmarks that move: the wheel turns, spotlights sweep.</summary>
    public static void Animate(Node3D landmark, double t)
    {
        if (landmark.GetNodeOrNull<Node3D>("Wheel") is Node3D wheel) wheel.Rotation = new Vector3(0f, 0f, (float)t * 0.35f);
        if (landmark.GetNodeOrNull<Node3D>("Spots") is Node3D spots)
        {
            int i = 0;
            foreach (Node n in spots.GetChildren())
                if (n is SpotLight3D s) s.Rotation = new Vector3(-1.0f + 0.25f * Mathf.Sin((float)t * 0.9f + i), 0.6f * Mathf.Sin((float)t * 0.6f + i++ * 2.1f), 0f);
        }
    }

    // ------------------------------------------------------------ materials

    private static StandardMaterial3D Mat(Color c, float rough = 0.7f, float metal = 0f, float glow = 0f) => new()
    {
        AlbedoColor = c, Roughness = rough, Metallic = metal,
        EmissionEnabled = glow > 0f, Emission = c, EmissionEnergyMultiplier = glow,
    };

    private static ImageTexture Stripes(Color a, Color b, int count)
    {
        var img = Image.CreateEmpty(count * 8, 4, false, Image.Format.Rgba8);
        for (int x = 0; x < count * 8; x++)
            for (int y = 0; y < 4; y++)
                img.SetPixel(x, y, (x / 8) % 2 == 0 ? a : b);
        return ImageTexture.CreateFromImage(img);
    }

    private static StandardMaterial3D Striped(Color a, Color b, int count) => new()
    {
        AlbedoTexture = Stripes(a, b, count), Roughness = 0.8f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
    };

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Material mat, Vector3 at, Vector3? rot = null, Vector3? scale = null)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = at };
        if (rot is Vector3 r) m.RotationDegrees = r;
        if (scale is Vector3 s) m.Scale = s;
        parent.AddChild(m);
        return m;
    }

    private static void Lamp(Node3D parent, Vector3 at, Color c, float energy = 2.5f, float range = 5f) =>
        parent.AddChild(new OmniLight3D { Position = at, LightColor = c, LightEnergy = energy, OmniRange = range });

    private static void Flag(Node3D parent, Vector3 top, Color c)
    {
        Add(parent, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.6f }, Mat(new Color(0.3f, 0.2f, 0.15f)), top + new Vector3(0f, 0.3f, 0f));
        Add(parent, new PrismMesh { Size = new Vector3(0.36f, 0.22f, 0.02f), LeftToRight = 0f }, Mat(c, glow: 0.2f), top + new Vector3(0.18f, 0.48f, 0f), new Vector3(0f, 0f, -90f));
    }

    private static void Dim(Node3D root)
    {
        foreach (Node n in root.FindChildren("*", "MeshInstance3D", true, false))
            if (n is MeshInstance3D m && m.MaterialOverride is StandardMaterial3D s)
            {
                var d = (StandardMaterial3D)s.Duplicate();
                d.AlbedoColor = d.AlbedoColor.Darkened(0.55f);
                d.EmissionEnabled = false;
                m.MaterialOverride = d;
            }
        foreach (Node n in root.FindChildren("*", "Light3D", true, false))
            if (n is Light3D l) l.LightEnergy *= 0.15f;
    }

    // ------------------------------------------------------------- the ten

    private static void BigTop(Node3D r, Color a, Color b, float s)
    {
        Add(r, new CylinderMesh { TopRadius = 1.25f * s, BottomRadius = 1.3f * s, Height = 1.0f * s, RadialSegments = 32 }, Striped(a, b, 14), new Vector3(0f, 0.5f * s, 0f));
        Add(r, new CylinderMesh { TopRadius = 0.04f * s, BottomRadius = 1.45f * s, Height = 1.3f * s, RadialSegments = 32 }, Striped(a, b, 14), new Vector3(0f, 1.65f * s, 0f));
        Add(r, new SphereMesh { Radius = 0.1f * s, Height = 0.2f * s }, Mat(new Color(1f, 0.85f, 0.3f), 0.3f, 0.6f), new Vector3(0f, 2.32f * s, 0f));
        Flag(r, new Vector3(0f, 2.35f * s, 0f), new Color(1f, 0.85f, 0.3f));
        Add(r, new BoxMesh { Size = new Vector3(0.5f, 0.7f, 0.1f) * s }, Mat(new Color(0.12f, 0.05f, 0.1f)), new Vector3(0f, 0.35f * s, 1.27f * s));
        for (int i = 0; i < 8; i++)
        {
            float ang = i * Mathf.Tau / 8f;
            Add(r, new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 1.2f * s }, Mat(new Color(0.9f, 0.75f, 0.3f), 0.4f, 0.5f),
                new Vector3(Mathf.Cos(ang) * 1.45f * s, 0.6f * s, Mathf.Sin(ang) * 1.45f * s));
        }
        Lamp(r, new Vector3(0f, 1.2f * s, 2.0f * s), new Color(1f, 0.85f, 0.55f), 3f, 5f * s);
    }

    private static void CandyTower(Node3D r)
    {
        Add(r, new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = 2.4f }, Striped(new Color(0.95f, 0.95f, 0.95f), new Color(0.9f, 0.3f, 0.4f), 6), new Vector3(0f, 1.2f, 0f));
        Color[] cols = { new(1f, 0.6f, 0.8f), new(0.6f, 0.8f, 1f), new(1f, 0.75f, 0.9f) };
        Add(r, new SphereMesh { Radius = 0.9f, Height = 1.6f }, Mat(cols[0], 0.95f), new Vector3(0f, 2.9f, 0f));
        Add(r, new SphereMesh { Radius = 0.6f, Height = 1.1f }, Mat(cols[1], 0.95f), new Vector3(0.75f, 2.5f, 0.2f));
        Add(r, new SphereMesh { Radius = 0.55f, Height = 1f }, Mat(cols[2], 0.95f), new Vector3(-0.7f, 2.6f, -0.1f));
        Add(r, new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.9f, Height = 0.3f }, Mat(new Color(0.95f, 0.85f, 0.6f)), new Vector3(0f, 0.15f, 0f));
        Lamp(r, new Vector3(0f, 2.5f, 1.4f), new Color(1f, 0.7f, 0.9f), 2.5f, 5f);
    }

    private static void Mushroom(Node3D r)
    {
        Add(r, new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.45f, Height = 1.6f }, Mat(new Color(0.96f, 0.9f, 0.8f)), new Vector3(0f, 0.8f, 0f));
        Add(r, new SphereMesh { Radius = 1.4f, Height = 1.4f, IsHemisphere = true }, Mat(new Color(0.85f, 0.15f, 0.2f), 0.5f), new Vector3(0f, 1.5f, 0f), scale: new Vector3(1f, 0.75f, 1f));
        var rng = new RandomNumberGenerator { Seed = 3 };
        for (int i = 0; i < 12; i++)
        {
            float a = rng.RandfRange(0f, Mathf.Tau), e = rng.RandfRange(0.25f, 1.2f);
            var p = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e) * 0.75f, Mathf.Sin(a) * Mathf.Cos(e)) * 1.4f;
            Add(r, new SphereMesh { Radius = 0.15f, Height = 0.1f }, Mat(new Color(1f, 0.98f, 0.92f)), new Vector3(0f, 1.5f, 0f) + p);
        }
        Add(r, new BoxMesh { Size = new Vector3(0.3f, 0.45f, 0.05f) }, Mat(new Color(0.4f, 0.25f, 0.15f)), new Vector3(0f, 0.25f, 0.42f));
        Lamp(r, new Vector3(0.5f, 0.6f, 0.9f), new Color(0.7f, 1f, 0.6f), 2.5f, 4f);
    }

    private static void FerrisWheel(Node3D r)
    {
        var frame = Mat(new Color(0.85f, 0.85f, 0.9f), 0.35f, 0.7f);
        for (int side = -1; side <= 1; side += 2)
        {
            Add(r, new BoxMesh { Size = new Vector3(0.08f, 2.4f, 0.08f) }, frame, new Vector3(side * 0.55f, 1.1f, 0.2f), new Vector3(0f, 0f, side * 14f));
            Add(r, new BoxMesh { Size = new Vector3(0.08f, 2.4f, 0.08f) }, frame, new Vector3(side * 0.55f, 1.1f, -0.2f), new Vector3(0f, 0f, side * 14f));
        }
        var wheel = new Node3D { Name = "Wheel", Position = new Vector3(0f, 2.2f, 0f) };
        r.AddChild(wheel);
        Add(wheel, new TorusMesh { InnerRadius = 1.42f, OuterRadius = 1.52f, Rings = 48, RingSegments = 6 }, Mat(new Color(1f, 0.85f, 0.3f), 0.3f, 0.6f, 0.4f), Vector3.Zero, new Vector3(90f, 0f, 0f));
        Color[] cars = { new(0.9f, 0.25f, 0.3f), new(0.3f, 0.6f, 1f), new(0.4f, 0.85f, 0.4f), new(1f, 0.8f, 0.3f) };
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f;
            Add(wheel, new BoxMesh { Size = new Vector3(0.05f, 1.45f, 0.05f) }, frame, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.72f, new Vector3(0f, 0f, Mathf.RadToDeg(a) - 90f));
            Add(wheel, new BoxMesh { Size = new Vector3(0.32f, 0.26f, 0.3f) }, Mat(cars[i % 4], 0.5f), new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 1.5f);
            Add(wheel, new SphereMesh { Radius = 0.05f, Height = 0.1f }, Mat(new Color(1f, 0.95f, 0.7f), glow: 3f), new Vector3(Mathf.Cos(a + 0.4f), Mathf.Sin(a + 0.4f), 0.05f) * 1.48f);
        }
        Lamp(r, new Vector3(0f, 2.2f, 1.5f), new Color(1f, 0.85f, 0.55f), 3f, 5f);
    }

    private static void Crystals(Node3D r)
    {
        Color[] cols = { new(0.6f, 0.85f, 1f), new(0.8f, 0.6f, 1f), new(0.6f, 1f, 0.95f) };
        float[] h = { 3.0f, 2.2f, 1.8f, 2.6f, 1.4f };
        Vector3[] at = { new(0f, 0f, 0f), new(0.7f, 0f, 0.3f), new(-0.6f, 0f, 0.4f), new(0.3f, 0f, -0.5f), new(-0.4f, 0f, -0.4f) };
        for (int i = 0; i < h.Length; i++)
        {
            var m = Mat(cols[i % 3], 0.1f, 0.2f, 0.9f);
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            m.AlbedoColor = new Color(cols[i % 3], 0.8f);
            Add(r, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.32f, Height = h[i], RadialSegments = 6 }, m, at[i] + new Vector3(0f, h[i] * 0.5f, 0f), new Vector3(0f, i * 17f, (i - 2) * 6f));
        }
        Lamp(r, new Vector3(0f, 1.6f, 1.2f), new Color(0.7f, 0.8f, 1f), 3.5f, 5f);
    }

    private static void Peak(Node3D r)
    {
        Add(r, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 1.6f, Height = 3.0f, RadialSegments = 9 }, Mat(new Color(0.55f, 0.55f, 0.62f), 0.95f), new Vector3(0f, 1.5f, 0f));
        Add(r, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.62f, Height = 1.15f, RadialSegments = 9 }, Mat(new Color(0.97f, 0.98f, 1f), 0.9f), new Vector3(0f, 2.44f, 0f));
        Flag(r, new Vector3(0f, 3.0f, 0f), new Color(0.9f, 0.25f, 0.3f));
        Lamp(r, new Vector3(0f, 1.5f, 2f), new Color(1f, 0.9f, 0.75f), 2f, 5f);
    }

    private static void PopcornMachine(Node3D r)
    {
        Add(r, new BoxMesh { Size = new Vector3(1.4f, 1.0f, 1.0f) }, Striped(new Color(0.85f, 0.15f, 0.2f), new Color(0.95f, 0.9f, 0.8f), 7), new Vector3(0f, 0.5f, 0f));
        var glass = Mat(new Color(0.8f, 0.95f, 1f, 0.25f), 0.05f);
        glass.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        Add(r, new BoxMesh { Size = new Vector3(1.3f, 1.3f, 0.9f) }, glass, new Vector3(0f, 1.65f, 0f));
        var rng = new RandomNumberGenerator { Seed = 7 };
        for (int i = 0; i < 40; i++)
            Add(r, new SphereMesh { Radius = 0.09f, Height = 0.16f }, Mat(new Color(1f, 0.96f, 0.82f), 0.9f),
                new Vector3(rng.RandfRange(-0.55f, 0.55f), rng.RandfRange(1.05f, 1.6f), rng.RandfRange(-0.35f, 0.35f)));
        Add(r, new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.9f, Height = 0.5f, RadialSegments = 4 }, Mat(new Color(1f, 0.8f, 0.25f), 0.4f, 0.5f), new Vector3(0f, 2.55f, 0f), new Vector3(0f, 45f, 0f));
        Lamp(r, new Vector3(0f, 1.6f, 1.3f), new Color(1f, 0.85f, 0.5f), 3f, 5f);
    }

    private static void OrganPipes(Node3D r)
    {
        var gold = Mat(new Color(0.95f, 0.75f, 0.3f), 0.25f, 0.85f);
        for (int i = 0; i < 9; i++)
        {
            float h = 1.2f + 1.6f * Mathf.Sin(i / 8f * Mathf.Pi);
            Add(r, new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = h }, gold, new Vector3((i - 4) * 0.36f, h * 0.5f, 0f));
            Add(r, new BoxMesh { Size = new Vector3(0.18f, 0.06f, 0.02f) }, Mat(new Color(0.08f, 0.05f, 0.08f)), new Vector3((i - 4) * 0.36f, h * 0.25f, 0.17f));
        }
        Add(r, new BoxMesh { Size = new Vector3(3.5f, 0.5f, 0.8f) }, Mat(new Color(0.45f, 0.2f, 0.3f), 0.6f), new Vector3(0f, 0.25f, 0.1f));
        var crystal = Mat(new Color(0.5f, 0.95f, 1f), 0.1f, 0f, 2.5f);
        Add(r, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.2f, Height = 0.8f, RadialSegments = 5 }, crystal, new Vector3(1.9f, 0.4f, 0.5f), new Vector3(0f, 0f, -12f));
        Add(r, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.16f, Height = 0.6f, RadialSegments = 5 }, crystal, new Vector3(-1.9f, 0.3f, 0.4f), new Vector3(0f, 0f, 15f));
        Lamp(r, new Vector3(0f, 1.6f, 1.4f), new Color(1f, 0.6f, 0.35f), 3f, 5f);
    }

    private static void Castle(Node3D r)
    {
        var stone = Mat(new Color(0.5f, 0.42f, 0.48f), 0.95f);
        var roof = Mat(new Color(0.7f, 0.15f, 0.22f), 0.6f);
        Add(r, new BoxMesh { Size = new Vector3(1.6f, 1.8f, 1.2f) }, stone, new Vector3(0f, 0.9f, 0f));
        for (int i = -2; i <= 2; i++)
            Add(r, new BoxMesh { Size = new Vector3(0.2f, 0.2f, 1.2f) }, stone, new Vector3(i * 0.36f, 1.9f, 0f));
        foreach (Vector3 t in new[] { new Vector3(-1f, 0f, 0.6f), new Vector3(1f, 0f, 0.6f), new Vector3(-1f, 0f, -0.6f), new Vector3(1f, 0f, -0.6f) })
        {
            Add(r, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.32f, Height = 2.3f }, stone, t + new Vector3(0f, 1.15f, 0f));
            Add(r, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.4f, Height = 0.8f }, roof, t + new Vector3(0f, 2.7f, 0f));
        }
        for (int i = 0; i < 6; i++)
            Add(r, new BoxMesh { Size = new Vector3(0.12f, 0.2f, 0.02f) }, Mat(new Color(1f, 0.75f, 0.35f), glow: 3f), new Vector3(-0.5f + (i % 3) * 0.5f, 0.7f + (i / 3) * 0.6f, 0.61f));
        Flag(r, new Vector3(0f, 2.0f, 0f), new Color(0.6f, 0.2f, 0.8f));
        Lamp(r, new Vector3(0f, 1.2f, 1.6f), new Color(1f, 0.55f, 0.3f), 3f, 5f);
    }

    private static void Finale(Node3D r)
    {
        BigTop(r, new Color(0.55f, 0.15f, 0.65f), new Color(1f, 0.85f, 0.3f), 1.45f);
        var spots = new Node3D { Name = "Spots", Position = new Vector3(0f, 0.2f, 1.8f) };
        r.AddChild(spots);
        foreach (Color c in new[] { new Color(1f, 0.9f, 0.6f), new Color(0.6f, 0.8f, 1f), new Color(1f, 0.6f, 0.85f) })
            spots.AddChild(new SpotLight3D { LightColor = c, LightEnergy = 6f, SpotRange = 14f, SpotAngle = 10f, LightVolumetricFogEnergy = 3f });
    }
}
