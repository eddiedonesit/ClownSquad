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
    private string _outcomeProbe = "";   // "win" or "lose" when a wave probe is running

    // ------------------------------------------------------------- startup

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        bool Has(string flag) => Array.IndexOf(args, flag) >= 0;

        // The HD art is the default; the original sheets are kept and selectable.
        ClownSquad.Visuals.ArtStyle.Classic = Has("classic");

        // Tests run against a throwaway save, so they never read or touch a real one.
        bool probe = Has("animtest") || Has("movetest") || Has("aitest") || Has("wavetest") || Has("losetest") || Has("tentshot");
        if (probe)
        {
            SaveSystem.Path = "user://probe_save.json";
            SaveSystem.DeleteForTests();
        }
        else
        {
            SaveSystem.UseActiveSlot();
            SaveSystem.Load();
        }

        // A stage chosen on the world map, or named on the command line
        // ("-- stage 2-3"). With neither, and no test or sandbox asked for, the
        // game opens on the world map.
        _stage = Campaign.Pending;
        int stageArg = Array.IndexOf(args, "stage");
        if (stageArg >= 0 && stageArg + 1 < args.Length) _stage = Campaign.Get(args[stageArg + 1]);
        Campaign.Pending = null;
        // Fights on the map are walked through left to right; bonus stages stay in the ring.
        _scrolling = _stage is StageDef sc && Campaign.IsFight(sc) && sc.Kind != StageKind.Bonus;
        if (!probe && !Has("sandbox") && !Has("fight") && _stage is null)
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

        _aiProbe = Has("aitest");
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
        if (_stage is StageDef st && Campaign.IsFight(st))
        {
            _director.Waves = st.Waves.Length > 0 ? st.Waves : _director.Waves;
            _director.LevelFloor = st.EnemyLevel;
            _director.RankBoost = st.RankBoost;
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
        AddChild(_director);
        _director.PhaseChanged += OnPhaseChanged;
        _director.EnemyDefeated += OnEnemyDefeated;
        _director.HostLevel = () => _players.Count > 0 && _players[0].Record is CharacterRecord host ? host.Level : 1;
        _lootRng.Seed = _director.Seed ^ 0x5eed10075UL;
        _director.Begin(this);

        if (_outcomeProbe.Length > 0) StartOutcomeProbe();
        if (Has("tentshot")) StartTentShot();
    }

    /// <summary>"-- tentshot": a levelled character, a HUD still, then the Stat Tent open. Needs a window.</summary>
    private void StartTentShot()
    {
        PlayerFighter p = _players[0];
        Leveling.Grant(p.Record!, 200);
        // A few banked items so the GEAR page has something to show.
        for (int i = 0; i < 7; i++)
        {
            ItemData item = Loot.Roll(_lootRng, 3, 2.5f);
            p.Record!.Stash.Add(item);
            if (p.Record.EquippedIn(item.Slot) is not ItemData worn || Loot.Score(item) > Loot.Score(worn))
                p.Record.Equip(item.Slot, item);
        }
        p.ApplyProgression(p.Record!);
        SceneTreeTimer first = GetTree().CreateTimer(3.0, processAlways: true);
        first.Timeout += () =>
        {
            GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/hud_level.png");
            _tent!.Open();
            GetTree().CreateTimer(0.6, processAlways: true).Timeout += () =>
            {
                GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/stat_tent.png");
                _tent.ShowPage(1);
                GetTree().CreateTimer(0.4, processAlways: true).Timeout += () =>
                {
                    GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/gear_page.png");
                    GetTree().Quit();
                };
            };
        };
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

    // ------------------------------------------------------------- players

    private void SpawnPlayers()
    {
        // The squad from the map: keyboard first, then any pads that joined
        // there. More pads can still join here on a button press rather than on
        // detection: one physical controller can appear as two devices, and
        // auto-binding both drives two fighters from one stick.
        for (int i = 0; i < Campaign.SquadDevices.Count; i++) AddPlayer(Campaign.SquadDevices[i], i);

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
                if (!Campaign.SquadDevices.Contains(pad)) Campaign.SquadDevices.Add(pad);
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
            DisplayName = Roster.DefaultFor(index).Name,
        };
        // Each player slot plays its saved character, created on first use,
        // looking like the clown chosen for it on the character select screen.
        CharacterRecord record = SaveSystem.CharacterFor(index, p.DisplayName);
        p.DisplayName = record.Name;
        p.Look = record.Look;
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
        if (_scrolling) home = new Vector3(_camX - 2f, 0f, home.Z);

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
        if (phase == WaveDirector.Phase.Intermission && d.WaveNumber == _secretWave && _secretCurtain is null)
            SpawnSecretCurtain();
        // The win probe forces Rare grunts for its first wave only: enough to be
        // sure of drops, without turning every later wave into a wall.
        if (_outcomeProbe == "win" && phase == WaveDirector.Phase.Intermission && d.WaveNumber >= 2) d.ForceRank = null;
        if (phase == WaveDirector.Phase.Victory)
        {
            CrowdCheer(6f, 20);
            BankLoot();
            if (_stage is StageDef cleared)
            {
                Campaign.MarkCleared(SaveSystem.Data.Campaign, cleared);
                SaveSystem.Data.Campaign.At = cleared.Id;
                SaveSystem.Save();
                GD.Print($"Stage {cleared.Id} cleared.");
            }
        }
        if (phase == WaveDirector.Phase.Defeat) LoseLoot();
        if (phase is WaveDirector.Phase.Victory or WaveDirector.Phase.Defeat)
        {
            _endedAt = GameClock.Seconds;
            bool windowed = !DisplayServer.GetName().Contains("headless");
            if (_stage is not null && (_outcomeProbe.Length == 0 || windowed))
                GetTree().CreateTimer(1.2).Timeout += () => ShowResults(phase == WaveDirector.Phase.Victory);
            // Win or lose, XP is kept (ADR 0006). Anyone with points to spend
            // is shown the Stat Tent once the ending has had a moment.
            SaveSystem.Save();
            foreach (PlayerFighter p in _players)
                if (p.Record is { StatPoints: > 0 }) _tentDueAt = GameClock.Seconds + 2.0;
        }
    }

    private void GoToMap() => GetTree().ChangeSceneToFile(Campaign.MapScene);

    // ------------------------------------------------------- scrolling stage

    /// <summary>
    /// The stage camera: lower and flatter than the ring's, so the scenery and
    /// sky behind the walkway are in view, Castle Crashers style. It rides a rig
    /// that slides along x; shake still moves the camera within the rig.
    /// </summary>
    private void MountStageCamera(Camera3D cam)
    {
        _rig = new Node3D { Name = "CameraRig" };
        AddChild(_rig);
        RemoveChild(cam);
        _rig.AddChild(cam);
        // Framed so the walkway fills the lower two thirds and the scenery the rest.
        cam.Position = new Vector3(0f, 3.8f, 9.6f);
        cam.RotationDegrees = new Vector3(-17f, 0f, 0f);
        cam.Fov = 35f;
    }

    /// <summary>Spread the stage's groups along the road, put the exit past the last, and fill in crates.</summary>
    private void ConfigureEncounters(StageDef st)
    {
        var at = new float[st.Waves.Length];
        for (int i = 0; i < at.Length; i++) at[i] = FirstEncounter + EncounterSpacing * i;
        _director!.EncounterX = at;
        _director.ExitX = at[^1] + 12f;
        _director.LeadX = () =>
        {
            float lead = float.MinValue;
            foreach (PlayerFighter p in _players) if (!p.IsDead) lead = Mathf.Max(lead, p.GlobalPosition.X);
            return lead == float.MinValue ? _camX : lead;
        };
        CallDeferred(MethodName.PlaceStageObjects);
    }

    private void PlaceStageObjects()
    {
        if (_scenery is null || _director?.EncounterX is not float[] at) return;
        _scenery.PlaceCrates(this, at, _director.ExitX);
        _scenery.PlaceExit(_director.ExitX);
    }

    /// <summary>
    /// The camera follows the furthest player right and never backs up, the
    /// rule of the genre; while a group is being fought it holds at that group.
    /// </summary>
    private void FollowCamera(float dt)
    {
        if (_director is null || _rig is null) return;
        float lead = _director.LeadX();
        float target = Mathf.Max(_camX, lead - 1.0f);
        if (_director.CameraLock is float hold) target = hold;
        target = Mathf.Min(target, _director.ExitX + 1f);
        _camX = Mathf.MoveToward(_camX, target, 9f * dt);
        _rig.Position = new Vector3(_camX, 0f, 0f);
        if (Arena.Current is Arena arena) arena.CameraX = _camX;
        _scenery?.Follow(_camX);
    }

    // ------------------------------------------------------------ secret shop

    /// <summary>Chance a stage has the secret shop somewhere in it.</summary>
    private const float SecretShopChance = 0.07f;

    /// <summary>A little velvet booth appears in the ring between waves. Walk into it to shop.</summary>
    private void SpawnSecretCurtain()
    {
        const float px = 0.0105f;
        var tex = GD.Load<Texture2D>("res://assets/arena/secret_curtain.png");
        var node = new Node3D();
        AddChild(node);
        float x = _scrolling ? _camX + (float)GD.RandRange(1.5, 4.5) : (float)GD.RandRange(-3.5, 3.5);
        node.GlobalPosition = new Vector3(x, 0f, -3.2f);
        var sprite = new Sprite3D
        {
            Texture = tex, PixelSize = px, Position = new Vector3(0f, tex.GetHeight() * px * 0.5f, 0f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
        };
        if (LitSprite.For("res://assets/arena/secret_curtain.png") is ShaderMaterial lit)
        {
            LitSprite.Apply(lit, Colors.White, 0f, false);
            sprite.MaterialOverride = lit;
        }
        node.AddChild(sprite);
        node.AddChild(new OmniLight3D { LightColor = new Color(0.8f, 0.4f, 1f), LightEnergy = 3f, OmniRange = 3f, Position = new Vector3(0f, 0.8f, 0.6f) });
        _secretCurtain = node;
        PopText.Spawn(this, node.GlobalPosition + new Vector3(0f, 1.8f, 0f), "SECRET SHOP!?", new Color(0.85f, 0.55f, 1f), 1.2f, 1.2f);
        ClownSquad.Audio.Sfx.Play("ding", 0.6f);
        ClownSquad.Audio.Sfx.Play("kazoo_rise", 1.3f, -6f);
        GD.Print("The secret shop has appeared.");
        if (_outcomeProbe.Length > 0 && !DisplayServer.GetName().Contains("headless"))
            GetTree().CreateTimer(0.8).Timeout += () =>
                GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/secret_shop.png");
    }

    private void TickSecretCurtain()
    {
        if (_secretCurtain is null || !IsInstanceValid(_secretCurtain) || _shop is not null) return;
        foreach (PlayerFighter p in _players)
        {
            if (p.IsDead) continue;
            Vector3 d = p.GlobalPosition - _secretCurtain.GlobalPosition;
            if (new Vector2(d.X, d.Z).Length() > 0.9f) continue;
            OpenSecretShop();
            return;
        }
    }

    private void OpenSecretShop()
    {
        int level = Mathf.Max(_director?.LevelFloor ?? 1, _players[0].Record?.Level ?? 1) + 3;
        // Better than any world shop: higher level, rarity pushed hard.
        var shelf = ShopMenu.Stock(_director!.Seed ^ 0xb0057UL, level, 5, 4f);
        var shoppers = new List<Seat>();
        foreach (PlayerFighter p in _players)
            if (p.Record is CharacterRecord r && p.Source is LocalDeviceInput d)
                shoppers.Add(new Seat(r, d.Device, () => p.ApplyProgression(r)));
        _shop = new ShopMenu("THE SECRET SHOP", shelf, shoppers);
        _shop.Closed += () =>
        {
            if (_secretCurtain is not null && IsInstanceValid(_secretCurtain)) _secretCurtain.QueueFree();
            ClownSquad.Audio.Sfx.Play("pop");
        };
        AddChild(_shop);
    }

    private void CrowdCheer(float seconds, int flashes)
    {
        if (_circus is null) return;
        _circus.Cheer(seconds, flashes);
        if (seconds >= 1.3f) ClownSquad.Audio.Sfx.Play("crowd_cheer", 1f, seconds >= 3f ? 0f : -5f);
    }

    // --------------------------------------------------------- progression

    /// <summary>
    /// Every player earns the full XP for every enemy, fallen players included:
    /// no kill stealing in co-op, and nobody is left behind for dying.
    /// </summary>
    private void OnEnemyDefeated(Fighter enemy)
    {
        _kos++;
        bool boss = enemy is BossClown;
        var foe = enemy as PlayerFighter;
        int level = foe?.EnemyLevel ?? 1;
        EnemyRank rank = foe?.Rank ?? EnemyRank.Common;
        int xp = (int)Mathf.Round((boss ? BossXp : GruntXp) * Leveling.EnemyXpScale(level) * Leveling.RankXp(rank));
        CrowdCheer(boss ? 4f : rank != EnemyRank.Common ? 2f : 0.9f, boss ? 14 : 1 + (int)rank * 3);
        CameraShake.RequestPunch(this, boss ? 1.3f : 0.35f + 0.25f * (int)rank);
        PopText.Spawn(this, enemy.GlobalPosition + new Vector3(0f, enemy.BodyHeight + 0.2f, 0f),
                      $"+{xp} XP", new Color(1f, 0.85f, 0.3f), boss ? 1.3f : 0.8f);

        // Gear that heals on a knockout pays the one who landed it.
        if (enemy.LastAttacker is PlayerFighter killer && killer.Record is CharacterRecord kr && !killer.IsDead)
        {
            float heal = Leveling.LifeOnKill(kr);
            if (heal > 0f) killer.Heal(heal);
        }

        RollDrops(enemy, boss, rank, level);
        PayGold(enemy, boss, rank, level);

        foreach (PlayerFighter p in _players)
        {
            if (p.Record is not CharacterRecord r) continue;
            if (Leveling.Grant(r, (int)Mathf.Round(xp * Leveling.XpMultiplier(r))) == 0) continue;

            // The probe's AI spends its points the way a player would, or the
            // enemies, which scale with level, would outgrow it.
            if (_outcomeProbe == "win")
                while (r.StatPoints > 0) { r.StatPoints--; if (r.StatPoints % 2 == 0) r.Pow++; else r.Guts++; }

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

    // ---------------------------------------------------------------- loot

    /// <summary>
    /// Drops on a knockout. Better enemies drop more often and better (GDD 6.3):
    /// a common grunt one time in five, ranked grunts nearly always, the boss
    /// twice, and the wild legendary twice with the rarity weights pushed hard.
    /// The host's LUCK and FORTUNE gear push them further.
    /// </summary>
    private void RollDrops(Fighter enemy, bool boss, EnemyRank rank, int level)
    {
        float boost = _players.Count > 0 && _players[0].Record is CharacterRecord host ? Leveling.LootBoost(host) : 0f;
        if (_director?.IsBonus == true)
        {
            // Pinatas: lots of gold, and now and then a prize inside.
            if (_lootRng.Randf() < 0.3f) SpawnDrop(Loot.Roll(_lootRng, level, 0.5f + boost), enemy.GlobalPosition);
            return;
        }
        (float chance, int count, float push) = boss ? (1f, 2, 1.5f) : rank switch
        {
            EnemyRank.Rare => (0.75f, 1, 0.6f),
            EnemyRank.Epic => (1f, 1, 1.2f),
            EnemyRank.Legendary => (1f, 2, 3f),
            _ => (0.2f, 1, 0f),
        };
        if (_lootRng.Randf() > chance) return;
        for (int i = 0; i < count; i++)
            SpawnDrop(Loot.Roll(_lootRng, level, push + boost), enemy.GlobalPosition);
    }

    private void SpawnDrop(ItemData item, Vector3 at)
    {
        var toss = new Vector3(_lootRng.RandfRange(-2.2f, 2.2f), _lootRng.RandfRange(5f, 6.5f), _lootRng.RandfRange(-1.2f, 1.2f));
        var drop = new LootDrop(item, toss);
        AddChild(drop);
        drop.GlobalPosition = at + new Vector3(0f, 0.6f, 0f);
        drop.PickedUp += OnPickedUp;
        _drops.Add(drop);
        _dropsSpawned++;
        if (item.Rarity >= ItemRarity.Rare) ClownSquad.Audio.Sfx.Play("ding", 1.3f + 0.1f * (int)item.Rarity);
        GD.Print($"Drop: {item.Rarity} {item.Name} (level {item.Level}).");
    }

    private void OnPickedUp(LootDrop drop, PlayerFighter p)
    {
        _drops.Remove(drop);
        ItemData item = drop.Item;
        // Swept items were already counted into a bag at the stage clear.
        if (!_swept.Contains(item.Id)) { Bag(p).Add(item); _itemsFound++; }
        PopText.Spawn(this, p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.25f, 0f), item.Name,
                      Loot.RarityColor(item.Rarity), 0.7f, 0.9f);
        ClownSquad.Audio.Sfx.Play("coin", 0.9f + 0.08f * (int)item.Rarity);
    }

    /// <summary>Gold for a knockout, to whoever landed it, into the stage bag like loot.</summary>
    private void PayGold(Fighter enemy, bool boss, EnemyRank rank, int level)
    {
        PlayerFighter? to = enemy.LastAttacker as PlayerFighter ?? (_players.Count > 0 ? _players[0] : null);
        if (to is null || to.Hostile) return;
        float amount = (3f + level * 1.2f) * Leveling.RankXp(rank) * (boss ? 10f : 1f) * _lootRng.RandfRange(0.8f, 1.2f);
        if (_director?.IsBonus == true) amount *= 3f;
        int gold = Mathf.Max(1, (int)Mathf.Round(amount));
        _goldEarned += gold;
        _bagGold[to] = (_bagGold.TryGetValue(to, out int had) ? had : 0) + gold;
        PopText.Spawn(this, enemy.GlobalPosition + new Vector3(0.4f, enemy.BodyHeight, 0f), $"+{gold} G",
                      new Color(1f, 0.9f, 0.4f), 0.7f, 0.8f);
        ClownSquad.Audio.Sfx.Play("coin", 1.3f, -8f);
    }

    public string BagText(PlayerFighter p)
    {
        int items = BagCount(p);
        int gold = _bagGold.TryGetValue(p, out int g) ? g : 0;
        if (items == 0 && gold == 0) return "";
        return items > 0 ? $"LOOT {items}  {gold}G" : $"{gold}G";
    }

    private List<ItemData> Bag(PlayerFighter p)
    {
        if (!_bags.TryGetValue(p, out List<ItemData>? bag)) _bags[p] = bag = new List<ItemData>();
        return bag;
    }

    public int BagCount(PlayerFighter p) => _bags.TryGetValue(p, out List<ItemData>? bag) ? bag.Count : 0;

    /// <summary>
    /// Stage cleared: anything still on the floor flies to the nearest player,
    /// then every bag is banked to its character. Empty slots are filled, and an
    /// item that scores better than what is worn is put on.
    /// </summary>
    private void BankLoot()
    {
        foreach (LootDrop drop in _drops.ToArray())
        {
            if (!IsInstanceValid(drop)) continue;
            PlayerFighter? nearest = null;
            float best = float.MaxValue;
            foreach (PlayerFighter p in _players)
            {
                float d = p.GlobalPosition.DistanceTo(drop.GlobalPosition);
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest is null) continue;
            Bag(nearest).Add(drop.Item);
            _itemsFound++;
            _swept.Add(drop.Item.Id);
            drop.SendTo(nearest);
        }

        foreach (PlayerFighter p in _players)
        {
            if (p.Record is not CharacterRecord r) continue;
            if (_bagGold.TryGetValue(p, out int gold) && gold > 0)
            {
                r.Gold += gold;
                GD.Print($"{r.Name} banked {gold} gold ({r.Gold} total).");
            }
            _bagGold[p] = 0;
            List<ItemData> bag = Bag(p);
            if (bag.Count == 0) continue;
            foreach (ItemData item in bag)
            {
                r.Stash.Add(item);
                ItemData? worn = r.EquippedIn(item.Slot);
                if (worn is null || Loot.Score(item) > Loot.Score(worn)) r.Equip(item.Slot, item);
            }
            _itemsBanked += bag.Count;
            p.ApplyProgression(r);
            PopText.Spawn(this, p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.6f, 0f),
                          $"LOOT BANKED x{bag.Count}", new Color(1f, 0.85f, 0.3f), 1.1f, 1.2f);
            GD.Print($"{r.Name} banked {bag.Count} item(s); wearing {r.Equipped.Count}, stash {r.Stash.Count}.");
            bag.Clear();
        }
        SaveSystem.Save();
    }

    /// <summary>Stage failed: XP stays, the bag is lost (ADR 0006).</summary>
    private void LoseLoot()
    {
        foreach (PlayerFighter p in _players)
        {
            int lost = BagCount(p);
            Bag(p).Clear();
            _bagGold[p] = 0;
            if (lost == 0) continue;
            PopText.Spawn(this, p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.6f, 0f),
                          $"LOOT LOST x{lost}", new Color(0.7f, 0.7f, 0.75f), 1.0f, 1.0f);
            GD.Print($"{p.DisplayName} lost {lost} item(s) with the stage.");
        }
        foreach (LootDrop drop in _drops)
            if (IsInstanceValid(drop)) drop.QueueFree();
        _drops.Clear();
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
        if (!fresh) return;
        // A campaign stage returns to the map, won or lost; the free fight replays.
        if (_stage is not null) GoToMap();
        else GetTree().ReloadCurrentScene();
    }

    /// <summary>After a campaign stage, DODGE (K / B) plays it again from the start.</summary>
    private void PollForRetry()
    {
        if (_stage is null || _endedAt < 0 || GameClock.Seconds - _endedAt < 1.5 || _outcomeProbe.Length > 0) return;
        if (_tent is not null && (_tent.IsOpen || _tent.MsSinceClosed < 400 || _tentDueAt >= 0)) return;
        bool held = Godot.Input.IsKeyPressed(Key.K);
        foreach (int pad in Godot.Input.GetConnectedJoypads()) held |= Godot.Input.IsJoyButtonPressed(pad, JoyButton.B);
        bool fresh = held && !_retryHeld;
        _retryHeld = held;
        if (fresh) Retry();
    }

    private void Retry()
    {
        SaveSystem.Save();
        Campaign.Pending = _stage;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>Esc or Start pauses: resume, the Stat Tent, retry, or the map.</summary>
    private void PollPause()
    {
        if (_pause is not null || _shop is not null || (_tent?.IsOpen ?? false) || _outcomeProbe.Length > 0) return;
        var devices = new List<int>();
        foreach (PlayerFighter p in _players) if (p.Source is LocalDeviceInput d) devices.Add(d.Device);
        bool held = PauseMenu.PauseHeld(devices);
        bool fresh = held && !_pauseHeld;
        _pauseHeld = held;
        if (!fresh) return;
        var items = new List<(PauseMenu.Choice, string)> { (PauseMenu.Choice.Resume, "RESUME"), (PauseMenu.Choice.Stats, "STAT TENT") };
        if (_stage is not null) items.Add((PauseMenu.Choice.Retry, "RETRY STAGE"));
        items.Add((PauseMenu.Choice.Map, "BACK TO THE MAP"));
        _pause = new PauseMenu(items, devices, choice =>
        {
            _pause = null;
            _pauseHeld = true;
            switch (choice)
            {
                case PauseMenu.Choice.Stats: _tent?.Open(); break;
                case PauseMenu.Choice.Retry: Retry(); break;
                case PauseMenu.Choice.Map: SaveSystem.Save(); GoToMap(); break;
            }
        });
        AddChild(_pause);
    }

    /// <summary>The stage's report card, with what was won (or lost) and what to do next.</summary>
    private void ShowResults(bool won)
    {
        if (_results is not null || !IsInsideTree()) return;
        double secs = GameClock.Seconds;
        var layer = new CanvasLayer { Layer = 8 };
        AddChild(layer);
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Position = new Vector2(-210, -40);
        panel.CustomMinimumSize = new Vector2(420, 0);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.12f, 0.3f, 0.93f), BorderColor = won ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.6f, 0.7f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 14, ContentMarginBottom = 16,
        });
        layer.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);
        LabelSettings S(int size, Color c) => new()
        {
            Font = UiFont.Display, FontSize = size, FontColor = c, OutlineSize = 5, OutlineColor = new Color(0.08f, 0.04f, 0.1f),
        };
        void Row(string a, string b)
        {
            var h = new HBoxContainer();
            h.AddChild(new Label { Text = a, LabelSettings = S(18, new Color(0.85f, 0.8f, 0.95f)), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            h.AddChild(new Label { Text = b, LabelSettings = S(18, Colors.White) });
            box.AddChild(h);
        }
        Row("TIME", $"{(int)secs / 60}:{(int)secs % 60:00}");
        Row("KNOCKOUTS", _kos.ToString());
        Row("BEST COMBO", $"{_bestCombo} HITS");
        Row(won ? "GOLD BANKED" : "GOLD LOST", $"{_goldEarned} G");
        Row(won ? "LOOT BANKED" : "LOOT LOST", _itemsFound.ToString());
        box.AddChild(new Label
        {
            Text = won ? "ATTACK  back to the map      DODGE  play again" : "ATTACK  back to the map      DODGE  try again",
            HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = S(14, new Color(1f, 0.85f, 0.3f)),
        });
        _results = panel;
        panel.Scale = new Vector2(0.6f, 0.6f);
        panel.PivotOffset = new Vector2(210, 80);
        CreateTween().TweenProperty(panel, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        if (_outcomeProbe.Length > 0)
            GetTree().CreateTimer(0.5).Timeout += () => GetViewport().GetTexture().GetImage().SavePng("C:/Claude/ClownSquad/debug/results.png");
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
        // Big hits lean the camera in: finishers and crits, harder for the biggest.
        if (amount >= 20f) CameraShake.RequestPunch(this, Mathf.Min(1f, amount / 45f));
        _comboHits++;
        _bestCombo = Mathf.Max(_bestCombo, _comboHits);
        _comboExpires = GameClock.Seconds + ComboLull;
        // The crowd loves a long combo.
        if (_comboHits % 5 == 0) CrowdCheer(1.4f + _comboHits * 0.04f, 2 + _comboHits / 5);
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
            () => _endedAt >= 0 && GameClock.Seconds - _endedAt > 1.5)
        {
            BagText = BagText,
            StageLabel = _stage is StageDef s ? (s.Kind == StageKind.Boss ? $"{s.World}-BOSS  " : $"{s.Id}  ") : "",
            ReplayText = _stage is not null ? "" : "press ATTACK to play again",
        };
        AddChild(_clownHud);

        _tent = new StatTent(() =>
        {
            var seats = new List<Seat>();
            foreach (PlayerFighter p in _players)
                if (p.Record is CharacterRecord r && p.Source is LocalDeviceInput d)
                    seats.Add(new Seat(r, d.Device, () => p.ApplyProgression(r)));
            return seats;
        });
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
        if (_outcomeProbe != "win")
        {
            // A loss must cost the bag: start the losing run already carrying an item.
            Bag(_players[0]).Add(Loot.Roll(_lootRng, 1, 0f));
            // On a scrolling stage nothing comes to a player who stands still,
            // so the idle player starts where the first group waits.
            if (_scrolling) { _camX = FirstEncounter; _players[0].GlobalPosition = new Vector3(FirstEncounter, 0f, 0f); }
            return;
        }

        PlayerFighter p = _players[0];
        p.Source = new BrawlerAi(p, attackRange: 1.7f, backoffRange: 0.8f, attackCooldownMs: 350) { AdvanceWhenIdle = true };
        p.ExtraDamageMultiplier = 2.5f;
        // Every grunt ranked Rare, so the run is certain to drop loot and to
        // exercise the ranked-enemy path.
        _director!.ForceRank = EnemyRank.Rare;
    }

    /// <summary>
    /// After a won stage: the player has levelled, a spent point changes the
    /// fighter, and what was saved reads back from disk identical.
    /// </summary>
    private bool CheckProgression()
    {
        PlayerFighter p = _players[0];
        CharacterRecord r = p.Record!;
        bool levelled = r.Level >= 2 && r.Pow + r.Guts + r.StatPoints == Leveling.StatPointsPerLevel * (r.Level - 1);
        r.StatPoints++;   // one point back to spend below

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

    /// <summary>
    /// After a won stage: drops fell, everything was banked, the bank survived
    /// a save and reload, and enemies were scaled to the host.
    /// </summary>
    private bool CheckLoot()
    {
        CharacterRecord r = SaveSystem.Data.Characters[0];
        int owned = r.Stash.Count + r.Equipped.Count;
        bool dropped = _dropsSpawned > 0;
        bool banked = _itemsBanked == _dropsSpawned && owned == _itemsBanked;
        bool worn = r.Equipped.Count > 0;
        GD.Print($"  loot: {_dropsSpawned} dropped, {_itemsBanked} banked, {owned} owned after reload, {r.Equipped.Count} worn; " +
                 $"dropped={dropped} banked={banked} worn={worn}");
        return dropped && banked && worn;
    }

    /// <summary>
    /// After a won campaign stage: it is saved as cleared, what it leads to is
    /// open, gold was banked, a shop sells for the right price, and a forced
    /// secret shop really appeared. Passes trivially for the free fight.
    /// </summary>
    private bool CheckCampaign()
    {
        if (_stage is not StageDef st) return true;
        CampaignState c = SaveSystem.Data.Campaign;
        CharacterRecord r = SaveSystem.Data.Characters[0];
        bool cleared = c.Cleared.Contains(st.Id);
        bool opened = true;
        foreach (StageDef next in Campaign.Neighbours(st))
            if (System.Array.IndexOf(next.Requires, st.Id) >= 0) opened &= Campaign.Unlocked(next, c);
        bool paid = r.Gold > 0;

        int before = r.Gold + 100000;
        r.Gold = before;
        var shelf = ShopMenu.Stock(42, st.EnemyLevel, 3, 1f);
        (ItemData item, int price) = shelf[0];
        bool bought = ShopMenu.TryBuy(r, shelf, 0, out _) && r.Gold == before - price && (r.Stash.Contains(item) || r.Equipped.Contains(item));

        bool secret = Array.IndexOf(OS.GetCmdlineUserArgs(), "secret") < 0 || _secretCurtain is not null;
        GD.Print($"  campaign: {st.Id} cleared={cleared} next open={opened} gold banked={paid} shop buy={bought} secret shop={secret}");
        return cleared && opened && paid && bought && secret;
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
        if (_outcomeProbe == "win") ok &= _director!.IsBonus ? CheckCampaign() : CheckProgression() & CheckLoot() & CheckCampaign();
        if (_outcomeProbe == "lose")
        {
            CharacterRecord r = _players[0].Record!;
            bool lost = BagCount(_players[0]) == 0 && r.Stash.Count == 0 && r.Equipped.Count == 0;
            GD.Print($"  loot on a loss: bag {BagCount(_players[0])}, stash {r.Stash.Count}, worn {r.Equipped.Count} -> lost={lost}");
            ok &= lost;
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
