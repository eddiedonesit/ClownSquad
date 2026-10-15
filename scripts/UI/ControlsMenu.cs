namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Input;
using ClownSquad.Map;

/// <summary>
/// Settings > CONTROLS: every action with its keyboard key and gamepad button.
/// Up/down picks an action, left/right picks the keyboard or gamepad column,
/// ATTACK starts listening and the next key or button pressed is bound (Esc
/// cancels). A key already used elsewhere swaps over, so nothing is left
/// unbound. RESET DEFAULTS puts everything back. Saved as it changes.
/// </summary>
public partial class ControlsMenu : CanvasLayer
{
    private readonly List<MapPad> _pads = new();
    private readonly List<(Label name, Label key, Label pad)> _rows = new();
    private Label _note = null!;
    private int _cursor;
    private int _column;              // 0 keyboard, 1 gamepad
    private bool _listening;
    private ulong _listenFrom;

    public event Action? Closed;

    private int Count => Bindings.Listed.Length + 2;      // the actions, then RESET and BACK
    private int ResetRow => Bindings.Listed.Length;
    private int BackRow => Bindings.Listed.Length + 1;

    public ControlsMenu(IEnumerable<int> devices)
    {
        foreach (int d in devices) _pads.Add(new MapPad(d));
        Layer = 25;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        foreach (MapPad p in _pads) p.Swallow();
        var shade = new ColorRect { Color = new Color(0.05f, 0.02f, 0.09f, 0.9f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(760, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.12f, 0.3f, 0.97f), BorderColor = new Color(1f, 0.85f, 0.3f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 12, ContentMarginBottom = 14,
        });
        centre.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);
        box.AddChild(new Label { Text = "CONTROLS", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(34, new Color(1f, 0.85f, 0.3f), 8) });
        var head = new HBoxContainer();
        box.AddChild(head);
        head.AddChild(new Label { Text = "", CustomMinimumSize = new Vector2(330, 0) });
        head.AddChild(new Label { Text = "KEYBOARD", HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(190, 0), LabelSettings = Style(15, new Color(0.75f, 0.7f, 0.85f), 3) });
        head.AddChild(new Label { Text = "GAMEPAD", HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(190, 0), LabelSettings = Style(15, new Color(0.75f, 0.7f, 0.85f), 3) });
        for (int i = 0; i < Count; i++)
        {
            var row = new HBoxContainer();
            box.AddChild(row);
            var n = new Label { LabelSettings = Style(17, Colors.White, 4), CustomMinimumSize = new Vector2(330, 0) };
            var k = new Label { LabelSettings = Style(17, Colors.White, 4), HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(190, 0) };
            var p = new Label { LabelSettings = Style(17, Colors.White, 4), HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(190, 0) };
            row.AddChild(n);
            row.AddChild(k);
            row.AddChild(p);
            _rows.Add((n, k, p));
        }
        _note = new Label { LabelSettings = Style(14, new Color(0.8f, 0.78f, 0.9f), 3), HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(_note);
        Refresh();
        Sfx.Play("pop", 0.9f);
    }

    private static LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline,
        OutlineColor = new Color(0.08f, 0.04f, 0.1f),
    };

    private void Refresh()
    {
        Color gold = new(1f, 0.85f, 0.3f), red = new(1f, 0.45f, 0.4f);
        for (int i = 0; i < _rows.Count; i++)
        {
            (Label n, Label k, Label p) = _rows[i];
            bool sel = i == _cursor;
            if (i < Bindings.Listed.Length)
            {
                (GameAction a, string name) = Bindings.Listed[i];
                n.Text = (sel ? "> " : "  ") + name;
                k.Text = sel && _listening && _column == 0 ? "PRESS A KEY..." : Bindings.KeyName(Bindings.KeysFor(a));
                p.Text = sel && _listening && _column == 1 ? "PRESS A BUTTON..." : Bindings.ButtonName(Bindings.ButtonsFor(a));
            }
            else
            {
                n.Text = (sel ? "> " : "  ") + (i == ResetRow ? "RESET DEFAULTS" : "BACK");
                k.Text = p.Text = "";
            }
            n.LabelSettings.FontColor = sel ? gold : Colors.White;
            k.LabelSettings.FontColor = sel && _column == 0 && i < ResetRow ? (_listening ? red : gold) : Colors.White;
            p.LabelSettings.FontColor = sel && _column == 1 && i < ResetRow ? (_listening ? red : gold) : Colors.White;
        }
        _note.Text = _listening
            ? (_column == 0 ? "Press the new key.  ESC cancels." : "Press the new button on any pad.  ESC cancels.")
            : "UP/DOWN  ACTION     LEFT/RIGHT  KEYBOARD OR PAD     ATTACK  REBIND     BACK  DONE";
    }

    public override void _Input(InputEvent e)
    {
        if (!_listening || Time.GetTicksMsec() < _listenFrom) return;
        GameAction action = Bindings.Listed[_cursor].action;
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            Key k = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            // Esc cancels, except when Esc is what the pause action wants.
            if (k == Key.Escape && action != GameAction.Pause) { StopListening(false); }
            else if (_column == 0) { Bindings.BindKey(action, k); StopListening(true); }
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventJoypadButton { Pressed: true } pad && _column == 1)
        {
            Bindings.BindButton(action, pad.ButtonIndex);
            StopListening(true);
            GetViewport().SetInputAsHandled();
        }
    }

    private void StopListening(bool bound)
    {
        _listening = false;
        Sfx.Play(bound ? "ding" : "clack", bound ? 1.2f : 0.9f);
        // Whatever was just pressed must not also move the cursor.
        foreach (MapPad p in _pads) p.Swallow();
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_listening) { foreach (MapPad p in _pads) p.Swallow(); return; }
        foreach (MapPad pad in _pads)
        {
            MapPad.Press press = pad.Poll();
            if (press == MapPad.Press.None) continue;
            switch (press)
            {
                case MapPad.Press.Up: _cursor = (_cursor + Count - 1) % Count; Sfx.Play("blip", 1.1f); break;
                case MapPad.Press.Down: _cursor = (_cursor + 1) % Count; Sfx.Play("blip"); break;
                case MapPad.Press.Left: _column = 0; Sfx.Play("blip", 0.9f); break;
                case MapPad.Press.Right: _column = 1; Sfx.Play("blip", 1.2f); break;
                case MapPad.Press.Enter:
                    if (_cursor == BackRow) { Close(); return; }
                    if (_cursor == ResetRow) { Bindings.ResetDefaults(); Sfx.Play("tada", 1.1f, -6f); break; }
                    _listening = true;
                    _listenFrom = Time.GetTicksMsec() + 120;        // let the press that started it pass
                    Sfx.Play("pop", 1.2f);
                    break;
                case MapPad.Press.Back: Close(); return;
            }
            Refresh();
            return;
        }
    }

    private void Close()
    {
        Sfx.Play("clack");
        Closed?.Invoke();
        QueueFree();
    }
}
