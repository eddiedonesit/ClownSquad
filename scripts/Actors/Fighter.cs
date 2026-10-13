namespace ClownSquad.Actors;

using System;
using Godot;
using ClownSquad.Combat;

/// <summary>
/// Anything that can fight: players, enemies, dummies. Owns health, movement,
/// knockback and hitstop. Subclasses decide what drives it.
/// </summary>
public partial class Fighter : CharacterBody3D
{
    public const string Group = "fighters";

    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float MoveSpeed { get; set; } = 5.0f;
    [Export] public float Acceleration { get; set; } = 60f;
    [Export] public float Friction { get; set; } = 45f;

    public float Health { get; private set; }
    public bool IsDead => Health <= 0f;

    /// <summary>Facing on the ground plane. Drives attack direction.</summary>
    public Vector3 Facing { get; protected set; } = Vector3.Back;

    /// <summary>While &gt; 0 the fighter is frozen mid-action. The feel lever.</summary>
    protected float HitstopRemaining;
    protected bool InHitstop => HitstopRemaining > 0f;

    private Vector3 _knockback;
    private float _flashRemaining;
    protected Sprite3D Sprite = null!;

    public event Action<Fighter>? Died;

    public override void _Ready()
    {
        Health = MaxHealth;
        AddToGroup(Group);
        Sprite = BuildSprite();
        AddChild(Sprite);
        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.4f },
            Position = new Vector3(0, 0.7f, 0),
        });
    }

    /// <summary>Sprite settings are load-bearing; see ADR 0002.</summary>
    private static Sprite3D BuildSprite()
    {
        var s = new Sprite3D
        {
            Texture = GD.Load<Texture2D>("res://assets/sprites/clown_16bit.png"),
            PixelSize = 0.0225f,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            Shaded = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        s.Position = new Vector3(0, 0.72f, 0);
        return s;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (HitstopRemaining > 0f)
        {
            HitstopRemaining -= dt;
            Velocity = Vector3.Zero;
            return;
        }

        if (_flashRemaining > 0f)
        {
            _flashRemaining -= dt;
            Sprite.Modulate = _flashRemaining > 0f ? new Color(3f, 2f, 2f) : Colors.White;
        }

        Tick(dt);

        if (_knockback.LengthSquared() > 0.001f)
        {
            Velocity += _knockback;
            _knockback = _knockback.MoveToward(Vector3.Zero, 40f * dt);
        }

        MoveAndSlide();
    }

    /// <summary>Per-frame logic, skipped entirely during hitstop.</summary>
    protected virtual void Tick(float dt) { }

    /// <summary>Move toward a desired ground-plane velocity with accel/friction.</summary>
    protected void ApplyGroundMovement(Vector3 desired, float dt)
    {
        float rate = desired.LengthSquared() > 0.001f ? Acceleration : Friction;
        var flat = new Vector3(Velocity.X, 0, Velocity.Z).MoveToward(desired, rate * dt);
        Velocity = new Vector3(flat.X, 0, flat.Z);
    }

    public virtual bool CanBeHit() => !IsDead;

    public virtual void TakeDamage(in DamageInfo info)
    {
        if (!CanBeHit()) return;

        Health = Mathf.Max(0f, Health - info.Amount);
        _knockback = info.KnockbackDirection.Normalized() * info.KnockbackForce;
        HitstopRemaining = info.Hitstop;
        _flashRemaining = 0.12f;
        Sprite.Modulate = new Color(3f, 2f, 2f);

        if (IsDead) Died?.Invoke(this);
    }

    /// <summary>Freeze the attacker too, so the hit reads as impact on both sides.</summary>
    public void ApplyHitstop(float seconds) => HitstopRemaining = Mathf.Max(HitstopRemaining, seconds);

    public void Heal(float amount) => Health = Mathf.Min(MaxHealth, Health + amount);
}
