namespace ClownSquad.Actors;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Visuals;
using ClownSquad.World;
using ClownSquad.Net;

/// <summary>
/// A boss, or one half of a boss act. The HUD shows the primary's bar; acts
/// of two (the trapeze duo) report their combined health through it.
/// </summary>
public interface IBoss
{
    string BossTitle { get; }
    float BossFraction { get; }
    bool PrimaryBoss { get; }
}

/// <summary>
/// The base of the whole new cast (tools/gen_cast_hd.py draws them): a fighter
/// on the shared combat code with a sheet from assets/sprites/cast and a list
/// of special moves. The AI asks for a special on a cooldown; the fighter then
/// picks one of its moves that suits the distance to its target and is off
/// its own cooldown, weighted, and runs it. Moves are small state machines
/// written as a start action and a tick that says when it is done.
///
/// Helpers cover what nearly every move needs: the nearest player, facing,
/// hitting everyone in an area, floor telegraphs that are cleared if the
/// move is interrupted, and guarding (taking less from hits in front).
/// </summary>
public partial class CastFighter : PlayerFighter
{
    public const string Folder = "res://assets/sprites/cast/";
    /// <summary>The squad's HD pixel size: cast sheets are drawn at the same density.</summary>
    public const float Pixel = 0.01125f;

    /// <summary>Which sheet in assets/sprites/cast.</summary>
    protected virtual string Cast => "juggler";
    /// <summary>Drawn this much larger than the squad's pixel density. Bosses loom.</summary>
    protected virtual float DrawScale => 1f;

    protected override string SheetPath => Folder + (SheetSwaps.TryGetValue(Cast, out string? swap) ? swap : Cast) + ".png";

    /// <summary>Test art: "-- castart juggler=juggler_b" plays a cast member with another sheet (debug builds).</summary>
    public static readonly System.Collections.Generic.Dictionary<string, string> SheetSwaps = new();
    /// <summary>This fighter's sheet, for the HUD portrait.</summary>
    public string SheetFile => SheetPath;
    protected override float SpritePixelSize => Pixel * DrawScale;
    public override bool Hostile => true;
    protected override bool HasMalletMoves => false;

    /// <summary>XP as a multiple of a grunt's.</summary>
    public virtual float XpScale => 1f;
    /// <summary>Whether this kind may spawn Rare, Epic or Legendary.</summary>
    public virtual bool RollsRank => true;

    // ------------------------------------------------------------- moves

    protected sealed class Move
    {
        public string Name = "";
        public float MinRange;
        public float MaxRange = 99f;
        public float Weight = 1f;
        public float Cooldown = 3f;
        public Action Start = () => { };
        /// <summary>Advance by dt; true when the move is over.</summary>
        public Func<float, bool> Tick = _ => true;
        public Func<bool>? Allowed;
        public double ReadyAt;
    }

    protected readonly List<Move> Moves = new();
    private Move? _move;
    private readonly List<Telegraph> _telegraphs = new();
    private readonly RandomNumberGenerator _pick = new();
    private double _clock;

    /// <summary>Seconds into the current move.</summary>
    protected float MoveTime { get; private set; }
    protected string CurrentMove => _move?.Name ?? "";
    /// <summary>Start the current move's clock again (a move that repeats itself).</summary>
    protected void ResetMoveTime() => MoveTime = 0f;
    /// <summary>Seconds since this fighter entered the fight.</summary>
    protected double Clock => _clock;

    protected override bool HasSpecial => Moves.Count > 0;

    protected Move AddMove(string name, float minRange, float maxRange, float cooldown, Action start, Func<float, bool> tick, float weight = 1f)
    {
        var m = new Move { Name = name, MinRange = minRange, MaxRange = maxRange, Cooldown = cooldown, Start = start, Tick = tick, Weight = weight };
        Moves.Add(m);
        return m;
    }

    public override void _Ready()
    {
        base._Ready();
        _pick.Randomize();
    }

    protected override void TickAlways(float dt)
    {
        base.TickAlways(dt);
        _clock += dt;
    }

    private string? _ordered;
    private bool _press;

    /// <summary>
    /// Run a named move at the next chance, whatever its range or cooldown:
    /// how a trainer commands her tiger, or a boss scripts its next act.
    /// </summary>
    public void Order(string move)
    {
        _ordered = move;
        _press = true;
    }

    /// <summary>For the input wrapper: whether an order is waiting to be pressed.</summary>
    public bool TakeOrderPress()
    {
        bool p = _press;
        _press = false;
        return p;
    }

