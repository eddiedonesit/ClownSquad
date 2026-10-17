namespace ClownSquad.World;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;
using ClownSquad.Net;

/// <summary>
/// The things on a stage that are not enemies: trampolines, a circus cannon,
/// a giant mallet, fire jets and slippery patches. They do not take sides:
/// a cannonball flattens a clown or a grunt alike, so luring the enemy into
/// the mallet's circle is a tactic. Laid out by <see cref="Terrain"/> from the
/// stage alone; built and run by the host (each is numbered for the network,
/// so a guest sees them mirrored). Timed ones only run with a clown nearby.
/// </summary>
public abstract partial class StageFeature : Node3D
{
    // Numbered for the network (NetRegistry) while in the scene.
    public override void _Notification(int what) => NetRegistry.Notify(this, what);

    public const string Group = "stage_features";

    protected int World = 1;
    protected float Phase;

    /// <summary>How hard this world's traps hit: 0.6 in world 1 up to 1.5 in world 10.</summary>
    protected float Severity => 0.6f + 0.1f * (World - 1);
    /// <summary>How long a timed trap rests between goes: slower early, quicker late.</summary>
    protected float Pace => 1.2f - 0.04f * (World - 1);
    protected float Clock;
    private readonly Dictionary<Fighter, double> _next = new();

    /// <summary>Whether a ground point is dangerous right now (the test bot steers round it).</summary>
    public virtual bool Covers(Vector3 p) => false;

    /// <summary>Always there (a slick, a stage's spill): walked round, like a pit, rather than fled.</summary>
    public virtual bool Lasting => false;

    public override void _EnterTree() => AddToGroup(Group);

    /// <summary>Every feature the terrain lays out, built and placed (the host's job).</summary>
    public static void Build(Node parent, Terrain t, int world)
    {
        foreach (Terrain.Feature f in t.Features)
        {
            if (f.Kind is Terrain.FeatureKind.PoisonBarrel or Terrain.FeatureKind.ExplosiveBarrel)
            {
                // a little stack of them: one to three
                int n = 1 + (int)(f.Phase * 2.99f);
                for (int i = 0; i < n; i++)
                {
                    var b = new HazardBarrel(f.Kind == Terrain.FeatureKind.ExplosiveBarrel, world);
                    parent.AddChild(b);
                    Vector3 at = t.Clear(new Vector3(f.X + (i - (n - 1) * 0.5f) * 0.85f, 0f, f.Z + (i % 2) * 0.35f), 0.5f);
                    at.Y = t.HeightAt(at);
                    b.GlobalPosition = at;
                }
                continue;
            }
            StageFeature node = f.Kind switch
            {
                Terrain.FeatureKind.Trampoline => new Trampoline(),
                Terrain.FeatureKind.Cannon => new Cannon(),
                Terrain.FeatureKind.Mallet => new GiantMallet(),
                Terrain.FeatureKind.FireJet => new FireJet(),
                Terrain.FeatureKind.Spill => new PoisonSpill(),
                _ => new SlickPatch(),
            };
            node.Setup(world, f.Phase);
            parent.AddChild(node);
            node.GlobalPosition = new Vector3(f.X, t.HeightAt(f.X, f.Z), f.Z);
        }
    }

    public override void _PhysicsProcess(double delta) => Clock += (float)delta;

    protected void Setup(int world, float phase)
    {
        World = Mathf.Clamp(world, 1, 10);
        Phase = phase;
    }

    /// <summary>A clown within a screen of here (timed features rest otherwise).</summary>
    protected bool Watched(float range = 13f)
    {
        foreach (Fighter f in Fighter.Live)
            if (!f.Hostile && !f.IsDead && Mathf.Abs(f.GlobalPosition.X - GlobalPosition.X) < range) return true;
        return false;
    }

    /// <summary>At most once per `every` seconds for each fighter.</summary>
    protected bool Due(Fighter f, float every)
    {
        if (_next.TryGetValue(f, out double at) && GameClock.Seconds < at) return false;
        _next[f] = GameClock.Seconds + every;
        return true;
    }

    /// <summary>A share of the fighter's health (bosses take far less), so it matters at any level; harder worlds, bigger shares.</summary>
    protected float Share(Fighter f, float fraction) => ShareOf(f, fraction * Severity);

