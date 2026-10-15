namespace ClownSquad.World;

using System.Collections.Generic;
using Godot;
using ClownSquad.Visuals;

/// <summary>
/// The scenery of a scrolling stage: a long walk left to right through one
/// world's theme. Built from the tiling pieces in tools/gen_stage.py, back to
/// front:
///   a painted panorama far behind (unlit, it is the sky);
///   the ground, with the walkway band down its middle;
///   a row of props behind the fence, lamp posts with real light among them;
///   the fence along the back of the walkway;
///   crates on the walkway to smash for food;
///   the exit at the far end;
///   dark silhouettes right at the camera, sliding past in the foreground;
///   and weather for the theme drifting through it all.
/// Seeded from the stage id, so a stage always looks the same.
/// </summary>
public partial class StageScenery : Node3D
{
    private const string Folder = "res://assets/stages/";
    private const float PropPixel = 0.01125f;
    public const float BandFar = -3.3f, BandNear = 3.2f;

    private readonly int _world;
    private readonly float _length;
    private readonly ulong _seed;
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<(OmniLight3D light, float energy, float seed)> _flicker = new();
    private GpuParticles3D? _weather;
    private double _t;

    /// <summary>Per world: sky colour behind the panorama, sun colour and strength, ambient, lamp colour.</summary>
    private static readonly (Color sky, Color sun, float sunEnergy, float ambient, Color lamp, string weather)[] Themes =
    {
        (new(0.35f, 0.58f, 0.9f), new(1f, 0.93f, 0.82f), 1.0f, 0.45f, new(1f, 0.82f, 0.5f), "confetti"),
        (new(0.47f, 0.78f, 0.94f), new(1f, 0.9f, 0.95f), 1.1f, 0.5f, new(1f, 0.75f, 0.85f), "sugar"),
        (new(0.27f, 0.47f, 0.43f), new(0.85f, 1f, 0.8f), 0.7f, 0.4f, new(0.7f, 1f, 0.7f), "fireflies"),
        (new(0.94f, 0.67f, 0.43f), new(1f, 0.85f, 0.65f), 1.0f, 0.45f, new(1f, 0.85f, 0.55f), "confetti"),
        (new(0.24f, 0.2f, 0.43f), new(0.75f, 0.75f, 1f), 0.6f, 0.4f, new(0.7f, 0.8f, 1f), "sparkle"),
        (new(0.43f, 0.63f, 0.86f), new(0.95f, 0.97f, 1f), 1.1f, 0.5f, new(1f, 0.9f, 0.75f), "snow"),
        (new(0.94f, 0.59f, 0.35f), new(1f, 0.85f, 0.6f), 1.2f, 0.5f, new(1f, 0.8f, 0.5f), "sand"),
        (new(0.12f, 0.09f, 0.16f), new(0.6f, 0.5f, 0.7f), 0.25f, 0.25f, new(1f, 0.6f, 0.35f), "embers"),
        (new(0.16f, 0.12f, 0.24f), new(0.9f, 0.55f, 0.5f), 0.45f, 0.3f, new(1f, 0.55f, 0.3f), "embers"),
        (new(0.08f, 0.05f, 0.2f), new(0.7f, 0.6f, 1f), 0.4f, 0.3f, new(1f, 0.9f, 0.6f), "confetti"),
    };

    public StageScenery(int world, float length, ulong seed)
    {
        _world = Mathf.Clamp(world, 1, 10);
        _length = length;
        _seed = seed;
    }

    public (Color sky, Color sun, float sunEnergy, float ambient, Color lamp, string weather) Theme => Themes[_world - 1];

    /// <summary>A world's colours and weather, for anything else dressed in its theme (the world map).</summary>
    public static (Color sky, Color sun, float sunEnergy, float ambient, Color lamp, string weather) ThemeOf(int world) =>
        Themes[Mathf.Clamp(world, 1, 10) - 1];

    private static Texture2D Tex(string name) => GD.Load<Texture2D>(Folder + name);

    private static StandardMaterial3D Tiled(string texture, float tileWidth, float totalWidth, bool cutout = false, bool unlit = false) => new()
    {
        AlbedoTexture = Tex(texture),
        TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmapsAnisotropic,
        Uv1Scale = new Vector3(totalWidth / tileWidth, 1f, 1f),
        Transparency = cutout ? BaseMaterial3D.TransparencyEnum.AlphaScissor : BaseMaterial3D.TransparencyEnum.Disabled,
        ShadingMode = unlit ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
        Roughness = 1f,
    };

