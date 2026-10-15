namespace ClownSquad.Actors;

using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Visuals;
using ClownSquad.World;

// The rank and file beyond the grunt. Each brings one idea to the fight:
// a thrower to chase down, a charger to sidestep, a blocker to get behind,
// a summoner to reach first, a tall one to topple, and a mime.

/// <summary>Keeps its distance and lobs juggling pins onto where you stand.</summary>
public partial class JugglerEnemy : CastFighter
{
    protected override string Cast => "juggler";
    protected override float ReachScale => 0.8f;
    public override float XpScale => 1.3f;
    public override string AttackSound => "clack";

    public override void _Ready()
    {
        MaxHealth = 50f;
        MoveSpeed = 3.0f;
        DamageMultiplier = 0.6f;
        base._Ready();
        AddMove("toss", 2.5f, 10f, 1.6f,
            () => Sfx.Play("whoosh", 1.4f, -6f),
            dt =>
            {
                Stop(dt);
                PinClipFrame("special", MoveTime < 0.3f ? 0 : 1);
                if (MoveTime >= 0.3f && MoveTime - dt < 0.3f && Target() is Fighter t)
                {
                    Vector3 lead = t.GlobalPosition + Flat(t.Velocity) * 0.35f;
                    var shot = EnemyShot.Lob(this, EnemyShot.Pin(1.3f), GlobalPosition + new Vector3(Facing.X * 0.3f, 1.5f, 0f), InView(lead, 0f), 0.75f);
                    shot.Damage = 9f;
                    Sfx.Play("fwee", 1.3f, -4f);
                }
                return MoveTime > 0.65f;
            });
    }

    public override BrawlerAi MakeAi(int gruntCooldownMs) =>
        new(this, attackRange: 1.2f, backoffRange: 0.6f, attackCooldownMs: (ulong)gruntCooldownMs,
            specialCooldownMs: 1700, specialRange: 10f, keepAway: 5.5f);
}

/// <summary>Pedals in place, then charges straight down the lane. Sidestep and punish the wobble after.</summary>
public partial class UnicyclistEnemy : CastFighter
{
    protected override string Cast => "unicyclist";
    public override float XpScale => 1.4f;
    private Vector3 _dir;
    private readonly HashSet<Fighter> _run = new();

    public override void _Ready()
    {
        MaxHealth = 55f;
        MoveSpeed = 4.0f;
        DamageMultiplier = 0.6f;
        base._Ready();
        AddMove("charge", 2f, 9f, 3.2f,
            () =>
            {
                _dir = Facing;
                _run.Clear();
                Track(Telegraph.Lane(Stage, GlobalPosition, _dir, 8f, 1.3f));
                Sfx.Play("whirr", 1.3f, -4f);
            },
            dt =>
            {
                PinClipFrame("special", (int)(MoveTime * 14f) % 4);
                if (MoveTime < 0.55f) { Stop(dt); return false; }
                if (MoveTime < 1.25f)
                {
                    if (MoveTime - dt < 0.55f) { ClearTelegraphs(); Sfx.Play("zip", 0.9f); }
                    Velocity = new Vector3(_dir.X * 11f, Velocity.Y, 0f);
                    HitPlayers(f => Mathf.Abs(f.GlobalPosition.X - GlobalPosition.X) < 0.8f && Mathf.Abs(f.GlobalPosition.Z - GlobalPosition.Z) < 0.65f,
                               14f, 6f, 5.5f, 0.4f, 0.08f, _dir, _run);
                    return false;
                }
                Stop(dt);
                PinClipFrame("hit", 0);            // dizzy after the run: the punish window
                return MoveTime > 2.0f;
            });
    }

    public override BrawlerAi MakeAi(int gruntCooldownMs) =>
        new(this, attackRange: 1.4f, backoffRange: 0.7f, attackCooldownMs: (ulong)gruntCooldownMs,
            specialCooldownMs: 2600, specialRange: 9f);
}

/// <summary>Blocks hits from the front; grabs, hoists and slams. Get behind him.</summary>
public partial class WeightlifterEnemy : CastFighter
{
    protected override string Cast => "weightlifter";
    protected override float ReachScale => 1.1f;
    public override float BodyHeight => 1.6f;
    public override float BodyHalfWidth => 0.55f;
    public override float XpScale => 2f;
    public override string AttackSound => "bonk";
    private Fighter? _held;