    public static float ShareOf(Fighter f, float fraction) => Prop(f) ? 99f : f.MaxHealth * fraction * (f is IBoss ? 0.3f : 1f);

    /// <summary>Scenery that can be smashed (crates, barrels): blasts break it, but it is not bounced or tripped.</summary>
    protected static bool Prop(Fighter f) => f is Crate or HazardBarrel;

    protected static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    protected static StandardMaterial3D Mat(Color c, float rough = 0.6f, float metal = 0f, float glow = 0f, bool unshaded = false) => new()
    {
        AlbedoColor = c, Roughness = rough, Metallic = metal,
        EmissionEnabled = glow > 0f, Emission = c, EmissionEnergyMultiplier = glow,
        ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
        Transparency = c.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };

    protected MeshInstance3D Add(Mesh mesh, Material mat, Vector3 at, Vector3? rotDeg = null, Node3D? under = null)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = at, RotationDegrees = rotDeg ?? Vector3.Zero };
        (under ?? this).AddChild(m);
        return m;
    }

    protected static CylinderMesh Cyl(float r, float h, float top = -1f, int seg = 20) =>
        new() { TopRadius = top < 0f ? r : top, BottomRadius = r, Height = h, RadialSegments = seg };
}

/// <summary>
/// A circus trampoline (a giant springy mushroom in the forest). Whoever lands
/// or walks on it is thrown high: a clown gets a big air attack out of it, an
/// enemy a long fall.
/// </summary>
public partial class Trampoline : StageFeature
{
    private const float Radius = 0.85f;
    private Node3D _pad = null!;
    private float _squash;

    public override void _Ready()
    {
        _pad = new Node3D();
        AddChild(_pad);
        if (World == 3)
        {
            // a fat red mushroom cap, white spots, on a short stem
            Add(Cyl(0.3f, 0.3f), Mat(new Color(0.95f, 0.9f, 0.8f)), new Vector3(0f, 0.15f, 0f));
            Add(new SphereMesh { Radius = Radius + 0.1f, Height = 0.7f, RadialSegments = 20, Rings = 8 }, Mat(new Color(0.85f, 0.15f, 0.15f), 0.5f), new Vector3(0f, 0.32f, 0f), null, _pad);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.Tau / 6f + 0.3f;
                Add(new SphereMesh { Radius = 0.13f, Height = 0.08f }, Mat(new Color(1f, 0.97f, 0.9f)), new Vector3(Mathf.Cos(a) * 0.55f, 0.55f, Mathf.Sin(a) * 0.55f), null, _pad);
            }
            Add(new SphereMesh { Radius = 0.16f, Height = 0.1f }, Mat(new Color(1f, 0.97f, 0.9f)), new Vector3(0f, 0.66f, 0f), null, _pad);
            return;
        }
        // legs, a striped skirt, a blue bed and a red rim
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Tau / 4f + 0.785f;
            Add(Cyl(0.05f, 0.3f, -1f, 8), Mat(new Color(0.3f, 0.3f, 0.35f), 0.4f, 0.6f), new Vector3(Mathf.Cos(a) * Radius * 0.85f, 0.15f, Mathf.Sin(a) * Radius * 0.85f));
        }
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.Tau / 12f;
            Add(new BoxMesh { Size = new Vector3(0.46f, 0.16f, 0.04f) }, Mat(i % 2 == 0 ? new Color(0.95f, 0.2f, 0.2f) : new Color(1f, 0.95f, 0.85f)),
                new Vector3(Mathf.Cos(a) * Radius, 0.3f, Mathf.Sin(a) * Radius), new Vector3(0f, -Mathf.RadToDeg(a) + 90f, 0f), _pad);
        }
        Add(Cyl(Radius - 0.04f, 0.04f), Mat(new Color(0.15f, 0.35f, 0.85f), 0.35f), new Vector3(0f, 0.36f, 0f), null, _pad);
        Add(new TorusMesh { InnerRadius = Radius - 0.06f, OuterRadius = Radius + 0.06f, Rings = 24, RingSegments = 8 }, Mat(new Color(0.95f, 0.75f, 0.2f), 0.4f, 0.3f), new Vector3(0f, 0.38f, 0f), null, _pad);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        _squash = Mathf.MoveToward(_squash, 0f, (float)delta * 4f);
        float s = 1f - 0.35f * Mathf.Sin(_squash * Mathf.Pi);
        _pad.Scale = new Vector3(1f + (1f - s) * 0.4f, s, 1f + (1f - s) * 0.4f);
        foreach (Fighter f in Fighter.Live)
        {
            if (f.IsDead || Prop(f) || f.Velocity.Y > 0.5f) continue;
            if (Flat(f.GlobalPosition - GlobalPosition).Length() > Radius) continue;
            float up = f.GlobalPosition.Y - GlobalPosition.Y;
            if (up < -0.1f || up > 0.7f || !Due(f, 0.5f)) continue;
            f.Bounce(f.Hostile ? 11.5f : 13.5f);
            _squash = 1f;
            Fx.Sound("boing", f.Hostile ? 0.85f : 1f);
            if (!f.Hostile) Fx.Pop(GetParent(), f.GlobalPosition + new Vector3(0f, f.BodyHeight + 0.4f, 0f), "BOING!", new Color(0.55f, 0.8f, 1f), 0.8f, 0.6f);
        }
    }
}

