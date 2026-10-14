namespace ClownSquad.Actors;

using System.Collections.Generic;
using Godot;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Visuals;

/// <summary>
/// A fighter driven by an <see cref="IInputSource"/>. Knows nothing about where
/// the input came from, so the same class serves single-player, local co-op,
/// AI enemies and (later) remote peers. See ADR 0003.
/// </summary>
public partial class PlayerFighter : Fighter
{
    private enum State { Free, Attacking, Dodging, Special, Charging, Spinning, Throwing, Flipping }

    /// <summary>One swing in the combo. Timings are in seconds.</summary>
    private readonly record struct AttackStep(
        float Startup, float Active, float Recovery,
        float Damage, float Knockback, float Hitstop,
        float Range, float Launch);

    // Escalating chain: fast opener, faster mid, slow committal finisher. Only
    // the finisher launches, so a full string is what opens a juggle.
    private static readonly AttackStep[] Combo =
    {
        new(0.08f, 0.06f, 0.16f, 10f, 2.0f, 0.050f, 1.9f, 0f),
        new(0.08f, 0.06f, 0.18f, 12f, 2.5f, 0.060f, 1.9f, 0f),
        new(0.13f, 0.08f, 0.32f, 22f, 8.5f, 0.110f, 2.3f, 6.5f),
    };

    private const float ComboWindow = 0.45f;
    private const float DodgeDuration = 0.35f;
    private const float DodgeSpeed = 11f;
    private const float IFrameStart = 0.05f;
    private const float IFrameEnd = 0.26f;
    private const float JumpSpeed = 8.5f;

    // Lane assist: how far off-lane a target may be for a swing to pull the
    // attacker onto it, and how far the attacker slides when it does.
    private const float LaneAssistReach = 1.6f;
    private const float LaneAssistStep = 0.55f;

    // Charge moves. Time is measured from the moment attack is pressed.
    private const float ChargeStart = 0.35f;     // held past a tap: the charge pose begins
    private const float SpinMinCharge = 0.6f;    // earliest a held direction turns it into a spin
    // Standing still this long throws the mallet: one real second, expressed in
    // game time, which runs at GameClock.Speed.
    private const float FullCharge = 1.0f * (float)ClownSquad.World.GameClock.Speed;
    private const float SpinSpeed = 5.2f;
    private const float SpinHitInterval = 0.2f;
    private const float SpinMin = 0.5f;
    private const float SpinMax = 1.8f;
    private const float SpinRadius = 1.8f;
    private const float FlipForward = 6.5f;
    private const float FlipRadius = 1.9f;

    // Where the mallet head meets the ground on the follow-through frame,
    // measured from the sprite geometry: 0.833 forward is the head centre and
    // 1.035 its leading edge, so 0.975 sits just inside the striking face.
    private const float MalletForward = 0.975f;
    private const float MalletHeight = 0.203f;

    public IInputSource Source { get; set; } = null!;
    public int PlayerIndex { get; set; }

    /// <summary>False while a thrown mallet is in flight. Unarmed fighters cannot attack.</summary>
    public bool Armed { get; private set; } = true;

    // Players blink while floored and while invulnerable after respawning.
    protected override bool BlinkWhileDown => !Hostile;

    /// <summary>Scales all damage this fighter deals. Weak enemies sit below one.</summary>
    public float DamageMultiplier { get; set; } = 1f;

    private State _state = State.Free;
    private InputCommand _cmd;
    private int _comboIndex;
    private float _phaseTime;
    private float _sinceCombo = 99f;
    private bool _bufferedAttack;
    private bool _bufferedSpecial;
    private float _dodgeTime;
    private Vector3 _dodgeDir;
    private readonly HashSet<Fighter> _hitThisSwing = new();
    private bool _impactSpawned;

    private float _heldTime;       // how long attack has been down
    private bool _chargeLock;      // a mallet move just ended; release before charging again
    private float _spinTime;
    private float _spinHitTimer;
    private Vector3 _spinDir;
    private float _throwTime;
    private bool _thrown;
    private float _flipTime;

