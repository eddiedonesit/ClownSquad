namespace ClownSquad.World;

using Godot;
using ClownSquad.Visuals;

/// <summary>
/// The circus ring the fight happens in: the pixel-art floor and big-top wall
/// from tools/gen_arena.py, props at the back, lamp light from the bulb string,
/// two sweeping spotlights whose beams show in the haze, and confetti drifting
/// down through them. The lights are what make the lit sprites look lit.
///
/// Built in code so the layout stays next to the numbers it depends on, which
/// must match tools/gen_arena.py (floor 28 x 18 centred on z = -3, wall at
/// z = -7.8, 30 wide). Used with the HD art; "-- classic" keeps the old room.
/// </summary>
public partial class CircusArena : Node3D
{
    private const string Folder = "res://assets/arena/";
    private const float WallZ = -7.8f;
    private const float PropPixel = 0.01125f;       // the characters' density

    private readonly System.Collections.Generic.List<(OmniLight3D light, float baseEnergy, float seed)> _bulbs = new();
    private readonly System.Collections.Generic.List<(SpotLight3D spot, Vector3 target, float phase)> _spots = new();
    private double _t;

    public override void _Ready()
    {
        BuildFloor();
        BuildWall();
        BuildProps();
        BuildBulbLight();
        BuildSpotlights();
        BuildConfetti();
    }

    private static Texture2D Tex(string name) => GD.Load<Texture2D>(Folder + name);

    private void BuildFloor()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(28f, 18f) },
            Position = new Vector3(0f, 0f, -3f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = Tex("floor.png"),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmapsAnisotropic,
                Roughness = 1f,
            },
        });
    }

    private void BuildWall()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(30f, 3f) },
            Position = new Vector3(0f, 1.5f, WallZ),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = Tex("wall.png"),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmapsAnisotropic,
                EmissionEnabled = true,
                EmissionTexture = Tex("wall_emit.png"),
                EmissionEnergyMultiplier = 2.2f,
                Roughness = 1f,
            },
        });
    }

    private void BuildProps()
    {
        // Props are scaled down a touch so they fit under the top of the screen
        // at the back of the ring, where the view is only about 1.3 units tall.
        const float scale = 0.72f;
        void Prop(string name, Vector3 at, bool flip = false)
        {
            Texture2D tex = Tex(name + ".png");
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
            if (LitSprite.For(Folder + name + ".png") is ShaderMaterial lit)
            {
                LitSprite.Apply(lit, Colors.White, 0f, flip);
                s.MaterialOverride = lit;
            }
            AddChild(s);
        }
        // Along the back, outside where anyone can walk.
        Prop("prop_cannon", new Vector3(-6.6f, 0f, -7.3f));
        Prop("prop_tub", new Vector3(-9.0f, 0f, -7.1f));
        Prop("prop_crates", new Vector3(8.4f, 0f, -7.2f));
        Prop("prop_tub", new Vector3(5.6f, 0f, -7.35f), flip: true);
    }

    /// <summary>The bulb string on the wall lights the back of the ring, flickering a little.</summary>
    private void BuildBulbLight()
    {
        float i = 0f;
        foreach (float x in new[] { -10f, -5f, 0f, 5f, 10f })
        {
            var light = new OmniLight3D
            {
                LightColor = new Color(1f, 0.78f, 0.45f),
                LightEnergy = 1.6f,
                OmniRange = 5f,
                LightVolumetricFogEnergy = 0f,
                Position = new Vector3(x, 1.2f, WallZ + 0.6f),
            };
            AddChild(light);
            _bulbs.Add((light, 1.6f, i++ * 1.7f));
        }
    }

    /// <summary>Two follow-spots sweeping slowly across the ring, beams visible in the haze.</summary>
    private void BuildSpotlights()
    {
        void Spot(Vector3 at, Vector3 target, Color color, float phase)
        {
            var spot = new SpotLight3D
            {
                LightColor = color,
                LightEnergy = 9f,
                SpotRange = 16f,
                SpotAngle = 13f,
                SpotAngleAttenuation = 0.6f,
                LightVolumetricFogEnergy = 2.4f,
                ShadowEnabled = false,
            };
            AddChild(spot);
            spot.LookAtFromPosition(at, target, Vector3.Up);
            _spots.Add((spot, target, phase));
        }
        Spot(new Vector3(-6f, 9f, 3f), new Vector3(-1.5f, 0f, -1.5f), new Color(1f, 0.92f, 0.75f), 0f);
        Spot(new Vector3(6f, 9f, 3f), new Vector3(1.5f, 0f, -0.5f), new Color(1f, 0.75f, 0.85f), 2.1f);
    }

    private void BuildConfetti()
    {
        var colors = new Gradient();
        colors.Offsets = new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
        colors.Colors = new[]
        {
            new Color(1f, 0.3f, 0.35f), new Color(1f, 0.85f, 0.3f), new Color(0.35f, 0.65f, 1f),
            new Color(0.45f, 0.9f, 0.5f), new Color(1f, 0.5f, 0.85f), new Color(1f, 1f, 1f),
        };
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(11f, 0.2f, 6f),
            Direction = new Vector3(0f, -1f, 0f),
            Spread = 25f,
            InitialVelocityMin = 0.2f,
            InitialVelocityMax = 0.5f,
            Gravity = new Vector3(0f, -0.35f, 0f),
            AngularVelocityMin = -240f,
            AngularVelocityMax = 240f,
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 0.6f,
            TurbulenceNoiseScale = 2.5f,
            ColorInitialRamp = new GradientTexture1D { Gradient = colors },
            ScaleMin = 0.6f,
            ScaleMax = 1.2f,
        };
        var quad = new QuadMesh
        {
            Size = new Vector2(0.07f, 0.045f),
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        var confetti = new GpuParticles3D
        {
            Amount = 160,
            Lifetime = 10f,
            Preprocess = 10f,
            ProcessMaterial = process,
            DrawPass1 = quad,
            Position = new Vector3(0f, 4.5f, -2f),
            VisibilityAabb = new Aabb(new Vector3(-14f, -6f, -9f), new Vector3(28f, 8f, 18f)),
        };
        AddChild(confetti);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        foreach ((OmniLight3D light, float baseEnergy, float seed) in _bulbs)
        {
            float flicker = 0.9f + 0.06f * Mathf.Sin((float)_t * 7.3f + seed) + 0.04f * Mathf.Sin((float)_t * 17.1f + seed * 2f);
            light.LightEnergy = baseEnergy * flicker;
        }
        // The follow-spots drift in slow figure-eights over the ring.
        foreach ((SpotLight3D spot, Vector3 target, float phase) in _spots)
        {
            float a = (float)_t * 0.35f + phase;
            Vector3 aim = target + new Vector3(Mathf.Sin(a) * 2.2f, 0f, Mathf.Sin(a * 2f) * 1.3f);
            spot.LookAt(aim, Vector3.Up);
        }
    }
}
