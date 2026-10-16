namespace ClownSquad.Actors;

using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Progression;
using ClownSquad.Visuals;
using ClownSquad.Net;

/// <summary>
/// A fighter driven by an <see cref="IInputSource"/>. Knows nothing about where
/// the input came from, so the same class serves single-player, local co-op,
/// AI enemies and (later) remote peers. See ADR 0003.
/// </summary>
public partial class PlayerFighter : Fighter
{
    private enum State { Free, Attacking, Dodging, Special, Charging, Spinning, Throwing, Flipping, Quaking, Lunging, AirDashing, Slamming, Thrusting, PinToss }

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
    private const int StrikerRungs = 3;         // clowns (plus a crit) caught in one pound to ring the bell
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
    private bool _trailDone;      // the swing's smear has been drawn this step

    // Air moves (players only): one extra jump and one dash per time in the air,
    // and a slam (down + attack in the air) that dives and shocks the ground.
    private const float DoubleJumpSpeed = 7.8f;
    private const float AirDashSpeed = 12.5f;
    private const float AirDashTime = 0.2f;
    private const float SlamDiveSpeed = 24f;
    private const float SlamRadius = 2.4f;
    private const float SlamDamage = 22f;
    private const float SlamRecover = 0.22f;
    private bool _usedAirJump;
    private bool _usedAirDash;
    private float _airTime;
    private float _somersault;          // time left on the double jump's tumble, for the pose
    private Vector3 _airDashDir;
    private bool _slamLanded;
    private float _landTime;            // a squash pose just after touching down
    private bool _celebrating;          // the stage is won: cheer when standing still

    /// <summary>The stage is won: stand and cheer (the victory pose) whenever not moving.</summary>
    public void Celebrate() => _celebrating = true;

    /// <summary>A pose if this sheet has it; sheets drawn before it (the classic art, enemies) fall back.</summary>
    private bool Has(string clip) => Visual is not null && Visual.HasClip(clip);

    /// <summary>Double jump, air dash and slam belong to the squad; enemies keep their simpler air game.</summary>
    protected virtual bool HasAirMoves => !Hostile && HasMalletMoves;

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
    // Hold attack with a direction for 0.5 real seconds (in game time, which runs at GameClock.Speed)
    // and the forward charge move starts: the spin, or the balloon sword's thrust. Same for every weapon.
    private const float SpinMinCharge = 0.5f * (float)ClownSquad.World.GameClock.Speed;
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
        State.AirDashing => "AIRDASH",
        State.Thrusting => "THRUST",
        State.PinToss => "PINTOSS",
        State.Slamming => "SLAM",
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

    /// <summary>
    /// Every combo's third hit is the power hit: harder, launching, with the
    /// big impact (see <see cref="PowerHitFx"/>). Swings a skill adds come
    /// after it, as follow-ups that keep the launched enemy in the air.
    /// </summary>
    private const int PowerHit = 2;

    /// <summary>The weapon's combo with the skill tree applied.</summary>
    private static AttackStep[] BuildCombo(WeaponDef w, WeaponPerks k)
    {
        var steps = new List<AttackStep>(w.Combo);
        int power = System.Math.Min(PowerHit, steps.Count - 1);
        // Follow-ups: as fast as an opener, a little harder, with a lift to juggle.
        for (int i = 0; i < k.ExtraSteps; i++)
            steps.Add(w.Extra with { Damage = w.Extra.Damage * 1.2f, Launch = 3.4f, Knockback = w.Extra.Knockback * 0.6f });
        for (int i = 0; i < steps.Count; i++)
        {
            AttackStep s = steps[i];
            bool big = i == power;
            steps[i] = s with
            {
                Damage = s.Damage * k.Damage * (big ? k.Finisher : 1f),
                Knockback = s.Knockback * k.Knockback,
                Launch = s.Launch * k.Launch,
                Hitstop = big ? s.Hitstop * 1.35f : s.Hitstop,
            };
        }
        return steps.ToArray();
    }

    private bool IsPowerHit => _comboIndex == System.Math.Min(PowerHit, Combo.Length - 1);