    public string DebugState => _state switch
    {
        State.Attacking => $"ATK{_comboIndex + 1}",
        State.Dodging => "DODGE",
        State.Special => "SPECIAL",
        State.Charging => $"CHARGE {Mathf.Min(_heldTime / FullCharge, 1f) * 100f:F0}%",
        State.Spinning => "SPIN",
        State.Throwing => "THROW",
        State.Flipping => "FLIP",
        _ => Armed ? "free" : "free (unarmed)",
    };

    /// <summary>Subclasses with a signature move override these three.</summary>
    protected virtual bool HasSpecial => false;
    protected virtual void OnSpecialStart() { }
    /// <summary>Advance the move; return true when it is finished.</summary>
    protected virtual bool TickSpecialMove(float dt) => true;

    protected float SpecialTime { get; private set; }
    protected bool InSpecial => _state == State.Special;

    /// <summary>Charge throw, charge spin and air flip. Off for fighters with no mallet.</summary>
    protected virtual bool HasMalletMoves => true;

    /// <summary>
    /// Multiplies melee reach. A fighter twice the size swings twice as far;
    /// without this a big enemy whiffs at distances it visually covers.
    /// </summary>
    protected virtual float ReachScale => 1f;

    /// <summary>How far off the attacker's depth lane a target can be and still
    /// be hit. Too tight and combat feels finicky; too loose and depth stops
    /// mattering.</summary>
    protected virtual float DepthBand => 0.85f;

    protected override void Tick(float dt)
    {
        InputCommand cmd = Source?.Poll() ?? InputCommand.None;
        _cmd = cmd;
        _sinceCombo += dt;
        if (_sinceCombo > ComboWindow) _comboIndex = 0;

        if (cmd.AttackPressed) _bufferedAttack = true;
        // Buffered like attacks: a special pressed mid-swing would otherwise be
        // dropped, while the requester has already spent its cooldown on it.
        if (cmd.SpecialPressed) _bufferedSpecial = true;

        if (cmd.AttackHeld)
        {
            _heldTime += dt;
        }
        else
        {
            _heldTime = 0f;
            _chargeLock = false;
        }

        switch (_state)
        {
            case State.Free: TickFree(cmd, dt); break;
            case State.Attacking: TickAttack(dt); break;
            case State.Dodging: TickDodge(dt); break;
            case State.Special: TickSpecial(dt); break;
            case State.Charging: TickCharging(cmd, dt); break;
            case State.Spinning: TickSpinning(cmd, dt); break;
            case State.Throwing: TickThrowing(dt); break;
            case State.Flipping: TickFlipping(dt); break;
        }
    }

    private void TickFree(in InputCommand cmd, float dt)
    {
        Vector3 move = ToWorld(cmd.Move);

        // Depth movement is allowed, but never turns the fighter: facing stays
        // on the X axis so attacks can only ever go left or right.
        if (Mathf.Abs(move.X) > 0.1f)
            Facing = new Vector3(Mathf.Sign(move.X), 0f, 0f);

        // Specials outrank a buffered attack. Checked the other way round, a
        // fast attack cadence starves the special.
        if (_bufferedSpecial && HasSpecial && Grounded)
        {
            _bufferedSpecial = false;
            _bufferedAttack = false;
            _state = State.Special;
            SpecialTime = 0f;
            OnSpecialStart();
            return;
        }

        if (!Grounded)
        {
            // Attack in the air is the front flip, whether tapped or still held
            // from before the jump.
            bool wantsFlip = _bufferedAttack || (_heldTime > 0.08f && !_chargeLock);
            if (HasMalletMoves && Armed && wantsFlip)
            {
                StartFlip();
                return;
            }
            _bufferedAttack = false;
        }
        else
        {
            if (_bufferedAttack)
            {
                _bufferedAttack = false;
                if (Armed)
                {
                    StartAttack();
                    return;
                }
            }

            // Still holding attack after the swing: wind up a charge move.
            if (HasMalletMoves && Armed && !_chargeLock && _heldTime >= ChargeStart)
            {
                _state = State.Charging;
                return;
            }

            if (cmd.JumpPressed)
            {
                Jump(JumpSpeed);
                Dust(0.25f);
                return;
            }

            // Dodging is a ground slide, so it needs the floor.
            if (cmd.DodgePressed)
            {
                StartDodge(move);
                return;
            }
        }

        ApplyGroundMovement(move * MoveSpeed, dt);

        if (!InHitAnim)
        {
            if (!Grounded)
            {
                // Tuck while rising, reach while falling.
                PinClipFrame("jump", Velocity.Y > 0f ? 0 : 1);
            }
            else
            {
                float speed = new Vector3(Velocity.X, 0f, Velocity.Z).Length();
                PlayClip(speed > 0.6f ? "walk" : "idle");
            }
        }
    }

