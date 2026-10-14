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
    private Label? _hud;

    public override void _Ready()
    {
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
        p.GlobalPosition = new Vector3(-1.5f + index * 3f, 0f, 2f);
        _players.Add(p);
    }

    private void SpawnDummies()
    {
        foreach (Vector3 pos in new[] { new Vector3(-2f, 0f, -2.5f), new Vector3(2.5f, 0f, -1.5f) })
        {
            var d = new TrainingDummy { Name = "Dummy" };
            AddChild(d);
            d.GlobalPosition = pos;
            _dummies.Add(d);
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

    public override void _Process(double delta)
    {
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
        _hud.Text = sb.ToString();
    }
}
