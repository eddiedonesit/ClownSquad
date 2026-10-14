namespace ClownSquad.Actors;

using System;
using Godot;
using ClownSquad.Combat;
using ClownSquad.Visuals;
using ClownSquad.World;

/// <summary>
/// Anything that can fight: players, enemies, dummies. Owns health, movement,
/// and the hit-reaction model the whole combat system rests on: hitstop, stun,
/// launch and juggle, knockdown, and wall bounce. Visuals are delegated to a
/// DirectionalSprite (ADR 0004); what drives the fighter is up to subclasses.
/// </summary>
public partial class Fighter : CharacterBody3D
{
    public const string Group = "fighters";

    /// <summary>Raised for every hit that lands: attacker (if any), victim, damage.</summary>
    public static event Action<Fighter?, Fighter, float>? HitLanded;

    protected virtual string SheetPath => "res://assets/sprites/clown_16bit.png";
    protected virtual string FramesPath => "res://assets/sprites/clown_16bit.frames.json";
    protected virtual float SpritePixelSize => 0.0225f;

    /// <summary>Which side this fighter is on. Attacks only cross factions.</summary>
    public virtual bool Hostile => false;

    /// <summary>Body size, used to keep the whole fighter on screen.</summary>
    public virtual float BodyHeight => 1.5f;
    public virtual float BodyHalfWidth => 0.45f;

    /// <summary>
    /// Super armour. A fighter with poise still takes damage but is not stunned,
    /// shoved or launched, so its move completes. Bosses have it during their
    /// specials, which is what makes those moves something to dodge rather than
    /// something to interrupt.
    /// </summary>
    protected virtual bool HasPoise => false;
    protected virtual float StunScale => 1f;
    /// <summary>Zero for fighters too heavy to be thrown into the air.</summary>
    protected virtual float LaunchScale => 1f;

    /// <summary>
    /// Blink while lying dead and while invulnerable after reviving. The arcade
    /// signal for "this one is coming back" and "cannot be hurt yet".
    /// </summary>
    protected virtual bool BlinkWhileDown => false;
    private const float BlinkPeriod = 0.09f;
    private float _deadTime;

    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float MoveSpeed { get; set; } = 5.0f;
    [Export] public float Acceleration { get; set; } = 60f;
    [Export] public float Friction { get; set; } = 45f;
    [Export] public float Gravity { get; set; } = 26f;

    private const float DownTime = 0.6f;
    private const float GetupInvulnerability = 0.55f;
    private const float WallBounceDamage = 6f;

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

    /// <summary>Knocked into the air by a hit and not yet landed. Juggle window.</summary>
    public bool Launched { get; private set; }
    public bool Down => _downRemaining > 0f;
    public bool Stunned => _stunRemaining > 0f;

    /// <summary>Unable to act: subclass logic is skipped entirely while true.</summary>
    protected bool Incapacitated => IsDead || Launched || Down || Stunned;

    public string CurrentClip => Visual?.CurrentClip ?? "-";
    public bool SpriteFlipped => Visual?.Flipped ?? false;
    public bool SpriteVisible => Visual?.Visible ?? true;

    protected DirectionalSprite Visual = null!;
    protected float HitstopRemaining;
    protected bool InHitstop => HitstopRemaining > 0f;
    protected bool InHitAnim => _hitAnimRemaining > 0f;

    private Vector3 _knockback;
    private float _pendingLaunch;
    private float _hitAnimRemaining;
    private float _stunRemaining;
    private float _downRemaining;
    private float _invulnerable;
    private int _juggleCount;
    private bool _groundBounced;
    private bool _wallBounced;
    private Color _pendingTint = Colors.White;
    private float _despawnIn;

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

        // Fighters pass through one another. Body blocking lets a crowd pin the
        // player in place, and it let launched bodies come to rest on top of
        // others; spacing is handled by the AI instead.
        CollisionLayer = 0;
        CollisionMask = 0;