/// <summary>
/// A circus cannon. Every few seconds it swings its warning down the walkway,
/// then fires a cannonball the length of it: anyone in the lane is flattened.
/// </summary>
public partial class Cannon : StageFeature
{
    private const float Period = 6.5f, Warn = 1.4f, Reach = 13f;
    private float _dir = 1f;
    private Node3D _barrel = null!;
    private Telegraph? _warn;
    private float _kick;
    private int _lastCycle = -1;

    public override void _Ready()
    {
        _dir = Phase < 0.5f ? 1f : -1f;
        var wood = Mat(new Color(0.55f, 0.32f, 0.16f), 0.8f);
        Add(new BoxMesh { Size = new Vector3(1.1f, 0.35f, 0.7f) }, wood, new Vector3(0f, 0.38f, 0f));
        foreach (float z in new[] { -0.4f, 0.4f })
        {
            Add(Cyl(0.38f, 0.1f), Mat(new Color(0.85f, 0.2f, 0.18f), 0.5f), new Vector3(-0.15f * _dir, 0.38f, z), new Vector3(90f, 0f, 0f));
            Add(Cyl(0.1f, 0.12f), Mat(new Color(0.95f, 0.8f, 0.25f), 0.3f, 0.6f), new Vector3(-0.15f * _dir, 0.38f, z), new Vector3(90f, 0f, 0f));
        }
        _barrel = new Node3D { Position = new Vector3(0f, 0.75f, 0f), RotationDegrees = new Vector3(0f, 0f, _dir * -12f) };
        AddChild(_barrel);
        Add(Cyl(0.26f, 1.5f, 0.2f), Mat(new Color(0.18f, 0.18f, 0.22f), 0.35f, 0.7f), new Vector3(0.35f * _dir, 0f, 0f), new Vector3(0f, 0f, -90f * _dir), _barrel);
        foreach (float x in new[] { -0.15f, 0.45f, 1.0f })
            Add(Cyl(0.28f, 0.08f), Mat(new Color(0.95f, 0.75f, 0.2f), 0.3f, 0.7f), new Vector3(x * _dir, 0f, 0f), new Vector3(0f, 0f, 90f), _barrel);
        Add(new SphereMesh { Radius = 0.27f, Height = 0.54f }, Mat(new Color(0.18f, 0.18f, 0.22f), 0.35f, 0.7f), new Vector3(-0.4f * _dir, 0f, 0f), null, _barrel);
        // a star on the carriage
        Add(new SphereMesh { Radius = 0.1f, Height = 0.08f }, Mat(new Color(1f, 0.85f, 0.3f), 0.3f, 0.5f), new Vector3(0f, 0.4f, 0.36f));
    }

