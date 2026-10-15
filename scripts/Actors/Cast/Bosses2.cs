namespace ClownSquad.Actors;

using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Combat;
using ClownSquad.Visuals;
using ClownSquad.World;

/// <summary>
/// STRETCH (world 5): the elastic man. Punches from across the screen with an
/// arm that keeps on going, grabs and reels you in for an uppercut, and coils
/// up to spring onto you.
/// </summary>
public partial class ElasticBoss : BossFighter
{
    protected override string Cast => "elastic";
    public override float BodyHeight => 2.4f;
    protected override float ReachScale => 1.3f;
    protected override string Tagline => "HE GOES TO ANY LENGTH";
    private Node3D _arm = null!;
    private MeshInstance3D _sleeve = null!;
    private MeshInstance3D _glove = null!;
    private float _reach;
    private float _laneZ;
    private Fighter? _caught;
    private int _punches;
    private const float MaxReach = 7.5f;

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "STRETCH";
        MaxHealth = 460f;
        MoveSpeed = 3.3f;
        base._Ready();
        _arm = new Node3D { Visible = false };
        AddChild(_arm);
        _sleeve = Kit3D.Put(_arm, Kit3D.Cyl(0.09f, 0.09f, 1f, 10), Kit3D.Paint(new Color(0.47f, 0.27f, 0.82f), 0.5f), Vector3.Zero, new Vector3(0f, 0f, 90f));
        _glove = Kit3D.Put(_arm, Kit3D.Ball(0.2f, -1f, 12), Kit3D.Paint(new Color(0.95f, 0.95f, 1f), 0.6f), Vector3.Zero);

