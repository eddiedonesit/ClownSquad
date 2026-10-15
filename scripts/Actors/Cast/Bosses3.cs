namespace ClownSquad.Actors;

using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Combat;
using ClownSquad.Visuals;
using ClownSquad.World;

/// <summary>
/// THE TOTEM TOTS (world 8): five little clowns standing on each other's
/// shoulders. The tower topples down the lane, throws its top tot, or comes
/// apart into a conga line that chases you round the ring. Every fifth of its
/// health knocked off, one tot gives up and runs away, and the tower shrinks.
/// </summary>
public partial class TotStackBoss : BossFighter
{
    protected override string Cast => "tot";
    private float Step => 0.7f * Big;
    protected override string Tagline => "FIVE TIMES THE TROUBLE";
    public override float BodyHeight => 0.3f + Mathf.Max(1, _count) * Step;
    public override float BodyHalfWidth => 0.5f;
    protected override bool HasPoise => InSpecial && !_lying;

    private static readonly Color[] Tints =
    {
        new(1f, 1f, 1f), new(0.75f, 1f, 0.8f), new(1f, 0.9f, 0.6f), new(0.8f, 0.85f, 1.15f), new(1.15f, 0.8f, 1f),
    };

    private readonly List<AnimatedSprite3D> _tots = new();
    private readonly List<ShaderMaterial?> _mats = new();
    private Node3D _pivot = null!;
    private int _count = 5;
    private float _lift = 0.45f;
    private int _lost;
    private bool _lying;
    private bool _conga;
    private bool _tossed;
    private float _tossBack;
    private float _flash;
    private readonly List<Vector3> _trail = new();
    private readonly Dictionary<Fighter, double> _bumped = new();
    private Vector3 _laneDir;

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "THE TOTEM TOTS";
        MaxHealth = 520f;
        MoveSpeed = 3.2f;
        base._Ready();
        Visual.Visible = false;
        _pivot = new Node3D();
        AddChild(_pivot);
        SpriteFrames frames = FrameLibrary.Load(SheetPath, ArtStyle.FramesFor(SheetPath));
        FrameLibrary.SheetInfo info = FrameLibrary.Info(SheetPath);
        float lift = (info.Baseline - info.FrameHeight * 0.5f) * SpritePixelSize;
        _lift = lift;
        for (int i = 0; i < 5; i++)
        {
            var s = new AnimatedSprite3D
            {
                SpriteFrames = frames, PixelSize = SpritePixelSize, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                AlphaCut = SpriteBase3D.AlphaCutMode.Discard, Shaded = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0f, lift + i * Step, 0.01f * i),
            };
            ShaderMaterial? m = LitSprite.For(SheetPath);
            if (m is not null) s.MaterialOverride = m;
            _pivot.AddChild(s);
            _tots.Add(s);
            _mats.Add(m);
            s.Play(i == 0 ? "idle" : "stack");
        }

        AddMove("topple", 0f, 6f, 4f, () =>
        {
            _laneDir = Facing;
            Track(Telegraph.Lane(Stage, GlobalPosition, _laneDir, _count * Step + 0.9f, 1.2f, 0.8f));
            Sfx.Play("slide_down", 1.2f);
        }, dt =>
        {
            Stop(dt);
            float tilt;
            if (MoveTime < 0.8f) tilt = Mathf.Sin(MoveTime * 30f) * 4f;                  // wobbling
            else if (MoveTime < 1.15f) tilt = Mathf.Lerp(0f, 90f, (MoveTime - 0.8f) / 0.35f);
            else if (MoveTime < 2.5f) tilt = 90f;
            else tilt = Mathf.Lerp(90f, 0f, Mathf.Clamp((MoveTime - 2.5f) / 0.5f, 0f, 1f));
            _pivot.RotationDegrees = new Vector3(0f, 0f, -tilt * _laneDir.X);
            if (MoveTime >= 1.15f && MoveTime - dt < 1.15f)
            {
                _lying = true;
                HitPlayers(f => Telegraph.InLane(f.GlobalPosition, GlobalPosition, _laneDir, _count * Step + 0.9f, 1.2f), 16f, 6f, 5f, 0.45f, 0.1f);
                CameraShake.Request(this, 0.12f, 0.25f);
                Sfx.Play("thump", 0.8f);
                Dust(1.5f);
            }
            if (MoveTime > 2.5f) _lying = false;
            return MoveTime > 3.0f;
        }, weight: 1.4f);

        AddMove("toss", 2.5f, 40f, 5f, () => Sfx.Play("whoosh", 1.3f), dt =>
        {
            Stop(dt);
            if (MoveTime >= 0.35f && MoveTime - dt < 0.35f && _count > 1 && Target() is Fighter t)
            {
                _tossed = true;
                _tossBack = 2.5f;
                var body = new Sprite3D
                {
                    Texture = ((AtlasTexture)_tots[0].SpriteFrames.GetFrameTexture("air", 0)),
                    PixelSize = SpritePixelSize, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                };
                var s = EnemyShot.Lob(this, body, GlobalPosition + new Vector3(0f, BodyHeight, 0f), InView(t.GlobalPosition, 0f), 1.0f);
                s.Damage = 8f;
                s.BurstRadius = 1.3f;
                s.BurstDamage = 12f;
                s.SpinSpeed = 7f;
                Sfx.Play("fwee", 1.4f);
            }
            return MoveTime > 0.8f;
        }).Allowed = () => _count > 1 && !_tossed;

        AddMove("conga", 0f, 40f, 7f, () =>
        {
            _conga = true;
            _trail.Clear();
            _bumped.Clear();
            Sfx.Play("kazoo_rise", 1.2f);
            PopText.Spawn(Stage, GlobalPosition + new Vector3(0f, BodyHeight + 0.3f, 0f), "CONGA!", new Color(1f, 0.85f, 0.3f), 0.9f, 0.7f);
        }, dt =>
        {
            Fighter? t = Target();
            if (t is not null) { Steer(t.GlobalPosition, MoveTime < 0.5f ? 0f : 4.6f, dt); FaceToward(t.GlobalPosition); }
            if (_trail.Count == 0 || _trail[^1].DistanceTo(GlobalPosition) > 0.12f) _trail.Add(GlobalPosition);
            if (_trail.Count > 80) _trail.RemoveAt(0);
            return MoveTime > 5f;
        }).Allowed = () => _count >= 3;
    }

    protected override void OnMoveEnded()
    {
        _lying = false;
        _conga = false;
        _pivot.RotationDegrees = Vector3.Zero;
    }

    public override void TakeDamage(in Combat.DamageInfo info)
    {
        base.TakeDamage(info);
        _flash = 0.12f;
    }

    protected override void TickAlways(float dt)
    {
        base.TickAlways(dt);
        if (_tossBack > 0f && (_tossBack -= dt) <= 0f) _tossed = false;

        // Every fifth of its health, one tot has had enough.
        int shouldLose = Mathf.Clamp((int)((1f - Health / MaxHealth) * 5f), 0, 4);
        while (_lost < shouldLose && !IsDead)
        {
            _lost++;
            _count = 5 - _lost;
            RunAway(_tots[_count]);
        }

        int shown = _count - (_tossed ? 1 : 0);
        float speed = Flat(Velocity).Length();
        _flash -= dt;
        for (int i = 0; i < _tots.Count; i++)
        {
            AnimatedSprite3D s = _tots[i];
            if (!IsInstanceValid(s) || s.GetParent() != _pivot) continue;
            s.Visible = i < shown && !(IsDead && i > 0);
            s.FlipH = Facing.X < 0f;
            string clip = _conga ? "special" : i == 0 ? (speed > 0.6f ? "walk" : "idle") : "stack";
            if (IsDead || InHitAnim) clip = "hit";
            if (s.Animation != clip) s.Play(clip);
            if (_mats[i] is ShaderMaterial m) LitSprite.Apply(m, _flash > 0f ? Colors.White : Tints[i], _flash > 0f ? 1.1f : 0f, s.FlipH);
            if (_conga && _trail.Count > 0)
            {
                int idx = Mathf.Max(0, _trail.Count - 1 - i * 5);
                s.GlobalPosition = _trail[idx] + new Vector3(0f, _lift, 0.01f * i);
            }
            else
            {
                s.Position = s.Position.Lerp(new Vector3(0f, _lift + i * Step, 0.01f * i), 1f - Mathf.Exp(-14f * dt));
            }
        }

        // A conga line hurts to walk into.
        if (_conga && MoveTime > 0.5f)
        {
            foreach (Fighter f in new List<Fighter>(Players()))
            {
                if (!f.CanBeHit() || (_bumped.TryGetValue(f, out double when) && Clock < when)) continue;
                for (int i = 0; i < _count; i++)
                {
                    if (Flat(_tots[i].GlobalPosition - f.GlobalPosition).Length() > 0.6f) continue;
                    _bumped[f] = Clock + 0.8;
                    f.TakeDamage(new Combat.DamageInfo(8f * DamageMultiplier, Flat(f.GlobalPosition - _tots[i].GlobalPosition).Normalized(), 5f, 0.05f, this, 3f, 0.3f));
                    break;
                }
            }
        }
    }

    private void RunAway(AnimatedSprite3D tot)
    {
        Vector3 at = tot.GlobalPosition;
        tot.Reparent(Stage);
        tot.GlobalPosition = at;
        tot.Play("walk");
        float dir = GD.Randf() < 0.5f ? -1f : 1f;
        tot.FlipH = dir < 0f;
        Tween tw = tot.CreateTween();
        tw.TweenProperty(tot, "global_position", new Vector3(at.X + dir * 0.6f, 0.45f, at.Z), 0.25f);
        tw.TweenProperty(tot, "global_position", new Vector3(at.X + dir * 12f, 0.45f, at.Z), 2.2f);
        tw.TweenCallback(Callable.From(tot.QueueFree));
        Sfx.Play("slide_down", 1.5f);
        PopText.Spawn(Stage, at + new Vector3(0f, 0.6f, 0f), "I QUIT!", Colors.White, 0.6f, 0.6f);
    }
}

