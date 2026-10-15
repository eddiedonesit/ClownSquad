namespace ClownSquad.Actors;

using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Progression;
using ClownSquad.Visuals;

/// <summary>
/// A fighter driven by an <see cref="IInputSource"/>. Knows nothing about where
/// the input came from, so the same class serves single-player, local co-op,
/// AI enemies and (later) remote peers. See ADR 0003.
/// </summary>
public partial class PlayerFighter : Fighter
{
    private enum State { Free, Attacking, Dodging, Special, Charging, Spinning, Throwing, Flipping, Quaking, Lunging }

    // The combo comes from the weapon in hand (Weapons.cs): an escalating chain
    // where only the finisher launches, so a full string is what opens a juggle.
    private AttackStep[] Combo = Weapons.Get(WeaponKind.Mallet).Combo;

    /// <summary>The weapon in hand, from the record. Fixed for the length of a stage.</summary>
    public WeaponKind Weapon { get; private set; } = WeaponKind.Mallet;
    private WeaponDef _weapon = Weapons.Get(WeaponKind.Mallet);
    private WeaponPerks _perks = WeaponPerks.None;
    private bool _secondWindUsed;

    // The hammer's charge move: a ground pound, and (with AFTERSHOCK) a second rumble.
    private const float QuakeRadius = 2.6f;
    private const float QuakeDamage = 30f;
    private const float QuakeImpact = 0.18f;
    private const float AftershockAt = 0.62f;
    private float _quakeTime;
    private int _quakeHits;

    // The balloon sword's charge: a short coil, then a dash that runs everyone through.
    private const float LungeWind = 0.14f;
    private const float LungeDash = 0.26f;
    private const float LungeSpeed = 15f;
    private float _lungeTime;

    // A ranged weapon fires once per combo step.
    private bool _fired;

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

    public override string AttackSound => !HasMalletMoves ? "slap" : _weapon.HitSound;
    public override float AttackPitch => HasMalletMoves ? _weapon.HitPitch : 1f;
    public override string HurtSound => Hostile ? "squeak" : "honk";
    public override string DeathSound => Hostile ? "slide_down" : "wah_short";

    private AudioStreamPlayer? _chargeVoice;

    /// <summary>Scales all damage this fighter deals. Weak enemies sit below one.</summary>
    public float DamageMultiplier { get; set; } = 1f;

    /// <summary>A multiplier on top of the stats, for tests and debugging; never saved.</summary>
    public float ExtraDamageMultiplier { get; set; } = 1f;

    /// <summary>The persistent character this fighter plays, if any. Enemies have none.</summary>
    public CharacterRecord? Record { get; private set; }

    /// <summary>Chance per hit of a critical, from LUCK. Zero for anything without a record.</summary>
    public float CritChance { get; private set; }

    private float _baseMoveSpeed = -1f;

    /// <summary>Which squad clown this player looks like (Roster id). Set before the fighter enters the scene.</summary>
    public string Look { get; set; } = "pip";

    protected override string SheetPath => Hostile ? base.SheetPath : ClownSquad.Progression.Roster.Sheet(Look, Weapon);
    private float _critMultiplier = 1.75f;
    private float _launchMultiplier = 1f;
    private float _kaboom;
    private bool _kaboomed;

    /// <summary>Enemy level and rank, set by whoever spawns it (GDD 3, 7). Players leave them alone.</summary>
    public int EnemyLevel { get; private set; } = 1;
    public EnemyRank Rank { get; private set; } = EnemyRank.Common;
    private readonly RandomNumberGenerator _critRng = new();

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
        State.Quaking => _weapon.Charge == ChargeMove.Clap ? "CLAP" : "QUAKE",
        State.Lunging => "LUNGE",
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

    /// <summary>
    /// Stretches every combo swing in time. Above one gives a big fighter a
    /// readable wind-up and a longer punish window after it.
    /// </summary>
    protected virtual float SwingTimeScale => 1f;

