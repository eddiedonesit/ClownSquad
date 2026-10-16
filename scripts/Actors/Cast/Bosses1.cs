namespace ClownSquad.Actors;

using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Combat;
using ClownSquad.Visuals;
using ClownSquad.World;
using ClownSquad.Net;

/// <summary>
/// BOBO THE BALANCER (world 2): a juggler standing on a giant beach ball.
/// Lobs volleys of pins, rolls the ball down the lane like a bowling ball,
/// and bounces across the ring on it, coming down hard each time.
/// </summary>
public partial class BallRiderBoss : BossFighter
{
    protected override string Cast => "ballrider";
    private float BallR => 0.75f * Big;
    public override float BodyHeight => 3.6f;
    public override float BodyHalfWidth => 0.85f * Big;
    protected override float ReachScale => 1.2f * Big;
    protected override string Tagline => "HE NEVER DROPS A PIN";
    protected override string[] Signature => new[] { "ball_bounce", "squeak" };
    private MeshInstance3D _ball = null!;
    private float _lastX;
    private Vector3 _rollDir;
    private int _hops;
    private readonly HashSet<Fighter> _rolled = new();

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "BOBO THE BALANCER";
        MaxHealth = 420f;
        MoveSpeed = 3.0f;
        Acceleration = 20f;
        base._Ready();
        VisualBase = new Vector3(0f, BallR * 1.9f, 0f);
        _ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = BallR, Height = BallR * 2f, RadialSegments = 24, Rings = 12 },
            MaterialOverride = Kit3D.Stripes(new Color(0.95f, 0.3f, 0.35f), new Color(0.98f, 0.95f, 0.9f), 6, rough: 0.35f),
            Position = new Vector3(0f, BallR, 0f),
        };
        AddChild(_ball);
        _lastX = GlobalPosition.X;

        AddMove("volley", 2f, 40f, 2.5f, () => Fx.Sound("whoosh", 1.2f), dt =>
        {
            Stop(dt);
            int count = PhaseNumber == 1 ? 3 : 5;
            PinClipFrame("special", MoveTime < 0.45f ? 0 : 1);
            for (int i = 0; i < count; i++)
            {
                float at = 0.45f + i * 0.14f;
                if (MoveTime >= at && MoveTime - dt < at && Target() is Fighter t)
                {
                    float spread = (i - (count - 1) * 0.5f) * 1.2f;
                    Vector3 to = t.GlobalPosition + new Vector3(spread, 0f, (i % 2 == 0 ? 0.6f : -0.6f) * (i == 0 ? 0f : 1f));
                    var s = EnemyShot.Lob(this, EnemyShot.Pin(1.5f), GlobalPosition + new Vector3(Facing.X * 0.4f, 2.8f, 0f), InView(to, 0f), 0.9f);
                    s.Damage = 10f;
                    Fx.Sound("fwee", 1.2f + i * 0.05f, -6f);
                }
            }
            return MoveTime > 0.45f + count * 0.14f + 0.4f;
        }, weight: 2f);

        AddMove("roll", 2.5f, 40f, 6f, () =>
        {
            _rollDir = Facing;
            _rolled.Clear();
            Track(Telegraph.Lane(Stage, GlobalPosition, _rollDir, 12f, 1.7f));
            Fx.Sound("honk_low", 0.9f);
        }, dt =>
        {
            PinClipFrame("special", 1);
            if (MoveTime < 0.8f) { Stop(dt); return false; }
            if (MoveTime - dt < 0.8f) { ClearTelegraphs(); Fx.Sound("whirr", 0.7f); }
            Velocity = new Vector3(_rollDir.X * 10.5f, Velocity.Y, 0f);
            HitPlayers(f => Mathf.Abs(f.GlobalPosition.X - GlobalPosition.X) < 1.0f && Mathf.Abs(f.GlobalPosition.Z - GlobalPosition.Z) < 0.85f,
                       18f, 7f, 6f, 0.45f, 0.1f, _rollDir, _rolled);
            bool wall = Arena.Current is Arena a && Mathf.Abs(a.Clamp(GlobalPosition + _rollDir * 0.6f, BodyHeight, BodyHalfWidth, out _).X - (GlobalPosition.X + _rollDir.X * 0.6f)) > 0.01f;
            return MoveTime > 2.0f || wall;
        });

        AddMove("bounce", 0f, 9f, 7f, () => { _hops = 0; Hop(); }, dt =>
        {
            PinClipFrame("special", 1);
            if (!TickLeap(dt)) return false;
            ClearTelegraphs();
            HitPlayers(f => Near(f, GlobalPosition, 1.6f), 16f, 6f, 6f, 0.4f, 0.12f);
            Fx.Impact(Stage, GlobalPosition + new Vector3(0f, 0.1f, 0f), 1.8f);
            Fx.Shake(this, 0.12f, 0.25f);
            Fx.Sound("boing", 0.6f);
            if (++_hops < (PhaseNumber >= 2 ? 3 : 2)) { Hop(); return false; }
            return true;
        });
    }

    private void Hop()
    {
        Vector3 to = Target()?.GlobalPosition ?? GlobalPosition;
        float air = StartLeap(to, 1.8f);
        Track(Telegraph.Circle(Stage, LeapTarget, 1.6f, air));
    }

    protected override void TickAlways(float dt)
    {
        base.TickAlways(dt);
        // The ball rolls as far as it travels.
        float dx = GlobalPosition.X - _lastX;
        _lastX = GlobalPosition.X;
        _ball.RotateZ(-dx / BallR);
        _ball.Visible = Visual.Visible;
    }
}

