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
    private Label _hud = null!;

    public override void _Ready()
    {
        SpawnPlayers();
        SpawnDummies();
        BuildHud();
        Godot.Input.JoyConnectionChanged += OnJoyConnectionChanged;
    }

    private void SpawnPlayers()
    {
        var devices = new List<int> { LocalDeviceInput.Keyboard };
        foreach (int pad in Godot.Input.GetConnectedJoypads())
            devices.Add(pad);

        for (int i = 0; i < devices.Count; i++)
            AddPlayer(devices[i], i);

        GD.Print($"Spawned {devices.Count} player(s). Gamepads detected: {Godot.Input.GetConnectedJoypads().Count}");
    }

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

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        if (!connected) return;
        GD.Print($"Gamepad {device} connected; binding a new fighter.");
        AddPlayer((int)device, _players.Count);
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
        var sb = new StringBuilder();
        sb.AppendLine("CLOWN SQUAD - combat test");
        sb.AppendLine("Keyboard: WASD move | J or Space attack | K or Shift dodge");
        sb.AppendLine("Gamepad:  stick move | X or A attack | B or RB dodge");
        sb.AppendLine();
        foreach (PlayerFighter p in _players)
            sb.AppendLine($"P{p.PlayerIndex + 1} [{p.Source.Describe()}]  hp {p.Health:F0}  {p.DebugState}");
        sb.AppendLine();
        for (int i = 0; i < _dummies.Count; i++)
            sb.AppendLine($"Dummy {i + 1}  hp {_dummies[i].Health:F0} / {_dummies[i].MaxHealth:F0}");
        _hud.Text = sb.ToString();
    }
}