    protected override void OnSpecialStart()
    {
        if (_ordered is string name)
        {
            _ordered = null;
            Move? m = Moves.Find(x => x.Name == name);
            if (m is not null)
            {
                _move = m;
                MoveTime = 0f;
                m.ReadyAt = _clock + m.Cooldown;
                if (Target() is Fighter tt) FaceToward(tt.GlobalPosition);
                m.Start();
                return;
            }
        }
        Fighter? t = Target();
        float dist = t is null ? 0f : Flat(t.GlobalPosition - GlobalPosition).Length();
        float total = 0f;
        var ok = new List<Move>();
        foreach (Move m in Moves)
        {
            if (_clock < m.ReadyAt || dist < m.MinRange || dist > m.MaxRange) continue;
            if (m.Allowed is not null && !m.Allowed()) continue;
            ok.Add(m);
            total += m.Weight;
        }
        _move = null;
        MoveTime = 0f;
        if (ok.Count == 0) return;
        float roll = _pick.Randf() * total;
        foreach (Move m in ok)
        {
            roll -= m.Weight;
            if (roll > 0f) continue;
            _move = m;
            break;
        }
        _move ??= ok[^1];
        _move.ReadyAt = _clock + _move.Cooldown;
        if (t is not null) FaceToward(t.GlobalPosition);
        _move.Start();
    }

    /// <summary>Start a particular move now, whatever the AI asked for. For bosses with a script.</summary>
    protected void ForceMove(string name)
    {
        foreach (Move m in Moves)
            if (m.Name == name) { m.ReadyAt = 0; }
    }

    protected override bool TickSpecialMove(float dt)
    {
        if (_move is null) return true;
        MoveTime += dt;
        bool done = _move.Tick(dt);
        if (done) EndMove();
        return done;
    }

    private void EndMove()
    {
        ClearTelegraphs();
        _move = null;
        OnMoveEnded();
    }

    /// <summary>A move finished or was cut short. Release anything it was holding.</summary>
    protected virtual void OnMoveEnded() { }

    protected override void OnInterrupted()
    {
        base.OnInterrupted();
        if (_move is not null) EndMove();
    }

    public override void _ExitTree()
    {
        ClearTelegraphs();
        base._ExitTree();
    }

    // ----------------------------------------------------------- helpers

    protected static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    protected IEnumerable<Fighter> Players()
    {
        foreach (Node n in Fighter.Live)
            if (n is Fighter f && !f.Hostile && !f.IsDead) yield return f;
    }