/// <summary>
/// MADAME STRIPES (world 3): a lion tamer with a whip, a chair, and RAJAH the
/// tiger, who pounces when she says so. Beat her and Rajah slinks off; beat
/// Rajah first and she loses her temper.
/// </summary>
public partial class TrainerBoss : BossFighter
{
    protected override string Cast => "trainer";
    protected override float ReachScale => 1.25f * Big;
    public override float BodyHeight => 1.9f * Big;
    protected override string Tagline => "AND RAJAH, THE TERROR OF THE BIG TOP";
    protected override string[] Signature => new[] { "whip_crack", "tiger_roar" };
    public TigerFighter? Tiger;
    private bool _alone;

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "MADAME STRIPES";
        MaxHealth = 380f;
        MoveSpeed = 3.0f;
        base._Ready();

        AddMove("crack", 1.4f, 5.6f, 2.4f, () =>
        {
            Track(Telegraph.Lane(Stage, GlobalPosition, Facing, 5f, 0.9f, 0.5f));
            Fx.Sound("whoosh", 0.8f);
        }, dt =>
        {
            Stop(dt);
            PinClipFrame("attack", MoveTime < 0.25f ? 0 : MoveTime < 0.5f ? 1 : 2);
            if (MoveTime >= 0.5f && MoveTime - dt < 0.5f)
            {
                Fx.Sound("whip_crack", 1.05f);
                HitPlayers(f => Telegraph.InLane(f.GlobalPosition, GlobalPosition, Facing, 5f, 0.9f), 14f, 5f, 0f, 0.6f, 0.1f, Facing);
            }
            return MoveTime > 0.85f;
        }, weight: 1.5f);

        AddMove("hup", 0f, 40f, 4.5f, () =>
        {
            Fx.Sound("whip_crack", 1.25f);
            Fx.Pop(Stage, GlobalPosition + new Vector3(0f, BodyHeight + 0.3f, 0f), "HUP!", new Color(1f, 0.85f, 0.3f), 0.8f, 0.6f);
            if (Tiger is not null && IsInstanceValid(Tiger) && !Tiger.IsDead) Tiger.Order(PhaseNumber >= 3 && GD.Randf() < 0.4f ? "roar" : "pounce");
        }, dt => { Stop(dt); PinClipFrame("special", 0); return MoveTime > 0.6f; }, weight: 2f).Allowed = () => !_alone;

        AddMove("hoop", 0f, 40f, 7f, () =>
        {
            Fx.Sound("whoosh", 0.6f);
            if (Target() is Fighter t) Hazard.Fire(this, t.GlobalPosition, 1.7f, 4.5f, 6f, 0.8f, thickness: 0.55f);
        }, dt => { Stop(dt); PinClipFrame("hoop", 0); return MoveTime > 0.8f; }).Allowed = () => PhaseNumber >= 2 || _alone;
    }

    protected override void TickAlways(float dt)
    {
        base.TickAlways(dt);
        if (!_alone && Tiger is not null && (!IsInstanceValid(Tiger) || Tiger.IsDead))
        {
            _alone = true;
            Fx.Pop(Stage, GlobalPosition + new Vector3(0f, BodyHeight + 0.4f, 0f), "RAJAH!!", new Color(1f, 0.4f, 0.3f), 1.1f, 1f);
            MoveSpeed *= 1.3f;
            DamageMultiplier *= 1.25f;
        }
        if (IsDead && Tiger is not null && IsInstanceValid(Tiger) && !Tiger.IsDead)
        {
            WaveDirector.Running?.Dismiss(Tiger);
            Tiger = null;
        }
    }
}

