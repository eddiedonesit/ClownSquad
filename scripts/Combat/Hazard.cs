namespace ClownSquad.Combat;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Visuals;

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
