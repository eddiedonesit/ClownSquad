namespace ClownSquad.Actors;

using System.Collections.Generic;
using Godot;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Visuals;

/// <summary>
/// A fighter driven by an <see cref="IInputSource"/>. Knows nothing about where
/// the input came from, so the same class serves single-player, local co-op and
/// (later) remote peers. See ADR 0003.
/// </summary>
public partial class PlayerFighter : Fighter
{
    private enum State { Free, Attacking, Dodging }

    /// <summary>One swing in the combo. Timings are in seconds.</summary>
    private readonly record struct AttackStep(
        float Startup, float Active, float Recovery,
        float Damage, float Knockback, float Hitstop,
        float Range, float HalfArcDegrees);

    // Escalating chain: fast opener, faster mid, slow committal finisher.
    private static readonly AttackStep[] Combo =
    {
        new(0.08f, 0.06f, 0.16f, 10f, 2.0f, 0.050f, 1.9f, 60f),
        new(0.08f, 0.06f, 0.18f, 12f, 2.5f, 0.060f, 1.9f, 60f),
        new(0.13f, 0.08f, 0.32f, 22f, 6.5f, 0.110f, 2.3f, 75f),
    };

    private const float ComboWindow = 0.45f;
    private const float DodgeDuration = 0.35f;
    private const float DodgeSpeed = 11f;
    private const float IFrameStart = 0.05f;
    private const float IFrameEnd = 0.26f;
    private const float JumpSpeed = 8.5f;

    // Where the mallet head sits on the GROUND-CONTACT frame, measured from the
    // sprite geometry. The strike frame (45 deg) puts the head at 0.657/0.837,
    // which is head height - centring the burst there made it bloom around the
    // clown rather than at the floor. The follow-through frame is the one that
    // actually meets the ground.
    // 0.833 is the head centre, 1.035 its leading edge; sit just inside the
    // front face so the blast reads as thrown off the striking surface.
    private const float MalletForward = 0.975f;
    private const float MalletHeight = 0.203f;

    public IInputSource Source { get; set; } = null!;
    public int PlayerIndex { get; set; }

    private State _state = State.Free;
    private int _comboIndex;
    private float _phaseTime;
    private float _sinceCombo = 99f;
    private bool _bufferedAttack;
    private float _dodgeTime;
    private Vector3 _dodgeDir;
    private readonly HashSet<Fighter> _hitThisSwing = new();
    private bool _impactSpawned;

    public string DebugState => _state switch
    {
        State.Attacking => $"ATK{_comboIndex + 1}",
        State.Dodging => "DODGE",
        _ => "free",
    };

    protected override void Tick(float dt)
    {
        InputCommand cmd = Source?.Poll() ?? InputCommand.None;
        _sinceCombo += dt;
        if (_sinceCombo > ComboWindow) _comboIndex = 0;

        if (cmd.AttackPressed) _bufferedAttack = true;

        switch (_state)
        {
            case State.Free: TickFree(cmd, dt); break;
            case State.Attacking: TickAttack(dt); break;
            case State.Dodging: TickDodge(dt); break;
        }
    }