    private Vector3 Muzzle => GlobalPosition + new Vector3(_dir * 1.1f, 0.6f, 0f);

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        float dt = (float)delta;
        _kick = Mathf.MoveToward(_kick, 0f, dt * 2.5f);
        _barrel.Position = new Vector3(-_dir * 0.25f * _kick, 0.75f, 0f);
        float period = Period * Pace;
        float t = Clock + Phase * period;
        int cycle = (int)(t / period);
        float inCycle = t - cycle * period;
        if (cycle != _lastCycle && inCycle < Warn && Watched())
        {
            _lastCycle = cycle;
            _warn = Telegraph.Lane(GetParent(), new Vector3(Muzzle.X, GlobalPosition.Y, GlobalPosition.Z), new Vector3(_dir, 0f, 0f), Reach, 1.1f, 0f, new Color(1f, 0.3f, 0.2f));
            Fx.Sound("fizz", 0.8f);
        }
        if (_warn is not null)
        {
            _warn.Urgency = Mathf.Clamp(inCycle / Warn, 0f, 1f);
            if (inCycle >= Warn)
            {
                _warn.Clear();
                _warn = null;
                Fire();
            }
        }
    }

    private void Fire()
    {
        _kick = 1f;
        Fx.Sound("kaboom", 1.1f);
        Fx.Shake(this, 0.12f, 0.25f);
        Fx.Impact(GetParent(), Muzzle, 1.2f);
        var ball = new Cannonball { Dir = _dir, Reach = Reach, Severity = Severity };
        GetParent().AddChild(ball);
        ball.GlobalPosition = Muzzle;
    }
}

/// <summary>The cannon's shot: rolls the lane at speed and bowls over everyone in it, once each.</summary>
public partial class Cannonball : Node3D
{
    public override void _Notification(int what) => NetRegistry.Notify(this, what);

    public float Dir = 1f, Reach = 13f, Severity = 1f;
    private const float Speed = 17f;
    private float _gone;
    private readonly HashSet<Fighter> _hit = new();

    public override void _Ready() =>
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.32f, Height = 0.64f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.15f), Roughness = 0.3f, Metallic = 0.7f },
        });

    public override void _PhysicsProcess(double delta)
    {
        float step = Speed * (float)delta;
        _gone += step;
        GlobalPosition += new Vector3(Dir * step, 0f, 0f);
        RotationDegrees = new Vector3(0f, 0f, RotationDegrees.Z - Dir * step * 180f);
        if (_gone > Reach) { Fx.Impact(GetParent(), GlobalPosition, 0.7f, glow: false); QueueFree(); return; }
        foreach (Fighter f in Fighter.Live)
        {
            if (f.IsDead || _hit.Contains(f) || !f.CanBeHit()) continue;
            Vector3 d = f.GlobalPosition - GlobalPosition;
            if (Mathf.Abs(d.X) > 0.6f || Mathf.Abs(d.Z) > 0.6f || d.Y > 0.5f || d.Y < -f.BodyHeight) continue;
            _hit.Add(f);
            f.TakeDamage(new DamageInfo(StageFeature.ShareOf(f, 0.12f * Severity), new Vector3(Dir, 0f, 0f), 9f, 0.08f, null, 6f, 0.4f));
            Fx.Sound("bonk");
            Fx.Pop(GetParent(), f.GlobalPosition + new Vector3(0f, f.BodyHeight + 0.3f, 0f), "KABOOM!", new Color(1f, 0.5f, 0.3f), 0.9f, 0.6f);
        }
    }
}

/// <summary>
/// A giant mallet hanging over the walkway on a rope. It hauls itself up, its
/// circle reddens on the floor, and it slams: everyone under it is flattened
/// and thrown.
/// </summary>
public partial class GiantMallet : StageFeature
{
    private const float Period = 5.5f, Warn = 1.3f, Radius = 1.25f, Rest = 4.6f;
    private Node3D _head = null!;
    private MeshInstance3D _rope = null!;
    private Telegraph? _warn;
    private int _lastCycle = -1;
    private float _y = Rest;
    private bool _slammed;

    public override void _Ready()
    {
        _head = new Node3D { Position = new Vector3(0f, Rest, 0f) };
        AddChild(_head);
        // a barrel of a head, red and white with gold bands, and a handle up into the rope
        Add(Cyl(0.6f, 1.7f), Mat(new Color(0.9f, 0.18f, 0.18f), 0.45f), Vector3.Zero, new Vector3(0f, 0f, 90f), _head);
        foreach (float x in new[] { -0.84f, 0.84f })
            Add(Cyl(0.62f, 0.12f), Mat(new Color(1f, 0.95f, 0.88f), 0.5f), new Vector3(x, 0f, 0f), new Vector3(0f, 0f, 90f), _head);
        foreach (float x in new[] { -0.4f, 0.4f })
            Add(Cyl(0.63f, 0.08f), Mat(new Color(0.95f, 0.75f, 0.2f), 0.3f, 0.6f), new Vector3(x, 0f, 0f), new Vector3(0f, 0f, 90f), _head);
        Add(Cyl(0.11f, 1.6f), Mat(new Color(0.6f, 0.4f, 0.2f), 0.8f), new Vector3(0f, 1.3f, 0f), null, _head);
        _rope = Add(Cyl(0.035f, 1f, -1f, 6), Mat(new Color(0.75f, 0.65f, 0.45f), 0.9f), Vector3.Zero);
        UpdateRope();
    }

