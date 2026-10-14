namespace ClownSquad.World;

using System;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Input;

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

    private readonly record struct Wave(int Grunts, bool Boss);

    // Teach, press, then test: a few, more, then the boss with company.
    private static readonly Wave[] Waves = { new(3, false), new(5, false), new(2, true) };

    private const float Intermission = 2.2f;

    private static readonly string[] GruntNames =
    {
        "GIGGLES", "BONZO", "CHUCKLES", "WOBBLES", "SQUEAKY", "DOODLES",
        "PICKLES", "BUTTONS", "SNIFFLES", "NOODLE", "KAZOO KEN", "LOLLY",
    };
    private const float SpawnInterval = 0.7f;

    public ulong Seed { get; set; } = 20261014;

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
    }

    public string Banner => Current switch
    {
        Phase.Intermission => WaveNumber == WaveCount ? "FINAL WAVE" : $"WAVE {WaveNumber}",
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

        if (Current == Phase.Intermission)
        {
            _timer -= dt;
            if (_timer > 0f) return;
            Wave wave = Waves[WaveNumber - 1];
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

    public override void _Process(double delta)
    {
        // Slow motion is timed on the wall clock, since game time is what it bends.
        if (_slowUntilMs != 0 && Time.GetTicksMsec() >= _slowUntilMs)
        {
            _slowUntilMs = 0;
            Engine.TimeScale = _normalTimeScale;
        }
    }

    private void Spawn(bool boss)
    {
        PlayerFighter enemy = boss ? new BossClown { Name = "BossClown" } : new GruntClown { Name = "Grunt" };
        if (!boss) enemy.DisplayName = GruntNames[_rng.RandiRange(0, GruntNames.Length - 1)];
        enemy.Source = boss
            ? new BrawlerAi(enemy, attackRange: 2.2f, backoffRange: 1.2f, attackCooldownMs: 700,
                            specialCooldownMs: 5000, specialRange: 6.0f)
            : new BrawlerAi(enemy, attackRange: 1.35f, backoffRange: 0.7f, attackCooldownMs: 1300);

        _world.AddChild(enemy);

        // In from alternating sides, at the screen edge, at a random depth.
        Vector3 at = Vector3.Zero;
        if (Arena.Current is Arena arena)
        {
            (float far, float near) = arena.DepthRange(enemy.BodyHeight);
            float z = _rng.RandfRange(far + 0.4f, near - 0.6f);
            float x = _side * (arena.HalfWidthAt(z, enemy.BodyHeight) - enemy.BodyHalfWidth);
            at = new Vector3(x, 0f, z);
        }
        _side = -_side;
        enemy.GlobalPosition = at;
        enemy.Died += OnEnemyDied;
        EnemiesAlive++;
    }

    private void OnEnemyDied(Fighter enemy)
    {
        EnemiesAlive--;
        enemy.BeginDespawn();
        EnemyDefeated?.Invoke(enemy);

        // The last blow of the fight lands in slow motion.
        bool finalBlow = WaveNumber >= WaveCount && EnemiesAlive == 0 && _gruntsToSpawn == 0 && !_bossToSpawn;
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

    public override void _ExitTree() => Engine.TimeScale = _normalTimeScale;
}
