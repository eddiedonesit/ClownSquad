namespace ClownSquad.Actors;

using Godot;
using ClownSquad.Audio;
using ClownSquad.Visuals;

/// <summary>
/// The first enemy: a big, fat, grinning clown. Reuses the input-driven fighter
/// wholesale and swaps sheet, scale, stats and faction, so AI-driven and
/// player-driven fighters share one combat implementation.
///
/// Signature move: the belly slam. Rears back, launches forward, and crashes
/// down with a radial shockwave that outreaches any normal swing.
/// </summary>
public partial class BossClown : PlayerFighter
{
    protected override string SheetPath => "res://assets/sprites/boss_clown.png";
    protected override string FramesPath => "res://assets/sprites/boss_clown.frames.json";

    // Authored at the same 64px height as the player but rendered larger, so it
    // towers over them without needing a taller canvas.
    protected override float SpritePixelSize => 0.034f;

    public override bool Hostile => true;

    public override string AttackSound => "slap";
    public override float AttackPitch => 0.7f;
    public override string HurtSound => "honk_low";
    public override string DeathSound => "kaboom";
    public override float BodyHeight => 2.25f;
    public override float BodyHalfWidth => 0.9f;

    // Its arms and belly cover far more ground than the player's mallet.
    protected override float ReachScale => 1.45f;

    protected override bool HasMalletMoves => false;

    // A boss shrugs off most of a hit and cannot be thrown into the air, and
    // nothing interrupts the slam once it has begun. It is beaten by reading
    // the wind-up and not being there, not by out-mashing it.
    protected override float StunScale => 0.45f;
    protected override float LaunchScale => 0f;
    protected override bool HasPoise => InSpecial;

    // Belly flop: crouch, leap, hang over the target, then pound straight down.
    private const float Crouch = 0.4f;
    // The leap goes as high as the camera allows and no higher, so Bertie hangs
    // fully on screen. At this framing the top of the view is only about 3 units
    // up mid-arena, which leaves him roughly a unit of air.
    private const float MaxApex = 3.2f;
    private const float MinApex = 0.9f;
    private const float HeadRoom = 0.15f;
    private const float HangTime = 1.0f;
    private const float HangDriftTime = 0.7f;     // stops homing before the drop, so it can be escaped
    private const float HangDrift = 3.0f;
    private const float DropSpeed = 30f;
    private const float Recover = 0.8f;           // the punish window after a dodge

    private const float SlamRadius = 2.3f;
    private const float SlamDamage = 26f;
    private const float SlamKnockback = 7f;
    private const float SlamLaunch = 7f;

    private enum Flop { Crouch, Rise, Hang, Drop, Landed }
    private Flop _flop;
    private float _phase;
    private float _peakY;
    private float _hung;
    private MeshInstance3D? _marker;
    private StandardMaterial3D? _markerMat;

    /// <summary>True during the mid-air hang. Exposed for probes.</summary>
    public bool Hanging => InSpecial && _flop == Flop.Hang;

    public override void _Ready()
    {
        if (DisplayName.Length == 0) DisplayName = "BELLYFLOP BERTIE";
        MaxHealth = 320f;
        MoveSpeed = 2.9f;      // slow and inevitable rather than quick
        Acceleration = 22f;
        Friction = 26f;
        base._Ready();
    }

    public override void _ExitTree() => HideMarker();

    protected override bool HasSpecial => true;

    protected override void OnSpecialStart()
    {
        _flop = Flop.Crouch;
        _phase = 0f;
        _peakY = 0f;
        _hung = 0f;
        // An audible tell as well as a visual one: hear the flop coming.
        Sfx.Play("honk_low", 0.75f, 2f);
    }

    private void Next(Flop f)
    {
        _flop = f;
        _phase = 0f;
    }

    protected override void TickAlways(float dt)
    {
        if (IsDead) HideMarker();   // killed in mid-air: no landing, no circle
    }

