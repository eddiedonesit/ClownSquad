namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Input;

/// <summary>
/// The pause menu: Esc on the keyboard, Start on a pad. Freezes the fight and
/// offers the ways out a player reaches for: carry on, spend points, start the
/// stage again, or go back to the map. Any controller in the squad can drive it.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    public enum Choice { Resume, Stats, Inventory, Retry, Map, Title, Quit }

    private readonly List<(Choice choice, string label)> _items;
    private readonly List<int> _devices;
    private readonly Action<Choice> _chosen;
    private readonly List<ClownSquad.Map.MapPad> _pads = new();
    private readonly List<Label> _labels = new();
    private int _cursor;
    private bool _closeHeld = true;

    public PauseMenu(IEnumerable<(Choice, string)> items, List<int> devices, Action<Choice> chosen)
    {
        _devices = devices;
        _chosen = chosen;
        _items = new(items);
        Layer = 22;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Whether any squad controller is pressing pause right now.</summary>
    public static bool PauseHeld(IEnumerable<int> devices)
    {
        foreach (int d in devices)
            if (d == LocalDeviceInput.Keyboard ? Godot.Input.IsKeyPressed(Key.Escape) : Godot.Input.IsJoyButtonPressed(d, JoyButton.Start))
                return true;
        return false;
    }

    public override void _Ready()
    {
        GetTree().Paused = true;
        foreach (int d in _devices) _pads.Add(new ClownSquad.Map.MapPad(d));
        var shade = new ColorRect { Color = new Color(0.05f, 0.02f, 0.09f, 0.7f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Position = new Vector2(-180, -150);
        panel.CustomMinimumSize = new Vector2(360, 0);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.12f, 0.3f, 0.97f), BorderColor = new Color(1f, 0.85f, 0.3f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 16, ContentMarginBottom = 18,
        });
        AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        box.AddChild(new Label { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(40, new Color(1f, 0.85f, 0.3f), 8) });
        foreach ((_, string label) in _items)
        {
            var l = new Label { Text = label, HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(24, Colors.White, 6) };
            box.AddChild(l);
            _labels.Add(l);
        }
        box.AddChild(new Label { Text = "UP/DOWN  pick     ATTACK/JUMP  choose", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(13, new Color(0.75f, 0.7f, 0.85f), 3) });
        Refresh();
        Sfx.Play("pop", 0.8f);
    }

    private static LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline,
        OutlineColor = new Color(0.08f, 0.04f, 0.1f), ShadowSize = 2, ShadowColor = new Color(0, 0, 0, 0.45f), ShadowOffset = new Vector2(2, 3),
    };

    private void Refresh()
    {
        for (int i = 0; i < _labels.Count; i++)
        {
            _labels[i].Text = (i == _cursor ? "> " : "") + _items[i].label + (i == _cursor ? " <" : "");
            _labels[i].LabelSettings.FontColor = i == _cursor ? new Color(1f, 0.85f, 0.3f) : Colors.White;
        }
    }

    public override void _Process(double delta)
    {
        // Pause again (Esc / Start) is the same as resume.
        bool held = PauseHeld(_devices);
        if (held && !_closeHeld) { Pick(Choice.Resume); return; }
        _closeHeld = held;

        foreach (var pad in _pads)
        {
            switch (pad.Poll())
            {
                case ClownSquad.Map.MapPad.Press.Up: _cursor = (_cursor + _items.Count - 1) % _items.Count; Sfx.Play("blip", 1.1f); Refresh(); return;
                case ClownSquad.Map.MapPad.Press.Down: _cursor = (_cursor + 1) % _items.Count; Sfx.Play("blip"); Refresh(); return;
                case ClownSquad.Map.MapPad.Press.Enter: Pick(_items[_cursor].choice); return;
            }
        }
    }

    private void Pick(Choice c)
    {
        Sfx.Play("clack");
        GetTree().Paused = false;
        QueueFree();
        _chosen(c);
    }
}