    /// <summary>The nearest living player, or null.</summary>
    protected Fighter? Target()
    {
        Fighter? best = null;
        float bd = float.MaxValue;
        foreach (Fighter f in Players())
        {
            float d = f.GlobalPosition.DistanceSquaredTo(GlobalPosition);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    protected void FaceToward(Vector3 p)
    {
        float dx = p.X - GlobalPosition.X;
        if (Mathf.Abs(dx) > 0.05f) Facing = new Vector3(Mathf.Sign(dx), 0f, 0f);
    }

    protected Node Stage => GetParent();

    /// <summary>Hurt every player the test accepts. Knocked away from this fighter unless a direction is given.</summary>
    protected int HitPlayers(Func<Fighter, bool> where, float damage, float knockback, float launch = 0f, float stun = 0.3f,
                             float hitstop = 0.08f, Vector3? dir = null, HashSet<Fighter>? once = null)
    {
        int hits = 0;
        foreach (Fighter f in new List<Fighter>(Players()))
        {
            if (!f.CanBeHit() || !where(f)) continue;
            if (once is not null && !once.Add(f)) continue;
            Vector3 d = dir ?? Flat(f.GlobalPosition - GlobalPosition);
            if (d.LengthSquared() < 0.01f) d = Facing;
            f.TakeDamage(new DamageInfo(damage * DamageMultiplier, d.Normalized(), knockback, hitstop, this, launch, stun));
            Fx.Impact(Stage, f.GlobalPosition + new Vector3(0f, f.BodyHeight * 0.55f, 0f), 0.45f);
            hits++;
        }
        if (hits > 0) ApplyHitstop(hitstop);
        return hits;
    }

    protected bool Near(Fighter f, Vector3 at, float radius) => Flat(f.GlobalPosition - at).Length() <= radius;

    protected Telegraph Track(Telegraph t)
    {
        _telegraphs.Add(t);
        return t;
    }

    protected void ClearTelegraphs()
    {
        foreach (Telegraph t in _telegraphs) if (IsInstanceValid(t)) t.Clear();
        _telegraphs.Clear();
    }

    /// <summary>Walk toward a point at a speed (used inside moves, where normal movement is off).</summary>
    protected void Steer(Vector3 toward, float speed, float dt)
    {
        Vector3 d = Flat(toward - GlobalPosition);
        ApplyGroundMovement(d.Length() > 0.1f ? d.Normalized() * speed : Vector3.Zero, dt);
    }

    protected void Stop(float dt) => ApplyGroundMovement(Vector3.Zero, dt);

    // A ballistic leap that lands exactly on a spot, whatever the distance.
    private Vector3 _leapTo;
    private float _leapLeft;
    private bool _leaping;

    /// <summary>Jump so as to land on `to`, rising `apex` units. Returns the time in the air.</summary>
    protected float StartLeap(Vector3 to, float apex)
    {
        float v0 = Mathf.Sqrt(2f * Gravity * apex);
        _leapTo = InView(to);
        _leapLeft = 2f * v0 / Gravity;
        _leaping = true;
        Jump(v0);
        return _leapLeft;
    }

    /// <summary>Steer the leap onto its spot. True on the frame it lands.</summary>
    protected bool TickLeap(float dt)
    {
        if (!_leaping) return true;
        _leapLeft -= dt;
        Vector3 d = Flat(_leapTo - GlobalPosition);
        float t = Mathf.Max(_leapLeft, 0.05f);
        Velocity = new Vector3(d.X / t, Velocity.Y, d.Z / t);
        if (Grounded && _leapLeft < 0.3f)
        {
            _leaping = false;
            Velocity = new Vector3(0f, 0f, 0f);
            return true;
        }
        return false;
    }

    protected Vector3 LeapTarget => _leapTo;

    /// <summary>Bring another enemy into the fight (a balloon dog, the clown car's passengers).</summary>
    protected void Summon(PlayerFighter f, Vector3 at) => WaveDirector.Running?.Summon(f, InView(at));

    /// <summary>Where on the floor the camera can still see, clamped: for charges and leaps.</summary>
    protected Vector3 InView(Vector3 p, float margin = 0.6f)
    {
        if (Arena.Current is not Arena a) return p;
        return a.Clamp(p, BodyHeight, BodyHalfWidth + margin, out _);
    }

    // ---------------------------------------------------------- guarding

    /// <summary>While true, hits from the front do a fraction of their damage and do not stun.</summary>
    protected bool Guarding { get; set; }
    /// <summary>For anything deciding where to hit this fighter from.</summary>
    public bool IsGuarding => Guarding;
    protected virtual float GuardFactor => 0.3f;

    public override void TakeDamage(in DamageInfo info)
    {
        bool front = info.Source is Node3D src && Mathf.Sign(src.GlobalPosition.X - GlobalPosition.X) == Mathf.Sign(Facing.X);
        if (Guarding && front && CanBeHit())
        {
            var reduced = new DamageInfo(info.Amount * GuardFactor, info.KnockbackDirection, info.KnockbackForce * 0.3f, info.Hitstop, info.Source, 0f, 0f);
            Fx.Sound("clack", 0.7f);
            Fx.Pop(Stage, GlobalPosition + new Vector3(0f, BodyHeight + 0.2f, 0f), "BLOCK!", new Color(0.7f, 0.85f, 1f), 0.6f, 0.5f);
            base.TakeDamage(reduced);
            return;
        }
        base.TakeDamage(info);
    }

    // ------------------------------------------------------------- AI

    /// <summary>The AI that drives this kind. Overridden per kind: brawlers close in, throwers keep away.</summary>
    public virtual BrawlerAi MakeAi(int gruntCooldownMs) =>
        new(this, attackRange: 1.4f * ReachScale, backoffRange: 0.7f, attackCooldownMs: (ulong)gruntCooldownMs,
            specialCooldownMs: 3500, specialRange: 7f);
}

/// <summary>The cast's input: its AI, plus a special press whenever the fighter has been given an order.</summary>
public sealed class CastInput : IInputSource
{
    private readonly CastFighter _self;
    private readonly IInputSource _ai;

    public CastInput(CastFighter self, IInputSource ai)
    {
        _self = self;
        _ai = ai;
    }

    public string Describe() => _ai.Describe();

    public InputCommand Poll()
    {
        InputCommand c = _ai.Poll();
        if (!_self.TakeOrderPress()) return c;
        return new InputCommand(c.Move, false, false, false, true);
    }
}

/// <summary>
/// The base of every boss: a name and title for the HUD, poise during its
/// moves, no launching, a slower stagger, and phases as its health falls
/// (each phase can unlock moves or quicken them). Bosses come in with a
/// comic-book title card.
/// </summary>
public partial class BossFighter : CastFighter, IBoss
{
    public virtual string BossTitle => DisplayName;
    public virtual float BossFraction => Health / MaxHealth;
    public virtual bool PrimaryBoss => true;
    public override float XpScale => 10f;
    public override bool RollsRank => false;

    /// <summary>
    /// Bosses are drawn this much bigger than the squad's pixel density, on top
    /// of being drawn big in their sheets: every boss stands well over the
    /// players. Their sizes and offsets in the code scale with it (Big).
    /// </summary>
    protected override float DrawScale => 1.35f;
    protected float Big => DrawScale;

    /// <summary>Where the sprite sits relative to the body (a rider on a ball, in a carriage).</summary>
    protected Vector3 VisualBase { get; set; }
    private float _life;

    /// <summary>
    /// Secondary motion on top of the frames: a slow breath, a heavy bounce in
    /// the step, a squash on landing. It is what keeps a big sprite alive
    /// between its key poses.
    /// </summary>
    private void Liven(float dt)
    {
        if (Visual is null) return;
        _life += dt;
        float speed = Flat(Velocity).Length();
        bool walking = speed > 0.6f && Grounded;
        float breath = Mathf.Sin(_life * 2.2f) * 0.022f;
        float step = walking ? Mathf.Abs(Mathf.Sin(_life * Mathf.Clamp(speed, 2f, 6f) * 1.5f)) : 0f;
        float sx = 1f - breath * 0.7f + step * 0.025f;
        float sy = 1f + breath - step * 0.035f;
        Visual.Scale = Visual.Scale.Lerp(new Vector3(sx, sy, 1f), 1f - Mathf.Exp(-18f * dt));
        Visual.Position = VisualBase + new Vector3(0f, step * 0.07f * Big, 0f);
    }

    protected override float StunScale => 0.45f;
    protected override float LaunchScale => 0f;
    protected override bool HasPoise => InSpecial;
    public override string HurtSound => "honk_low";
    public override string DeathSound => "kaboom";

    /// <summary>1 at full health, 2 below two thirds, 3 below one third.</summary>
    protected int PhaseNumber => Health > MaxHealth * 0.66f ? 1 : Health > MaxHealth * 0.33f ? 2 : 3;
    private int _shownPhase = 1;

    /// <summary>The boss's world, for its level of aggression. Set by the spawner.</summary>
    public int World { get; set; } = 1;

    /// <summary>The sounds that announce this boss: played as it is revealed.</summary>
    protected virtual string[] Signature => new[] { "fanfare" };

    /// <summary>Seconds left of the entrance, while the boss stands for its introduction.</summary>
    private float _presenting = UI.BossIntro.Seconds;
    protected bool Presenting => _presenting > 0f;

    public override bool CanBeHit() => !Presenting && base.CanBeHit();

    protected override void Tick(float dt)
    {
        if (Presenting)
        {
            Stop(dt);
            PlayClip("idle");
            return;
        }
        base.Tick(dt);
    }

    protected virtual void OnPhase(int phase)
    {
        Fx.Sound("crowd_gasp");
        Fx.Sound("honk_low", 0.7f, 2f);
        Fx.Pop(Stage, GlobalPosition + new Vector3(0f, BodyHeight + 0.4f, 0f), phase == 3 ? "FURIOUS!" : "ANGRY!", new Color(1f, 0.4f, 0.3f), 1.1f, 0.9f);
        Fx.Shake(this, 0.1f, 0.3f);
    }

    protected override void TickAlways(float dt)
    {
        base.TickAlways(dt);
        if (_presenting > 0f) _presenting -= dt;
        Liven(dt);
        if (!IsDead && PhaseNumber > _shownPhase)
        {
            _shownPhase = PhaseNumber;
            OnPhase(_shownPhase);
        }
    }

    public override void _Ready()
    {
        base._Ready();
        CallDeferred(MethodName.Announce);
    }

    private void Announce()
    {
        // Acts of two are introduced once, by their lead.
        if (PrimaryBoss) ClownSquad.UI.BossIntro.Play(this, BossTitle, Tagline, Signature, drumroll: Signature[0] != "ringmaster_fanfare");
    }

    /// <summary>The line under the boss's name on the title card.</summary>
    protected virtual string Tagline => "";

    public override BrawlerAi MakeAi(int gruntCooldownMs) =>
        new(this, attackRange: 1.6f * ReachScale, backoffRange: 0.9f, attackCooldownMs: 800,
            specialCooldownMs: (ulong)Mathf.Lerp(2600f, 1500f, (World - 1) / 9f), specialRange: 30f);
}
