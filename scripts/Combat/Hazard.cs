namespace ClownSquad.Combat;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Visuals;

/// <summary>
/// Broken glass on the floor: a scatter of glinting shards that cut whoever
/// walks over them (jumping clears them), until they are swept up by time or
/// kicked back at you by the one who dropped them. Every live field is listed
/// so a kicker can find the glass near its feet.
/// </summary>
public partial class GlassField : Node3D
{
    public static readonly List<GlassField> All = new();
    public PlayerFighter Owner = null!;
    public float Damage = 5f;
    public float Seconds = 9f;
    private readonly List<Node3D> _shards = new();
    private readonly Dictionary<Fighter, float> _next = new();
    private float _age;

    public int Count => _shards.Count;

    public static GlassField Scatter(PlayerFighter owner, Vector3 at, float radius, int count, float seconds = 9f)
    {
        var g = new GlassField { Owner = owner, Seconds = seconds };
        owner.GetParent().AddChild(g);
        g.GlobalPosition = new Vector3(at.X, 0f, at.Z);
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        for (int i = 0; i < count; i++)
        {
            float a = rng.Randf() * Mathf.Tau, r = Mathf.Sqrt(rng.Randf()) * radius;
            Node3D s = EnemyShot.Shard(rng.RandfRange(1.3f, 1.9f));
            s.Position = new Vector3(Mathf.Cos(a) * r, 0.04f, Mathf.Sin(a) * r * 0.8f);
            s.RotationDegrees = new Vector3(rng.RandfRange(60f, 90f), rng.RandfRange(0f, 360f), 0f);
            g.AddChild(s);
            g._shards.Add(s);
        }
        return g;
    }

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    /// <summary>Take up to `n` shards nearest a point, removing them from the floor. Returns where they lay.</summary>
    public List<Vector3> Take(Vector3 near, int n)
    {
        _shards.Sort((a, b) => a.GlobalPosition.DistanceSquaredTo(near).CompareTo(b.GlobalPosition.DistanceSquaredTo(near)));
        var took = new List<Vector3>();
        while (took.Count < n && _shards.Count > 0)
        {
            took.Add(_shards[0].GlobalPosition);
            _shards[0].QueueFree();
            _shards.RemoveAt(0);
        }
        if (_shards.Count == 0) QueueFree();
        return took;
    }

    public override void _PhysicsProcess(double delta)
    {
        _age += (float)delta;
        // A glint now and then, so the glass is seen before it is stepped in.
        foreach (Node3D s in _shards) s.Scale = Vector3.One * (1f + 0.15f * Mathf.Max(0f, Mathf.Sin(_age * 5f + s.Position.X * 7f)));
        if (_age > Seconds)
        {
            foreach (Node3D s in _shards) s.Scale *= Mathf.Max(0f, 1f - (_age - Seconds));
            if (_age > Seconds + 1f) QueueFree();
            return;
        }
        if (!IsInstanceValid(Owner)) return;
        foreach (Node n in GetTree().GetNodesInGroup(Fighter.Group))
        {
            if (n is not Fighter f || f.Hostile || f.IsDead || !f.CanBeHit() || f.GlobalPosition.Y > 0.3f) continue;
            if (_next.TryGetValue(f, out float when) && _age < when) continue;
            foreach (Node3D s in _shards)
            {
                Vector3 d = f.GlobalPosition - s.GlobalPosition;
                d.Y = 0f;
                if (d.Length() > 0.4f) continue;
                _next[f] = _age + 0.7f;
                f.TakeDamage(new DamageInfo(Damage * Owner.DamageMultiplier, d.LengthSquared() > 0.01f ? d.Normalized() : Vector3.Right, 2f, 0.04f, Owner, 0f, 0.35f));
                ClownSquad.Audio.Sfx.Play("ding", 2.2f, -6f);
                PopText.Spawn(GetParent(), f.GlobalPosition + new Vector3(0f, f.BodyHeight + 0.2f, 0f), "OUCH!", new Color(0.7f, 0.9f, 1f), 0.6f, 0.5f);
                break;
            }
        }
    }
}

/// <summary>
/// A patch of floor that hurts while it lasts: burning oil after a torch
/// lands, the fire eater's ring of flame, the tiger's flaming hoop. It warms
/// up for a moment (a telegraph), then burns anyone standing in it every
/// half second until it goes out.
/// </summary>
public partial class Hazard : Node3D
{
    public PlayerFighter Owner = null!;
    public float Radius = 1f;
    public float Damage = 4f;
    public float Seconds = 4f;
    public float WarmUp = 0.6f;
    /// <summary>A ring hazard burns only between Radius - Thickness and Radius.</summary>
    public float Thickness;
    /// <summary>Grows the radius per second (an expanding ring of fire).</summary>
    public float Grow;