    // ---------------------------------------------------------------- combo

    private void StartAttack()
    {
        _state = State.Attacking;
        _phaseTime = 0f;
        _hitThisSwing.Clear();
        _impactSpawned = false;
        _sinceCombo = 0f;
        AssistLane();
    }

    /// <summary>
    /// Slide a short way onto the depth lane of whoever the swing is aimed at.
    /// Lining up by hand on a tilted camera is the least fun part of the genre;
    /// every modern brawler quietly helps, and nobody notices that it does.
    /// </summary>
    private void AssistLane()
    {
        Fighter? best = null;
        float bestOffset = LaneAssistReach;
        float reach = Combo[_comboIndex].Range * ReachScale * 1.15f;

        foreach (Fighter other in Opponents())
        {
            Vector3 to = other.GlobalPosition - GlobalPosition;
            float forward = to.X * Facing.X;
            if (forward < -0.3f || forward > reach) continue;
            float offset = Mathf.Abs(to.Z);
            if (offset < bestOffset) { best = other; bestOffset = offset; }
        }

        if (best is null || bestOffset < 0.05f) return;
        float z = Mathf.MoveToward(GlobalPosition.Z, best.GlobalPosition.Z, LaneAssistStep);
        GlobalPosition = new Vector3(GlobalPosition.X, GlobalPosition.Y, z);
    }

    private void TickAttack(float dt)
    {
        AttackStep step = Combo[_comboIndex];
        float prev = _phaseTime;
        _phaseTime += dt;

        // Slight forward drift makes swings feel committed rather than static.
        float drift = _phaseTime < step.Startup + step.Active ? 2.2f : 0.6f;
        ApplyGroundMovement(Facing * drift, dt);

        float activeStart = step.Startup;
        float activeEnd = step.Startup + step.Active;

        // Impact fires once per swing, whether or not it connected: the swing
        // should feel like it hit the world. Timed to the second half of the
        // active window, the frame where the mallet meets the ground.
        if (!_impactSpawned && _phaseTime >= activeStart + step.Active * 0.5f)
        {
            _impactSpawned = true;
            if (HasMalletMoves)   // only a mallet strikes the ground
            {
                Vector3 at = GlobalPosition + Facing * MalletForward + new Vector3(0f, MalletHeight, 0f);
                ImpactEffect.Spawn(GetParent(), at, 0.7f + _comboIndex * 0.25f);
                CameraShake.Request(this, 0.035f + _comboIndex * 0.03f, 0.16f + _comboIndex * 0.04f);
            }
        }

        // Pin the swing frame to the phase, so the visible swing is the hitbox.
        if (!InHitAnim)
        {
            int frame = _phaseTime < step.Startup * 0.5f ? 0
                      : _phaseTime < activeStart ? 1
                      : _phaseTime < activeStart + step.Active * 0.5f ? 2
                      : 3;
            PinClipFrame("attack", frame);
        }

        if (_phaseTime > activeStart && prev < activeEnd)
            ResolveSwing(step);

        // Recovery can be cancelled into a dodge. An attack you cannot get out
        // of is an attack you stop using once enemies crowd in.
        if (_phaseTime >= activeEnd && _cmd.DodgePressed && Grounded)
        {
            _sinceCombo = 0f;
            StartDodge(ToWorld(_cmd.Move));
            return;
        }

        if (_phaseTime >= activeEnd + step.Recovery)
        {
            _sinceCombo = 0f;
            if (_bufferedAttack && _comboIndex < Combo.Length - 1)
            {
                _bufferedAttack = false;
                _comboIndex++;
                StartAttack();
            }
            else
            {
                _bufferedAttack = false;
                _comboIndex = _comboIndex >= Combo.Length - 1 ? 0 : _comboIndex;
                _state = State.Free;
            }
        }
    }

