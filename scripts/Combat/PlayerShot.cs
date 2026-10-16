namespace ClownSquad.Combat;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Visuals;
using ClownSquad.World;
using ClownSquad.Net;

/// <summary>
/// Anything a player fires that does not come back: corks from the pop gun,
/// juggling pins thrown in a fan, cream pies lobbed onto a crowd. Straight
/// shots hit the first enemy in the lane (or fly through, with Pierce); lobbed
/// shots splat where they land, blinding everyone in the splash. Hits go
/// through the owner, so crits, gear and skills all apply. The look is a small
/// 3D model, like the enemies' shots (EnemyShot).
/// </summary>
public partial class PlayerShot : Node3D
{
    // Numbered for the network (NetRegistry) while in the scene.
    public override void _Notification(int what) => ClownSquad.Net.NetRegistry.Notify(this, what);

    public PlayerFighter Shooter = null!;
    public Vector3 Velocity;
    public float Gravity;
    public float Damage = 8f;
    public float Knockback = 2.5f;
    public float Launch;
    public float Range = 8f;
    public float HitRadius = 0.5f;
    public bool Pierce;
    /// <summary>Splash radius where a lobbed shot lands; zero for straight shots.</summary>
    public float Splat;
    /// <summary>Seconds a splat or hit leaves its victims reeling.</summary>
    public float Blind;
    /// <summary>Bursts in confetti at the end of its flight, hitting everyone near.</summary>
    public bool Burst;
    public float SpinSpeed = 14f;
    /// <summary>Left on the floor where it lands (the big pie's cream).</summary>
    public System.Action<Vector3>? LeavesBehind;
    /// <summary>How far off its lane a shot still hits: the same band as a swing (PlayerFighter.DepthBand), so lining up works the same for both.</summary>
    public float LaneBand = 0.85f;

    private readonly HashSet<Fighter> _hit = new();
    private Node3D _model = null!;
    private float _travelled;
    private float _age;

    /// <summary>A shot straight down the lane.</summary>
    public static PlayerShot Fire(PlayerFighter owner, Node3D model, Vector3 from, Vector3 dir, float speed)
    {
        var s = new PlayerShot { Shooter = owner, Velocity = dir.Normalized() * speed };
        s.Begin(model, from);
        return s;
    }

    /// <summary>A shot lobbed to land on a spot after some seconds.</summary>
    public static PlayerShot Lob(PlayerFighter owner, Node3D model, Vector3 from, Vector3 to, float seconds, float gravity = 20f)
    {
        var s = new PlayerShot { Shooter = owner, Gravity = gravity, Range = 99f };
        Vector3 d = to - from;
        s.Velocity = new Vector3(d.X / seconds, (to.Y - from.Y + 0.5f * gravity * seconds * seconds) / seconds, d.Z / seconds);
        s.Begin(model, from);
        return s;
    }

    private void Begin(Node3D model, Vector3 from)
    {
        _model = model;
        AddChild(model);
        Shooter.GetParent().AddChild(this);
        GlobalPosition = from;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (!IsInstanceValid(Shooter) || _age > 5f) { QueueFree(); return; }

        Velocity += Vector3.Down * Gravity * dt;
        Vector3 step = Velocity * dt;
        GlobalPosition += step;
        _travelled += new Vector2(step.X, step.Z).Length();
        _model.RotateZ(SpinSpeed * dt * (Velocity.X >= 0f ? -1f : 1f));

        foreach (Node n in Fighter.Live)
        {
            if (n is not Fighter f || f.Hostile == Shooter.Hostile || f.IsDead || _hit.Contains(f) || !f.CanBeHit()) continue;
            Vector3 d = f.GlobalPosition + new Vector3(0f, f.BodyHeight * 0.5f, 0f) - GlobalPosition;
            if (Mathf.Abs(d.X) > HitRadius + f.BodyHalfWidth * 0.5f || Mathf.Abs(d.Z) > LaneBand || Mathf.Abs(d.Y) > f.BodyHeight * 0.6f + HitRadius) continue;
            if (Splat > 0f) { Land(); return; }            // a pie splats on whoever it meets first
            _hit.Add(f);
            Vector3 dir = new(Velocity.X >= 0f ? 1f : -1f, 0f, 0f);
            Shooter.StrikeFromProjectile(f, Damage, dir, Knockback, Launch);
            if (Blind > 0f && !f.IsDead) f.Stagger(Blind);
            if (!Pierce) { End(); return; }
        }

        if (Gravity > 0f && GlobalPosition.Y <= 0.05f && Velocity.Y < 0f) { Land(); return; }
        if (_travelled >= Range) End();
    }

