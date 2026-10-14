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

    // Slower swings than the squad's, so each thrown fist can be seen coming.
    protected override float SwingTimeScale => 1.4f;

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
        BuildFists();
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
        UpdateFists(dt);
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

    // ---- floating fists ----------------------------------------------------
    // Bertie has no arms. Two matching white-gloved fists hover close beside him,
    // Rayman style, and a punch is a fist flung at whoever he is hitting and
    // reeled back in. The swing's
    // hitbox is unchanged; the glove is drawn to arrive as the hit lands.

    private const string FistSheet = "res://assets/sprites/boss_fist.png";
    private Sprite3D? _nearFist;
    private Sprite3D? _farFist;
    private float _fistClock;
    private int _whooshedSwing = -1;

    private void BuildFists()
    {
        var sheet = GD.Load<Texture2D>(FistSheet);
        _nearFist = MakeFist(sheet);
        _farFist = MakeFist(sheet);
        float f = Facing.X >= 0f ? 1f : -1f;
        _nearFist.Position = new Vector3(f * 0.72f, 0.85f, 0.14f);
        _farFist.Position = new Vector3(-f * 0.62f, 0.9f, -0.14f);
    }

    private Sprite3D MakeFist(Texture2D sheet)
    {
        var s = new Sprite3D
        {
            Texture = sheet,
            PixelSize = SpritePixelSize * 0.85f,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            Shaded = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(s);
        return s;
    }

    private void UpdateFists(float dt)
    {
        if (_nearFist is null || _farFist is null) return;
        _fistClock += dt;
        float f = Facing.X >= 0f ? 1f : -1f;
        float bob = Mathf.Sin(_fistClock * 3.1f) * 0.07f;
        float bob2 = Mathf.Sin(_fistClock * 3.1f + 1.7f) * 0.07f;

        // Resting: one fist just ahead of the belly, one just behind, at the same height.
        Vector3 near = new(f * 0.72f, 0.85f + bob, 0.14f);
        Vector3 far = new(-f * 0.62f, 0.9f + bob2, -0.14f);
        float rate = 12f;

        if (IsDead || Down)
        {
            near = new Vector3(f * 0.8f, 0.14f, 0.2f);       // fists flop to the floor
            far = new Vector3(-f * 0.7f, 0.14f, -0.2f);
            rate = 7f;
        }
        else if (Launched || Stunned || InHitAnim)
        {
            near = new Vector3(f * 0.78f, 1.15f, 0.14f);     // jolted up by the hit
            far = new Vector3(-f * 0.68f, 1.15f, -0.14f);
            rate = 20f;
        }
        else if (InSpecial)
        {
            float wave = Mathf.Sin(_fistClock * 15f) * 0.09f;
            switch (_flop)
            {
                case Flop.Crouch:
                    near = new Vector3(f * 0.75f, 0.45f, 0.14f);
                    far = new Vector3(-f * 0.65f, 0.45f, -0.14f);
                    break;
                case Flop.Rise:
                case Flop.Hang:
                    // Hands up, waving, for the whole hang: the flop is coming.
                    // Beside the head rather than over it: the leap tops out
                    // just under the screen edge, so overhead would be off screen.
                    near = new Vector3(f * 0.66f, 1.5f + wave, 0.14f);
                    far = new Vector3(-f * 0.58f, 1.5f - wave, -0.14f);
                    rate = 16f;
                    break;
                case Flop.Drop:
                    near = new Vector3(f * 0.66f, 1.55f, 0.14f);
                    far = new Vector3(-f * 0.58f, 1.55f, -0.14f);
                    rate = 30f;
                    break;
                default:
                    near = new Vector3(f * 1.0f, 0.3f, 0.2f);      // braced on the landing
                    far = new Vector3(-f * 0.9f, 0.3f, -0.2f);
                    break;
            }
        }
        else if (TryGetSwing(out int index, out float t, out float startup, out float active, out float reach))
        {
            // Wind back, fling out to land with the hit, hold, reel back in.
            Vector3 wind = new(f * 0.35f, 0.95f, 0.14f);
            Vector3 aim = PunchAim(f, reach);
            float windEnd = startup * 0.45f;
            float ext = t < windEnd ? 0f
                      : t < startup ? (t - windEnd) / (startup - windEnd)
                      : t < startup + active + 0.06f ? 1f
                      : Mathf.Max(0f, 1f - (t - startup - active - 0.06f) / 0.22f);
            Vector3 punch = wind.Lerp(aim, Mathf.SmoothStep(0f, 1f, ext));
            rate = 45f;
            // Jab with the front glove, cross with the back one, both for the finisher.
            if (index != 1) near = punch;
            if (index >= 1) far = punch + new Vector3(0f, index == 2 ? 0.3f : 0f, -0.1f);
            if (t >= windEnd && _whooshedSwing != index)
            {
                _whooshedSwing = index;
                Sfx.Play("whoosh", 0.65f, -4f);
            }
        }
        if (!TryGetSwing(out _, out _, out _, out _, out _)) _whooshedSwing = -1;

        float k = 1f - Mathf.Exp(-rate * dt);
        _nearFist.Position = _nearFist.Position.Lerp(near, k);
        _farFist.Position = _farFist.Position.Lerp(far, k);
        _nearFist.FlipH = _farFist.FlipH = f < 0f;

        // Flash and blink with the body, the back glove a shade darker.
        Color c = Visual.CurrentModulate;
        _nearFist.Modulate = c;
        _farFist.Modulate = new Color(c.R * 0.78f, c.G * 0.78f, c.B * 0.82f, c.A);
        _nearFist.Visible = _farFist.Visible = SpriteVisible;
    }

    /// <summary>Where a thrown glove should land: on the nearest target ahead, within reach.</summary>
    private Vector3 PunchAim(float f, float reach)
    {
        Vector3 best = new(f * reach, 0.95f, 0.14f);
        float bestDist = reach * 1.3f;
        float band = DepthBand * ReachScale;
        foreach (Node n in GetTree().GetNodesInGroup(Group))
        {
            if (n is not Fighter other || other.Hostile || other.IsDead) continue;
            Vector3 to = other.GlobalPosition - GlobalPosition;
            float forward = to.X * f;
            if (forward < 0.3f || forward > bestDist) continue;
            bestDist = forward;
            best = new Vector3(f * Mathf.Clamp(forward - 0.2f, 0.8f, reach),
                               Mathf.Clamp(to.Y + other.BodyHeight * 0.55f, 0.4f, 2.2f),
                               Mathf.Clamp(to.Z, -band, band) + 0.14f);
        }
        return best;
    }
}