        AddMove("punch", 1.5f, 40f, 2.2f, () => BeginPunch(), dt => TickArm(dt, grab: false), weight: 2f);
        AddMove("grab", 1.5f, 40f, 6f, () => BeginPunch(), dt => TickArm(dt, grab: true));
        AddMove("spring", 0f, 10f, 6f, () => { _punches = 0; }, dt =>
        {
            if (MoveTime < 0.5f) { Stop(dt); PinClipFrame("coil", 0); return false; }
            if (MoveTime - dt < 0.5f || (_punches > 0 && Grounded && MoveTime > 0.5f && _leapPending))
            {
                _leapPending = false;
                float air = StartLeap(Target()?.GlobalPosition ?? GlobalPosition, 2.4f);
                Track(Telegraph.Circle(Stage, LeapTarget, 1.7f, air));
                Sfx.Play("boing", 0.7f);
            }
            PinClipFrame("air", 0);
            if (!TickLeap(dt)) return false;
            ClearTelegraphs();
            HitPlayers(f => Near(f, GlobalPosition, 1.7f), 18f, 6f, 6f, 0.45f, 0.12f);
            ImpactEffect.Spawn(Stage, GlobalPosition + new Vector3(0f, 0.1f, 0f), 1.8f);
            CameraShake.Request(this, 0.12f, 0.25f);
            Sfx.Play("boing", 0.5f);
            _punches++;
            if (PhaseNumber == 3 && _punches < 2) { _leapPending = true; return false; }
            return true;
        });
    }

    private bool _leapPending;

    private void BeginPunch()
    {
        _reach = 0f;
        _caught = null;
        Fighter? t = Target();
        _laneZ = t?.GlobalPosition.Z ?? GlobalPosition.Z;
        // Step onto the target's lane first: a lane punch must line up.
        GlobalPosition = new Vector3(GlobalPosition.X, GlobalPosition.Y, Mathf.MoveToward(GlobalPosition.Z, _laneZ, 0.9f));
        Track(Telegraph.Lane(Stage, GlobalPosition + Facing * 0.4f, Facing, MaxReach, 0.8f));
        Sfx.Play("slide_up", 0.6f);
    }

    private bool TickArm(float dt, bool grab)
    {
        Stop(dt);
        PinClipFrame("special", 0);
        const float Wind = 0.55f, Out = 0.22f, Hold = 0.15f, Back = 0.3f;
        _arm.Visible = MoveTime >= Wind;
        _arm.Position = new Vector3(Facing.X * 0.4f, 1.45f, 0.06f);
        _arm.Scale = new Vector3(Facing.X, 1f, 1f);
        if (MoveTime < Wind) return false;
        if (MoveTime - dt < Wind) { ClearTelegraphs(); Sfx.Play("slide_up", 1.4f); }
        float t = MoveTime - Wind;
        if (t < Out && _caught is null)
        {
            _reach = MaxReach * (t / Out);
            foreach (Fighter f in Players())
            {
                Vector3 d = f.GlobalPosition - GlobalPosition;
                if (!f.CanBeHit() || Mathf.Abs(d.Z) > 0.55f || d.X * Facing.X < 0f || d.X * Facing.X > _reach + 0.3f) continue;
                _reach = Mathf.Abs(d.X);
                if (grab)
                {
                    _caught = f;
                    f.Stagger(2f);
                    Sfx.Play("boing", 1.2f);
                }
                else
                {
                    f.TakeDamage(new DamageInfo(18f * DamageMultiplier, Facing, 9f, 0.12f, this, 3f, 0.4f));
                    ImpactEffect.Spawn(Stage, f.GlobalPosition + new Vector3(0f, 1f, 0f), 0.8f);
                    Sfx.Play("bonk", 0.7f);
                }
                break;
            }
        }
        else if (t > Out + Hold)
        {
            float k = Mathf.Clamp((t - Out - Hold) / Back, 0f, 1f);
            float reach = Mathf.Lerp(_reach, 0.9f, k);
            if (_caught is not null)
            {
                Vector3 hand = GlobalPosition + new Vector3(Facing.X * (0.4f + reach), 0f, 0f);
                _caught.Hold = () => hand;
            }
            DrawArm(reach);
            if (k >= 1f)
            {
                _arm.Visible = false;
                if (_caught is not null)
                {
                    Fighter f = _caught;
                    _caught = null;
                    f.Hold = null;
                    PinClipFrame("attack", 2);
                    f.TakeDamage(new DamageInfo(16f * DamageMultiplier, Facing, 4f, 0.14f, this, 9f, 0.5f));
                    Sfx.Play("crit");
                }
                if (!grab && PhaseNumber >= 2 && ++_punches < 2) { BeginPunch(); _rewind = true; return false; }
                _punches = 0;
                return true;
            }
            return false;
        }
        DrawArm(_reach);
        return false;
    }

    private bool _rewind;

    protected override bool TickSpecialMove(float dt)
    {
        if (_rewind) { _rewind = false; ResetMoveTime(); }
        return base.TickSpecialMove(dt);
    }

    private void DrawArm(float reach)
    {
        _sleeve.Position = new Vector3(reach * 0.5f, 0f, 0f);
        _sleeve.Scale = new Vector3(1f, Mathf.Max(0.05f, reach), 1f);
        _glove.Position = new Vector3(reach, 0f, 0f);
    }

    protected override void OnMoveEnded()
    {
        _arm.Visible = false;
        if (_caught is not null) { _caught.Hold = null; _caught = null; }
    }
}

/// <summary>
/// THE FLYING FRATELLI (world 6): two trapeze artists. They swing above the
/// ring out of reach, take turns diving onto you, fight a while on the
/// ground, then climb back up. Lose one and the other only works harder.
/// </summary>
public partial class TrapezeArtist : BossFighter
{
    private enum Act { Aloft, Diving, Ground, Climbing }

    public string Sheet = "trapeze_a";
    protected override string Cast => Sheet;
    public TrapezeArtist? Partner;
    public bool Primary;
    public int Bar;
    public override bool PrimaryBoss => Primary;
    public override string BossTitle => "THE FLYING FRATELLI";
    public override float BossFraction
    {
        get
        {
            if (Partner is null || !IsInstanceValid(Partner)) return Health / MaxHealth;
            return (Mathf.Max(0f, Health) + Mathf.Max(0f, Partner.Health)) / (MaxHealth + Partner.MaxHealth);
        }
    }
    protected override string Tagline => Primary ? "NO NET!" : "";
    public override float BodyHeight => 1.9f;
    protected override float ReachScale => 1.15f;
    public override float XpScale => 6f;

