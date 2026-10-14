namespace ClownSquad;

using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Progression;
using ClownSquad.UI;
using ClownSquad.Visuals;
using ClownSquad.World;

/// <summary>
/// Scene bootstrap. Sets up the arena, binds devices to fighters (local co-op
/// exercising the full multi-player path with no networking), and runs either
/// the wave fight or, with "-- sandbox", the old training room.
///
/// Command-line modes after "--":
///   (none)     the wave fight
///   sandbox    training dummy and a respawning boss, nothing ends
///   wavetest   an AI plays the waves; expects a win       (headless-safe)
///   losetest   nobody plays; expects a game over          (headless-safe)
///   aitest     boss AI assertions, in the sandbox
///   animtest   clip-selection assertions
///   movetest   bounds and mallet-move assertions
/// </summary>
public partial class GameRoot : Node3D
{
    private readonly List<PlayerFighter> _players = new();
    private readonly Dictionary<PlayerFighter, Vector3> _homes = new();
    /// <summary>Devices already driving a fighter, so a pad is never bound twice.</summary>
    private readonly HashSet<int> _boundDevices = new();
    private readonly HashSet<int> _joinHeld = new();
    private readonly List<TrainingDummy> _dummies = new();
    private readonly List<BossClown> _bosses = new();
    private readonly List<(Fighter fighter, Vector3 at, double due)> _pendingRespawns = new();

    private Label? _hud;
    private Label? _banner;
    private ClownSquad.UI.ClownHud? _clownHud;
    private bool _f3Down;
    private WaveDirector? _director;
    private Camera3D? _arenaCamera;
    private Transform3D _arenaCameraRest;
    private float _joinCooldown;
    private double _endedAt = -1;

    private StatTent? _tent;
    private bool _tentHeld;
    private double _tentDueAt = -1;
    private bool _restartHeld = true;

    // XP per enemy, paid to every player. Starting numbers, not balance.
    private const int GruntXp = 15;
    private const int BossXp = 150;

    private bool _aiProbe;
    private string _outcomeProbe = "";   // "win" or "lose" when a wave probe is running

    // ------------------------------------------------------------- startup

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        bool Has(string flag) => Array.IndexOf(args, flag) >= 0;

        // Tests run against a throwaway save, so they never read or touch a real one.
        bool probe = Has("animtest") || Has("movetest") || Has("aitest") || Has("wavetest") || Has("losetest") || Has("tentshot");
        if (probe)
        {
            SaveSystem.Path = "user://probe_save.json";
            SaveSystem.DeleteForTests();
        }
        else
        {
            SaveSystem.Path = SaveSystem.DefaultPath;
            SaveSystem.Load();
        }

        GameClock.Reset();
        AttackTokens.Reset();
        Engine.TimeScale = GameClock.Speed;
        AddChild(new ClownSquad.Audio.Sfx());

        // The arena is whatever the camera sees, captured before any shake, and
        // rebuilt whenever the window changes shape so the walls stay at the
        // screen edge.
        if (GetNodeOrNull<Camera3D>("Camera3D") is Camera3D cam)
        {
            _arenaCamera = cam;
            _arenaCameraRest = cam.GlobalTransform;
            RebuildArena();
            GetViewport().SizeChanged += RebuildArena;
            cam.AddChild(new ClownSquad.Visuals.CameraShake());
        }

        Fighter.HitLanded += OnHitLanded;

        if (Has("animtest")) { AddChild(new ClownSquad.Debug.AnimationProbe()); return; }
        if (Has("movetest")) { AddChild(new ClownSquad.Debug.MovesProbe()); return; }

        _aiProbe = Has("aitest");
        PlayerFighter.HdSample = Has("hd");
        if (Has("wavetest")) _outcomeProbe = "win";
        if (Has("losetest")) _outcomeProbe = "lose";

        SpawnPlayers();
        BuildHud();
        ClownSquad.Audio.Sfx.PlayMusic("music_march");

        if (Has("sandbox") || _aiProbe)
        {
            SpawnSandbox();
            return;
        }