    public override void _Ready()
    {
        _rng.Seed = _seed;
        if (!ArtStyle.Classic) { Build3D(); return; }
        float left = -14f, right = _length + 14f, width = right - left, mid = (left + right) * 0.5f;

        // The panorama, far back and unlit; tall enough to fill the view above the ground.
        const float backZ = -12.5f, backH = 13f, backTile = 25.6f * 1.6f;
        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(width + 40f, backH) },
            Position = new Vector3(mid, backH * 0.5f - 0.4f, backZ),
            MaterialOverride = Tiled($"backdrop_{_world}.png", backTile, width + 40f, unlit: true),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // The ground: 15 deep, from far behind the fence (z -9) to under the camera (z 6).
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(width, 15f) },
            Position = new Vector3(mid, 0f, -1.5f),
            MaterialOverride = Tiled($"floor_{_world}.png", 9.6f, width),
        });
        // Beyond z -9 the outer ground carries on to meet the panorama: the
        // floor texture's far rows (the ground behind the fence), repeated.
        StandardMaterial3D beyond = Tiled($"floor_{_world}.png", 9.6f, width + 40f);
        beyond.Uv1Scale = new Vector3((width + 40f) / 9.6f, 0.2f, 1f);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(width + 40f, 4f) },
            Position = new Vector3(mid, -0.01f, -11f),
            MaterialOverride = beyond,
        });

        BuildProps(left, right);

        // The fence along the back of the walkway.
        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(width, 1.8f) },
            Position = new Vector3(mid, 0.9f, BandFar - 0.5f),
            MaterialOverride = Tiled($"fence_{_world}.png", 9.6f, width, cutout: true),
        });

        BuildForeground(left, right);
        BuildWeather();
    }

    private Sprite3D Prop(string file, Vector3 at, float scale, bool flip, float selfLight = -1f, Color? tint = null)
    {
        Texture2D tex = Tex(file + ".png");
        var s = new Sprite3D
        {
            Texture = tex,
            PixelSize = PropPixel * scale,
            FlipH = flip,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = at + new Vector3(0f, tex.GetHeight() * PropPixel * scale * 0.5f, 0f),
        };
        if (LitSprite.For(Folder + file + ".png") is ShaderMaterial lit)
        {
            if (selfLight >= 0f) lit.SetShaderParameter("self_light", selfLight);
            LitSprite.Apply(lit, tint ?? Colors.White, 0f, flip);
            s.MaterialOverride = lit;
        }
        AddChild(s);
        return s;
    }

    private void BuildProps(float left, float right)
    {
        // Behind the fence: a loose row of the theme's props, two deep, with a
        // lamp every so often that really lights the walkway.
        float x = left + _rng.RandfRange(0f, 2f);
        int n = 0;
        while (x < right)
        {
            int kind = _rng.RandiRange(0, 3);
            bool lamp = n % 3 == 0;
            string file = lamp ? $"prop_{_world}_{LampIndex()}" : $"prop_{_world}_{kind}";
            float z = BandFar - _rng.RandfRange(1.0f, 3.2f);
            Prop(file, new Vector3(x, 0f, z), _rng.RandfRange(0.95f, 1.25f), _rng.Randf() < 0.5f);
            if (lamp)
            {
                var light = new OmniLight3D
                {
                    LightColor = Theme.lamp, LightEnergy = 2.4f, OmniRange = 6.5f, OmniAttenuation = 1.1f,
                    Position = new Vector3(x + 0.25f, 2.3f, z + 0.6f),
                };
                AddChild(light);
                _flicker.Add((light, 2.4f, _rng.Randf() * 10f));
            }
            x += _rng.RandfRange(2.2f, 4.2f);
            n++;
        }
    }

    /// <summary>The prop slot that holds a light source in each theme (lamp, lantern, torch, spotlight).</summary>
    private int LampIndex() => _world switch
    {
        1 => 0, 2 => 3, 3 => 2, 4 => 2, 5 => 2, 6 => 2, 7 => 1, 8 => 2, 9 => 1, _ => 0,
    };

    private void BuildForeground(float left, float right)
    {
        // Right at the camera, below the walkway: dark shapes sliding past
        // faster than everything else, which is what sells the scroll.
        // Sunk into the ground so only their tops peek up over the bottom edge;
        // the tall light-bearing prop is left out, or it would stand in the way.
        float x = left;
        while (x < right)
        {
            int kind = _rng.RandiRange(0, 3);
            if (kind == LampIndex()) kind = (kind + 1) % 4;
            float scale = _rng.RandfRange(1.1f, 1.4f);
            Texture2D tex = Tex($"prop_{_world}_{kind}.png");
            float height = tex.GetHeight() * PropPixel * scale;
            Prop($"prop_{_world}_{kind}", new Vector3(x, 0.55f - height, 4.9f), scale, _rng.Randf() < 0.5f,
                 selfLight: 0.05f, tint: new Color(0.35f, 0.3f, 0.4f));
            x += _rng.RandfRange(6f, 11f);
        }
    }

    /// <summary>Crates along the walkway, between the groups, for the squad to smash.</summary>
    public void PlaceCrates(Node parent, float[] encounters, float exitX)
    {
        float from = 3f;
        var stops = new List<float>(encounters) { exitX };
        foreach (float stop in stops)
        {
            // About every other stretch of road has a crate: a treat, not a buffet.
            int count = _rng.Randf() < 0.55f ? 1 : 0;
            for (int i = 0; i < count; i++)
            {
                float cx = _rng.RandfRange(from + 2f, stop - 3f);
                if (cx <= from) continue;
                var crate = new Crate();
                parent.AddChild(crate);
                crate.GlobalPosition = new Vector3(cx, 0f, _rng.RandfRange(BandFar + 0.6f, BandNear - 1.2f));
            }
            from = stop;
        }
    }

    /// <summary>The way out: a big gaudy gate at the end of the road.</summary>
    public void PlaceExit(float exitX)
    {
        if (!ArtStyle.Classic)
        {
            Look l = Looks[_world - 1];
            Node3D gate = Kit3D.Make("gate", _kit, l.A, l.B);
            gate.Position = new Vector3(exitX + 0.5f, 0f, BandFar - 0.2f);
            AddChild(gate);
            AddChild(new Label3D
            {
                Text = "EXIT >>", Font = ClownSquad.UI.UiFont.Display, FontSize = 96, OutlineSize = 24, PixelSize = 0.008f,
                Modulate = new Color(1f, 0.85f, 0.3f), OutlineModulate = new Color(0.1f, 0.05f, 0.12f),
                Position = new Vector3(exitX + 0.5f, 4.62f, BandFar + 0.12f),
            });
            return;
        }
        Prop($"prop_{_world}_{(_world == 1 ? 3 : 0)}", new Vector3(exitX + 1.5f, 0f, BandFar - 0.3f), 1.6f, false);
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.85f, 0.4f), LightEnergy = 4f, OmniRange = 6f, Position = new Vector3(exitX, 2.5f, 0f) });
        var label = new Label3D
        {
            Text = "EXIT >>",
            Font = ClownSquad.UI.UiFont.Display,
            FontSize = 96, OutlineSize = 24, PixelSize = 0.008f,
            Modulate = new Color(1f, 0.85f, 0.3f), OutlineModulate = new Color(0.1f, 0.05f, 0.12f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(exitX, 2.8f, -1f),
        };
        AddChild(label);
    }

    private void BuildWeather()
    {
        _weather = MakeWeather(Theme.weather, new Vector3(10f, 3f, 5f), 1f);
        _weather.Position = new Vector3(0f, 3f, -1f);
        AddChild(_weather);
    }

    /// <summary>
    /// A world's weather as a particle system: confetti, sugar, fireflies,
    /// sparkle, snow, blowing sand or embers, filling a box of the given extents.
    /// </summary>
    public static GpuParticles3D MakeWeather(string kind, Vector3 box, float density)
    {
        (Color a, Color b, Vector2 size, Vector3 gravity, int amount, bool glow) = kind switch
        {
            "sugar" => (new Color(1f, 0.85f, 0.95f), new Color(1f, 1f, 1f), new Vector2(0.06f, 0.06f), new Vector3(0f, -0.15f, 0f), 120, false),
            "fireflies" => (new Color(0.8f, 1f, 0.4f), new Color(0.5f, 1f, 0.6f), new Vector2(0.05f, 0.05f), new Vector3(0f, 0.02f, 0f), 90, true),
            "sparkle" => (new Color(0.8f, 0.9f, 1f), new Color(1f, 1f, 1f), new Vector2(0.04f, 0.04f), new Vector3(0f, -0.05f, 0f), 120, true),
            "snow" => (new Color(1f, 1f, 1f), new Color(0.85f, 0.9f, 1f), new Vector2(0.06f, 0.06f), new Vector3(0.2f, -0.6f, 0f), 260, false),
            "sand" => (new Color(1f, 0.85f, 0.55f), new Color(0.9f, 0.7f, 0.45f), new Vector2(0.04f, 0.03f), new Vector3(1.6f, -0.1f, 0f), 220, false),
            "embers" => (new Color(1f, 0.6f, 0.2f), new Color(1f, 0.3f, 0.1f), new Vector2(0.04f, 0.04f), new Vector3(0f, 0.35f, 0f), 140, true),
            _ => (new Color(1f, 0.3f, 0.35f), new Color(0.35f, 0.65f, 1f), new Vector2(0.07f, 0.045f), new Vector3(0f, -0.35f, 0f), 150, false),
        };
        var ramp = new Gradient();
        ramp.Offsets = new[] { 0f, 0.5f, 1f };
        ramp.Colors = new[] { a, b, a.Lerp(new Color(1f, 0.9f, 0.4f), 0.5f) };
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = box,
            Gravity = gravity,
            InitialVelocityMin = 0.05f, InitialVelocityMax = 0.3f,
            Direction = new Vector3(0f, -1f, 0f), Spread = 180f,
            AngularVelocityMin = -200f, AngularVelocityMax = 200f,
            TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.5f, TurbulenceNoiseScale = 3f,
            ColorInitialRamp = new GradientTexture1D { Gradient = ramp },
            ScaleMin = 0.6f, ScaleMax = 1.3f,
        };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        if (glow) { mat.EmissionEnabled = true; mat.Emission = a; mat.EmissionEnergyMultiplier = 3f; }
        return new GpuParticles3D
        {
            Amount = Mathf.Max(8, (int)(amount * density)), Lifetime = 9f, Preprocess = 9f,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = size, Material = mat },
            VisibilityAabb = new Aabb(-box - new Vector3(4f, 6f, 4f), box * 2f + new Vector3(8f, 12f, 8f)),
            LocalCoords = false,
        };
    }

    /// <summary>Keep the weather around the camera as it travels.</summary>
    public void Follow(float cameraX)
    {
        if (_weather is not null) _weather.Position = new Vector3(cameraX, 3f, -1f);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        foreach ((OmniLight3D light, float energy, float seed) in _flicker)
            light.LightEnergy = energy * (0.92f + 0.05f * Mathf.Sin((float)_t * 6.3f + seed) + 0.03f * Mathf.Sin((float)_t * 15.7f + seed));
        foreach ((OmniLight3D light, float energy, float seed) in _kit.Lights)
            light.LightEnergy = energy * (0.92f + 0.05f * Mathf.Sin((float)_t * 6.3f + seed) + 0.03f * Mathf.Sin((float)_t * 15.7f + seed));
        foreach ((Node3D node, Vector3 axis, float speed) in _kit.Spinners)
            node.Rotate(axis, speed * (float)delta);
    }

    // ================================================================== 3D
    // The stage as a real 3D place: sky and depth fog, landmarks far off,
    // a row of props and a fence behind the walkway, grass and stones on
    // the verges, and shapes right at the lens, blurred by the camera.

    private readonly Kit3D.Ctx _kit = new();

    /// <summary>A world's 3D dressing.</summary>
    private sealed record Look(
        string[] Props, string Lamp, string[] Far, Color A, Color B, Color FarTint,
        Color Ground, string Fence, Color FenceColor, string Dressing);

    private static readonly Look[] Looks =
    {
        new(new[] { "tent", "popcorn", "balloons", "bunting", "tent" }, "lamp", new[] { "bigtop", "ferris", "hill" },
            Kit3D.Red, Kit3D.Cream, new(0.36f, 0.62f, 0.36f), new(0.38f, 0.66f, 0.32f), "picket", new(0.96f, 0.94f, 0.9f), "meadow"),
        new(new[] { "umbrella", "candycart", "candytree", "balloons" }, "lamp", new[] { "lighthouse", "hill", "hill" },
            new(1f, 0.55f, 0.75f), Kit3D.Cream, new(0.98f, 0.78f, 0.88f), new(0.94f, 0.84f, 0.63f), "rope", new(0.85f, 0.72f, 0.55f), "beach"),
        new(new[] { "mushroom", "tree", "mushroom", "tree" }, "lantern", new[] { "bigtree", "bigtree", "hill" },
            Kit3D.Red, new(0.3f, 0.55f, 0.32f), new(0.22f, 0.45f, 0.3f), new(0.28f, 0.52f, 0.3f), "hedge", new(0.22f, 0.48f, 0.26f), "forest"),
        new(new[] { "booth", "pins", "barrel", "bunting" }, "lamp", new[] { "ferris", "bigtop", "hill" },
            new(0.95f, 0.55f, 0.2f), Kit3D.Cream, new(0.62f, 0.5f, 0.36f), new(0.74f, 0.58f, 0.39f), "rail", new(0.47f, 0.31f, 0.23f), "dirt"),
        new(new[] { "mirror", "crystal", "mirror", "crystal" }, "lamp", new[] { "spire", "spire" },
            new(0.62f, 0.72f, 1f), Kit3D.Gold, new(0.5f, 0.45f, 0.8f), new(0.43f, 0.39f, 0.63f), "mirror", Kit3D.Gold, "crystal"),
        new(new[] { "pine", "rock", "flagpole", "pine" }, "lantern", new[] { "mountain", "mountain" },
            Kit3D.Red, new(0.98f, 0.98f, 1f), new(0.55f, 0.6f, 0.75f), new(0.91f, 0.93f, 0.96f), "rope", new(0.47f, 0.35f, 0.24f), "snow"),
        new(new[] { "cactus", "popmachine", "barrel", "cactus" }, "torch", new[] { "dune", "mesa", "dune" },
            new(0.9f, 0.55f, 0.3f), Kit3D.Cream, new(0.86f, 0.62f, 0.4f), new(0.93f, 0.81f, 0.55f), "rail", new(0.55f, 0.38f, 0.24f), "desert"),
        new(new[] { "crystal", "pipes", "rock", "crystal" }, "lantern", new[] { "stalagmite", "stalagmite" },
            new(0.85f, 0.45f, 1f), Kit3D.Stone, new(0.25f, 0.2f, 0.28f), new(0.27f, 0.23f, 0.27f), "pipes", new(0.85f, 0.62f, 0.3f), "cave"),
        new(new[] { "banner", "statue", "barrel", "banner" }, "torch", new[] { "tower", "tower" },
            new(0.62f, 0.12f, 0.18f), new(0.47f, 0.42f, 0.5f), new(0.35f, 0.26f, 0.36f), new(0.35f, 0.29f, 0.33f), "battlement", new(0.47f, 0.42f, 0.5f), "stones"),
        new(new[] { "spotlight", "trophy", "starprop", "balloons" }, "lamp", new[] { "bigtop", "spotlight" },
            Kit3D.Red, Kit3D.Gold, new(0.3f, 0.18f, 0.4f), new(0.63f, 0.16f, 0.24f), "lights", new(0.95f, 0.8f, 0.4f), "carpet"),
    };

    private void Build3D()
    {
        Look l = Looks[_world - 1];
        _kit.Rng.Seed = _seed;
        _kit.Lamp = Theme.lamp;
        float left = -18f, right = _length + 18f, width = right - left, mid = (left + right) * 0.5f;

        // The walkway and its verges keep the painted pixel floor.
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(width, 15f) },
            Position = new Vector3(mid, 0f, -1.5f),
            MaterialOverride = Tiled($"floor_{_world}.png", 9.6f, width),
        });
        // Beyond it the land runs on to the horizon, into the fog.
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(width + 260f, 160f) },
            Position = new Vector3(mid, -0.02f, -88.5f),
            MaterialOverride = Kit3D.Paint(l.Ground, 1f),
        });

        BuildFar(l, left, right);
        BuildProps3D(l, left, right);
        BuildFence3D(l, left, right);
        BuildDressing(l, left, right);
        BuildForeground3D(l, left, right);
        BuildWeather();
    }

    /// <summary>Sky, fog and lens for the stage: called by the fight scene once the scenery is in.</summary>
    public void Dress(Godot.Environment env, CameraAttributesPractical? lens)
    {
        if (ArtStyle.Classic) return;
        Look l = Looks[_world - 1];
        Color top = Theme.sky.Darkened(0.2f), horizon = Theme.sky.Lerp(Colors.White, 0.35f);
        if (_world >= 8) horizon = Theme.sky.Lerp(l.FarTint, 0.5f);
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
        env.Sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = top, SkyHorizonColor = horizon, SkyCurve = 0.12f,
                GroundHorizonColor = horizon, GroundBottomColor = l.Ground.Darkened(0.3f),
                SunAngleMax = 30f, SunCurve = 0.1f,
            },
        };
        env.FogEnabled = true;
        env.FogMode = Godot.Environment.FogModeEnum.Depth;
        env.FogDepthBegin = 20f;
        env.FogDepthEnd = 95f;
        env.FogDepthCurve = 1.4f;
        env.FogLightColor = horizon;
        env.FogDensity = 1f;
        env.FogSkyAffect = 0f;
        env.FogAerialPerspective = 0.35f;
        if (lens is not null)
        {
            // The HD-2D lens: the walkway pin sharp, the far scenery and
            // anything right at the camera softly out of focus.
            lens.DofBlurFarEnabled = true;
            lens.DofBlurFarDistance = 24f;
            lens.DofBlurFarTransition = 30f;
            lens.DofBlurNearEnabled = true;
            lens.DofBlurNearDistance = 5.2f;
            lens.DofBlurNearTransition = 2.2f;
            lens.DofBlurAmount = 0.08f;
        }
    }

    private Node3D Place(string kind, Look l, Vector3 at, float scale = 1f, float yaw = 0f)
    {
        Node3D n = Kit3D.Make(kind, _kit, l.A, l.B);
        n.Position = at;
        n.Scale = Vector3.One * scale;
        n.RotationDegrees = new Vector3(0f, yaw, 0f);
        AddChild(n);
        return n;
    }

    private void BuildFar(Look l, float left, float right)
    {
        // Rolling land first, then the theme's landmarks rising out of it.
        Color land = l.FarTint;
        for (float x = left - 40f; x < right + 40f; x += _rng.RandfRange(14f, 22f))
        {
            string kind = _world switch { 7 => "dune", 6 => "mountain", 8 => "stalagmite", 5 => "spire", 9 => "hill", _ => "hill" };
            Node3D n = Kit3D.Make(kind, _kit, land, l.B);
            n.Position = new Vector3(x, 0f, _rng.RandfRange(-62f, -46f));
            n.Scale = Vector3.One * _rng.RandfRange(1.2f, 2f);
            AddChild(n);
        }
        float gap = _world is 6 or 8 or 5 ? 11f : 26f;
        for (float x = left - 20f + _rng.RandfRange(0f, gap); x < right + 20f; x += gap * _rng.RandfRange(0.8f, 1.3f))
        {
            string kind = l.Far[_rng.RandiRange(0, l.Far.Length - 1)];
            float z = kind is "hill" or "mountain" or "dune" ? _rng.RandfRange(-40f, -28f) : _rng.RandfRange(-30f, -18f);
            float s = kind switch { "bigtop" => _world == 10 ? 1.6f : 1.1f, "ferris" => 1.3f, "spotlight" => 2.2f, _ => 1f };
            Color a = kind is "hill" or "mountain" or "dune" or "mesa" or "stalagmite" or "spire" ? land.Lerp(l.A, kind == "spire" ? 0.4f : 0.15f) : l.A;
            Node3D n = Kit3D.Make(kind, _kit, a, l.B);
            n.Position = new Vector3(x, 0f, z);
            n.Scale = Vector3.One * s;
            n.RotationDegrees = new Vector3(0f, _rng.RandfRange(-25f, 25f), 0f);
            AddChild(n);
        }
    }

    private void BuildProps3D(Look l, float left, float right)
    {
        // Two loose rows behind the fence, a lamp every few props.
        for (int row = 0; row < 2; row++)
        {
            float x = left + _rng.RandfRange(0f, 2f) + row * 1.6f;
            int n = row;
            while (x < right)
            {
                bool lamp = row == 0 && n % 3 == 0;
                string kind = lamp ? l.Lamp : l.Props[_rng.RandiRange(0, l.Props.Length - 1)];
                float z = row == 0 ? BandFar - _rng.RandfRange(1.3f, 2.4f) : BandFar - _rng.RandfRange(3.6f, 6.5f);
                float s = _rng.RandfRange(0.9f, 1.15f) * (row == 0 ? 1f : 1.2f);
                Place(kind, l, new Vector3(x, 0f, z), s, _rng.RandfRange(-20f, 20f));
                x += row == 0 ? _rng.RandfRange(2.6f, 4.4f) : _rng.RandfRange(3.5f, 6f);
                n++;
            }
        }
    }

    private void BuildFence3D(Look l, float left, float right)
    {
        var fence = new Node3D { Name = "Fence" };
        AddChild(fence);
        float z = BandFar - 0.55f, len = right - left, mid = (left + right) * 0.5f;
        Material paint = Kit3D.Paint(l.FenceColor, 0.7f);
        var at = new List<Transform3D>();
        switch (l.Fence)
        {
            case "picket":
                for (float x = left; x < right; x += 0.3f) at.Add(new Transform3D(Basis.Identity, new Vector3(x, 0.5f, z)));
                Kit3D.Many(fence, Kit3D.Box(0.16f, 1f, 0.05f), paint, at);
                var tips = new List<Transform3D>();
                foreach (Transform3D t in at) tips.Add(new Transform3D(Basis.Identity, t.Origin + new Vector3(0f, 0.58f, 0f)));
                Kit3D.Many(fence, Kit3D.Prism(0.16f, 0.16f, 0.05f), paint, tips);
                foreach (float y in new[] { 0.3f, 0.8f })
                    Kit3D.Put(fence, Kit3D.Box(len, 0.1f, 0.04f), paint, new Vector3(mid, y, z - 0.05f));
                break;
            case "rope":
                for (float x = left; x < right; x += 2.4f)
                {
                    at.Add(new Transform3D(Basis.Identity, new Vector3(x, 0.6f, z)));
                    for (int k = 0; k < 4; k++)
                    {
                        float t0 = k / 4f, t1 = (k + 1) / 4f;
                        float y0 = 1f - Mathf.Sin(t0 * Mathf.Pi) * 0.25f, y1 = 1f - Mathf.Sin(t1 * Mathf.Pi) * 0.25f;
                        Kit3D.Rod(fence, new Vector3(x + t0 * 2.4f, y0, z), new Vector3(x + t1 * 2.4f, y1, z), 0.035f, Kit3D.Paint(new Color(0.85f, 0.75f, 0.55f)), 5);
                    }
                }
                Kit3D.Many(fence, Kit3D.Cyl(0.08f, 0.09f, 1.2f, 8), paint, at);
                break;
            case "hedge":
                var cols = new List<Color>();
                for (float x = left; x < right; x += 0.45f)
                {
                    at.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1f, _rng.RandfRange(0.85f, 1.15f), 0.8f)), new Vector3(x, 0.55f, z)));
                    cols.Add(l.FenceColor.Lightened(_rng.RandfRange(-0.08f, 0.1f)));
                }
                Kit3D.Many(fence, Kit3D.Ball(0.5f, -1f, 10), Kit3D.Paint(Colors.White, 0.95f), at, cols);
                break;
            case "rail":
                for (float x = left; x < right; x += 2f) at.Add(new Transform3D(Basis.Identity, new Vector3(x, 0.55f, z)));
                Kit3D.Many(fence, Kit3D.Box(0.14f, 1.1f, 0.14f), paint, at);
                foreach (float y in new[] { 0.45f, 0.9f })
                    Kit3D.Put(fence, Kit3D.Box(len, 0.12f, 0.06f), paint, new Vector3(mid, y, z + 0.08f));
                break;
            case "mirror":
                for (float x = left; x < right; x += 1.6f) at.Add(new Transform3D(Basis.Identity, new Vector3(x, 0.6f, z)));
                Kit3D.Many(fence, Kit3D.Box(0.12f, 1.2f, 0.12f), Kit3D.Paint(l.FenceColor, 0.35f, 0.7f), at);
                var panes = new List<Transform3D>();
                foreach (Transform3D t in at) panes.Add(new Transform3D(Basis.Identity, t.Origin + new Vector3(0.8f, -0.05f, 0f)));
                Kit3D.Many(fence, Kit3D.Box(1.45f, 0.95f, 0.03f), Kit3D.Paint(new Color(0.8f, 0.85f, 1f), 0.05f, 1f), panes);
                Kit3D.Put(fence, Kit3D.Box(len, 0.08f, 0.08f), Kit3D.Paint(l.FenceColor, 0.35f, 0.7f), new Vector3(mid, 1.17f, z));
                break;
            case "pipes":
                Material brass = Kit3D.Paint(l.FenceColor, 0.35f, 0.8f);
                foreach (float y in new[] { 0.45f, 0.95f })
                    Kit3D.Put(fence, Kit3D.Cyl(0.07f, 0.07f, len, 10), brass, new Vector3(mid, y, z), new Vector3(0f, 0f, 90f));
                for (float x = left; x < right; x += 2.5f) at.Add(new Transform3D(Basis.Identity, new Vector3(x, 0.6f, z)));
                Kit3D.Many(fence, Kit3D.Cyl(0.1f, 0.12f, 1.2f, 10), brass, at);
                var valves = new List<Transform3D>();
                foreach (Transform3D t in at) valves.Add(new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2f), t.Origin + new Vector3(1.25f, 0.35f, 0.08f)));
                Kit3D.Many(fence, Kit3D.Ring(0.1f, 0.15f, 12), Kit3D.Paint(Kit3D.Red, 0.4f), valves);
                break;
            case "battlement":
                Kit3D.Put(fence, Kit3D.Box(len, 1.1f, 0.5f), Kit3D.Stripes(l.FenceColor, l.FenceColor.Darkened(0.12f), 3, along: true, rough: 1f), new Vector3(mid, 0.55f, z - 0.15f));
                for (float x = left; x < right; x += 0.9f) at.Add(new Transform3D(Basis.Identity, new Vector3(x, 1.3f, z - 0.15f)));
                Kit3D.Many(fence, Kit3D.Box(0.5f, 0.4f, 0.5f), paint, at);
                break;
            case "lights":
                for (float x = left; x < right; x += 2f) at.Add(new Transform3D(Basis.Identity, new Vector3(x, 0.65f, z)));
                Kit3D.Many(fence, Kit3D.Cyl(0.05f, 0.06f, 1.3f, 8), Kit3D.Paint(Kit3D.Gold, 0.4f, 0.6f), at);
                var bulbs = new List<Transform3D>();
                var bcols = new List<Color>();
                Color[] hues = { new(1f, 0.85f, 0.4f), new(1f, 0.4f, 0.4f), new(0.5f, 0.7f, 1f) };
                for (float x = left; x < right; x += 0.33f)
                {
                    float t = (x - left) % 2f / 2f;
                    bulbs.Add(new Transform3D(Basis.Identity, new Vector3(x, 1.25f - Mathf.Sin(t * Mathf.Pi) * 0.22f, z)));
                    bcols.Add(hues[bulbs.Count % 3]);
                }
                Kit3D.Many(fence, Kit3D.Ball(0.07f, -1f, 6), Kit3D.Paint(Colors.White, 0.4f, 0f, 3f), bulbs, bcols);
                break;
        }
    }

    private void BuildDressing(Look l, float left, float right)
    {
        // Grass, flowers, stones: scattered over the verges in front of and
        // behind the walkway, thousands of them in a handful of draws.
        var tufts = new List<Transform3D>();
        var tuftCol = new List<Color>();
        var bits = new List<Transform3D>();
        var bitCol = new List<Color>();
        bool grass = l.Dressing is "meadow" or "forest" or "dirt";
        Color[] flowers = { new(1f, 0.9f, 0.3f), new(1f, 0.45f, 0.55f), new(0.95f, 0.95f, 1f), new(0.6f, 0.55f, 1f) };
        foreach ((float z0, float z1) in new[] { (-9.5f, BandFar - 0.7f), (BandNear + 0.4f, 6.5f) })
        {
            bool front = z0 > 0f;                         // nearer the lens: smaller, or it reads as spikes
            int count = (int)((right - left) * (z1 - z0) * (grass ? 2.4f : 0.7f));
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3(_rng.RandfRange(left, right), 0f, _rng.RandfRange(z0, z1));
                float yaw = _rng.RandfRange(0f, Mathf.Tau);
                if (grass)
                {
                    float h = _rng.RandfRange(0.7f, 1.4f) * (front ? 0.5f : 1f);
                    tufts.Add(new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(1f, h, 1f)), p + new Vector3(0f, 0.12f * h, 0f)));
                    tuftCol.Add(l.Ground.Lightened(_rng.RandfRange(-0.15f, 0.12f)));
                    if (_rng.Randf() < (l.Dressing == "meadow" ? 0.12f : 0.05f))
                    {
                        bits.Add(new Transform3D(Basis.Identity, p + new Vector3(0f, 0.27f * h, 0f)));
                        bitCol.Add(flowers[_rng.RandiRange(0, flowers.Length - 1)]);
                    }
                }
                else
                {
                    float s = _rng.RandfRange(0.5f, 1.4f);
                    bits.Add(new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(s, s * 0.6f, s)), p));
                    bitCol.Add(l.Dressing switch
                    {
                        "beach" => new[] { new Color(1f, 0.85f, 0.9f), new Color(0.98f, 0.95f, 0.85f), new Color(0.8f, 0.9f, 1f) }[_rng.RandiRange(0, 2)],
                        "snow" => new Color(0.95f, 0.97f, 1f),
                        "crystal" => new Color(0.7f, 0.75f, 1f),
                        "cave" => new Color(0.4f, 0.33f, 0.4f),
                        "carpet" => new[] { Kit3D.Gold, new Color(1f, 0.4f, 0.4f), new Color(0.5f, 0.7f, 1f) }[_rng.RandiRange(0, 2)],
                        _ => l.Ground.Darkened(_rng.RandfRange(0.05f, 0.3f)),
                    });
                }
            }
        }
        if (tufts.Count > 0) Kit3D.Many(this, Kit3D.Prism(0.12f, 0.26f, 0.05f), Kit3D.Paint(Colors.White, 0.9f), tufts, tuftCol);
        if (bits.Count > 0)
        {
            Mesh bit = l.Dressing switch
            {
                "crystal" => Kit3D.Cyl(0f, 0.08f, 0.3f, 6),
                "carpet" => Kit3D.Box(0.1f, 0.01f, 0.06f),
                "snow" => Kit3D.Ball(0.3f, -1f, 8),
                _ => Kit3D.Ball(grass ? 0.05f : 0.12f, -1f, 6),
            };
            float glow = l.Dressing is "crystal" ? 1.2f : 0f;
            Kit3D.Many(this, bit, Kit3D.Paint(Colors.White, 0.8f, 0f, glow), bits, bitCol);
        }
    }

    private void BuildForeground3D(Look l, float left, float right)
    {
        // Right at the lens, below the walkway: big shapes sliding past
        // faster than everything else, blurred by the near focus.
        string[] fg = _world switch
        {
            1 or 4 => new[] { "barrel", "balloons", "pins" },
            2 => new[] { "umbrella", "candytree" },
            3 => new[] { "mushroom", "tree" },
            5 => new[] { "crystal", "mirror" },
            6 => new[] { "pine", "rock" },
            7 => new[] { "cactus", "barrel" },
            8 => new[] { "crystal", "rock" },
            9 => new[] { "statue", "barrel" },
            _ => new[] { "trophy", "balloons" },
        };
        bool was = _kit.LightsOn;
        _kit.LightsOn = false;
        for (float x = left + _rng.RandfRange(0f, 4f); x < right; x += _rng.RandfRange(7f, 12f))
        {
            // Sunk so that, whatever its size, only its top pokes up into the
            // bottom of the frame: at z 6.5 the lowest line of sight is about
            // 1.3 above the ground, so the top goes a little over that.
            float z = _rng.RandfRange(6.2f, 6.8f);
            Node3D n = Place(fg[_rng.RandiRange(0, fg.Length - 1)], l, new Vector3(x, 0f, z), 1f, _rng.RandfRange(-30f, 30f));
            float peek = _rng.RandfRange(1.6f, 2.0f);
            n.Position = new Vector3(x, Mathf.Min(0f, peek - Kit3D.Height(n)), z);
        }
        _kit.LightsOn = was;
    }
}