    private Act _act = Act.Aloft;
    private float _timer;
    private Vector3 _diveTo;
    private Telegraph? _mark;
    private TrapezeRig? _rig;

    public override void _Ready()
    {
        MaxHealth = 230f;
        MoveSpeed = 3.8f;
        base._Ready();
        _timer = Primary ? 1.5f : 3.5f;
        AddMove("flipkick", 0f, 5f, 3f, () =>
        {
            float air = StartLeap(Target()?.GlobalPosition ?? GlobalPosition, 1.2f);
            Track(Telegraph.Circle(Stage, LeapTarget, 1.1f, air));
        }, dt =>
        {
            PinClipFrame("air", 0);
            if (!TickLeap(dt)) return false;
            HitPlayers(f => Near(f, GlobalPosition, 1.1f), 12f, 6f, 5f);
            return true;
        });
        if (Primary) CallDeferred(MethodName.BuildRig);
        HoldClip = "hang";
    }

    private void BuildRig()
    {
        _rig = new TrapezeRig();
        Stage.AddChild(_rig);
        if (Partner is not null) Partner._rig = _rig;
        Hold = () => _rig.Grip(Bar) - new Vector3(0f, 2.0f, 0f);
        if (Partner is not null) Partner.Hold = () => _rig.Grip(Partner.Bar) - new Vector3(0f, 2.0f, 0f);
    }

    public override bool CanBeHit() => _act is Act.Ground or Act.Diving && base.CanBeHit();

    private bool Alone => Partner is null || !IsInstanceValid(Partner) || Partner.IsDead;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (IsDead || _rig is null) return;
        _timer -= dt;
        switch (_act)
        {
            case Act.Aloft:
                if (_timer > 0f) break;
                bool partnerBusy = !Alone && Partner!._act is Act.Ground or Act.Diving;
                if (partnerBusy) { _timer = 0.5f; break; }
                if (_mark is null)
                {
                    _diveTo = InView(Target()?.GlobalPosition ?? GlobalPosition with { Y = 0f });
                    _mark = Telegraph.Circle(Stage, _diveTo, 1.6f, 0f);
                    _timer = 0.9f;
                    Sfx.Play("slide_down", 1.3f);
                    break;
                }
                // Let go of the bar and come down on the mark.
                Hold = null;
                _act = Act.Diving;
                const float T = 0.5f;
                Vector3 p = GlobalPosition;
                Velocity = new Vector3((_diveTo.X - p.X) / T, (-p.Y + 0.5f * Gravity * T * T) / T, (_diveTo.Z - p.Z) / T);
                Sfx.Play("whoosh", 0.8f);
                break;
            case Act.Ground:
                if (_timer <= 0f && !InSpecial && Grounded)
                {
                    _act = Act.Climbing;
                    _timer = 0.8f;
                    Jump(14f);
                }
                break;
        }
        if (_mark is not null) _mark.Urgency = _act == Act.Aloft ? Mathf.Clamp(1f - _timer / 0.9f, 0f, 1f) : 1f;
    }

    protected override void Tick(float dt)
    {
        switch (_act)
        {
            case Act.Diving:
                PinClipFrame("dive", 0);
                if (Grounded)
                {
                    _mark?.Clear();
                    _mark = null;
                    HitPlayers(f => Near(f, GlobalPosition, 1.6f), 18f, 6f, 6f, 0.4f, 0.12f);
                    ImpactEffect.Spawn(Stage, GlobalPosition + new Vector3(0f, 0.1f, 0f), 1.8f);
                    CameraShake.Request(this, 0.12f, 0.25f);
                    Sfx.Play("thump");
                    _act = Act.Ground;
                    _timer = Alone ? 3f : 4.5f;
                }
                return;
            case Act.Climbing:
                PinClipFrame("air", 0);
                if (_timer <= 0f || GlobalPosition.Y > 1.8f)
                {
                    _act = Act.Aloft;
                    _timer = Alone ? 1.2f : 3f;
                    Hold = () => _rig!.Grip(Bar) - new Vector3(0f, 2.0f, 0f);
                }
                return;
        }
        base.Tick(dt);
    }

    protected override void TickAlways(float dt)
    {
        base.TickAlways(dt);
        if (IsDead) { _mark?.Clear(); _mark = null; Hold = null; }
    }

    public override void _ExitTree()
    {
        _mark?.Clear();
        if (Primary && _rig is not null && IsInstanceValid(_rig) && (Alone || Partner!.IsDead)) _rig.QueueFree();
        base._ExitTree();
    }
}