    protected override bool TickSpecialMove(float dt)
    {
        _phase += dt;
        _peakY = Mathf.Max(_peakY, GlobalPosition.Y);
        UpdateMarker();

        switch (_flop)
        {
            case Flop.Crouch:
                PinClipFrame("attack", 0);
                ApplyGroundMovement(Vector3.Zero, dt);
                Visual.Squash(0.3f);
                if (_phase >= Crouch)
                {
                    Jump(Mathf.Sqrt(2f * Gravity * ApexHere()));
                    Sfx.Play("boing", 0.55f, 2f);
                    Sfx.Play("slide_up", 0.5f);
                    Dust(1.0f);
                    ShowMarker();
                    Next(Flop.Rise);
                }
                return false;

            case Flop.Rise:
                PinClipFrame("air", 0);
                HomeToward();
                if (Velocity.Y <= 0f) Next(Flop.Hang);
                return false;

            case Flop.Hang:
                PinClipFrame("air", 0);
                // Cancel gravity exactly (the base applies -g*dt after this tick),
                // and ease down if drifting has brought the top of the frame lower.
                float ceiling = ApexHere();
                float sink = GlobalPosition.Y > ceiling ? (ceiling - GlobalPosition.Y) / Mathf.Max(dt, 0.001f) * 0.2f : 0f;
                Velocity = new Vector3(0f, Gravity * dt + sink, 0f);
                if (_phase < HangDriftTime) HomeToward();
                _hung += dt;
                if (_phase >= HangTime)
                {
                    Velocity = new Vector3(0f, -DropSpeed, 0f);
                    Sfx.Play("slide_down", 0.6f);
                    Next(Flop.Drop);
                }
                return false;

            case Flop.Drop:
                PinClipFrame("air", 1);
                Velocity = new Vector3(0f, Mathf.Min(Velocity.Y, -DropSpeed), 0f);
                if (Grounded)
                {
                    Slam();
                    Next(Flop.Landed);
                }
                return false;

            default:
                PinClipFrame("attack", 2);
                ApplyGroundMovement(Vector3.Zero, dt);
                return _phase >= Recover;
        }
    }

    /// <summary>How high he may rise here and still be fully on screen.</summary>
    private float ApexHere()
    {
        if (ClownSquad.World.Arena.Current is not ClownSquad.World.Arena arena) return MaxApex;
        float apex = arena.MaxVisibleHeight(GlobalPosition.Z) - BodyHeight - HeadRoom;
        return Mathf.Clamp(apex, MinApex, MaxApex);
    }

    /// <summary>Drift toward the nearest player while airborne.</summary>
    private void HomeToward()
    {
        Fighter? target = null;
        float best = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup(Group))
        {
            if (n is not Fighter f || f.Hostile || f.IsDead) continue;
            float d = f.GlobalPosition.DistanceTo(GlobalPosition);
            if (d < best) { best = d; target = f; }
        }
        if (target is null) return;

        Vector3 to = target.GlobalPosition - GlobalPosition;
        to.Y = 0f;
        float dist = to.Length();
        Vector3 v = dist > 0.1f ? to / dist * Mathf.Min(HangDrift, dist * 3f) : Vector3.Zero;
        Velocity = new Vector3(v.X, Velocity.Y, v.Z);
        if (Mathf.Abs(to.X) > 0.2f) Facing = new Vector3(Mathf.Sign(to.X), 0f, 0f);
    }

    private void Slam()
    {
        HideMarker();
        // Anyone caught is thrown into the air and floored on landing.
        int hits = DamageInRadius(SlamRadius, SlamDamage, SlamKnockback, 0.16f, SlamLaunch, 0.4f);
        ApplyHitstop(0.16f);
        ImpactEffect.Spawn(GetParent(), GlobalPosition + new Vector3(0f, 0.12f, 0f), 2.4f);
        CameraShake.Request(this, 0.2f, 0.4f);
        Sfx.Play("kaboom");
        GD.Print($"Boss belly flop: {hits} hit(s), peak y={_peakY:F2}, hung {_hung:F2}s.");
    }

    // ---- the landing circle, drawn exactly the size of the slam's hit area ----

    private void ShowMarker()
    {
        HideMarker();
        _markerMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.95f, 0.15f, 0.2f, 0.2f),
        };
        _marker = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = SlamRadius, BottomRadius = SlamRadius, Height = 0.02f, RadialSegments = 48 },
            MaterialOverride = _markerMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        GetParent().AddChild(_marker);
        UpdateMarker();
    }

    private void UpdateMarker()
    {
        if (_marker is null || _markerMat is null) return;
        _marker.GlobalPosition = new Vector3(GlobalPosition.X, 0.03f, GlobalPosition.Z);
        // Darkens as the drop approaches, and pulses faster once it is coming.
        float urgency = _flop == Flop.Drop ? 1f : _flop == Flop.Hang ? Mathf.Clamp(_phase / HangTime, 0f, 1f) : 0f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_phase * Mathf.Lerp(6f, 22f, urgency));
        _markerMat.AlbedoColor = new Color(0.95f, 0.15f, 0.2f, 0.16f + 0.3f * urgency + 0.1f * pulse);
    }

    private void HideMarker()
    {
        if (_marker is not null && GodotObject.IsInstanceValid(_marker)) _marker.QueueFree();
        _marker = null;
        _markerMat = null;
    }
}
