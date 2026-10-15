namespace ClownSquad.World;

using System;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Input;
using ClownSquad.Map;
using ClownSquad.Progression;
using ClownSquad.Visuals;

/// <summary>
/// Runs the fight: waves of enemies in from the screen edges, a shared pool of
/// lives, and a win or lose at the end. This is what turns a combat sandbox
/// into something with a beginning and an end.
///
/// Spawn positions come from a seeded generator the director owns, in keeping
/// with ADR 0003: one place decides, and a given seed replays the same fight.
/// </summary>
public partial class WaveDirector : Node
{
    public enum Phase { Intermission, Fighting, Victory, Defeat }

    // The fight run with no stage chosen (tests, the sandbox): teach, press,
    // then test - a few, more, then the boss with company.
    private static readonly WaveDef[] DefaultWaves = { new(3, false), new(5, false), new(2, true) };

    /// <summary>The waves to run. A stage from the world map replaces the default.</summary>
    public WaveDef[] Waves { get; set; } = DefaultWaves;

    /// <summary>Enemies never fall below this level, however low the host is.</summary>
    public int LevelFloor { get; set; } = 1;

    /// <summary>Multiplies the Rare, Epic and Legendary spawn weights; later worlds raise it.</summary>
    public float RankBoost { get; set; } = 1f;

    /// <summary>Time between a grunt's attacks; later worlds shorten it.</summary>
    public int GruntCooldownMs { get; set; } = 1300;

    /// <summary>Above zero, this is a bonus stage: harmless pinatas stream in until the clock runs out.</summary>
    public float BonusSeconds { get; set; }
    public float BonusLeft { get; private set; }
    public bool IsBonus => BonusSeconds > 0f;

    // ---- scrolling stages ----
    // The squad walks right; each wave waits, unseen, at a point along the
    // stage. Reaching it locks the camera there until the group is beaten,
    // then the way on opens again. After the last group, the exit ends it.

    /// <summary>Camera positions of the encounters, one per wave. Null for the fixed ring.</summary>
    public float[]? EncounterX { get; set; }
    public float ExitX { get; set; }
    /// <summary>How far right the squad has got: the furthest living player.</summary>
    public Func<float> LeadX { get; set; } = () => 0f;
    /// <summary>While a group is being fought, the camera holds here.</summary>
    public float? CameraLock { get; private set; }
    /// <summary>Between groups: nothing to fight, so the HUD points the way on.</summary>
    public bool ShowGo { get; private set; }
    public bool IsScrolling => EncounterX is not null;
    /// <summary>Encounters beaten so far, for the results screen.</summary>
    public int EncountersCleared { get; private set; }

    private const float Intermission = 2.2f;

    private static readonly string[] GruntNames =
    {
        "GIGGLES", "BONZO", "CHUCKLES", "WOBBLES", "SQUEAKY", "DOODLES",
        "PICKLES", "BUTTONS", "SNIFFLES", "NOODLE", "KAZOO KEN", "LOLLY",
    };
    private const float SpawnInterval = 0.7f;

    public ulong Seed { get; set; } = 20261014;

    /// <summary>The level enemies scale to: the host's (GDD 7). Set by whoever runs the stage.</summary>
    public Func<int> HostLevel { get; set; } = () => 1;

    /// <summary>Forces every grunt to one rank, for tests. Null rolls normally.</summary>
    public EnemyRank? ForceRank { get; set; }

    // GDD 3 spawn weights: Common 1000, Rare 150, Epic 30, wild Legendary 1.
    private static readonly (EnemyRank rank, int weight)[] RankWeights =
        { (EnemyRank.Common, 1000), (EnemyRank.Rare, 150), (EnemyRank.Epic, 30), (EnemyRank.Legendary, 1) };

    public Phase Current { get; private set; } = Phase.Intermission;
    public int WaveNumber { get; private set; } = 1;
    public int WaveCount => Waves.Length;
    public int EnemiesAlive { get; private set; }
    /// <summary>Spare lives shared by all players.</summary>
    public int Lives { get; private set; } = 3;

    public event Action<Phase>? PhaseChanged;
    /// <summary>An enemy went down for good. Where XP is paid out.</summary>
    public event Action<Fighter>? EnemyDefeated;