/// <summary>
/// IRON IVAN (world 9): the strong man. Nothing stops him mid-move. Slams
/// his barbell to send a shockwave down the lane, picks you up and throws
/// you across the ring, tosses dumbbells, and flexes off your hits.
/// </summary>
public partial class StrongmanBoss : BossFighter
{
    protected override string Cast => "strongman";
    protected override float DrawScale => 1.22f;
    public override float BodyHeight => 2.6f * Big;
    public override float BodyHalfWidth => 0.95f * Big;
    protected override float ReachScale => 1.45f * Big;
    protected override bool HasPoise => true;
    protected override float StunScale => 0.12f;
    protected override float SwingTimeScale => 1.3f;
    public override string AttackSound => "bonk";
    protected override string Tagline => "STRONGEST MAN IN THE WORLD (HE SAYS)";
    private Fighter? _held;
    private bool _flex;
    private readonly HashSet<Fighter> _waved = new();

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "IRON IVAN";
        MaxHealth = 640f;
        MoveSpeed = 2.6f;
        base._Ready();

        AddMove("slam", 0f, 8f, 4.5f, () =>
        {
            _waved.Clear();
            Track(Telegraph.Circle(Stage, GlobalPosition + Facing * 1.4f, 1.7f, 0.9f));
            Track(Telegraph.Lane(Stage, GlobalPosition + Facing * 1.4f, Facing, 7f, 1.4f, 0.9f));
            Sfx.Play("honk_low", 0.6f);
        }, dt =>
        {
            Stop(dt);
            PinClipFrame("special", MoveTime < 0.45f ? 0 : MoveTime < 0.9f ? 1 : 2);
            Vector3 at = GlobalPosition + Facing * 1.4f;
            if (MoveTime >= 0.9f && MoveTime - dt < 0.9f)
            {
                HitPlayers(f => Near(f, at, 1.7f), 22f, 7f, 7f, 0.5f, 0.15f);
                ImpactEffect.Spawn(Stage, at + new Vector3(0f, 0.1f, 0f), 2.2f);
                CameraShake.Request(this, 0.2f, 0.35f);
                Sfx.Play("kaboom", 0.6f);
            }
            if (MoveTime > 0.9f && MoveTime < 1.4f)
            {
                // The shockwave runs down the lane.
                float front = (MoveTime - 0.9f) / 0.5f * 7f;
                if ((int)(front * 2f) != (int)((front - dt / 0.5f * 7f) * 2f))
                    ImpactEffect.Spawn(Stage, at + Facing * front + new Vector3(0f, 0.1f, 0f), 0.9f, glow: false);
                HitPlayers(f => Telegraph.InLane(f.GlobalPosition, at, Facing, front, 1.4f), 14f, 5f, 5f, 0.4f, 0.06f, Facing, _waved);
            }
            return MoveTime > 1.8f;
        }, weight: 1.5f);