    public override bool Covers(Vector3 p) => _y < 1.5f && Flat(p - GlobalPosition).Length() < Radius + 0.3f;

    private void UpdateRope()
    {
        float bottom = _y + 2.1f, top = 12f;
        _rope.Position = new Vector3(0f, (bottom + top) * 0.5f, 0f);
        _rope.Scale = new Vector3(1f, top - bottom, 1f);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        float period = Period * Pace;
        float t = Clock + Phase * period;
        int cycle = (int)(t / period);
        float c = t - cycle * period;
        if (cycle != _lastCycle && c < Warn && Watched())
        {
            _lastCycle = cycle;
            _slammed = false;
            _warn = Telegraph.Circle(GetParent(), GlobalPosition, Radius, 0f, new Color(1f, 0.25f, 0.2f));
            Fx.Sound("whirr", 0.7f);
        }
        bool active = _lastCycle == cycle;
        if (!active || c > Warn + 1.9f)
        {
            // resting high, swaying a little
            _y = Mathf.MoveToward(_y, Rest, (float)delta * 2.5f);
            _head.Rotation = new Vector3(0f, 0f, Mathf.Sin(Clock * 1.3f) * 0.05f);
        }
        else if (c < Warn)
        {
            if (_warn is not null) _warn.Urgency = c / Warn;
            _y = Rest + 1.2f * Mathf.SmoothStep(0f, 1f, c / Warn);      // hauled up
            _head.Rotation = new Vector3(0f, 0f, Mathf.Sin(c * 30f) * 0.04f * (c / Warn));
        }
        else
        {
            _y = Mathf.MoveToward(_y, 0.6f, (float)delta * 55f);           // down it comes
            if (!_slammed && _y <= 0.61f) Slam();
            if (c > Warn + 0.9f) _y = Mathf.Lerp(0.6f, Rest, Mathf.Clamp((c - Warn - 0.9f) / 1f, 0f, 1f));
        }
        _head.Position = new Vector3(0f, _y, 0f);
        UpdateRope();
    }

    private void Slam()
    {
        _slammed = true;
        _warn?.Clear();
        _warn = null;
        Fx.Sound("thump", 0.6f);
        Fx.Sound("bonk", 0.7f);
        Fx.Shake(this, 0.22f, 0.3f);
        Fx.Impact(GetParent(), GlobalPosition + new Vector3(0f, 0.2f, 0f), 1.6f, glow: false);
        foreach (Fighter f in Fighter.Live)
        {
            if (f.IsDead || !f.CanBeHit()) continue;
            Vector3 d = Flat(f.GlobalPosition - GlobalPosition);
            if (d.Length() > Radius || f.GlobalPosition.Y - GlobalPosition.Y > 1.6f) continue;
            Vector3 away = d.LengthSquared() > 0.01f ? d.Normalized() : new Vector3(1f, 0f, 0f);
            f.TakeDamage(new DamageInfo(Share(f, 0.15f), away, 6f, 0.12f, null, 7f, 0.5f));
            Fx.Pop(GetParent(), f.GlobalPosition + new Vector3(0f, f.BodyHeight + 0.3f, 0f), "BONK!", new Color(1f, 0.85f, 0.3f), 1f, 0.6f);
        }
    }
}

/// <summary>
/// A grate in the floor that breathes fire: embers and a warning circle, then
/// a roaring column that burns whoever stands in it, then quiet again.
/// </summary>
public partial class FireJet : StageFeature
{
    private const float Period = 4.6f, Warn = 0.9f, Burn = 1.6f, Radius = 0.9f;
    private readonly List<MeshInstance3D> _flames = new();
    private OmniLight3D _light = null!;
    private Telegraph? _warn;
    private int _lastCycle = -1;
    private float _c;