    private readonly RandomNumberGenerator _rng = new();
    private Node3D _world = null!;
    private float _timer;
    private int _gruntsToSpawn;
    private bool _bossToSpawn;
    private float _spawnTimer;
    private int _side = 1;
    private double _normalTimeScale = 1.0;
    private ulong _slowUntilMs;

    public void Begin(Node3D world)
    {
        _world = world;
        _rng.Seed = Seed;
        _normalTimeScale = Engine.TimeScale;
        _timer = Intermission;
        BonusLeft = BonusSeconds;
    }

    public string Banner => Current switch
    {
        Phase.Intermission when IsBonus => "PINATA PARTY!",
        Phase.Intermission when IsScrolling => "",
        Phase.Fighting when IsScrolling => "",
        Phase.Victory when IsScrolling => "STAGE CLEAR!",
        Phase.Intermission => WaveNumber == WaveCount ? "FINAL WAVE" : $"WAVE {WaveNumber}",
        Phase.Victory when IsBonus => "PARTY'S OVER!",
        Phase.Victory => "YOU WIN!",
        Phase.Defeat => "GAME OVER",
        _ => "",
    };

    private void Set(Phase phase)
    {
        if (Current == phase) return;
        Current = phase;
        switch (phase)
        {
            case Phase.Fighting: Sfx.Play("fanfare"); break;
            case Phase.Victory: Sfx.StopMusic(); Sfx.Play("victory"); break;
            case Phase.Defeat: Sfx.StopMusic(); Sfx.Play("sad_trombone"); break;
        }
        PhaseChanged?.Invoke(phase);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (IsBonus) { TickBonus(dt); return; }
        if (IsScrolling) { TickScrolling(dt); return; }

        if (Current == Phase.Intermission)
        {
            _timer -= dt;
            if (_timer > 0f) return;
            WaveDef wave = Waves[WaveNumber - 1];
            _gruntsToSpawn = wave.Grunts;
            _bossToSpawn = wave.Boss;
            _spawnTimer = 0f;
            Set(Phase.Fighting);
        }
        else if (Current == Phase.Fighting)
        {
            if (_gruntsToSpawn > 0 || _bossToSpawn)
            {
                // Staggered, so a wave arrives as a stream rather than a wall.
                _spawnTimer -= dt;
                if (_spawnTimer > 0f) return;
                _spawnTimer = SpawnInterval;
                if (_bossToSpawn) { _bossToSpawn = false; Spawn(boss: true); }
                else { _gruntsToSpawn--; Spawn(boss: false); }
                return;
            }

            if (EnemiesAlive > 0) return;
            if (WaveNumber >= WaveCount) { Set(Phase.Victory); return; }
            WaveNumber++;
            _timer = Intermission;
            Set(Phase.Intermission);
        }
    }

    private void TickScrolling(float dt)
    {
        if (Current == Phase.Intermission)
        {
            int i = WaveNumber - 1;
            float lead = LeadX();
            if (i < Waves.Length)
            {
                ShowGo = true;
                if (lead < EncounterX![i] - 2.5f) return;
                // Walked into the next group: hold the camera here and bring them on.
                WaveDef wave = Waves[i];
                _gruntsToSpawn = wave.Grunts;
                _bossToSpawn = wave.Boss;
                _spawnTimer = 0.35f;
                CameraLock = EncounterX[i];
                ShowGo = false;
                Set(Phase.Fighting);
            }
            else
            {
                ShowGo = true;
                if (lead >= ExitX) { ShowGo = false; Set(Phase.Victory); }
            }
            return;
        }
        if (Current != Phase.Fighting) return;

        if (_gruntsToSpawn > 0 || _bossToSpawn)
        {
            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;
            _spawnTimer = SpawnInterval;
            if (_bossToSpawn) { _bossToSpawn = false; Spawn(boss: true); }
            else { _gruntsToSpawn--; Spawn(boss: false); }
            return;
        }
        if (EnemiesAlive > 0) return;
        CameraLock = null;
        EncountersCleared++;
        WaveNumber++;
        Set(Phase.Intermission);
    }