/// <summary>RAJAH: pounces on command (and on its own), roars to stagger. Not a boss itself.</summary>
public partial class TigerFighter : CastFighter
{
    protected override string Cast => "tiger";
    protected override float DrawScale => 1.6f;
    protected override float ReachScale => 1.7f;
    public override float BodyHeight => 2.0f;
    public override float BodyHalfWidth => 1.3f;
    public override float XpScale => 4f;
    public override bool RollsRank => false;
    protected override float LaunchScale => 0f;
    protected override float StunScale => 0.6f;
    protected override bool HasPoise => InSpecial;
    public override string AttackSound => "slap";
    public override string HurtSound => "honk_low";
    public override string DeathSound => "wah_short";
    private Vector3 _aim;

    // Rajah waits beside his trainer while she is introduced.
    protected override void Tick(float dt)
    {
        if (Clock < ClownSquad.UI.BossIntro.Seconds) { Stop(dt); PlayClip("idle"); return; }
        base.Tick(dt);
    }

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "RAJAH";
        MaxHealth = 170f;
        MoveSpeed = 4.4f;
        DamageMultiplier = 0.9f;
        base._Ready();

        AddMove("pounce", 2.2f, 9f, 4f, () =>
        {
            _aim = Target()?.GlobalPosition ?? GlobalPosition + Facing * 4f;
            Track(Telegraph.Circle(Stage, _aim, 1.4f, 0.65f));
            Fx.Sound("tiger_roar", 1.4f, -4f);
        }, dt =>
        {
            if (MoveTime < 0.65f) { Stop(dt); PinClipFrame("special", 0); return false; }
            if (MoveTime - dt < 0.65f) { StartLeap(_aim, 1.6f); Fx.Sound("whoosh", 0.7f); }
            PinClipFrame("air", 0);
            if (!TickLeap(dt)) return false;
            ClearTelegraphs();
            HitPlayers(f => Near(f, GlobalPosition + Facing * 0.6f, 1.4f), 15f, 6f, 5f, 0.45f, 0.1f);
            Dust(1.2f);
            Fx.Sound("thump", 0.8f);
            return true;
        });

        AddMove("roar", 0f, 4f, 8f, () =>
        {
            Track(Telegraph.Cone(Stage, GlobalPosition, Facing, 4.2f, 75f, 0.55f));
        }, dt =>
        {
            Stop(dt);
            PinClipFrame(MoveTime < 0.55f ? "special" : "roar", 0);
            if (MoveTime >= 0.55f && MoveTime - dt < 0.55f)
            {
                Fx.Sound("tiger_roar");
                Fx.Shake(this, 0.15f, 0.4f);
                foreach (Fighter f in new List<Fighter>(Players()))
                    if (f.CanBeHit() && Telegraph.InCone(f.GlobalPosition, GlobalPosition, Facing, 4.2f, 75f)) f.Stagger(1.0f);
                HitPlayers(f => Telegraph.InCone(f.GlobalPosition, GlobalPosition, Facing, 4.2f, 75f), 4f, 5f);
            }
            return MoveTime > 1.3f;
        });
    }
}

/// <summary>
/// THE JALOPY (world 4): a clown car with no driver anyone can see. Rams
/// down the lane, honks you off your feet, backfires, and stops now and then
/// to let out more clowns than could possibly fit.
/// </summary>
public partial class ClownCarBoss : BossFighter
{
    protected override string Cast => "clowncar";
    protected override float DrawScale => 1.0f;
    public override float BodyHeight => 2.3f * Big;
    public override float BodyHalfWidth => 1.9f * Big;
    protected override float ReachScale => 1.45f * Big;
    protected override bool HasPoise => true;
    protected override float StunScale => 0.1f;
    public override string AttackSound => "bonk";
    public override string HurtSound => "clack";
    protected override string Tagline => "ROOM FOR ONE MORE";
    protected override string[] Signature => new[] { "car_horn", "engine" };
    private Vector3 _dir;
    private readonly HashSet<Fighter> _rammed = new();
    private readonly List<PlayerFighter> _passengers = new();

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "THE JALOPY";
        MaxHealth = 520f;
        MoveSpeed = 3.4f;
        Acceleration = 14f;
        Friction = 14f;
        base._Ready();