    public override void _Ready()
    {
        MaxHealth = 120f;
        MoveSpeed = 2.5f;
        DamageMultiplier = 0.85f;
        base._Ready();
        AddMove("guard", 0f, 4f, 3.5f,
            () => Guarding = true,
            dt => { Stop(dt); PinClipFrame("guard", 0); return MoveTime > 1.6f; }, weight: 1.2f);
        AddMove("grab", 0f, 1.8f, 4.5f,
            () => Sfx.Play("honk_low", 1.1f, -4f),
            dt =>
            {
                if (_held is null)
                {
                    PinClipFrame("special", 0);
                    if (MoveTime < 0.35f) { ApplyGroundMovement(Facing * 4f, dt); }
                    else if (MoveTime < 0.45f)
                    {
                        foreach (Fighter f in Players())
                        {
                            Vector3 d = f.GlobalPosition - GlobalPosition;
                            if (f.CanBeHit() && d.X * Facing.X > -0.1f && d.X * Facing.X < 1.3f && Mathf.Abs(d.Z) < 0.6f)
                            {
                                _held = f;
                                f.Stagger(2f);
                                f.Hold = () => GlobalPosition + new Vector3(Facing.X * 0.15f, Mathf.Min(1f, (MoveTime - 0.4f) * 3f) * BodyHeight * 1.05f, 0.05f);
                                Sfx.Play("boing", 0.8f);
                                break;
                            }
                        }
                    }
                    else { Stop(dt); return MoveTime > 1.0f; }
                    return false;
                }
                PinClipFrame("special", MoveTime < 1.1f ? 1 : 2);
                if (MoveTime > 1.1f) Slam();
                return MoveTime > 1.5f;
            });
    }

    private void Slam()
    {
        if (_held is null) return;
        Fighter f = _held;
        _held = null;
        f.Hold = null;
        f.GlobalPosition = GlobalPosition + new Vector3(Facing.X * 1.0f, 0f, 0f);
        f.TakeDamage(new DamageInfo(16f * DamageMultiplier, Facing, 6f, 0.14f, this, 7f, 0.5f));
        ImpactEffect.Spawn(Stage, f.GlobalPosition + new Vector3(0f, 0.2f, 0f), 1.2f);
        CameraShake.Request(this, 0.12f, 0.25f);
        Sfx.Play("thump");
    }

    protected override void OnMoveEnded()
    {
        Guarding = false;
        if (_held is not null) { _held.Hold = null; _held = null; }
    }
}

/// <summary>Twists balloon dogs and sends them in. Reach the artist first.</summary>
public partial class BalloonistEnemy : CastFighter
{
    protected override string Cast => "balloonist";
    public override float XpScale => 1.4f;
    public override string AttackSound => "pop";
    private readonly List<Fighter> _dogs = new();

    public override void _Ready()
    {
        MaxHealth = 60f;
        MoveSpeed = 2.7f;
        DamageMultiplier = 0.55f;
        base._Ready();
        var twist = AddMove("twist", 0f, 12f, 4.5f,
            () => Sfx.Play("squeak", 1.4f),
            dt =>
            {
                Stop(dt);
                PinClipFrame("special", Mathf.Min(2, (int)(MoveTime / 0.35f)));
                if ((int)(MoveTime * 10f) != (int)((MoveTime - dt) * 10f)) Sfx.Play("squeak", 1.2f + MoveTime * 0.6f, -8f);
                if (MoveTime >= 1.0f)
                {
                    var dog = new BalloonDog { Name = "BalloonDog", DisplayName = "BALLOON DOG" };
                    Vector3 at = InView(GlobalPosition + new Vector3(Facing.X * 0.8f, 0f, 0.3f));
                    if (WaveDirector.Running is WaveDirector d) d.Summon(dog, at);
                    _dogs.Add(dog);
                    Sfx.Play("pop", 1.4f);
                    return true;
                }
                return false;
            });
        twist.Allowed = () => { _dogs.RemoveAll(d => !IsInstanceValid(d) || d.IsDead); return _dogs.Count < 2; };
    }