    /// <summary>The end of a straight flight: just gone, or a burst of confetti.</summary>
    private void End()
    {
        if (Splat > 0f) { Land(); return; }               // a pie that reaches the end of its flight splats on the floor
        if (Burst) Splash(1.8f, Damage * 0.8f, "kaboom", 1.4f, new Color(1f, 0.6f, 0.9f));
        QueueFree();
    }

    private void Land()
    {
        Splash(Splat > 0f ? Splat : 0.8f, Damage, "splat", 1f, new Color(1f, 0.97f, 0.9f));
        LeavesBehind?.Invoke(new Vector3(GlobalPosition.X, 0f, GlobalPosition.Z));
        QueueFree();
    }

    private void Splash(float radius, float damage, string sound, float pitch, Color tint)
    {
        Vector3 at = new(GlobalPosition.X, 0f, GlobalPosition.Z);
        Fx.Impact(GetParent(), at + new Vector3(0f, 0.3f, 0f), radius);
        Fx.Pop(GetParent(), at + new Vector3(0f, 1.2f, 0f), sound == "splat" ? "SPLAT!" : "POP!", tint, 0.8f, 0.5f);
        Fx.Sound(sound, pitch, -2f);
        Fx.Shake(this, 0.05f, 0.18f);
        foreach (Node n in Fighter.Live)
        {
            if (n is not Fighter f || f.Hostile == Shooter.Hostile || f.IsDead || !f.CanBeHit()) continue;
            Vector3 d = f.GlobalPosition - at;
            d.Y = 0f;
            if (d.Length() > radius) continue;
            Shooter.StrikeFromProjectile(f, damage, d.LengthSquared() > 0.01f ? d.Normalized() : Vector3.Right, Knockback, Launch);
            if (Blind > 0f && !f.IsDead) f.Stagger(Blind);
        }
    }

    // ---------------------------------------------------------------- models

    /// <summary>A cream pie in a tin, cream side up, with a cherry.</summary>
    public static Node3D Pie(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Kit3D.Put(n, Kit3D.Cyl(0.24f, 0.2f, 0.07f, 14), Kit3D.Paint(new Color(0.75f, 0.78f, 0.85f), 0.3f, 0.7f), Vector3.Zero);
        Kit3D.Put(n, Kit3D.Cyl(0.23f, 0.23f, 0.05f, 14), Kit3D.Paint(new Color(0.87f, 0.64f, 0.36f), 0.8f), new Vector3(0f, 0.05f, 0f));
        Kit3D.Put(n, Kit3D.Ball(0.2f, 0.12f, 12), Kit3D.Paint(new Color(1f, 0.97f, 0.9f), 0.6f, 0f, 0.15f), new Vector3(0f, 0.1f, 0f));
        Kit3D.Put(n, Kit3D.Ball(0.05f, -1f, 8), Kit3D.Paint(Kit3D.Red, 0.2f), new Vector3(0f, 0.24f, 0f));
        return n;
    }

    /// <summary>A cork, flying sideways.</summary>
    public static Node3D Cork(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Kit3D.Put(n, Kit3D.Cyl(0.07f, 0.09f, 0.16f, 10), Kit3D.Paint(new Color(0.77f, 0.59f, 0.39f), 0.9f), Vector3.Zero, new Vector3(0f, 0f, 90f));
        return n;
    }
}