    public override void _Ready()
    {
        var iron = Mat(new Color(0.2f, 0.18f, 0.2f), 0.5f, 0.7f);
        Add(Cyl(Radius, 0.06f), iron, new Vector3(0f, 0.03f, 0f));
        Add(Cyl(Radius * 0.75f, 0.07f), Mat(new Color(0.35f, 0.08f, 0.02f), 0.8f, 0f, 0.8f), new Vector3(0f, 0.035f, 0f));
        for (int i = -2; i <= 2; i++)
            Add(new BoxMesh { Size = new Vector3(0.08f, 0.09f, Radius * 1.5f) }, iron, new Vector3(i * 0.26f, 0.05f, 0f));
        Color[] tones = { new(1f, 0.85f, 0.3f), new(1f, 0.5f, 0.12f), new(1f, 0.3f, 0.08f) };
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.Tau / 5f;
            float r = i == 0 ? 0f : 0.42f;
            var m = Add(Cyl(i == 0 ? 0.42f : 0.3f, 1f, 0f, 10), Mat(tones[i % 3] with { A = 0.85f }, 1f, 0f, 2.5f, unshaded: true),
                new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * 0.7f));
            m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            m.Visible = false;
            _flames.Add(m);
        }
        _light = new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 0f, OmniRange = 4f, Position = new Vector3(0f, 1f, 0f) };
        AddChild(_light);
    }

    public override bool Covers(Vector3 p) => _c >= Warn && _c < Warn + Burn && Flat(p - GlobalPosition).Length() < Radius + 0.3f;

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        float period = Period * Pace;
        float t = Clock + Phase * period;
        int cycle = (int)(t / period);
        float c = t - cycle * period;
        if (cycle != _lastCycle && c < Warn && Watched())
        {
            _lastCycle = cycle;
            _warn = Telegraph.Circle(GetParent(), GlobalPosition, Radius, 0f, new Color(1f, 0.55f, 0.1f));
            Fx.Sound("fizz", 0.6f, -4f);
        }
        _c = _lastCycle == cycle ? c : period;
        float height = 0f;
        if (_c < Warn)
        {
            if (_warn is not null) _warn.Urgency = _c / Warn;
            height = 0.25f * (0.6f + 0.4f * Mathf.Sin(Clock * 40f));      // licks of flame
        }
        else if (_c < Warn + Burn)
        {
            if (_warn is not null) { _warn.Clear(); _warn = null; Fx.Sound("fire_roar", 1f, -2f); }
            float k = Mathf.Min(1f, (_c - Warn) * 8f) * Mathf.Min(1f, (Warn + Burn - _c) * 5f);
            height = 2.6f * k;
            Scorch();
        }
        for (int i = 0; i < _flames.Count; i++)
        {
            float h = height * (i == 0 ? 1f : 0.7f) * (0.85f + 0.15f * Mathf.Sin(Clock * 23f + i * 1.7f));
            _flames[i].Visible = h > 0.05f;
            _flames[i].Scale = new Vector3(1f, Mathf.Max(0.01f, h), 1f);
            _flames[i].Position = new Vector3(_flames[i].Position.X, h * 0.5f, _flames[i].Position.Z);
        }
        _light.LightEnergy = height * 1.3f;
    }

    private void Scorch()
    {
        foreach (Fighter f in Fighter.Live)
        {
            if (f.IsDead || !f.CanBeHit()) continue;
            Vector3 d = Flat(f.GlobalPosition - GlobalPosition);
            if (d.Length() > Radius || f.GlobalPosition.Y - GlobalPosition.Y > 2.2f || !Due(f, 0.5f)) continue;
            Vector3 away = d.LengthSquared() > 0.01f ? d.Normalized() : new Vector3(1f, 0f, 0f);
            f.TakeDamage(new DamageInfo(Share(f, 0.05f), away, 3f, 0.03f, null, 3.5f, 0.25f));
            Fx.Pop(GetParent(), f.GlobalPosition + new Vector3(0f, f.BodyHeight + 0.3f, 0f), "HOT!", new Color(1f, 0.55f, 0.2f), 0.7f, 0.5f);
        }
    }
}

/// <summary>
/// A slippery patch: soap suds, syrup, mud, oil or ice by world. Anyone who
/// runs across it goes over on their back, enemies included: lure them in.
/// Walk it carefully, or jump it.
/// </summary>
public partial class SlickPatch : StageFeature
{
    private const float Radius = 1.35f;