    private readonly Dictionary<Fighter, float> _next = new();
    private float _age;
    private GpuParticles3D _fire = null!;
    private OmniLight3D _light = null!;
    private Telegraph? _warn;

    public static Hazard Fire(PlayerFighter owner, Vector3 at, float radius, float seconds, float damage, float warmUp = 0.6f,
                              float thickness = 0f, float grow = 0f)
    {
        var h = new Hazard { Owner = owner, Radius = radius, Seconds = seconds, Damage = damage, WarmUp = warmUp, Thickness = thickness, Grow = grow };
        owner.GetParent().AddChild(h);
        h.GlobalPosition = new Vector3(at.X, 0f, at.Z);
        return h;
    }

    public override void _Ready()
    {
        _warn = Telegraph.Circle(GetParent(), GlobalPosition, Radius, 0f, new Color(1f, 0.55f, 0.1f));
        var ramp = new Gradient { Offsets = new[] { 0f, 0.5f, 1f }, Colors = new[] { new Color(1f, 0.95f, 0.5f), new Color(1f, 0.45f, 0.1f), new Color(0.4f, 0.1f, 0.05f, 0f) } };
        _fire = new GpuParticles3D
        {
            Amount = Mathf.Clamp((int)(Radius * Radius * 40f), 16, 220),
            Lifetime = 0.8f,
            Emitting = false,
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = Thickness > 0f ? ParticleProcessMaterial.EmissionShapeEnum.Ring : ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = Radius * 0.8f,
                EmissionRingRadius = Radius, EmissionRingInnerRadius = Mathf.Max(0f, Radius - Thickness), EmissionRingHeight = 0.05f,
                EmissionRingAxis = Vector3.Up,
                Gravity = new Vector3(0f, 2.2f, 0f),
                InitialVelocityMin = 0.2f, InitialVelocityMax = 0.8f,
                ScaleMin = 0.6f, ScaleMax = 1.4f,
                ColorRamp = new GradientTexture1D { Gradient = ramp },
            },
            DrawPass1 = new QuadMesh
            {
                Size = new Vector2(0.28f, 0.36f),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                },
            },
            Scale = new Vector3(1f, 1f, 0.6f),
        };
        AddChild(_fire);
        _light = new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 0f, OmniRange = Radius + 2.5f, Position = new Vector3(0f, 0.6f, 0f) };
        AddChild(_light);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (_warn is not null) _warn.Urgency = Mathf.Clamp(_age / Mathf.Max(0.01f, WarmUp), 0f, 1f);
        if (_age < WarmUp) return;
        if (_warn is not null) { _warn.Clear(); _warn = null; _fire.Emitting = true; }
        if (Grow > 0f)
        {
            Radius += Grow * dt;
            if (_fire.ProcessMaterial is ParticleProcessMaterial pm) { pm.EmissionRingRadius = Radius; pm.EmissionRingInnerRadius = Mathf.Max(0f, Radius - Thickness); }
        }
        float left = WarmUp + Seconds - _age;
        _light.LightEnergy = Mathf.Clamp(left, 0f, 1f) * (2f + 0.4f * Mathf.Sin(_age * 17f));
        if (left <= 0f)
        {
            _fire.Emitting = false;
            if (left < -1f) QueueFree();
            return;
        }
        if (!IsInstanceValid(Owner)) return;
        foreach (Node n in GetTree().GetNodesInGroup(Fighter.Group))
        {
            if (n is not Fighter f || f.Hostile || f.IsDead || !f.CanBeHit() || f.GlobalPosition.Y > 0.6f) continue;
            Vector3 d = f.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            float dist = d.Length();
            if (dist > Radius || (Thickness > 0f && dist < Radius - Thickness)) continue;
            if (_next.TryGetValue(f, out float when) && _age < when) continue;
            _next[f] = _age + 0.5f;
            f.TakeDamage(new DamageInfo(Damage * Owner.DamageMultiplier, d.LengthSquared() > 0.01f ? d.Normalized() : Vector3.Right, 3f, 0.03f, Owner, 2.5f, 0.25f));
        }
    }
}