        AddMove("grab", 0f, 2.6f, 5f, () =>
        {
            Track(Telegraph.Lane(Stage, GlobalPosition, Facing, 3.4f * Big * 0.8f, 1.4f, 0.45f));
            Sfx.Play("honk_low", 0.9f);
        }, dt =>
        {
            if (_held is null)
            {
                PinClipFrame("grab", 0);
                if (MoveTime < 0.45f) { Stop(dt); return false; }
                if (MoveTime < 0.85f) { ApplyGroundMovement(Facing * 4.5f, dt); return false; }
                if (MoveTime < 0.95f)
                {
                    foreach (Fighter f in Players())
                    {
                        Vector3 d = f.GlobalPosition - GlobalPosition;
                        if (!f.CanBeHit() || d.X * Facing.X < -0.2f || d.X * Facing.X > 1.6f * Big || Mathf.Abs(d.Z) > 0.7f) continue;
                        _held = f;
                        f.Stagger(2.5f);
                        f.Hold = () => GlobalPosition + new Vector3(0f, BodyHeight + 0.1f, 0.05f);
                        Sfx.Play("boing", 0.7f);
                        break;
                    }
                }
                Stop(dt);
                return MoveTime > 1.5f;
            }
            Stop(dt);
            PinClipFrame("grab", 1);
            if (MoveTime > 1.75f)
            {
                Fighter f = _held;
                _held = null;
                f.Hold = null;
                f.TakeDamage(new DamageInfo(18f * DamageMultiplier, Facing, 14f, 0.15f, this, 9f, 0.6f));
                Sfx.Play("whoosh", 0.6f);
                CameraShake.Request(this, 0.15f, 0.3f);
                return true;
            }
            return false;
        });

