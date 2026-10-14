namespace ClownSquad.Actors;

using System;
using Godot;
using ClownSquad.Combat;
using ClownSquad.Visuals;

/// <summary>
/// Anything that can fight: players, enemies, dummies. Owns health, movement,
/// knockback and hitstop. Visuals are delegated to a DirectionalSprite, so
/// every fighter gets directional animation by construction (ADR 0004).
/// </summary>
public partial class Fighter : CharacterBody3D
{
    public const string Group = "fighters";

    protected virtual string SheetPath => "res://assets/sprites/clown_16bit.png";
    protected virtual string FramesPath => "res://assets/sprites/clown_16bit.frames.json";

    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float MoveSpeed { get; set; } = 5.0f;
    [Export] public float Acceleration { get; set; } = 60f;
    [Export] public float Friction { get; set; } = 45f;
    [Export] public float Gravity { get; set; } = 26f;

    /// <summary>
    /// The arena is a flat plane with no floor collider, so ground contact is a
    /// clamp at y=0 rather than a physics query. Replace with real colliders if
    /// platforms or uneven terrain ever appear.
    /// </summary>
    public bool Grounded { get; private set; } = true;

    public float Health { get; private set; }
    public bool IsDead => Health <= 0f;
    /// <summary>
    /// Heading, horizontal only. Attacks resolve along it, so locking it to the
    /// X axis is what prevents attacking up or down (ADR 0004).
    /// </summary>
    public Vector3 Facing { get; protected set; } = Vector3.Right;

    public string CurrentClip => Visual?.CurrentClip ?? "-";
    public bool SpriteFlipped => Visual?.Flipped ?? false;

    protected DirectionalSprite Visual = null!;
    protected float HitstopRemaining;
    protected bool InHitstop => HitstopRemaining > 0f;
    protected bool InHitAnim => _hitAnimRemaining > 0f;

    private Vector3 _knockback;
    private float _hitAnimRemaining;
    private Color _pendingTint = Colors.White;

    public event Action<Fighter>? Died;

    /// <summary>Per-fighter tint, applied through the visual.</summary>
    protected Color BaseTint
    {
        get => _pendingTint;
        set { _pendingTint = value; if (Visual is not null) Visual.Tint = value; }
    }

    public override void _Ready()
    {
        Health = MaxHealth;
        AddToGroup(Group);

        Visual = new DirectionalSprite(SheetPath, FramesPath)
        {
            Position = new Vector3(0, 0.72f, 0),
        };
        Visual.Tint = _pendingTint;
        AddChild(Visual);

        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.4f },
            Position = new Vector3(0, 0.7f, 0),
        });
    }

    /// <summary>Play a state; the visual resolves the direction prefix.</summary>
    protected void PlayClip(string state, bool restart = false) => Visual?.Play(state, restart);

    protected void PinClipFrame(string state, int frame) => Visual?.Pin(state, frame);

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (HitstopRemaining > 0f)
        {
            HitstopRemaining -= dt;
            Velocity = Vector3.Zero;
            return;
        }

        if (_hitAnimRemaining > 0f) _hitAnimRemaining -= dt;

        Tick(dt);

        if (_knockback.LengthSquared() > 0.001f)
        {
            Velocity += _knockback;
            _knockback = _knockback.MoveToward(Vector3.Zero, 40f * dt);
        }

        if (!Grounded) Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * dt, Velocity.Z);

        Visual?.SetFacing(Facing);
        Visual?.Tick(dt);

        MoveAndSlide();

        if (GlobalPosition.Y <= 0f)
        {
            GlobalPosition = new Vector3(GlobalPosition.X, 0f, GlobalPosition.Z);
            if (Velocity.Y < 0f) Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
            Grounded = true;
        }
        else
        {
            Grounded = false;
        }
    }

    protected virtual void Tick(float dt) { }

    protected void ApplyGroundMovement(Vector3 desired, float dt)
    {
        float rate = desired.LengthSquared() > 0.001f ? Acceleration : Friction;
        var flat = new Vector3(Velocity.X, 0, Velocity.Z).MoveToward(desired, rate * dt);
        Velocity = new Vector3(flat.X, Velocity.Y, flat.Z);   // preserve vertical
    }

    public virtual bool CanBeHit() => !IsDead;

    public virtual void TakeDamage(in DamageInfo info)
    {
        if (!CanBeHit()) return;

        Health = Mathf.Max(0f, Health - info.Amount);
        _knockback = info.KnockbackDirection.Normalized() * info.KnockbackForce;
        HitstopRemaining = info.Hitstop;
        _hitAnimRemaining = 0.22f;
        Visual?.Flash();
        Visual?.Squash(0.35f);
        PlayClip("hit", restart: true);

        if (IsDead) Died?.Invoke(this);
    }

    public void ApplyHitstop(float seconds)
    {
        HitstopRemaining = Mathf.Max(HitstopRemaining, seconds);
        Visual?.Squash(0.18f);
    }

    /// <summary>Launch upward. Caller decides whether jumping is allowed.</summary>
    protected void Jump(float speed)
    {
        Velocity = new Vector3(Velocity.X, speed, Velocity.Z);
        Grounded = false;
    }

    public void Heal(float amount) => Health = Mathf.Min(MaxHealth, Health + amount);
}
