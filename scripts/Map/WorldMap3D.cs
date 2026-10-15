namespace ClownSquad.Map;

using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Progression;
using ClownSquad.UI;
using ClownSquad.Visuals;
using ClownSquad.World;

/// <summary>
/// The world map in 3D: all ten worlds as islands in one sea, a diorama under
/// a tilt-shift camera. Each island is a height-mapped landscape wearing its
/// pixel-art ground and road (tools/gen_map.py), dressed with its world's
/// stage props, crowned by a 3D landmark at the boss (MapLandmarks), with its
/// own weather; the sky and light shift to each world's mood as the camera
/// travels. Stages are pedestals, the shop a booth, the bonus a spinning star,
/// the way on a glowing gate. Islands not yet reached sit dark across the water.
///
/// Controls and rules are the 2D map's (MapPad, Campaign): walk between open
/// nodes, enter with ATTACK/JUMP, Q/E between reached worlds, pads join with A.
/// </summary>
public partial class WorldMap3D : Node3D
{
    private const float Unit = 0.05f;              // world units per map pixel
    private const float Spacing = 40f;             // between island centres
    private const float HeightScale = 1.5f;
    private const float SeaLevelH = 0.19f;         // heightmap value at the shoreline
    private const float WalkSpeed = 3.6f;

    private CampaignState _state = null!;
    private int _world = 1;
    private StageDef _at = null!;
    private StageDef? _walkingTo;
    private Vector2 _mapPos;                        // the clown, in the current world's map pixels

    private readonly Dictionary<int, Image> _heights = new();
    private readonly Dictionary<int, Node3D> _islands = new();
    private readonly List<Node3D> _landmarks = new();
    private readonly List<(Node3D node, float phase)> _bobbers = new();
    private readonly List<MapPad> _pads = new();

    private Node3D _avatar = null!;
    private DirectionalSprite _clown = null!;
    private Camera3D _cam = null!;
    private Vector3 _focus;
    private ProceduralSkyMaterial _sky = null!;
    private Godot.Environment _env = null!;
    private DirectionalLight3D _sun = null!;
    private ShopMenu? _shop;
    private StatTent _tent = null!;
    private PauseMenu? _pause;
    private InventoryMenu? _inventory;
    private bool _pauseHeld = true;
    private double _clock;
    private Label _worldTitle = null!, _nodeTitle = null!, _nodeInfo = null!, _squad = null!;

    public override void _Ready()
    {
        Engine.TimeScale = 1.0;
        GetTree().Paused = false;
        if (Sfx.Instance is null) AddChild(new Sfx());
        SaveSystem.UseActiveSlot();
        SaveSystem.Load();
        _state = SaveSystem.Data.Campaign;
        string[] cli = OS.GetCmdlineUserArgs();
        _showAll = System.Array.IndexOf(cli, "mapall") >= 0;
        int atArg = System.Array.IndexOf(cli, "mapat");
        _at = (atArg >= 0 && atArg + 1 < cli.Length ? Campaign.Get(cli[atArg + 1]) : null)
              ?? Campaign.Get(_state.At) ?? Campaign.Get("1-1")!;
        _world = _at.World;
        _mapPos = _at.MapPos;

        BuildWorldLook();
        BuildSea();
        for (int w = 1; w <= Campaign.Worlds; w++) BuildIsland(w);
        BuildAvatar();
        BuildLabels();
        foreach (int d in Campaign.SquadDevices) _pads.Add(new MapPad(d));
        _tent = new StatTent(Seats);
        _tent.Closed += () => { foreach (MapPad p in _pads) p.Swallow(); RefreshInfo(); };
        AddChild(_tent);

        _focus = Place(_world, _mapPos);
        PlaceCamera(1f);
        Sfx.PlayMusic("music_map");
        RefreshInfo();
        GD.Print($"World map (3D): world {_world}, standing on {_at.Id}; {_state.Cleared.Count} node(s) cleared.");

        string[] args = OS.GetCmdlineUserArgs();
        if (System.Array.IndexOf(args, "mapstats") >= 0)
            GetTree().CreateTimer(1.5).Timeout += () => _tent.Open();
        if (System.Array.IndexOf(args, "mapshot") >= 0)
            GetTree().CreateTimer(2.5).Timeout += () =>
            {
                // Without a window there is nothing rendered to save; the map still loaded.
                if (!DisplayServer.GetName().Contains("headless"))
                    GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/world_map3d.png");
                GetTree().Quit();
            };
    }

