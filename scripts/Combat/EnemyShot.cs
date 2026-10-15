namespace ClownSquad.Combat;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Visuals;
using ClownSquad.World;
using ClownSquad.Net;

/// <summary>
/// Anything an enemy throws: juggling pins, flaming torches, dumbbells, a
/// whole tot, the Ringmaster's hat-bombs. Either lobbed to land on a spot
/// (a telegraph marks where) or flung straight along the lane. Hits each
/// player once, can leave something behind where it lands (fire), and can
/// burst on landing. The look is a small 3D model from Kit3D.
/// </summary>
public partial class EnemyShot : Node3D
{
    // Numbered for the network (NetRegistry) while in the scene.
    public override void _Notification(int what) => ClownSquad.Net.NetRegistry.Notify(this, what);

    public PlayerFighter Shooter = null!;
    public Vector3 Velocity;
    public float Gravity;
    public float Damage = 10f;
    public float Knockback = 4f;
    public float Launch;
    public float HitRadius = 0.55f;
    public float Life = 4f;
    public float SpinSpeed = 10f;
    /// <summary>Burst radius when it lands (0 = it just drops).</summary>
    public float BurstRadius;
    public float BurstDamage;
    /// <summary>Called where it lands, e.g. to leave fire on the floor.</summary>
    public Action<Vector3>? OnLand;
    /// <summary>Steer toward the nearest player at this rate (0 = no homing).</summary>
    public float Homing;
    /// <summary>Flies on through whoever it hits (a thrown ring of fire), instead of stopping.</summary>
    public bool Pierce;
    public Telegraph? Marker;

    private readonly HashSet<Fighter> _hit = new();
    private Node3D _model = null!;
    private float _age;

    /// <summary>Lob a shot from `from` so it lands at `to` after `seconds`.</summary>
    public static EnemyShot Lob(PlayerFighter owner, Node3D model, Vector3 from, Vector3 to, float seconds, float gravity = 18f, bool mark = true)
    {
        var s = new EnemyShot { Shooter = owner, Gravity = gravity };
        Vector3 d = to - from;
        s.Velocity = new Vector3(d.X / seconds, (to.Y - from.Y + 0.5f * gravity * seconds * seconds) / seconds, d.Z / seconds);
        s.Launch0(owner, model, from);
        if (mark) s.Marker = Telegraph.Circle(owner.GetParent(), to, Mathf.Max(0.6f, s.BurstRadius), seconds);
        return s;
    }

    /// <summary>Fling a shot straight along a direction at a speed.</summary>
    public static EnemyShot Fling(PlayerFighter owner, Node3D model, Vector3 from, Vector3 dir, float speed)
    {
        var s = new EnemyShot { Shooter = owner, Velocity = dir.Normalized() * speed };
        s.Launch0(owner, model, from);
        return s;
    }

    public const string Group = "enemy_shots";

    /// <summary>Where this shot will be in `t` seconds (ignoring homing), for anything trying to get out of its way.</summary>
    public Vector3 Predict(float t) => GlobalPosition + Velocity * t + Vector3.Down * (0.5f * Gravity * t * t);