    /// <summary>
    /// The swing in progress, if any: combo index, time into it, and its startup,
    /// active window and reach. For subclasses that animate a prop to match.
    /// </summary>
    protected bool TryGetSwing(out int index, out float time, out float startup, out float active, out float reach)
    {
        index = _comboIndex;
        time = _phaseTime;
        AttackStep step = Combo[_comboIndex];
        startup = step.Startup;
        active = step.Active;
        reach = step.Range * ReachScale;
        return _state == State.Attacking;
    }

    /// <summary>
    /// Play a persistent character and take on its stats. Called again whenever
    /// the record changes (a level gained, points spent); extra max health from
    /// GUTS arrives as health too, so spending points mid-fight is never a loss.
    /// </summary>
    public void ApplyProgression(CharacterRecord record)
    {
        Record = record;
        if (_baseMoveSpeed < 0f) _baseMoveSpeed = MoveSpeed;
        // The weapon is taken up once, before the sprite is built; it cannot
        // change mid-stage, but learned skills can (the skill menu works anywhere).
        if (!IsInsideTree())
        {
            Weapon = record.Weapon;
            _weapon = Weapons.Get(Weapon);
        }
        _perks = Weapons.Perks(record);
        Combo = BuildCombo(_weapon, _perks);
        float oldMax = MaxHealth;
        MaxHealth = Leveling.MaxHealth(record);
        DamageMultiplier = Leveling.DamageMultiplier(record);
        MoveSpeed = _baseMoveSpeed * Leveling.SpeedMultiplier(record);
        CritChance = Leveling.CritChance(record);
        _critMultiplier = Leveling.CritMultiplier(record);
        _launchMultiplier = Leveling.LaunchMultiplier(record);
        _kaboom = Leveling.Kaboom(record);
        _critRng.Randomize();
        if (IsInsideTree() && !IsDead && MaxHealth > oldMax) Heal(MaxHealth - oldMax);
    }

    /// <summary>The weapon's combo with the skill tree applied: extra swings go in before the finisher.</summary>
    private static AttackStep[] BuildCombo(WeaponDef w, WeaponPerks k)
    {
        var steps = new List<AttackStep>(w.Combo);
        for (int i = 0; i < k.ExtraSteps; i++) steps.Insert(steps.Count - 1, w.Extra);
        for (int i = 0; i < steps.Count; i++)
        {
            AttackStep s = steps[i];
            bool last = i == steps.Count - 1;
            steps[i] = s with
            {
                Damage = s.Damage * k.Damage * (last ? k.Finisher : 1f),
                Knockback = s.Knockback * k.Knockback,
                Launch = s.Launch * k.Launch,
            };
        }
        return steps.ToArray();
    }