    /// <summary>
    /// The bonus stage: pinatas keep coming, a few at a time, and the party
    /// ends when the clock does. Nothing fights back, so it cannot be lost.
    /// </summary>
    private void TickBonus(float dt)
    {
        if (Current == Phase.Intermission)
        {
            _timer -= dt;
            if (_timer <= 0f) Set(Phase.Fighting);
            return;
        }
        if (Current != Phase.Fighting) return;
        BonusLeft -= dt;
        if (BonusLeft <= 0f)
        {
            BonusLeft = 0f;
            foreach (Node n in GetTree().GetNodesInGroup(Fighter.Group))
                if (n is GruntClown g && !g.IsDead) g.BeginDespawn(0.1f);
            Set(Phase.Victory);
            return;
        }
        _spawnTimer -= dt;
        if (_spawnTimer <= 0f && EnemiesAlive < 6)
        {
            _spawnTimer = 0.55f;
            Spawn(boss: false, harmless: true);
        }
    }

    public override void _Process(double delta)
    {
        // Slow motion is timed on the wall clock, since game time is what it bends.
        if (_slowUntilMs != 0 && Time.GetTicksMsec() >= _slowUntilMs)
        {
            _slowUntilMs = 0;
            Engine.TimeScale = _normalTimeScale;
        }
    }

    /// <summary>The running director, for enemies that bring others into the fight.</summary>
    public static WaveDirector? Running { get; private set; }

    /// <summary>The world being fought in: picks the boss act. Set by whoever runs the stage.</summary>
    public int World { get; set; } = 1;

    /// <summary>The enemy mix: kinds and weights (EnemyCatalog). Grunts only when unset.</summary>
    public (string kind, int weight)[] Pool { get; set; } = { ("grunt", 1) };

    public override void _EnterTree() => Running = this;

    private string PickKind()
    {
        int total = 0;
        foreach ((_, int w) in Pool) total += w;
        int roll = _rng.RandiRange(1, Mathf.Max(1, total));
        foreach ((string kind, int w) in Pool)
        {
            roll -= w;
            if (roll <= 0) return kind;
        }
        return "grunt";
    }

    private void Spawn(bool boss, bool harmless = false)
    {
        if (boss)
        {
            // A boss act: the boss and any partner, in from opposite sides.
            foreach (PlayerFighter b in EnemyCatalog.Boss(World))
            {
                b.Name = b is BossClown ? "BossClown" : b.GetType().Name;
                b.Source = b is BossClown
                    ? new BrawlerAi(b, attackRange: 2.2f, backoffRange: 1.2f, attackCooldownMs: 700, specialCooldownMs: 5000, specialRange: 6.0f)
                    : EnemyCatalog.Brain(b, GruntCooldownMs);
                Place(b, EnemyRank.Common);
            }
            return;
        }

        string kind = harmless ? "grunt" : PickKind();
        PlayerFighter enemy = EnemyCatalog.Create(kind);
        enemy.Name = kind;
        enemy.DisplayName = harmless ? "PINATA" : EnemyCatalog.NameFor(kind, _rng, GruntNames);
        enemy.Source = harmless
            ? new BrawlerAi(enemy, attackRange: 1.35f, backoffRange: 0.7f, attackCooldownMs: 99999999UL)
            : EnemyCatalog.Brain(enemy, GruntCooldownMs);
        bool ranks = !harmless && (enemy is not CastFighter cf || cf.RollsRank);
        // A forced rank (tests) applies to grunts; the rest of the cast rolls normally.
        bool forcedOff = ForceRank is not null && enemy is not GruntClown;
        Place(enemy, !ranks ? EnemyRank.Common : forcedOff ? EnemyRank.Common : RollRank());
        if (harmless) enemy.DamageMultiplier = 0f;
    }