    public override bool Lasting => true;

    private static readonly (Color c, string word)[] Kinds =
    {
        (new Color(0.85f, 0.93f, 1f, 0.9f), "SOAP!"),        // 1 big top
        (new Color(1f, 0.55f, 0.75f, 0.9f), "SYRUP!"),       // 2 candy
        (new Color(0.35f, 0.27f, 0.15f, 0.92f), "MUD!"),     // 3 forest
        (new Color(0.12f, 0.1f, 0.14f, 0.9f), "OIL!"),       // 4
        (new Color(0.85f, 0.93f, 1f, 0.9f), "SOAP!"),        // 5 mirrors
        (new Color(0.7f, 0.9f, 1f, 0.85f), "ICE!"),          // 6 peaks
        (new Color(0.12f, 0.1f, 0.14f, 0.9f), "OIL!"),       // 7 desert
        (new Color(0.6f, 0.85f, 1f, 0.85f), "ICE!"),         // 8 caverns
        (new Color(0.12f, 0.1f, 0.14f, 0.9f), "OIL!"),       // 9 fortress
        (new Color(0.85f, 0.93f, 1f, 0.9f), "SOAP!"),        // 10
    };

    public override void _Ready()
    {
        (Color c, string _) = Kinds[World - 1];
        var paint = Mat(c, c.R + c.G + c.B > 2f ? 0.05f : 0.15f, 0.1f, 0.25f);
        Add(Cyl(Radius, 0.03f, -1f, 24), paint, new Vector3(0f, 0.02f, 0f)).Scale = new Vector3(1f, 1f, 0.6f);
        var rng = new RandomNumberGenerator { Seed = (ulong)(Phase * 100000f) + 7UL };
        for (int i = 0; i < 6; i++)
        {
            float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(0.3f, Radius * 0.9f), r = rng.RandfRange(0.12f, 0.28f);
            Add(new SphereMesh { Radius = r, Height = r * 0.6f, RadialSegments = 10, Rings = 5 }, paint, new Vector3(Mathf.Cos(a) * d, 0.03f, Mathf.Sin(a) * d * 0.55f));
        }
        // soap gets a few bubbles; the big top's gets a banana peel too
        if (Kinds[World - 1].word == "SOAP!")
            for (int i = 0; i < 5; i++)
            {
                float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(0.2f, Radius * 0.8f);
                Add(new SphereMesh { Radius = 0.1f, Height = 0.2f }, Mat(new Color(0.9f, 0.95f, 1f, 0.5f), 0.05f, 0.3f), new Vector3(Mathf.Cos(a) * d, 0.12f, Mathf.Sin(a) * d * 0.55f));
            }
        if (World == 1)
            for (int i = 0; i < 3; i++)
                Add(Cyl(0.06f, 0.4f, 0.03f, 6), Mat(new Color(1f, 0.85f, 0.2f), 0.6f), new Vector3(0.3f - i * 0.3f, 0.05f, 0.1f * i), new Vector3(80f, i * 120f, 0f));
    }

    public override bool Covers(Vector3 p)
    {
        Vector3 d = p - GlobalPosition;
        return new Vector2(d.X, d.Z / 0.6f).Length() < Radius + 0.3f;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        foreach (Fighter f in Fighter.Live)
        {
            if (f.IsDead || Prop(f) || !f.Grounded || f.Down || !f.CanBeHit()) continue;
            if (Flat(f.Velocity).Length() < 3.2f) continue;              // walking it carefully is fine
            Vector3 d = f.GlobalPosition - GlobalPosition;
            if (new Vector2(d.X, d.Z / 0.6f).Length() > Radius || Mathf.Abs(d.Y) > 0.3f || !Due(f, 2f)) continue;
            Vector3 dir = Flat(f.Velocity).Normalized();
            f.TakeDamage(new DamageInfo(Share(f, 0.02f), dir, 3.5f, 0.03f, null, 3.8f, 0.6f));
            Fx.Pop(GetParent(), f.GlobalPosition + new Vector3(0f, f.BodyHeight + 0.3f, 0f), Kinds[World - 1].word, new Color(1f, 0.97f, 0.85f), 0.8f, 0.6f);
            Fx.Sound("slide_up", 1.4f, -4f);
        }
    }
}