    // ------------------------------------------------------------ geometry

    private static Vector3 Origin(int world) => new((world - 1) * Spacing, 0f, 0f);

    private Image Heights(int world)
    {
        if (_heights.TryGetValue(world, out Image? img)) return img;
        img = GD.Load<Texture2D>($"res://assets/map/height_{world}.png").GetImage();
        if (img.IsCompressed()) img.Decompress();
        _heights[world] = img;
        return img;
    }

    /// <summary>Ground height at a map pixel, from the heightmap, bilinear.</summary>
    private float Height(int world, Vector2 p)
    {
        Image img = Heights(world);
        float fx = Mathf.Clamp(p.X / 3f, 0f, img.GetWidth() - 1.001f), fy = Mathf.Clamp(p.Y / 3f, 0f, img.GetHeight() - 1.001f);
        int x = (int)fx, y = (int)fy;
        float tx = fx - x, ty = fy - y;
        float a = img.GetPixel(x, y).R, b = img.GetPixel(x + 1, y).R, c = img.GetPixel(x, y + 1).R, d = img.GetPixel(x + 1, y + 1).R;
        float h = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        float ground = Mathf.Max(-0.8f, (h - SeaLevelH) * HeightScale);
        // Every island slopes into the sea before its map's edge, so none ends in a cut-off cliff.
        float edge = Mathf.Min(Mathf.Min(p.X, 576f - p.X), Mathf.Min(p.Y, 324f - p.Y));
        return edge < 34f ? Mathf.Lerp(-0.8f, ground, Mathf.SmoothStep(0f, 1f, edge / 34f)) : ground;
    }

    /// <summary>A map pixel of a world to its point on that world's island.</summary>
    private Vector3 Place(int world, Vector2 p) => Origin(world) + new Vector3((p.X - 288f) * Unit, Height(world, p), (p.Y - 162f) * Unit);

    private bool Reached(int world) => _showAll || world == 1 || _state.Cleared.Contains($"{world - 1}-G");

    // Screenshot hooks: "-- mapat 4-B" starts on a node, "-- mapall" shows every island lit. Neither is saved.
    private bool _showAll;

    // --------------------------------------------------------- environment