    /// <summary>
    /// Scale an enemy to a level and give it a rank: health and damage grow with
    /// level, then by rank, and ranked enemies get a colour and a glowing aura so
    /// they are spotted at a glance (GDD 3).
    /// </summary>
    public void ApplyEnemyRank(int level, EnemyRank rank)
    {
        EnemyLevel = Mathf.Max(1, level);
        Rank = rank;
        float stats = Leveling.RankStats(rank);
        ScaleHealth(Leveling.EnemyHealthScale(EnemyLevel) * stats);
        DamageMultiplier *= Leveling.EnemyDamageScale(EnemyLevel) * (rank == EnemyRank.Common ? 1f : Mathf.Sqrt(stats));
        if (rank == EnemyRank.Common) return;

        (Color tint, Color aura, string tag, float size) = rank switch
        {
            EnemyRank.Rare => (new Color(1.12f, 1.04f, 0.8f), new Color(1f, 0.85f, 0.35f), "RARE", 1.05f),
            EnemyRank.Epic => (new Color(1.1f, 0.85f, 1.25f), new Color(0.75f, 0.4f, 1f), "EPIC", 1.12f),
            _ => (new Color(1.3f, 0.95f, 0.65f), new Color(1f, 0.5f, 0.1f), "LEGENDARY", 1.2f),
        };
        BaseTint = tint;
        Visual.Scale = Vector3.One * size;
        DisplayName = $"{tag} {DisplayName}";
        AddChild(new OmniLight3D
        {
            LightColor = aura,
            // A halo on the floor and a rim on the sprite, not a floodlight.
            LightEnergy = rank switch { EnemyRank.Legendary => 2.4f, EnemyRank.Epic => 1.4f, _ => 0.9f },
            OmniRange = rank == EnemyRank.Legendary ? 2.6f : 1.7f,
            Position = new Vector3(0f, 0.35f, -0.35f),
        });
    }

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
            case State.Quaking: TickQuake(dt); break;
            case State.Lunging: TickLunge(dt); break;
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
                _chargeVoice = Sfx.Play("kazoo_rise");
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
        _fired = false;
        _kaboomed = false;
        _sinceCombo = 0f;
        AssistLane();
        Sfx.Play("whoosh", 1f + 0.12f * _comboIndex);
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
        _phaseTime += dt / SwingTimeScale * _perks.SwingSpeed;

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
            float heft = _weapon.Heft;
            if (HasMalletMoves && heft > 0f)   // only something heavy strikes the ground
            {
                Vector3 at = GlobalPosition + Facing * MalletForward + new Vector3(0f, MalletHeight, 0f);
                ImpactEffect.Spawn(GetParent(), at, (0.7f + _comboIndex * 0.25f) * heft);
                Sfx.Play("thump", Weapon == WeaponKind.Hammer ? 0.8f : 1.1f, -4f);
                CameraShake.Request(this, (0.035f + _comboIndex * 0.03f) * heft, 0.16f + _comboIndex * 0.04f);
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

        if (_weapon.Ranged && HasMalletMoves)
        {
            if (!_fired && _phaseTime >= activeStart) { _fired = true; FireCork(step); }
        }
        else if (_phaseTime > activeStart && prev < activeEnd)
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
            Strike(other, step.Damage, Facing, step.Knockback, step.Hitstop, step.Launch * _launchMultiplier);
            // A pie in the face: the finisher leaves them reeling.
            if (_weapon.FinisherStagger > 0f && _comboIndex == Combo.Length - 1 && !other.IsDead)
                other.Stagger(_weapon.FinisherStagger * _perks.Blind);
        }