    private void Launch0(PlayerFighter owner, Node3D model, Vector3 from)
    {
        AddToGroup(Group);
        _model = model;
        AddChild(model);
        owner.GetParent().AddChild(this);
        GlobalPosition = from;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (_age > Life || !IsInstanceValid(Shooter)) { Gone(); return; }

        if (Homing > 0f && Nearest() is Fighter t)
        {
            Vector3 want = (t.GlobalPosition + new Vector3(0f, 0.8f, 0f) - GlobalPosition).Normalized() * Velocity.Length();
            Velocity = Velocity.Lerp(want, Mathf.Clamp(Homing * dt, 0f, 1f));
        }
        Velocity += Vector3.Down * Gravity * dt;
        GlobalPosition += Velocity * dt;
        _model.RotateZ(SpinSpeed * dt * (Velocity.X >= 0f ? -1f : 1f));

        foreach (Node n in Fighter.Live)
        {
            if (n is not Fighter f || f.Hostile || f.IsDead || _hit.Contains(f) || !f.CanBeHit()) continue;
            Vector3 d = f.GlobalPosition + new Vector3(0f, f.BodyHeight * 0.5f, 0f) - GlobalPosition;
            if (Mathf.Abs(d.X) > HitRadius + f.BodyHalfWidth * 0.5f || Mathf.Abs(d.Z) > HitRadius || Mathf.Abs(d.Y) > f.BodyHeight * 0.6f + HitRadius) continue;
            _hit.Add(f);
            Vector3 dir = new(Mathf.Sign(Velocity.X == 0f ? 1f : Velocity.X), 0f, 0f);
            f.TakeDamage(new DamageInfo(Damage * Shooter.DamageMultiplier, dir, Knockback, 0.06f, Shooter, Launch, 0.35f));
            Fx.Impact(GetParent(), GlobalPosition, 0.5f);
            if (!Pierce && BurstRadius <= 0f && Gravity <= 0f) { Gone(); return; }
        }

        if (GlobalPosition.Y <= 0.05f && Velocity.Y < 0f)
        {
            Vector3 at = new(GlobalPosition.X, 0f, GlobalPosition.Z);
            if (BurstRadius > 0f)
            {
                Fx.Impact(GetParent(), at + new Vector3(0f, 0.2f, 0f), BurstRadius * 0.9f);
                Fx.Sound("kaboom", 1.3f, -6f);
                Fx.Shake(this, 0.06f, 0.2f);
                foreach (Node n in Fighter.Live)
                {
                    if (n is not Fighter f || f.Hostile || f.IsDead || !f.CanBeHit()) continue;
                    Vector3 d = f.GlobalPosition - at;
                    d.Y = 0f;
                    if (d.Length() > BurstRadius) continue;
                    f.TakeDamage(new DamageInfo(BurstDamage * Shooter.DamageMultiplier, d.LengthSquared() > 0.01f ? d.Normalized() : Vector3.Right, 5f, 0.08f, Shooter, 4f, 0.4f));
                }
            }
            else
            {
                Fx.Sound("clack", 0.8f, -4f);
            }
            OnLand?.Invoke(at);
            Gone();
        }
    }

    private Fighter? Nearest()
    {
        Fighter? best = null;
        float bd = float.MaxValue;
        foreach (Node n in Fighter.Live)
        {
            if (n is not Fighter f || f.Hostile || f.IsDead) continue;
            float d = f.GlobalPosition.DistanceSquaredTo(GlobalPosition);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    private void Gone()
    {
        Marker?.Clear();
        QueueFree();
    }

    // ---------------------------------------------------------------- models

    /// <summary>A juggling pin: white, two red bands.</summary>
    public static Node3D Pin(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Kit3D.Put(n, Kit3D.Cyl(0.05f, 0.11f, 0.36f, 10), Kit3D.Paint(Kit3D.Cream, 0.3f), new Vector3(0f, -0.02f, 0f));
        Kit3D.Put(n, Kit3D.Ball(0.12f, 0.3f, 10), Kit3D.Paint(Kit3D.Cream, 0.3f), new Vector3(0f, -0.12f, 0f));
        Kit3D.Put(n, Kit3D.Cyl(0.055f, 0.055f, 0.04f, 10), Kit3D.Paint(Kit3D.Red, 0.4f), new Vector3(0f, 0.08f, 0f));
        Kit3D.Put(n, Kit3D.Ball(0.065f, -1f, 8), Kit3D.Paint(Kit3D.Cream, 0.3f), new Vector3(0f, 0.2f, 0f));
        return n;
    }

    /// <summary>A lit torch with a real flicker of light.</summary>
    public static Node3D Torch(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Kit3D.Put(n, Kit3D.Cyl(0.03f, 0.04f, 0.5f, 8), Kit3D.Paint(Kit3D.Wood), Vector3.Zero);
        Kit3D.Put(n, Kit3D.Cyl(0f, 0.12f, 0.3f, 8), Kit3D.Paint(new Color(1f, 0.55f, 0.15f), 0.3f, 0f, 4f), new Vector3(0f, 0.38f, 0f));
        n.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 1.6f, OmniRange = 3f, Position = new Vector3(0f, 0.4f, 0f) });
        return n;
    }

    public static Node3D Dumbbell(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Kit3D.Put(n, Kit3D.Cyl(0.04f, 0.04f, 0.5f, 8), Kit3D.Paint(Kit3D.Stone, 0.3f, 0.8f), Vector3.Zero, new Vector3(0f, 0f, 90f));
        foreach (float x in new[] { -0.25f, 0.25f })
            Kit3D.Put(n, Kit3D.Ball(0.16f, -1f, 10), Kit3D.Paint(Kit3D.Iron, 0.4f, 0.5f), new Vector3(x, 0f, 0f));
        return n;
    }