        AddMove("toss", 3f, 40f, 4f, () => { }, dt =>
        {
            Stop(dt);
            PinClipFrame("attack", MoveTime < 0.35f ? 0 : 2);
            if (MoveTime >= 0.35f && MoveTime - dt < 0.35f && Target() is Fighter t)
            {
                var s = EnemyShot.Lob(this, EnemyShot.Dumbbell(1.6f), GlobalPosition + new Vector3(Facing.X * 0.6f, 2.2f, 0f), InView(t.GlobalPosition, 0f), 0.9f);
                s.Damage = 10f;
                s.BurstRadius = 1.3f;
                s.BurstDamage = 14f;
                Sfx.Play("fwee", 0.7f);
            }
            return MoveTime > 0.8f;
        });

        AddMove("flex", 0f, 40f, 9f, () =>
        {
            _flex = true;
            Sfx.Play("crowd_cheer", 1.1f, -6f);
            PopText.Spawn(Stage, GlobalPosition + new Vector3(0f, BodyHeight + 0.3f, 0f), "FLEX!", new Color(1f, 0.85f, 0.3f), 1f, 0.8f);
        }, dt => { Stop(dt); PinClipFrame("special", 1); return MoveTime > 1.8f; }).Allowed = () => PhaseNumber >= 2;
    }

    public override void TakeDamage(in DamageInfo info)
    {
        if (_flex && CanBeHit())
        {
            Sfx.Play("clack", 0.6f);
            base.TakeDamage(new DamageInfo(info.Amount * 0.2f, info.KnockbackDirection, 0f, info.Hitstop, info.Source, 0f, 0f));
            return;
        }
        base.TakeDamage(info);
    }

    protected override void OnMoveEnded()
    {
        _flex = false;
        if (_held is not null) { _held.Hold = null; _held = null; }
    }
}