        // KABOOM gear, or POP GOES THE... on a balloon sword: the finisher goes
        // off like a firework, once per swing.
        float boom = _kaboom + (_perks.PopFinisher ? 14f : 0f);
        if (boom > 0f && _comboIndex == Combo.Length - 1 && _hitThisSwing.Count > 0 && !_kaboomed)
        {
            _kaboomed = true;
            Vector3 at = GlobalPosition + Facing * (step.Range * ReachScale * 0.7f);
            ImpactEffect.Spawn(GetParent(), at + new Vector3(0f, 0.3f, 0f), 1.8f);
            Sfx.Play("kaboom", 1.4f, -6f);
            CameraShake.Request(this, 0.12f, 0.25f);
            foreach (Fighter other in Opponents())
            {
                Vector3 d = other.GlobalPosition - at;
                d.Y = 0f;
                if (d.Length() > 1.8f) continue;
                other.TakeDamage(new DamageInfo(boom * DamageMultiplier, d.LengthSquared() > 0.01f ? d.Normalized() : Facing, 5f, 0.08f, this, 4f, 0.35f));
            }
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
        Sfx.Play("zip");
        if (_perks.SquawkDodge)
        {
            // SQUAWK: the slide goes off like a startled hen; anyone close is frozen to the spot.
            Sfx.Play("squawk", 1.1f);
            foreach (Fighter other in Opponents())
            {
                Vector3 to = other.GlobalPosition - GlobalPosition;
                if (new Vector2(to.X, to.Z).Length() < 1.9f) other.Stagger(0.9f);
            }
        }
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

        if (_heldTime >= FullCharge)
        {
            switch (_weapon.Charge)
            {
                case ChargeMove.Quake:
                case ChargeMove.Clap: StartQuake(); break;
                case ChargeMove.Lunge: StartLunge(); break;
                default: StartThrow(); break;
            }
            return;
        }

        // The shudder and the glow both quicken as the charge builds, so the
        // player can read how close the throw is without a meter.
        float charge = Mathf.Clamp(_heldTime / FullCharge, 0f, 1f);
        PinClipFrame("charge", (int)(_heldTime * Mathf.Lerp(7f, 24f, charge)) % 2);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_heldTime * Mathf.Lerp(9f, 34f, charge));
        Visual.Tint = BaseTint.Lerp(new Color(2.0f, 1.7f, 0.8f), charge * (0.3f + 0.7f * pulse));
    }

    private void EndMalletMove(bool lockCharge = true)
    {
        Sfx.Stop(_chargeVoice);
        _chargeVoice = null;
        Visual.Tint = BaseTint;
        Visual.FlipOverride = null;
        _state = State.Free;
        if (lockCharge) _chargeLock = true;
    }

    private void StartSpin()
    {
        Visual.Tint = BaseTint;
        Sfx.Stop(_chargeVoice);
        _chargeVoice = null;
        Sfx.Play("whirr", 1.15f);
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
                Strike(other, 6f * _perks.SpinDamage * (Weapon == WeaponKind.Hammer ? 1.4f : 1f), _spinDir, 1.4f, 0.025f, 0f, 0.3f);
            }
        }

        bool stillHeld = cmd.AttackHeld && Mathf.Abs(cmd.Move.X) > 0.2f;
        if (_spinTime >= SpinMax * _perks.SpinTime || (_spinTime >= SpinMin && !stillHeld))
            EndMalletMove();
    }

    private void StartThrow()
    {
        Visual.Tint = BaseTint;
        Sfx.Stop(_chargeVoice);
        _chargeVoice = null;
        _throwTime = 0f;
        _thrown = false;
        _state = State.Throwing;
    }

    private void TickThrowing(float dt)
    {
        _throwTime += dt;
        ApplyGroundMovement(Vector3.Zero, dt);

        // The pop gun fires from the shoulder; everything else is thrown.
        bool gun = _weapon.Charge == ChargeMove.BigShot;
        if (!_thrown)
        {
            PinClipFrame(gun ? "attack" : "throw", gun ? 1 : 0);
            if (_throwTime >= 0.14f && _weapon.Charge != ChargeMove.Throw)
            {
                // Pies, pins and corks do not come back: there are always more in the pockets.
                _thrown = true;
                Toss();
            }
            else if (_throwTime >= 0.14f)
            {
                _thrown = true;
                Armed = false;
                Visual.ClipPrefix = "empty_";
                MalletProjectile.Spawn(this, Facing, new MalletProjectile.Throw(
                    _weapon.SpinSheet,
                    _perks.ThrowDamage,
                    _perks.ThrowRange * (Weapon == WeaponKind.Chicken ? 1.5f : 1f),
                    _perks.ThrowExplodes, _perks.ThrowLaunches));
                Sfx.Play(_weapon.ThrowSound);
                CameraShake.Request(this, 0.04f, 0.12f);
            }
        }
        else
        {
            PinClipFrame(gun ? "attack" : "throw", gun ? 2 : 1);
        }

        if (_throwTime >= 0.38f) EndMalletMove();
    }

    private Vector3 Hand => GlobalPosition + new Vector3(Facing.X * 0.55f, 1.05f, 0f);

    /// <summary>The charge throws that do not come back: a lobbed pie, a fan of pins, the big cork.</summary>
    private void Toss()
    {
        int extra = _perks.ExtraShots;
        CameraShake.Request(this, 0.04f, 0.12f);
        Sfx.Play(_weapon.ThrowSound, _weapon.Charge == ChargeMove.BigShot ? 0.6f : 1f);
        switch (_weapon.Charge)
        {
            case ChargeMove.Lob:
                for (int i = 0; i <= extra; i++)
                {
                    Vector3 to = GlobalPosition + Facing * (4.6f + 1.3f * i) + new Vector3(0f, 0f, (i % 2 == 0 ? 1f : -1f) * 0.6f * ((i + 1) / 2));
                    if (ClownSquad.World.Arena.Current is ClownSquad.World.Arena arena) to = arena.Clamp(to, 0f, 0.6f, out _);
                    var pie = PlayerShot.Lob(this, PlayerShot.Pie(1.5f), Hand, new Vector3(to.X, 0f, to.Z), 0.55f + 0.08f * i);
                    pie.Damage = 16f * _perks.ThrowDamage;
                    pie.Splat = 1.5f * _perks.SplatRadius;
                    pie.Blind = 1.4f * _perks.Blind;
                    pie.Knockback = 3f;
                    pie.SpinSpeed = 4f;
                }
                break;
            case ChargeMove.Fan:
                int n = 3 + extra;
                for (int i = 0; i < n; i++)
                {
                    float z = (i - (n - 1) * 0.5f) * 0.16f;
                    var pin = PlayerShot.Fire(this, EnemyShot.Pin(1.4f), Hand, new Vector3(Facing.X, 0f, z), 15f);
                    pin.Damage = 12f * _perks.ThrowDamage;
                    pin.Range = 9f * _perks.ThrowRange;
                    pin.Pierce = _perks.Pierce;
                    pin.Launch = _perks.ThrowLaunches ? 5f : 0f;
                    pin.Knockback = 4f;
                }
                break;
            case ChargeMove.BigShot:
                var cork = PlayerShot.Fire(this, PlayerShot.Cork(2.8f), GlobalPosition + new Vector3(Facing.X * 0.1f, 1.05f, 0f), Facing, 17f);
                cork.Damage = 30f * _perks.ThrowDamage;
                cork.Range = 10f * _perks.ThrowRange;
                cork.Pierce = true;
                cork.Knockback = 9f;
                cork.Launch = 3f;
                cork.HitRadius = 0.7f;
                cork.Burst = _perks.ThrowExplodes;
                Visual.Squash(0.3f);
                break;
        }
    }

    /// <summary>A pop-gun shot: one cork (two with DOUBLE BARREL) straight down the lane.</summary>
    private void FireCork(in AttackStep step)
    {
        int n = 1 + _perks.ExtraShots;
        for (int i = 0; i < n; i++)
        {
            float z = n == 1 ? 0f : (i - (n - 1) * 0.5f) * 0.12f;
            // Fired from the body, not the muzzle, so a shot at point blank still lands.
            Vector3 from = GlobalPosition + new Vector3(Facing.X * 0.1f, 1.05f, 0f);
            var cork = PlayerShot.Fire(this, PlayerShot.Cork(1.3f), from, new Vector3(Facing.X, 0f, z), 18f);
            cork.Damage = step.Damage * (n > 1 ? 0.65f : 1f);
            cork.Range = step.Range * _perks.ThrowRange;
            cork.Knockback = step.Knockback;
            cork.Launch = step.Launch * _launchMultiplier;
            cork.Pierce = _perks.Pierce;
        }
        Sfx.Play("pop", 1.3f + 0.08f * _comboIndex);
        CameraShake.Request(this, 0.02f, 0.08f);
    }

    /// <summary>The balloon sword's charge: coil, then dash forward running everyone through.</summary>
    private void StartLunge()
    {
        Visual.Tint = BaseTint;
        Sfx.Stop(_chargeVoice);
        _chargeVoice = null;
        _lungeTime = 0f;
        _hitThisSwing.Clear();
        _state = State.Lunging;
    }

    private void TickLunge(float dt)
    {
        _lungeTime += dt;
        if (_lungeTime < LungeWind)
        {
            ApplyGroundMovement(Vector3.Zero, dt);
            PinClipFrame("charge", 0);
            return;
        }
        float t = _lungeTime - LungeWind;
        float dash = LungeDash * _perks.Lunge;
        PinClipFrame("attack", 3);
        if (t < dash)
        {
            if (t - dt < 0f) { Sfx.Play("whoosh", 1.3f); Dust(0.4f); }
            Velocity = new Vector3(Facing.X * LungeSpeed, Velocity.Y, 0f);
            foreach (Fighter other in Opponents())
            {
                if (_hitThisSwing.Contains(other)) continue;
                Vector3 to = other.GlobalPosition - GlobalPosition;
                float forward = to.X * Facing.X;
                if (forward < -0.4f || forward > 2.6f || Mathf.Abs(to.Z) > 0.9f || Mathf.Abs(to.Y) > 1.9f) continue;
                _hitThisSwing.Add(other);
                Strike(other, 20f * _perks.Lunge, Facing, 5f, 0.06f, 4f, 0.4f);
            }
            return;
        }
        ApplyGroundMovement(Vector3.Zero, dt);
        if (t > dash + 0.18f) EndMalletMove();
    }

    /// <summary>Big Bertha's charge: a ground pound that hits everyone around.</summary>
    private void StartQuake()
    {
        Visual.Tint = BaseTint;
        Sfx.Stop(_chargeVoice);
        _chargeVoice = null;
        _quakeTime = 0f;
        _quakeHits = 0;
        _state = State.Quaking;
        Sfx.Play("whoosh", 0.7f);
    }

    private void TickQuake(float dt)
    {
        _quakeTime += dt;
        ApplyGroundMovement(Vector3.Zero, dt);
        PinClipFrame("attack", _quakeTime < QuakeImpact * 0.6f ? 1 : _quakeTime < QuakeImpact ? 2 : 3);

        if (_weapon.Charge == ChargeMove.Clap) { TickClap(); return; }

        int due = _quakeTime >= QuakeImpact ? 1 : 0;
        if (_perks.Aftershock && _quakeTime >= AftershockAt) due = 2;
        while (_quakeHits < due)
        {
            bool first = _quakeHits == 0;
            _quakeHits++;
            float radius = QuakeRadius * _perks.QuakeRadius * (first ? 1f : 1.2f);
            float damage = QuakeDamage * _perks.QuakeDamage * (first ? 1f : 0.6f);
            Vector3 at = GlobalPosition + Facing * MalletForward + new Vector3(0f, MalletHeight, 0f);
            ImpactEffect.Spawn(GetParent(), at, first ? 2.2f : 1.6f);
            Dust(first ? 1.2f : 0.9f);
            Sfx.Play("kaboom", first ? 0.7f : 0.55f, -3f);
            CameraShake.Request(this, first ? 0.2f : 0.13f, 0.35f);
            bool crit = RollCrit();
            DamageInRadius(radius, Scaled(damage, crit), 6f * _perks.Knockback, 0.1f, first ? 5.5f * _perks.Launch : 3.5f, 0.6f);
            if (crit) Sfx.Play("crit");
        }

        if (_quakeTime >= (_perks.Aftershock ? AftershockAt + 0.2f : 0.55f)) EndMalletMove();
    }

    /// <summary>The slapstick's charge: one enormous clap that stuns everything in front.</summary>
    private void TickClap()
    {
        if (_quakeHits == 0 && _quakeTime >= QuakeImpact)
        {
            _quakeHits = 1;
            float reach = 3.4f * _perks.QuakeRadius;
            ImpactEffect.Spawn(GetParent(), GlobalPosition + Facing * 1.3f + new Vector3(0f, 0.9f, 0f), 1.6f);
            PopText.Spawn(GetParent(), GlobalPosition + Facing * 1.6f + new Vector3(0f, 1.9f, 0f), "CLAP!", new Color(1f, 0.85f, 0.3f), 1f, 0.6f);
            Sfx.Play("slap", 0.55f, 2f);
            Sfx.Play("whip_crack", 0.8f, -4f);
            CameraShake.Request(this, 0.14f, 0.3f);
            foreach (Fighter other in Opponents())
            {
                Vector3 to = other.GlobalPosition - GlobalPosition;
                float forward = to.X * Facing.X;
                if (forward <= -0.2f || forward > reach || Mathf.Abs(to.Z) > 1.4f || Mathf.Abs(to.Y) > 2f) continue;
                Strike(other, 18f * _perks.QuakeDamage, Facing, 7f * _perks.Knockback, 0.08f, 2f, 1.0f);
            }
        }
        if (_quakeTime >= 0.45f) EndMalletMove();
    }

    /// <summary>The boomerang came home.</summary>
    public void CatchMallet()
    {
        if (Armed) return;
        Armed = true;
        Visual.ClipPrefix = "";
        Visual.Squash(0.25f);
        _chargeLock = true;
        Sfx.Play("clack");
    }

    private void StartFlip()
    {
        _bufferedAttack = false;
        _flipTime = 0f;
        _hitThisSwing.Clear();
        _state = State.Flipping;
        Sfx.Play("whoosh", 1.4f);
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
            Sfx.Play("thump");
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
        bool crit = RollCrit();
        stun *= _weapon.StunScale * _perks.StunTime;
        other.TakeDamage(new DamageInfo(Scaled(damage, crit), dir, knockback, crit ? hitstop * 1.6f : hitstop, this, launch, stun));
        ApplyHitstop(crit ? hitstop * 1.6f : hitstop);
        Spark(other, crit);
        Encore(other);
    }

    /// <summary>ENCORE: a knockout heals.</summary>
    private void Encore(Fighter other)
    {
        if (_perks.HealOnKill > 0f && other.IsDead && !IsDead) Heal(MaxHealth * _perks.HealOnKill);
    }

    /// <summary>A hit delivered by this fighter's thrown weapon rather than its body.</summary>
    public void StrikeFromProjectile(Fighter other, float damage, Vector3 dir, float knockback, float launch)
    {
        bool crit = RollCrit();
        other.TakeDamage(new DamageInfo(Scaled(damage, crit), dir, knockback, 0.06f, this, launch, 0.35f));
        Spark(other, crit);
        Encore(other);
    }

    private bool RollCrit() => CritChance > 0f && _critRng.Randf() < CritChance;

    private float Scaled(float damage, bool crit) =>
        damage * DamageMultiplier * ExtraDamageMultiplier * (crit ? _critMultiplier : 1f)
        * (_perks.LastStand && Health < MaxHealth * 0.3f ? 1.3f : 1f);

    private void Spark(Fighter other, bool crit = false)
    {
        Vector3 at = other.GlobalPosition + new Vector3(0f, other.BodyHeight * 0.55f, 0f);
        ImpactEffect.Spawn(GetParent(), at, crit ? 0.7f : 0.34f);
        if (!crit) return;
        PopText.Spawn(GetParent(), at + new Vector3(0f, 0.5f, 0f), "CRIT!", new Color(1f, 0.45f, 0.3f), 0.8f, 0.6f);
        Sfx.Play("crit");
    }

    // -------------------------------------------------------------- hooks

    protected override void OnInterrupted()
    {
        // Being hit drops whatever was in progress. A thrown mallet stays out.
        Sfx.Stop(_chargeVoice);
        _chargeVoice = null;
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

    // UNSTOPPABLE: a hit lands but does not stop the swing.
    protected override bool HasPoise => _perks.Unstoppable && _state is State.Attacking or State.Quaking or State.Lunging;

    /// <summary>THICK SKIN softens hits; SECOND WIND catches a player about to go down.</summary>
    public override void TakeDamage(in DamageInfo info)
    {
        if (_perks.DamageTaken >= 1f && !_perks.SecondWind) { base.TakeDamage(info); return; }
        base.TakeDamage(new DamageInfo(info.Amount * _perks.DamageTaken, info.KnockbackDirection, info.KnockbackForce,
                                       info.Hitstop, info.Source, info.Launch, info.Stun));
        if (_perks.SecondWind && !_secondWindUsed && !IsDead && Health < MaxHealth * 0.25f)
        {
            _secondWindUsed = true;
            Heal(MaxHealth * 0.3f);
            PopText.Spawn(GetParent(), GlobalPosition + new Vector3(0f, BodyHeight + 0.4f, 0f), "SECOND WIND!", new Color(0.5f, 1f, 0.6f), 0.8f, 0.8f);
            Sfx.Play("level_up", 1.2f, -6f);
        }
    }

    /// <summary>Invulnerable through the middle of the roll, not its edges.</summary>
    public override bool CanBeHit()
    {
        if (_state == State.Dodging && _dodgeTime >= IFrameStart && _dodgeTime <= IFrameEnd * _perks.DodgeFrames)
            return false;
        return base.CanBeHit();
    }

    /// <summary>
    /// Stick space to world space. The camera looks down -Z at 35 degrees, so
    /// stick-up must travel away from the camera.
    /// </summary>
    private static Vector3 ToWorld(Vector2 move) => new(move.X, 0f, move.Y);
}
