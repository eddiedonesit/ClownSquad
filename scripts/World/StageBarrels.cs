namespace ClownSquad.World;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;
using ClownSquad.Net;

/// <summary>
/// A spill of something toxic: green, bubbling, and it eats at whoever stands
/// in it (no knockdown, just a steady sting). Laid on the stage, or left by a
/// poison barrel when it bursts (then it dries up after a while).
/// </summary>
public partial class PoisonSpill : StageFeature
{
    public float Radius = 1.3f;
    /// <summary>How long it lasts; 0 for a spill that is part of the stage.</summary>
    public float Seconds;
    private readonly List<MeshInstance3D> _bubbles = new();

    public override bool Lasting => Seconds <= 0f;

    public static PoisonSpill Leave(Node parent, Vector3 at, int world, float radius, float seconds)
    {
        var s = new PoisonSpill { Radius = radius, Seconds = seconds };
        s.Setup(world, Mathf.PosMod(at.X * 0.37f, 1f));
        parent.AddChild(s);
        s.GlobalPosition = new Vector3(at.X, Terrain.Current?.HeightAt(at) ?? 0f, at.Z);
        return s;
    }

    public override void _Ready()
    {
        var paint = Mat(new Color(0.45f, 0.95f, 0.2f, 0.85f), 0.1f, 0f, 0.8f);
        Add(Cyl(Radius, 0.03f, -1f, 24), paint, new Vector3(0f, 0.025f, 0f)).Scale = new Vector3(1f, 1f, 0.6f);
        var rng = new RandomNumberGenerator { Seed = (ulong)(Phase * 100000f) + 11UL };
        for (int i = 0; i < 5; i++)
        {
            float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(0.3f, Radius * 0.9f), r = rng.RandfRange(0.15f, 0.3f);
            Add(new SphereMesh { Radius = r, Height = r * 0.5f, RadialSegments = 10, Rings = 5 }, paint, new Vector3(Mathf.Cos(a) * d, 0.03f, Mathf.Sin(a) * d * 0.55f));
        }
        var bubble = Mat(new Color(0.7f, 1f, 0.4f, 0.75f), 0.05f, 0f, 1.2f);
        for (int i = 0; i < 6; i++)
        {
            float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(0.1f, Radius * 0.75f);
            _bubbles.Add(Add(new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 8, Rings = 4 }, bubble, new Vector3(Mathf.Cos(a) * d, 0.05f, Mathf.Sin(a) * d * 0.55f)));
        }
        AddChild(new OmniLight3D { LightColor = new Color(0.5f, 1f, 0.3f), LightEnergy = 0.6f, OmniRange = Radius + 1.2f, Position = new Vector3(0f, 0.5f, 0f) });
    }

    public override bool Covers(Vector3 p)
    {
        Vector3 d = p - GlobalPosition;
        return new Vector2(d.X, d.Z / 0.6f).Length() < Radius + 0.3f;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        // bubbles swell and pop
        for (int i = 0; i < _bubbles.Count; i++)
        {
            float k = Mathf.PosMod(Clock * 0.8f + i * 0.37f, 1f);
            _bubbles[i].Scale = Vector3.One * (k < 0.9f ? k * 1.4f : 0.01f);
        }
        if (Seconds > 0f)
        {
            float left = Seconds - Clock;
            if (left <= 0f) { QueueFree(); return; }
            Scale = Vector3.One * Mathf.Clamp(left, 0.2f, 1f);
        }
        foreach (Fighter f in Fighter.Live)
        {
            if (f.IsDead || Prop(f) || !f.CanBeHit()) continue;
            Vector3 d = f.GlobalPosition - GlobalPosition;
            if (d.Y > 0.3f || new Vector2(d.X, d.Z / 0.6f).Length() > Radius || !Due(f, 0.6f)) continue;
            f.TakeDamage(new DamageInfo(Share(f, 0.03f), Vector3.Zero, 0f, 0f, null, 0f, 0.05f));
            Fx.Pop(GetParent(), f.GlobalPosition + new Vector3(0f, f.BodyHeight + 0.3f, 0f), "TOXIC!", new Color(0.55f, 1f, 0.3f), 0.6f, 0.5f);
        }
    }
}

/// <summary>
/// A barrel on the walkway, smashed like a crate. A red TNT barrel lights its
/// fuse and blows up everyone near it (clowns too: knock enemies into its
/// range, then hit it); a green poison barrel bursts into a toxic spill.
/// Blasts, cannonballs and the mallet set them off as well, so they chain.
/// </summary>
public partial class HazardBarrel : Fighter
{
    private readonly bool _explosive;
    private readonly int _world;
    private Node3D? _body;
    private float _wobble;

    public HazardBarrel() : this(true, 1) { }
    public HazardBarrel(bool explosive, int world) { _explosive = explosive; _world = world; }

    public bool Explosive => _explosive;

    protected override string SheetPath => "res://assets/stages/crate.png";
    protected override float SpritePixelSize => 0.01125f;
    public override bool Hostile => true;
    public override float BodyHeight => 1.0f;
    public override float BodyHalfWidth => 0.42f;
    protected override float StunScale => 0f;
    protected override float LaunchScale => 0f;
    public override string HurtSound => "clang";
    public override string DeathSound => "pop";