/// <summary>
/// THE RINGMASTER (world 10, the final boss). First from his little gilded
/// carriage: charging down the lane, cracking his whip, calling on his acts.
/// Wreck the carriage and he fights on foot, meaner: hat tricks full of
/// bombs and doves, and spotlights that come down where you stand.
/// </summary>
public partial class RingmasterBoss : BossFighter
{
    protected override string Cast => "ringmaster";
    public override float BodyHeight => (_mounted ? 2.6f : 2.1f) * Big;
    public override float BodyHalfWidth => (_mounted ? 1.4f : 0.55f) * Big;
    protected override float ReachScale => 1.3f * Big;
    protected override string Tagline => "LADIES AND GENTLEMEN... THE GRAND FINALE!";
    private const string CarriageSheet = Folder + "carriage.png";

    private bool _mounted = true;
    private AnimatedSprite3D _cart = null!;
    private ShaderMaterial? _cartMat;
    private Vector3 _dir;
    private readonly HashSet<Fighter> _run = new();
    private readonly List<PlayerFighter> _acts = new();
    private readonly List<Vector3> _spots = new();

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "THE RINGMASTER";
        MaxHealth = 900f;
        MoveSpeed = 4.0f;
        base._Ready();
        _cart = new AnimatedSprite3D
        {
            SpriteFrames = FrameLibrary.Load(CarriageSheet, ArtStyle.FramesFor(CarriageSheet)),
            PixelSize = SpritePixelSize, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard, Shaded = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
        };
        _cartMat = LitSprite.For(CarriageSheet);
        if (_cartMat is not null) _cart.MaterialOverride = _cartMat;
        FrameLibrary.SheetInfo info = FrameLibrary.Info(CarriageSheet);
        _cart.Position = new Vector3(0f, (info.Baseline - info.FrameHeight * 0.5f) * SpritePixelSize, 0.06f);
        AddChild(_cart);
        _cart.Play("idle");
        VisualBase = new Vector3(0f, 0.55f * Big, 0f);

        AddMove("charge", 0f, 40f, 5f, () =>
        {
            _dir = Facing;
            _run.Clear();
            Track(Telegraph.Lane(Stage, GlobalPosition, _dir, 15f, 2.0f));
            Sfx.Play("crit", 1.2f);
            PopText.Spawn(Stage, GlobalPosition + new Vector3(0f, BodyHeight + 0.3f, 0f), "HYAH!", new Color(1f, 0.85f, 0.3f), 0.8f, 0.5f);
        }, dt =>
        {
            if (MoveTime < 0.85f) { Stop(dt); return false; }
            if (MoveTime - dt < 0.85f) { ClearTelegraphs(); Sfx.Play("whirr", 0.6f); }
            Velocity = new Vector3(_dir.X * 11.5f, Velocity.Y, 0f);
            HitPlayers(f => Mathf.Abs(f.GlobalPosition.X - (GlobalPosition.X + _dir.X * 1.2f)) < 1.1f && Mathf.Abs(f.GlobalPosition.Z - GlobalPosition.Z) < 0.9f,
                       20f, 8f, 7f, 0.5f, 0.12f, _dir, _run);
            return MoveTime > 2.2f;
        }).Allowed = () => _mounted;