    /// <summary>Add an enemy at the screen edge, scaled to the stage, counted for the wave.</summary>
    private void Place(PlayerFighter enemy, EnemyRank rank)
    {
        _world.AddChild(enemy);
        int level = Mathf.Max(LevelFloor, HostLevel());
        enemy.ApplyEnemyRank(level, rank);
        if (enemy.Rank != EnemyRank.Common)
            GD.Print($"{enemy.DisplayName} appears (level {enemy.EnemyLevel}).");

        // In from alternating sides, at the screen edge, at a random depth.
        Vector3 at = Vector3.Zero;
        if (Arena.Current is Arena arena)
        {
            (float far, float near) = arena.DepthRange(enemy.BodyHeight);
            float z = _rng.RandfRange(far + 0.4f, Mathf.Max(far + 0.5f, near - 0.6f));
            float cx = CameraLock ?? arena.CameraX;
            float x = cx + _side * Mathf.Max(0.5f, arena.HalfWidthAt(z, enemy.BodyHeight) - enemy.BodyHalfWidth);
            at = new Vector3(x, 0f, z);
        }
        _side = -_side;
        enemy.GlobalPosition = at;
        enemy.Died += OnEnemyDied;
        EnemiesAlive++;
    }

    /// <summary>
    /// An enemy called up mid-fight (a balloon dog, the clown car's passengers,
    /// the Ringmaster's acts): scaled like the rest, counted for the wave.
    /// </summary>
    public void Summon(PlayerFighter enemy, Vector3 at)
    {
        if (enemy.Source is null) enemy.Source = EnemyCatalog.Brain(enemy, GruntCooldownMs);
        if (enemy.DisplayName.Length == 0) enemy.DisplayName = EnemyCatalog.NameFor(enemy.Name, _rng, GruntNames);
        _world.AddChild(enemy);
        enemy.ApplyEnemyRank(Mathf.Max(LevelFloor, HostLevel()), EnemyRank.Common);
        enemy.GlobalPosition = at;
        enemy.Died += OnEnemyDied;
        EnemiesAlive++;
    }

    /// <summary>Send an enemy away without a fight (no XP): a tiger whose trainer has fallen.</summary>
    public void Dismiss(PlayerFighter enemy)
    {
        if (!IsInstanceValid(enemy) || enemy.IsDead) return;
        enemy.Died -= OnEnemyDied;
        EnemiesAlive = Mathf.Max(0, EnemiesAlive - 1);
        enemy.Stagger(5f);
        enemy.BeginDespawn(0.4f);
        PopText.Spawn(_world, enemy.GlobalPosition + new Vector3(0f, enemy.BodyHeight + 0.2f, 0f), "RUN AWAY!", Colors.White, 0.7f, 0.6f);
        CheckFinalBlow();
    }

    private EnemyRank RollRank()
    {
        if (ForceRank is EnemyRank forced) return forced;
        int Weight(EnemyRank r, int w) => r == EnemyRank.Common ? w : (int)Mathf.Round(w * RankBoost);
        int total = 0;
        foreach ((EnemyRank r, int w) in RankWeights) total += Weight(r, w);
        int roll = _rng.RandiRange(1, total);
        foreach ((EnemyRank rank, int w) in RankWeights)
        {
            roll -= Weight(rank, w);
            if (roll <= 0) return rank;
        }
        return EnemyRank.Common;
    }

    private void OnEnemyDied(Fighter enemy)
    {
        EnemiesAlive--;
        enemy.BeginDespawn();
        EnemyDefeated?.Invoke(enemy);
        CheckFinalBlow();
    }

    private void CheckFinalBlow()
    {
        // The last blow of the fight lands in slow motion.
        bool finalBlow = !IsBonus && WaveNumber >= WaveCount && EnemiesAlive == 0 && _gruntsToSpawn == 0 && !_bossToSpawn;
        if (finalBlow && Engine.TimeScale <= 1.01)
        {
            Engine.TimeScale = 0.3;
            _slowUntilMs = Time.GetTicksMsec() + 700;
        }
    }

    /// <summary>Spend a spare life on a respawn. False when none remain.</summary>
    public bool TryUseLife()
    {
        if (Lives <= 0) return false;
        Lives--;
        return true;
    }

    public void DeclareDefeat()
    {
        if (Current is Phase.Victory or Phase.Defeat) return;
        Set(Phase.Defeat);
    }

    public override void _ExitTree()
    {
        Engine.TimeScale = _normalTimeScale;
        if (Running == this) Running = null;
    }
}