    private void TickFree(in InputCommand cmd, float dt)
    {
        Vector3 move = ToWorld(cmd.Move);

        // Depth movement is allowed, but never turns the fighter: facing stays
        // on the X axis so attacks can only ever go left or right.
        if (Mathf.Abs(move.X) > 0.1f)
            Facing = new Vector3(Mathf.Sign(move.X), 0f, 0f);

        if (_bufferedAttack)
        {
            _bufferedAttack = false;
            StartAttack();
            return;
        }

        if (cmd.JumpPressed && Grounded)
        {
            Jump(JumpSpeed);
            ImpactEffect.Spawn(GetParent(), GlobalPosition + new Vector3(0f, 0.1f, 0f), 0.25f);
            return;
        }

        // Dodging is a ground slide, so it needs the floor.
        if (cmd.DodgePressed && Grounded)
        {
            _dodgeDir = move.LengthSquared() > 0.001f ? move.Normalized() : Facing;
            // Turn into the slide, so the pose never reads as moonwalking when
            // dodging away from the current facing.
            if (Mathf.Abs(_dodgeDir.X) > 0.1f)
                Facing = new Vector3(Mathf.Sign(_dodgeDir.X), 0f, 0f);
            _state = State.Dodging;
            _dodgeTime = 0f;
            // scuff of dust kicked up as the slide launches
            ImpactEffect.Spawn(GetParent(), GlobalPosition + new Vector3(0f, 0.1f, 0f), 0.3f);
            return;
        }

        ApplyGroundMovement(move * MoveSpeed, dt);

        if (!InHitAnim)
        {
            if (!Grounded)
            {
                // Airborne pose overrides the ground clips: tuck while rising,
                // reach while falling.
                PinClipFrame("jump", Velocity.Y > 0f ? 0 : 1);
            }
            else
            {
                float speed = new Vector3(Velocity.X, 0f, Velocity.Z).Length();
                PlayClip(speed > 0.6f ? "walk" : "idle");
            }
        }
    }

    private void StartAttack()
    {
        _state = State.Attacking;
        _phaseTime = 0f;
        _hitThisSwing.Clear();
        _impactSpawned = false;
        _sinceCombo = 0f;
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
        // active window, which is now the frame where the mallet meets the
        // ground, so the burst and the visible contact coincide.
        if (!_impactSpawned && _phaseTime >= activeStart + step.Active * 0.5f)
        {
            _impactSpawned = true;
            Vector3 at = GlobalPosition + Facing * MalletForward + new Vector3(0f, MalletHeight, 0f);
            ImpactEffect.Spawn(GetParent(), at, 0.7f + _comboIndex * 0.25f);
            CameraShake.Request(this, 0.035f + _comboIndex * 0.03f, 0.16f + _comboIndex * 0.04f);
        }

        // Pin the swing frame to the phase, so the visible swing is the hitbox.
        if (!InHitAnim)
        {
            // Startup covers the windup and raise; the active window carries the
            // strike through to ground contact, so the frame the player reads as
            // the hit is the frame the hitbox is live on.
            int frame = _phaseTime < step.Startup * 0.5f ? 0
                      : _phaseTime < activeStart ? 1
                      : _phaseTime < activeStart + step.Active * 0.5f ? 2
                      : 3;
            PinClipFrame("attack", frame);
        }
        if (_phaseTime > activeStart && prev < activeEnd)
            ResolveHits(step);

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
    /// Arc query rather than physics hitboxes: deterministic, trivially
    /// inspectable, and host-authoritative by construction. Swap for real
    /// hitboxes when attacks need non-conical shapes.
    /// </summary>
    private void ResolveHits(in AttackStep step)
    {
        foreach (Node node in GetTree().GetNodesInGroup(Group))
        {
            if (node is not Fighter other || other == this || !other.CanBeHit()) continue;
            if (_hitThisSwing.Contains(other)) continue;

            Vector3 to = other.GlobalPosition - GlobalPosition;
            to.Y = 0f;
            float dist = to.Length();
            if (dist > step.Range || dist < 0.01f) continue;
            if (Mathf.RadToDeg(Facing.AngleTo(to / dist)) > step.HalfArcDegrees) continue;

            _hitThisSwing.Add(other);
            other.TakeDamage(new DamageInfo(step.Damage, to / dist, step.Knockback, step.Hitstop, this));
            ApplyHitstop(step.Hitstop);
        }
    }

    private void TickDodge(float dt)
    {
        _dodgeTime += dt;
        float t = _dodgeTime / DodgeDuration;

        // Pinned rather than played, so the slide pose tracks the actual
        // movement curve: drop, hold the extended slide, gather to recover.
        if (!InHitAnim)
            PinClipFrame("dodge", t < 0.22f ? 0 : t < 0.72f ? 1 : 2);

        ApplyGroundMovement(_dodgeDir * DodgeSpeed * (1f - t * 0.75f), dt);

        if (_dodgeTime >= DodgeDuration)
        {
            _state = State.Free;
            _bufferedAttack = false;
        }
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