    private void BuildWorldLook()
    {
        _sky = new ProceduralSkyMaterial();
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            GlowEnabled = true, GlowIntensity = 0.5f, GlowBloom = 0.12f,
            SsaoEnabled = true, SsaoRadius = 0.8f, SsaoIntensity = 1.4f,
            FogEnabled = true, FogDensity = 0.006f, FogSkyAffect = 0.2f,
            AdjustmentEnabled = true, AdjustmentSaturation = 1.15f, AdjustmentContrast = 1.05f,
        };
        AddChild(new WorldEnvironment
        {
            Environment = _env,
            // Tilt-shift: the near edge and the far islands soften, so the
            // island under the clown reads as a little model.
            CameraAttributes = new CameraAttributesPractical
            {
                DofBlurFarEnabled = true, DofBlurFarDistance = 15f, DofBlurFarTransition = 10f,
                DofBlurNearEnabled = true, DofBlurNearDistance = 5.5f, DofBlurNearTransition = 2.5f,
                DofBlurAmount = 0.07f,
            },
        });
        _sun = new DirectionalLight3D { ShadowEnabled = true, RotationDegrees = new Vector3(-52f, -38f, 0f), LightEnergy = 1.3f };
        _sun.DirectionalShadowMaxDistance = 40f;
        AddChild(_sun);
        _cam = new Camera3D { Fov = 38f, Current = true };
        AddChild(_cam);
    }

    /// <summary>Blend sky, sun and fog toward the world the camera is over.</summary>
    private void TintToward(float worldF)
    {
        int a = Mathf.Clamp(Mathf.FloorToInt(worldF), 1, Campaign.Worlds), b = Mathf.Clamp(a + 1, 1, Campaign.Worlds);
        float t = Mathf.Clamp(worldF - a, 0f, 1f);
        var ta = StageScenery.ThemeOf(a);
        var tb = StageScenery.ThemeOf(b);
        Color sky = ta.sky.Lerp(tb.sky, t), sun = ta.sun.Lerp(tb.sun, t);
        float energy = Mathf.Lerp(ta.sunEnergy, tb.sunEnergy, t);
        _sky.SkyTopColor = sky.Darkened(0.25f);
        _sky.SkyHorizonColor = sky.Lerp(Colors.White, 0.45f);
        _sky.GroundHorizonColor = sky.Lerp(new Color(0.1f, 0.3f, 0.5f), 0.6f);
        _sky.GroundBottomColor = new Color(0.05f, 0.12f, 0.25f);
        _env.FogLightColor = sky.Lerp(Colors.White, 0.35f);
        _sun.LightColor = sun;
        _sun.LightEnergy = 0.5f + energy * 0.9f;
        _env.AmbientLightEnergy = Mathf.Lerp(ta.ambient, tb.ambient, t) + 0.25f;
    }

    private void BuildSea()
    {
        var water = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/water.gdshader") };
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(Spacing * (Campaign.Worlds + 2), 90f), SubdivideWidth = 220, SubdivideDepth = 60 },
            Position = new Vector3(Spacing * (Campaign.Worlds - 1) * 0.5f, -0.05f, 0f),
            MaterialOverride = water,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // -------------------------------------------------------------- islands

    private void BuildIsland(int w)
    {
        if (_islands.TryGetValue(w, out Node3D? old)) { old.QueueFree(); _landmarks.RemoveAll(l => !IsInstanceValid(l) || l.GetParent() == old); }
        bool reached = Reached(w);
        var island = new Node3D { Name = $"Island{w}" };
        AddChild(island);
        _islands[w] = island;

        island.AddChild(Terrain(w, reached));
        Dress(island, w, reached);

        foreach (StageDef s in Campaign.InWorld(w))
        {
            if (s.Kind == StageKind.Boss)
            {
                Node3D mark = MapLandmarks.Build(w, reached);
                mark.Position = Place(w, s.MapPos) + new Vector3(0f, 0f, -0.35f);
                mark.Scale = Vector3.One * 0.75f;
                island.AddChild(mark);
                _landmarks.Add(mark);
                if (reached) island.AddChild(Tag(s.Kind == StageKind.Boss ? "BOSS" : s.Id, Place(w, s.MapPos) + new Vector3(0f, 2.6f, 0f), new Color(1f, 0.6f, 0.85f)));
                continue;
            }
            if (!reached) continue;
            island.AddChild(Marker(s));
        }
        if (reached) Paths(island, w);
        else island.AddChild(Tag($"WORLD {w}\n???", Origin(w) + new Vector3(0f, 2.5f, 0f), new Color(0.75f, 0.7f, 0.85f), 1.4f));

        if (reached)
        {
            var weather = StageScenery.MakeWeather(StageScenery.ThemeOf(w).weather, new Vector3(13f, 2.5f, 7.5f), 0.7f);
            weather.Position = Origin(w) + new Vector3(0f, 4f, 0f);
            island.AddChild(weather);
        }
    }

    /// <summary>The island's ground: a grid lifted by the heightmap, wearing the pixel-art map.</summary>
    private MeshInstance3D Terrain(int w, bool reached)
    {
        Image img = Heights(w);
        int gw = img.GetWidth(), gh = img.GetHeight();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                var p = new Vector2(x * 3f + 1.5f, y * 3f + 1.5f);
                st.SetUV(new Vector2(p.X / 576f, p.Y / 324f));
                st.AddVertex(Place(w, p) - Origin(w));
            }
        for (int y = 0; y < gh - 1; y++)
            for (int x = 0; x < gw - 1; x++)
            {
                int i = y * gw + x;
                st.AddIndex(i); st.AddIndex(i + 1); st.AddIndex(i + gw);
                st.AddIndex(i + 1); st.AddIndex(i + gw + 1); st.AddIndex(i + gw);
            }
        st.GenerateNormals();
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>($"res://assets/map/ground_{w}.png"),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmapsAnisotropic,
            Roughness = 1f,
            AlbedoColor = reached ? Colors.White : new Color(0.38f, 0.36f, 0.45f),
        };
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat, Position = Origin(w) };
    }

    /// <summary>The world's stage props, standing in the spots tools/gen_map.py left for scenery.</summary>
    private void Dress(Node3D island, int w, bool reached)
    {
        using FileAccess? f = FileAccess.Open($"res://assets/map/decos_{w}.json", FileAccess.ModeFlags.Read);
        if (f is null) return;
        var spots = Json.ParseString(f.GetAsText()).AsGodotArray();
        var rng = new RandomNumberGenerator { Seed = (ulong)(w * 977) };
        foreach (Variant v in spots)
        {
            var pair = v.AsGodotArray();
            var p = new Vector2((float)pair[0], (float)pair[1]);
            int kind = rng.RandiRange(0, 3);
            string file = $"res://assets/stages/prop_{w}_{kind}.png";
            Texture2D tex = GD.Load<Texture2D>(file);
            float px = 0.0058f * rng.RandfRange(0.85f, 1.15f);
            var s = new Sprite3D
            {
                Texture = tex, PixelSize = px, FlipH = rng.Randf() < 0.5f,
                Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                Position = Place(w, p) + new Vector3(0f, tex.GetHeight() * px * 0.5f - 0.02f, 0f),
                Modulate = reached ? Colors.White : new Color(0.4f, 0.38f, 0.5f),
            };
            island.AddChild(s);
        }
    }

    private Label3D Tag(string text, Vector3 at, Color color, float size = 1f) => new()
    {
        Text = text, Font = UiFont.Display, FontSize = (int)(64 * size), OutlineSize = (int)(18 * size),
        PixelSize = 0.0075f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        Modulate = color, OutlineModulate = new Color(0.08f, 0.04f, 0.1f), Position = at,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static StandardMaterial3D Mat(Color c, float glow = 0f, float rough = 0.7f, float metal = 0f) => new()
    {
        AlbedoColor = c, Roughness = rough, Metallic = metal,
        EmissionEnabled = glow > 0f, Emission = c, EmissionEnergyMultiplier = glow,
    };

    /// <summary>A node's 3D marker: pedestal, booth, star or gate, coloured by its state.</summary>
    private Node3D Marker(StageDef s)
    {
        bool open = Campaign.Unlocked(s, _state), done = _state.Cleared.Contains(s.Id);
        var root = new Node3D { Position = Place(s.World, s.MapPos) };
        var stone = Mat(new Color(0.62f, 0.58f, 0.66f), rough: 0.9f);
        switch (s.Kind)
        {
            case StageKind.Shop:
                root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.45f, 0.45f) }, MaterialOverride = Mat(new Color(0.3f, 0.55f, 0.95f)), Position = new Vector3(0f, 0.22f, 0f) });
                root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.5f, Height = 0.4f, RadialSegments = 4 }, MaterialOverride = Mat(new Color(0.9f, 0.25f, 0.3f)), Position = new Vector3(0f, 0.65f, 0f), RotationDegrees = new Vector3(0f, 45f, 0f) });
                root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.25f, 0.02f) }, MaterialOverride = Mat(new Color(1f, 0.85f, 0.45f), glow: open ? 1.5f : 0f), Position = new Vector3(0f, 0.25f, 0.23f) });
                root.AddChild(Tag("SHOP", new Vector3(0f, 1.2f, 0f), new Color(0.6f, 0.85f, 1f), 0.8f));
                break;
            case StageKind.Bonus:
                var star = new MeshInstance3D { Mesh = StarMesh(), MaterialOverride = Mat(new Color(1f, 0.82f, 0.3f), glow: open ? 1.6f : 0f, rough: 0.3f, metal: 0.5f), Position = new Vector3(0f, 0.7f, 0f) };
                root.AddChild(star);
                _bobbers.Add((star, s.World * 1.7f));
                root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.3f, Height = 0.12f }, MaterialOverride = stone, Position = new Vector3(0f, 0.06f, 0f) });
                root.AddChild(Tag("BONUS", new Vector3(0f, 1.35f, 0f), new Color(1f, 0.85f, 0.3f), 0.8f));
                break;
            case StageKind.Gate:
                var wood = Mat(new Color(0.55f, 0.35f, 0.2f));
                foreach (float x in new[] { -0.42f, 0.42f })
                    root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.07f, Height = 1.2f }, MaterialOverride = wood, Position = new Vector3(x, 0.6f, 0f) });
                root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.05f, 0.14f, 0.14f) }, MaterialOverride = Mat(new Color(0.9f, 0.25f, 0.3f)), Position = new Vector3(0f, 1.2f, 0f) });
                if (open)
                {
                    var portal = Mat(new Color(0.55f, 0.85f, 1f, 0.55f), glow: 2f);
                    portal.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    root.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.78f, 1.05f) }, MaterialOverride = portal, Position = new Vector3(0f, 0.6f, 0f) });
                    root.AddChild(new OmniLight3D { LightColor = new Color(0.55f, 0.85f, 1f), LightEnergy = 2f, OmniRange = 2.5f, Position = new Vector3(0f, 0.7f, 0.4f) });
                }
                root.AddChild(Tag(s.World < Campaign.Worlds ? $"TO WORLD {s.World + 1}" : "THE END?", new Vector3(0f, 1.7f, 0f), new Color(0.6f, 0.9f, 1f), 0.75f));
                break;
            default:
                Color top = !open ? new Color(0.35f, 0.33f, 0.4f) : done ? new Color(0.9f, 0.25f, 0.3f) : new Color(1f, 0.8f, 0.25f);
                root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.36f, Height = 0.2f, RadialSegments = 20 }, MaterialOverride = stone, Position = new Vector3(0f, 0.1f, 0f) });
                root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 0.05f, RadialSegments = 20 }, MaterialOverride = Mat(top, glow: open ? 1.2f : 0f, rough: 0.3f), Position = new Vector3(0f, 0.22f, 0f) });
                if (open && !done) root.AddChild(new OmniLight3D { LightColor = top, LightEnergy = 1.2f, OmniRange = 1.4f, Position = new Vector3(0f, 0.6f, 0.2f) });
                root.AddChild(Tag(s.Id, new Vector3(0f, 0.75f, 0f), open ? Colors.White : new Color(0.6f, 0.6f, 0.65f), 0.75f));
                break;
        }
        return root;
    }

    /// <summary>A five-point star with a little thickness, for the bonus node.</summary>
    private static ArrayMesh StarMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -Mathf.Pi / 2f + i * Mathf.Pi / 5f, r = i % 2 == 0 ? 0.32f : 0.13f;
            pts[i] = new Vector2(Mathf.Cos(a) * r, -Mathf.Sin(a) * r);
        }
        const float d = 0.06f;
        for (int i = 0; i < 10; i++)
        {
            Vector2 p0 = pts[i], p1 = pts[(i + 1) % 10];
            foreach (float z in new[] { d, -d })
            {
                st.AddVertex(new Vector3(0f, 0f, z * 1.4f));
                st.AddVertex(new Vector3(p0.X, p0.Y, z));
                st.AddVertex(new Vector3(p1.X, p1.Y, z));
            }
            st.AddVertex(new Vector3(p0.X, p0.Y, d)); st.AddVertex(new Vector3(p0.X, p0.Y, -d)); st.AddVertex(new Vector3(p1.X, p1.Y, d));
            st.AddVertex(new Vector3(p1.X, p1.Y, d)); st.AddVertex(new Vector3(p0.X, p0.Y, -d)); st.AddVertex(new Vector3(p1.X, p1.Y, -d));
        }
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>Stepping stones along every path, warm where open and dark where not yet.</summary>
    private void Paths(Node3D island, int w)
    {
        var points = new List<(Vector3 at, bool open)>();
        foreach (StageDef s in Campaign.InWorld(w))
            foreach (string req in s.Requires)
            {
                StageDef? from = Campaign.Get(req);
                if (from is null || from.World != w) continue;
                bool open = Campaign.Unlocked(s, _state);
                float len = from.MapPos.DistanceTo(s.MapPos);
                for (float t = 10f; t < len - 10f; t += 7f)
                    points.Add((Place(w, from.MapPos.Lerp(s.MapPos, t / len)) + new Vector3(0f, 0.04f, 0f), open));
            }
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new SphereMesh { Radius = 0.07f, Height = 0.06f, RadialSegments = 8, Rings = 4 },
            InstanceCount = points.Count,
        };
        for (int i = 0; i < points.Count; i++)
        {
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, points[i].at));
            mm.SetInstanceColor(i, points[i].open ? new Color(1f, 0.92f, 0.7f) : new Color(0.3f, 0.26f, 0.3f));
        }
        island.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.6f, EmissionEnabled = true, Emission = new Color(0.25f, 0.2f, 0.1f) },
        });
    }

    // --------------------------------------------------------------- avatar

    private void BuildAvatar()
    {
        _avatar = new Node3D();
        AddChild(_avatar);
        // The squad's leader walks the map, in player one's chosen colours.
        string sheet = Roster.Sheet(Character(0).Look);
        _clown = new DirectionalSprite(sheet, ArtStyle.FramesFor(sheet), ArtStyle.Pixel(0.0225f) * 0.62f);
        _avatar.AddChild(_clown);
        var shadow = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(0.55f, 0.3f) },
            Position = new Vector3(0f, 0.02f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoTexture = new GradientTexture2D
                {
                    Gradient = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(0f, 0f, 0f, 0.55f), new Color(0f, 0f, 0f, 0f) } },
                    Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
                },
            },
        };
        _avatar.AddChild(shadow);
        _avatar.Position = Place(_world, _mapPos);
    }

    // ---------------------------------------------------------------- frame

    private void PlaceCamera(float k)
    {
        Vector3 want = _focus + new Vector3(0f, 8.6f, 9.4f);
        _cam.Position = _cam.Position.Lerp(want, k);
        _cam.LookAt(_cam.Position + (new Vector3(_focus.X, _focus.Y + 0.4f, _focus.Z) - want).Normalized(), Vector3.Up);
        TintToward(_cam.Position.X / Spacing + 1f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _clock += delta;
        foreach (Node3D l in _landmarks) if (IsInstanceValid(l)) MapLandmarks.Animate(l, _clock);
        foreach ((Node3D node, float phase) in _bobbers)
            if (IsInstanceValid(node))
            {
                node.Rotation = new Vector3(0f, (float)_clock * 1.6f + phase, 0f);
                node.Position = new Vector3(0f, 0.7f + 0.08f * Mathf.Sin((float)_clock * 2.4f + phase), 0f);
            }

        if (_walkingTo is StageDef target)
        {
            Vector2 to = target.MapPos - _mapPos;
            float step = WalkSpeed / Unit * dt;
            if (to.Length() <= step)
            {
                _mapPos = target.MapPos;
                _at = target;
                _walkingTo = null;
                _state.At = _at.Id;
                _clown.Play("idle");
                Sfx.Play("boing_small", 1.3f, -6f);
            }
            else
            {
                _mapPos += to.Normalized() * step;
                if (Mathf.Abs(to.X) > 0.5f) _clown.SetFacing(new Vector3(Mathf.Sign(to.X), 0f, 0f));
            }
            RefreshInfo();
        }
        _avatar.Position = Place(_world, _mapPos) + new Vector3(0f, _walkingTo is null ? 0.03f * Mathf.Abs(Mathf.Sin((float)_clock * 3f)) : 0f, 0f);
        _clown.Tick(dt);
        _focus = _focus.Lerp(_avatar.Position, 1f - Mathf.Exp(-4f * dt));
        PlaceCamera(1f - Mathf.Exp(-3f * dt));

        if (_shop is not null || _pause is not null || _inventory is not null || _tent.IsOpen || _walkingTo is not null) return;
        PollPause();
        if (_pause is not null) return;
        PollJoins();
        foreach (MapPad pad in _pads)
        {
            MapPad.Press p = pad.Poll();
            if (p == MapPad.Press.None) continue;
            Handle(p);
            break;
        }
    }

    // ---------------------------------------------------------------- input

    private void Handle(MapPad.Press p)
    {
        switch (p)
        {
            case MapPad.Press.Up: TryWalk(new Vector2(0f, -1f)); break;
            case MapPad.Press.Down: TryWalk(new Vector2(0f, 1f)); break;
            case MapPad.Press.Right: TryWalk(new Vector2(1f, 0f)); break;
            case MapPad.Press.Left: TryWalk(new Vector2(-1f, 0f)); break;
            case MapPad.Press.Enter: Enter(); break;
            case MapPad.Press.World:
                for (int i = 1; i < Campaign.Worlds; i++)
                {
                    int w = (_world + i - 1) % Campaign.Worlds + 1;
                    if (!Reached(w)) continue;
                    Arrive(w);
                    Sfx.Play("whoosh", 1.2f, -4f);
                    break;
                }
                break;
        }
    }

    private void Arrive(int world)
    {
        _world = world;
        _at = Campaign.Get($"{world}-1")!;
        _mapPos = _at.MapPos;
        _state.At = _at.Id;
        RefreshInfo();
    }

    private void TryWalk(Vector2 dir)
    {
        StageDef? best = null;
        float bestDot = 0.35f;
        foreach (StageDef n in Campaign.Neighbours(_at))
        {
            if (n.World != _world || !Campaign.Unlocked(n, _state)) continue;
            float dot = (n.MapPos - _at.MapPos).Normalized().Dot(dir);
            if (dot > bestDot) { bestDot = dot; best = n; }
        }
        if (best is null) { Sfx.Play("squeak", 0.8f, -10f); return; }
        _walkingTo = best;
        _clown.Play("walk");
        Sfx.Play("zip", 1.3f, -8f);
    }

    private void Enter()
    {
        switch (_at.Kind)
        {
            case StageKind.Shop:
                OpenShop();
                return;
            case StageKind.Gate:
                if (_world >= Campaign.Worlds) { Sfx.Play("victory"); return; }
                Campaign.MarkCleared(_state, _at);
                SaveSystem.Save();
                // The next island wakes up: rebuilt lit, with its stages, and the camera sails over.
                BuildIsland(_world + 1);
                Arrive(_world + 1);
                Sfx.Play("fanfare");
                return;
        }
        Campaign.Pending = _at;
        SaveSystem.Save();
        Sfx.Play("kazoo_rise", 1.2f, -4f);
        GetTree().ChangeSceneToFile(Campaign.FightScene);
    }

    private void OpenShop()
    {
        int restocks = _state.ShopRestocks.TryGetValue(_world.ToString(), out int n) ? n : 0;
        int level = Mathf.Max(_at.EnemyLevel, Character(0).Level);
        var shelf = ShopMenu.Stock((ulong)(_world * 1000 + restocks), level, 6, 0.3f + 0.12f * _world);
        _shop = new ShopMenu($"{Campaign.WorldNames[_world - 1]} SHOP", shelf, Seats(), pauses: false);
        _shop.Closed += () =>
        {
            _shop = null;
            foreach (MapPad p in _pads) p.Swallow();
            RefreshInfo();
        };
        AddChild(_shop);
    }

    private static CharacterRecord Character(int i) => SaveSystem.CharacterFor(i, Roster.DefaultFor(i).Name);

    /// <summary>Everyone in the squad, as seats for the Stat Tent and shops.</summary>
    private List<Seat> Seats()
    {
        var seats = new List<Seat>();
        for (int i = 0; i < Campaign.SquadDevices.Count; i++) seats.Add(new Seat(Character(i), Campaign.SquadDevices[i], null));
        return seats;
    }

    /// <summary>Esc or Start on the map: resume, the Stat Tent, back to the title, or quit.</summary>
    private void PollPause()
    {
        bool held = PauseMenu.PauseHeld(Campaign.SquadDevices);
        bool fresh = held && !_pauseHeld;
        _pauseHeld = held;
        if (!fresh) return;
        var items = new List<(PauseMenu.Choice, string)>
        {
            (PauseMenu.Choice.Resume, "RESUME"), (PauseMenu.Choice.Stats, "STAT TENT"), (PauseMenu.Choice.Inventory, "INVENTORY"),
            (PauseMenu.Choice.Title, "SAVE & QUIT TO TITLE"), (PauseMenu.Choice.Quit, "SAVE & QUIT GAME"),
        };
        _pause = new PauseMenu(items, new List<int>(Campaign.SquadDevices), choice =>
        {
            _pause = null;
            _pauseHeld = true;
            foreach (MapPad p in _pads) p.Swallow();
            switch (choice)
            {
                case PauseMenu.Choice.Stats: _tent.Open(); break;
                case PauseMenu.Choice.Inventory:
                    var inv = new InventoryMenu(Seats());
                    inv.Closed += () => { foreach (MapPad p in _pads) p.Swallow(); RefreshInfo(); };
                    AddChild(inv);
                    _inventory = inv;
                    inv.Closed += () => _inventory = null;
                    break;
                case PauseMenu.Choice.Title: SaveSystem.Save(); GetTree().ChangeSceneToFile(Campaign.TitleScene); break;
                case PauseMenu.Choice.Quit: SaveSystem.Save(); GetTree().Quit(); break;
            }
        });
        AddChild(_pause);
    }

    private void PollJoins()
    {
        foreach (int pad in Godot.Input.GetConnectedJoypads())
        {
            if (Campaign.SquadDevices.Contains(pad) || !Godot.Input.IsJoyButtonPressed(pad, JoyButton.A)) continue;
            Campaign.SquadDevices.Add(pad);
            _pads.Add(new MapPad(pad));
            Sfx.Play("ding");
            RefreshInfo();
        }
    }

    // ---------------------------------------------------------------- labels

    private static LabelSettings Style(int size, Color color, int outline = 6) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline,
        OutlineColor = new Color(0.08f, 0.04f, 0.1f),
        ShadowSize = 2, ShadowColor = new Color(0f, 0f, 0f, 0.45f), ShadowOffset = new Vector2(2f, 3f),
    };

    private static StyleBoxFlat Plate(Color bg, Color border, int radius = 12, int width = 4) => new()
    {
        BgColor = bg, BorderColor = border,
        BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ShadowColor = new Color(0f, 0f, 0f, 0.35f), ShadowSize = 4, ShadowOffset = new Vector2(2f, 3f),
    };

    private void BuildLabels()
    {
        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        var top = new Panel { Size = new Vector2(560, 46) };
        top.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        top.Position = new Vector2(-280, 10);
        top.AddThemeStyleboxOverride("panel", Plate(new Color(0.86f, 0.2f, 0.25f, 0.95f), new Color(0.98f, 0.8f, 0.2f)));
        layer.AddChild(top);
        _worldTitle = new Label { Size = new Vector2(560, 46), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, LabelSettings = Style(22, new Color(1f, 0.97f, 0.9f), 5) };
        top.AddChild(_worldTitle);

        var bottom = new Panel { Size = new Vector2(1120, 80) };
        bottom.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        bottom.Position = new Vector2(-560, -92);
        bottom.AddThemeStyleboxOverride("panel", Plate(new Color(0.18f, 0.12f, 0.28f, 0.92f), new Color(0.98f, 0.8f, 0.2f)));
        layer.AddChild(bottom);
        _nodeTitle = new Label { Position = new Vector2(18, 8), LabelSettings = Style(24, new Color(1f, 0.85f, 0.3f), 6) };
        _nodeInfo = new Label { Position = new Vector2(18, 46), LabelSettings = Style(14, new Color(0.9f, 0.88f, 1f), 4) };
        _squad = new Label { Position = new Vector2(560, 10), Size = new Vector2(544, 28), HorizontalAlignment = HorizontalAlignment.Right, LabelSettings = Style(16, new Color(0.6f, 1f, 0.7f), 4) };
        bottom.AddChild(_nodeTitle);
        bottom.AddChild(_nodeInfo);
        bottom.AddChild(_squad);
        bottom.AddChild(new Label
        {
            Position = new Vector2(460, 46), Size = new Vector2(644, 24), HorizontalAlignment = HorizontalAlignment.Right,
            LabelSettings = Style(13, new Color(0.85f, 0.8f, 0.95f), 4),
            Text = "WASD/STICK WALK    J/SPACE OR X/A ENTER    Q/E WORLDS    ESC/START MENU",
        });
    }

    private void RefreshInfo()
    {
        _worldTitle.Text = $"WORLD {_world}  -  {Campaign.WorldNames[_world - 1]}";
        StageDef s = _walkingTo ?? _at;
        bool done = _state.Cleared.Contains(s.Id);
        _nodeTitle.Text = s.Kind switch
        {
            StageKind.Normal => $"STAGE {s.Id}" + (done ? "   - CLEARED -" : ""),
            StageKind.Boss => $"BOSS: {Campaign.WorldNames[s.World - 1]}" + (done ? "   - CLEARED -" : ""),
            _ => s.Title,
        };
        _nodeInfo.Text = s.Kind switch
        {
            StageKind.Normal or StageKind.Boss => $"enemy level {s.EnemyLevel}+",
            StageKind.Bonus => $"{s.BonusSeconds:F0} seconds of pinatas full of gold  -  nothing hits back",
            StageKind.Shop => "this world's gear  -  restocks after every clear here",
            _ => s.World < Campaign.Worlds ? "the road to the next world" : "the end of the road... for now",
        };
        var parts = new List<string>();
        for (int i = 0; i < Campaign.SquadDevices.Count; i++)
        {
            CharacterRecord r = Character(i);
            parts.Add($"{r.Name} LV{r.Level}  {r.Gold}G");
        }
        _squad.Text = string.Join("     ", parts);
    }
}
