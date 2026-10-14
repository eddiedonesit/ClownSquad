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
            int count = _rng.RandiRange(0, 2);
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
        (Color a, Color b, Vector2 size, Vector3 gravity, int amount, bool glow) = Theme.weather switch
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
            EmissionBoxExtents = new Vector3(10f, 3f, 5f),
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
        _weather = new GpuParticles3D
        {
            Amount = amount, Lifetime = 9f, Preprocess = 9f,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = size, Material = mat },
            Position = new Vector3(0f, 3f, -1f),
            VisibilityAabb = new Aabb(new Vector3(-16f, -6f, -10f), new Vector3(32f, 14f, 20f)),
            LocalCoords = false,
        };
        AddChild(_weather);
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
    }
}