    /// <summary>
    /// A forward box, not a cone. Facing is locked to the X axis, so a cone
    /// around it misses anyone offset in depth even when they look adjacent on
    /// screen. Brawlers want depth lanes: line up, then hit.
    /// </summary>
    private void ResolveSwing(in AttackStep step)
    {
        foreach (Fighter other in Opponents())
        {
            if (_hitThisSwing.Contains(other)) continue;

            Vector3 to = other.GlobalPosition - GlobalPosition;
            float forward = to.X * Facing.X;
            if (forward <= 0f || forward > step.Range * ReachScale) continue;
            if (Mathf.Abs(to.Z) > DepthBand * ReachScale) continue;
            if (Mathf.Abs(to.Y) > 1.9f * ReachScale) continue;   // juggled targets stay reachable

            _hitThisSwing.Add(other);
            // Knock straight along the facing, so hits shove down the lane
            // instead of scattering sideways.
            Strike(other, step.Damage, Facing, step.Knockback, step.Hitstop, step.Launch);
        }
    }

    // ---------------------------------------------------------------- dodge

    private void StartDodge(Vector3 move)
    {
        _dodgeDir = move.LengthSquared() > 0.001f ? move.Normalized() : Facing;
        // Turn into the slide, so the pose never reads as moonwalking.
        if (Mathf.Abs(_dodgeDir.X) > 0.1f)
            Facing = new Vector3(Mathf.Sign(_dodgeDir.X), 0f, 0f);
        _state = State.Dodging;
        _dodgeTime = 0f;
        _bufferedAttack = false;
        Dust(0.3f);
    }

    private void TickDodge(float dt)
    {
        _dodgeTime += dt;
        float t = _dodgeTime / DodgeDuration;

        // Pinned rather than played, so the slide pose tracks the movement
        // curve: drop, hold the extended slide, gather to recover.
        if (!InHitAnim)
            PinClipFrame("dodge", t < 0.22f ? 0 : t < 0.72f ? 1 : 2);

        ApplyGroundMovement(_dodgeDir * DodgeSpeed * (1f - t * 0.75f), dt);

        if (_dodgeTime >= DodgeDuration)
        {
            _state = State.Free;
            _bufferedAttack = false;
        }
    }

    private void TickSpecial(float dt)
    {
        SpecialTime += dt;
        if (TickSpecialMove(dt))
        {
            _state = State.Free;
            SpecialTime = 0f;
        }
    }

    // --------------------------------------------------------- charge moves

