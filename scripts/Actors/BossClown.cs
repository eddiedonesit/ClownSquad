namespace ClownSquad.Actors;

using Godot;
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

    // Belly slam phases, in seconds.
    private const float WindUp = 0.55f;
    private const float Lunge = 0.28f;
    private const float Recover = 0.60f;
    private const float LungeSpeed = 13f;

    private const float SlamRadius = 3.0f;
    private const float SlamDamage = 26f;
    private const float SlamKnockback = 7f;
    private const float SlamLaunch = 7f;

    private bool _slammed;
    private Vector3 _lungeDir;

    public override void _Ready()
    {
        MaxHealth = 320f;
        MoveSpeed = 2.9f;      // slow and inevitable rather than quick
        Acceleration = 22f;
        Friction = 26f;
        base._Ready();
    }

    protected override bool HasSpecial => true;

    protected override void OnSpecialStart()
    {
        _slammed = false;
        _lungeDir = Facing;
    }

    protected override bool TickSpecialMove(float dt)
    {
        float t = SpecialTime;

        if (t < WindUp)
        {
            // Rear back: a readable tell, so the move can be dodged on sight.
            PinClipFrame("attack", 0);
            ApplyGroundMovement(-_lungeDir * 1.4f, dt);
            return false;
        }

        if (t < WindUp + Lunge)
        {
            PinClipFrame("attack", 1);
            ApplyGroundMovement(_lungeDir * LungeSpeed, dt);
            return false;
        }

        if (!_slammed)
        {
            _slammed = true;
            PinClipFrame("attack", 2);

            // Anyone caught is thrown into the air and floored on landing.
            int hits = DamageInRadius(SlamRadius, SlamDamage, SlamKnockback, 0.16f, SlamLaunch, 0.4f);
            ApplyHitstop(0.16f);

            // Shockwave reads as the whole body landing, so it is centred under
            // the boss rather than out at a weapon.
            ImpactEffect.Spawn(GetParent(), GlobalPosition + new Vector3(0f, 0.12f, 0f), 2.2f);
            CameraShake.Request(this, 0.16f, 0.34f);
            GD.Print($"Boss belly slam: {hits} hit(s).");
            return false;
        }

        PinClipFrame("attack", 2);
        ApplyGroundMovement(Vector3.Zero, dt);
        return t >= WindUp + Lunge + Recover;
    }
}
