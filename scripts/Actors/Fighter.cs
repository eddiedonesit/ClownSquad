namespace ClownSquad.Actors;

using System;
using Godot;
using ClownSquad.Combat;

/// <summary>
/// Anything that can fight: players, enemies, dummies. Owns health, movement,
/// knockback, hitstop and the animation rig. Subclasses decide what drives it.
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
    public Vector3 Facing { get; protected set; } = Vector3.Back;

    protected float HitstopRemaining;
    protected bool InHitstop => HitstopRemaining > 0f;

    private Vector3 _knockback;
    private float _flashRemaining;
    private float _hitAnimRemaining;
    private float _squash;
    protected AnimatedSprite3D Sprite = null!;

    private static SpriteFrames? _sharedFrames;

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
        PlayClip("idle");
    }

    /// <summary>
    /// Builds SpriteFrames from the generated strip, driven by the frame map
    /// that tools/gen_sprite.py emits, so adding frames needs no code change.
    /// Shared across fighters; the atlas is immutable.
    /// </summary>
    private static SpriteFrames LoadFrames()
    {
        if (_sharedFrames is not null) return _sharedFrames;

        var sheet = GD.Load<Texture2D>("res://assets/sprites/clown_16bit.png");
        using FileAccess f = FileAccess.Open(
            "res://assets/sprites/clown_16bit.frames.json", FileAccess.ModeFlags.Read);
        var meta = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        int fw = (int)meta["frame_width"];
        int fh = (int)meta["frame_height"];
        var clips = meta["clips"].AsGodotDictionary();

        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        foreach (Variant key in clips.Keys)
        {
            string name = key.AsString();
            frames.AddAnimation(name);
            frames.SetAnimationLoop(name, name is "idle" or "walk");
            frames.SetAnimationSpeed(name, name switch
            {
                "idle" => 3.0,
                "walk" => 12.0,
                "attack" => 14.0,
                _ => 8.0,
            });
            foreach (Variant idx in clips[key].AsGodotArray())
            {
                frames.AddFrame(name, new AtlasTexture
                {
                    Atlas = sheet,
                    Region = new Rect2((int)idx * fw, 0, fw, fh),
                    FilterClip = true,
                });
            }
        }
        _sharedFrames = frames;
        return frames;
    }

    /// <summary>Sprite settings are load-bearing; see ADR 0002.</summary>
    private static AnimatedSprite3D BuildSprite()
    {
        var s = new AnimatedSprite3D
        {
            SpriteFrames = LoadFrames(),
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

    protected void PlayClip(string name, bool restart = false)
    {
        if (!restart && Sprite.Animation == name && Sprite.IsPlaying()) return;
        Sprite.SpeedScale = 1f;
        Sprite.Play(name);
    }

    /// <summary>Pin a clip to an exact frame, so swings match attack phases.</summary>
    protected void PinClipFrame(string name, int frame)
    {
        if (Sprite.Animation != name) Sprite.Animation = name;
        Sprite.Pause();
        Sprite.SpeedScale = 0f;
        Sprite.Frame = Mathf.Clamp(frame, 0, Sprite.SpriteFrames.GetFrameCount(name) - 1);
    }

    /// <summary>True while the hit recoil clip owns the sprite.</summary>
    protected bool InHitAnim => _hitAnimRemaining > 0f;

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
            Sprite.Modulate = _flashRemaining > 0f ? new Color(3f, 2f, 2f) : BaseTint;
        }
        if (_hitAnimRemaining > 0f) _hitAnimRemaining -= dt;

        // Squash-and-stretch decays back to rest, giving impacts a physical pop.
        _squash = Mathf.MoveToward(_squash, 0f, 4f * dt);
        Sprite.Scale = new Vector3(1f + _squash * 0.5f, 1f - _squash * 0.5f, 1f);

        Tick(dt);

        if (_knockback.LengthSquared() > 0.001f)
        {
            Velocity += _knockback;
            _knockback = _knockback.MoveToward(Vector3.Zero, 40f * dt);
        }

        // Face the way we are travelling, so the sprite never moonwalks.
        if (Mathf.Abs(Facing.X) > 0.05f) Sprite.FlipH = Facing.X < 0f;

        MoveAndSlide();
    }

    protected virtual void Tick(float dt) { }

    /// <summary>Per-fighter tint, so subclasses can recolour without fighting the flash.</summary>
    protected Color BaseTint { get; set; } = Colors.White;

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
        _hitAnimRemaining = 0.22f;
        _squash = 0.35f;
        Sprite.Modulate = new Color(3f, 2f, 2f);
        PlayClip("hit", restart: true);

        if (IsDead) Died?.Invoke(this);
    }

    public void ApplyHitstop(float seconds)
    {
        HitstopRemaining = Mathf.Max(HitstopRemaining, seconds);
        _squash = Mathf.Max(_squash, 0.18f);
    }

    public void Heal(float amount) => Health = Mathf.Min(MaxHealth, Health + amount);
}
