namespace ClownSquad.Combat;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Net;
using ClownSquad.World;

/// <summary>
/// What the big cream pie leaves behind: a slick of cream on the floor. Any
/// enemy who walks into it slips and lands on their back (a small hop, then
/// floored), at most once every second or so, until it dries up. The squad
/// walks through it safely.
/// </summary>
public partial class CreamPuddle : Node3D
{
    private PlayerFighter _owner = null!;
    private float _radius;
    private float _left;
    private float _life;
    private StandardMaterial3D _paint = null!;
    private readonly Dictionary<Fighter, double> _slipped = new();

    // Numbered for the network (NetRegistry) while in the scene.
    public override void _Notification(int what) => NetRegistry.Notify(this, what);

    public static readonly Color Cream = new(1f, 0.97f, 0.88f);
    /// <summary>The seltzer bottle's puddles: soda water, just as slippery.</summary>
    public static readonly Color Soda = new(0.62f, 0.86f, 1f);
    private Color _color = Cream;

    public static void Spawn(Node parent, PlayerFighter owner, Vector3 at, float radius, float seconds, Color? color = null)
    {
        var p = new CreamPuddle { _owner = owner, _radius = radius, _left = seconds, _life = seconds, _color = color ?? Cream };
        parent.AddChild(p);
        float floor = ClownSquad.World.Terrain.Current?.HeightAt(at) ?? 0f;
        p.GlobalPosition = new Vector3(at.X, floor + 0.02f, at.Z);
    }

    public override void _Ready()
    {
        _paint = new StandardMaterial3D
        {
            AlbedoColor = _color, Roughness = _color == Cream ? 0.25f : 0.05f, Metallic = 0f,
            EmissionEnabled = true, Emission = _color * 0.3f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        // A flat, slightly lumpy splat: one wide disc and a few blobs.
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = _radius, BottomRadius = _radius, Height = 0.04f, RadialSegments = 20 }, MaterialOverride = _paint });
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        for (int i = 0; i < 7; i++)
        {
            float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(0.2f, _radius * 0.95f), r = rng.RandfRange(0.12f, 0.3f);
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = r, Height = r * 0.7f, RadialSegments = 10, Rings = 5 },
                MaterialOverride = _paint,
                Position = new Vector3(Mathf.Cos(a) * d, 0.04f, Mathf.Sin(a) * d * 0.6f),
            });
        }
        Fx.Sound(_color == Cream ? "splat" : "splash", 0.8f);
    }

    public override void _PhysicsProcess(double delta)
    {
        _left -= (float)delta;
        if (_left <= 0f || !IsInstanceValid(_owner)) { QueueFree(); return; }
        // Dries up over the last second.
        _paint.AlbedoColor = new Color(_color.R, _color.G, _color.B, Mathf.Clamp(_left, 0f, 1f) * (_color == Cream ? 1f : 0.8f));

        foreach (Fighter f in Fighter.Live)
        {
            if (!f.Hostile || f.IsDead || !f.Grounded || f.Down || !f.CanBeHit()) continue;
            Vector3 d = f.GlobalPosition - GlobalPosition;
            if (new Vector2(d.X, d.Z / 0.6f).Length() > _radius) continue;       // the splat is an oval on the floor
            if (_slipped.TryGetValue(f, out double at) && GameClock.Seconds - at < 1.4) continue;
            _slipped[f] = GameClock.Seconds;
            // Feet out from under them: a little hop, flat on the back.
            Vector3 dir = new(f.Velocity.X >= 0f ? 1f : -1f, 0f, 0f);
            f.TakeDamage(new DamageInfo(3f * _owner.DamageMultiplier, dir, 2.5f, 0.03f, _owner, 3.8f, 0.6f));
            Fx.Pop(GetParent(), f.GlobalPosition + new Vector3(0f, f.BodyHeight + 0.3f, 0f), "SLIP!", new Color(1f, 0.97f, 0.85f), 0.8f, 0.6f);
            Fx.Sound("slide_up", 1.4f, -4f);
        }
    }
}