        AddMove("ram", 0f, 40f, 5f, () =>
        {
            _dir = Facing;
            _rammed.Clear();
            Track(Telegraph.Lane(Stage, GlobalPosition - _dir * 1.5f, _dir, 15f, 2.0f));
            Fx.Sound("honk_low", 1.2f);
            Fx.Sound("car_horn");
        }, dt =>
        {
            PinClipFrame("walk", (int)(MoveTime * 16f) % 6);
            if (MoveTime < 0.9f) { ApplyGroundMovement(-_dir * 1.5f, dt); return false; }      // revving back
            if (MoveTime - dt < 0.9f) { ClearTelegraphs(); Fx.Sound("engine", 1.2f); }
            Velocity = new Vector3(_dir.X * 12f, Velocity.Y, 0f);
            HitPlayers(f => Mathf.Abs(f.GlobalPosition.X - (GlobalPosition.X + _dir.X * 1.6f)) < 1.0f && Mathf.Abs(f.GlobalPosition.Z - GlobalPosition.Z) < 1.0f,
                       20f, 8f, 7f, 0.5f, 0.12f, _dir, _rammed);
            bool wall = Arena.Current is Arena a && a.Clamp(GlobalPosition + _dir * 0.5f, BodyHeight, BodyHalfWidth, out int side) != GlobalPosition + _dir * 0.5f && side != 0;
            if (wall && MoveTime > 1.0f) { Fx.Shake(this, 0.15f, 0.3f); Fx.Sound("kaboom", 0.7f, -4f); return true; }
            return MoveTime > 2.4f;
        }, weight: 1.6f);

        AddMove("unload", 0f, 40f, 7f, () => Fx.Sound("clack", 0.6f), dt =>
        {
            Stop(dt);
            PinClipFrame("special", MoveTime < 0.3f ? 0 : 1);
            int count = PhaseNumber == 3 ? 3 : 2;
            for (int i = 0; i < count; i++)
            {
                float at = 0.4f + i * 0.35f;
                if (MoveTime >= at && MoveTime - dt < at)
                {
                    PlayerFighter p = i == 2 || (PhaseNumber >= 2 && i == 1) ? new JugglerEnemy { DisplayName = "PASSENGER" } : new GruntClown { DisplayName = "PASSENGER" };
                    Summon(p, GlobalPosition + new Vector3(Facing.X * 0.3f, 0f, 0.9f));
                    _passengers.Add(p);
                    Fx.Sound("boing_small", 1.1f + i * 0.1f);
                }
            }
            return MoveTime > 0.5f + count * 0.35f;
        }).Allowed = () => { _passengers.RemoveAll(p => !IsInstanceValid(p) || p.IsDead); return _passengers.Count < 3; };

        AddMove("honk", 0f, 4.5f, 4f, () =>
        {
            Track(Telegraph.Cone(Stage, GlobalPosition + Facing * 1.6f, Facing, 4f, 60f, 0.5f));
        }, dt =>
        {
            Stop(dt);
            PinClipFrame(MoveTime < 0.5f ? "idle" : "honk", 0);
            if (MoveTime >= 0.5f && MoveTime - dt < 0.5f)
            {
                Fx.Sound("car_horn", 0.8f, 3f);
                Fx.Shake(this, 0.1f, 0.3f);
                HitPlayers(f => Telegraph.InCone(f.GlobalPosition, GlobalPosition + Facing * 1.6f, Facing, 4f, 60f), 6f, 10f, 4f, 0.6f);
            }
            return MoveTime > 1.0f;
        });

        AddMove("backfire", 0f, 40f, 5.5f, () =>
        {
            Track(Telegraph.Circle(Stage, GlobalPosition - Facing * 2.4f, 1.7f, 0.6f));
        }, dt =>
        {
            Stop(dt);
            PinClipFrame(MoveTime < 0.6f ? "idle" : "honk", 0);
            if (MoveTime >= 0.6f && MoveTime - dt < 0.6f)
            {
                Vector3 at = GlobalPosition - Facing * 2.4f;
                Fx.Impact(Stage, at + new Vector3(0f, 0.4f, 0f), 2f);
                Fx.Sound("kaboom", 1.1f);
                HitPlayers(f => Near(f, at, 1.7f), 16f, 7f, 6f, 0.4f, 0.1f);
            }
            return MoveTime > 1.0f;
        }).Allowed = HasPlayerBehind;
    }

    private bool HasPlayerBehind()
    {
        foreach (Fighter f in Players())
            if ((f.GlobalPosition.X - GlobalPosition.X) * Facing.X < -0.5f) return true;
        return false;
    }

    protected override void TickAlways(float dt)
    {
        base.TickAlways(dt);
        if (IsDead && _passengers.Count > 0)
        {
            // The car is wrecked: its passengers scatter.
            foreach (PlayerFighter p in _passengers)
                if (IsInstanceValid(p) && !p.IsDead) WaveDirector.Running?.Dismiss(p);
            _passengers.Clear();
            Fx.Impact(Stage, GlobalPosition + new Vector3(0f, 1f, 0f), 3f);
        }
    }
}