    /// <summary>
    /// Winding up. Standing still for the full charge throws the mallet; a held
    /// direction converts the wind-up into a travelling spin instead. Letting
    /// go early simply abandons it.
    /// </summary>
    private void TickCharging(in InputCommand cmd, float dt)
    {
        ApplyGroundMovement(Vector3.Zero, dt);

        if (!cmd.AttackHeld) { EndMalletMove(lockCharge: false); return; }
        if (cmd.DodgePressed) { EndMalletMove(); StartDodge(ToWorld(cmd.Move)); return; }
        if (cmd.JumpPressed)
        {
            // Still holding attack, so this carries straight into the flip.
            EndMalletMove(lockCharge: false);
            Jump(JumpSpeed);
            Dust(0.25f);
            return;
        }

        if (Mathf.Abs(cmd.Move.X) > 0.35f)
        {
            Facing = new Vector3(Mathf.Sign(cmd.Move.X), 0f, 0f);
            if (_heldTime >= SpinMinCharge) { StartSpin(); return; }
        }

        if (_heldTime >= FullCharge) { StartThrow(); return; }

        // The shudder and the glow both quicken as the charge builds, so the
        // player can read how close the throw is without a meter.
        float charge = Mathf.Clamp(_heldTime / FullCharge, 0f, 1f);
        PinClipFrame("charge", (int)(_heldTime * Mathf.Lerp(7f, 24f, charge)) % 2);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_heldTime * Mathf.Lerp(9f, 34f, charge));
        Visual.Tint = BaseTint.Lerp(new Color(2.0f, 1.7f, 0.8f), charge * (0.3f + 0.7f * pulse));
    }

    private void EndMalletMove(bool lockCharge = true)
    {
        Visual.Tint = BaseTint;
        Visual.FlipOverride = null;
        _state = State.Free;
        if (lockCharge) _chargeLock = true;
    }

    private void StartSpin()
    {
        Visual.Tint = BaseTint;
        _spinDir = Facing;
        _spinTime = 0f;
        _spinHitTimer = 0f;
        _state = State.Spinning;
    }

    /// <summary>
    /// Whirl along the lane, hitting everything in reach several times. With
    /// only a profile view to draw from, the whirl is the mallet-out pose
    /// mirrored rapidly back and forth.
    /// </summary>
    private void TickSpinning(in InputCommand cmd, float dt)
    {
        _spinTime += dt;
        ApplyGroundMovement(_spinDir * SpinSpeed + new Vector3(0f, 0f, cmd.Move.Y * 2f), dt);

        PlayClip("spin");
        bool mirrored = (int)(_spinTime / 0.07f) % 2 == 1;
        Visual.FlipOverride = (_spinDir.X < 0f) != mirrored;

        _spinHitTimer -= dt;
        if (_spinHitTimer <= 0f)
        {
            _spinHitTimer = SpinHitInterval;
            foreach (Fighter other in Opponents())
            {
                Vector3 to = other.GlobalPosition - GlobalPosition;
                if (new Vector2(to.X, to.Z).Length() > SpinRadius || Mathf.Abs(to.Y) > 1.9f) continue;
                Strike(other, 6f, _spinDir, 1.4f, 0.025f, 0f, 0.3f);
            }
        }

        bool stillHeld = cmd.AttackHeld && Mathf.Abs(cmd.Move.X) > 0.2f;
        if (_spinTime >= SpinMax || (_spinTime >= SpinMin && !stillHeld))
            EndMalletMove();
    }

    private void StartThrow()
    {
        Visual.Tint = BaseTint;
        _throwTime = 0f;
        _thrown = false;
        _state = State.Throwing;
    }

    private void TickThrowing(float dt)
    {
        _throwTime += dt;
        ApplyGroundMovement(Vector3.Zero, dt);

        if (!_thrown)
        {
            PinClipFrame("throw", 0);
            if (_throwTime >= 0.14f)
            {
                _thrown = true;
                Armed = false;
                Visual.ClipPrefix = "empty_";
                MalletProjectile.Spawn(this, Facing);
                CameraShake.Request(this, 0.04f, 0.12f);
            }
        }
        else
        {
            PinClipFrame("throw", 1);
        }

        if (_throwTime >= 0.38f) EndMalletMove();
    }

    /// <summary>The boomerang came home.</summary>
    public void CatchMallet()
    {
        if (Armed) return;
        Armed = true;
        Visual.ClipPrefix = "";
        Visual.Squash(0.25f);
        _chargeLock = true;
    }

    private void StartFlip()
    {
        _bufferedAttack = false;
        _flipTime = 0f;
        _hitThisSwing.Clear();
        _state = State.Flipping;
        // A little lift, so a flip started at the top of a jump still completes.
        Velocity = new Vector3(Facing.X * FlipForward, Mathf.Max(Velocity.Y, 3.5f), Velocity.Z * 0.5f);
    }

    /// <summary>Front flip: carries forward, hits each enemy once, slams on landing.</summary>
    private void TickFlipping(float dt)
    {
        _flipTime += dt;
        Velocity = new Vector3(Facing.X * FlipForward, Velocity.Y, Velocity.Z);
        PinClipFrame("flip", (int)(_flipTime / 0.05f) % 8);

        foreach (Fighter other in Opponents())
        {
            if (_hitThisSwing.Contains(other)) continue;
            Vector3 to = other.GlobalPosition - GlobalPosition;
            if (new Vector2(to.X, to.Z).Length() > FlipRadius || Mathf.Abs(to.Y) > 2.0f) continue;
            _hitThisSwing.Add(other);
            Strike(other, 16f, Facing, 5f, 0.07f, 5.5f, 0.35f);
        }

        if (Grounded && _flipTime > 0.08f)
        {
            ImpactEffect.Spawn(GetParent(), GlobalPosition + Facing * 0.7f + new Vector3(0f, 0.15f, 0f), 0.9f);
            CameraShake.Request(this, 0.06f, 0.2f);
            Visual.Squash(0.3f);
            EndMalletMove();
        }
    }

    // ------------------------------------------------------------- striking

    private IEnumerable<Fighter> Opponents()
    {
        foreach (Node node in GetTree().GetNodesInGroup(Group))
        {
            if (node is Fighter other && other != this && other.Hostile != Hostile && other.CanBeHit())
                yield return other;
        }
    }

    private void Strike(Fighter other, float damage, Vector3 dir, float knockback,
                        float hitstop, float launch = 0f, float stun = 0.26f)
    {
        other.TakeDamage(new DamageInfo(damage * DamageMultiplier, dir, knockback, hitstop, this, launch, stun));
        ApplyHitstop(hitstop);
        Spark(other);
    }

    /// <summary>A hit delivered by this fighter's thrown weapon rather than its body.</summary>
    public void StrikeFromProjectile(Fighter other, float damage, Vector3 dir, float knockback, float launch)
    {
        other.TakeDamage(new DamageInfo(damage * DamageMultiplier, dir, knockback, 0.06f, this, launch, 0.35f));
        Spark(other);
    }

    private void Spark(Fighter other) =>
        ImpactEffect.Spawn(GetParent(), other.GlobalPosition + new Vector3(0f, other.BodyHeight * 0.55f, 0f), 0.34f);

    // -------------------------------------------------------------- hooks

    protected override void OnInterrupted()
    {
        // Being hit drops whatever was in progress. A thrown mallet stays out.
        Visual.Tint = BaseTint;
        Visual.FlipOverride = null;
        _state = State.Free;
        _bufferedAttack = false;
        _chargeLock = true;
    }

    protected override void OnRevived()
    {
        Armed = true;
        Visual.ClipPrefix = "";
        Visual.Tint = BaseTint;
        Visual.FlipOverride = null;
        _state = State.Free;
        _bufferedAttack = false;
        _bufferedSpecial = false;
        _comboIndex = 0;
    }

    /// <summary>Invulnerable through the middle of the roll, not its edges.</summary>
    public override bool CanBeHit()
    {
        if (_state == State.Dodging && _dodgeTime >= IFrameStart && _dodgeTime <= IFrameEnd)
            return false;
        return base.CanBeHit();
    }

    /// <summary>
    /// Stick space to world space. The camera looks down -Z at 35 degrees, so
    /// stick-up must travel away from the camera.
    /// </summary>
    private static Vector3 ToWorld(Vector2 move) => new(move.X, 0f, move.Y);
}