    /// <summary>
    /// The swing for this hit of the combo. Melee weapons: a sideways swipe,
    /// a backhand swipe back the other way, then the first swipe again, hard
    /// (the power hit, "attack3"); follow-ups alternate the first two. Sheets
    /// without the per-hit swings use "attack" for all.
    /// </summary>
    private string SwingClip
    {
        get
        {
            string clip = IsPowerHit ? "attack3" : _comboIndex % 2 == 0 ? "attack1" : "attack2";
            return Has(clip) ? clip : "attack";
        }
    }
    private bool _powerFxDone;

    /// <summary>The third hit lands: it should feel like the big one.</summary>
    private void PowerHitFx(Fighter target)
    {
        if (_powerFxDone || Hostile) return;
        _powerFxDone = true;
        Vector3 at = target.GlobalPosition + new Vector3(0f, target.BodyHeight * 0.6f, 0f);
        Fx.Impact(GetParent(), at, 1.5f);
        Fx.Punch(this, 0.55f);
        Fx.Shake(this, 0.11f, 0.22f);
        Fx.Sound("kaboom", 1.7f, -9f);
        string word = Weapon switch
        {
            WeaponKind.Hammer => "WHAM!", WeaponKind.Chicken => "BAWK!", WeaponKind.Pie => "SPLAT!",
            WeaponKind.Pins => "CLONK!", WeaponKind.Slapstick => "SLAP!", WeaponKind.PopGun => "POP!",
            WeaponKind.BalloonSword => "SQUEAK!", _ => "BONK!",
        };
        Fx.Pop(GetParent(), at + new Vector3(0f, 0.6f, 0f), word, new Color(1f, 0.9f, 0.35f), 1.1f, 0.8f);
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

        TickJuggle(dt);
        // Touching down gives the air moves back, after a real jump with a squash.
        if (Grounded)
        {
            if (_airTime > 0.25f) _landTime = 0.1f;
            _usedAirJump = false; _usedAirDash = false; _airTime = 0f;
        }
        if (_landTime > 0f) _landTime -= dt;
        else _airTime += dt;
        if (_somersault > 0f) _somersault -= dt;

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
            case State.AirDashing: TickAirDash(dt); break;
            case State.Thrusting: TickThrust(dt); break;
            case State.PinToss: TickPinToss(dt); break;
            case State.Slamming: TickSlam(dt); break;
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
            if (HasAirMoves)
            {
                // Down + attack in the air: the slam.
                if (_bufferedAttack && cmd.Move.Y > 0.5f && Armed) { StartSlam(); return; }
                // Jump again: the double jump (a moment after leaving the ground, so one press is not both).
                if (cmd.JumpPressed && !_usedAirJump && _airTime > 0.08f) { DoubleJump(); return; }
                // Dodge in the air: the air dash.
                if (cmd.DodgePressed && !_usedAirDash) { StartAirDash(move); return; }
            }
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
                if (Armed && _weapon.Juggles && HasMalletMoves && _pinStock > 0)
                {
                    ThrowPin();
                    return;
                }
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
                _chargeVoice = Fx.Sound("kazoo_rise");
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
                // The double jump tumbles once; otherwise tuck while rising, reach while falling.
                if (_somersault > 0f) PinClipFrame("flip", Mathf.Clamp((int)((1f - _somersault / 0.36f) * 8f), 0, 7));
                else PinClipFrame("jump", Velocity.Y > 0f ? 0 : 1);
            }
            else
            {
                float speed = new Vector3(Velocity.X, 0f, Velocity.Z).Length();
                if (_landTime > 0f && Has("land")) PinClipFrame("land", 0);
                else if (speed <= 0.6f && _celebrating && Has("victory")) PlayClip("victory");
                else PlayClip(speed > 0.6f ? "walk" : "idle");
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
        _trailDone = false;
        _fired = false;
        _powerFxDone = false;
        _kaboomed = false;
        _sinceCombo = 0f;
        AssistLane();
        Fx.Sound("whoosh", 1f + 0.12f * _comboIndex);
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
                Fx.Impact(GetParent(), at, (0.7f + _comboIndex * 0.25f) * heft);
                Fx.Sound("thump", Weapon == WeaponKind.Hammer ? 0.8f : 1.1f, -4f);
                Fx.Shake(this, (0.035f + _comboIndex * 0.03f) * heft, 0.16f + _comboIndex * 0.04f);
            }
        }

        // Pin the swing frame to the phase, so the visible swing is the hitbox.
        // Each hit of the combo has its own swing: a swipe, an uppercut, then
        // the overhead power hit (follow-ups alternate the first two).
        if (!InHitAnim)
        {
            int frame = _phaseTime < step.Startup * 0.5f ? 0
                      : _phaseTime < activeStart ? 1
                      : _phaseTime < activeStart + step.Active * 0.5f ? 2
                      : 3;
            PinClipFrame(SwingClip, frame);
        }

        // The smear: as the hit window opens, a crescent behind the weapon (squad only, melee only).
        if (!_trailDone && _phaseTime >= activeStart && !Hostile && HasMalletMoves && !_weapon.Ranged)
        {
            _trailDone = true;
            string trail = SwingClip switch { "attack1" or "attack3" => "sweep", "attack2" => "backsweep", _ => "arc" };
            float size = 1.15f * step.Range / 1.9f * (IsPowerHit ? 1.3f : 1f);
            SwingTrail.Spawn(this, trail, size, trail == "arc" ? 0.62f : 0.72f);
        }

        if (_weapon.Ranged && HasMalletMoves)
        {
            if (!_fired && _phaseTime >= activeStart)
            {
                _fired = true;
                if (Weapon == WeaponKind.Pie) ThrowPie(step); else FireCork(step);
            }
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
            if (IsPowerHit) PowerHitFx(other);
            // A pie in the face: the power hit leaves them reeling.
            if (_weapon.FinisherStagger > 0f && IsPowerHit && !other.IsDead)
                other.Stagger(_weapon.FinisherStagger * _perks.Blind);
        }

        // KABOOM gear, or POP GOES THE... on a balloon sword: the finisher goes
        // off like a firework, once per swing.
        float boom = _kaboom + (_perks.PopFinisher ? 14f : 0f);
        if (boom > 0f && IsPowerHit && _hitThisSwing.Count > 0 && !_kaboomed)
        {
            _kaboomed = true;
            Vector3 at = GlobalPosition + Facing * (step.Range * ReachScale * 0.7f);
            Fx.Impact(GetParent(), at + new Vector3(0f, 0.3f, 0f), 1.8f);
            Fx.Sound("kaboom", 1.4f, -6f);
            Fx.Shake(this, 0.12f, 0.25f);
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
        Fx.Sound("zip");
        if (_perks.SquawkDodge)
        {
            // SQUAWK: the slide goes off like a startled hen; anyone close is frozen to the spot.
            Fx.Sound("squawk", 1.1f);
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
            if (_heldTime >= SpinMinCharge)
            {
                if (_weapon.Thrusts) StartThrust(); else StartSpin();
                return;
            }
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
        Fx.Sound("whirr", 1.15f);
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
                Fx.Sound(_weapon.ThrowSound);
                Fx.Shake(this, 0.04f, 0.12f);
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
        Fx.Shake(this, 0.04f, 0.12f);
        Fx.Sound(_weapon.ThrowSound, _weapon.Charge == ChargeMove.BigShot ? 0.6f : 1f);
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
                    float slick = 1.6f * _perks.SplatRadius;
                    pie.LeavesBehind = at => CreamPuddle.Spawn(GetParent(), this, at, slick, 6.5f);
                }
                break;
            case ChargeMove.Fan:
                // Everything in the juggle goes at once (at least one, pulled from a pocket).
                int n = Mathf.Max(1, _pinStock);
                if (_pinStock >= MaxPins) _pinRegen = 0f;
                _pinStock = 0;
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

    /// <summary>A pie flung in an arc at face height; it splats on the first face it meets. The power hit's pie is bigger.</summary>
    private void ThrowPie(in AttackStep step)
    {
        bool big = IsPowerHit;
        Vector3 from = GlobalPosition + new Vector3(Facing.X * 0.3f, 1.2f, 0f);
        var pie = PlayerShot.Fire(this, PlayerShot.Pie(big ? 1.6f : 1.05f), from, Facing, 13f);
        pie.Velocity += new Vector3(0f, 2.4f, 0f);
        pie.Gravity = 6f;
        pie.Damage = step.Damage;
        pie.Range = step.Range * _perks.ThrowRange;
        pie.Knockback = step.Knockback;
        pie.Launch = step.Launch * _launchMultiplier;
        pie.Splat = big ? 1.3f * _perks.SplatRadius : 0.6f;
        pie.Blind = (big ? 1.0f : 0.45f) * _perks.Blind;
        pie.SpinSpeed = 3f;
        Fx.Sound("fwee", big ? 0.9f : 1.2f);
        if (big) Fx.Pop(GetParent(), GlobalPosition + new Vector3(Facing.X * 0.8f, BodyHeight + 0.4f, 0f), "PIE TIME!", new Color(1f, 0.95f, 0.8f), 0.9f, 0.6f);
    }

    // ------------------------------------------------------- the pin juggle

    // One more pin joins the juggle every 2 real seconds (in game time, which runs at
    // GameClock.Speed), on a steady beat that throwing does not interrupt.
    private const float PinRegenEach = 1.5f * (float)ClownSquad.World.GameClock.Speed;
    private int _pinStock = 5;
    private float _pinRegen;
    private int _pinStreak;
    private double _lastPinAt = -9;
    private Node3D? _juggle;
    private readonly List<Node3D> _jugglePins = new();
    private float _juggleClock;

    private int MaxPins => 5 + _perks.ExtraShots;

    /// <summary>Pins in the juggle right now (the pins weapon), for the HUD and tests.</summary>
    public int PinStock => _weapon.Juggles ? _pinStock : 0;

    /// <summary>One pin from the juggle, straight down the lane; every third in a quick run is a power pin.</summary>
    private void ThrowPin()
    {
        if (_pinStock >= MaxPins) _pinRegen = 0f;     // the beat starts with the first pin out of a full juggle
        _pinStock--;
        _pinStreak = ClownSquad.World.GameClock.Seconds - _lastPinAt < 0.7 ? _pinStreak + 1 : 1;
        _lastPinAt = ClownSquad.World.GameClock.Seconds;
        bool power = _pinStreak % 3 == 0;
        var pin = PlayerShot.Fire(this, EnemyShot.Pin(power ? 1.9f : 1.3f), GlobalPosition + new Vector3(Facing.X * 0.4f, 1.3f, 0f), Facing, 17f);
        pin.Damage = (power ? 18f : 8f) * _perks.Damage * _perks.ThrowDamage;
        pin.Range = 9f * _perks.ThrowRange;
        pin.Pierce = _perks.Pierce;
        pin.Knockback = power ? 7f : 3f;
        pin.Launch = power || _perks.ThrowLaunches ? 5f : 0f;
        Fx.Sound("whoosh", power ? 1.0f : 1.5f, -4f);
        if (power) Fx.Pop(GetParent(), GlobalPosition + new Vector3(Facing.X * 0.8f, BodyHeight + 0.4f, 0f), "CLONK!", new Color(1f, 0.9f, 0.35f), 1f, 0.6f);
        _flipTime = 0f;
        _state = State.PinToss;
    }

    private void TickPinToss(float dt)
    {
        _flipTime += dt;
        ApplyGroundMovement(ToWorld(_cmd.Move) * MoveSpeed * 0.5f, dt);
        PinClipFrame("attack", _flipTime < 0.05f ? 2 : 3);
        // Short enough to throw as fast as you can tap; a press during it is kept.
        if (_flipTime >= 0.1f) _state = State.Free;
    }

    /// <summary>The juggle: the stock refills while you juggle, and you can see it, pins wheeling over the head.</summary>
    private void TickJuggle(float dt)
    {
        if (!_weapon.Juggles || Hostile || !HasMalletMoves)
        {
            if (_juggle is not null) _juggle.Visible = false;
            return;
        }
        if (_pinStock < MaxPins)
        {
            _pinRegen += dt;
            if (_pinRegen >= PinRegenEach) { _pinStock++; _pinRegen = 0f; Fx.Sound("clack", 1.6f, -14f); }
        }
        if (_juggle is null)
        {
            _juggle = new Node3D { Name = "Juggle" };
            AddChild(_juggle);
            for (int i = 0; i < 7; i++)
            {
                Node3D pin = EnemyShot.Pin(0.85f);
                _juggle.AddChild(pin);
                _jugglePins.Add(pin);
            }
        }
        _juggle.Visible = !IsDead;
        _juggleClock += dt;
        int n = _pinStock;
        for (int i = 0; i < _jugglePins.Count; i++)
        {
            Node3D pin = _jugglePins[i];
            pin.Visible = i < n;
            if (i >= n) continue;
            // A cascade: each pin on the same figure-of-eight loop, spaced evenly in time.
            float ph = _juggleClock * 3.4f + Mathf.Tau * i / Mathf.Max(1, n);
            pin.Position = new Vector3(Mathf.Sin(ph) * 0.42f + Facing.X * 0.15f, BodyHeight + 0.55f + Mathf.Abs(Mathf.Cos(ph)) * 0.55f, 0.12f);
            pin.Rotation = new Vector3(0f, 0f, ph * 2f);
        }
    }

    // ------------------------------------------------------ the wall thrust

    /// <summary>The balloon sword, held charge plus a direction: a straight thrust that shoves them into the wall.</summary>
    private void StartThrust()
    {
        Visual.Tint = BaseTint;
        Sfx.Stop(_chargeVoice);
        _chargeVoice = null;
        _flipTime = 0f;
        _hitThisSwing.Clear();
        _state = State.Thrusting;
        Fx.Sound("whoosh", 1.1f);
    }

    private void TickThrust(float dt)
    {
        _flipTime += dt;
        bool striking = _flipTime >= 0.12f && _flipTime < 0.28f;
        if (striking && _flipTime - dt < 0.12f) SwingTrail.Spawn(this, "thrust", 1.6f, 0.55f);
        ApplyGroundMovement(striking ? Facing * 7f : Vector3.Zero, dt);       // drives forward as it strikes
        PinClipFrame("attack", _flipTime < 0.12f ? 1 : 3);
        if (striking)
        {
            float power = _perks.SpinDamage;      // TWISTER
            foreach (Fighter other in Opponents())
            {
                if (_hitThisSwing.Contains(other)) continue;
                Vector3 to = other.GlobalPosition - GlobalPosition;
                float forward = to.X * Facing.X;
                if (forward <= 0f || forward > 3.3f || Mathf.Abs(to.Z) > 1.0f || Mathf.Abs(to.Y) > 1.9f) continue;
                _hitThisSwing.Add(other);
                // A small lift and a huge shove: they sail flat across the ring and bounce off the wall.
                Strike(other, 14f * power, Facing, 24f * power * _perks.Knockback, 0.1f, 2.2f, 0.7f);
                Fx.Pop(GetParent(), other.GlobalPosition + new Vector3(0f, other.BodyHeight + 0.3f, 0f), "BOING!", new Color(1f, 0.6f, 0.85f), 1f, 0.6f);
                Fx.Shake(this, 0.08f, 0.18f);
            }
        }
        if (_flipTime >= 0.5f) EndMalletMove();
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
        Fx.Sound("pop", 1.3f + 0.08f * _comboIndex);
        Fx.Shake(this, 0.02f, 0.08f);
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
            if (t - dt < 0f) { Fx.Sound("whoosh", 1.3f); Dust(0.4f); SwingTrail.Spawn(this, "thrust", 1.6f, 0.55f); }
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

    // ----------------------------------------------------------- air moves

    private void DoubleJump()
    {
        _usedAirJump = true;
        _bufferedAttack = false;
        Velocity = new Vector3(Velocity.X, DoubleJumpSpeed, Velocity.Z);
        _somersault = 0.36f;
        Dust(0.45f);
        Fx.Sound("boing_small", 1.5f);
        Fx.Sound("whoosh", 1.6f, -8f);
    }

    /// <summary>A short burst sideways that holds its height, briefly untouchable.</summary>
    private void StartAirDash(Vector3 move)
    {
        _usedAirDash = true;
        _bufferedAttack = false;
        _airDashDir = Mathf.Abs(move.X) > 0.1f ? new Vector3(Mathf.Sign(move.X), 0f, 0f) : Facing;
        Facing = _airDashDir;
        _dodgeTime = 0f;
        _state = State.AirDashing;
        Dust(0.35f);
        Fx.Sound("zip", 1.3f);
    }

    private void TickAirDash(float dt)
    {
        _dodgeTime += dt;
        float t = _dodgeTime / AirDashTime;
        // Fast out, easing off; gravity is held off (it is added after this tick).
        Velocity = new Vector3(_airDashDir.X * AirDashSpeed * (1f - 0.5f * t), Gravity * dt, Velocity.Z * 0.5f);
        if (Has("dash")) PinClipFrame("dash", 0); else PinClipFrame("dodge", 1);
        if (_dodgeTime >= AirDashTime || Grounded)
        {
            Velocity = new Vector3(Velocity.X * 0.4f, 0f, Velocity.Z);
            _state = State.Free;
        }
    }

    /// <summary>Down + attack in the air: a short hang, then straight down, and a shockwave on landing.</summary>
    private void StartSlam()
    {
        _bufferedAttack = false;
        _flipTime = 0f;
        _slamLanded = false;
        _hitThisSwing.Clear();
        _state = State.Slamming;
        Fx.Sound("whoosh", 0.8f);
    }

    private void TickSlam(float dt)
    {
        _flipTime += dt;
        if (!_slamLanded)
        {
            // A beat at the top so it reads, then the dive.
            if (_flipTime < 0.1f)
            {
                Velocity = new Vector3(0f, Gravity * dt + 1.5f, 0f);
                if (Has("slam")) PinClipFrame("slam", 0); else PinClipFrame("attack", 1);
                return;
            }
            Velocity = new Vector3(0f, -SlamDiveSpeed, 0f);
            if (Has("slam")) PinClipFrame("slam", 0); else PinClipFrame("attack", 3);
            // Anyone under the dive is struck on the way down.
            foreach (Fighter other in Opponents())
            {
                if (_hitThisSwing.Contains(other)) continue;
                Vector3 to = other.GlobalPosition - GlobalPosition;
                if (new Vector2(to.X, to.Z).Length() > 1.1f || to.Y > 0.5f || to.Y < -2.2f) continue;
                _hitThisSwing.Add(other);
                Strike(other, SlamDamage * 0.5f, Facing, 2f, 0.05f, 0f, 0.3f);
            }
            if (!Grounded) return;
            _slamLanded = true;
            _flipTime = 0f;
            Vector3 at = GlobalPosition + new Vector3(0f, 0.15f, 0f);
            Fx.Impact(GetParent(), at, 2.4f);
            Dust(1.3f);
            Fx.Sound("kaboom", 0.8f, -2f);
            Fx.Sound("thump", 0.7f);
            Fx.Shake(this, 0.18f, 0.32f);
            Visual.Squash(0.45f);
            bool crit = RollCrit();
            DamageInRadius(SlamRadius, Scaled(SlamDamage * _perks.Damage, crit), 5.5f * _perks.Knockback, 0.1f, 5.5f * _perks.Launch, 0.55f);
            if (crit) Fx.Sound("crit");
            return;
        }
        // A brief recovery on the ground: the price of the slam.
        ApplyGroundMovement(Vector3.Zero, dt);
        if (Has("slam")) PinClipFrame("slam", 1); else PinClipFrame("attack", 3);
        if (_flipTime >= SlamRecover) EndMalletMove();
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
        Fx.Sound("whoosh", 0.7f);
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
            Fx.Impact(GetParent(), at, first ? 2.2f : 1.6f);
            Dust(first ? 1.2f : 0.9f);
            Fx.Sound("kaboom", first ? 0.7f : 0.55f, -3f);
            Fx.Shake(this, first ? 0.2f : 0.13f, 0.35f);
            bool crit = RollCrit();
            int hits = DamageInRadius(radius, Scaled(damage, crit), 6f * _perks.Knockback, 0.1f, first ? 5.5f * _perks.Launch : 3.5f, 0.6f);
            if (crit) Fx.Sound("crit");
            if (first && _weapon.Kind == WeaponKind.Hammer) TestYourStrength(hits, crit, radius, damage);
        }

        if (_quakeTime >= (_perks.Aftershock ? AftershockAt + 0.2f : 0.55f)) EndMalletMove();
    }

    /// <summary>
    /// Big Bertha is the carnival strength tester's sledge: every ground pound
    /// springs up a High Striker beside you, and the puck flies as high as the
    /// pound was good, a rung for each clown caught in it and one for a crit.
    /// Three rungs ring the bell, and the bell rings out a second shockwave
    /// that leaves everyone near dizzy.
    /// </summary>
    private void TestYourStrength(int hits, bool crit, float radius, float damage)
    {
        float strength = (hits + (crit ? 1 : 0)) / (float)StrikerRungs;
        Fx.Striker(GetParent(), GlobalPosition - Facing * 0.8f + new Vector3(0f, 0f, -0.3f), strength);
        if (strength < 1f) return;
        GetTree().CreateTimer(HighStriker.RiseTime + 0.08f, false).Timeout += () =>
        {
            if (!IsInstanceValid(this) || IsDead) return;
            Fx.Sound("ding", 1f, 2f);
            Fx.Sound("ding", 1.5f, -2f);
            Fx.Impact(GetParent(), GlobalPosition + new Vector3(0f, 0.4f, 0f), 2.6f);
            Fx.Shake(this, 0.16f, 0.3f);
            DamageInRadius(radius * 1.35f, Scaled(damage * 0.6f, false), 4f * _perks.Knockback, 0.06f, 3f * _perks.Launch, 1.1f);
            if (!Hostile) ClownSquad.Services.Achievements.Award("ring_bell");
        };
    }

    /// <summary>The slapstick's charge: one enormous clap that stuns everything in front.</summary>
    private void TickClap()
    {
        if (_quakeHits == 0 && _quakeTime >= QuakeImpact)
        {
            _quakeHits = 1;
            float reach = 3.4f * _perks.QuakeRadius;
            Fx.Impact(GetParent(), GlobalPosition + Facing * 1.3f + new Vector3(0f, 0.9f, 0f), 1.6f);
            Fx.Pop(GetParent(), GlobalPosition + Facing * 1.6f + new Vector3(0f, 1.9f, 0f), "CLAP!", new Color(1f, 0.85f, 0.3f), 1f, 0.6f);
            Fx.Sound("slap", 0.55f, 2f);
            Fx.Sound("whip_crack", 0.8f, -4f);
            Fx.Shake(this, 0.14f, 0.3f);
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
        Fx.Sound("clack");
    }

    private void StartFlip()
    {
        _bufferedAttack = false;
        _flipTime = 0f;
        _hitThisSwing.Clear();
        _state = State.Flipping;
        Fx.Sound("whoosh", 1.4f);
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
            Fx.Impact(GetParent(), GlobalPosition + Facing * 0.7f + new Vector3(0f, 0.15f, 0f), 0.9f);
            Fx.Shake(this, 0.06f, 0.2f);
            Fx.Sound("thump");
            Visual.Squash(0.3f);
            EndMalletMove();
        }
    }

    // ------------------------------------------------------------- striking

    private IEnumerable<Fighter> Opponents()
    {
        foreach (Node node in Fighter.Live)
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
        !Hostile && GameSettings.God ? 99999f :
        damage * DamageMultiplier * ExtraDamageMultiplier * (crit ? _critMultiplier : 1f)
        * (_perks.LastStand && Health < MaxHealth * 0.3f ? 1.3f : 1f);

    private void Spark(Fighter other, bool crit = false)
    {
        Vector3 at = other.GlobalPosition + new Vector3(0f, other.BodyHeight * 0.55f, 0f);
        Fx.Impact(GetParent(), at, crit ? 0.7f : 0.34f);
        if (!crit) return;
        Fx.Pop(GetParent(), at + new Vector3(0f, 0.5f, 0f), "CRIT!", new Color(1f, 0.45f, 0.3f), 0.8f, 0.6f);
        Fx.Sound("crit");
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
    // A slam cannot be knocked out of mid-dive; UNSTOPPABLE also keeps swings going.
    protected override bool HasPoise => (_state == State.Slamming && !_slamLanded)
        || (_perks.Unstoppable && _state is State.Attacking or State.Quaking or State.Lunging);

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
            Fx.Pop(GetParent(), GlobalPosition + new Vector3(0f, BodyHeight + 0.4f, 0f), "SECOND WIND!", new Color(0.5f, 1f, 0.6f), 0.8f, 0.8f);
            Fx.Sound("level_up", 1.2f, -6f);
        }
    }

    /// <summary>Invulnerable through the middle of the roll, not its edges.</summary>
    public override bool CanBeHit()
    {
        if (!Hostile && GameSettings.God) return false;
        if (_state == State.AirDashing && _dodgeTime <= AirDashTime * 0.7f) return false;
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
