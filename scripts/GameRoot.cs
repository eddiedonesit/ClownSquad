namespace ClownSquad;

using System.Collections.Generic;
using System.Text;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Input;

/// <summary>
/// Combat test bootstrap. Binds every detected device to its own fighter, which
/// is local co-op exercising the full multi-player path with no networking.
/// </summary>
public partial class GameRoot : Node3D
{
    private readonly List<PlayerFighter> _players = new();
    /// <summary>Devices already driving a fighter, so a pad is never bound twice.</summary>
    private readonly HashSet<int> _boundDevices = new();
    private readonly List<TrainingDummy> _dummies = new();
    private readonly List<BossClown> _bosses = new();
    private Label? _hud;
    private bool _aiProbe;

    /// <summary>Fighters waiting to respawn, with the time left and where to go.</summary>
    private readonly List<(Fighter fighter, Vector3 at, double at_time)> _pendingRespawns = new();
    private double _clock;

    public override void _Ready()
    {
        _aiProbe = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "aitest") >= 0;

        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "animtest") >= 0)
        {
            AddChild(new ClownSquad.Debug.AnimationProbe());
            return;
        }

        if (GetNodeOrNull<Camera3D>("Camera3D") is Camera3D cam)
            cam.AddChild(new ClownSquad.Visuals.CameraShake());

        SpawnPlayers();
        SpawnDummies();
        BuildHud();
    }

    private void SpawnPlayers()
    {
        // Keyboard always joins. Pads join on a button press rather than on
        // detection: tools like DS4Windows expose one physical controller as two
        // devices, and auto-binding both drives two fighters from one stick.
        AddPlayer(LocalDeviceInput.Keyboard, 0);

        foreach (int pad in Godot.Input.GetConnectedJoypads())
            GD.Print($"  pad {pad}: name=\"{Godot.Input.GetJoyName(pad)}\" guid={Godot.Input.GetJoyGuid(pad)}");
        GD.Print("Press A / X on a controller to join.");
    }

    private float _joinCooldown;

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

    private readonly HashSet<int> _joinHeld = new();

    private void AddPlayer(int device, int index)
    {
        // Godot raises JoyConnectionChanged for pads that were already present
        // at startup, so without this guard one pad drives two fighters.
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
        };
        AddChild(p);
        Vector3 home = new(-1.5f + index * 3f, 0f, 2f);
        p.GlobalPosition = home;
        p.Died += f => ScheduleRespawn(f, home, 1.5);
        _players.Add(p);
    }

    private void SpawnDummies()
    {
        var d = new TrainingDummy { Name = "Dummy" };
        AddChild(d);
        d.GlobalPosition = new Vector3(-3.5f, 0f, -2.5f);
        _dummies.Add(d);

        SpawnBoss(new Vector3(3.5f, 0f, -2f));
    }

    private void SpawnBoss(Vector3 at)
    {
        var boss = new BossClown { Name = "BossClown" };
        boss.Source = new BrawlerAi(boss, attackRange: 2.2f, backoffRange: 1.2f,
                                    attackCooldownMs: 700,
                                    specialCooldownMs: 5000, specialRange: 6.0f);
        AddChild(boss);
        boss.GlobalPosition = at;
        boss.Died += f => ScheduleRespawn(f, at, 3.0);
        _bosses.Add(boss);
        GD.Print("Boss clown spawned, driven by BrawlerAi.");
    }

    /// <summary>
    /// Death is a respawn in the test room. Without it a downed fighter stops
    /// being a valid AI target, and enemies stand idle forever with nothing to
    /// chase - which looks exactly like the AI having broken.
    /// </summary>
    private void ScheduleRespawn(Fighter f, Vector3 at, double delay)
    {
        GD.Print($"{f.Name} down; respawning in {delay:F1}s.");
        _pendingRespawns.Add((f, at, _clock + delay));
    }

    private void TickRespawns(double delta)
    {
        _clock += delta;
        for (int i = _pendingRespawns.Count - 1; i >= 0; i--)
        {
            (Fighter f, Vector3 at, double due) = _pendingRespawns[i];
            if (_clock < due) continue;
            _pendingRespawns.RemoveAt(i);
            if (GodotObject.IsInstanceValid(f))
            {
                f.Revive(at);
                GD.Print($"{f.Name} respawned.");
            }
        }
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Label
        {
            Position = new Vector2(16, 12),
            Theme = new Theme(),
        };
        _hud.AddThemeFontSizeOverride("font_size", 15);
        _hud.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _hud.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        _hud.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(_hud);
    }

    private float _aiProbeTime;
    private int _aiProbeTick;

    /// <summary>
    /// Headless assertion that the AI closes distance and swings. Run with:
    ///   godot --headless --path . -- aitest
    /// </summary>
    private void TickAiProbe(float dt)
    {
        _aiProbeTime += dt;
        if (_aiProbeTime < _aiProbeTick * 0.6f) return;
        if (_players.Count == 0 || _bosses.Count == 0) return;

        BossClown b = _bosses[0];
        PlayerFighter p = _players[0];
        float d = b.GlobalPosition.DistanceTo(p.GlobalPosition);
        GD.Print($"  t={_aiProbeTime:F1}  dist={d:F2}  boss={b.DebugState} clip={b.CurrentClip}  playerHp={p.Health:F0}");

        if (!DisplayServer.GetName().Contains("headless") && _aiProbeTick % 3 == 0)
        {
            Image img = GetViewport().GetTexture().GetImage();
            img.SavePng($"C:/Claude/ClownSquad/debug/ai_{_aiProbeTick}.png");
        }

        if (++_aiProbeTick > 26)
        {
            GD.Print("=== AI PROBE DONE ===");
            GetTree().Quit();
        }
    }

    public override void _Process(double delta)
    {
        TickRespawns(delta);
        if (_aiProbe) { TickAiProbe((float)delta); return; }
        if (_hud is null) return;   // probe mode builds no HUD

        PollForJoins((float)delta);

        var sb = new StringBuilder();
        sb.AppendLine("CLOWN SQUAD - combat test");
        sb.AppendLine("Keyboard: WASD move | J attack | Space jump | K or Shift dodge");
        sb.AppendLine("Gamepad:  stick move | X attack | A jump | B or RB dodge");
        sb.AppendLine("Unjoined pad? Press A / X to join.");
        sb.AppendLine();
        foreach (PlayerFighter p in _players)
            sb.AppendLine($"P{p.PlayerIndex + 1} [{p.Source.Describe()}]  hp {p.Health:F0}  {p.DebugState}  clip={p.CurrentClip}");
        sb.AppendLine();
        for (int i = 0; i < _dummies.Count; i++)
            sb.AppendLine($"Dummy {i + 1}  hp {_dummies[i].Health:F0} / {_dummies[i].MaxHealth:F0}");
        foreach (BossClown b in _bosses)
            sb.AppendLine($"BOSS CLOWN  hp {b.Health:F0} / {b.MaxHealth:F0}  {b.DebugState}  clip={b.CurrentClip}");
        _hud.Text = sb.ToString();
    }
}