/// <summary>Two trapezes swinging high over the ring, on ropes from the dark above.</summary>
public partial class TrapezeRig : Node3D
{
    private readonly Node3D[] _pivots = new Node3D[2];
    private double _t;
    private float _top;

    public override void _Ready()
    {
        float cx = Arena.Current?.CameraX ?? 0f;
        _top = Mathf.Clamp((Arena.Current?.MaxVisibleHeight(-1.5f) ?? 4.6f) - 0.25f, 3.2f, 5.2f);
        Material rope = Kit3D.Paint(new Color(0.85f, 0.75f, 0.55f));
        Material bar = Kit3D.Stripes(Kit3D.Red, Kit3D.Cream, 4, along: true, rough: 0.4f);
        for (int i = 0; i < 2; i++)
        {
            var pivot = new Node3D { Position = new Vector3(cx + (i == 0 ? -2.8f : 2.8f), _top + 3.5f, -1.5f) };
            AddChild(pivot);
            foreach (float z in new[] { -0.3f, 0.3f })
                Kit3D.Put(pivot, Kit3D.Cyl(0.02f, 0.02f, 3.5f, 6), rope, new Vector3(0f, -1.75f, z));
            Kit3D.Put(pivot, Kit3D.Cyl(0.05f, 0.05f, 0.75f, 8), bar, new Vector3(0f, -3.5f, 0f), new Vector3(90f, 0f, 0f));
            _pivots[i] = pivot;
        }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        for (int i = 0; i < 2; i++)
            _pivots[i].Rotation = new Vector3(0f, 0f, Mathf.Sin((float)_t * 2.2f + i * Mathf.Pi) * 0.4f);
    }

    /// <summary>Where the bar is now, in world space.</summary>
    public Vector3 Grip(int bar)
    {
        Node3D p = _pivots[Mathf.Clamp(bar, 0, 1)];
        return p.GlobalTransform * new Vector3(0f, -3.5f, 0.1f);
    }
}