    public override void _Ready()
    {
        DisplayName = _explosive ? "TNT BARREL" : "POISON BARREL";
        MaxHealth = 14f;
        base._Ready();
        Visual.Visible = false;              // drawn as a barrel instead of the crate sprite
        _body = new Node3D();
        AddChild(_body);
        Color main = _explosive ? new Color(0.85f, 0.15f, 0.12f) : new Color(0.25f, 0.6f, 0.2f);
        Color band = _explosive ? new Color(0.98f, 0.85f, 0.25f) : new Color(0.15f, 0.15f, 0.18f);
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.95f, RadialSegments = 16 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = main, Roughness = 0.45f, Metallic = 0.3f },
            Position = new Vector3(0f, 0.48f, 0f),
        });
        foreach (float y in new[] { 0.14f, 0.82f })
            _body.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.375f, BottomRadius = 0.375f, Height = 0.08f, RadialSegments = 16 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = band, Roughness = 0.4f, Metallic = 0.5f },
                Position = new Vector3(0f, y, 0f),
            });
        _body.AddChild(new Label3D
        {
            Text = _explosive ? "TNT" : "TOXIC", FontSize = _explosive ? 64 : 44, OutlineSize = 10, PixelSize = 0.005f,
            Modulate = _explosive ? new Color(1f, 0.95f, 0.6f) : new Color(0.75f, 1f, 0.45f), OutlineModulate = new Color(0.1f, 0.05f, 0.05f),
            Position = new Vector3(0f, 0.5f, 0.37f), Shaded = false,
        });
        if (!_explosive)
            _body.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.2f, Height = 0.12f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 1f, 0.25f), EmissionEnabled = true, Emission = new Color(0.4f, 1f, 0.2f), EmissionEnergyMultiplier = 0.8f },
                Position = new Vector3(0.05f, 0.96f, 0.05f),
            });
        Died += _ =>
        {
            Vector3 at = GlobalPosition;
            if (_explosive) BarrelBlast.Light(GetParent(), at, _world);
            else
            {
                Fx.Sound("splash", 0.8f);
                Fx.Impact(GetParent(), at + new Vector3(0f, 0.4f, 0f), 1.0f, glow: false);
                PoisonSpill.Leave(GetParent(), at, _world, 1.5f, 9f);
            }
            QueueFree();
        };
    }

    public override void TakeDamage(in DamageInfo info)
    {
        base.TakeDamage(info);
        _wobble = 1f;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (_body is null) return;
        _wobble = Mathf.MoveToward(_wobble, 0f, (float)delta * 3f);
        _body.Rotation = new Vector3(0f, 0f, Mathf.Sin(_wobble * 25f) * 0.15f * _wobble);
    }
}

/// <summary>A TNT barrel going off: a swelling, blinking barrel and a warning circle, then the bang.</summary>
public partial class BarrelBlast : StageFeature
{
    private const float Fuse = 0.9f, Radius = 2.3f;
    private Node3D _barrel = null!;
    private Telegraph? _warn;
    private bool _done;

    public static void Light(Node parent, Vector3 at, int world)
    {
        var b = new BarrelBlast();
        b.Setup(world, 0f);
        parent.AddChild(b);
        b.GlobalPosition = at;
    }

    public override bool Covers(Vector3 p) => !_done && Flat(p - GlobalPosition).Length() < Radius + 0.3f;

    public override void _Ready()
    {
        _barrel = new Node3D();
        AddChild(_barrel);
        Add(Cyl(0.36f, 0.95f, -1f, 16), Mat(new Color(0.85f, 0.15f, 0.12f), 0.45f, 0.3f, 0.3f), new Vector3(0f, 0.48f, 0f), null, _barrel);
        Add(Cyl(0.04f, 0.3f, -1f, 6), Mat(new Color(1f, 0.8f, 0.3f), 0.5f, 0f, 3f, unshaded: true), new Vector3(0f, 1.1f, 0f), null, _barrel);
        _warn = Telegraph.Circle(GetParent(), GlobalPosition, Radius, 0f, new Color(1f, 0.35f, 0.1f));
        Fx.Sound("fizz", 1.2f);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (_done) { if (Clock > Fuse + 0.4f) QueueFree(); return; }
        float k = Clock / Fuse;
        if (_warn is not null) _warn.Urgency = Mathf.Clamp(k, 0f, 1f);
        _barrel.Scale = Vector3.One * (1f + 0.25f * k * k);
        _barrel.Visible = (int)(Clock / Mathf.Lerp(0.16f, 0.05f, k)) % 2 == 0 || k > 0.9f;
        if (Clock < Fuse) return;
        _done = true;
        _warn?.Clear();
        _warn = null;
        _barrel.QueueFree();
        Fx.Sound("kaboom");
        Fx.Shake(this, 0.3f, 0.35f);
        Fx.Impact(GetParent(), GlobalPosition + new Vector3(0f, 0.5f, 0f), 2.4f);
        Fx.Pop(GetParent(), GlobalPosition + new Vector3(0f, 1.8f, 0f), "KA-BOOM!", new Color(1f, 0.5f, 0.2f), 1.2f, 0.8f);
        foreach (Fighter f in Fighter.Live)
        {
            if (f.IsDead || !f.CanBeHit()) continue;
            Vector3 d = Flat(f.GlobalPosition - GlobalPosition);
            if (d.Length() > Radius || Mathf.Abs(f.GlobalPosition.Y - GlobalPosition.Y) > 2f) continue;
            Vector3 away = d.LengthSquared() > 0.01f ? d.Normalized() : new Vector3(1f, 0f, 0f);
            // props take a fixed whack (so barrels chain), fighters a share of their health
            float dmg = Prop(f) ? 99f : Share(f, 0.18f);
            f.TakeDamage(new DamageInfo(dmg, away, 8f, 0.1f, null, 7.5f, 0.5f));
        }
    }
}