    public override BrawlerAi MakeAi(int gruntCooldownMs) =>
        new(this, attackRange: 1.3f, backoffRange: 0.6f, attackCooldownMs: (ulong)gruntCooldownMs,
            specialCooldownMs: 2500, specialRange: 12f, keepAway: 4.5f);
}

/// <summary>A balloon animal: small, quick and nippy, pops in a couple of hits.</summary>
public partial class BalloonDog : CastFighter
{
    protected override string Cast => "balloondog";
    protected override float ReachScale => 0.6f;
    public override float BodyHeight => 0.8f;
    public override float BodyHalfWidth => 0.35f;
    public override float XpScale => 0.3f;
    public override bool RollsRank => false;
    public override string HurtSound => "squeak";
    public override string DeathSound => "pop";
    public override string AttackSound => "squeak";

    public override void _Ready()
    {
        MaxHealth = 18f;
        MoveSpeed = 5.2f;
        DamageMultiplier = 0.4f;
        base._Ready();
    }
}

/// <summary>Tall and hard to hurt on his stilts. Four quick hits topple him; then he is easy meat.</summary>
public partial class StiltWalkerEnemy : CastFighter
{
    protected override string Cast => "stiltwalker";
    protected override float ReachScale => 1.3f;
    public override float BodyHeight => 2.5f;
    public override float XpScale => 1.8f;
    protected override string StunClip => _toppled > 0f ? "down" : "hit";
    private readonly List<double> _hits = new();
    private float _toppled;
    private Vector3 _foot;

    public override void _Ready()
    {
        MaxHealth = 85f;
        MoveSpeed = 2.6f;
        DamageMultiplier = 0.75f;
        base._Ready();
        AddMove("stomp", 0f, 2.6f, 3.2f,
            () =>
            {
                _foot = GlobalPosition + new Vector3(Facing.X * 1.2f, 0f, 0f);
                Track(Telegraph.Circle(Stage, _foot, 1.3f, 0.6f));
            },
            dt =>
            {
                Stop(dt);
                PinClipFrame("special", MoveTime < 0.6f ? 0 : 1);
                if (MoveTime >= 0.6f && MoveTime - dt < 0.6f)
                {
                    HitPlayers(f => Near(f, _foot, 1.3f), 16f, 5f, 5f, 0.4f, 0.1f);
                    ImpactEffect.Spawn(Stage, _foot + new Vector3(0f, 0.1f, 0f), 1.3f);
                    CameraShake.Request(this, 0.08f, 0.2f);
                    Sfx.Play("thump", 0.8f);
                }
                return MoveTime > 1.0f;
            });
    }

    protected override void TickAlways(float dt)
    {
        base.TickAlways(dt);
        if (_toppled > 0f) _toppled -= dt;
    }

    public override void TakeDamage(in DamageInfo info)
    {
        if (!CanBeHit()) return;
        float scale = _toppled > 0f ? 1.5f : 0.6f;
        var scaled = new DamageInfo(info.Amount * scale, info.KnockbackDirection, info.KnockbackForce * (_toppled > 0f ? 1f : 0.3f),
                                    info.Hitstop, info.Source, _toppled > 0f ? info.Launch : 0f, info.Stun);
        base.TakeDamage(scaled);
        if (IsDead || _toppled > 0f) return;
        _hits.Add(Clock);
        _hits.RemoveAll(t => Clock - t > 2.5);
        if (_hits.Count >= 4)
        {
            _hits.Clear();
            _toppled = 2.6f;
            Stagger(2.6f);
            Sfx.Play("slide_down", 0.7f);
            Sfx.Play("thump", 0.7f);
            PopText.Spawn(Stage, GlobalPosition + new Vector3(0f, 1.2f, 0f), "TIMBER!", new Color(1f, 0.8f, 0.3f), 0.9f, 0.8f);
        }
    }
}

/// <summary>Pulls you in on an invisible rope, and walls you off with a pane of nothing.</summary>
public partial class MimeEnemy : CastFighter
{
    protected override string Cast => "mime";
    public override float XpScale => 1.5f;
    public override string AttackSound => "slap";
    public override string HurtSound => "squeak";
    private Fighter? _pulled;
    private Vector3 _from;