/// <summary>
/// PYRO PETE (world 7): breathes a cone of fire that leaves the floor
/// burning, lobs flaming torches, and later sends out a ring of fire to
/// jump over.
/// </summary>
public partial class FireEaterBoss : BossFighter
{
    protected override string Cast => "fireeater";
    public override float BodyHeight => 2.0f;
    protected override float ReachScale => 1.25f;
    protected override string Tagline => "HOT STUFF";
    public override string AttackSound => "whoosh";
    private GpuParticles3D _flame = null!;
    private OmniLight3D _glow = null!;
    private readonly Dictionary<Fighter, float> _burned = new();

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "PYRO PETE";
        MaxHealth = 480f;
        MoveSpeed = 3.0f;
        base._Ready();
        var ramp = new Gradient { Offsets = new[] { 0f, 0.4f, 1f }, Colors = new[] { new Color(1f, 1f, 0.7f), new Color(1f, 0.5f, 0.1f), new Color(0.3f, 0.05f, 0.05f, 0f) } };
        _flame = new GpuParticles3D
        {
            Amount = 160, Lifetime = 0.55f, Emitting = false, LocalCoords = false,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = new Vector3(1f, 0.05f, 0f), Spread = 22f, InitialVelocityMin = 6f, InitialVelocityMax = 9f,
                Gravity = new Vector3(0f, 1.5f, 0f), ScaleMin = 0.8f, ScaleMax = 2.2f,
                ColorRamp = new GradientTexture1D { Gradient = ramp },
            },
            DrawPass1 = new QuadMesh
            {
                Size = new Vector2(0.35f, 0.35f),
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                },
            },
        };
        AddChild(_flame);
        _glow = new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 0f, OmniRange = 6f, Position = new Vector3(1.5f, 1.4f, 0.4f) };
        AddChild(_glow);

        AddMove("breath", 0f, 6f, 4f, () =>
        {
            _burned.Clear();
            Track(Telegraph.Cone(Stage, GlobalPosition, Facing, 4.8f, 50f, 0.7f, new Color(1f, 0.5f, 0.1f)));
            Sfx.Play("whoosh", 0.5f);
        }, dt =>
        {
            Stop(dt);
            if (MoveTime < 0.7f) { PinClipFrame("special", 0); return false; }
            PinClipFrame("special", 1 + (int)(MoveTime * 10f) % 2);
            if (MoveTime - dt < 0.7f)
            {
                ClearTelegraphs();
                _flame.Position = new Vector3(Facing.X * 0.55f, 1.7f, 0.08f);
                if (_flame.ProcessMaterial is ParticleProcessMaterial pm) pm.Direction = new Vector3(Facing.X, 0.05f, 0f);
                _flame.Emitting = true;
                Sfx.Play("kaboom", 0.6f, -6f);
                for (int i = 1; i <= 2; i++) Hazard.Fire(this, GlobalPosition + Facing * (1.6f * i + 0.4f), 0.9f, 3.5f, 4f, 0.5f);
            }
            _glow.LightEnergy = 3f;
            _glow.Position = new Vector3(Facing.X * 1.5f, 1.4f, 0.4f);
            foreach (Fighter f in new List<Fighter>(Players()))
            {
                if (!f.CanBeHit() || !Telegraph.InCone(f.GlobalPosition, GlobalPosition, Facing, 4.8f, 50f)) continue;
                if (_burned.TryGetValue(f, out float next) && MoveTime < next) continue;
                _burned[f] = MoveTime + 0.3f;
                f.TakeDamage(new DamageInfo(6f * DamageMultiplier, Facing, 3f, 0.03f, this, 2f, 0.25f));
            }
            if (MoveTime > 1.9f) { _flame.Emitting = false; _glow.LightEnergy = 0f; return true; }
            return false;
        }, weight: 1.6f);

        AddMove("torches", 2f, 40f, 4.5f, () => Sfx.Play("whoosh", 1.1f), dt =>
        {
            Stop(dt);
            int count = PhaseNumber == 3 ? 5 : 3;
            PinClipFrame("special", 0);
            for (int i = 0; i < count; i++)
            {
                float at = 0.35f + i * 0.2f;
                if (MoveTime >= at && MoveTime - dt < at && Target() is Fighter t)
                {
                    Vector3 to = t.GlobalPosition + new Vector3((i - count / 2) * 1.4f, 0f, (i % 2) * 0.8f - 0.4f);
                    var s = EnemyShot.Lob(this, EnemyShot.Torch(1.4f), GlobalPosition + new Vector3(Facing.X * 0.4f, 2.4f, 0f), InView(to, 0f), 0.95f);
                    s.Damage = 8f;
                    s.OnLand = at2 => Hazard.Fire(this, at2, 1.0f, 3.5f, 4f, 0.1f);
                    Sfx.Play("fwee", 0.9f, -6f);
                }
            }
            return MoveTime > 0.35f + count * 0.2f + 0.3f;
        });

        AddMove("ring", 0f, 40f, 9f, () =>
        {
            Sfx.Play("kaboom", 0.5f);
            Hazard.Fire(this, GlobalPosition, 0.9f, 2.4f, 8f, 0.7f, thickness: 0.6f, grow: 3.2f);
            PopText.Spawn(Stage, GlobalPosition + new Vector3(0f, BodyHeight + 0.3f, 0f), "JUMP!", new Color(1f, 0.6f, 0.2f), 0.8f, 0.6f);
        }, dt => { Stop(dt); PinClipFrame("special", 2); return MoveTime > 1.0f; }).Allowed = () => PhaseNumber >= 2;
    }

    protected override void OnMoveEnded()
    {
        _flame.Emitting = false;
        _glow.LightEnergy = 0f;
    }
}