        AddMove("crack", 1.2f, 6f, 2.2f, () =>
        {
            Track(Telegraph.Lane(Stage, GlobalPosition, Facing, 5.5f, 0.9f, 0.5f));
            Sfx.Play("whoosh", 0.8f);
        }, dt =>
        {
            Stop(dt);
            PinClipFrame("attack", MoveTime < 0.25f ? 0 : MoveTime < 0.5f ? 1 : 2);
            if (MoveTime >= 0.5f && MoveTime - dt < 0.5f)
            {
                Sfx.Play("crit", 1.4f);
                HitPlayers(f => Telegraph.InLane(f.GlobalPosition, GlobalPosition, Facing, 5.5f, 0.9f), 16f, 6f, 0f, 0.6f, 0.1f, Facing);
            }
            if (PhaseNumber == 3 && MoveTime > 0.85f && MoveTime - dt <= 0.85f)
            {
                ResetMoveTime();
                Track(Telegraph.Lane(Stage, GlobalPosition, Facing, 5.5f, 0.9f, 0.5f));
            }
            return MoveTime > 0.85f;
        }, weight: 1.5f);

        AddMove("clowns", 0f, 40f, 11f, () =>
        {
            PopText.Spawn(Stage, GlobalPosition + new Vector3(0f, BodyHeight + 0.4f, 0f), "BRING ON THE CLOWNS!", new Color(1f, 0.85f, 0.3f), 1f, 1f);
            Sfx.Play("fanfare", 1.1f, -4f);
        }, dt =>
        {
            Stop(dt);
            PinClipFrame("point", 0);
            if (MoveTime >= 0.6f && MoveTime - dt < 0.6f)
            {
                string[] kinds = { "juggler", "unicyclist", "mime", "grunt", "balloonist" };
                int n = _mounted ? 3 : 2;
                for (int i = 0; i < n; i++)
                {
                    PlayerFighter p = EnemyCatalog.Create(kinds[GD.RandRange(0, kinds.Length - 1)]);
                    float side = i % 2 == 0 ? -1f : 1f;
                    float x = (Arena.Current?.CameraX ?? GlobalPosition.X) + side * 6f;
                    Summon(p, new Vector3(x, 0f, GlobalPosition.Z + (i - 1) * 1.2f));
                    _acts.Add(p);
                }
            }
            return MoveTime > 1.1f;
        }).Allowed = () => { _acts.RemoveAll(p => !IsInstanceValid(p) || p.IsDead); return _acts.Count < 2; };

        AddMove("hat", 0f, 40f, 5f, () => Sfx.Play("pop", 0.8f), dt =>
        {
            Stop(dt);
            PinClipFrame("special", 0);
            int bombs = PhaseNumber == 3 ? 5 : 3;
            for (int i = 0; i < bombs; i++)
            {
                float at = 0.4f + i * 0.15f;
                if (MoveTime >= at && MoveTime - dt < at && Target() is Fighter t)
                {
                    Vector3 to = t.GlobalPosition + new Vector3((i - bombs / 2) * 1.5f, 0f, (i % 2) * 1.0f - 0.5f);
                    var s = EnemyShot.Lob(this, EnemyShot.Bomb(1.3f), GlobalPosition + new Vector3(Facing.X * 0.3f, 2.6f, 0f), InView(to, 0f), 1.0f);
                    s.Damage = 0f;
                    s.BurstRadius = 1.3f;
                    s.BurstDamage = 14f;
                    Sfx.Play("fwee", 1f + i * 0.08f, -6f);
                }
            }
            if (PhaseNumber == 3 && MoveTime >= 1.2f && MoveTime - dt < 1.2f)
                for (int i = 0; i < 3; i++)
                {
                    var d = EnemyShot.Fling(this, EnemyShot.Dove(1.3f), GlobalPosition + new Vector3(0f, 2.6f, 0f), new Vector3(Facing.X, 0.4f + i * 0.2f, i - 1f), 4f);
                    d.Homing = 2.2f;
                    d.Life = 4.5f;
                    d.Damage = 7f;
                    d.SpinSpeed = 0f;
                }
            return MoveTime > 1.5f;
        }).Allowed = () => !_mounted;

