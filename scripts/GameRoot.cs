namespace ClownSquad;

using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;
using ClownSquad.Input;
using ClownSquad.Map;
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
    private StageDef? _stage;

    // ---- scrolling stages ----
    private bool _scrolling;
    private StageScenery? _scenery;
    private Node3D? _rig;              // carries the camera (and its lamps) along the stage
    private float _camX;
    private const float EncounterSpacing = 15f;

    // ---- the results screen and pause ----
    private int _kos;
    private int _bestCombo;
    private int _goldEarned;
    private int _itemsFound;
    private PauseMenu? _pause;
    private bool _pauseHeld = true;
    private bool _retryHeld = true;
    private Control? _results;
    private bool _rush;
    private double _rushTime;
    private bool _rushRecord;
    private const float FirstEncounter = 9f;
    private readonly Dictionary<PlayerFighter, int> _bagGold = new();
    private int _secretWave = -1;
    private Node3D? _secretCurtain;
    private ShopMenu? _shop;
    private CircusArena? _circus;
    private bool _tentHeld;
    private double _tentDueAt = -1;
    private bool _restartHeld = true;

    // XP per enemy, paid to every player. Starting numbers, not balance.
    private const int GruntXp = 15;
    private const int BossXp = 150;

    // Loot (ADR 0006): picked up into a bag for the stage, banked on a win, lost on a loss.
    private readonly RandomNumberGenerator _lootRng = new();
    private readonly Dictionary<PlayerFighter, List<ItemData>> _bags = new();
    private readonly List<LootDrop> _drops = new();
    private readonly HashSet<string> _swept = new();
    private int _dropsSpawned;
    private int _itemsBanked;

    private bool _aiProbe;
    private bool _practice;
    private string _outcomeProbe = "";   // "win" or "lose" when a wave probe is running

    // ------------------------------------------------------------- startup

    public override void _Ready()
    {
        // Command-line test and debug flags only work in debug builds: a
        // released game ignores them.
        string[] args = OS.IsDebugBuild() ? OS.GetCmdlineUserArgs() : Array.Empty<string>();
        bool Has(string flag) => Array.IndexOf(args, flag) >= 0;

        // The HD art is the default; the original sheets are kept and selectable.
        ClownSquad.Visuals.ArtStyle.Classic = Has("classic");

        // Tests run against a throwaway save, so they never read or touch a real one.
        bool probe = Has("animtest") || Has("movetest") || Has("aitest") || Has("wavetest") || Has("losetest") || Has("tentshot") || Has("skillshot") || Has("weapontest") || Has("castshot") || Has("demo");
        // Which kind of play this is, and so which save it uses (Session). The
        // stage chosen on the map is read before a Begin clears it.
        StageDef? picked = Campaign.Pending;
        if (probe) Session.BeginTest();
        else if (Has("bossrush") || Session.Mode == PlayMode.BossRush) Session.BeginBossRush();   // fresh every run, retries too
        else if (Session.Mode == PlayMode.Campaign) { SaveSystem.UseActiveSlot(); SaveSystem.Load(); }
        // A probe of the boss rush ("-- wavetest bossrush") runs the rush on the test save.
        _rush = Session.Mode == PlayMode.BossRush || (probe && Has("bossrush"));
        _practice = Session.Mode == PlayMode.Practice;

        // A stage chosen on the world map, or named on the command line
        // ("-- stage 2-3"). With neither, and no test or sandbox asked for, the
        // game opens on the world map.
        _stage = Session.Mode == PlayMode.Campaign || probe ? picked : null;
        int stageArg = Array.IndexOf(args, "stage");
        if (stageArg >= 0 && stageArg + 1 < args.Length) _stage = Campaign.Get(args[stageArg + 1]);
        Campaign.Pending = null;
        // Fights on the map are walked through left to right; bonus stages stay in the ring.
        _scrolling = _stage is StageDef sc && Campaign.IsFight(sc) && sc.Kind != StageKind.Bonus;
        if (!probe && !_rush && !_practice && !Has("sandbox") && !Has("fight") && _stage is null)
        {
            CallDeferred(MethodName.GoToMap);
            return;
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
            if (_scrolling) MountStageCamera(cam);
            _arenaCamera = cam;
            _arenaCameraRest = cam.GlobalTransform;
            RebuildArena();
            GetViewport().SizeChanged += RebuildArena;
            cam.AddChild(new ClownSquad.Visuals.CameraShake());
        }

        Fighter.HitLanded += OnHitLanded;
        if (!ClownSquad.Visuals.ArtStyle.Classic) SetUpLighting();

        if (Has("animtest")) { AddChild(new ClownSquad.Debug.AnimationProbe()); return; }
        if (Has("movetest")) { AddChild(new ClownSquad.Debug.MovesProbe()); return; }
        if (Has("weapontest")) { AddChild(new ClownSquad.Debug.WeaponProbe()); return; }
        if (Has("castshot")) { SpawnPlayers(); AddChild(new ClownSquad.Debug.CastLineup()); return; }

        _aiProbe = Has("aitest");
        if (Has("wavetest")) _outcomeProbe = "win";
        if (Has("losetest")) _outcomeProbe = "lose";

        SpawnPlayers();
        if (_rush) foreach (PlayerFighter rp in _players) ApplyLevel(rp, BossRush.Level);
        BuildHud();
        ClownSquad.Audio.Sfx.PlayMusic(_stage switch
        {
            StageDef { Kind: StageKind.Boss } => "music_boss",
            StageDef { Kind: StageKind.Bonus } => "music_bonus",
            StageDef song => $"music_world{song.World}",
            _ => "music_world1",
        });

        if (Has("sandbox") || _aiProbe)
        {
            SpawnSandbox();
            return;
        }

        _director = new WaveDirector();
        if (_stage is StageDef st && Campaign.IsFight(st))
        {
            _director.Waves = st.Waves.Length > 0 ? st.Waves : _director.Waves;
            _director.LevelFloor = st.EnemyLevel;
            _director.RankBoost = st.RankBoost;
            _director.World = st.World;
            _director.Pool = EnemyCatalog.Pool(st.World);
            _director.GruntCooldownMs = st.GruntCooldownMs;
            _director.BonusSeconds = st.BonusSeconds;
            int clears = SaveSystem.Data.Campaign.Cleared.Count;
            _director.Seed = (ulong)GD.Hash(st.Id) * 7919UL + (ulong)clears;
            // A rare chance of the secret shop appearing before one of the later waves.
            var roll = new RandomNumberGenerator { Seed = _director.Seed ^ 0x5ec2e7UL };
            if (st.Kind != StageKind.Bonus && st.Waves.Length > 1 && (roll.Randf() < SecretShopChance || Has("secret")))
                _secretWave = roll.RandiRange(2, st.Waves.Length);
            if (_scrolling) ConfigureEncounters(st);
            GD.Print($"Stage {st.Id}: {st.Waves.Length} wave(s), enemy level floor {st.EnemyLevel}, rank boost {st.RankBoost:F2}" +
                     (_secretWave > 0 ? $", secret shop before wave {_secretWave}" : ""));
        }
        if (_rush)
        {
            var waves = new WaveDef[BossRush.Bosses];
            for (int i = 0; i < waves.Length; i++) waves[i] = new WaveDef(0, true);
            _director.Waves = waves;
            _director.BossRushMode = true;
            _director.LevelFloor = BossRush.Level;
        }
        // "-- fight bossnow world 7": straight to a world's boss act in the ring, for checking bosses.
        if (_stage is null && Has("bossnow"))
        {
            int w = Array.IndexOf(args, "world") is int wi && wi >= 0 && wi + 1 < args.Length && int.TryParse(args[wi + 1], out int wv) ? wv : 1;
            _director.Waves = new[] { new WaveDef(0, true) };
            _director.World = w;
            _director.LevelFloor = Campaign.StageLevel(w, 5);
        }
        AddChild(_director);
        _director.PhaseChanged += OnPhaseChanged;
        _director.EnemyDefeated += OnEnemyDefeated;
        _director.HostLevel = () => _players.Count > 0 && _players[0].Record is CharacterRecord host ? host.Level : 1;
        _lootRng.Seed = _director.Seed ^ 0x5eed10075UL;
        _director.Begin(this);

        if (_outcomeProbe.Length > 0) StartOutcomeProbe();
        if (Has("tentshot")) StartTentShot();
        if (Has("skillshot")) StartSkillShot();
        if (Has("stageshot")) StartStageShot();
        if (Has("demo")) StartDemo();

        // "-- snaps 4,8.5,12": screenshots at those seconds into the fight (needs a window).
        int snapsAt = Array.IndexOf(args, "snaps");
        if (snapsAt >= 0 && snapsAt + 1 < args.Length && DisplayServer.GetName() != "headless")
            foreach (string s in args[snapsAt + 1].Split(','))
                if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t))
                    GetTree().CreateTimer(t, true, false, true).Timeout += () =>
                        GetViewport().GetTexture().GetImage().SavePng($"{ClownSquad.Debug.DebugShots.Dir}snap_{s}.png");
    }

    /// <summary>
    /// Lighting for the normal-mapped art: the key light turned to come from the
    /// upper left, where the sprites' own shading says it does, and a warm and a
    /// cool circus lamp either side so every outline catches a coloured rim.
    /// </summary>
    private void SetUpLighting()
    {
        if (GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") is DirectionalLight3D sun)
        {
            sun.LookAtFromPosition(sun.GlobalPosition, sun.GlobalPosition + new Vector3(0.55f, -0.7f, -0.45f), Vector3.Up);
            sun.LightEnergy = 0.85f;     // lower, so lamps, spots and hit flashes read
            sun.LightVolumetricFogEnergy = 0f;   // haze shows only the spotlight beams
        }
        if (GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment is Godot.Environment env)
        {
            env.AmbientLightEnergy = 0.35f;
            // Both reference games are saturated and clean, not hazy: less fog,
            // and a grade that keeps the sprites' colours vivid through the tonemap.
            // Haze that only lights up where a beam passes: no emission of its
            // own, and only the follow-spots scatter into it.
            env.VolumetricFogDensity = 0.012f;
            env.VolumetricFogEmission = Colors.Black;
            env.AdjustmentEnabled = true;
            env.AdjustmentContrast = 1.1f;
            env.AdjustmentSaturation = 1.22f;
        }

        void Lamp(Vector3 at, Color color, float energy)
        {
            var lamp = new OmniLight3D { LightColor = color, LightEnergy = energy, OmniRange = 11f, OmniAttenuation = 1.2f, LightVolumetricFogEnergy = 0f };
            // On a scrolling stage the rim lamps travel with the camera.
            (_rig ?? (Node)this).AddChild(lamp);
            lamp.GlobalPosition = at;
        }
        Lamp(new Vector3(-6.5f, 3.2f, 0.5f), new Color(1f, 0.55f, 0.45f), 3.4f);   // warm, stage left
        Lamp(new Vector3(6.5f, 3.2f, 0.5f), new Color(0.45f, 0.7f, 1f), 3.0f);     // cool, stage right

        foreach (string old in new[] { "Ground", "PropBack" })
            if (GetNodeOrNull<Node3D>(old) is Node3D n) n.Visible = false;
        if (_scrolling && _stage is StageDef st)
        {
            // A long walk through the world's own scenery.
            float length = FirstEncounter + EncounterSpacing * st.Waves.Length + 14f;
            _scenery = new StageScenery(st.World, length, (ulong)GD.Hash(st.Id));
            AddChild(_scenery);
            var theme = _scenery.Theme;
            if (GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment is Godot.Environment senv)
            {
                senv.BackgroundColor = theme.sky;
                senv.AmbientLightEnergy = theme.ambient;
                senv.AmbientLightColor = theme.sky.Lerp(Colors.White, 0.5f);
                senv.VolumetricFogDensity = 0.006f;
                _scenery.Dress(senv, GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.CameraAttributes as CameraAttributesPractical);
            }
            if (GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D") is DirectionalLight3D ssun)
            {
                ssun.LightColor = theme.sun;
                ssun.LightEnergy = theme.sunEnergy;
            }
            return;
        }
        // The circus ring replaces the grey test room.
        _circus = new CircusArena();
        AddChild(_circus);
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
        if (_scrolling)
        {
            Arena.Current.Band = (StageScenery.BandFar, StageScenery.BandNear);
            Arena.Current.CameraX = _camX;
        }
    }

    // -------------------------------------------------------------- frames

    public override void _PhysicsProcess(double delta)
    {
        GameClock.Advance(delta);
        if (_scrolling) FollowCamera((float)delta);
        TickRespawns();
        if (_outcomeProbe.Length > 0) TickOutcomeProbe();
    }

    public override void _Process(double delta)
    {
        if (_aiProbe) { TickAiProbe((float)delta); return; }
        if (_hud is null) return;   // probe modes that build no HUD

        PollForJoins((float)delta);
        PollPause();
        PollForRetry();
        TickSecretCurtain();
        PollStatTent();
        PollForRestart();
        UpdateHud();
    }

}