    /// <summary>A round black bomb with a lit fuse: the Ringmaster's hat trick.</summary>
    public static Node3D Bomb(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Kit3D.Put(n, Kit3D.Ball(0.2f, -1f, 12), Kit3D.Paint(Kit3D.Iron, 0.3f, 0.3f), Vector3.Zero);
        Kit3D.Put(n, Kit3D.Cyl(0.02f, 0.02f, 0.14f, 6), Kit3D.Paint(Kit3D.Wood), new Vector3(0f, 0.24f, 0f));
        Kit3D.Put(n, Kit3D.Ball(0.05f, -1f, 6), Kit3D.Paint(new Color(1f, 0.8f, 0.3f), 0.3f, 0f, 5f), new Vector3(0f, 0.32f, 0f));
        return n;
    }

    /// <summary>A sliver of broken glass: pale, sharp, glinting.</summary>
    public static Node3D Shard(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Kit3D.Put(n, Kit3D.Prism(0.12f, 0.3f, 0.03f), GlassPaint, Vector3.Zero, new Vector3(0f, 0f, GD.Randf() * 360f));
        return n;
    }

    /// <summary>A green glass bottle, thrown to smash.</summary>
    public static Node3D Bottle(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Material green = Kit3D.Paint(new Color(0.3f, 0.75f, 0.45f), 0.1f, 0.3f, 0.3f);
        Kit3D.Put(n, Kit3D.Cyl(0.09f, 0.1f, 0.3f, 10), green, Vector3.Zero);
        Kit3D.Put(n, Kit3D.Cyl(0.035f, 0.08f, 0.14f, 10), green, new Vector3(0f, 0.21f, 0f));
        return n;
    }

    /// <summary>A gob of fire, spat.</summary>
    public static Node3D Fireball(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Kit3D.Put(n, Kit3D.Ball(0.22f, -1f, 10), Kit3D.Paint(new Color(1f, 0.5f, 0.12f), 0.3f, 0f, 4f), Vector3.Zero);
        Kit3D.Put(n, Kit3D.Ball(0.13f, -1f, 8), Kit3D.Paint(new Color(1f, 0.95f, 0.5f), 0.3f, 0f, 6f), new Vector3(0.06f, 0.04f, 0.05f));
        n.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 2f, OmniRange = 3.5f });
        return n;
    }

    /// <summary>A hoop of fire, thrown rolling down the lane.</summary>
    public static Node3D FireRing(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Kit3D.Put(n, Kit3D.Ring(0.5f, 0.62f, 24), Kit3D.Paint(Kit3D.Gold, 0.3f, 0.6f), Vector3.Zero, new Vector3(90f, 0f, 0f));
        var flames = new List<Transform3D>();
        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.Tau * i / 12f;
            flames.Add(new Transform3D(new Basis(Vector3.Back, a), new Vector3(Mathf.Sin(a) * 0.62f, Mathf.Cos(a) * 0.62f, 0f)));
        }
        Kit3D.Many(n, Kit3D.Cyl(0f, 0.1f, 0.3f, 6), Kit3D.Paint(new Color(1f, 0.6f, 0.15f), 0.3f, 0f, 5f), flames);
        n.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 2.4f, OmniRange = 4f });
        return n;
    }

    private static StandardMaterial3D? _glass;
    public static StandardMaterial3D GlassPaint => _glass ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.8f, 0.95f, 1f, 0.75f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.05f, Metallic = 0.6f, EmissionEnabled = true, Emission = new Color(0.6f, 0.85f, 1f), EmissionEnergyMultiplier = 0.6f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>A white dove (a puff of white with wings), homing in to peck.</summary>
    public static Node3D Dove(float scale = 1f)
    {
        var n = new Node3D { Scale = Vector3.One * scale };
        Material white = Kit3D.Paint(new Color(0.98f, 0.98f, 1f), 0.6f, 0f, 0.3f);
        Kit3D.Put(n, Kit3D.Ball(0.14f, 0.22f, 10), white, Vector3.Zero);
        Kit3D.Put(n, Kit3D.Ball(0.08f, -1f, 8), white, new Vector3(0.14f, 0.06f, 0f));
        Kit3D.Put(n, Kit3D.Prism(0.3f, 0.16f, 0.02f), white, new Vector3(-0.02f, 0.1f, 0.06f), new Vector3(30f, 0f, 0f));
        Kit3D.Put(n, Kit3D.Cyl(0f, 0.03f, 0.06f, 6), Kit3D.Paint(Kit3D.Gold), new Vector3(0.22f, 0.06f, 0f), new Vector3(0f, 0f, -90f));
        return n;
    }
}