        AddMove("spotlight", 0f, 40f, 6f, () =>
        {
            _spots.Clear();
            foreach (Fighter f in Players())
            {
                _spots.Add(f.GlobalPosition);
                Track(Telegraph.Circle(Stage, f.GlobalPosition, 1.3f, 1.0f, new Color(1f, 0.9f, 0.4f)));
            }
            Sfx.Play("ding", 0.6f);
        }, dt =>
        {
            Stop(dt);
            PinClipFrame("point", 0);
            if (MoveTime >= 1.0f && MoveTime - dt < 1.0f)
            {
                foreach (Vector3 at in _spots)
                {
                    var beam = new MeshInstance3D { Mesh = Kit3D.Cyl(1.3f, 1.3f, 12f, 24), MaterialOverride = Kit3D.Glow(new Color(1f, 0.95f, 0.7f), 0.45f, 2f) };
                    Stage.AddChild(beam);
                    beam.GlobalPosition = at + new Vector3(0f, 6f, 0f);
                    Tween tw = beam.CreateTween();
                    tw.TweenProperty(beam, "scale", new Vector3(0.05f, 1f, 0.05f), 0.35f);
                    tw.TweenCallback(Callable.From(beam.QueueFree));
                    HitPlayers(f => Near(f, at, 1.3f), 16f, 4f, 6f, 0.4f, 0.1f);
                }
                Sfx.Play("kaboom", 1.3f, -4f);
                CameraShake.Request(this, 0.1f, 0.25f);
            }
            return MoveTime > 1.4f;
        }).Allowed = () => !_mounted;
    }

    protected override void Tick(float dt)
    {
        base.Tick(dt);
        if (_mounted && !InSpecial && !InHitAnim) PinClipFrame("sit", 0);
    }

    protected override void TickAlways(float dt)
    {
        base.TickAlways(dt);
        if (_mounted)
        {
            bool flip = Facing.X < 0f;
            _cart.FlipH = flip;
            _cart.Position = new Vector3((flip ? -0.65f : 0.65f) * Big, _cart.Position.Y, 0.06f);
            float speed = Flat(Velocity).Length();
            string clip = speed > 0.5f ? "walk" : "idle";
            if (_cart.Animation != clip) _cart.Play(clip);
            _cart.SpeedScale = Mathf.Clamp(speed / 4f, 0.6f, 2.5f);
            if (_cartMat is not null) LitSprite.Apply(_cartMat, Visual.CurrentModulate.R > 1.5f ? Colors.White : Colors.White, Visual.CurrentModulate.R > 1.5f ? 1.1f : 0f, flip);
            _cart.Visible = Visual.Visible;
            if (Health <= MaxHealth * 0.55f && !IsDead) Dismount();
        }
        if (IsDead)
            foreach (PlayerFighter p in _acts)
                if (IsInstanceValid(p) && !p.IsDead) WaveDirector.Running?.Dismiss(p);
    }

    private void Dismount()
    {
        _mounted = false;
        Vector3 at = _cart.GlobalPosition;
        _cart.Reparent(Stage);
        _cart.GlobalPosition = at;
        _cart.Play("down");
        Tween tw = _cart.CreateTween();
        tw.TweenInterval(6f);
        tw.TweenProperty(_cart, "modulate:a", 0f, 1f);
        tw.TweenCallback(Callable.From(_cart.QueueFree));
        VisualBase = Vector3.Zero;
        ImpactEffect.Spawn(Stage, at + new Vector3(0f, 0.8f, 0f), 2.6f);
        Sfx.Play("kaboom");
        CameraShake.Request(this, 0.2f, 0.4f);
        PopText.Spawn(Stage, GlobalPosition + new Vector3(0f, 2.6f, 0f), "MY CARRIAGE!", new Color(1f, 0.4f, 0.3f), 1.2f, 1f);
        Stagger(0.9f);
        MoveSpeed = 3.6f;
    }
}