        // The visual aligns its own feet to this node's origin, so fighters of
        // different sprite scales all stand on the ground correctly.
        Visual = new DirectionalSprite(SheetPath, FramesPath, SpritePixelSize);
        Visual.Tint = _pendingTint;
        AddChild(Visual);

        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.4f },
            Position = new Vector3(0, 0.7f, 0),
        });
    }

    protected void PlayClip(string state, bool restart = false) => Visual?.Play(state, restart);

    protected void PinClipFrame(string state, int frame) => Visual?.Pin(state, frame);

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (_despawnIn > 0f)
        {
            _despawnIn -= dt;
            if (_despawnIn <= 0f) { QueueFree(); return; }
            // Blink out over the last moments, the way arcade bodies always have.
            if (_despawnIn < 0.7f) Visual.Visible = (int)(_despawnIn / 0.07f) % 2 == 0;
        }

        // Hitstop: nothing moves, nothing advances, and velocity is kept so a
        // mid-air hit resumes its arc afterwards instead of dropping dead.
        if (HitstopRemaining > 0f)
        {
            HitstopRemaining -= dt;
            return;
        }

        if (_hitAnimRemaining > 0f) _hitAnimRemaining -= dt;
        if (_stunRemaining > 0f) _stunRemaining -= dt;
        if (_invulnerable > 0f) _invulnerable -= dt;
        if (BlinkWhileDown && !IsDead)
            Visual.Visible = _invulnerable <= 0f || (int)(_invulnerable / BlinkPeriod) % 2 == 0;

        TickAlways(dt);
        if (Incapacitated) TickIncapacitated(dt);
        else Tick(dt);

        // Knockback is an impulse, applied once and then left to friction. It is
        // held pending rather than applied in TakeDamage so that it lands after
        // the hitstop it shares a frame with.
        if (_knockback.LengthSquared() > 0.001f)
        {
            Velocity += _knockback;
            _knockback = Vector3.Zero;
        }
        if (_pendingLaunch > 0f)
        {
            Velocity = new Vector3(Velocity.X, _pendingLaunch, Velocity.Z);
            _pendingLaunch = 0f;
            Grounded = false;
        }

        if (!Grounded) Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * dt, Velocity.Z);

        Visual?.SetFacing(Facing);
        Visual?.Tick(dt);

        bool wasAirborne = !Grounded;
        MoveAndSlide();
        float fallSpeed = -Velocity.Y;

        if (GlobalPosition.Y <= 0f && Velocity.Y <= 0f)
        {
            GlobalPosition = new Vector3(GlobalPosition.X, 0f, GlobalPosition.Z);
            Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
            Grounded = true;
            if (wasAirborne) OnLanded(fallSpeed);
        }
        else
        {
            Grounded = false;
        }

        KeepInArena();
    }

    /// <summary>Per-frame logic for a fighter able to act.</summary>
    protected virtual void Tick(float dt) { }

    /// <summary>Runs every frame regardless of state. For timers that must not stall.</summary>
    protected virtual void TickAlways(float dt) { }

    /// <summary>Called when a hit takes control away, so subclasses can abandon what they were doing.</summary>
    protected virtual void OnInterrupted() { }

    protected virtual void OnRevived() { }

    private void TickIncapacitated(float dt)
    {
        // Airborne bodies keep their momentum; that travel is the juggle.
        if (Grounded) ApplyGroundMovement(Vector3.Zero, dt);

        bool floored = (Down || IsDead) && Grounded;
        bool hasFlooredArt = Visual.HasClip("down");
        if (IsDead && floored && BlinkWhileDown)
        {
            _deadTime += dt;
            Visual.Visible = (int)(_deadTime / BlinkPeriod) % 2 == 0;
        }
        PinClipFrame(floored && hasFlooredArt ? "down" : "hit", 0);

        if (Down || IsDead)
        {
            // Sheets without a floored frame fall back to the hit pose pressed flat.
            if (floored && !hasFlooredArt) Visual?.Squash(0.7f);
            if (!IsDead)
            {
                _downRemaining -= dt;
                if (_downRemaining <= 0f)
                {
                    // Brief invulnerability on rising, or a floored fighter can
                    // be kept floored forever.
                    _invulnerable = GetupInvulnerability;
                    _juggleCount = 0;
                    _wallBounced = false;
                }
            }
        }
    }

    private void OnLanded(float fallSpeed)
    {
        if (!Launched) return;

        Dust(0.5f);
        if (!_groundBounced && fallSpeed > 6.5f && !IsDead)
        {
            // One bounce off the floor: a last chance to extend the juggle.
            _groundBounced = true;
            Velocity = new Vector3(Velocity.X * 0.6f, fallSpeed * 0.38f, Velocity.Z * 0.6f);
            Grounded = false;
            return;
        }

        Launched = false;
        _groundBounced = false;
        _downRemaining = DownTime;
    }

    /// <summary>
    /// Hold the fighter inside the camera view. A launched body that reaches a
    /// side wall bounces back off it, once per launch, taking a little damage:
    /// the screen edge becomes a combo extender instead of a dead end.
    /// </summary>
    private void KeepInArena()
    {
        Arena? arena = Arena.Current;
        if (arena is null) return;

        Vector3 clamped = arena.Clamp(GlobalPosition, BodyHeight, BodyHalfWidth, out int wall);
        if (clamped == GlobalPosition) return;
        GlobalPosition = clamped;
        if (wall == 0) return;

        bool movingIntoWall = Mathf.Sign(Velocity.X) == wall;
        if (Launched && !_wallBounced && movingIntoWall && Mathf.Abs(Velocity.X) > 2.5f)
        {
            _wallBounced = true;
            Velocity = new Vector3(-Velocity.X * 0.55f, Mathf.Max(Velocity.Y, 4.5f), Velocity.Z);
            Grounded = false;
            Visual?.Flash();
            Visual?.Squash(0.4f);
            ImpactEffect.Spawn(GetParent(), GlobalPosition + new Vector3(0f, BodyHeight * 0.5f, 0f), 0.6f);
            CameraShake.Request(this, 0.05f, 0.18f);
            if (!IsDead)
            {
                Health = Mathf.Max(0f, Health - WallBounceDamage);
                HitLanded?.Invoke(null, this, WallBounceDamage);
                if (IsDead) Died?.Invoke(this);
            }
        }
        else if (movingIntoWall)
        {
            Velocity = new Vector3(0f, Velocity.Y, Velocity.Z);
        }
    }

    protected void Dust(float scale) =>
        ImpactEffect.Spawn(GetParent(), GlobalPosition + new Vector3(0f, 0.1f, 0f), scale);

    protected void ApplyGroundMovement(Vector3 desired, float dt)
    {
        float rate = desired.LengthSquared() > 0.001f ? Acceleration : Friction;
        var flat = new Vector3(Velocity.X, 0, Velocity.Z).MoveToward(desired, rate * dt);
        Velocity = new Vector3(flat.X, Velocity.Y, flat.Z);   // preserve vertical
    }

    /// <summary>Floored and freshly risen fighters cannot be hit; airborne ones can.</summary>
    public virtual bool CanBeHit() => !IsDead && !Down && _invulnerable <= 0f;

    public virtual void TakeDamage(in DamageInfo info)
    {
        if (!CanBeHit()) return;

        Health = Mathf.Max(0f, Health - info.Amount);
        HitstopRemaining = info.Hitstop;
        _hitAnimRemaining = 0.22f;
        Visual?.Flash();
        Visual?.Squash(0.35f);
        PlayClip("hit", restart: true);

        if (!HasPoise || IsDead)
        {
            _knockback = info.KnockbackDirection.Normalized() * info.KnockbackForce;

            float launch = info.Launch * LaunchScale;
            // Any hit on a body already in the air keeps it up a little longer.
            if (Launched && !Grounded && LaunchScale > 0f) launch = Mathf.Max(launch, 3.4f);
            // A killing blow always lifts the body, so a knockout reads as one.
            if (IsDead && LaunchScale > 0f) launch = Mathf.Max(launch, 4.8f);
            if (launch > 0f)
            {
                // Each successive juggle hit lifts less, so juggles end on their
                // own rather than by an arbitrary hit cap.
                launch *= Mathf.Max(0.4f, 1f - 0.17f * _juggleCount);
                _pendingLaunch = launch;
                _juggleCount++;
                Launched = true;
                _groundBounced = false;
            }

            _stunRemaining = Mathf.Max(_stunRemaining, info.Stun * StunScale);
            OnInterrupted();
        }

        HitLanded?.Invoke(info.Source as Fighter, this, info.Amount);
        if (IsDead) Died?.Invoke(this);
    }

    /// <summary>
    /// Damage every opposing fighter within a radius, knocking them outward.
    /// Used by area specials, where a forward box would be the wrong shape.
    /// </summary>
    protected int DamageInRadius(float radius, float amount, float knockback, float hitstop,
                                 float launch = 0f, float stun = 0.3f)
    {
        int hits = 0;
        foreach (Node node in GetTree().GetNodesInGroup(Group))
        {
            if (node is not Fighter f || f == this || !f.CanBeHit()) continue;
            if (f.Hostile == Hostile) continue;

            Vector3 d = f.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            float dist = d.Length();
            if (dist > radius) continue;

            Vector3 dir = dist < 0.01f ? Facing : d / dist;
            f.TakeDamage(new DamageInfo(amount, dir, knockback, hitstop, this, launch, stun));
            hits++;
        }
        return hits;
    }

    /// <summary>
    /// Leave the fight for good: stop being a target, lie there, blink, vanish.
    /// </summary>
    public void BeginDespawn(float lieFor = 0.9f)
    {
        RemoveFromGroup(Group);
        _despawnIn = lieFor + 0.7f;
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

    /// <summary>Restore to full and place at a position. Used for respawns.</summary>
    public void Revive(Vector3 at)
    {
        Health = MaxHealth;
        Velocity = Vector3.Zero;
        GlobalPosition = at;
        Grounded = at.Y <= 0f;
        Launched = false;
        _knockback = Vector3.Zero;
        _pendingLaunch = 0f;
        _stunRemaining = 0f;
        _downRemaining = 0f;
        _juggleCount = 0;
        _groundBounced = false;
        _wallBounced = false;
        HitstopRemaining = 0f;
        _invulnerable = 1.0f;
        _deadTime = 0f;
        Visual.Visible = true;
        OnRevived();
        PlayClip("idle", restart: true);
    }

    public void Heal(float amount) => Health = Mathf.Min(MaxHealth, Health + amount);
}