    public override void _Ready()
    {
        MaxHealth = 55f;
        MoveSpeed = 3.5f;
        DamageMultiplier = 0.6f;
        base._Ready();
        AddMove("rope", 2.5f, 7.5f, 5f,
            () => { _pulled = null; Sfx.Play("whoosh", 0.7f, -6f); },
            dt =>
            {
                Stop(dt);
                if (MoveTime < 0.4f) { PinClipFrame("special", 0); return false; }
                if (_pulled is null)
                {
                    if (Target() is not Fighter t || !t.CanBeHit()) return true;
                    _pulled = t;
                    _from = t.GlobalPosition;
                    t.Stagger(1.4f);
                    PopText.Spawn(Stage, t.GlobalPosition + new Vector3(0f, t.BodyHeight + 0.3f, 0f), "?!", Colors.White, 0.8f, 0.5f);
                }
                float k = Mathf.Clamp((MoveTime - 0.4f) / 0.7f, 0f, 1f);
                Vector3 to = GlobalPosition + new Vector3(Facing.X * 1.1f, 0f, 0f);
                _pulled.Hold = () => _from.Lerp(to, Mathf.SmoothStep(0f, 1f, k));
                PinClipFrame("special", 1 + (int)(MoveTime * 8f) % 2);
                if (k >= 1f)
                {
                    Fighter f = _pulled;
                    _pulled = null;
                    f.Hold = null;
                    PinClipFrame("attack", 2);
                    f.TakeDamage(new DamageInfo(11f * DamageMultiplier, Facing, 5f, 0.1f, this, 4f, 0.45f));
                    Sfx.Play("slap", 0.9f);
                    return true;
                }
                return false;
            });
        AddMove("wall", 0.8f, 5f, 8f,
            () =>
            {
                if (Target() is not Fighter t) return;
                float x = GlobalPosition.X + (t.GlobalPosition.X - GlobalPosition.X) * 0.5f;
                InvisibleWall.Raise(Stage, x, Mathf.Sign(t.GlobalPosition.X - GlobalPosition.X), 4f);
            },
            dt => { Stop(dt); PinClipFrame("wall", 0); return MoveTime > 0.8f; });
    }

    protected override void OnMoveEnded()
    {
        if (_pulled is not null) { _pulled.Hold = null; _pulled = null; }
    }
}

/// <summary>
/// The mime's wall: a pane you cannot see but cannot pass, for a few seconds.
/// Players on the far side are kept there. A shimmer gives it away.
/// </summary>
public partial class InvisibleWall : Node3D
{
    private float _x;
    private float _side;     // which side the players are on
    private float _left;
    private MeshInstance3D _pane = null!;
    private StandardMaterial3D _mat = null!;

    public static void Raise(Node parent, float x, float playerSide, float seconds)
    {
        var w = new InvisibleWall { _x = x, _side = playerSide, _left = seconds };
        parent.AddChild(w);
        w.GlobalPosition = new Vector3(x, 0f, 0f);
        Sfx.Play("ding", 1.6f, -8f);
    }

    public override void _Ready()
    {
        _mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.85f, 0.95f, 1f, 0.06f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        _pane = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 2.4f, 7f) }, MaterialOverride = _mat, Position = new Vector3(0f, 1.2f, 0f) };
        AddChild(_pane);
    }

    public override void _PhysicsProcess(double delta)
    {
        _left -= (float)delta;
        _mat.AlbedoColor = new Color(0.85f, 0.95f, 1f, 0.05f + 0.04f * Mathf.Sin(_left * 9f));
        if (_left <= 0f) { QueueFree(); return; }
        foreach (Node n in GetTree().GetNodesInGroup(Fighter.Group))
        {
            if (n is not Fighter f || f.Hostile || f.IsDead) continue;
            float rel = f.GlobalPosition.X - _x;
            if (Mathf.Sign(rel) != _side || Mathf.Abs(rel) < 0.35f)
            {
                f.GlobalPosition = new Vector3(_x + _side * 0.4f, f.GlobalPosition.Y, f.GlobalPosition.Z);
                if (Mathf.Abs(rel) < 0.5f) _mat.AlbedoColor = new Color(0.9f, 1f, 1f, 0.25f);
            }
        }
    }
}