        _director = new WaveDirector();
        AddChild(_director);
        _director.PhaseChanged += OnPhaseChanged;
        _director.EnemyDefeated += OnEnemyDefeated;
        _director.Begin(this);

        if (_outcomeProbe.Length > 0) StartOutcomeProbe();
        if (Has("tentshot")) StartTentShot();
    }

    /// <summary>"-- tentshot": a levelled character, a HUD still, then the Stat Tent open. Needs a window.</summary>
    private void StartTentShot()
    {
        PlayerFighter p = _players[0];
        Leveling.Grant(p.Record!, 200);
        p.ApplyProgression(p.Record!);
        SceneTreeTimer first = GetTree().CreateTimer(3.0, processAlways: true);
        first.Timeout += () =>
        {
            GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/hud_level.png");
            _tent!.Open();
            GetTree().CreateTimer(0.6, processAlways: true).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/stat_tent.png");
                GetTree().Quit();
            };
        };
    }

    public override void _ExitTree()
    {
        // Leaving the scene (quit, or replaying the stage) always saves.
        SaveSystem.Save();
        Fighter.HitLanded -= OnHitLanded;
        Arena.Current = null;
    }

    private void RebuildArena()
    {
        if (_arenaCamera is null) return;

        Vector2 view = GetViewport().GetVisibleRect().Size;
        // Headless runs report a token square viewport; fall back to the
        // project's design resolution so tests see the real arena.
        if (DisplayServer.GetName().Contains("headless") || view.Y < 100f)
        {
            view = new Vector2(
                (float)ProjectSettings.GetSetting("display/window/size/viewport_width", 1152),
                (float)ProjectSettings.GetSetting("display/window/size/viewport_height", 648));
        }
        Arena.Current = new Arena(_arenaCameraRest, _arenaCamera.Fov, view.X / Mathf.Max(view.Y, 1f));
    }

    // ------------------------------------------------------------- players

    private void SpawnPlayers()
    {
        // Keyboard always joins. Pads join on a button press rather than on
        // detection: one physical controller can appear as two devices, and
        // auto-binding both drives two fighters from one stick.
        AddPlayer(LocalDeviceInput.Keyboard, 0);

        foreach (int pad in Godot.Input.GetConnectedJoypads())
            GD.Print($"  pad {pad}: name=\"{Godot.Input.GetJoyName(pad)}\" guid={Godot.Input.GetJoyGuid(pad)}");
        GD.Print("Press A / X on a controller to join.");
    }

    /// <summary>
    /// Let an unbound pad join on a fresh button press. One join per cooldown
    /// window, so two devices reporting the same physical press cannot both join.
    /// </summary>
    private void PollForJoins(float dt)
    {
        if (_joinCooldown > 0f) { _joinCooldown -= dt; return; }

        foreach (int pad in Godot.Input.GetConnectedJoypads())
        {
            if (_boundDevices.Contains(pad)) continue;

            bool held = Godot.Input.IsJoyButtonPressed(pad, JoyButton.A)
                     || Godot.Input.IsJoyButtonPressed(pad, JoyButton.X)
                     || Godot.Input.IsJoyButtonPressed(pad, JoyButton.Start);
            bool wasHeld = _joinHeld.Contains(pad);
            if (held) _joinHeld.Add(pad); else _joinHeld.Remove(pad);

            if (held && !wasHeld)
            {
                GD.Print($"Player joined on pad {pad} ({Godot.Input.GetJoyName(pad)}).");
                AddPlayer(pad, _players.Count);
                _joinCooldown = 0.6f;
                return;
            }
        }
    }

    private void AddPlayer(int device, int index)
    {
        if (!_boundDevices.Add(device))
        {
            GD.Print($"Device {device} already bound; ignoring duplicate.");
            return;
        }

        var p = new PlayerFighter
        {
            Name = $"Player{index}",
            Source = new LocalDeviceInput(device),
            PlayerIndex = index,
            DisplayName = new[] { "PIP", "POPPY", "BINGO", "DOT" }[index % 4],
        };
        // Each player slot plays its saved character, created on first use.
        CharacterRecord record = SaveSystem.CharacterFor(index, p.DisplayName);
        p.DisplayName = record.Name;
        p.ApplyProgression(record);
        AddChild(p);
        Vector3 home = new(-1.5f + index * 1.5f, 0f, 1.5f);
        p.GlobalPosition = home;
        _homes[p] = home;
        p.Died += OnPlayerDied;
        _players.Add(p);
    }

    /// <summary>
    /// In the sandbox death is just a respawn. In the wave fight it spends one
    /// of the shared lives, and when a player falls with none left and nobody
    /// else is standing or on the way back, the fight is lost.
    /// </summary>
    private void OnPlayerDied(Fighter f)
    {
        Vector3 home = f is PlayerFighter p && _homes.TryGetValue(p, out Vector3 h) ? h : Vector3.Zero;

        if (_director is null) { ScheduleRespawn(f, home, 1.5); return; }
        if (_director.TryUseLife()) { ScheduleRespawn(f, home, 2.0); return; }

        GD.Print($"{f.Name} is out: no lives left.");
        bool anyoneLeft = _pendingRespawns.Count > 0;
        foreach (PlayerFighter other in _players)
            if (!other.IsDead) anyoneLeft = true;
        if (!anyoneLeft) _director.DeclareDefeat();
    }

    private void ScheduleRespawn(Fighter f, Vector3 at, double delay)
    {
        GD.Print($"{f.Name} down; respawning in {delay:F1}s.");
        _pendingRespawns.Add((f, at, GameClock.Seconds + delay));
    }

    private void TickRespawns()
    {
        for (int i = _pendingRespawns.Count - 1; i >= 0; i--)
        {
            (Fighter f, Vector3 at, double due) = _pendingRespawns[i];
            if (GameClock.Seconds < due) continue;
            _pendingRespawns.RemoveAt(i);
            if (GodotObject.IsInstanceValid(f))
            {
                f.Revive(at);
                GD.Print($"{f.Name} respawned.");
            }
        }
    }

    // ------------------------------------------------------------- sandbox

    private void SpawnSandbox()
    {
        var d = new TrainingDummy { Name = "Dummy" };
        AddChild(d);
        d.GlobalPosition = new Vector3(-3.5f, 0f, -2.5f);
        _dummies.Add(d);

        Vector3 at = new(3.5f, 0f, -2f);
        var boss = new BossClown { Name = "BossClown" };
        boss.Source = new BrawlerAi(boss, attackRange: 2.2f, backoffRange: 1.2f,
                                    attackCooldownMs: 700,
                                    specialCooldownMs: 5000, specialRange: 6.0f);
        AddChild(boss);
        boss.GlobalPosition = at;
        boss.Died += f => ScheduleRespawn(f, at, 3.0);
        _bosses.Add(boss);
        GD.Print("Sandbox: training dummy and boss clown.");
    }

    // ----------------------------------------------------------- the fight

    private void OnPhaseChanged(WaveDirector.Phase phase)
    {
        WaveDirector d = _director!;
        GD.Print($"[{GameClock.Seconds:F1}s] phase={phase} wave={d.WaveNumber}/{d.WaveCount} lives={d.Lives}");
        if (phase == WaveDirector.Phase.Intermission) SaveSystem.Save();
        if (phase is WaveDirector.Phase.Victory or WaveDirector.Phase.Defeat)
        {
            _endedAt = GameClock.Seconds;
            // Win or lose, XP is kept (ADR 0006). Anyone with points to spend
            // is shown the Stat Tent once the ending has had a moment.
            SaveSystem.Save();
            foreach (PlayerFighter p in _players)
                if (p.Record is { StatPoints: > 0 }) _tentDueAt = GameClock.Seconds + 2.0;
        }
    }

    // --------------------------------------------------------- progression

    /// <summary>
    /// Every player earns the full XP for every enemy, fallen players included:
    /// no kill stealing in co-op, and nobody is left behind for dying.
    /// </summary>
    private void OnEnemyDefeated(Fighter enemy)
    {
        bool boss = enemy is BossClown;
        int xp = boss ? BossXp : GruntXp;
        PopText.Spawn(this, enemy.GlobalPosition + new Vector3(0f, enemy.BodyHeight + 0.2f, 0f),
                      $"+{xp} XP", new Color(1f, 0.85f, 0.3f), boss ? 1.3f : 0.8f);

        foreach (PlayerFighter p in _players)
        {
            if (p.Record is not CharacterRecord r) continue;
            if (Leveling.Grant(r, xp) == 0) continue;

            // A level-up is a full heal: the classic reward for surviving.
            p.ApplyProgression(r);
            if (!p.IsDead) p.Heal(p.MaxHealth);
            PopText.Spawn(this, p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.35f, 0f),
                          "LEVEL UP!", new Color(0.5f, 0.9f, 1f), 1.2f, 1.3f);
            ClownSquad.Audio.Sfx.Play("level_up");
            GD.Print($"{r.Name} reached level {r.Level} ({r.StatPoints} stat point(s) to spend).");
        }
        SaveSystem.Save();
    }

    /// <summary>Tab or Back/Select opens the Stat Tent at any time; it also opens itself after a stage.</summary>
    private void PollStatTent()
    {
        if (_tent is null || _tent.IsOpen || _outcomeProbe.Length > 0) return;

        bool held = false;
        foreach (PlayerFighter p in _players)
            if (p.Source is LocalDeviceInput d && MenuPad.OpenHeld(d.Device)) held = true;
        bool pressed = held && !_tentHeld;
        _tentHeld = held;

        bool due = _tentDueAt >= 0 && GameClock.Seconds >= _tentDueAt;
        if (due) _tentDueAt = -1;
        if (pressed || due) _tent.Open();
    }

    /// <summary>Once the fight is over, any attack or start press plays it again.</summary>
    private void PollForRestart()
    {
        if (_endedAt < 0 || GameClock.Seconds - _endedAt < 1.5 || _outcomeProbe.Length > 0) return;

        bool pressed = Godot.Input.IsKeyPressed(Key.J) || Godot.Input.IsKeyPressed(Key.Enter);
        foreach (int pad in Godot.Input.GetConnectedJoypads())
            pressed |= Godot.Input.IsJoyButtonPressed(pad, JoyButton.X)
                    || Godot.Input.IsJoyButtonPressed(pad, JoyButton.Start);
        // A fresh press only, and not straight out of the Stat Tent, whose
        // buttons overlap: the press that spent a point must not restart.
        bool fresh = pressed && !_restartHeld;
        _restartHeld = pressed;
        if (_tent is not null && (_tent.IsOpen || _tent.MsSinceClosed < 400 || _tentDueAt >= 0)) return;
        if (fresh) GetTree().ReloadCurrentScene();
    }

    // Combo counter: consecutive hits by players, reset after a short lull.
    private const double ComboLull = 1.6;
    private int _comboHits;
    private double _comboExpires;

    private void OnHitLanded(Fighter? attacker, Fighter victim, float amount)
    {
        // Wall bounces arrive with no attacker and extend whatever is running.
        bool byPlayer = attacker is null ? victim.Hostile && _comboHits > 0 : !attacker.Hostile;
        if (!byPlayer) return;
        _comboHits++;
        _comboExpires = GameClock.Seconds + ComboLull;
    }

    // ----------------------------------------------------------------- hud

    private void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        _hud = MakeLabel(15, 5);
        _hud.Position = new Vector2(16, 12);
        layer.AddChild(_hud);

        _banner = MakeLabel(44, 10);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _banner.OffsetTop = 120;
        layer.AddChild(_banner);

        // The old text readout survives as a debug overlay on F3; the circus HUD
        // is what players see.
        _hud.Visible = false;
        _banner.Visible = false;
        _clownHud = new ClownSquad.UI.ClownHud(() => _players, () => _director,
            () => _endedAt >= 0 && GameClock.Seconds - _endedAt > 1.5);
        AddChild(_clownHud);

        _tent = new StatTent(() => _players);
        AddChild(_tent);
    }

    private static Label MakeLabel(int size, int outline)
    {
        var label = new Label { Theme = new Theme() };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        label.AddThemeConstantOverride("outline_size", outline);
        return label;
    }

    private void UpdateHud()
    {
        if (_hud is null || _banner is null) return;

        var sb = new StringBuilder();
        if (_director is WaveDirector d)
        {
            sb.AppendLine($"WAVE {d.WaveNumber}/{d.WaveCount}    ENEMIES {d.EnemiesAlive}    LIVES {d.Lives}");
            _banner.Text = d.Banner;
            if (_endedAt >= 0 && GameClock.Seconds - _endedAt > 1.5)
                _banner.Text += "\npress attack to play again";
        }
        else
        {
            sb.AppendLine("SANDBOX");
            _banner.Text = "";
        }

        sb.AppendLine();
        foreach (PlayerFighter p in _players)
            sb.AppendLine($"P{p.PlayerIndex + 1} [{p.Source.Describe()}]  hp {p.Health:F0}  {p.DebugState}");
        for (int i = 0; i < _dummies.Count; i++)
            sb.AppendLine($"Dummy  hp {_dummies[i].Health:F0} / {_dummies[i].MaxHealth:F0}");
        foreach (BossClown b in _bosses)
            sb.AppendLine($"BOSS  hp {b.Health:F0} / {b.MaxHealth:F0}  {b.DebugState}");

        if (_comboHits > 0 && GameClock.Seconds > _comboExpires) _comboHits = 0;
        if (_clownHud is not null) _clownHud.Combo = _comboHits;
        bool f3 = Godot.Input.IsKeyPressed(Key.F3);
        if (f3 && !_f3Down) _hud.Visible = !_hud.Visible;
        _f3Down = f3;
        if (_comboHits > 1)
        {
            sb.AppendLine();
            sb.AppendLine($">>> {_comboHits} HITS <<<");
        }

        sb.AppendLine();
        sb.AppendLine("WASD move | J attack | Space jump | K dodge      pad: stick | X | A | B");
        sb.AppendLine("hold attack 3s: throw mallet | hold attack + left/right: spin | attack in air: flip");
        _hud.Text = sb.ToString();
    }

    // -------------------------------------------------------------- frames

    public override void _PhysicsProcess(double delta)
    {
        GameClock.Advance(delta);
        TickRespawns();
        if (_outcomeProbe.Length > 0) TickOutcomeProbe();
    }

    public override void _Process(double delta)
    {
        if (_aiProbe) { TickAiProbe((float)delta); return; }
        if (_hud is null) return;   // probe modes that build no HUD

        PollForJoins((float)delta);
        PollStatTent();
        PollForRestart();
        UpdateHud();
    }

    // -------------------------------------------------------------- probes

    private float _aiProbeTime;
    private int _aiProbeTick;
    private bool _hangShot;
    private bool _punchShot;

    /// <summary>Boss AI closes distance, swings and slams. "-- aitest"</summary>
    private void TickAiProbe(float dt)
    {
        _aiProbeTime += dt;
        if (_aiProbeTime < _aiProbeTick * 0.6f) return;
        if (_players.Count == 0 || _bosses.Count == 0) return;

        BossClown b = _bosses[0];
        PlayerFighter p = _players[0];
        float d = b.GlobalPosition.DistanceTo(p.GlobalPosition);
        GD.Print($"  t={_aiProbeTime:F1}  dist={d:F2}  boss={b.DebugState} clip={b.CurrentClip}  playerHp={p.Health:F0}");

        if (b.Hanging && !_hangShot && !DisplayServer.GetName().Contains("headless"))
        {
            _hangShot = true;
            GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/flop_hang.png");
        }

        if (b.DebugState.StartsWith("ATK") && !_punchShot && !DisplayServer.GetName().Contains("headless"))
        {
            _punchShot = true;
            GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/boss_punch.png");
        }

        if (++_aiProbeTick > 26)
        {
            GD.Print("=== AI PROBE DONE ===");
            GetTree().Quit();
        }
    }

    private int _shots;
    private bool _sawDeadBlink;

    /// <summary>
    /// Plays the whole fight unattended, fast. For "win" an AI drives the
    /// player; for "lose" nobody does. Either way the outcome is asserted.
    /// </summary>
    private void StartOutcomeProbe()
    {
        Engine.TimeScale = 4.0;
        GD.Print($"=== WAVE PROBE: expecting {_outcomeProbe} ===");
        if (_outcomeProbe != "win") return;

        PlayerFighter p = _players[0];
        p.Source = new BrawlerAi(p, attackRange: 1.7f, backoffRange: 0.8f, attackCooldownMs: 350);
        p.ExtraDamageMultiplier = 2.5f;
    }

    /// <summary>
    /// After a won stage: the player has levelled, a spent point changes the
    /// fighter, and what was saved reads back from disk identical.
    /// </summary>
    private bool CheckProgression()
    {
        PlayerFighter p = _players[0];
        CharacterRecord r = p.Record!;
        bool levelled = r.Level >= 2 && r.StatPoints >= Leveling.StatPointsPerLevel;

        float before = p.MaxHealth;
        r.StatPoints--;
        r.Guts++;
        p.ApplyProgression(r);
        bool applied = Mathf.IsEqualApprox(p.MaxHealth, before + 8f);

        bool wrote = SaveSystem.Save();
        SaveSystem.Load();
        CharacterRecord back = SaveSystem.Data.Characters[0];
        bool persisted = wrote && back.Name == r.Name && back.Level == r.Level && back.Xp == r.Xp
                      && back.TotalXp == r.TotalXp && back.StatPoints == r.StatPoints && back.Guts == r.Guts;

        GD.Print($"  progression: {r.Name} level {r.Level}, {r.TotalXp} xp total, {r.StatPoints} point(s) unspent; " +
                 $"levelled={levelled} guts point applied={applied} ({before:F0} -> {p.MaxHealth:F0} hp) reloaded from disk={persisted}");
        return levelled && applied && persisted;
    }

    private void TickOutcomeProbe()
    {
        WaveDirector d = _director!;
        if (_players[0].IsDead && !_players[0].SpriteVisible) _sawDeadBlink = true;

        // A few stills along the way, when there is a window to take them from.
        if (!DisplayServer.GetName().Contains("headless") && GameClock.Seconds > 4.0 + _shots * 5.0 && _shots < 6)
        {
            GetViewport().GetTexture().GetImage().SavePng($"C:/Claude/ClownSquad/debug/wave_{_shots}.png");
            _shots++;
        }

        bool over = d.Current is WaveDirector.Phase.Victory or WaveDirector.Phase.Defeat;
        if (!over && GameClock.Seconds < 300) return;
        if (over && GameClock.Seconds - _endedAt < 2.0) return;   // let the ending show

        string got = d.Current == WaveDirector.Phase.Victory ? "win"
                   : d.Current == WaveDirector.Phase.Defeat ? "lose" : "timeout";
        bool ok = got == _outcomeProbe;
        var heard = new List<string>();
        foreach (var kv in ClownSquad.Audio.Sfx.Played) heard.Add($"{kv.Key}x{kv.Value}");
        heard.Sort();
        GD.Print($"  sound banks loaded: {ClownSquad.Audio.Sfx.BankCount}; played: {string.Join(" ", heard)}");
        GD.Print($"  sound names with no file: {(ClownSquad.Audio.Sfx.Missing.Count == 0 ? "none" : string.Join(" ", ClownSquad.Audio.Sfx.Missing))}");
        ok &= ClownSquad.Audio.Sfx.Missing.Count == 0;
        if (_outcomeProbe == "win") ok &= CheckProgression();
        if (_outcomeProbe == "lose")
        {
            GD.Print($"  player blinked while lying dead: {_sawDeadBlink}");
            ok &= _sawDeadBlink;
        }
        GD.Print($"=== WAVE PROBE {(ok ? "PASS" : "FAIL")}: got {got}, expected {_outcomeProbe}; " +
                 $"wave {d.WaveNumber}/{d.WaveCount}, lives {d.Lives}, {GameClock.Seconds:F0}s of game time ===");
        _outcomeProbe = "";
        Engine.TimeScale = GameClock.Speed;
        GetTree().Quit(ok ? 0 : 1);
    }
}
